using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// First drops a discovered wrong diagnosis (WrongDiagnosis/AutoCorrect, see <see cref="WrongDiagnosis"/>), so
    /// vanilla's next step diagnoses again or schedules an exam. Then AI patients in the ICU move to the HDU of their
    /// department once a nurse check-up has cleared them (see HospitalizationComponent_SwitchState) and their assigned
    /// doctor is on shift (see IcuStepDown).
    /// </summary>
    [HarmonyPatch(typeof(HospitalizationComponent), "SelectNextStep")]
    public static class HospitalizationComponent_SelectNextStep
    {
        private static bool AutomaticStepDown => ModConfig.Icu.Enabled && ModConfig.Icu.AutomaticForAiPatients;

        private static bool Prepare() => ModConfig.Patch(AutomaticStepDown || ModConfig.WrongDiagnosis.AutoCorrect, nameof(HospitalizationComponent_SelectNextStep));

        public static bool Prefix(HospitalizationComponent __instance, ref bool __result)
        {
            Entity entity = __instance.m_entity;
            BehaviorPatient patient = entity.GetComponent<BehaviorPatient>();
            WrongDiagnosis.CorrectIfNeeded(patient);

            if (!AutomaticStepDown || !IcuStepDown.IsCleared(entity))
                return true;

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
