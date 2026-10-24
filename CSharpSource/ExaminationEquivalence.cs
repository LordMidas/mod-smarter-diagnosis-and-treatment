using System.Collections.Generic;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Lab examinations are split into a sampling examination (performed on the patient,
    /// references the lab test via LabTestingExaminationRef) and a testing examination
    /// (performed in the lab, referenced by symptoms). These helpers map between the two
    /// so both can be treated as the same examination.
    /// </summary>
    public static class ExaminationEquivalence
    {
        private static Dictionary<GameDBExamination, GameDBExamination> testingToSampling;

        public static bool IsSampling(GameDBExamination examination)
        {
            return examination != null &&
                examination.LabTestingExaminationRef != null &&
                examination.LabTestingExaminationRef.Entry != null;
        }

        public static GameDBExamination ToSampling(GameDBExamination examination)
        {
            if (examination == null || IsSampling(examination))
                return examination;

            GameDBExamination samplingExamination;
            return GetSamplingExaminationsByTestingExamination().TryGetValue(examination, out samplingExamination)
                ? samplingExamination
                : examination;
        }

        public static GameDBExamination ToTesting(GameDBExamination examination)
        {
            return IsSampling(examination)
                ? examination.LabTestingExaminationRef.Entry
                : examination;
        }

        public static bool AreEquivalent(GameDBExamination first, GameDBExamination second)
        {
            if (first == null || second == null)
                return false;

            return ToSampling(first) == ToSampling(second);
        }

        public static bool SymptomListsExamination(GameDBSymptom symptom, GameDBExamination examination)
        {
            if (symptom == null || examination == null || symptom.Examinations == null)
                return false;

            foreach (DatabaseEntryRef<GameDBExamination> examinationRef in symptom.Examinations)
            {
                if (AreEquivalent(examinationRef.Entry, examination))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<GameDBExamination, GameDBExamination>
            GetSamplingExaminationsByTestingExamination()
        {
            if (testingToSampling != null)
                return testingToSampling;

            testingToSampling = new Dictionary<GameDBExamination, GameDBExamination>();

            foreach (GameDBExamination examination in Database.Instance.GetEntries<GameDBExamination>())
            {
                if (IsSampling(examination))
                    testingToSampling[examination.LabTestingExaminationRef.Entry] = examination;
            }

            return testingToSampling;
        }
    }
}
