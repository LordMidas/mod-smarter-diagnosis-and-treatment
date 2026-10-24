using System;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Treatments for symptoms that carry a collapse risk, directly (CollapseProcedureRef) or through a
    /// CollapseSymptomRef chain. Hazard == High is not used: seven High symptoms have no collapse risk.
    /// </summary>
    internal static class CriticalTreatments
    {
        private const int MaxCollapseChainLength = 16;

        private static readonly Dictionary<GameDBSymptom, bool> collapseRiskCache =
            new Dictionary<GameDBSymptom, bool>();

        [ThreadStatic] private static List<PlannedTreatmentState> reorderBuffer;
        [ThreadStatic] private static List<int> rankBuffer;

        public static bool CarriesCollapseRisk(GameDBSymptom symptom)
        {
            if (symptom == null)
                return false;

            bool risk;
            if (collapseRiskCache.TryGetValue(symptom, out risk))
                return risk;

            risk = false;
            GameDBSymptom current = symptom;
            for (int step = 0; step < MaxCollapseChainLength && current != null; step++)
            {
                if (current.CollapseProcedureRef != null)
                {
                    risk = true;
                    break;
                }

                current = current.CollapseSymptomRef == null ? null : current.CollapseSymptomRef.Entry;
            }

            collapseRiskCache[symptom] = risk;
            return risk;
        }

        public static bool IsCriticalSymptom(Symptom symptom)
        {
            return symptom.m_active && symptom.m_spawned && !symptom.m_hidden &&
                CarriesCollapseRisk(symptom.m_symptom.Entry);
        }

        public static bool HasCriticalSymptom(MedicalCondition medicalCondition)
        {
            List<Symptom> symptoms = medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                if (IsCriticalSymptom(symptoms[index]))
                    return true;
            }

            return false;
        }

        public static bool IsCriticalTreatment(BehaviorPatient patient, GameDBTreatment treatment)
        {
            if (treatment.TreatmentType == TreatmentType.SURGERY ||
                treatment.TreatmentType == TreatmentType.HOSPITALIZATION)
            {
                return false;
            }

            List<Symptom> symptoms = patient.m_state.m_medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (!IsCriticalSymptom(symptom) || symptom.m_symptom.Entry.Treatments == null)
                    continue;

                DatabaseEntryRef<GameDBTreatment>[] treatments = symptom.m_symptom.Entry.Treatments;
                for (int treatmentIndex = 0; treatmentIndex < treatments.Length; treatmentIndex++)
                {
                    if (treatments[treatmentIndex].Entry == treatment)
                        return true;
                }
            }

            return false;
        }

        /// <summary>A critical symptom with no active or finished treatment, and optionally no planned one either.</summary>
        public static bool HasUntreatedCriticalSymptom(BehaviorPatient patient, bool ignorePlanned)
        {
            ProcedureComponent procedureComponent = patient.m_entity.GetComponent<ProcedureComponent>();
            List<Symptom> symptoms = patient.m_state.m_medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (!IsCriticalSymptom(symptom))
                    continue;

                GameDBSymptom entry = symptom.m_symptom.Entry;
                if (procedureComponent.HasTreatmentForSymptom(entry))
                    continue;

                if (ignorePlanned || !procedureComponent.HasPlannedTreatmentForSymptom(entry))
                    return true;
            }

            return false;
        }

        /// <summary>The first non-surgery, non-hospitalization treatment of the symptom.</summary>
        public static GameDBTreatment FindCriticalTreatment(GameDBSymptom symptom)
        {
            if (symptom.Treatments == null)
                return null;

            for (int index = 0; index < symptom.Treatments.Length; index++)
            {
                GameDBTreatment treatment = symptom.Treatments[index].Entry;
                if (treatment.TreatmentType != TreatmentType.SURGERY &&
                    treatment.TreatmentType != TreatmentType.HOSPITALIZATION)
                {
                    return treatment;
                }
            }

            return null;
        }

        /// <summary>
        /// Stable reorder of the planned treatments. Clinic mode (beforeSurgeryOnly false): hospitalizations required
        /// by critical treatments, then critical treatments, then the rest. Hospital mode: critical treatments move
        /// ahead of the first surgery and everything else keeps its order. Returns true when the queue changed.
        /// Does not allocate when the queue is already in order.
        /// </summary>
        public static bool MoveCriticalAhead(List<PlannedTreatmentState> queue, BehaviorPatient patient, bool beforeSurgeryOnly)
        {
            if (queue.Count < 2)
                return false;

            return beforeSurgeryOnly ? MoveAheadOfFirstSurgery(queue, patient) : MoveAheadOfAll(queue, patient);
        }

        public static bool IsSurgery(PlannedTreatmentState state)
        {
            return state.m_treatment.Entry.TreatmentType == TreatmentType.SURGERY;
        }

        private static bool MoveAheadOfAll(List<PlannedTreatmentState> queue, BehaviorPatient patient)
        {
            bool inOrder = true;
            int previousRank = 0;
            for (int index = 0; index < queue.Count; index++)
            {
                int rank = GetRank(queue, patient, queue[index].m_treatment.Entry);
                if (rank < previousRank)
                {
                    inOrder = false;
                    break;
                }

                previousRank = rank;
            }

            if (inOrder)
                return false;

            List<PlannedTreatmentState> buffer = GetReorderBuffer();
            List<int> ranks = GetRankBuffer();
            for (int index = 0; index < queue.Count; index++)
                ranks.Add(GetRank(queue, patient, queue[index].m_treatment.Entry));

            for (int rank = 0; rank <= 2; rank++)
            {
                for (int index = 0; index < queue.Count; index++)
                {
                    if (ranks[index] == rank)
                        buffer.Add(queue[index]);
                }
            }

            ReplaceContents(queue, buffer);
            ranks.Clear();
            return true;
        }

        private static bool MoveAheadOfFirstSurgery(List<PlannedTreatmentState> queue, BehaviorPatient patient)
        {
            int firstSurgery = -1;
            bool criticalBehindSurgery = false;
            for (int index = 0; index < queue.Count; index++)
            {
                GameDBTreatment treatment = queue[index].m_treatment.Entry;
                if (firstSurgery < 0)
                {
                    if (treatment.TreatmentType == TreatmentType.SURGERY)
                        firstSurgery = index;
                }
                else if (IsCriticalTreatment(patient, treatment))
                {
                    criticalBehindSurgery = true;
                    break;
                }
            }

            if (!criticalBehindSurgery)
                return false;

            List<PlannedTreatmentState> buffer = GetReorderBuffer();
            for (int index = 0; index < firstSurgery; index++)
                buffer.Add(queue[index]);

            for (int index = firstSurgery; index < queue.Count; index++)
            {
                if (IsCriticalTreatment(patient, queue[index].m_treatment.Entry))
                    buffer.Add(queue[index]);
            }

            for (int index = firstSurgery; index < queue.Count; index++)
            {
                if (!IsCriticalTreatment(patient, queue[index].m_treatment.Entry))
                    buffer.Add(queue[index]);
            }

            ReplaceContents(queue, buffer);
            return true;
        }

        // 0 = hospitalization needed by a critical treatment, 1 = critical treatment, 2 = anything else.
        private static int GetRank(List<PlannedTreatmentState> queue, BehaviorPatient patient, GameDBTreatment treatment)
        {
            if (IsCriticalTreatment(patient, treatment))
                return 1;

            if (treatment.TreatmentType == TreatmentType.HOSPITALIZATION && IsRequiredByCriticalTreatment(queue, patient, treatment))
                return 0;

            return 2;
        }

        private static bool IsRequiredByCriticalTreatment(List<PlannedTreatmentState> queue, BehaviorPatient patient, GameDBTreatment hospitalization)
        {
            for (int index = 0; index < queue.Count; index++)
            {
                GameDBTreatment other = queue[index].m_treatment.Entry;
                if (other.HospitalizationTreatmentRef != null &&
                    other.HospitalizationTreatmentRef.Entry == hospitalization &&
                    IsCriticalTreatment(patient, other))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<PlannedTreatmentState> GetReorderBuffer()
        {
            if (reorderBuffer == null)
                reorderBuffer = new List<PlannedTreatmentState>();

            reorderBuffer.Clear();
            return reorderBuffer;
        }

        private static List<int> GetRankBuffer()
        {
            if (rankBuffer == null)
                rankBuffer = new List<int>();

            rankBuffer.Clear();
            return rankBuffer;
        }

        private static void ReplaceContents(List<PlannedTreatmentState> queue, List<PlannedTreatmentState> buffer)
        {
            queue.Clear();
            queue.AddRange(buffer);
            buffer.Clear();
        }

        public static bool IsAvailable(FakeMap<GameDBTreatment, ProcedureSceneAvailability> availability, GameDBTreatment treatment)
        {
            return availability.ContainsKey(treatment) && ProcedureScene.IsProcedureAvailable(availability[treatment]);
        }

        // Mirrors the conditions under which vanilla BehaviorPatient.TryToStartScheduledTreatment starts a clinic treatment.
        public static bool CanStartInClinic(BehaviorPatient patient, ProcedureComponent procedureComponent, GameDBTreatment treatment)
        {
            StaffSelectionRules rules = treatment.Procedure.StaffSelectionRules;
            bool requiresRoom = rules == StaffSelectionRules.REQUIRED_ROOM ||
                rules == StaffSelectionRules.REQUIRED_ROOM_OR_DEFAULT_ROOM;
            bool usesDefaultRooms = rules == StaffSelectionRules.DEFAULT_ROOMS ||
                rules == StaffSelectionRules.REQUIRED_ROOM_OR_DEFAULT_ROOM;
            if (!requiresRoom && !usesDefaultRooms)
                return true;

            if (requiresRoom)
            {
                Room room = MapScriptInterface.Instance.GetRoomAt(patient.m_entity.GetComponent<WalkComponent>());
                if (procedureComponent.GetProcedureAvailabilty(
                        treatment.Procedure,
                        patient.m_entity,
                        patient.m_state.m_doctor.GetEntity(),
                        AccessRights.PATIENT_PROCEDURE,
                        room) == ProcedureSceneAvailability.AVAILABLE)
                {
                    return true;
                }
            }

            return usesDefaultRooms &&
                procedureComponent.GetProcedureAvailabilty(
                    treatment.Procedure,
                    patient.m_entity,
                    patient.GetDepartment(),
                    AccessRights.PATIENT_PROCEDURE) == ProcedureSceneAvailability.AVAILABLE;
        }

        /// <summary>Absolute in-game time in hours, independent of game speed.</summary>
        public static double GetGameHours()
        {
            return DayTime.Instance.GetDay() * 24.0 + DayTime.Instance.GetDayTimeHours();
        }
    }
}
