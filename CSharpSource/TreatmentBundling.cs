using System;
using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    // Vanilla plays a full animation cycle and a 5 s wait for every medication (RECEIPT) and every piece of advice
    // (PRESCRIPTION), and only chains through the planned treatment at index 0. These patches group the two types in
    // the queue and let each running script take every further item of its type inside one continuous session.

    internal static class TreatmentBundling
    {
        private const float OtherFloorPenalty = 1000f;
        private const float OrderChoiceSeconds = 1f;
        private const float OrderChoiceStaleSeconds = 30f;
        private const int OrderChoicePruneCount = 32;

        private sealed class OrderChoice
        {
            public float m_time;
            public TreatmentType m_type;
        }

        private static readonly Dictionary<Entity, OrderChoice> orderChoices = new Dictionary<Entity, OrderChoice>();

        [ThreadStatic] private static List<PlannedTreatmentState> receiptBuffer;
        [ThreadStatic] private static List<PlannedTreatmentState> prescriptionBuffer;
        [ThreadStatic] private static List<PlannedTreatmentState> otherBuffer;
        [ThreadStatic] private static List<PlannedTreatmentState> targetBuffer;

        public static bool IsBundleable(GameDBTreatment treatment)
        {
            return treatment.TreatmentType == TreatmentType.RECEIPT ||
                treatment.TreatmentType == TreatmentType.PRESCRIPTION;
        }

        public static bool CanJoinNow(BehaviorPatient patient, GameDBTreatment treatment)
        {
            if (!IsBundleable(treatment))
                return false;

            if (treatment.HospitalizationTreatmentRef == null)
                return true;

            HospitalizationComponent hospitalization = patient.m_entity.GetComponent<HospitalizationComponent>();
            if (!hospitalization.IsHospitalized())
                return false;

            if (treatment.AllowedWithAnyHospitalization)
                return true;

            GameDBTreatment current = hospitalization.GetHospitalizationTreatment();
            return current != null &&
                current.Procedure.HospitalizationLevel >= treatment.HospitalizationTreatmentRef.Entry.Procedure.HospitalizationLevel;
        }

        // A ward change or collapse-risk care that is not bundleable must never be jumped over.
        private static bool IsStopItem(BehaviorPatient patient, GameDBTreatment treatment)
        {
            if (treatment.TreatmentType == TreatmentType.HOSPITALIZATION)
                return true;

            return !IsBundleable(treatment) && CriticalTreatments.IsCriticalTreatment(patient, treatment);
        }

        // A symptom that needs treatment quickly: High hazard or collapse risk.
        private static bool IsUrgentSymptom(Symptom symptom)
        {
            if (!symptom.m_spawned || !symptom.m_active)
                return false;

            GameDBSymptom entry = symptom.m_symptom.Entry;
            return entry.Hazard == SymptomHazard.High || CriticalTreatments.CarriesCollapseRisk(entry);
        }

        private static bool ListsTreatment(DatabaseEntryRef<GameDBTreatment>[] treatments, GameDBTreatment treatment)
        {
            if (treatments == null)
                return false;

            for (int index = 0; index < treatments.Length; index++)
            {
                if (treatments[index].Entry == treatment)
                    return true;
            }

            return false;
        }

        /// <summary>A surgery the patient needs quickly: prioritised by the player, or treating an urgent symptom or the diagnosis behind one.</summary>
        public static bool IsUrgentSurgery(BehaviorPatient patient, GameDBTreatment treatment)
        {
            if (treatment.TreatmentType != TreatmentType.SURGERY)
                return false;

            if (patient.m_entity.GetComponent<HospitalizationComponent>().m_state.m_forcedSurgeryPriority > 0)
                return true;

            bool anyUrgentSymptom = false;
            List<Symptom> symptoms = patient.m_state.m_medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (!IsUrgentSymptom(symptom))
                    continue;

                anyUrgentSymptom = true;
                if (ListsTreatment(symptom.m_symptom.Entry.Treatments, treatment))
                    return true;
            }

            if (!anyUrgentSymptom)
                return false;

            GameDBPointer<GameDBMedicalCondition> diagnosed = patient.m_state.m_medicalCondition.m_diagnosedMedicalCondition;
            return diagnosed != null && ListsTreatment(diagnosed.Entry.Treatments, treatment);
        }

        /// <summary>A treatment that may go ahead of an urgent surgery: collapse-risk care or a treatment of an urgent symptom.</summary>
        public static bool IsUrgentTreatment(BehaviorPatient patient, GameDBTreatment treatment)
        {
            if (CriticalTreatments.IsCriticalTreatment(patient, treatment))
                return true;

            List<Symptom> symptoms = patient.m_state.m_medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (IsUrgentSymptom(symptom) && ListsTreatment(symptom.m_symptom.Entry.Treatments, treatment))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Index of the first planned joinable item of the type before any stop item, or -1. Behind an urgent surgery
        /// only urgent treatments qualify.
        /// </summary>
        public static int FindNext(ProcedureComponent component, BehaviorPatient patient, TreatmentType type)
        {
            List<PlannedTreatmentState> queue = component.m_state.m_procedureQueue.m_plannedTreatmentStates;
            bool pastBarrier = false;
            for (int index = 0; index < queue.Count; index++)
            {
                GameDBTreatment treatment = queue[index].m_treatment.Entry;
                if (treatment.TreatmentType == type && CanJoinNow(patient, treatment) &&
                    (!pastBarrier || IsUrgentTreatment(patient, treatment)))
                {
                    return index;
                }

                if (IsStopItem(patient, treatment))
                    return -1;

                if (!pastBarrier && IsUrgentSurgery(patient, treatment))
                    pastBarrier = true;
            }

            return -1;
        }

        /// <summary>Hospitalized patients: groups from the first joinable item. Run before the vanilla loop, never inside it.</summary>
        public static void GroupHospitalQueue(BehaviorPatient patient, List<PlannedTreatmentState> queue)
        {
            if (!ModConfig.Bundling.Enabled || queue.Count < 2)
                return;

            bool pastBarrier = false;
            for (int index = 0; index < queue.Count; index++)
            {
                GameDBTreatment treatment = queue[index].m_treatment.Entry;
                if (CanJoinNow(patient, treatment) && (!pastBarrier || IsUrgentTreatment(patient, treatment)))
                {
                    GroupQueue(queue, index, patient, IsDoctorFree(patient));
                    return;
                }

                if (!pastBarrier && IsUrgentSurgery(patient, treatment))
                    pastBarrier = true;
            }
        }

        // The check vanilla makes before it starts a medication or advice of a hospitalized patient.
        private static bool IsDoctorFree(BehaviorPatient patient)
        {
            if (patient.m_entity.GetComponent<HospitalizationComponent>().m_state.m_oustideRoom || patient.m_state.m_doctor == null)
                return false;

            Entity doctor = patient.m_state.m_doctor.GetEntity();
            if (doctor == null)
                return false;

            BehaviorDoctor behavior = doctor.GetComponent<BehaviorDoctor>();
            return behavior != null && behavior.IsFree();
        }

        /// <summary>
        /// Stable reorder of the items from start up to the first stop item: joinable items of the first type,
        /// joinable items of the other type, then everything else. An urgent surgery is a barrier: behind it only
        /// urgent treatments are joinable, the rest stays behind the surgery. Does not allocate when nothing changes.
        /// mayChooseOrder says whether the medication-or-advice order may be decided now (the doctor is free).
        /// </summary>
        public static bool GroupQueue(List<PlannedTreatmentState> queue, int start, BehaviorPatient patient, bool mayChooseOrder)
        {
            List<PlannedTreatmentState> receipts = GetCleared(ref receiptBuffer);
            List<PlannedTreatmentState> prescriptions = GetCleared(ref prescriptionBuffer);
            List<PlannedTreatmentState> others = GetCleared(ref otherBuffer);

            int stop = queue.Count;
            bool criticalReceipt = false;
            TreatmentType firstJoinableType = TreatmentType.NONE;
            bool pastBarrier = false;
            for (int index = 0; index < start; index++)
            {
                if (IsUrgentSurgery(patient, queue[index].m_treatment.Entry))
                {
                    pastBarrier = true;
                    break;
                }
            }

            for (int index = start; index < queue.Count; index++)
            {
                PlannedTreatmentState planned = queue[index];
                GameDBTreatment treatment = planned.m_treatment.Entry;
                if (IsStopItem(patient, treatment))
                {
                    stop = index;
                    break;
                }

                if (!CanJoinNow(patient, treatment) || (pastBarrier && !IsUrgentTreatment(patient, treatment)))
                {
                    others.Add(planned);
                    if (!pastBarrier && IsUrgentSurgery(patient, treatment))
                        pastBarrier = true;

                    continue;
                }

                if (firstJoinableType == TreatmentType.NONE)
                    firstJoinableType = treatment.TreatmentType;

                if (treatment.TreatmentType == TreatmentType.RECEIPT)
                {
                    receipts.Add(planned);
                    if (!criticalReceipt && CriticalTreatments.IsCriticalTreatment(patient, treatment))
                        criticalReceipt = true;
                }
                else
                {
                    prescriptions.Add(planned);
                }
            }

            bool changed = false;
            if (receipts.Count + prescriptions.Count >= 1 && stop - start >= 2)
            {
                TreatmentType first = firstJoinableType;
                bool both = receipts.Count > 0 && prescriptions.Count > 0;
                if (both)
                {
                    if (criticalReceipt)
                    {
                        first = TreatmentType.RECEIPT;
                    }
                    else if (mayChooseOrder)
                    {
                        if (patient.m_entity.GetComponent<HospitalizationComponent>().IsHospitalized())
                            first = ChooseFirstTypeThrottled(patient);
                        else if (!MatchesTarget(queue, start, stop, first, receipts, prescriptions, others))
                            first = ChooseFirstType(patient);
                    }
                }

                changed = !MatchesTarget(queue, start, stop, first, receipts, prescriptions, others);
                if (changed)
                {
                    List<PlannedTreatmentState> target = FillTarget(first, receipts, prescriptions, others);
                    for (int index = 0; index < target.Count; index++)
                        queue[start + index] = target[index];
                }
            }

            receipts.Clear();
            prescriptions.Clear();
            others.Clear();
            if (targetBuffer != null)
                targetBuffer.Clear();

            return changed;
        }

        private static bool MatchesTarget(
            List<PlannedTreatmentState> queue,
            int start,
            int stop,
            TreatmentType first,
            List<PlannedTreatmentState> receipts,
            List<PlannedTreatmentState> prescriptions,
            List<PlannedTreatmentState> others)
        {
            List<PlannedTreatmentState> target = FillTarget(first, receipts, prescriptions, others);
            if (target.Count != stop - start)
                return false;

            for (int index = 0; index < target.Count; index++)
            {
                if (queue[start + index] != target[index])
                    return false;
            }

            return true;
        }

        private static List<PlannedTreatmentState> FillTarget(
            TreatmentType first,
            List<PlannedTreatmentState> receipts,
            List<PlannedTreatmentState> prescriptions,
            List<PlannedTreatmentState> others)
        {
            List<PlannedTreatmentState> target = GetCleared(ref targetBuffer);
            if (first == TreatmentType.PRESCRIPTION)
            {
                target.AddRange(prescriptions);
                target.AddRange(receipts);
            }
            else
            {
                target.AddRange(receipts);
                target.AddRange(prescriptions);
            }

            target.AddRange(others);
            return target;
        }

        private static List<PlannedTreatmentState> GetCleared(ref List<PlannedTreatmentState> buffer)
        {
            if (buffer == null)
                buffer = new List<PlannedTreatmentState>();

            buffer.Clear();
            return buffer;
        }

        // Walking-cost evaluation at most once per second per patient: the vanilla loop runs every frame while it waits.
        private static TreatmentType ChooseFirstTypeThrottled(BehaviorPatient patient)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            Entity key = patient.m_entity;
            OrderChoice choice;
            if (orderChoices.TryGetValue(key, out choice))
            {
                if (now >= choice.m_time && now - choice.m_time < OrderChoiceSeconds)
                    return choice.m_type;
            }
            else
            {
                if (orderChoices.Count >= OrderChoicePruneCount)
                    PruneOrderChoices(now);

                choice = new OrderChoice();
                orderChoices[key] = choice;
            }

            choice.m_type = ChooseFirstType(patient);
            choice.m_time = now;
            return choice.m_type;
        }

        private static void PruneOrderChoices(float now)
        {
            List<Entity> stale = new List<Entity>();
            foreach (KeyValuePair<Entity, OrderChoice> pair in orderChoices)
            {
                if (now < pair.Value.m_time || now - pair.Value.m_time > OrderChoiceStaleSeconds)
                    stale.Add(pair.Key);
            }

            for (int index = 0; index < stale.Count; index++)
                orderChoices.Remove(stale[index]);
        }

        /// <summary>
        /// Which type goes first when both have joinable items: by walking cost from where the doctor is now.
        /// Falls back to RECEIPT when a spot cannot be found.
        /// </summary>
        public static TreatmentType ChooseFirstType(BehaviorPatient patient)
        {
            Entity doctor = patient.m_state.m_doctor == null ? null : patient.m_state.m_doctor.GetEntity();
            if (doctor == null)
                return TreatmentType.RECEIPT;

            WalkComponent doctorWalk = doctor.GetComponent<WalkComponent>();
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            Entity patientEntity = patient.m_entity;
            WalkComponent patientWalk = patientEntity.GetComponent<WalkComponent>();
            if (doctorWalk == null || employee == null || patientWalk == null)
                return TreatmentType.RECEIPT;

            bool hospitalized = patientEntity.GetComponent<HospitalizationComponent>().IsHospitalized();

            Vector2f workPlace;
            int workPlaceFloor;
            bool hasWorkPlace = TryGetWorkPlace(employee, out workPlace, out workPlaceFloor);

            Vector2f talkSpot;
            int talkFloor;
            bool hasTalkSpot;
            if (hospitalized)
            {
                talkSpot = patient.GetLyingInteractionPosition(LyingInteractionPositions.RIGHT);
                talkFloor = patientWalk.GetFloorIndex();
                hasTalkSpot = true;
            }
            else
            {
                talkSpot = workPlace;
                talkFloor = workPlaceFloor;
                hasTalkSpot = hasWorkPlace;
            }

            Vector2f medsStart = workPlace;
            int medsStartFloor = workPlaceFloor;
            bool hasMedsStart = hasWorkPlace;
            if (hospitalized)
            {
                Room room = MapScriptInterface.Instance.GetRoomAt(patientWalk);
                if (room == null)
                    room = employee.m_state.m_homeRoom.GetEntity();

                TileObject workstation = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(
                    patientEntity, null, patientWalk.GetCurrentTile(), room, "mobile_workstation", AccessRights.STAFF);
                if (workstation != null)
                {
                    medsStart = workstation.GetDefaultUsePosition();
                    medsStartFloor = workstation.GetFloorIndex();
                    hasMedsStart = true;
                }
            }

            Vector2f medsEnd = medsStart;
            int medsEndFloor = medsStartFloor;
            if (!hospitalized)
            {
                medsEnd = new Vector2f(patientWalk.GetCurrentTile());
                medsEndFloor = patientWalk.GetFloorIndex();
            }

            if (!hasTalkSpot || !hasMedsStart)
                return TreatmentType.RECEIPT;

            Vector2f doctorSpot = new Vector2f(doctorWalk.GetCurrentTile());
            int doctorFloor = doctorWalk.GetFloorIndex();

            float receiptFirst = Distance(doctorSpot, doctorFloor, medsStart, medsStartFloor) +
                Distance(medsEnd, medsEndFloor, talkSpot, talkFloor);
            float prescriptionFirst = Distance(doctorSpot, doctorFloor, talkSpot, talkFloor) +
                Distance(talkSpot, talkFloor, medsStart, medsStartFloor);

            return prescriptionFirst < receiptFirst ? TreatmentType.PRESCRIPTION : TreatmentType.RECEIPT;
        }

        private static bool TryGetWorkPlace(EmployeeComponent employee, out Vector2f position, out int floorIndex)
        {
            TileObject chair = employee.GetWorkChair();
            if (chair != null)
            {
                position = chair.GetDefaultUsePosition();
                floorIndex = chair.GetFloorIndex();
                return true;
            }

            if (employee.m_state.m_workDesk != null && employee.m_state.m_workDesk.GetEntity() != null)
            {
                TileObject desk = employee.m_state.m_workDesk.GetEntity();
                position = desk.GetDefaultUsePosition();
                floorIndex = desk.GetFloorIndex();
                return true;
            }

            position = new Vector2f(employee.m_state.m_workPlacePosition);
            floorIndex = employee.m_state.m_workPlaceFloorIndex;
            return employee.m_state.m_workPlacePosition != Vector2i.ZERO_VECTOR;
        }

        private static float Distance(Vector2f from, int fromFloor, Vector2f to, int toFloor)
        {
            float dx = from.m_x - to.m_x;
            float dy = from.m_y - to.m_y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            return fromFloor == toFloor ? distance : distance + OtherFloorPenalty;
        }

        /// <summary>Both characters are in place and the doctor's current animation has finished.</summary>
        public static bool IsReadyToStart(ProcedureScript script)
        {
            Entity doctor = script.m_stateData.m_procedureScene.Doctor.GetEntity();
            Entity patientEntity = script.m_stateData.m_procedureScene.m_patient.GetEntity();
            if (doctor == null || patientEntity == null)
                return false;

            if (doctor.GetComponent<WalkComponent>().IsBusy() || patientEntity.GetComponent<WalkComponent>().IsBusy())
                return false;

            UseComponent use = doctor.GetComponent<UseComponent>();
            if (use != null && use.IsBusy())
                return false;

            return doctor.GetComponent<AnimModelComponent>().IsIdle();
        }

        public static bool IsStandingAt(Entity entity, TileObject tileObject)
        {
            WalkComponent walk = entity.GetComponent<WalkComponent>();
            if (walk.GetFloorIndex() != tileObject.GetFloorIndex())
                return false;

            Vector2f spot = tileObject.GetDefaultUsePosition();
            Vector2f tile = new Vector2f(walk.GetCurrentTile());
            return Math.Abs(tile.m_x - spot.m_x) <= 1f && Math.Abs(tile.m_y - spot.m_y) <= 1f;
        }

        /// <summary>
        /// The doctor stands next to their own workstation (work chair, desk or the receipt equipment) and the patient
        /// is right behind it on the far side, straight across or one tile to the side along the workstation, with no
        /// wall in between. doctorDirection points from the doctor across the workstation.
        /// </summary>
        public static bool TryGetAcrossDesk(ProcedureScript script, Entity doctor, Entity patientEntity, out Direction doctorDirection)
        {
            doctorDirection = Direction.None;
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            TileObject chair = employee.GetWorkChair();
            TileObject desk = employee.m_state.m_workDesk == null ? null : employee.m_state.m_workDesk.GetEntity();
            TileObject equipment = script.GetEquipment(0);

            Vector2i doctorTile = doctor.GetComponent<WalkComponent>().GetCurrentTile();
            Vector2i patientTile = patientEntity.GetComponent<WalkComponent>().GetCurrentTile();
            int floorIndex = doctor.GetComponent<WalkComponent>().GetFloorIndex();
            MapScriptInterface map = MapScriptInterface.Instance;

            Vector2i[] directions = Vector2i.ALL_DIRECTIONS;
            for (int index = 0; index < directions.Length; index++)
            {
                Vector2i across = directions[index];
                Vector2i near = doctorTile + across;
                if (!IsWorkstationTile(near, chair, desk, equipment) || !map.IsAccessibleNoWalls(doctorTile, near, floorIndex))
                    continue;

                Vector2i side = new Vector2i(across.m_y, across.m_x);
                for (int step = -1; step <= 1; step++)
                {
                    Vector2i far = new Vector2i(near.m_x + side.m_x * step, near.m_y + side.m_y * step);
                    if (patientTile != far + across || !IsWorkstationTile(far, chair, desk, equipment))
                        continue;

                    if ((step != 0 && !map.IsAccessibleNoWalls(near, far, floorIndex)) ||
                        !map.IsAccessibleNoWalls(far, patientTile, floorIndex))
                    {
                        continue;
                    }

                    doctorDirection = ToDirection(across);
                    return true;
                }
            }

            return false;
        }

        private static bool IsWorkstationTile(Vector2i tile, TileObject chair, TileObject desk, TileObject equipment)
        {
            return (chair != null && chair.m_state.m_position == tile) ||
                (desk != null && desk.m_state.m_position == tile) ||
                (equipment != null && equipment.m_state.m_position == tile);
        }

        // Facing for a one-tile step, as in Vector2i.NW/NE/SE/SW (NW = +x, NE = +y).
        private static Direction ToDirection(Vector2i step)
        {
            if (step.m_x > 0)
                return Direction.NW;

            if (step.m_x < 0)
                return Direction.SE;

            return step.m_y > 0 ? Direction.NE : Direction.SW;
        }

        public static void AwardSkillPoints(ProcedureScript script, Entity doctor)
        {
            doctor.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(
                script.m_stateData.GetProcedure().RequiredDoctorQualifications,
                Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_TREATMENT_SKILL_POINTS").Value);
        }

        public static GameDBTreatment GetLatestActiveTreatment(ProcedureComponent component)
        {
            List<TreatmentState> active = component.m_state.m_procedureQueue.m_activeTreatmentStates;
            return active[active.Count - 1].m_treatment.Entry;
        }

        // The non-animation part of vanilla UpdateStateDoctorStartPrescribing for one more item.
        public static void ApplyReceiptStart(ProcedureScript script, BehaviorPatient patient, GameDBTreatment treatment)
        {
            Entity doctor = script.m_stateData.m_procedureScene.Doctor.GetEntity();
            script.SpeakCharacter(doctor, "DIALOGUE_STAFF_STATEMENT", treatment.IconIndex, -1f);
            patient.m_entity.GetComponent<MoodComponent>().AddDiscomfortModifier(treatment);

            HospitalizationComponent hospitalization = patient.m_entity.GetComponent<HospitalizationComponent>();
            if (!hospitalization.IsHospitalized())
                return;

            if (treatment.ReceiptType == ReceiptType.BLOOD_BAG)
                hospitalization.m_state.m_bloodBag = true;

            if (treatment.ReceiptType == ReceiptType.IV_BAG)
                hospitalization.m_state.m_fluidstandMedicine = true;

            if (treatment.ReceiptType == ReceiptType.OXYGEN_MASK)
                hospitalization.m_state.m_oxygenMask = true;
        }
    }

    /// <summary>
    /// Outpatients: group the queue right before vanilla reads the entry at treatmentIndex. Hospitalized patients are
    /// grouped before the vanilla loop in HospitalizationComponent_TryToStartScheduledTreatment, because that loop
    /// enumerates the list and would throw if the list changed under it.
    /// </summary>
    [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.TryToStartScheduledTreatment))]
    public static class BehaviorPatient_TryToStartScheduledTreatment
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.Enabled, nameof(BehaviorPatient_TryToStartScheduledTreatment));

        public static bool Prefix(BehaviorPatient __instance, int treatmentIndex)
        {
            ProcedureComponent component = __instance.m_entity.GetComponent<ProcedureComponent>();
            List<PlannedTreatmentState> queue = component.m_state.m_procedureQueue.m_plannedTreatmentStates;
            if (treatmentIndex < 0 || treatmentIndex >= queue.Count - 1)
                return true;

            if (!TreatmentBundling.IsBundleable(queue[treatmentIndex].m_treatment.Entry) ||
                __instance.m_entity.GetComponent<HospitalizationComponent>().IsHospitalized())
            {
                return true;
            }

            TreatmentBundling.GroupQueue(queue, treatmentIndex, __instance, true);
            return true;
        }
    }

    /// <summary>Advice: further advice items continue the talking loop instead of a new start/stop cycle.</summary>
    [HarmonyPatch(typeof(ProcedureScriptTreatmentPrescription), nameof(ProcedureScriptTreatmentPrescription.UpdateStateDoctorStopTalking))]
    public static class ProcedureScriptTreatmentPrescription_UpdateStateDoctorStopTalking
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.Enabled, nameof(ProcedureScriptTreatmentPrescription_UpdateStateDoctorStopTalking));

        private const float ItemSeconds = 5f;

        public static bool Prefix(ProcedureScriptTreatmentPrescription __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            if (data.m_timeInState <= ItemSeconds)
                return true;

            Entity doctor = data.m_procedureScene.Doctor.GetEntity();
            Entity patientEntity = data.m_procedureScene.m_patient.GetEntity();
            if (doctor == null || patientEntity == null)
                return true;

            BehaviorPatient patient = patientEntity.GetComponent<BehaviorPatient>();
            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            int next = TreatmentBundling.FindNext(component, patient, TreatmentType.PRESCRIPTION);
            if (next < 0)
                return true;

            component.SwitchToNextTreatment(next);
            TreatmentBundling.AwardSkillPoints(__instance, doctor);
            __instance.SpeakCharacter(
                doctor, "DIALOGUE_STAFF_STATEMENT", TreatmentBundling.GetLatestActiveTreatment(component).IconIndex, -1f);
            data.m_timeInState = 0f;
            return false;
        }
    }

    /// <summary>Medication: further medications continue the typing loop instead of sitting down again.</summary>
    [HarmonyPatch(typeof(ProcedureScriptTreatmentReceipt), nameof(ProcedureScriptTreatmentReceipt.UpdateStateDoctorStopPrescribing))]
    public static class ProcedureScriptTreatmentReceipt_UpdateStateDoctorStopPrescribing
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.Enabled, nameof(ProcedureScriptTreatmentReceipt_UpdateStateDoctorStopPrescribing));

        private const float AbandonSeconds = 30f;

        public static bool Prefix(ProcedureScriptTreatmentReceipt __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            Entity doctor = data.m_procedureScene.Doctor.GetEntity();
            Entity patientEntity = data.m_procedureScene.m_patient.GetEntity();
            if (doctor == null || patientEntity == null)
                return true;

            if (data.m_timeInState > AbandonSeconds || data.m_timeInState <= __instance.GetParam(1))
                return true;

            BehaviorPatient patient = patientEntity.GetComponent<BehaviorPatient>();
            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            int next = TreatmentBundling.FindNext(component, patient, TreatmentType.RECEIPT);
            if (next < 0)
                return true;

            component.SwitchToNextTreatment(next);
            TreatmentBundling.AwardSkillPoints(__instance, doctor);
            TreatmentBundling.ApplyReceiptStart(__instance, patient, TreatmentBundling.GetLatestActiveTreatment(component));
            data.m_timeInState = 0f;
            return false;
        }
    }

    /// <summary>Medication added just after the last boundary: sit back down through vanilla's own chain path.</summary>
    [HarmonyPatch(typeof(ProcedureScriptTreatmentReceipt), "UpdateStateDoctorPrepareReceipt")]
    public static class ProcedureScriptTreatmentReceipt_UpdateStateDoctorPrepareReceipt
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.Enabled, nameof(ProcedureScriptTreatmentReceipt_UpdateStateDoctorPrepareReceipt));

        private const float UseWaitSeconds = 5f;

        public static bool Prefix(ProcedureScriptTreatmentReceipt __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            Entity doctor = data.m_procedureScene.Doctor.GetEntity();
            Entity patientEntity = data.m_procedureScene.m_patient.GetEntity();
            if (doctor == null || patientEntity == null)
                return true;

            // IsIdle logs a warning while the use component is busy, so wait for it first. Vanilla gives up waiting
            // for the use component after the cap.
            UseComponent use = doctor.GetComponent<UseComponent>();
            if (use != null && use.IsBusy() && data.m_timeInState < UseWaitSeconds)
                return false;

            // Same wait as vanilla for the stand-up animation to finish.
            if (!doctor.GetComponent<AnimModelComponent>().IsIdle() && !doctor.GetComponent<EmployeeComponent>().IsFired())
                return true;

            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            int next = TreatmentBundling.FindNext(component, patientEntity.GetComponent<BehaviorPatient>(), TreatmentType.RECEIPT);
            if (next < 0)
                return true;

            component.SwitchToNextTreatment(next);
            ReturnToWorkstation(__instance, doctor, patientEntity, use);
            __instance.SwitchState(ProcedureScriptTreatmentReceipt.STATE_DOCTOR_START_PRESCRIBING);
            return false;
        }

        // On a ward, vanilla's stop-typing step interrupted the mobile workstation. Use it again so the late item is typed.
        private static void ReturnToWorkstation(
            ProcedureScriptTreatmentReceipt script,
            Entity doctor,
            Entity patientEntity,
            UseComponent use)
        {
            if (use == null || use.IsUsing() || script.GetParam(2) < 1f ||
                !patientEntity.GetComponent<HospitalizationComponent>().IsHospitalized())
            {
                return;
            }

            TileObject workstation = script.GetEquipment(1);
            if (workstation == null || (workstation.User != null && workstation.User != doctor) ||
                !TreatmentBundling.IsStandingAt(doctor, workstation))
            {
                return;
            }

            use.ReserveObject(workstation);
            use.Activate(UseComponentMode.UNTIL_INTERRUPTED);
        }
    }

    /// <summary>
    /// Medication: a doctor who already stands across their own desk or chair from the patient after writing or
    /// printing hands the receipt over across it, instead of vanilla's walk round to a tile next to the patient.
    /// Does the work of vanilla UpdateStateDoctorUsingPrinter and UpdateStateDoctorGoingToPatient without the walk.
    /// </summary>
    [HarmonyPatch(typeof(ProcedureScriptTreatmentReceipt), "UpdateStateDoctorUsingPrinter")]
    public static class ProcedureScriptTreatmentReceipt_UpdateStateDoctorUsingPrinter
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.HandOverAcrossDesk, nameof(ProcedureScriptTreatmentReceipt_UpdateStateDoctorUsingPrinter));

        public static bool Prefix(ProcedureScriptTreatmentReceipt __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            Entity doctor = data.m_procedureScene.Doctor.GetEntity();
            Entity patientEntity = data.m_procedureScene.m_patient.GetEntity();
            if (doctor == null || patientEntity == null || doctor.GetComponent<UseComponent>().IsBusy() ||
                patientEntity.GetComponent<HospitalizationComponent>().IsHospitalized())
            {
                return true;
            }

            WalkComponent doctorWalk = doctor.GetComponent<WalkComponent>();
            WalkComponent patientWalk = patientEntity.GetComponent<WalkComponent>();
            if (doctorWalk.IsBusy() || patientWalk.IsBusy() || doctorWalk.GetFloorIndex() != patientWalk.GetFloorIndex() ||
                MapScriptInterface.Instance.GetRoomAt(doctorWalk) != MapScriptInterface.Instance.GetRoomAt(patientWalk))
            {
                return true;
            }

            Direction direction;
            if (!TreatmentBundling.TryGetAcrossDesk(__instance, doctor, patientEntity, out direction))
                return true;

            __instance.SpeakCharacter(doctor, "DIALOGUE_STAFF_STATEMENT", "BUBBLE_PRESCRIPTION", -1f);
            AnimModelComponent doctorModel = doctor.GetComponent<AnimModelComponent>();
            doctorModel.SetDirection(direction);
            if (!patientWalk.IsSitting())
                patientEntity.GetComponent<AnimModelComponent>().SetDirection(direction.GetOppositeDirection());

            doctor.GetComponent<SpeechComponent>().PlayDialogue("DIALOGUE_STAFF_STATEMENT");
            doctorModel.PlayAnimation("stand_doc_give_receipt", looping: false);
            __instance.SwitchState(ProcedureScriptTreatmentReceipt.STATE_DOCTOR_GIVING_RECEIPT);
            return false;
        }
    }

    /// <summary>Advice added just after the last boundary: start talking again through vanilla's own chain path.</summary>
    [HarmonyPatch(typeof(ProcedureScriptTreatmentPrescription), "UpdateStateDoctorFinished")]
    public static class ProcedureScriptTreatmentPrescription_UpdateStateDoctorFinished
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.Enabled, nameof(ProcedureScriptTreatmentPrescription_UpdateStateDoctorFinished));

        private const float FinishedSeconds = 2f;
        private const float EarliestEndSeconds = 0.5f;

        public static bool Prefix(ProcedureScriptTreatmentPrescription __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            if (data.m_timeInState <= FinishedSeconds)
            {
                // The wait only lets the end animation play: stop waiting once it has finished.
                if (data.m_timeInState < EarliestEndSeconds || !TreatmentBundling.IsReadyToStart(__instance))
                    return true;

                data.m_timeInState = FinishedSeconds + 0.01f;
            }

            Entity patientEntity = data.m_procedureScene.m_patient.GetEntity();
            if (patientEntity == null || data.m_procedureScene.Doctor.GetEntity() == null)
                return true;

            ProcedureComponent component = patientEntity.GetComponent<ProcedureComponent>();
            int next = TreatmentBundling.FindNext(component, patientEntity.GetComponent<BehaviorPatient>(), TreatmentType.PRESCRIPTION);
            if (next < 0)
                return true;

            component.SwitchToNextTreatment(next);
            __instance.SwitchState(ProcedureScriptTreatmentPrescription.STATE_DOCTOR_START_TALKING);
            return false;
        }
    }

    /// <summary>Advice: the 5 s opening wait is pure pause once both characters are in place.</summary>
    [HarmonyPatch(typeof(ProcedureScriptTreatmentPrescription), nameof(ProcedureScriptTreatmentPrescription.UpdateStateDoctorStartTalking))]
    public static class ProcedureScriptTreatmentPrescription_UpdateStateDoctorStartTalking
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.Enabled, nameof(ProcedureScriptTreatmentPrescription_UpdateStateDoctorStartTalking));

        private const float EarliestSeconds = 0.5f;
        private const float VanillaSeconds = 5f;

        public static bool Prefix(ProcedureScriptTreatmentPrescription __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            if (data.m_timeInState >= EarliestSeconds && data.m_timeInState <= VanillaSeconds &&
                TreatmentBundling.IsReadyToStart(__instance))
            {
                data.m_timeInState = VanillaSeconds + 0.01f;
            }

            return true;
        }
    }

    /// <summary>Medication: the 5 s opening wait is pure pause once both characters are in place.</summary>
    [HarmonyPatch(typeof(ProcedureScriptTreatmentReceipt), nameof(ProcedureScriptTreatmentReceipt.UpdateStateDoctorStartPrescribing))]
    public static class ProcedureScriptTreatmentReceipt_UpdateStateDoctorStartPrescribing
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Bundling.Enabled, nameof(ProcedureScriptTreatmentReceipt_UpdateStateDoctorStartPrescribing));

        private const float EarliestSeconds = 0.5f;
        private const float VanillaSeconds = 5f;

        public static bool Prefix(ProcedureScriptTreatmentReceipt __instance)
        {
            ProcedureScriptPersistentData data = __instance.m_stateData;
            if (data.m_timeInState >= VanillaSeconds)
                return true;

            Entity doctor = data.m_procedureScene.Doctor.GetEntity();
            if (doctor == null)
                return true;

            // Vanilla's hospitalized branch would call IsIdle, which logs a warning while the workstation starts up.
            UseComponent use = doctor.GetComponent<UseComponent>();
            if (use != null && use.IsBusy())
                return false;

            if (data.m_timeInState >= EarliestSeconds && TreatmentBundling.IsReadyToStart(__instance))
                data.m_timeInState = VanillaSeconds + 0.01f;

            return true;
        }
    }
}
