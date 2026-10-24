using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    public static class FinishedExamSymptomHighlight
    {
        private static readonly HighlightColorSet HighlightColors = new HighlightColorSet();

        private static GameDBExamination hoveredFinishedExamination;
        private static GameDBSymptom hoveredSymptom;
        private static GameDBExamination examinationUnderPointer;
        private static ExaminationsPanelController examinationsPanel;
        private static SymptomsPanelController symptomsPanel;
        private static Entity currentPatient;

        public static void BeginExaminationScan()
        {
            examinationUnderPointer = null;
        }

        public static void TrackHoveredFinishedExamination(
            ExaminationsPanelController panel,
            GameObject examinationType,
            GameDBExamination examination,
            int index)
        {
            if (!ModConfig.Highlights.SymptomsOnFinishedExamHover)
                return;

            if (panel.m_activeExaminationsStaticSegment == null ||
                examinationType != panel.m_activeExaminationsStaticSegment)
            {
                return;
            }

            Entity patient = PatientPanelContext.CurrentPatient;
            ProcedureComponent procedureComponent =
                patient == null ? null : patient.GetComponent<ProcedureComponent>();
            SegmentController segment = PatientUi.GetSegment(examinationType);
            if (segment == null || procedureComponent == null || !segment.IsInRange(index))
                return;

            ProcedureQueue queue = procedureComponent.m_state.m_procedureQueue;
            int finishedIndex = index - PatientUi.GetFinishedExamStartIndex(queue);
            if (finishedIndex < 0 || finishedIndex >= queue.m_finishedExaminations.Count)
                return;

            if (PatientUi.IsHovered(segment, index))
                examinationUnderPointer = examination;
        }

        public static void EndExaminationScan(ExaminationsPanelController panel, Entity patient)
        {
            if (!ModConfig.Highlights.SymptomsOnFinishedExamHover)
                return;

            examinationsPanel = panel;
            currentPatient = patient;

            if (examinationUnderPointer == null || patient == null)
            {
                ClearHoveredExamination();
                if (hoveredSymptom != null)
                {
                    HighlightColors.DiscardSavedColors();
                    HighlightFinishedExaminations(hoveredSymptom);
                }

                return;
            }

            SetHoveredExamination(panel, patient, examinationUnderPointer);
        }

        public static void TrackHoveredSymptom(SymptomsPanelController panel, MedicalCondition medicalCondition)
        {
            if (!ModConfig.Highlights.SymptomsOnFinishedExamHover)
                return;

            symptomsPanel = panel;
            SegmentController segment = panel.GetComponent<SegmentController>();
            Symptom hovered = null;
            if (segment != null && medicalCondition != null)
            {
                PatientUi.ForEachVisibleSymptomItem(segment, medicalCondition, (itemIndex, symptom) =>
                {
                    if (!PatientUi.IsHovered(segment, itemIndex))
                        return true;

                    hovered = symptom;
                    return false;
                });
            }

            if (hovered == null)
                ClearHoveredSymptom();
            else
                SetHoveredSymptom(hovered.m_symptom.Entry);
        }

        private static void SetHoveredExamination(
            ExaminationsPanelController panel,
            Entity patient,
            GameDBExamination examination)
        {
            bool unchanged = hoveredFinishedExamination == examination && hoveredSymptom == null;
            if (unchanged)
                HighlightColors.DiscardSavedColors();
            else
                HighlightColors.Restore();

            hoveredFinishedExamination = examination;
            hoveredSymptom = null;
            HighlightRevealedSymptoms(panel, patient, examination);
        }

        private static void SetHoveredSymptom(GameDBSymptom symptom)
        {
            bool unchanged = hoveredSymptom == symptom && hoveredFinishedExamination == null;
            if (unchanged)
                HighlightColors.DiscardSavedColors();
            else
                HighlightColors.Restore();

            hoveredSymptom = symptom;
            hoveredFinishedExamination = null;
            HighlightFinishedExaminations(symptom);
        }

        private static void ClearHoveredExamination()
        {
            if (hoveredFinishedExamination == null)
                return;

            hoveredFinishedExamination = null;
            HighlightColors.Restore();
        }

        private static void ClearHoveredSymptom()
        {
            if (hoveredSymptom == null)
                return;

            hoveredSymptom = null;
            HighlightColors.Restore();
        }

        private static void HighlightRevealedSymptoms(
            ExaminationsPanelController panel,
            Entity patient,
            GameDBExamination examination)
        {
            if (panel.m_activeExaminationsStaticSegment == null || patient == null)
                return;

            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(patient);
            if (medicalCondition == null)
                return;

            if (symptomsPanel == null || !symptomsPanel.gameObject.activeInHierarchy)
                return;

            SegmentController segment = symptomsPanel.GetComponent<SegmentController>();
            if (segment == null)
                return;

            PatientUi.ForEachVisibleSymptomItem(segment, medicalCondition, (itemIndex, symptom) =>
            {
                if (ExaminationEquivalence.SymptomListsExamination(symptom.m_symptom.Entry, examination))
                    HighlightColors.Apply(segment, itemIndex);

                return true;
            });
        }

        private static void HighlightFinishedExaminations(GameDBSymptom symptom)
        {
            ExaminationsPanelController panel = examinationsPanel;
            Entity patient = currentPatient;
            if (panel == null || patient == null)
                return;

            ProcedureComponent procedureComponent = patient.GetComponent<ProcedureComponent>();
            SegmentController segment = PatientUi.GetSegment(panel.m_activeExaminationsStaticSegment);
            if (procedureComponent == null || segment == null)
                return;

            ProcedureQueue queue = procedureComponent.m_state.m_procedureQueue;
            int itemIndex = PatientUi.GetFinishedExamStartIndex(queue);

            for (int index = 0; index < queue.m_finishedExaminations.Count; index++, itemIndex++)
            {
                GameDBExamination examination = queue.m_finishedExaminations[index].m_examination.Entry;
                if (examination != null && segment.IsInRange(itemIndex) &&
                    ExaminationEquivalence.SymptomListsExamination(symptom, examination))
                {
                    HighlightColors.Apply(segment, itemIndex);
                }
            }
        }
    }
}
