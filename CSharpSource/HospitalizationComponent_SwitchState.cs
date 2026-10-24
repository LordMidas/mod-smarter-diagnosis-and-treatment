using HarmonyLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// A finished nurse check-up (vanilla switches OverridenByNurseCheckUp to InBed and resets m_checkUpTime) clears an
    /// eligible ICU patient for step-down. The transfer itself happens later, in HospitalizationComponent_SelectNextStep,
    /// because the nurse script is still running here. An aborted check-up does not reset the timer.
    /// </summary>
    [HarmonyPatch(typeof(HospitalizationComponent), nameof(HospitalizationComponent.SwitchState))]
    public static class HospitalizationComponent_SwitchState
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Icu.Enabled && ModConfig.Icu.AutomaticForAiPatients, nameof(HospitalizationComponent_SwitchState));

        public static void Prefix(HospitalizationComponent __instance, out HospitalizationState __state)
        {
            __state = __instance.m_state.m_hospitalizationState;
        }

        public static void Postfix(HospitalizationComponent __instance, HospitalizationState state, HospitalizationState __state)
        {
            if (__state != HospitalizationState.OverridenByNurseCheckUp ||
                state != HospitalizationState.InBed ||
                __instance.m_state.m_checkUpTime > 0.001f)
            {
                return;
            }

            BehaviorPatient patient = __instance.m_entity.GetComponent<BehaviorPatient>();
            if (patient == null ||
                patient.GetControlMode() != PatientControlMode.AI ||
                !IcuStepDown.IsIcuPatient(patient))
            {
                return;
            }

            if (IcuStepDown.GetBlockReason(patient, false) == null)
                IcuStepDown.MarkCleared(__instance.m_entity);
            else
                IcuStepDown.ClearMark(__instance.m_entity);
        }
    }
}
