using System;
using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Hospitalized AI patients treat collapse-risk symptoms before a queued surgery. Vanilla can still start the
    /// surgery first, through an existing surgery reservation or through the skip rule that moves on to the next
    /// planned treatment after PROCEDURE_SKIP_TIME_HOURS.
    /// </summary>
    [HarmonyPatch(typeof(HospitalizationComponent), nameof(HospitalizationComponent.TryToStartScheduledTreatment))]
    public static class HospitalizationComponent_TryToStartScheduledTreatment
    {
        public static void Prefix(HospitalizationComponent __instance, out List<PlannedTreatmentState> __state)
        {
            __state = null;

            BehaviorPatient patient = __instance.m_entity.GetComponent<BehaviorPatient>();
            // Done here, before the vanilla loop, because that loop enumerates the list.
            TreatmentBundling.GroupHospitalQueue(
                patient, __instance.m_entity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_plannedTreatmentStates);

            // Bundling above stays active; only the critical-first handling below is optional.
            if (!ModConfig.Critical.HospitalEnabled)
                return;

            if (patient.GetControlMode() != PatientControlMode.AI ||
                !CriticalTreatments.HasCriticalSymptom(patient.m_state.m_medicalCondition))
            {
                return;
            }

            ProcedureComponent procedureComponent = __instance.m_entity.GetComponent<ProcedureComponent>();
            List<PlannedTreatmentState> queue = procedureComponent.m_state.m_procedureQueue.m_plannedTreatmentStates;
            // The critical entries are now ahead of the first surgery, so this changes the order of the planned
            // treatments. HasCriticalSurgeryPlanned only looks at the first entry, so while a critical medicine
            // is ahead the surgery loses its critical operating room priority for the moment.
            CriticalTreatments.MoveCriticalAhead(queue, patient, true);

            if (!ShouldHoldSurgery(procedureComponent, patient, queue))
                return;

            ProcedureScript reserved = procedureComponent.m_state.m_reservedProcedureScript.GetEntity();
            if (reserved != null && reserved.m_stateData.m_treatment.IsValid &&
                reserved.m_stateData.m_treatment.Entry.TreatmentType == TreatmentType.SURGERY)
            {
                // Vanilla would release a reservation whose treatment is no longer in the list, which is not
                // safe while the patient is already being moved.
                if (__instance.m_state.m_oustideRoom)
                    return;

                procedureComponent.CancelReservation();
            }

            __state = new List<PlannedTreatmentState>();
            for (int index = queue.Count - 1; index >= 0; index--)
            {
                if (CriticalTreatments.IsSurgery(queue[index]))
                {
                    __state.Insert(0, queue[index]);
                    queue.RemoveAt(index);
                }
            }
        }

        public static Exception Finalizer(
            Exception __exception,
            HospitalizationComponent __instance,
            List<PlannedTreatmentState> __state)
        {
            if (__state == null)
                return __exception;

            BehaviorPatient patient = __instance.m_entity.GetComponent<BehaviorPatient>();
            List<PlannedTreatmentState> queue =
                __instance.m_entity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_plannedTreatmentStates;
            int insertAt = 0;
            while (insertAt < queue.Count &&
                CriticalTreatments.IsCriticalTreatment(patient, queue[insertAt].m_treatment.Entry))
            {
                insertAt++;
            }

            queue.InsertRange(insertAt, __state);
            return __exception;
        }

        // True when a critical treatment ahead of a surgery can be reserved now and has not waited past the cap.
        private static bool ShouldHoldSurgery(
            ProcedureComponent procedureComponent,
            BehaviorPatient patient,
            List<PlannedTreatmentState> queue)
        {
            float maxWait = DayTime.Instance.IngameTimeHoursToRealTimeSeconds(ModConfig.Critical.SurgeryHoldHours);
            for (int index = 0; index < queue.Count; index++)
            {
                PlannedTreatmentState planned = queue[index];
                if (CriticalTreatments.IsSurgery(planned))
                    return false;

                GameDBTreatment treatment = planned.m_treatment.Entry;
                if (planned.m_waitTime < maxWait &&
                    CriticalTreatments.IsCriticalTreatment(patient, treatment) &&
                    procedureComponent.m_treatmentAvailability.ContainsKey(treatment) &&
                    ProcedureScene.IsProcedureAvailable(procedureComponent.m_treatmentAvailability[treatment]) &&
                    HasSurgeryBehind(queue, index))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSurgeryBehind(List<PlannedTreatmentState> queue, int index)
        {
            for (int next = index + 1; next < queue.Count; next++)
            {
                if (CriticalTreatments.IsSurgery(queue[next]))
                    return true;
            }

            return false;
        }
    }
}
