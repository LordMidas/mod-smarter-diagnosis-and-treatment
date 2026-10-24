using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// After vanilla planned the diagnosis's own treatment list, AI doctors also plan one treatment for every
    /// visible symptom at or above the department's treatment threshold that is still untreated. Extras never
    /// escalate care: no surgery, no new or higher hospitalization, and no clinic treatment that cannot start now.
    /// Independent of the collapse-risk treatments in CriticalTreatments.
    /// </summary>
    [HarmonyPatch(typeof(ProcedureComponent), nameof(ProcedureComponent.PlanAllTreatments))]
    public static class ProcedureComponent_PlanAllTreatments
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Threshold.Enabled, nameof(ProcedureComponent_PlanAllTreatments));

        public static void Postfix(ProcedureComponent __instance, MedicalCondition medicalCondition, bool onlyCritical, TreatmentPlanningResult __result)
        {
            if (onlyCritical ||
                __result != TreatmentPlanningResult.PLANNED ||
                medicalCondition.m_diagnosedMedicalCondition == null)
            {
                return;
            }

            BehaviorPatient patient = __instance.m_entity.GetComponent<BehaviorPatient>();
            if (patient == null || patient.GetControlMode() != PatientControlMode.AI)
                return;

            Department department = patient.GetDepartment();
            if (department == null)
                return;

            SymptomHazard? threshold = TreatmentThreshold.GetMinimumHazard(TreatmentThreshold.Get(department.GetDepartmentType()));
            if (!threshold.HasValue)
                return;

            PlanExtraTreatments(__instance, patient, medicalCondition, TreatmentThreshold.GetHazardRank(threshold.Value));
        }

        private static void PlanExtraTreatments(ProcedureComponent procedureComponent, BehaviorPatient patient, MedicalCondition medicalCondition, int minimumRank)
        {
            HospitalizationComponent hospitalization = procedureComponent.m_entity.GetComponent<HospitalizationComponent>();
            ProcedureQueue procedureQueue = procedureComponent.m_state.m_procedureQueue;
            FakeMap<GameDBTreatment, ProcedureSceneAvailability> availability = null;

            List<Symptom> symptoms = medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (!symptom.m_active || !symptom.m_spawned || symptom.m_hidden)
                    continue;

                GameDBSymptom entry = symptom.m_symptom.Entry;
                if (entry.Treatments == null ||
                    TreatmentThreshold.GetHazardRank(entry.Hazard) < minimumRank ||
                    procedureComponent.HasTreatmentForSymptom(entry) ||
                    procedureComponent.HasPlannedTreatmentForSymptom(entry))
                {
                    continue;
                }

                if (availability == null)
                {
                    availability = procedureComponent.GetAllTreatmentsForMedicalCondition(
                        medicalCondition, TreatmentPlanningMode.ALL_SYMPTOMS);
                }

                for (int treatmentIndex = 0; treatmentIndex < entry.Treatments.Length; treatmentIndex++)
                {
                    GameDBTreatment treatment = entry.Treatments[treatmentIndex].Entry;
                    if (!CanPlanExtra(procedureComponent, patient, hospitalization, availability, treatment))
                        continue;

                    procedureQueue.AddPlannedTreatment(treatment);
                    break;
                }
            }
        }

        private static bool CanPlanExtra(
            ProcedureComponent procedureComponent,
            BehaviorPatient patient,
            HospitalizationComponent hospitalization,
            FakeMap<GameDBTreatment, ProcedureSceneAvailability> availability,
            GameDBTreatment treatment)
        {
            if (treatment.TreatmentType == TreatmentType.SURGERY ||
                treatment.TreatmentType == TreatmentType.HOSPITALIZATION ||
                !CriticalTreatments.IsAvailable(availability, treatment))
            {
                return false;
            }

            bool hospitalized = hospitalization.IsHospitalized();
            GameDBTreatment required = treatment.HospitalizationTreatmentRef == null
                ? null
                : treatment.HospitalizationTreatmentRef.Entry;
            if (required != null)
            {
                if (!hospitalized)
                    return false;

                GameDBTreatment current = hospitalization.GetHospitalizationTreatment();
                if (current == null ||
                    (!treatment.AllowedWithAnyHospitalization &&
                        current.Procedure.HospitalizationLevel < required.Procedure.HospitalizationLevel))
                {
                    return false;
                }
            }

            return hospitalized ||
                (patient.m_state.m_doctor.GetEntity() != null &&
                    CriticalTreatments.CanStartInClinic(patient, procedureComponent, treatment));
        }
    }
}
