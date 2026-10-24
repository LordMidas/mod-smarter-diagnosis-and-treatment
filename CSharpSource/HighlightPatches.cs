using System;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    // One patch class per vanilla method: Harmony does not define the relative order of several
    // patches on the same method, so everything that must run in sequence is called from here.

    /// <summary>Rewrites the percentage labels of the possible-diagnoses list.</summary>
    internal static class DiagnosisProbabilityLabels
    {
        public static void Apply(DiagnosisPanelController panel, Entity patient)
        {
            if (!ModConfig.Highlights.RoundedProbabilityLabels)
                return;

            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(patient);
            SegmentController segment = PatientUi.GetSegment(panel.m_possibleDiagnosesSegmentStatic);
            if (medicalCondition == null || segment == null)
                return;

            for (int i = 0; i < medicalCondition.m_possibleDiagnoses.Count; i++)
            {
                if (!segment.IsInRange(i))
                    continue;

                SegmentItemPercentageController item = segment.GetItemComponent<SegmentItemPercentageController>(i);
                if (item == null || item.m_textPercentage == null)
                    continue;

                UnityEngine.UI.Text percentageText = item.m_textPercentage.GetComponent<UnityEngine.UI.Text>();
                if (percentageText == null || string.IsNullOrEmpty(percentageText.text))
                    continue;

                percentageText.text = DiagnosisProbabilityLabel.Format(
                    medicalCondition.m_possibleDiagnoses[i].m_probabilityWeighted,
                    medicalCondition.m_possibleDiagnoses.Count);
            }
        }
    }

    [HarmonyPatch(typeof(DiagnosisPanelController), nameof(DiagnosisPanelController.UpdateDiagnoses))]
    public static class DiagnosisPanelController_UpdateDiagnoses_Patch
    {
        public static void Prefix(Entity patient, out Entity __state)
        {
            __state = PatientPanelContext.CurrentPatient;
            PatientPanelContext.CurrentPatient = patient;
            PatientPanelContext.LastCardPatient = patient;

            RelevantDiagnosisHighlight.RestoreHighlights();
        }

        public static void Postfix(DiagnosisPanelController __instance, Entity patient)
        {
            DiagnosisProbabilityLabels.Apply(__instance, patient);
            PossibleDiagnosisExaminationHighlight.TrackHoveredDiagnosis(__instance, patient);
            TreatmentInteractionHighlight.SetDiagnosisPanel(__instance, patient);
            RelevantDiagnosisHighlight.OnDiagnosisPanelUpdated(__instance, patient);
        }

        public static Exception Finalizer(Exception __exception, Entity __state)
        {
            PatientPanelContext.CurrentPatient = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ExaminationsPanelController), nameof(ExaminationsPanelController.UpdateExaminations))]
    public static class ExaminationsPanelController_UpdateExaminations_Patch
    {
        public static void Prefix(Entity patient, out Entity __state)
        {
            __state = PatientPanelContext.CurrentPatient;
            PatientPanelContext.CurrentPatient = patient;
            PatientPanelContext.LastCardPatient = patient;

            PossibleDiagnosisExaminationHighlight.RestoreHighlights();
            FinishedExamSymptomHighlight.BeginExaminationScan();
            RelevantDiagnosisHighlight.BeginExaminationScan();
        }

        public static void Postfix(ExaminationsPanelController __instance, Entity patient)
        {
            PossibleDiagnosisExaminationHighlight.RefreshHighlights(__instance, patient);
            FinishedExamSymptomHighlight.EndExaminationScan(__instance, patient);
            RelevantDiagnosisHighlight.EndExaminationScan(patient);
        }

        public static Exception Finalizer(Exception __exception, Entity __state)
        {
            PatientPanelContext.CurrentPatient = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ExaminationsPanelController), nameof(ExaminationsPanelController.CheckTooltip))]
    public static class ExaminationsPanelController_CheckTooltip_Patch
    {
        public static void Prefix(
            ExaminationsPanelController __instance,
            GameObject examinationType,
            GameDBExamination examination,
            int index)
        {
            FinishedExamSymptomHighlight.TrackHoveredFinishedExamination(
                __instance,
                examinationType,
                examination,
                index);
            RelevantDiagnosisHighlight.TrackHoveredExamination(examinationType, examination, index);
        }
    }

    [HarmonyPatch(typeof(ExaminationsPanelController), nameof(ExaminationsPanelController.UnavailableTooltip))]
    public static class ExaminationsPanelController_UnavailableTooltip_Patch
    {
        public static void Prefix(GameObject examinationType, GameDBExamination examination, int index)
        {
            RelevantDiagnosisHighlight.TrackHoveredExamination(examinationType, examination, index);
        }
    }

    [HarmonyPatch(typeof(SymptomsPanelController), nameof(SymptomsPanelController.UpdateSymptoms))]
    public static class SymptomsPanelController_UpdateSymptoms_Patch
    {
        public static void Prefix(MedicalCondition medicalCondition, out Entity __state)
        {
            __state = PatientPanelContext.CurrentPatient;

            // The symptoms panel does not receive the patient, so only trust the last card patient
            // when it owns this medical condition.
            Entity lastPatient = PatientPanelContext.LastCardPatient;
            PatientPanelContext.CurrentPatient =
                medicalCondition != null && PatientUi.GetMedicalCondition(lastPatient) == medicalCondition
                    ? lastPatient
                    : null;
        }

        public static void Postfix(SymptomsPanelController __instance, MedicalCondition medicalCondition)
        {
            FinishedExamSymptomHighlight.TrackHoveredSymptom(__instance, medicalCondition);
            TreatmentInteractionHighlight.SetSymptomsPanel(__instance, medicalCondition);
        }

        public static Exception Finalizer(Exception __exception, Entity __state)
        {
            PatientPanelContext.CurrentPatient = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(TreatmentPanelController), nameof(TreatmentPanelController.UpdateTreatments))]
    public static class TreatmentPanelController_UpdateTreatments_Highlight
    {
        public static void Prefix()
        {
            TreatmentInteractionHighlight.BeginTreatmentUpdate();
        }

        public static void Postfix(TreatmentPanelController __instance, Entity patient)
        {
            TreatmentInteractionHighlight.UpdateHighlights(__instance, patient);
        }
    }

    [HarmonyPatch(typeof(TreatmentPanelController), nameof(TreatmentPanelController.CheckTooltip))]
    public static class TreatmentPanelController_CheckTooltip_Highlight
    {
        public static void Prefix(GameObject treatmentObject, GameDBTreatment treatment, int index)
        {
            TreatmentInteractionHighlight.TrackHoveredTreatment(treatmentObject, treatment, index);
        }
    }

    [HarmonyPatch(typeof(TreatmentPanelController), nameof(TreatmentPanelController.ShowUnavailableTreatmentTooltip))]
    public static class TreatmentPanelController_ShowUnavailableTreatmentTooltip_Highlight
    {
        public static void Prefix(GameObject treatmentType, GameDBTreatment treatment, int index)
        {
            TreatmentInteractionHighlight.TrackHoveredTreatment(treatmentType, treatment, index);
        }
    }

    [HarmonyPatch(typeof(TooltipBase), nameof(TooltipBase.Update))]
    public static class TooltipBase_Update_DiagnosisHighlight
    {
        public static void Postfix(TooltipBase __instance)
        {
            if (__instance is TooltipExamination && !__instance.gameObject.activeSelf)
                RelevantDiagnosisHighlight.ClearHoveredExamination();
        }
    }
}
