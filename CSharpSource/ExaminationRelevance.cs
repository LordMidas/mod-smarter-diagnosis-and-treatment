using System.Collections.Generic;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Single definition of "examination X is relevant to diagnosis Y": X reveals a symptom of Y
    /// that the patient does not show yet. Used by highlights, tooltips, exam sorting and NPC choice.
    /// </summary>
    public static class ExaminationRelevance
    {
        public static List<GameDBMedicalCondition> FindMatchingDiagnoses(
            MedicalCondition medicalCondition,
            GameDBExamination examination)
        {
            var matchingDiagnoses = new List<GameDBMedicalCondition>();
            if (medicalCondition == null || examination == null || medicalCondition.m_possibleDiagnoses == null)
                return matchingDiagnoses;

            foreach (PossibleDiagnosis possibleDiagnosis in medicalCondition.m_possibleDiagnoses)
            {
                GameDBMedicalCondition condition = possibleDiagnosis.m_diagnosis.Entry;
                if (HasUnrevealedSymptomRevealedBy(medicalCondition, condition, examination))
                    matchingDiagnoses.Add(condition);
            }

            return matchingDiagnoses;
        }

        public static bool HasUnrevealedSymptomRevealedBy(
            MedicalCondition medicalCondition,
            GameDBMedicalCondition diagnosis,
            GameDBExamination examination)
        {
            if (medicalCondition == null ||
                diagnosis == null ||
                diagnosis.Symptoms == null ||
                examination == null)
            {
                return false;
            }

            foreach (GameDBSymptomRules symptomRule in diagnosis.Symptoms)
            {
                GameDBSymptom symptom = symptomRule?.GameDBSymptomRef?.Entry;
                if (symptom == null || medicalCondition.HasVisibleSymptom(symptom) ||
                    symptom.Examinations == null)
                {
                    continue;
                }

                if (ExaminationEquivalence.SymptomListsExamination(symptom, examination))
                    return true;
            }

            return false;
        }

        public static float GetRelevanceScore(GameDBExamination examination, MedicalCondition medicalCondition)
        {
            List<PossibleDiagnosis> possibleDiagnoses = medicalCondition.m_possibleDiagnoses;
            if (examination == ModDb.DifferentialDiagnosis)
            {
                int maxDiagnoses = ModConfig.ExamList.DifferentialDiagnosisMaxDiagnoses;
                if (possibleDiagnoses.Count >= maxDiagnoses)
                    return 0f;

                // Encourage Differential Diagnosis as the remaining possibilities narrow. The score is
                // positive whenever the exam is offered, so the base count stays two above the limit.
                return (maxDiagnoses + 2 - possibleDiagnoses.Count) * ModConfig.ExamList.DifferentialDiagnosisScorePerStep;
            }

            float score = 0f;
            foreach (PossibleDiagnosis possibleDiagnosis in possibleDiagnoses)
            {
                if (HasUnrevealedSymptomRevealedBy(medicalCondition, possibleDiagnosis.m_diagnosis.Entry, examination))
                    score += possibleDiagnosis.m_probabilityWeighted;
            }

            return score;
        }
    }
}
