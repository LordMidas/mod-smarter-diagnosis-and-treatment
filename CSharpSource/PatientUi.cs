using System;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    // Scoped "which patient card is being drawn" context. Set only while a patient-card panel
    // updates, so tooltips shown elsewhere (e.g. the encyclopedia) see null.
    internal static class PatientPanelContext
    {
        [ThreadStatic] internal static Entity CurrentPatient;
        internal static Entity LastCardPatient; // last patient whose diagnosis/exam panel updated
    }

    internal static class PatientUi
    {
        public static MedicalCondition GetMedicalCondition(Entity patient)
        {
            if (patient == null)
                return null;

            BehaviorPatient behaviorPatient = patient.GetComponent<BehaviorPatient>();
            return behaviorPatient == null ? null : behaviorPatient.m_state.m_medicalCondition;
        }

        public static SegmentController GetSegment(GameObject segmentObject)
        {
            return segmentObject == null ? null : segmentObject.GetComponent<SegmentController>();
        }

        public static bool IsHovered(SegmentController segment, int index)
        {
            if (index >= segment.m_itemCount || !segment.IsInRange(index))
                return false;

            HoverTooltipDelay hover = segment.GetItemComponent<HoverTooltipDelay>(index);
            return hover != null && hover.IsMouseOver();
        }

        public static int GetFinishedExamStartIndex(ProcedureQueue queue)
        {
            return queue.m_plannedExaminationStates.Count +
                ((queue.m_activeExamination != null) ? 1 : 0) +
                queue.m_labProcedures.Count;
        }

        // Mirrors SymptomsPanelController.UpdateSymptoms item layout (hidden-symptoms tile first).
        // The visitor returns false to stop.
        public static void ForEachVisibleSymptomItem(
            SegmentController segment,
            MedicalCondition medicalCondition,
            Func<int, Symptom, bool> visitor)
        {
            int itemIndex = medicalCondition.GetNumberOfHiddenSymptoms() > 0 ? 1 : 0;
            foreach (Symptom symptom in medicalCondition.m_symptoms)
            {
                if (symptom.m_hidden || !symptom.m_spawned)
                    continue;

                if (itemIndex < segment.m_displayedItemCount && segment.IsInRange(itemIndex))
                {
                    if (!visitor(itemIndex, symptom))
                        return;
                }

                itemIndex++;
            }
        }

        public static GameDBMedicalCondition GetHoveredDiagnosis(
            DiagnosisPanelController panel,
            Entity panelPatient,
            Entity patient)
        {
            if (panel == null ||
                patient == null ||
                panelPatient != patient ||
                !panel.gameObject.activeInHierarchy)
            {
                return null;
            }

            MedicalCondition medicalCondition = GetMedicalCondition(patient);
            if (medicalCondition == null)
                return null;

            SegmentController possibleSegment = GetSegment(panel.m_possibleDiagnosesSegmentStatic);
            if (possibleSegment != null && medicalCondition.m_possibleDiagnoses != null)
            {
                for (int index = 0; index < medicalCondition.m_possibleDiagnoses.Count; index++)
                {
                    if (IsHovered(possibleSegment, index))
                        return medicalCondition.m_possibleDiagnoses[index].m_diagnosis.Entry;
                }
            }

            if (medicalCondition.m_diagnosedMedicalCondition == null)
                return null;

            SegmentController finalSegment = GetSegment(panel.m_finalDiagnosisSegmentStatic);
            if (finalSegment == null || !IsHovered(finalSegment, 0))
                return null;

            return medicalCondition.m_diagnosedMedicalCondition.Entry;
        }
    }
}
