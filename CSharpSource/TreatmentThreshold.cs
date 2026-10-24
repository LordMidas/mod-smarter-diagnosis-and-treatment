using System.Collections.Generic;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>Which visible symptoms AI doctors treat beyond the treatments listed for the diagnosis.</summary>
    internal enum TreatmentThresholdLevel
    {
        DiagnosisRelevant,
        High,
        Medium,
        Low
    }

    /// <summary>
    /// Per-department treatment threshold of the current hospital. DepartmentPersistentData cannot take new fields, so
    /// the setting is stored in InsuranceManager's m_successfulEventsOfType (a string → int map that is saved and loaded
    /// with every game, replaced on a new game, and that vanilla only reads by specific event IDs). A department the
    /// player never set has no key and follows the config DefaultLevel. Every choice made in the UI is stored, so it
    /// survives a later change of DefaultLevel.
    /// </summary>
    internal static class TreatmentThreshold
    {
        private const string KeyPrefix = "LMSDT_TREATMENT_THRESHOLD:";
        // FakeMap returns 0 for a missing key, and DiagnosisRelevant is 0, so an explicit DiagnosisRelevant is stored
        // as this value. Saves from before the config only ever stored High/Medium/Low (1-3).
        private const int StoredDiagnosisRelevant = -1;

        private static readonly Dictionary<GameDBDepartment, string> keys = new Dictionary<GameDBDepartment, string>();

        public static TreatmentThresholdLevel Get(GameDBDepartment department)
        {
            FakeMap<string, int> store = GetStore();
            if (department == null || store == null)
                return ModConfig.Threshold.DefaultLevel;

            int value = store[GetKey(department)];
            if (value == StoredDiagnosisRelevant)
                return TreatmentThresholdLevel.DiagnosisRelevant;

            return value > (int)TreatmentThresholdLevel.DiagnosisRelevant && value <= (int)TreatmentThresholdLevel.Low
                ? (TreatmentThresholdLevel)value
                : ModConfig.Threshold.DefaultLevel;
        }

        public static void Set(GameDBDepartment department, TreatmentThresholdLevel level)
        {
            FakeMap<string, int> store = GetStore();
            if (department == null || store == null)
                return;

            store.Put(GetKey(department), level == TreatmentThresholdLevel.DiagnosisRelevant ? StoredDiagnosisRelevant : (int)level);
        }

        /// <summary>The lowest hazard that gets extra treatments, or null when only the diagnosis list applies.</summary>
        public static SymptomHazard? GetMinimumHazard(TreatmentThresholdLevel level)
        {
            switch (level)
            {
                case TreatmentThresholdLevel.High:
                    return SymptomHazard.High;
                case TreatmentThresholdLevel.Medium:
                    return SymptomHazard.Medium;
                case TreatmentThresholdLevel.Low:
                    return SymptomHazard.Low;
                default:
                    return null;
            }
        }

        /// <summary>Low = 1, Medium = 2, High = 3, anything else 0. SymptomHazard.Positive sorts above High, so a plain cast cannot be compared.</summary>
        public static int GetHazardRank(SymptomHazard hazard)
        {
            switch (hazard)
            {
                case SymptomHazard.Low:
                    return 1;
                case SymptomHazard.Medium:
                    return 2;
                case SymptomHazard.High:
                    return 3;
                default:
                    return 0;
            }
        }

        private static FakeMap<string, int> GetStore()
        {
            InsuranceManagerPersistentData state = InsuranceManager.Instance.m_state;
            if (state == null)
                return null;

            if (state.m_successfulEventsOfType == null)
                state.m_successfulEventsOfType = new FakeMap<string, int>();

            return state.m_successfulEventsOfType;
        }

        private static string GetKey(GameDBDepartment department)
        {
            string key;
            if (!keys.TryGetValue(department, out key))
            {
                key = KeyPrefix + department.DatabaseID.ToString();
                keys[department] = key;
            }

            return key;
        }
    }
}
