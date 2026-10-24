using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// AI patients in the ICU move to the HDU of their department once a nurse check-up has cleared them (see
    /// HospitalizationComponent_SwitchState) and their assigned doctor is on shift (see IcuStepDown).
    /// </summary>
    [HarmonyPatch(typeof(HospitalizationComponent), "SelectNextStep")]
    public static class HospitalizationComponent_SelectNextStep
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Icu.Enabled && ModConfig.Icu.AutomaticForAiPatients, nameof(HospitalizationComponent_SelectNextStep));

        public static bool Prefix(HospitalizationComponent __instance, ref bool __result)
        {
            Entity entity = __instance.m_entity;
            if (!IcuStepDown.IsCleared(entity))
                return true;

            BehaviorPatient patient = entity.GetComponent<BehaviorPatient>();
            if (patient == null ||
                patient.GetControlMode() != PatientControlMode.AI ||
                !IcuStepDown.IsIcuPatient(patient))
            {
                return true;
            }

            if (IcuStepDown.GetBlockReason(patient, false) != null)
            {
                IcuStepDown.ClearMark(entity);
                return true;
            }

            if (IcuStepDown.GetBlockReason(patient, true) != null || !IcuStepDown.IsDoctorOnDuty(patient))
                return true;

            Department target = MapScriptInterface.Instance.GetLeastBusyDepartmentOfType(patient.BelongsToDepartment());
            string reason;
            if (target == null || !IcuStepDown.TryStepDown(patient, target, false, out reason))
                return true;

            IcuStepDown.ClearMark(entity);
            __result = true;
            return false;
        }
    }
}
