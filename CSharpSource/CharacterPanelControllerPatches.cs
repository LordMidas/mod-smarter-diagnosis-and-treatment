using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Lets the player move an ICU patient who no longer needs intensive care to the HDU of a department, and
    /// explains in the tooltip why a patient cannot move yet. See IcuStepDown for the criteria.
    /// </summary>
    internal static class CharacterPanelIcu
    {
        public const string HospitalCareLevelTransferHarmonyId = "Loupito75:HospitalCareLevelTransfer";

        public static BehaviorPatient GetIcuPatient(EntityIDPointer<Entity> character)
        {
            Entity entity = character.GetEntity();
            if (entity == null)
                return null;

            BehaviorPatient patient = entity.GetComponent<BehaviorPatient>();
            return patient != null && IcuStepDown.IsIcuPatient(patient) ? patient : null;
        }
    }

    [HarmonyPatch(typeof(CharacterPanelController), nameof(CharacterPanelController.IsAbleToChangeDepartments))]
    public static class CharacterPanelController_IsAbleToChangeDepartments
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Icu.Enabled, nameof(CharacterPanelController_IsAbleToChangeDepartments));

        // Vanilla refuses every ICU patient; any other blocker it checks keeps the result false.
        public static void Postfix(ref bool __result, EntityIDPointer<Entity> ___m_character)
        {
            if (__result)
                return;

            BehaviorPatient patient = CharacterPanelIcu.GetIcuPatient(___m_character);
            if (patient != null && IcuStepDown.GetBlockReason(patient, false) == null)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(CharacterPanelController), nameof(CharacterPanelController.OnDepartmentSelected))]
    public static class CharacterPanelController_OnDepartmentSelected
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Icu.Enabled, nameof(CharacterPanelController_OnDepartmentSelected));

        // Hospital Care Level Transfer acts on a selected department equal to the patient's current one. It must see
        // the patient while they are still in the ICU, so this runs after it.
        [HarmonyAfter(CharacterPanelIcu.HospitalCareLevelTransferHarmonyId)]
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(
            CharacterPanelController __instance,
            int index,
            ref bool __result,
            bool __runOriginal,
            EntityIDPointer<Entity> ___m_character)
        {
            if (!__runOriginal)
                return true;

            BehaviorPatient patient = CharacterPanelIcu.GetIcuPatient(___m_character);
            if (patient == null)
                return true;

            // Vanilla would keep the ICU hospitalization, so it never runs for an ICU patient.
            string reason = null;
            if (!__instance.IsAbleToChangeDepartments())
                reason = IcuStepDown.GetBlockReason(patient, false) ?? IcuStepDown.ReasonNotDiagnosed;

            Entity entity = patient.m_entity;
            if (reason == null && IcuStepDown.TryStepDown(patient, Hospital.Instance.m_departments[index], true, out reason))
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_BEEP");
                bool forceAll = SymptomsPanelController.DLCHardCoreModeCheck(
                    Hospital.Instance.m_state.m_diagnosePercentageAll,
                    Hospital.Instance.m_state.m_diagnosePercentageControlled,
                    patient.GetControlMode() == PatientControlMode.PlayerControl,
                    patient.m_state.m_doctor);
                entity.GetComponent<ProcedureComponent>().UpdateAllExaminationsForMedicalCondition(
                    patient.m_state.m_medicalCondition, 1000, forceAll);
                __instance.GetComponentInChildren<ExaminationsPanelController>().UpdateExaminations(entity);
                __result = true;
                return false;
            }

            UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
            IcuStepDown.ShowReason(entity, reason, patient);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterPanelController), nameof(CharacterPanelController.Update))]
    public static class CharacterPanelController_Update
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Icu.Enabled, nameof(CharacterPanelController_Update));

        // Only the big button: Hospital Care Level Transfer saves and restores the small button's tooltip.
        public static void Postfix(CharacterPanelController __instance, EntityIDPointer<Entity> ___m_character)
        {
            BehaviorPatient patient = CharacterPanelIcu.GetIcuPatient(___m_character);
            if (patient == null)
                return;

            IconButtonController button = __instance.m_buttonAssignToDepartment.GetComponentInChildren<IconButtonController>();
            string reason = IcuStepDown.GetBlockReason(patient, false);
            if (reason == null)
                button.SetTextLocID(IcuStepDown.ReasonReady);
            else if (reason == IcuStepDown.ReasonMinStay)
                button.SetTextLocID(IcuStepDown.GetMinStayText(patient), true);
            else
                button.SetTextLocID(reason);
        }
    }
}
