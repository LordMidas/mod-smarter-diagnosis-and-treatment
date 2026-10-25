using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    // Vanilla runs every queued sampling exam as its own procedure script: cabinet, animation, bin, sink, and then
    // the next one starts from scratch. These patches let a running sampling script take further queued samples of
    // the same kind inside one session. Each exam is still recorded as finished on its own and creates its own lab
    // sample. No state is stored: the script's m_examination and the queue's m_activeExamination always name the
    // same exam, so a save taken mid-session loads as vanilla would.

    internal enum ExamBundleGroup
    {
        None,
        BloodDraw,
        WasteSample
    }

    internal static class ExamBundling
    {
        private const string GeneralEquipmentScript = "ProcedureScriptExaminationGeneralEquipment";
        private const string UrineAnalysisScript = "ProcedureScriptExaminationUrineAnalysis";
        private const string BloodSamplingIdle = "stand_blood_sampling_idle";
        private const float BoredomReduction = 10f;
        private const int ParamEmployeeSkill = 1;
        private const int ParamProcedureTime = 2;
        private const int ActionTimeConstant = 20;

        private static readonly Dictionary<GameDBExamination, ExamBundleGroup> groups = new Dictionary<GameDBExamination, ExamBundleGroup>();

        private static GameDBExamination bloodTest;

        private static GameDBExamination BloodTest
        {
            get { return bloodTest ?? (bloodTest = Database.Instance.GetEntry<GameDBExamination>("EXM_BLOOD_TEST_SAMPLING")); }
        }

        public static ExamBundleGroup GetGroup(GameDBExamination examination)
        {
            if (examination == null || examination.Procedure == null)
                return ExamBundleGroup.None;

            ExamBundleGroup group;
            if (!groups.TryGetValue(examination, out group))
            {
                group = ComputeGroup(examination);
                groups[examination] = group;
            }

            return group;
        }

        private static ExamBundleGroup ComputeGroup(GameDBExamination examination)
        {
            GameDBProcedure procedure = examination.Procedure;
            if (examination.ExaminationType == ExaminationType.OBSERVATION ||
                procedure.RequiredStatLabQualificationRef != null ||
                procedure.RequiredRoomTags == null)
            {
                return ExamBundleGroup.None;
            }

            string script = procedure.ProcedureScript;
            bool sampling = examination.LabTestingExaminationRef != null;
            if (script == GeneralEquipmentScript && sampling && procedure.DressLevel == DressLevel.FULL &&
                procedure.AnimationSetupSitting != null && procedure.AnimationSetupSitting.AnimationNameIdle == BloodSamplingIdle)
            {
                return ExamBundleGroup.BloodDraw;
            }

            return script == UrineAnalysisScript && sampling ? ExamBundleGroup.WasteSample : ExamBundleGroup.None;
        }

        private static bool IsEnabled(ExamBundleGroup group)
        {
            switch (group)
            {
                case ExamBundleGroup.BloodDraw: return ModConfig.ExamBundling.BloodDraw;
                case ExamBundleGroup.WasteSample: return ModConfig.ExamBundling.WasteSamples;
                default: return false;
            }
        }

        // The vanilla CheckAnimationSetup of the examination scripts.
        private static AnimationSetup ResolveAnimationSetup(GameDBProcedure procedure, bool hospitalized)
        {
            if ((procedure.AnimationSetupLying != null && hospitalized) || procedure.AnimationSetupSitting == null)
                return procedure.AnimationSetupLying;

            return procedure.AnimationSetupSitting;
        }

        private static bool SameLoop(GameDBProcedure first, GameDBProcedure second, bool hospitalized)
        {
            AnimationSetup a = ResolveAnimationSetup(first, hospitalized);
            AnimationSetup b = ResolveAnimationSetup(second, hospitalized);
            return a != null && b != null && a.AnimationNameIdle == b.AnimationNameIdle && a.AnimationNameOut == b.AnimationNameOut;
        }

        /// <summary>Whether the examination can join the session the script is running now.</summary>
        public static bool CanJoin(ProcedureScript script, GameDBExamination examination)
        {
            GameDBExamination current = script.m_stateData.m_examination == null ? null : script.m_stateData.m_examination.Entry;
            ExamBundleGroup group = GetGroup(current);
            if (group == ExamBundleGroup.None || !IsEnabled(group) || examination == null || examination == current ||
                GetGroup(examination) != group)
            {
                return false;
            }

            ProcedureScene scene = script.m_stateData.m_procedureScene;
            Entity doctor = scene.Doctor == null ? null : scene.Doctor.GetEntity();
            Entity patientEntity = scene.m_patient == null ? null : scene.m_patient.GetEntity();
            if (doctor == null || patientEntity == null || doctor.GetComponent<BehaviorDoctor>() == null)
                return false;

            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            ProcedureScript reserved = component.m_state.m_reservedProcedureScript == null ? null : component.m_state.m_reservedProcedureScript.GetEntity();
            if (reserved != null && reserved.m_stateData.m_examination != null && reserved.m_stateData.m_examination.Entry == examination)
                return false;

            GameDBProcedure procedure = examination.Procedure;
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            if ((procedure.RequiredDoctorQualifications != null && !employee.HasAnySkill(procedure.RequiredDoctorQualifications)) ||
                (procedure.RequiredSkillsToPrescribe != null && !employee.HasAnySkill(procedure.RequiredSkillsToPrescribe)))
            {
                return false;
            }

            Room room = scene.m_room == null ? null : scene.m_room.GetEntity();
            if (room == null || !MapScriptInterface.Instance.IsRoomValidForExamination(room, examination))
                return false;

            GameDBProcedure currentProcedure = current.Procedure;
            if (procedure.TopProcedure != currentProcedure.TopProcedure || procedure.BottomProcedure != currentProcedure.BottomProcedure)
                return false;

            // The blood draw keeps its idle loop and ends with the last exam's out animation, so they must match.
            return group != ExamBundleGroup.BloodDraw ||
                SameLoop(currentProcedure, procedure, patientEntity.GetComponent<HospitalizationComponent>().IsHospitalized());
        }

        /// <summary>Index of the first planned examination that can join the running session, or -1.</summary>
        public static int FindNext(ProcedureScript script)
        {
            Entity patientEntity = script.m_stateData.m_procedureScene.m_patient.GetEntity();
            if (patientEntity == null)
                return -1;

            List<PlannedExaminationState> planned = patientEntity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_plannedExaminationStates;
            for (int index = 0; index < planned.Count; index++)
            {
                if (CanJoin(script, planned[index].m_examination.Entry))
                    return index;
            }

            return -1;
        }

        /// <summary>
        /// The finish block of vanilla ProcedureComponent.Update for the running exam, done now: recorded as
        /// finished, symptoms revealed, statistics counted, and the active exam cleared. Vanilla then skips it when
        /// the script ends.
        /// </summary>
        public static bool FinishCurrent(ProcedureScript script)
        {
            Entity patientEntity = script.m_stateData.m_procedureScene.m_patient.GetEntity();
            if (patientEntity == null)
                return false;

            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            ProcedureQueue queue = component.m_state.m_procedureQueue;
            GameDBExamination examination = script.m_stateData.m_examination.Entry;
            if (queue.m_activeExamination == null || queue.m_activeExamination.Entry != examination)
                return false;

            BehaviorPatient patient = patientEntity.GetComponent<BehaviorPatient>();
            ExaminationState finished = new ExaminationState();
            finished.m_examination = queue.m_activeExamination;
            queue.m_finishedExaminations.Add(finished);
            finished.m_discoveredSymptoms = patient.UncoverSymptomsFromLastExamination(script);
            component.m_state.m_lastProcedureID = examination.DatabaseID.ToString();
            queue.m_activeExamination = null;

            Department department = patient.GetDepartment();
            if (department != null)
                department.m_departmentPersistentData.m_todaysStatistics.m_examinations++;

            return true;
        }

        // What the script would have given at its own end for an exam that ends inside the session instead.
        private static void CreditFinished(ProcedureScript script, GameDBExamination examination, Entity doctor, BehaviorPatient patient)
        {
            string skillPoints = null;
            if (script is ProcedureScriptExaminationGeneralEquipment || script is ProcedureScriptExaminationUrineAnalysis)
                skillPoints = "TWEAKABLE_EXAMINATION_EQUIPMENT_SKILL_POINTS";

            if (skillPoints != null)
            {
                doctor.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(
                    examination.Procedure.RequiredDoctorQualifications,
                    Database.Instance.GetEntry<GameDBTweakableInt>(skillPoints).Value);
            }

            patient.ReceiveMessage(new Message(Messages.BOREDOM_REDUCED, BoredomReduction));
            if (examination == BloodTest && patient.m_state.m_controlMode == PatientControlMode.PlayerControl)
                PlayerStatistics.Instance.IncrementStatistic("STAT_EXM_BLOOD_DRAW");
        }

        /// <summary>Credits the finished exam, then makes the planned exam at index the running one.</summary>
        public static void SwitchTo(ProcedureScript script, int index, bool announce)
        {
            ProcedureScene scene = script.m_stateData.m_procedureScene;
            Entity doctor = scene.Doctor.GetEntity();
            Entity patientEntity = scene.m_patient.GetEntity();
            BehaviorPatient patient = patientEntity.GetComponent<BehaviorPatient>();
            CreditFinished(script, script.m_stateData.m_examination.Entry, doctor, patient);

            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            List<PlannedExaminationState> planned = component.m_state.m_procedureQueue.m_plannedExaminationStates;
            GameDBExamination examination = planned[index].m_examination.Entry;
            planned.RemoveAt(index);
            component.m_state.m_procedureQueue.m_activeExamination = examination;
            script.m_stateData.m_examination = examination;

            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            employee.SetReserved(examination.DatabaseID.ToString(), patientEntity);
            if (!doctor.GetComponent<PerkComponent>().m_perkSet.HasPerk("PERK_COMFORTING"))
                patientEntity.GetComponent<MoodComponent>().AddDiscomfortModifier(examination);

            if (script is ProcedureScriptExaminationGeneralEquipment)
            {
                float skill = employee.GetAnyApplicableSkillLevel(examination.Procedure.RequiredDoctorQualifications);
                script.SetParam(ParamEmployeeSkill, skill);
                script.SetParam(ParamProcedureTime, script.GetActionTime(doctor, ActionTimeConstant, skill));
            }

            if (announce)
            {
                script.SilenceCharacter(doctor);
                script.SpeakCharacter(doctor, "DIALOGUE_STAFF_ORDER_PATIENT", examination.IconIndex, -1f);
            }
        }

        /// <summary>The vanilla sample creation of the sampling scripts, for the running exam.</summary>
        public static void CreateSample(ProcedureScript script, Entity patientEntity)
        {
            LabProcedure procedure = LabProcedureManager.Instance.AddLabProcedure(
                patientEntity, script.m_stateData.m_examination.Entry, script.GetEquipment(1));
            patientEntity.GetComponent<BehaviorPatient>().AddLabProcedure(procedure);
        }

        /// <summary>
        /// Sampling scripts at the point where vanilla creates the sample: creates the samples of the running exam
        /// and every joinable planned one, leaving the last one running for vanilla to sample and end.
        /// </summary>
        public static void SampleAllButLast(ProcedureScript script, bool fromToilet)
        {
            Entity patientEntity = script.m_stateData.m_procedureScene.m_patient.GetEntity();
            if (patientEntity == null)
                return;

            int next;
            while ((next = FindNext(script)) >= 0)
            {
                CreateSample(script, patientEntity);
                if (fromToilet)
                    patientEntity.GetComponent<BehaviorPatient>().ReceiveMessage(new Message(Messages.BOREDOM_REDUCED, BoredomReduction));

                if (!FinishCurrent(script))
                    return;

                SwitchTo(script, next, false);
            }
        }
    }

    /// <summary>Blood draw: further blood samples continue the idle loop, one sample per exam.</summary>
    [HarmonyPatch(typeof(ProcedureScriptExaminationGeneralEquipment), "UpdateStateProcedureIdleAnimation")]
    public static class ProcedureScriptExaminationGeneralEquipment_UpdateStateProcedureIdleAnimation
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.BloodDraw, nameof(ProcedureScriptExaminationGeneralEquipment_UpdateStateProcedureIdleAnimation));

        public static bool Prefix(ProcedureScriptExaminationGeneralEquipment __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            if (data.m_timeInState <= __instance.GetParam(2) ||
                ExamBundling.GetGroup(data.m_examination == null ? null : data.m_examination.Entry) != ExamBundleGroup.BloodDraw)
            {
                return true;
            }

            int next = ExamBundling.FindNext(__instance);
            if (next < 0)
                return true;

            ExamBundling.CreateSample(__instance, data.m_procedureScene.m_patient.GetEntity());
            if (!ExamBundling.FinishCurrent(__instance))
                return true;

            ExamBundling.SwitchTo(__instance, next, true);
            data.m_timeInState = 0f;
            return false;
        }
    }

    /// <summary>Urine and stool, clinic: one toilet visit gives the samples of every joinable queued exam.</summary>
    [HarmonyPatch(typeof(ProcedureScriptExaminationUrineAnalysis), "UpdatePatientOnToilet")]
    public static class ProcedureScriptExaminationUrineAnalysis_UpdatePatientOnToilet
    {
        private const float ToiletSeconds = 5f;

        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.WasteSamples, nameof(ProcedureScriptExaminationUrineAnalysis_UpdatePatientOnToilet));

        public static bool Prefix(ProcedureScriptExaminationUrineAnalysis __instance)
        {
            if (__instance.m_stateData.m_timeInState > ToiletSeconds)
                ExamBundling.SampleAllButLast(__instance, true);

            return true;
        }
    }

    /// <summary>Urine and stool, in bed: one bedside visit gives the samples of every joinable queued exam.</summary>
    [HarmonyPatch(typeof(ProcedureScriptExaminationUrineAnalysis), "UpdateStateEmployeeUsingCabinet")]
    public static class ProcedureScriptExaminationUrineAnalysis_UpdateStateEmployeeUsingCabinet
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.ExamBundling.WasteSamples, nameof(ProcedureScriptExaminationUrineAnalysis_UpdateStateEmployeeUsingCabinet));

        public static bool Prefix(ProcedureScriptExaminationUrineAnalysis __instance)
        {
            Entity employee = __instance.m_stateData.m_procedureScene.EmployeeCharacter;
            if (employee == null || employee.GetComponent<UseComponent>().IsBusy() || __instance.GetParam(0) < 1f)
                return true;

            ExamBundling.SampleAllButLast(__instance, false);
            return true;
        }
    }
}
