using System;
using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    [HarmonyPatch(typeof(ProcedureComponent), nameof(ProcedureComponent.UpdateAllExaminationsForMedicalCondition))]
    // Vanilla keeps the availability map in Procedure.Priority order, and NPC doctors take the first selectable
    // entry. Here the map is re-sorted by relevance score (stable, so equal scores keep the vanilla order).
    public static class ProcedureComponent_UpdateAllExaminationsForMedicalCondition
    {
        // The component currently inside UpdateAllExaminationsForMedicalCondition, so the
        // GetAllExaminationsForMedicalCondition postfix knows it is vanilla's own refresh call.
        [ThreadStatic]
        internal static ProcedureComponent UpdatingComponent;

        public static void Prefix(ProcedureComponent __instance, out ProcedureComponent __state)
        {
            __state = UpdatingComponent;
            UpdatingComponent = __instance;
        }

        public static Exception Finalizer(Exception __exception, ProcedureComponent __state)
        {
            UpdatingComponent = __state;
            return __exception;
        }

        public static void Postfix(ProcedureComponent __instance, MedicalCondition medicalCondition)
        {
            if (!ModConfig.ExamList.SortEnabled)
                return;

            FakeMap<GameDBExamination, ProcedureSceneAvailability> availability =
                __instance.m_examinationAvailability;
            List<GameDBExamination> examinations = availability.m_keys;
            List<ProcedureSceneAvailability> statuses = availability.m_values;

            float[] scores = new float[examinations.Count];
            for (int index = 0; index < scores.Length; index++)
                scores[index] = ExaminationRelevance.GetRelevanceScore(examinations[index], medicalCondition);

            List<int> sortedIndices = new List<int>(examinations.Count);
            for (int index = 0; index < examinations.Count; index++)
                sortedIndices.Add(index);

            // Sort indices rather than keys so each exam stays paired with its availability status.
            sortedIndices.Sort((left, right) =>
            {
                int scoreComparison = scores[right].CompareTo(scores[left]);
                return scoreComparison != 0 ? scoreComparison : left.CompareTo(right);
            });

            List<GameDBExamination> sortedExaminations =
                new List<GameDBExamination>(examinations.Count);
            List<ProcedureSceneAvailability> sortedStatuses =
                new List<ProcedureSceneAvailability>(statuses.Count);
            foreach (int index in sortedIndices)
            {
                sortedExaminations.Add(examinations[index]);
                sortedStatuses.Add(statuses[index]);
            }

            examinations.Clear();
            examinations.AddRange(sortedExaminations);
            statuses.Clear();
            statuses.AddRange(sortedStatuses);
        }
    }

    [HarmonyPatch(typeof(ProcedureComponent), nameof(ProcedureComponent.GetAllExaminationsForMedicalCondition))]
    // Vanilla lists every exam named by any possible diagnosis, including ones that can no longer reveal
    // anything (all their symptoms already visible). The Postfix keeps only exams that still can.
    public static class ProcedureComponent_GetAllExaminationsForMedicalCondition
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamList.FilterEnabled, nameof(ProcedureComponent_GetAllExaminationsForMedicalCondition));

        public static void Postfix(
            ProcedureComponent __instance,
            MedicalCondition medicalCondition,
            bool diagnosed,
            bool forceAll,
            ref List<GameDBExamination> __result)
        {
            if (!forceAll &&
                !__instance.m_entity.GetComponent<HospitalizationComponent>().m_state.m_dead)
                __result = FilterExaminations(__instance, medicalCondition, diagnosed, __result);

            ClearIfStale(__instance, __result);
        }

        private static List<GameDBExamination> FilterExaminations(
            ProcedureComponent __instance,
            MedicalCondition medicalCondition,
            bool diagnosed,
            List<GameDBExamination> allExaminations)
        {
            List<PossibleDiagnosis> possibleDiagnoses = medicalCondition.m_possibleDiagnoses;
            HashSet<GameDBSymptom> seenSymptoms = new HashSet<GameDBSymptom>();
            List<GameDBSymptom> potentialSymptoms = new List<GameDBSymptom>();
            foreach (PossibleDiagnosis possibleDiagnosis in possibleDiagnoses)
            {
                GameDBMedicalCondition diagnosis = possibleDiagnosis.m_diagnosis.Entry;
                if (diagnosis == null || diagnosis.Symptoms == null)
                    continue;

                foreach (GameDBSymptomRules symptomRules in diagnosis.Symptoms)
                {
                    GameDBSymptom symptom = symptomRules.GameDBSymptomRef == null
                        ? null
                        : symptomRules.GameDBSymptomRef.Entry;
                    if (symptom != null && seenSymptoms.Add(symptom))
                        potentialSymptoms.Add(symptom);
                }
            }

            HashSet<GameDBExamination> hiddenSymptomExaminationSet =
                new HashSet<GameDBExamination>();
            List<GameDBExamination> examinationsForHiddenSymptoms =
                new List<GameDBExamination>();
            foreach (GameDBSymptom symptom in potentialSymptoms)
            {
                if (medicalCondition.HasVisibleSymptom(symptom) || symptom.Examinations == null)
                    continue;

                foreach (DatabaseEntryRef<GameDBExamination> examinationRef in symptom.Examinations)
                {
                    GameDBExamination examination = examinationRef.Entry;
                    if (examination == null)
                        continue;

                    GameDBExamination samplingExamination =
                        ExaminationEquivalence.ToSampling(examination);
                    if (hiddenSymptomExaminationSet.Add(samplingExamination))
                        examinationsForHiddenSymptoms.Add(samplingExamination);
                }
            }

            HashSet<GameDBExamination> surgeryComplicationExaminations =
                GetSurgeryComplicationExaminations(__instance, diagnosed);
            List<GameDBExamination> filteredExaminations = new List<GameDBExamination>();
            HashSet<GameDBExamination> filteredSet = new HashSet<GameDBExamination>();

            foreach (GameDBExamination examination in allExaminations)
            {
                if (examination == null)
                    continue;

                if (examination == ModDb.Interview ||
                    examination == ModDb.DifferentialDiagnosis ||
                    surgeryComplicationExaminations.Contains(examination))
                {
                    AddIfMissing(filteredExaminations, filteredSet, examination);
                    continue;
                }

                GameDBExamination samplingExamination =
                    ExaminationEquivalence.ToSampling(examination);
                if (hiddenSymptomExaminationSet.Contains(samplingExamination) &&
                    IsNotAlreadyQueued(__instance, samplingExamination, diagnosed))
                    AddIfMissing(filteredExaminations, filteredSet, samplingExamination);
            }

            foreach (GameDBExamination examination in examinationsForHiddenSymptoms)
            {
                if (examination.LabTestingExaminationRef != null &&
                    IsNotAlreadyQueued(__instance, examination, diagnosed))
                    AddIfMissing(filteredExaminations, filteredSet, examination);
            }

            return filteredExaminations;
        }

        // Vanilla UpdateAll only compares counts, so a same-size but different exam set would keep
        // stale keys. Clearing here (vanilla UpdateAll's first call is GetAll) forces a full rebuild.
        private static void ClearIfStale(ProcedureComponent instance, List<GameDBExamination> result)
        {
            if (instance != ProcedureComponent_UpdateAllExaminationsForMedicalCondition.UpdatingComponent)
                return;

            FakeMap<GameDBExamination, ProcedureSceneAvailability> availability =
                instance.m_examinationAvailability;
            if (SameSet(availability.m_keys, result))
                return;

            availability.Clear();
            instance.m_examinationAvailabilityUIDirty = true;
        }

        private static bool SameSet(List<GameDBExamination> cached, List<GameDBExamination> expected)
        {
            return cached.Count == expected.Count && cached.TrueForAll(expected.Contains);
        }

        private static HashSet<GameDBExamination> GetSurgeryComplicationExaminations(
            ProcedureComponent procedureComponent,
            bool diagnosed)
        {
            HashSet<GameDBExamination> examinations = new HashSet<GameDBExamination>();
            foreach (TreatmentState finishedTreatment in
                     procedureComponent.m_state.m_procedureQueue.m_finishedTreatmentStates)
            {
                GameDBTreatment treatment = finishedTreatment.m_treatment.Entry;
                if (treatment == null ||
                    treatment.TreatmentType != TreatmentType.SURGERY ||
                    treatment.Procedure.Complications == null)
                    continue;

                foreach (GameDBProcedureComplication complication in treatment.Procedure.Complications)
                {
                    GameDBSymptom symptom = complication.SymptomRef.Entry;
                    if (symptom == null || symptom.Examinations == null)
                        continue;

                    foreach (DatabaseEntryRef<GameDBExamination> examinationRef in symptom.Examinations)
                    {
                        GameDBExamination examination = examinationRef.Entry;
                        if (examination != null &&
                            ((examination.ExaminationType != ExaminationType.INTERVIEW &&
                              examination.ExaminationType != ExaminationType.OBSERVATION) ||
                             !diagnosed))
                            examinations.Add(examination);
                    }
                }
            }

            return examinations;
        }

        private static bool IsNotAlreadyQueued(
            ProcedureComponent procedureComponent,
            GameDBExamination examination,
            bool diagnosed)
        {
            ProcedureQueue queue = procedureComponent.m_state.m_procedureQueue;
            return !queue.HasFinishedExamination(examination) &&
                   !queue.HasPlannedExamination(examination) &&
                   queue.m_activeExamination != examination &&
                   (examination.ExaminationType != ExaminationType.OBSERVATION || !diagnosed);
        }

        private static void AddIfMissing(
            List<GameDBExamination> examinations,
            HashSet<GameDBExamination> examinationSet,
            GameDBExamination examination)
        {
            if (examinationSet.Add(examination))
                examinations.Add(examination);
        }
    }
}
