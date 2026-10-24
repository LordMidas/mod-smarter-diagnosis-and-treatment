using System;
using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// While an AI patient is on the operating table, vanilla ignores collapses and collapse-risk symptoms until the
    /// patient is back in bed. Here the OR team resolves a pending collapse with the survival roll (anesthesiologist
    /// bonus included, no collapse script) and gives the treatment of each untreated collapse-risk symptom at once.
    /// </summary>
    [HarmonyPatch(typeof(HospitalizationComponent), "UpdateStateBeingTreated")]
    public static class HospitalizationComponent_UpdateStateBeingTreated
    {
        private static Action<BehaviorPatient, GameDBSymptom> activateOrAddCollapseSymptom;

        private static bool Prepare() => ModConfig.Patch(ModConfig.Critical.InSurgeryEnabled, nameof(HospitalizationComponent_UpdateStateBeingTreated));

        public static void Prefix(HospitalizationComponent __instance)
        {
            ProcedureComponent procedureComponent = __instance.m_entity.GetComponent<ProcedureComponent>();
            ProcedureScript script = procedureComponent.m_state.m_currentProcedureScript.GetEntity();
            if (script == null ||
                !script.m_stateData.m_treatment.IsValid ||
                script.m_stateData.m_treatment.Entry.TreatmentType != TreatmentType.SURGERY)
            {
                return;
            }

            BehaviorPatient patient = __instance.m_entity.GetComponent<BehaviorPatient>();
            if (patient.GetControlMode() != PatientControlMode.AI)
                return;

            bool collapsePending = patient.m_state.m_collapseSymptom.IsValid && !patient.m_state.m_fromLevelNoCollapse;
            if (!collapsePending && !CriticalTreatments.HasUntreatedCriticalSymptom(patient, true))
                return;

            MedicalCondition medicalCondition = patient.m_state.m_medicalCondition;
            if (collapsePending)
                ResolveCollapse(patient, medicalCondition);

            GiveCriticalTreatments(patient, procedureComponent, medicalCondition);
            ClearStaleCollapseProcedure(patient, medicalCondition);
        }

        // Mirrors BehaviorPatient.SetupCollapseProcedure without starting the collapse script.
        private static void ResolveCollapse(BehaviorPatient patient, MedicalCondition medicalCondition)
        {
            GameDBSymptom top = medicalCondition.GetTopCollapseSymptom(patient.m_state.m_collapseSymptom.Entry);
            if (top.CollapseProcedureRef == null)
                return;

            GameDBProcedure collapseProcedure = top.CollapseProcedureRef.Entry;
            int survivability = 100;
            if (collapseProcedure == ModDb.CollapseMediumProcedure || collapseProcedure == ModDb.CollapseHighProcedure)
            {
                int count = patient.m_state.m_criticalCollapseCount;
                survivability = patient.m_state.m_fromLevelImmortal
                    ? 100
                    : (count < patient.COLLAPSE_SURVIVAL.Length ? patient.COLLAPSE_SURVIVAL[count] : 0);
                patient.m_state.m_criticalCollapseCount++;
            }

            patient.WillSuriveCollapse(survivability, true);
            ActivateOrAddCollapseSymptom(patient, top);
            patient.m_state.m_collapseProcedure = null;
            patient.m_state.m_collapseSymptom = null;
            medicalCondition.UpdatePossibleDiagnoses(patient.m_entity);

            if (!patient.m_state.m_willSurviveCollapse)
                patient.TriggerDeath();
        }

        private static void GiveCriticalTreatments(
            BehaviorPatient patient,
            ProcedureComponent procedureComponent,
            MedicalCondition medicalCondition)
        {
            List<Symptom> symptoms = medicalCondition.m_symptoms;
            List<PlannedTreatmentState> planned = procedureComponent.m_state.m_procedureQueue.m_plannedTreatmentStates;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (!CriticalTreatments.IsCriticalSymptom(symptom))
                    continue;

                GameDBSymptom entry = symptom.m_symptom.Entry;
                if (procedureComponent.HasTreatmentForSymptom(entry))
                    continue;

                GameDBTreatment treatment = CriticalTreatments.FindCriticalTreatment(entry);
                if (treatment == null)
                    continue;

                procedureComponent.m_state.m_procedureQueue.AddPlannedTreatment(treatment);
                int plannedIndex = IndexOf(planned, treatment);
                if (plannedIndex < 0)
                    continue;

                procedureComponent.SwitchToNextTreatment(plannedIndex);
                PlanHospitalizationAfterSurgery(procedureComponent, treatment);
            }
        }

        private static void PlanHospitalizationAfterSurgery(ProcedureComponent procedureComponent, GameDBTreatment treatment)
        {
            if (treatment.HospitalizationTreatmentRef == null || treatment.AllowedWithAnyHospitalization)
                return;

            GameDBTreatment hospitalization = treatment.HospitalizationTreatmentRef.Entry;
            if (hospitalization.Procedure.HospitalizationLevel > procedureComponent.GetHighestHospitalizationLevel())
                procedureComponent.PlanHospitalization(hospitalization, false, true);
        }

        // Same rule as HospitalizationComponent.Update: no collapse procedure without an active collapsing symptom.
        private static void ClearStaleCollapseProcedure(BehaviorPatient patient, MedicalCondition medicalCondition)
        {
            if (!patient.m_state.m_collapseProcedure.IsValid)
                return;

            List<Symptom> symptoms = medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (symptom.m_active && symptom.m_spawned && symptom.m_symptom.Entry.CollapseProcedureRef != null)
                    return;
            }

            patient.m_state.m_collapseProcedure = null;
        }

        private static int IndexOf(List<PlannedTreatmentState> planned, GameDBTreatment treatment)
        {
            for (int index = 0; index < planned.Count; index++)
            {
                if (planned[index].m_treatment.Entry == treatment)
                    return index;
            }

            return -1;
        }

        private static void ActivateOrAddCollapseSymptom(BehaviorPatient patient, GameDBSymptom symptom)
        {
            if (activateOrAddCollapseSymptom == null)
            {
                activateOrAddCollapseSymptom = (Action<BehaviorPatient, GameDBSymptom>)Delegate.CreateDelegate(
                    typeof(Action<BehaviorPatient, GameDBSymptom>),
                    AccessTools.Method(typeof(BehaviorPatient), "ActivateOrAddCollapseSymptom"));
            }

            activateOrAddCollapseSymptom(patient, symptom);
        }
    }
}
