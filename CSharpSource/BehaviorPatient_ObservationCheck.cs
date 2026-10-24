using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// When the ER doctor confirms a diagnosis owned by another department, vanilla still forces ER observation if a
    /// bed is free. Observation never gives that diagnosis's main treatment: in HospitalizationComponent.SelectNextStep,
    /// CheckPatientPlacement transfers the patient before DoctorPlannedTreatment plans it. So skip it and let the AI
    /// transfer the patient once the critical treatments are done.
    /// </summary>
    [HarmonyPatch(typeof(BehaviorPatient), "ObservationCheck")]
    public static class BehaviorPatient_ObservationCheck
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ErObservation.SkipForOtherDepartment, nameof(BehaviorPatient_ObservationCheck));

        public static bool Prefix(BehaviorPatient __instance, bool forceIfPossible, ref bool __result)
        {
            if (!forceIfPossible || __instance.GetControlMode() != PatientControlMode.AI)
                return true;

            GameDBPointer<GameDBMedicalCondition> diagnosed = __instance.m_state.m_medicalCondition.m_diagnosedMedicalCondition;
            if (!diagnosed.IsValid || __instance.GetDepartment().GetDepartmentType() != ModDb.EmergencyDepartment)
                return true;

            GameDBDepartment target = diagnosed.Entry.DepartmentRef.Entry;
            if (target == ModDb.EmergencyDepartment ||
                MapScriptInterface.Instance.GetLeastBusyDepartmentOfType(target) == null)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
