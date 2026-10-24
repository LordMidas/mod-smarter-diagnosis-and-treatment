using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Gets clinic patients with a new, dangerous finding back to their doctor sooner. Nothing is stored: the
    /// raised priority lives in the vanilla (saved) m_priority, and "skip needs" is derived from the saved
    /// symptoms every time, so both survive save and load.
    /// Vanilla sets m_priority once at reception from the visible symptoms (GetWorstKnownHazard) and never
    /// recalculates it, so a hazard revealed later by an exam does not move the patient up the queue.
    /// </summary>
    internal static class FastTrack
    {
        // Same rule as reception, re-applied after a finding; a collapse risk counts as High even when the
        // symptom's own hazard is lower. Only ever raises, never touches Code Blue, which the player controls.
        public static void RaisePriority(BehaviorPatient patient)
        {
            BehaviorPatientStateData state = patient.m_state;
            if (!state.m_finishedAtReception || state.m_codeBlue || state.m_priority == SymptomHazard.Positive)
                return;

            if (patient.m_entity.GetComponent<HospitalizationComponent>().IsHospitalized())
                return;

            SymptomHazard target = patient.GetWorstKnownHazardIgnoreCodeBlue();
            if (target < SymptomHazard.High && CriticalTreatments.HasUntreatedCriticalSymptom(patient, true))
                target = SymptomHazard.High;

            // SetPriority also re-sorts the main queue of the patient's current waiting room.
            if (target > state.m_priority)
                patient.SetPriority(target);
        }

        // True while a clinic patient has a visible collapse-risk symptom without an active treatment
        // (planned but not started still counts).
        public static bool IsUrgent(BehaviorPatient patient)
        {
            BehaviorPatientStateData state = patient.m_state;
            if (!state.m_finishedAtReception || state.m_sentHome || state.m_deathTriggered)
                return false;

            if (!CriticalTreatments.HasCriticalSymptom(state.m_medicalCondition))
                return false;

            if (patient.m_entity.GetComponent<HospitalizationComponent>().IsHospitalized())
                return false;

            return CriticalTreatments.HasUntreatedCriticalSymptom(patient, true);
        }

        // With this many patients ahead in the waiting room's main queue there is time for a need. Leaving the
        // queue costs no place: UpdateStateFulfillingNeeds re-enqueues and SortQueue puts the patient back by
        // priority and arrival.
        public static bool HasTimeForNeeds(BehaviorPatient patient)
        {
            int allowance = ModConfig.FastTrack.NeedsQueueAllowance;
            if (allowance <= 0 || patient.m_state.m_waitingRoom == null)
                return false;

            Room room = patient.m_state.m_waitingRoom.GetEntity();
            if (room == null)
                return false;

            List<EntityIDPointer<Entity>> queue = room.m_roomPersistentData.m_characterQueue;
            for (int index = 0; index < queue.Count; index++)
            {
                if (queue[index] == patient.m_entity)
                    return index >= allowance;
            }

            return false;
        }

        // Stable insertion sort: priority first (vanilla tiers), then earlier hospital arrival, then the current
        // order. Vanilla's List.Sort compares priority only and is not stable. Queues are short.
        public static void SortQueue(List<EntityIDPointer<Entity>> queue)
        {
            for (int index = 1; index < queue.Count; index++)
            {
                EntityIDPointer<Entity> item = queue[index];
                BehaviorPatient itemPatient = GetPatient(item);
                int target = index - 1;
                while (target >= 0 && Compare(GetPatient(queue[target]), itemPatient) > 0)
                {
                    queue[target + 1] = queue[target];
                    target--;
                }

                queue[target + 1] = item;
            }
        }

        private static BehaviorPatient GetPatient(EntityIDPointer<Entity> pointer)
        {
            Entity entity = pointer == null ? null : pointer.GetEntity();
            return entity == null ? null : entity.GetComponent<BehaviorPatient>();
        }

        // Negative when a goes first. Non-patients (visitors) go last, as in vanilla.
        // m_fVisitDuration starts at 0 on arrival and grows for every waiting patient alike, so larger = earlier.
        private static int Compare(BehaviorPatient a, BehaviorPatient b)
        {
            if (a == null || b == null)
                return a == null ? (b == null ? 0 : 1) : -1;

            SymptomHazard priorityA = a.m_state.m_priority;
            SymptomHazard priorityB = b.m_state.m_priority;
            if (priorityA != priorityB)
                return priorityA > priorityB ? -1 : 1;

            float visitA = a.m_state.m_fVisitDuration;
            float visitB = b.m_state.m_fVisitDuration;
            if (visitA != visitB)
                return visitA > visitB ? -1 : 1;

            return 0;
        }
    }

    /// <summary>Raises the queue priority after an exam (any exam script, including reception and interview).</summary>
    [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.UncoverSymptomsFromLastExamination))]
    public static class BehaviorPatient_UncoverSymptomsFromLastExamination
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.FastTrack.PriorityAndQueue, nameof(BehaviorPatient_UncoverSymptomsFromLastExamination));

        public static void Postfix(BehaviorPatient __instance)
        {
            FastTrack.RaisePriority(__instance);
        }
    }

    /// <summary>Raises the queue priority when lab results come in (LabProcedure.Finish), usually while the patient waits.</summary>
    [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.UncoverSymptomsFromLabExamination))]
    public static class BehaviorPatient_UncoverSymptomsFromLabExamination
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.FastTrack.PriorityAndQueue, nameof(BehaviorPatient_UncoverSymptomsFromLabExamination));

        public static void Postfix(BehaviorPatient __instance)
        {
            FastTrack.RaisePriority(__instance);
        }
    }

    /// <summary>
    /// Replaces the waiting-room queue sort (main, lab and units queues) with <see cref="FastTrack.SortQueue"/>:
    /// higher risk first, first come first served within a risk level.
    /// </summary>
    [HarmonyPatch(typeof(Room), nameof(Room.SortQueue))]
    public static class Room_SortQueue
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.FastTrack.PriorityAndQueue, nameof(Room_SortQueue));

        public static bool Prefix(List<EntityIDPointer<Entity>> characterQueue)
        {
            FastTrack.SortQueue(characterQueue);
            return false;
        }
    }

    /// <summary>
    /// A patient with an untreated collapse risk stays in the queue instead of leaving it for a need (vanilla goes
    /// for any need above 50), unless a need reaches the safety valve or enough patients are ahead
    /// (<see cref="FastTrack.HasTimeForNeeds"/>). Covers every CheckNeeds call: walking to a chair, waiting sitting
    /// and waiting standing.
    /// </summary>
    [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.CheckNeeds))]
    public static class BehaviorPatient_CheckNeeds
    {
        // Vanilla acts on needs above this value.
        private const float VanillaNeedThreshold = 50f;

        private static bool Prepare() => ModConfig.Patch(ModConfig.FastTrack.SkipNeeds, nameof(BehaviorPatient_CheckNeeds));

        public static bool Prefix(BehaviorPatient __instance, ref bool __result, out List<KeyValuePair<Need, float>> __state)
        {
            __state = null;
            if (!FastTrack.IsUrgent(__instance) || FastTrack.HasTimeForNeeds(__instance))
                return true;

            MoodComponent mood = __instance.m_entity.GetComponent<MoodComponent>();
            if (mood == null)
                return true;

            // Only needs vanilla could act on (CheckNeeds goes through GetNeedsSortedFromMostCritical, which drops
            // needs without a procedure). Needs are capped at 100.
            float valve = ModConfig.FastTrack.NeedsSafetyValve;
            List<Need> needs = mood.m_state.m_needs;
            bool valveOpen = false;
            for (int index = 0; index < needs.Count; index++)
            {
                Need need = needs[index];
                if (need.m_currentValue >= valve && need.m_gameDBNeed.Entry.Procedure != null)
                    valveOpen = true;
            }

            if (!valveOpen)
            {
                __result = false;
                return false;
            }

            // Vanilla takes the most critical need above 50 that it can reach, so a valve opened by boredom would
            // also send the patient off for a hunger of 55. Hold the needs below the valve at 50 for this call so
            // only the ones over it can trigger; the Finalizer restores them.
            for (int index = 0; index < needs.Count; index++)
            {
                Need need = needs[index];
                if (need.m_currentValue > VanillaNeedThreshold && need.m_currentValue < valve)
                {
                    if (__state == null)
                        __state = new List<KeyValuePair<Need, float>>();

                    __state.Add(new KeyValuePair<Need, float>(need, need.m_currentValue));
                    need.m_currentValue = VanillaNeedThreshold;
                }
            }

            return true;
        }

        public static void Finalizer(List<KeyValuePair<Need, float>> __state)
        {
            if (__state == null)
                return;

            for (int index = 0; index < __state.Count; index++)
                __state[index].Key.m_currentValue = __state[index].Value;
        }
    }
}
