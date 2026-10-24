using HarmonyLib;
using Lopital;
using GLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    internal static class DiagnosisPatientContext
    {
        [ThreadStatic]
        internal static Entity CurrentPatient;
    }

    [HarmonyPatch(typeof(MedicalCondition), nameof(MedicalCondition.UpdatePossibleDiagnoses))]
    public static class MedicalCondition_UpdatePossibleDiagnosesPatientContext
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Scoring.Enabled, nameof(MedicalCondition_UpdatePossibleDiagnosesPatientContext));

        private static void Prefix(Entity patient, out Entity __state)
        {
            __state = DiagnosisPatientContext.CurrentPatient;
            DiagnosisPatientContext.CurrentPatient = patient;
        }

        private static Exception Finalizer(Exception __exception, Entity __state)
        {
            DiagnosisPatientContext.CurrentPatient = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MedicalCondition), nameof(MedicalCondition.GetPossibleDiagnoses))]
    public class MedicalCondition_GetPossibleDiagnoses
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Scoring.Enabled, nameof(MedicalCondition_GetPossibleDiagnoses));

        private enum SymptomState
        {
            Unknown,
            Present,
            Absent
        }

        private static bool Prefix(
            MedicalCondition __instance,
            List<PossibleDiagnosis> possibleDiagnoses,
            bool sortPossibleDiagnoses,
            GameDBGender gender)
        {
            Entity patient = DiagnosisPatientContext.CurrentPatient;
            if (patient == null)
                return true;

            GetPossibleDiagnoses_Enhanced3(patient, __instance, possibleDiagnoses, sortPossibleDiagnoses, gender);
            return false;
        }

        // Interview-style exams only reveal complained-about symptoms; a symptom is complained about
        // for certain only when the patient complains about it and it carries no shame.
        private static bool CanInterviewReveal(GameDBSymptom symptom) => symptom.PatientComplains && symptom.ShameLevel == 0;

        private static bool HasTestedForSymptom(HashSet<GameDBExamination> finishedExams, GameDBSymptom symptom)
        {
            if (symptom.Examinations == null)
                return false;

            foreach (DatabaseEntryRef<GameDBExamination> examinationRef in symptom.Examinations)
            {
                GameDBExamination examination = examinationRef.Entry;
                if (examination == null || !finishedExams.Contains(examination))
                    continue;

                if (ModDb.RevealsOnlyComplainedSymptoms(examination) && !CanInterviewReveal(symptom))
                    continue;

                return true;
            }

            return false;
        }

        private static SymptomState GetSymptomState(
            HashSet<GameDBExamination> finishedExams,
            MedicalCondition medicalCondition,
            GameDBSymptom symptom)
        {
            if (medicalCondition.HasVisibleSymptom(symptom))
                return SymptomState.Present;

            return HasTestedForSymptom(finishedExams, symptom)
                ? SymptomState.Absent
                : SymptomState.Unknown;
        }

        // Third version of enhanced logic. Uses log-evidence scoring for negative results.
        // Vanilla scores each diagnosis as the average over its symptom rules of P (symptom visible) or 100 - P
        // (not visible), so a symptom nobody has tested for counts as evidence against it. Here only tested
        // symptoms count: a visible one adds log(P), a tested-but-absent one adds a down-weighted log(1 - P), and
        // untested ones add nothing. Diagnoses are then normalised relative to the best one. A visible main
        // symptom still short-circuits to a single 100% diagnosis, as in vanilla.
        private static void GetPossibleDiagnoses_Enhanced3(Entity patient, MedicalCondition medicalCondition, List<PossibleDiagnosis> possibleDiagnoses, bool sortResults, GameDBGender patientGender)
        {
            possibleDiagnoses.Clear();
            HashSet<GameDBExamination> finishedExams = new HashSet<GameDBExamination>();
            foreach (ExaminationState examinationState in patient.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_finishedExaminations)
            {
                GameDBExamination finishedExamination = examinationState.m_examination.Entry;
                if (finishedExamination != null)
                    finishedExams.Add(finishedExamination);
            }

            List<double> logLikelihoods = new List<double>();
            GameDBMedicalCondition mainSymptomDiagnosis = null;
            bool noFiltering = Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_NO_DIAGNOSIS_FILTERING").Value > 0;
            if (noFiltering)
                sortResults = false;

            foreach (GameDBMedicalCondition condition in Database.Instance.GetEntries<GameDBMedicalCondition>())
            {
                if (condition.Disabled)
                    continue;

                if (!noFiltering &&
                    ((medicalCondition.m_wrongDiagnoses != null && medicalCondition.m_wrongDiagnoses.Contains(condition)) ||
                     (medicalCondition.m_diagnosesEliminatedBySpecialExaminations != null && medicalCondition.m_diagnosesEliminatedBySpecialExaminations.Contains(condition))))
                    continue;

                if (condition.MatchingGenderRef != null && patientGender != condition.MatchingGenderRef.Entry)
                    continue;

                bool matchesVisibleSymptoms = true;
                if (!noFiltering)
                {
                    foreach (Symptom patientSymptom in medicalCondition.m_symptoms)
                    {
                        bool isVisibleSymptom = !patientSymptom.m_fromCollapse &&
                                                 !patientSymptom.m_fromSurgery &&
                                                 !patientSymptom.m_hidden &&
                                                 patientSymptom.m_spawned;
                        if (!isVisibleSymptom || condition.HasSymptom(patientSymptom.m_symptom.Entry))
                            continue;

                        matchesVisibleSymptoms = false;
                        break;
                    }
                }

                if (!matchesVisibleSymptoms && !noFiltering)
                    continue;

                bool hasVisibleSymptom = false;
                bool hasVisibleMainSymptom = false;
                bool eliminated = false;
                double logLikelihood = 0d;

                foreach (GameDBSymptomRules symptomRule in condition.Symptoms)
                {
                    GameDBSymptom symptom = symptomRule.GameDBSymptomRef.Entry;
                    SymptomState state = GetSymptomState(finishedExams, medicalCondition, symptom);
                    float probability = Mathf.Clamp01(symptomRule.ProbabilityPercent * 0.01f);

                    if (state == SymptomState.Present)
                    {
                        hasVisibleSymptom = true;
                        if (probability <= 0f)
                        {
                            eliminated = true;
                            break;
                        }

                        logLikelihood += Math.Log(probability);
                        if (symptom.IsMainSymptom)
                            hasVisibleMainSymptom = true;
                    }
                    else if (state == SymptomState.Absent)
                    {
                        if (symptom.IsMainSymptom)
                        {
                            eliminated = true;
                            break;
                        }

                        // Delayed-onset symptoms (DayOfFirstOccurence > 0) may spawn hidden after the exam ran, so their
                        // absence is never conclusive. No vanilla/DLC rule uses this today; guards modded data.
                        bool absenceConclusive = symptomRule.DayOfFirstOccurence == 0;
                        if (probability >= 1f && absenceConclusive)
                        {
                            eliminated = true;
                            break;
                        }

                        double absentProbability = absenceConclusive ? probability : Math.Min(probability, ModConfig.Scoring.MaxDelayedAbsentProbability);
                        logLikelihood += ModConfig.Scoring.NegativeEvidenceWeight * Math.Log(1d - absentProbability);
                    }
                }

                if (eliminated || !hasVisibleSymptom)
                    continue;

                possibleDiagnoses.Add(new PossibleDiagnosis(condition, 0f));
                logLikelihoods.Add(logLikelihood);

                if (hasVisibleMainSymptom && !noFiltering && mainSymptomDiagnosis == null)
                {
                    mainSymptomDiagnosis = condition;
                    break;
                }
            }

            if (mainSymptomDiagnosis != null)
            {
                possibleDiagnoses.Clear();
                PossibleDiagnosis diagnosis = new PossibleDiagnosis(mainSymptomDiagnosis, 100f);
                diagnosis.m_probabilityWeighted = 100f;
                possibleDiagnoses.Add(diagnosis);
            }
            else if (possibleDiagnoses.Count > 0)
            {
                double maxLogLikelihood = logLikelihoods[0];
                for (int i = 1; i < logLikelihoods.Count; i++)
                {
                    if (logLikelihoods[i] > maxLogLikelihood)
                        maxLogLikelihood = logLikelihoods[i];
                }

                double totalProbability = 0d;
                for (int i = 0; i < possibleDiagnoses.Count; i++)
                {
                    double relativeLikelihood = Math.Exp(logLikelihoods[i] - maxLogLikelihood);
                    double rawProbability = relativeLikelihood * 99.999d;
                    PossibleDiagnosis diagnosis = possibleDiagnoses[i];
                    diagnosis.m_probability = rawProbability < float.Epsilon
                        ? float.Epsilon
                        : (float)rawProbability;
                    totalProbability += diagnosis.m_probability;
                }

                foreach (PossibleDiagnosis diagnosis in possibleDiagnoses)
                {
                    double weightedProbability = 100d * diagnosis.m_probability / totalProbability;
                    diagnosis.m_probabilityWeighted = weightedProbability < float.Epsilon
                        ? float.Epsilon
                        : (float)weightedProbability;
                }
            }

            if (sortResults)
            {
                possibleDiagnoses.Sort((first, second) => second.m_probability.CompareTo(first.m_probability));
            }
            else
            {
                possibleDiagnoses.Sort((first, second) =>
                    StringTable.GetInstance().GetLocalizedText(first.m_diagnosis.Entry)
                        .CompareTo(StringTable.GetInstance().GetLocalizedText(second.m_diagnosis.Entry))
                );
            }
        }
    }

    public static class DiagnosisProbabilityLabel
    {
        public static string Format(float percentage, int diagnosisCount)
        {
            if (diagnosisCount <= 1)
                return "100%";
            if (percentage < 0.5f)
                return "<1%";
            if (percentage >= 99.5f)
                return ">99%";
            return ((int)(percentage + 0.5f)).ToString() + "%";
        }
    }

    [HarmonyPatch(typeof(MedicalCondition), nameof(MedicalCondition.IsClear), new Type[] { typeof(float), typeof(int) })]
    // Vanilla compares the raw m_probability of the top diagnosis with the threshold. Our raw value is relative to
    // the best diagnosis (always about 100 for the top one), so compare the normalised m_probabilityWeighted.
    public static class MedicalCondition_IsClearWithCertaintyThreshold
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Scoring.Enabled, nameof(MedicalCondition_IsClearWithCertaintyThreshold));

        private static bool Prefix(MedicalCondition __instance, int thresholdOfCertainty, ref bool __result)
        {
            if (__instance.m_possibleDiagnoses.Count == 0)
            {
                __result = false;
            }
            else if (__instance.IsMainSymptomUncovered() ||
                     __instance.m_possibleDiagnoses.Count == 1 ||
                     __instance.m_possibleDiagnoses[0].m_probabilityWeighted > thresholdOfCertainty)
            {
                __result = true;
            }
            else
            {
                __result = false;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MedicalCondition), nameof(MedicalCondition.Diagnose))]
    public static class MedicalCondition_Diagnose
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.NpcDiagnosis.EarlyDiagnosisEnabled, nameof(MedicalCondition_Diagnose));

        // Adjusts the logic of NPC doctors to only diagnose the leading diagnosis if it has enough positive support from symptoms.
        // Vanilla's WAIT_TO_BE_CERTAIN branch diagnoses early on the 1.5 top/runner-up ratio alone, which with our
        // scoring could fire on a single symptom. The 66.7 cutoff, the RANDOM branch and the rest mirror vanilla.
        private static bool Prefix(MedicalCondition __instance, DiagnosticApproach diagnosticApproach, float thresholdOfCertainty, Entity patient, bool forceCorrect, ref DiagnosisResult __result)
        {
            __instance.UpdatePossibleDiagnoses(patient, true);
            if (forceCorrect)
            {
                __instance.m_diagnosedMedicalCondition = __instance.m_gameDBMedicalCondition;
                __result = DiagnosisResult.DIAGNOSED;
                return false;
            }

            if (__instance.IsClear())
            {
                __instance.m_diagnosedMedicalCondition = __instance.m_gameDBMedicalCondition.Entry;
                __instance.m_correctlyDiagnosed = true;
                __instance.UpdateDiagnosesModifiers(patient, true);
                __result = DiagnosisResult.DIAGNOSED;
                return false;
            }

            if (diagnosticApproach == DiagnosticApproach.WAIT_TO_BE_CERTAIN &&
                thresholdOfCertainty < 66.7f &&
                __instance.m_possibleDiagnoses.Count > 1)
            {
                PossibleDiagnosis top = __instance.m_possibleDiagnoses[0];
                PossibleDiagnosis runnerUp = __instance.m_possibleDiagnoses[1];
                bool enoughPositiveEvidence = HasEnoughPositiveEvidence(__instance, top.m_diagnosis.Entry);
                bool enoughRelativeSupport = top.m_probabilityWeighted >= Mathf.Max(ModConfig.NpcDiagnosis.MinimumRelativeShare, thresholdOfCertainty);
                bool enoughLead = runnerUp.m_probability > 0f &&
                                  top.m_probability / runnerUp.m_probability > ModConfig.NpcDiagnosis.MinimumTopRunnerUpRatio;

                if (enoughPositiveEvidence && enoughRelativeSupport && enoughLead)
                {
                    __instance.m_diagnosedMedicalCondition = top.m_diagnosis.Entry;
                    __instance.m_correctlyDiagnosed = __instance.m_gameDBMedicalCondition == __instance.m_diagnosedMedicalCondition;
                    __instance.UpdateDiagnosesModifiers(patient, __instance.m_correctlyDiagnosed);
                    patient.LogWarning("First diagnosis selected! Correct? " + __instance.m_correctlyDiagnosed);
                    __result = DiagnosisResult.DIAGNOSED;
                    return false;
                }
            }
            else if (diagnosticApproach == DiagnosticApproach.RANDOM &&
                     __instance.m_possibleDiagnoses.Count > 1 &&
                     __instance.m_possibleDiagnoses.Count <= 6)
            {
                __instance.m_diagnosedMedicalCondition = __instance.m_possibleDiagnoses[UnityEngine.Random.Range(0, __instance.m_possibleDiagnoses.Count)].m_diagnosis.Entry;
                __instance.m_correctlyDiagnosed = __instance.m_gameDBMedicalCondition == __instance.m_diagnosedMedicalCondition;
                __instance.UpdateDiagnosesModifiers(patient, __instance.m_correctlyDiagnosed);
                if (!__instance.m_correctlyDiagnosed)
                    patient.LogWarning("WRONG DIAG!");
                __result = DiagnosisResult.DIAGNOSED;
                return false;
            }

            __result = DiagnosisResult.NONE;
            return false;
        }

        private static bool HasEnoughPositiveEvidence(MedicalCondition medicalCondition, GameDBMedicalCondition diagnosis)
        {
            int visibleCount = 0;
            foreach (GameDBSymptomRules rule in diagnosis.Symptoms)
            {
                GameDBSymptom symptom = rule.GameDBSymptomRef.Entry;
                if (medicalCondition.HasVisibleSymptom(symptom) &&
                    (symptom.IsMainSymptom || ++visibleCount >= ModConfig.NpcDiagnosis.MinimumVisibleSymptoms))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
