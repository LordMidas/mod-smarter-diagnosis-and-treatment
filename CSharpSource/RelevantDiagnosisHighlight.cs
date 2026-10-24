using System.Collections.Generic;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    public static class RelevantDiagnosisHighlight
    {
        private static readonly HighlightColorSet HighlightColors = new HighlightColorSet();
        private static readonly HashSet<GameDBMedicalCondition> RelevantDiagnoses = new HashSet<GameDBMedicalCondition>();

        private static GameDBExamination hoveredExamination;
        private static Entity hoveredPatient;
        private static DiagnosisPanelController diagnosisPanel;
        private static Entity diagnosisPatient;
        private static GameDBExamination examinationUnderPointer;

        public static void BeginExaminationScan()
        {
            examinationUnderPointer = null;
        }

        public static void TrackHoveredExamination(GameObject examinationType, GameDBExamination examination, int index)
        {
            if (!ModConfig.Highlights.DiagnosesOnExamHover)
                return;

            SegmentController segment = PatientUi.GetSegment(examinationType);
            if (segment != null && PatientUi.IsHovered(segment, index))
                examinationUnderPointer = examination;
        }

        public static void EndExaminationScan(Entity patient)
        {
            if (!ModConfig.Highlights.DiagnosesOnExamHover)
                return;

            if (examinationUnderPointer == null || patient == null)
            {
                SetHoveredExamination(null, null, null);
                return;
            }

            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(patient);
            SetHoveredExamination(
                examinationUnderPointer,
                patient,
                ExaminationRelevance.FindMatchingDiagnoses(medicalCondition, examinationUnderPointer));
        }

        public static void SetHoveredExamination(
            GameDBExamination examination,
            Entity patient,
            IList<GameDBMedicalCondition> matchingDiagnoses)
        {
            RestoreHighlights();
            hoveredExamination = examination;
            hoveredPatient = patient;
            RelevantDiagnoses.Clear();

            if (matchingDiagnoses != null)
            {
                foreach (GameDBMedicalCondition diagnosis in matchingDiagnoses)
                {
                    if (diagnosis != null)
                        RelevantDiagnoses.Add(diagnosis);
                }
            }

            RefreshHighlights();
        }

        public static void ClearHoveredExamination()
        {
            hoveredExamination = null;
            hoveredPatient = null;
            RelevantDiagnoses.Clear();
            RestoreHighlights();
        }

        public static void RestoreHighlights()
        {
            HighlightColors.Restore();
        }

        public static void OnDiagnosisPanelUpdated(DiagnosisPanelController panel, Entity patient)
        {
            if (!ModConfig.Highlights.DiagnosesOnExamHover)
                return;

            if (hoveredPatient == null || patient == hoveredPatient || diagnosisPanel == null || diagnosisPanel == panel)
            {
                diagnosisPanel = panel;
                diagnosisPatient = patient;
            }

            RefreshHighlights();
        }

        private static void RefreshHighlights()
        {
            RestoreHighlights();

            if (hoveredExamination == null ||
                hoveredPatient == null ||
                diagnosisPanel == null ||
                diagnosisPatient != hoveredPatient ||
                !diagnosisPanel.gameObject.activeInHierarchy)
            {
                return;
            }

            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(hoveredPatient);
            if (medicalCondition == null)
                return;

            List<PossibleDiagnosis> possibleDiagnoses = medicalCondition.m_possibleDiagnoses;
            SegmentController segment = PatientUi.GetSegment(diagnosisPanel.m_possibleDiagnosesSegmentStatic);
            if (segment != null && possibleDiagnoses != null && RelevantDiagnoses.Count > 0)
            {
                for (int index = 0; index < possibleDiagnoses.Count; index++)
                {
                    if (!segment.IsInRange(index))
                        continue;

                    PossibleDiagnosis possibleDiagnosis = possibleDiagnoses[index];
                    if (possibleDiagnosis == null || possibleDiagnosis.m_diagnosis.Entry == null ||
                        !RelevantDiagnoses.Contains(possibleDiagnosis.m_diagnosis.Entry))
                    {
                        continue;
                    }

                    HighlightColors.Apply(segment, index);
                }
            }

            if (medicalCondition.m_diagnosedMedicalCondition == null)
                return;

            GameDBMedicalCondition finalDiagnosis = medicalCondition.m_diagnosedMedicalCondition.Entry;
            SegmentController finalSegment = PatientUi.GetSegment(diagnosisPanel.m_finalDiagnosisSegmentStatic);
            if (finalDiagnosis != null &&
                finalSegment != null &&
                finalSegment.m_itemCount > 0 &&
                finalSegment.IsInRange(0) &&
                ExaminationRelevance.HasUnrevealedSymptomRevealedBy(medicalCondition, finalDiagnosis, hoveredExamination))
            {
                HighlightColors.Apply(finalSegment, 0);
            }
        }
    }
}
