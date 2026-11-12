using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    // Hospitalized patients get the configured bedside exams from a nurses' station nurse instead of the on-call
    // doctor. Each listed exam gets a copy of its procedure that asks for a patient-care nurse (DEFAULT_ROOMS: the
    // nurses' station of the department) and no doctor, and runs this mod's nurse port of the vanilla script.
    // Vanilla's scene factory, reservation and finish code handle the rest. No stored state: the copy is only used
    // while reserving, and the running script finds the exam through m_stateData.m_examination.
    internal static class NurseExams
    {
        private const string LogPrefix = "[ModSmarterDiagnosisAndTreatment] NurseExams: ";

        private static readonly Dictionary<string, string> NurseScripts = new Dictionary<string, string>
        {
            { "ProcedureScriptExaminationGeneral", nameof(LmsdtNurseExaminationGeneral) },
            { "ProcedureScriptExaminationGeneralEquipment", nameof(LmsdtNurseExaminationGeneralEquipment) },
            { "ProcedureScriptExaminationUrineAnalysis", nameof(LmsdtNurseExaminationUrineAnalysis) }
        };

        private static readonly MethodInfo MemberwiseCloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone");

        private static Dictionary<GameDBExamination, GameDBProcedure> nurseProcedures;
        private static DatabaseEntryRef<GameDBSkill>[] nurseQualifications;

        /// <summary>The qualification the nurse needs; also the skill that sets the duration and gains the points.</summary>
        public static DatabaseEntryRef<GameDBSkill>[] NurseQualifications
        {
            get
            {
                return nurseQualifications ?? (nurseQualifications = new[]
                {
                    new DatabaseEntryRef<GameDBSkill>(Database.Instance.GetEntry<GameDBSkill>("SKILL_NURSE_QUALIF_PATIENT_CARE"))
                });
            }
        }

        /// <summary>The script is one of this mod's nurse exam scripts.</summary>
        public static bool IsNurseScript(ProcedureScript script)
        {
            return script is LmsdtNurseExaminationGeneral || script is LmsdtNurseExaminationGeneralEquipment ||
                script is LmsdtNurseExaminationUrineAnalysis;
        }

        /// <summary>The configured exam a nurse does for this patient: hospitalized, alive, exam listed and supported.</summary>
        public static bool Applies(Entity patientEntity, GameDBExamination examination)
        {
            if (!ModConfig.NurseExams.Enabled || patientEntity == null || examination == null)
                return false;

            HospitalizationComponent hospitalization = patientEntity.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || !hospitalization.IsHospitalized() || hospitalization.m_state.m_dead)
                return false;

            return GetNurseProcedure(examination) != null;
        }

        /// <summary>The nurse copy of the exam's procedure, or null if the exam is not listed or not supported.</summary>
        public static GameDBProcedure GetNurseProcedure(GameDBExamination examination)
        {
            if (nurseProcedures == null)
                nurseProcedures = Build();

            GameDBProcedure procedure;
            return nurseProcedures.TryGetValue(examination, out procedure) ? procedure : null;
        }

        private static Dictionary<GameDBExamination, GameDBProcedure> Build()
        {
            Dictionary<GameDBExamination, GameDBProcedure> result = new Dictionary<GameDBExamination, GameDBProcedure>();
            foreach (string id in ModConfig.NurseExams.Examinations)
            {
                GameDBExamination examination = Database.Instance.GetEntry<GameDBExamination>(id);
                if (examination == null || examination.Procedure == null)
                {
                    Debug.LogWarning(LogPrefix + "unknown examination " + id + ", ignored");
                    continue;
                }

                string nurseScript;
                if (!NurseScripts.TryGetValue(examination.Procedure.ProcedureScript ?? string.Empty, out nurseScript))
                {
                    Debug.LogWarning(LogPrefix + id + " uses " + examination.Procedure.ProcedureScript + ", which nurses cannot do, ignored");
                    continue;
                }

                try
                {
                    result[examination] = CreateNurseProcedure(examination.Procedure, nurseScript);
                }
                catch (Exception ex)
                {
                    Debug.LogError(LogPrefix + "could not prepare " + id + ", ignored. " + ex);
                }
            }

            return result;
        }

        private static GameDBProcedure CreateNurseProcedure(GameDBProcedure original, string nurseScript)
        {
            GameDBProcedure copy = (GameDBProcedure)MemberwiseCloneMethod.Invoke(original, null);
            Traverse traverse = Traverse.Create(copy);
            traverse.Property("RequiredDoctorQualifications").SetValue(null);
            traverse.Property("RequiredDoctorRoles").SetValue(null);
            traverse.Property("FallbackDoctorDepartments").SetValue(null);
            traverse.Property("RequiredNurseQualifications").SetValue(NurseQualifications);
            traverse.Property("RequiredNurseRoles").SetValue(null);
            traverse.Property("ProcedureScript").SetValue(nurseScript);
            copy.StaffSelectionRules = StaffSelectionRules.DEFAULT_ROOMS;
            return copy;
        }
    }

    /// <summary>
    /// A listed exam of a hospitalized patient is reserved with a nurses' station nurse. The doctor does it instead
    /// unless the only problem is that every such nurse is busy and WaitForNurse is on.
    /// </summary>
    [HarmonyPatch(typeof(ProcedureComponent), nameof(ProcedureComponent.ReserveExamination))]
    public static class ProcedureComponent_ReserveExamination
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.NurseExams.Enabled, nameof(ProcedureComponent_ReserveExamination));

        public static bool Prefix(ProcedureComponent __instance, GameDBExamination examination, Entity patient,
            Department department, Room room, AccessRights accessRights, ref ProcedureSceneAvailability __result,
            bool __runOriginal)
        {
            // An earlier Prefix already decided (Hospital Flow Optimizer's trauma-center wait): keep its result.
            if (!__runOriginal)
                return false;

            if (!NurseExams.Applies(patient, examination))
                return true;

            ProcedureSceneAvailability availability;
            try
            {
                availability = __instance.ReserveProcedure(NurseExams.GetNurseProcedure(examination), patient, department,
                    room, accessRights, examination.DatabaseID.ToString(), assignProcedureData: false);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ModSmarterDiagnosisAndTreatment] NurseExams: reserving " + examination.DatabaseID +
                    " with a nurse failed, the doctor does it. " + ex);
                return true;
            }

            if (availability == ProcedureSceneAvailability.AVAILABLE)
            {
                __instance.m_state.m_reservedProcedureScript.GetEntity().m_stateData.m_examination = examination;
                __result = availability;
                return false;
            }

            // Only a busy nurse is waited for. No qualified nurse, or a room or equipment the nurse scene cannot get,
            // goes to the doctor, whose own scene then decides.
            if (availability != ProcedureSceneAvailability.STAFF_BUSY || !ModConfig.NurseExams.WaitForNurse)
                return true;

            __result = availability;
            return false;
        }
    }

    /// <summary>
    /// Loading a save: vanilla resolves saved procedure scripts only in its own assembly, so a reserved or running
    /// nurse exam would fail to load. Mod types come from the game's own mod type registry. Always on, so a save
    /// still loads after the feature is switched off.
    /// </summary>
    [HarmonyPatch(typeof(LopitalEntityFactory), nameof(LopitalEntityFactory.InstantiateProcedureScript), new Type[] { typeof(string), typeof(ProcedureScriptPersistentData) })]
    public static class LopitalEntityFactory_InstantiateProcedureScript
    {
        // Mirrors the vanilla body for a type found in HospitalMod.sm_modTypes.
        public static bool Prefix(string scriptName, ProcedureScriptPersistentData persistentData, ref ProcedureScript __result)
        {
            Type type;
            if (scriptName == null || Type.GetType("Lopital." + scriptName) != null || !HospitalMod.sm_modTypes.TryGetValue(scriptName, out type))
                return true;

            ProcedureScript script = Activator.CreateInstance(type) as ProcedureScript;
            if (persistentData != null)
            {
                script.m_stateData = persistentData;
                if (script.m_stateData.m_procedureScene == null)
                {
                    Debug.LogError("No procedure scene for " + scriptName + ", not loaded");
                }
                else
                {
                    script.m_stateData.m_procedureScene.Validate();
                    ProcedureManager.GetInstance().AddScript(script);
                }
            }
            else
            {
                ProcedureManager.GetInstance().AddScript(script);
            }

            __result = script;
            return false;
        }
    }
}
