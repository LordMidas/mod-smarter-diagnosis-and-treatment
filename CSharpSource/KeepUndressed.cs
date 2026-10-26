using System;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    // Vanilla dresses a clinic patient after every exam that undressed them, and the next exam undresses them again.
    // Here the patient stays undressed while their own doctor goes on examining them: the re-dress of the exam scripts
    // is skipped, also before a fully clothed exam, and an exam that needs other undressing changes the clothing to its
    // own level as in vanilla. The patient is dressed back instantly when they get up and walk during an exam that
    // does not need their current undressing (eye chart, toilet samples, any mod exam that moves them), a treatment
    // starts, an exam is done by other staff, or the patient leaves the doctor. No state is stored: the dress level
    // is saved with the character, and every rule reads only the current situation.

    internal static class KeepUndressed
    {
        private const string DressState = "PROCEDURE_PATIENT_DRESS";
        private const string DressBubble = "BUBBLE_DRESS";
        private const float DressWaitSeconds = 3f;

        [ThreadStatic] private static bool restoring;

        /// <summary>
        /// The script is an exam of an undressed clinic patient by their own doctor, and the exam needs either full
        /// clothing or exactly the current level. Any change of clothing now would only dress the patient again
        /// (before a fully clothed exam, or after any exam). An exam that needs other undressing is not kept, so it
        /// undresses the patient to its own level as in vanilla.
        /// </summary>
        public static bool IsKeeping(ProcedureScript script, Entity patientEntity)
        {
            GameDBExamination examination;
            BehaviorPatient patient;
            DressLevel current;
            if (!IsUndressedForOwnDoctor(script, patientEntity, out examination, out patient, out current))
                return false;

            DressLevel required = examination.Procedure.DressLevel;
            return required == DressLevel.FULL || current == patient.GetTreatedSymptomDressLevel(required);
        }

        /// <summary>The script is an exam of an undressed clinic patient by their own doctor.</summary>
        private static bool IsUndressedForOwnDoctor(ProcedureScript script, Entity patientEntity,
            out GameDBExamination examination, out BehaviorPatient patient, out DressLevel current)
        {
            examination = null;
            patient = null;
            current = DressLevel.FULL;
            if (script == null || patientEntity == null || script.m_stateData.m_examination == null)
                return false;

            examination = script.m_stateData.m_examination.Entry;
            patient = patientEntity.GetComponent<BehaviorPatient>();
            if (examination == null || patient == null || patientEntity.GetComponent<HospitalizationComponent>().IsHospitalized())
                return false;

            current = patientEntity.GetComponent<AnimModelComponent>().m_state.m_dressLevel;
            if (current == patient.GetTreatedSymptomDressLevel())
                return false;

            return IsOwnDoctor(patient, script.m_stateData.m_procedureScene.Doctor.GetEntity());
        }

        /// <summary>
        /// A patient who gets up and walks during their own doctor's exam is dressed back unless the exam needs
        /// exactly their current undressing: a fully clothed exam that moves them (eye chart, toilet samples) or
        /// one that needs other undressing. Only ever dresses: an undressing exam still undresses them on arrival.
        /// </summary>
        public static void OnPatientWalks(Entity patientEntity)
        {
            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            if (component == null || component.m_state.m_currentProcedureScript == null)
                return;

            GameDBExamination examination;
            BehaviorPatient patient;
            DressLevel current;
            if (IsUndressedForOwnDoctor(component.m_state.m_currentProcedureScript.GetEntity(), patientEntity, out examination, out patient, out current) &&
                current != patient.GetTreatedSymptomDressLevel(examination.Procedure.DressLevel))
            {
                Restore(patientEntity);
            }
        }

        private static bool IsOwnDoctor(BehaviorPatient patient, Entity doctor)
        {
            return doctor != null && patient.m_state.m_doctor != null && patient.m_state.m_doctor.GetEntity() == doctor;
        }

        public static bool IsKeeping(Entity patientEntity)
        {
            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            if (component == null || component.m_state.m_currentProcedureScript == null)
                return false;

            return IsKeeping(component.m_state.m_currentProcedureScript.GetEntity(), patientEntity);
        }

        public static bool IsRestoring
        {
            get { return restoring; }
        }

        /// <summary>
        /// Whether the next exam stays with the patient's own doctor. Then the patient keeps their clothing: a fully
        /// clothed exam or one on the same level leaves it as it is, and any other undressing exam changes it itself.
        /// </summary>
        public static bool StaysWithDoctor(Entity patientEntity, Entity doctor)
        {
            BehaviorPatient patient = patientEntity.GetComponent<BehaviorPatient>();
            return patient != null && IsOwnDoctor(patient, doctor);
        }

        /// <summary>Dresses a clinic patient back instantly if an exam left them undressed.</summary>
        public static void Restore(Entity patientEntity)
        {
            BehaviorPatient patient = patientEntity.GetComponent<BehaviorPatient>();
            if (patient == null || patientEntity.GetComponent<HospitalizationComponent>().IsHospitalized())
                return;

            AnimModelComponent model = patientEntity.GetComponent<AnimModelComponent>();
            DressLevel dressed = patient.GetTreatedSymptomDressLevel();
            if (model.m_state.m_dressLevel == dressed)
                return;

            restoring = true;
            try
            {
                model.SetClothingLevel(dressed);
            }
            finally
            {
                restoring = false;
            }
        }

        public static bool IsDressState(string state)
        {
            return state == DressState;
        }

        public static bool IsDressBubble(string message)
        {
            return message == DressBubble;
        }

        public static float SkippedDressWait
        {
            get { return DressWaitSeconds + 0.01f; }
        }
    }

    /// <summary>Skips the re-dress of the exam scripts while the patient is kept undressed.</summary>
    [HarmonyPatch(typeof(AnimModelComponent), nameof(AnimModelComponent.SetClothingLevel))]
    public static class AnimModelComponent_SetClothingLevel
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(AnimModelComponent_SetClothingLevel));

        public static bool Prefix(AnimModelComponent __instance, DressLevel dressLevel)
        {
            if (KeepUndressed.IsRestoring || dressLevel == __instance.m_state.m_dressLevel)
                return true;

            Entity entity = __instance.m_entity;
            return entity.GetComponent<BehaviorPatient>() == null || !KeepUndressed.IsKeeping(entity);
        }
    }

    /// <summary>No "get dressed" order while the patient is kept undressed.</summary>
    [HarmonyPatch(typeof(ProcedureScript), nameof(ProcedureScript.SpeakCharacter), new Type[] { typeof(Entity), typeof(string), typeof(string), typeof(float) })]
    public static class ProcedureScript_SpeakCharacter
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(ProcedureScript_SpeakCharacter));

        public static bool Prefix(ProcedureScript __instance, string message)
        {
            if (!KeepUndressed.IsDressBubble(message))
                return true;

            return !KeepUndressed.IsKeeping(__instance, __instance.m_stateData.m_procedureScene.m_patient.GetEntity());
        }
    }

    /// <summary>The dress state only waits for the patient to dress, so it ends at once while they stay undressed.</summary>
    [HarmonyPatch(typeof(ProcedureScript), nameof(ProcedureScript.SwitchState))]
    public static class ProcedureScript_SwitchState
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(ProcedureScript_SwitchState));

        public static void Postfix(ProcedureScript __instance, string state)
        {
            if (KeepUndressed.IsDressState(state) &&
                KeepUndressed.IsKeeping(__instance, __instance.m_stateData.m_procedureScene.m_patient.GetEntity()))
            {
                __instance.m_stateData.m_timeInState = KeepUndressed.SkippedDressWait;
            }
        }
    }

    /// <summary>An exam by the patient's own doctor keeps the patient undressed; one by other staff starts dressed.</summary>
    [HarmonyPatch(typeof(ProcedureComponent), nameof(ProcedureComponent.StartExamination),
        new Type[] { typeof(GameDBExamination), typeof(Entity), typeof(Entity), typeof(Entity), typeof(Entity), typeof(AccessRights), typeof(EquipmentListRules) })]
    public static class ProcedureComponent_StartExamination_Staff
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(ProcedureComponent_StartExamination_Staff));

        public static void Prefix(ProcedureComponent __instance, Entity doctor, Entity nurse, Entity labSpecialist)
        {
            if (nurse == null && labSpecialist == null && KeepUndressed.StaysWithDoctor(__instance.m_entity, doctor))
            {
                return;
            }

            KeepUndressed.Restore(__instance.m_entity);
        }
    }

    /// <summary>An exam assigned by department is done by other staff, so the patient starts dressed.</summary>
    [HarmonyPatch(typeof(ProcedureComponent), nameof(ProcedureComponent.StartExamination),
        new Type[] { typeof(GameDBExamination), typeof(Entity), typeof(Department), typeof(AccessRights), typeof(EquipmentListRules) })]
    public static class ProcedureComponent_StartExamination_Department
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(ProcedureComponent_StartExamination_Department));

        public static void Prefix(ProcedureComponent __instance)
        {
            KeepUndressed.Restore(__instance.m_entity);
        }
    }

    /// <summary>Treatments start dressed; their scripts undress the patient as they need.</summary>
    [HarmonyPatch]
    public static class ProcedureComponent_StartTreatment
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(ProcedureComponent_StartTreatment));

        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (System.Reflection.MethodInfo method in typeof(ProcedureComponent).GetMethods())
            {
                if (method.DeclaringType == typeof(ProcedureComponent) &&
                    (method.Name == nameof(ProcedureComponent.StartTreatment) || method.Name == nameof(ProcedureComponent.StartReservedProcedure)))
                {
                    yield return method;
                }
            }
        }

        public static void Prefix(ProcedureComponent __instance)
        {
            KeepUndressed.Restore(__instance.m_entity);
        }
    }

    /// <summary>Getting up and walking during an exam that does not need the current undressing dresses the patient back.</summary>
    [HarmonyPatch]
    public static class WalkComponent_Walk
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(WalkComponent_Walk));

        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (System.Reflection.MethodInfo method in typeof(WalkComponent).GetMethods())
            {
                if (method.DeclaringType == typeof(WalkComponent) &&
                    (method.Name == nameof(WalkComponent.SetDestination) || method.Name == nameof(WalkComponent.GoSit)))
                {
                    yield return method;
                }
            }
        }

        public static void Prefix(WalkComponent __instance)
        {
            Entity entity = __instance.m_entity;
            if (entity != null && entity.GetComponent<BehaviorPatient>() != null)
                KeepUndressed.OnPatientWalks(entity);
        }
    }

    /// <summary>Leaving the doctor, in any way, dresses the patient back.</summary>
    [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.SwitchState))]
    public static class BehaviorPatient_SwitchState
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.KeepUndressed, nameof(BehaviorPatient_SwitchState));

        public static void Postfix(BehaviorPatient __instance, PatientState state)
        {
            switch (state)
            {
                // With the doctor, or owned by systems that set the clothing themselves.
                case PatientState.GoingToDoctor:
                case PatientState.BeingExamined:
                case PatientState.BeingTreated:
                case PatientState.GoingToCollapse:
                case PatientState.Collapsing:
                case PatientState.Dead:
                case PatientState.OverriddenByHospitalization:
                    return;
            }

            KeepUndressed.Restore(__instance.m_entity);
        }
    }
}
