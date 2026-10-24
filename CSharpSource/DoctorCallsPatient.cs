using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Vanilla fix (always on). When the patient's waiting room has no queue machine (for example the radiology
    /// waiting room after an exam there), the doctor walks over, calls "Next" and walks back to the office
    /// (ProcedureScriptControlDoctorCallsPatient). That script stays in DOCTOR_RETURNING until the doctor's walk
    /// ends, and the patient's UpdateStateBeingCalled waits for the script, so the patient only stood up once the
    /// doctor was back at the desk. Here the call ends as soon as the doctor turns back: GoToWorkPlace is already
    /// issued, so the doctor keeps walking and the patient follows. <see cref="BehaviorPatient_UpdateStateGoingToDoctor"/>
    /// holds the patient at the office until the doctor has arrived.
    /// </summary>
    [HarmonyPatch(typeof(ProcedureScriptControlDoctorCallsPatient), "UpdateStateDoctorReturning")]
    public static class ProcedureScriptControlDoctorCallsPatient_UpdateStateDoctorReturning
    {
        public static bool Prefix(ProcedureScriptControlDoctorCallsPatient __instance)
        {
            __instance.SwitchState(ProcedureScriptControlDoctorCallsPatient.STATE_IDLE);
            return false;
        }
    }

    /// <summary>
    /// Companion of the early call release above: a patient who reaches the office before the doctor waits there,
    /// instead of starting an exam or treatment with a doctor who is still in the corridor. Vanilla never had this
    /// case, since the patient only left once the doctor was seated. The wait is capped, so a doctor who never
    /// arrives (shift change, other mods) cannot block the patient.
    /// </summary>
    [HarmonyPatch(typeof(BehaviorPatient), "UpdateStateGoingToDoctor")]
    public static class BehaviorPatient_UpdateStateGoingToDoctor
    {
        private const float MaxWaitHours = 0.25f;

        public static bool Prefix(BehaviorPatient __instance)
        {
            BehaviorPatientStateData state = __instance.m_state;
            if (state.m_labSpecialist != null || state.m_doctor == null || state.m_deathTriggered)
                return true;

            if (__instance.m_entity.GetComponent<WalkComponent>().IsBusy())
                return true;

            Entity doctor = state.m_doctor.GetEntity();
            if (doctor == null)
                return true;

            // After CANCEL_OVERRIDE_BY_PROCEDURE_SCRIPT the doctor is Idle while walking back to the workplace.
            BehaviorDoctor behaviorDoctor = doctor.GetComponent<BehaviorDoctor>();
            if (behaviorDoctor.CurrentPatient != __instance.m_entity ||
                behaviorDoctor.m_state.m_doctorState != DoctorState.Idle ||
                !doctor.GetComponent<WalkComponent>().IsBusy())
            {
                return true;
            }

            return state.m_timeInState > DayTime.Instance.IngameTimeHoursToRealTimeSeconds(MaxWaitHours);
        }
    }
}
