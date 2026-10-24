using System;
using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Vanilla only releases an ICU patient after the full ICU stay and a completed treatment, and the player cannot
    /// move them earlier. A patient who no longer needs the ICU may move to the HDU (or a ward) of a department. The
    /// criteria live here, for both the automatic move of AI patients and the manual move from the patient card.
    /// </summary>
    internal static class IcuStepDown
    {
        public static float MinIcuHours => ModConfig.Icu.MinIcuHours;
        public const string ReasonNotDiagnosed = "MOD_LMSDT_ICU_NOT_DIAGNOSED";
        public const string ReasonHighHazard = "MOD_LMSDT_ICU_HIGH_HAZARD";
        public const string ReasonCollapsePending = "MOD_LMSDT_ICU_COLLAPSE_PENDING";
        public const string ReasonIcuOnlyCare = "MOD_LMSDT_ICU_ONLY_CARE";
        public const string ReasonHiddenRisk = "MOD_LMSDT_ICU_HIDDEN_RISK";
        public const string ReasonMinStay = "MOD_LMSDT_ICU_MIN_STAY";
        public const string ReasonReady = "MOD_LMSDT_ICU_STEP_DOWN_READY";
        public const string ReasonInvalidDepartment = "MOD_LMSDT_ICU_INVALID_DEPARTMENT";
        public const string ReasonNoHospitalization = "MOD_LMSDT_ICU_NO_HOSPITALIZATION";
        public const string ReasonNoFreeBed = "MOD_LMSDT_ICU_NO_FREE_BED";
        public const string ReasonNoTransport = "MOD_LMSDT_ICU_NO_TRANSPORT";
        public const string MessageMovedToHdu = "MOD_LMSDT_ICU_MOVED_HDU";
        public const string MessageMovedToWard = "MOD_LMSDT_ICU_MOVED_WARD";

        private const string TransportedReason = "TOOLTIP_SELECT_DEPARTMENT_TRANSPORTED";
        private const int ClearedPruneCount = 64;

        private static readonly HashSet<Entity> cleared = new HashSet<Entity>();
        private static readonly List<Entity> pruneBuffer = new List<Entity>();
        private static readonly string[] hospitalizationTags = { "hospitalization" };

        private static Func<HospitalizationComponent, Vehicle> getTransportVehicle;
        private static Action<HospitalizationComponent> resetHospitalization;

        private static string minStayTemplate;
        private static int minStayHours = -1;
        private static string minStayText;

        public static bool IsIcuPatient(BehaviorPatient patient)
        {
            Department department = patient.GetDepartment();
            if (department == null || department.GetDepartmentType() != ModDb.IcuDepartment)
                return false;

            GameDBTreatment hospitalized = patient.m_entity.GetComponent<HospitalizationComponent>().GetHospitalizationTreatment();
            return hospitalized != null && hospitalized == ModDb.IcuTreatment;
        }

        /// <summary>
        /// The localization ID of the first reason this patient still needs the ICU, or null when the patient may
        /// move. The automatic move also needs an idle patient, the manual one only a patient who is not in motion.
        /// </summary>
        public static string GetBlockReason(BehaviorPatient patient, bool automatic)
        {
            Entity entity = patient.m_entity;
            HospitalizationComponent hospitalization = entity.GetComponent<HospitalizationComponent>();
            ProcedureComponent procedureComponent = entity.GetComponent<ProcedureComponent>();
            HospitalizationState hospitalizationState = hospitalization.m_state.m_hospitalizationState;

            if (patient.m_state.m_deathTriggered ||
                hospitalizationState == HospitalizationState.Dead ||
                patient.m_state.m_patientState == PatientState.Collapsing ||
                hospitalizationState == HospitalizationState.Collapsing ||
                hospitalizationState == HospitalizationState.CollapsingInBed ||
                (hospitalizationState == HospitalizationState.BeingTreated && procedureComponent.IsPatientInSurgery()) ||
                hospitalization.IsBeingTransported() ||
                hospitalization.IsBeingStabilized())
            {
                return TransportedReason;
            }

            if (automatic &&
                (!hospitalization.IsFree() ||
                    procedureComponent.IsBusy() ||
                    procedureComponent.m_state.m_reservedProcedureScript.GetEntity() != null))
            {
                return TransportedReason;
            }

            MedicalCondition medicalCondition = patient.m_state.m_medicalCondition;
            if (medicalCondition.m_diagnosedMedicalCondition == null)
                return ReasonNotDiagnosed;

            if (medicalCondition.GetWorstKnownHazard(procedureComponent.m_state.m_procedureQueue) >= SymptomHazard.High)
                return ReasonHighHazard;

            if (patient.m_state.m_collapseSymptom.IsValid || patient.m_state.m_collapseProcedure.IsValid)
                return ReasonCollapsePending;

            if (NeedsIcuOnlyCare(procedureComponent.m_state.m_procedureQueue))
                return ReasonIcuOnlyCare;

            if (medicalCondition.GetNumberOfHiddenSymptoms() > 0 && HasUnseenDanger(medicalCondition))
                return ReasonHiddenRisk;

            if (GetElapsedHours(hospitalization) < MinIcuHours)
                return ReasonMinStay;

            return null;
        }

        public static float GetRemainingMinStayHours(BehaviorPatient patient)
        {
            float remaining = MinIcuHours - GetElapsedHours(patient.m_entity.GetComponent<HospitalizationComponent>());
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>The min-stay tooltip text, rebuilt only when the rounded-up hours or the template change.</summary>
        public static string GetMinStayText(BehaviorPatient patient)
        {
            int hours = (int)Math.Ceiling(GetRemainingMinStayHours(patient));
            if (hours < 1)
                hours = 1;

            string template = StringTable.GetInstance().GetLocalizedText(ReasonMinStay);
            if (minStayText == null || hours != minStayHours || !ReferenceEquals(template, minStayTemplate))
            {
                minStayHours = hours;
                minStayTemplate = template;
                minStayText = string.Format(template, hours);
            }

            return minStayText;
        }

        private static float GetElapsedHours(HospitalizationComponent hospitalization)
        {
            return DayTime.Instance.RealTimeSecondsToIngameTimeHours(hospitalization.m_state.m_elapsedHospitalizationTime);
        }

        // Vanilla GetSurgeryHospitalizationTreatment returns the current hospitalization for any treatment allowed
        // with any hospitalization, so in the ICU it would always report ICU.
        private static bool NeedsIcuOnlyCare(ProcedureQueue queue)
        {
            List<PlannedTreatmentState> planned = queue.m_plannedTreatmentStates;
            for (int index = 0; index < planned.Count; index++)
            {
                if (RequiresIcu(planned[index].m_treatment.Entry))
                    return true;
            }

            List<TreatmentState> active = queue.m_activeTreatmentStates;
            for (int index = 0; index < active.Count; index++)
            {
                if (RequiresIcu(active[index].m_treatment.Entry))
                    return true;
            }

            return false;
        }

        private static bool RequiresIcu(GameDBTreatment treatment)
        {
            return treatment.HospitalizationTreatmentRef != null &&
                !treatment.AllowedWithAnyHospitalization &&
                treatment.HospitalizationTreatmentRef.Entry.Procedure.HospitalizationLevel == HospitalizationLevel.ICU;
        }

        private static bool RequiresHighPriority(GameDBTreatment treatment)
        {
            return treatment.HospitalizationTreatmentRef != null &&
                !treatment.AllowedWithAnyHospitalization &&
                treatment.HospitalizationTreatmentRef.Entry.Procedure.HospitalizationLevel >= HospitalizationLevel.HIGH_PRIORITY;
        }

        // The player only sees the symptoms that are not hidden, so this looks at what the diagnosis could still show.
        private static bool HasUnseenDanger(MedicalCondition medicalCondition)
        {
            foreach (GameDBSymptomRules rule in medicalCondition.m_diagnosedMedicalCondition.Entry.Symptoms)
            {
                GameDBSymptom symptom = rule == null || rule.GameDBSymptomRef == null ? null : rule.GameDBSymptomRef.Entry;
                if (symptom == null ||
                    (symptom.Hazard != SymptomHazard.High && !CriticalTreatments.CarriesCollapseRisk(symptom)))
                {
                    continue;
                }

                if (!medicalCondition.HasVisibleSymptom(symptom))
                    return true;
            }

            return false;
        }

        public static GameDBTreatment GetTargetLevel(BehaviorPatient patient)
        {
            MedicalCondition medicalCondition = patient.m_state.m_medicalCondition;
            bool dontSendToHdu = medicalCondition.m_gameDBMedicalCondition.Entry.DontSendToHDU;
            if (dontSendToHdu && !medicalCondition.IsInfectious() && !NeedsHighPriority(patient))
                return ModDb.NormalTreatment;

            return ModDb.HighPriorityTreatment;
        }

        private static bool NeedsHighPriority(BehaviorPatient patient)
        {
            ProcedureQueue queue = patient.m_entity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue;
            for (int index = 0; index < queue.m_plannedTreatmentStates.Count; index++)
            {
                if (RequiresHighPriority(queue.m_plannedTreatmentStates[index].m_treatment.Entry))
                    return true;
            }

            for (int index = 0; index < queue.m_activeTreatmentStates.Count; index++)
            {
                if (RequiresHighPriority(queue.m_activeTreatmentStates[index].m_treatment.Entry))
                    return true;
            }

            return false;
        }

        public static bool IsValidTarget(Department department, out string reason)
        {
            reason = null;
            if (department == null)
            {
                reason = ReasonInvalidDepartment;
                return false;
            }

            GameDBDepartment type = department.GetDepartmentType();
            if (type == ModDb.IcuDepartment ||
                type == ModDb.EmergencyDepartment ||
                type == ModDb.RadiologyDepartment ||
                type == ModDb.LabDepartment ||
                (ModDb.PathologyDepartment != null && type == ModDb.PathologyDepartment) ||
                (ModDb.AdministrativeDepartment != null && type == ModDb.AdministrativeDepartment))
            {
                reason = ReasonInvalidDepartment;
                return false;
            }

            if (!department.HasWorkingHospitalization())
            {
                reason = ReasonNoHospitalization;
                return false;
            }

            return true;
        }

        public static TileObject FindFreeBed(BehaviorPatient patient, Department department, GameDBTreatment level)
        {
            WalkComponent walk = patient.m_entity.GetComponent<WalkComponent>();
            GameDBRoomType roomType = level == ModDb.HighPriorityTreatment
                ? ModDb.HighPriorityWardRoomType
                : ModDb.InpatientWardRoomType;
            return MapScriptInterface.Instance.FindClosestFreeObjectWithTags(
                walk.GetCurrentTile(), walk.GetFloorIndex(), department, hospitalizationTags, AccessRights.STAFF, roomType);
        }

        /// <summary>
        /// Moves the patient from the ICU to a free bed of the target department. Modeled on
        /// HospitalizationComponent.UpdateStateFindingNewBed, plus the department change. On failure nothing has
        /// changed and reason holds the localization ID.
        /// </summary>
        public static bool TryStepDown(BehaviorPatient patient, Department target, bool manual, out string reason)
        {
            if (!IsValidTarget(target, out reason))
                return false;

            Entity entity = patient.m_entity;
            HospitalizationComponent hospitalization = entity.GetComponent<HospitalizationComponent>();
            GameDBTreatment level = GetTargetLevel(patient);

            TileObject bed = FindFreeBed(patient, target, level);
            if (bed == null)
            {
                reason = ReasonNoFreeBed;
                return false;
            }

            if (!entity.GetComponent<StretcherComponent>().SetDestination(
                    bed,
                    LyingState.COVERED,
                    StretcherUse.RETURN_STRETCHER,
                    BedReservation.FREE_BED,
                    null,
                    patient.m_state.m_codeBlue ? MovementType.RUNNING : MovementType.WALKING,
                    GetTransportVehicle(hospitalization)))
            {
                reason = ReasonNoTransport;
                return false;
            }

            ResetHospitalization(hospitalization);
            // SwitchBed releases the old bed from hospitalization.m_state.m_bed, so it must run before SetHospitalized.
            hospitalization.SwitchBed(bed);
            hospitalization.SetHospitalized(level, bed);
            hospitalization.m_state.m_elapsedHospitalizationTime = 0f;
            hospitalization.m_state.m_prescribedHospitalizationTime = level.DurationHours;
            patient.ChangeDepartment(target, false, manual);
            hospitalization.SwitchState(HospitalizationState.BeingTransportedBackToRoom);

            ShowMessage(
                entity,
                string.Format(
                    StringTable.GetInstance().GetLocalizedText(level == ModDb.HighPriorityTreatment ? MessageMovedToHdu : MessageMovedToWard),
                    StringTable.GetInstance().GetLocalizedText(target.GetDepartmentType())),
                Color.white);
            return true;
        }

        public static void ShowReason(Entity entity, string reasonLocId, BehaviorPatient patient)
        {
            string text = reasonLocId == ReasonMinStay
                ? GetMinStayText(patient)
                : StringTable.GetInstance().GetLocalizedText(reasonLocId);
            ShowMessage(entity, text, Color.white);
        }

        private static void ShowMessage(Entity entity, string text, Color color)
        {
            NotificationManager.GetInstance().AddFloatingIngameNotification(entity, text, color);
        }

        /// <summary>A finished nurse check-up found this patient ready to leave the ICU. Held in memory only.</summary>
        public static void MarkCleared(Entity entity)
        {
            cleared.Add(entity);
            if (cleared.Count > ClearedPruneCount)
                PruneCleared();
        }

        public static void ClearMark(Entity entity)
        {
            cleared.Remove(entity);
        }

        public static bool IsCleared(Entity entity)
        {
            return cleared.Contains(entity);
        }

        private static void PruneCleared()
        {
            pruneBuffer.Clear();
            foreach (Entity entity in cleared)
            {
                BehaviorPatient patient = entity == null ? null : entity.GetComponent<BehaviorPatient>();
                if (patient == null || !IsIcuPatient(patient))
                    pruneBuffer.Add(entity);
            }

            for (int index = 0; index < pruneBuffer.Count; index++)
                cleared.Remove(pruneBuffer[index]);

            pruneBuffer.Clear();
        }

        /// <summary>Same test as vanilla SelectNextStep: the assigned doctor is on the current shift.</summary>
        public static bool IsDoctorOnDuty(BehaviorPatient patient)
        {
            return patient.m_state.m_doctor != null &&
                patient.m_state.m_doctor.GetEntity().GetComponent<EmployeeComponent>().m_state.m_shift == DayTime.Instance.GetShift();
        }

        private static Vehicle GetTransportVehicle(HospitalizationComponent hospitalization)
        {
            if (getTransportVehicle == null)
            {
                getTransportVehicle = (Func<HospitalizationComponent, Vehicle>)Delegate.CreateDelegate(
                    typeof(Func<HospitalizationComponent, Vehicle>),
                    AccessTools.Method(typeof(HospitalizationComponent), "GetTransportVehicle"));
            }

            return getTransportVehicle(hospitalization);
        }

        // Records the ICU stay as a finished hospitalization, so it is billed like vanilla's own ICU release.
        private static void ResetHospitalization(HospitalizationComponent hospitalization)
        {
            if (resetHospitalization == null)
            {
                resetHospitalization = (Action<HospitalizationComponent>)Delegate.CreateDelegate(
                    typeof(Action<HospitalizationComponent>),
                    AccessTools.Method(typeof(HospitalizationComponent), "ResetHospitalization"));
            }

            resetHospitalization(hospitalization);
        }
    }
}
