using System.Collections.Generic;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    public static class TreatmentInteractionHighlight
    {

        private static readonly HighlightColorSet HighlightColors = new HighlightColorSet();
        private static readonly HashSet<GameDBTreatment> RelevantTreatments = new HashSet<GameDBTreatment>();

        private static DiagnosisPanelController diagnosisPanel;
        private static Entity diagnosisPatient;
        private static SymptomsPanelController symptomsPanel;
        private static MedicalCondition symptomsMedicalCondition;
        private static GameDBTreatment treatmentUnderPointer;
        private static GameDBTreatment hoveredTreatment;
        private static GameDBSymptom hoveredSymptom;
        private static GameDBMedicalCondition hoveredDiagnosis;

        public static void SetDiagnosisPanel(DiagnosisPanelController panel, Entity patient)
        {
            if (!ModConfig.Highlights.TreatmentInteractions)
                return;

            diagnosisPanel = panel;
            diagnosisPatient = patient;
        }

        public static void SetSymptomsPanel(SymptomsPanelController panel, MedicalCondition medicalCondition)
        {
            if (!ModConfig.Highlights.TreatmentInteractions)
                return;

            symptomsPanel = panel;
            symptomsMedicalCondition = medicalCondition;
        }

        public static void BeginTreatmentUpdate()
        {
            treatmentUnderPointer = null;
            HighlightColors.Restore();
        }

        public static void TrackHoveredTreatment(
            GameObject treatmentObject,
            GameDBTreatment treatment,
            int index)
        {
            if (!ModConfig.Highlights.TreatmentInteractions)
                return;

            TreatmentPanelController panel = treatmentObject.GetComponentInParent<TreatmentPanelController>();
            if (panel == null || panel.m_availableTreatmentsStaticSegment != treatmentObject)
                return;

            SegmentController segment = PatientUi.GetSegment(treatmentObject);
            if (segment != null && PatientUi.IsHovered(segment, index))
                treatmentUnderPointer = treatment;
        }

        public static void UpdateHighlights(TreatmentPanelController panel, Entity patient)
        {
            if (!ModConfig.Highlights.TreatmentInteractions)
                return;

            if (panel == null || patient == null)
            {
                ClearHover();
                return;
            }

            GameDBSymptom symptom = GetHoveredSymptom(patient);
            GameDBMedicalCondition diagnosis = GetHoveredDiagnosis(patient);

            if (treatmentUnderPointer != null)
            {
                hoveredTreatment = treatmentUnderPointer;
                hoveredSymptom = null;
                hoveredDiagnosis = null;
                RelevantTreatments.Clear();
                AddRequiredHospitalizationTreatment(hoveredTreatment);
                HighlightTreatedSymptoms(patient, hoveredTreatment);
                HighlightTreatedDiagnoses(patient, hoveredTreatment);
                HighlightAvailableTreatments(panel, patient);
                return;
            }

            hoveredTreatment = null;
            hoveredSymptom = symptom;
            hoveredDiagnosis = diagnosis;
            RelevantTreatments.Clear();

            if (diagnosis != null)
            {
                AddDiagnosisTreatment(diagnosis);
            }
            else if (symptom != null)
            {
                AddTreatments(symptom.Treatments);
            }

            HighlightAvailableTreatments(panel, patient);
        }

        private static GameDBSymptom GetHoveredSymptom(Entity patient)
        {
            if (symptomsPanel == null ||
                !symptomsPanel.gameObject.activeInHierarchy ||
                symptomsMedicalCondition == null ||
                diagnosisPatient != patient)
            {
                return null;
            }

            SegmentController segment = symptomsPanel.GetComponent<SegmentController>();
            if (segment == null)
                return null;

            GameDBSymptom hovered = null;
            PatientUi.ForEachVisibleSymptomItem(segment, symptomsMedicalCondition, (itemIndex, symptom) =>
            {
                if (!PatientUi.IsHovered(segment, itemIndex))
                    return true;

                hovered = symptom.m_symptom.Entry;
                return false;
            });

            return hovered;
        }

        private static GameDBMedicalCondition GetHoveredDiagnosis(Entity patient)
        {
            return PatientUi.GetHoveredDiagnosis(diagnosisPanel, diagnosisPatient, patient);
        }

        private static void HighlightAvailableTreatments(TreatmentPanelController panel, Entity patient)
        {
            if (panel.m_availableTreatmentsStaticSegment == null)
                return;

            ProcedureComponent procedureComponent = patient.GetComponent<ProcedureComponent>();
            if (procedureComponent == null)
                return;

            // Vanilla UpdateTreatments just rebuilt this map, so it is current and needs no recomputation.
            FakeMap<GameDBTreatment, ProcedureSceneAvailability> availableTreatments =
                procedureComponent.m_treatmentAvailability;
            SegmentController segment = PatientUi.GetSegment(panel.m_availableTreatmentsStaticSegment);
            if (segment == null)
                return;

            for (int index = 0; index < availableTreatments.m_keys.Count; index++)
            {
                GameDBTreatment treatment = availableTreatments.m_keys[index];
                if (treatment != hoveredTreatment &&
                    index < segment.m_itemCount &&
                    segment.IsInRange(index) &&
                    RelevantTreatments.Contains(treatment))
                {
                    HighlightColors.Apply(segment, index);
                }
            }
        }

        private static void HighlightTreatedSymptoms(Entity patient, GameDBTreatment treatment)
        {
            if (symptomsPanel == null ||
                !symptomsPanel.gameObject.activeInHierarchy ||
                symptomsMedicalCondition == null ||
                diagnosisPatient != patient)
            {
                return;
            }

            SegmentController segment = symptomsPanel.GetComponent<SegmentController>();
            if (segment == null)
                return;

            PatientUi.ForEachVisibleSymptomItem(segment, symptomsMedicalCondition, (itemIndex, symptom) =>
            {
                if (itemIndex < segment.m_itemCount &&
                    SymptomHasTreatment(symptom.m_symptom.Entry, treatment))
                {
                    HighlightColors.Apply(segment, itemIndex);
                }

                return true;
            });
        }

        private static void HighlightTreatedDiagnoses(Entity patient, GameDBTreatment treatment)
        {
            if (diagnosisPanel == null ||
                diagnosisPatient != patient ||
                !diagnosisPanel.gameObject.activeInHierarchy ||
                diagnosisPanel.m_possibleDiagnosesSegmentStatic == null)
            {
                return;
            }

            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(patient);
            SegmentController segment = PatientUi.GetSegment(diagnosisPanel.m_possibleDiagnosesSegmentStatic);
            if (medicalCondition == null || segment == null)
                return;

            for (int index = 0; index < medicalCondition.m_possibleDiagnoses.Count; index++)
            {
                if (index >= segment.m_itemCount || !segment.IsInRange(index))
                    continue;

                GameDBMedicalCondition diagnosis = medicalCondition.m_possibleDiagnoses[index].m_diagnosis.Entry;
                if (diagnosis != null && DiagnosisHasTreatment(diagnosis, treatment))
                    HighlightColors.Apply(segment, index);
            }

            if (medicalCondition.m_diagnosedMedicalCondition == null ||
                diagnosisPanel.m_finalDiagnosisSegmentStatic == null)
            {
                return;
            }

            SegmentController finalSegment = PatientUi.GetSegment(diagnosisPanel.m_finalDiagnosisSegmentStatic);
            GameDBMedicalCondition finalDiagnosis = medicalCondition.m_diagnosedMedicalCondition.Entry;
            if (finalSegment != null &&
                finalSegment.m_itemCount > 0 &&
                finalSegment.IsInRange(0) &&
                DiagnosisHasTreatment(finalDiagnosis, treatment))
            {
                HighlightColors.Apply(finalSegment, 0);
            }
        }

        private static bool DiagnosisHasTreatment(GameDBMedicalCondition diagnosis, GameDBTreatment treatment)
        {
            GameDBSymptom mainSymptom = diagnosis == null ? null : diagnosis.GetMainSymptom();
            if (diagnosis == null ||
                treatment == null ||
                mainSymptom == null ||
                mainSymptom.Treatments == null ||
                mainSymptom.Treatments.Length == 0 ||
                mainSymptom.Treatments[0].Entry == null)
            {
                return false;
            }

            return mainSymptom.Treatments[0].Entry == treatment;
        }

        private static bool SymptomHasTreatment(GameDBSymptom symptom, GameDBTreatment treatment)
        {
            return symptom != null && ContainsTreatment(symptom.Treatments, treatment);
        }

        private static bool ContainsTreatment(
            DatabaseEntryRef<GameDBTreatment>[] treatmentReferences,
            GameDBTreatment treatment)
        {
            if (treatmentReferences == null || treatment == null)
                return false;

            foreach (DatabaseEntryRef<GameDBTreatment> treatmentReference in treatmentReferences)
            {
                if (treatmentReference.Entry == treatment)
                    return true;
            }

            return false;
        }

        private static void AddTreatments(DatabaseEntryRef<GameDBTreatment>[] treatments)
        {
            if (treatments == null)
                return;

            foreach (DatabaseEntryRef<GameDBTreatment> treatmentReference in treatments)
            {
                if (treatmentReference.Entry != null)
                    RelevantTreatments.Add(treatmentReference.Entry);
            }
        }

        private static void AddDiagnosisTreatment(GameDBMedicalCondition diagnosis)
        {
            GameDBSymptom mainSymptom = diagnosis.GetMainSymptom();
            if (mainSymptom == null ||
                mainSymptom.Treatments == null ||
                mainSymptom.Treatments.Length == 0 ||
                mainSymptom.Treatments[0].Entry == null)
            {
                return;
            }

            GameDBTreatment treatment = mainSymptom.Treatments[0].Entry;
            RelevantTreatments.Add(treatment);
            AddRequiredHospitalizationTreatment(treatment);
        }

        private static void AddRequiredHospitalizationTreatment(GameDBTreatment treatment)
        {
            if (treatment.TreatmentType == TreatmentType.HOSPITALIZATION ||
                treatment.HospitalizationTreatmentRef == null ||
                treatment.HospitalizationTreatmentRef.Entry == null)
            {
                return;
            }

            RelevantTreatments.Add(treatment.HospitalizationTreatmentRef.Entry);
        }



        private static void ClearHover()
        {
            HighlightColors.Restore();
            hoveredTreatment = null;
            hoveredSymptom = null;
            hoveredDiagnosis = null;
            RelevantTreatments.Clear();
        }
    }
}
