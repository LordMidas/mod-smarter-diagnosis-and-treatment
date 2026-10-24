using System.Collections.Generic;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    public static class PossibleDiagnosisExaminationHighlight
    {
        private static readonly HighlightColorSet HighlightColors = new HighlightColorSet();

        private static GameDBMedicalCondition hoveredDiagnosis;
        private static Entity hoveredPatient;

        public static void TrackHoveredDiagnosis(DiagnosisPanelController panel, Entity patient)
        {
            if (!ModConfig.Highlights.ExamsOnDiagnosisHover)
                return;

            GameDBMedicalCondition diagnosis = PatientUi.GetHoveredDiagnosis(panel, patient, patient);

            if (hoveredDiagnosis != diagnosis || hoveredPatient != patient)
            {
                RestoreHighlights();
                hoveredDiagnosis = diagnosis;
                hoveredPatient = patient;
            }
        }

        public static void RefreshHighlights(ExaminationsPanelController panel, Entity patient)
        {
            if (!ModConfig.Highlights.ExamsOnDiagnosisHover ||
                hoveredDiagnosis == null || hoveredPatient != patient || panel == null || patient == null)
                return;

            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(patient);
            if (medicalCondition == null)
                return;

            ProcedureComponent procedureComponent = patient.GetComponent<ProcedureComponent>();
            SegmentController availableSegment = PatientUi.GetSegment(panel.m_availableExaminationsStaticSegment);
            if (availableSegment != null && procedureComponent != null)
            {
                List<GameDBExamination> availableExaminations =
                    procedureComponent.m_examinationAvailability.m_keys;
                for (int index = 0; index < availableExaminations.Count; index++)
                {
                    GameDBExamination examination = availableExaminations[index];
                    if (index < availableSegment.m_itemCount &&
                        availableSegment.IsInRange(index) &&
                        IsRelevant(medicalCondition, examination))
                        HighlightColors.Apply(availableSegment, index);
                }
            }

            SegmentController activeSegment = PatientUi.GetSegment(panel.m_activeExaminationsStaticSegment);
            if (activeSegment == null || procedureComponent == null)
                return;

            ProcedureQueue queue = procedureComponent.m_state.m_procedureQueue;
            int itemIndex = PatientUi.GetFinishedExamStartIndex(queue);

            foreach (ExaminationState examinationState in queue.m_finishedExaminations)
            {
                if (activeSegment.IsInRange(itemIndex) &&
                    IsRelevant(medicalCondition, examinationState.m_examination.Entry))
                    HighlightColors.Apply(activeSegment, itemIndex);

                itemIndex++;
            }
        }

        public static void RestoreHighlights()
        {
            HighlightColors.Restore();
        }

        private static bool IsRelevant(MedicalCondition medicalCondition, GameDBExamination examination)
        {
            return ExaminationRelevance.HasUnrevealedSymptomRevealedBy(
                medicalCondition,
                hoveredDiagnosis,
                examination);
        }
    }
}
