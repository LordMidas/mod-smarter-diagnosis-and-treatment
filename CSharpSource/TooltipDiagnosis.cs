using HarmonyLib;
using Lopital;
using GLib;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Postfix on TooltipDiagnosis.UpdateData.
    ///
    /// After vanilla populates the diagnosis tooltip, we append a colour-coded list of
    /// examinations that can reveal this diagnosis's symptoms, showing which have
    /// been done on the currently-viewed patient and whether they found anything.
    ///
    /// Colour key:
    ///   Green  – exam was completed and revealed at least one symptom
    ///   Grey   – exam was completed but found nothing
    ///   White  – exam has not been performed yet
    /// </summary>
    [HarmonyPatch(typeof(TooltipDiagnosis), nameof(TooltipDiagnosis.UpdateData))]
    public static class TooltipDiagnosis_UpdateData
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Tooltips.DiagnosisDetails, nameof(TooltipDiagnosis_UpdateData));

        // Green: exam revealed a symptom
        private const string COLOR_DONE_POSITIVE = "<color=#88dd88>";
        // Grey: exam done, nothing found
        private const string COLOR_DONE_NEUTRAL  = "<color=#888888>";
        // Section header colour (muted olive, matches the user's existing choice)
        private const string COLOR_HEADER        = "<color=#aaddff>";
        private const string COLOR_END           = "</color>";    

        public static void Prefix(GameDBMedicalCondition diagnosis, ref string textProbability)
        {
            if (string.IsNullOrEmpty(textProbability) || textProbability.IndexOf('?') >= 0)
                return;

            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(PatientPanelContext.CurrentPatient);
            if (medicalCondition == null)
                return;

            List<PossibleDiagnosis> possibleDiagnoses = medicalCondition.m_possibleDiagnoses;
            for (int i = 0; i < possibleDiagnoses.Count; i++)
            {
                PossibleDiagnosis possibleDiagnosis = possibleDiagnoses[i];
                if (possibleDiagnosis.m_diagnosis.Entry != diagnosis)
                    continue;

                textProbability = DiagnosisProbabilityLabel.Format(possibleDiagnosis.m_probabilityWeighted, possibleDiagnoses.Count);
                return;
            }
        }

        public static void Postfix(
            TooltipDiagnosis __instance,
            GameDBMedicalCondition diagnosis,
            string textProbability,
            int insuranceIconIndex,
            int insuranceCoverPercentage)
        {
            // --- Guard: need a tracked patient ---
            Entity patient = PatientPanelContext.CurrentPatient;
            if (patient == null)
                return;

            if (diagnosis == null || diagnosis.Symptoms == null || diagnosis.Symptoms.Length == 0)
                return;

            var relevantExaminations = new List<GameDBExamination>();
            var relevantExaminationSet = new HashSet<GameDBExamination>();
            foreach (GameDBSymptomRules symptomRule in diagnosis.Symptoms)
            {
                GameDBSymptom symptom = symptomRule?.GameDBSymptomRef?.Entry;
                if (symptom == null || symptom.Examinations == null)
                    continue;

                foreach (DatabaseEntryRef<GameDBExamination> examinationRef in symptom.Examinations)
                {
                    // Lab tests are listed by the sampling examination the player orders.
                    GameDBExamination relevantExamination = ExaminationEquivalence.ToSampling(examinationRef?.Entry);
                    if (relevantExamination == null)
                        continue;

                    if (relevantExaminationSet.Add(relevantExamination))
                        relevantExaminations.Add(relevantExamination);
                }
            }

            if (relevantExaminations.Count == 0)
                return;

            // --- Build a lookup: exam → (done, discoveredSymptoms) ---
            // from the patient's finished-examination list.
            ProcedureComponent procedureComponent = patient.GetComponent<ProcedureComponent>();
            if (procedureComponent == null)
                return;

            var finishedExams = procedureComponent.m_state.m_procedureQueue.m_finishedExaminations;

            // Map exam → discoveredSymptoms count (absent means not done yet)
            var finishedLookup = new Dictionary<GameDBExamination, int>();
            if (finishedExams != null)
            {
                foreach (ExaminationState es in finishedExams)
                {
                    // A finished sampling examination has no results until its lab test finishes,
                    // so only the testing examination counts, keyed by its sampling examination.
                    if (es.m_examination.Entry == null || ExaminationEquivalence.IsSampling(es.m_examination.Entry))
                        continue;
                    GameDBExamination id = ExaminationEquivalence.ToSampling(es.m_examination.Entry);
                    // If the same exam appears twice (shouldn't happen, but be safe), take max
                    if (!finishedLookup.ContainsKey(id) || es.m_discoveredSymptoms > finishedLookup[id])
                        finishedLookup[id] = es.m_discoveredSymptoms;
                }
            }

            // --- Build the examination list ---
            var examsBuilder = new StringBuilder();
            examsBuilder.Append("\n\n");
            examsBuilder.Append(COLOR_HEADER);        
            examsBuilder.Append(StringTable.GetInstance().GetLocalizedText("MOD_LMSDT_RELEVANT_EXAMINATIONS", new string[0]));
            examsBuilder.Append(COLOR_END);

            foreach (GameDBExamination relevantExamination in relevantExaminations)
            {
                string examName = StringTable.GetInstance().GetLocalizedText(relevantExamination);

                examsBuilder.Append("\n");

                if (finishedLookup.TryGetValue(relevantExamination, out int discovered))
                {
                    if (discovered > 0)
                    {
                        // Green – revealed something
                        examsBuilder.Append(COLOR_DONE_POSITIVE);
                        examsBuilder.Append(" ✓ ");
                        examsBuilder.Append(examName);
                        examsBuilder.Append(COLOR_END);
                    }
                    else
                    {
                        // Grey – done but nothing found
                        examsBuilder.Append(COLOR_DONE_NEUTRAL);
                        examsBuilder.Append(" - ");
                        examsBuilder.Append(examName);
                        examsBuilder.Append(COLOR_END);
                    }
                }
                else
                {
                    // White (no color tag) – not done yet
                    examsBuilder.Append(" - ");
                    examsBuilder.Append(examName);
                }
            }

            // --- Append to whatever vanilla already set in m_textMain ---
            Text textComponent = __instance.m_textMain.GetComponent<Text>();
            textComponent.text += examsBuilder.ToString();

            // --- Resize the tooltip to fit the new content ---
            // Vanilla uses +185f for the diagnosis tooltip (it has more fixed UI widgets
            // than the examination tooltip which uses +100f).
            RectTransform mainRect    = __instance.m_textMain.GetComponent<RectTransform>();
            RectTransform tooltipRect = __instance.gameObject.GetComponent<RectTransform>();

            if (textComponent.preferredHeight >= 30f)
            {
                mainRect.sizeDelta    = new Vector2(mainRect.sizeDelta.x,    textComponent.preferredHeight);
                tooltipRect.sizeDelta = new Vector2(tooltipRect.sizeDelta.x, textComponent.preferredHeight + 185f);
            }
        }
    }
}
