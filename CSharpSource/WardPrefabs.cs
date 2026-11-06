using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// ModRoomTypes.xml makes regular and HDU wards require an equipment cabinet, a CRP scanner and a hand sanitizer.
    /// Several built-in ward prefabs lack them, so their layouts are replaced with the mod's own versions (in the mod's
    /// Prefabs folder) that contain them. Replaced per m_devPrefabs key (vanilla file name or DLC asset ID), so every
    /// prefab entry using that layout follows (e.g. the Infectious Diseases wards reuse the vanilla files), and a key
    /// of a DLC that is not installed is simply absent. The prefabs are saved in the vanilla orientation and size, and
    /// the build cost changes by the cost of the added items.
    /// </summary>
    internal static class WardPrefabs
    {
        private const string LogPrefix = "[ModSmarterDiagnosisAndTreatment] ";

        // m_devPrefabs key → the mod's prefab file.
        private static readonly string[][] Replacements =
        {
            new[] { "room_inpatient_ward_small.xml", "lmsdt_room_inpatient_ward_small.xml" },
            new[] { "room_inpatient_ward_large.xml", "lmsdt_room_inpatient_ward_large.xml" },
            new[] { "room_high_priority_ward_small.xml", "lmsdt_room_high_priority_ward_small.xml" },
            new[] { "ASSET_PREFAB_HDU_TRM_SMALL", "lmsdt_room_hdu_trm_small.xml" },
            new[] { "ASSET_PREFAB_HDU_TRM_LARGE", "lmsdt_room_hdu_trm_large.xml" },
        };

        private static readonly MethodInfo CostSetter = AccessTools.PropertySetter(typeof(GameDBPrefabObject), nameof(GameDBPrefabObject.Cost));
        private static readonly Dictionary<GameDBPrefabObject, int> OriginalCosts = new Dictionary<GameDBPrefabObject, int>();
        private static readonly HashSet<string> FailedFiles = new HashSet<string>();

        internal static string ModDirectory;

        public static void Apply(Dictionary<string, Prefab> devPrefabs)
        {
            if (devPrefabs == null || ModDirectory == null)
                return;

            GameDBPrefabObject[] entries = Database.Instance.GetEntries<GameDBPrefabObject>();
            foreach (string[] replacement in Replacements)
            {
                string key = replacement[0];
                Prefab vanilla;
                if (!devPrefabs.TryGetValue(key, out vanilla) || vanilla == null)
                    continue;

                Prefab ours = Load(replacement[1], vanilla);
                if (ours == null)
                    continue;

                int costDifference = ours.m_costEstimation - vanilla.m_costEstimation;
                Prefab northEast = ours.Rotate90CW();
                Prefab southEast = northEast.Rotate90CW();
                Prefab southWest = southEast.Rotate90CW();
                devPrefabs[key] = ours;

                foreach (GameDBPrefabObject entry in entries)
                {
                    if (entry.PrefabNW != key)
                        continue;

                    // Same keys and rotations as vanilla's FillInPrefabsForDirections.
                    devPrefabs[entry.PrefabNE] = northEast;
                    devPrefabs[entry.PrefabSE] = southEast;
                    devPrefabs[entry.PrefabSW] = southWest;

                    int originalCost;
                    if (!OriginalCosts.TryGetValue(entry, out originalCost))
                    {
                        originalCost = entry.Cost;
                        OriginalCosts[entry] = originalCost;
                    }

                    if (CostSetter != null && originalCost > 0)
                        CostSetter.Invoke(entry, new object[] { Math.Max(originalCost + costDifference, 0) });
                }
            }
        }

        // Loads the mod's prefab, saved in the vanilla prefab's orientation. Null (vanilla stays) if the file is missing,
        // references an entry this game does not have (vanilla's LoadPrefab then fails and logs it) or has another size.
        private static Prefab Load(string fileName, Prefab vanilla)
        {
            if (FailedFiles.Contains(fileName))
                return null;

            Prefab prefab = GameSave.LoadPrefab(Path.Combine(Path.Combine(ModDirectory, "Prefabs"), fileName));
            if (prefab != null &&
                (prefab.m_persistentData.m_size.m_x != vanilla.m_persistentData.m_size.m_x ||
                 prefab.m_persistentData.m_size.m_y != vanilla.m_persistentData.m_size.m_y))
            {
                prefab = null;
            }

            if (prefab == null)
            {
                FailedFiles.Add(fileName);
                Debug.LogWarning(LogPrefix + "could not use ward prefab " + fileName + ", the vanilla prefab is kept");
            }

            return prefab;
        }
    }

    /// <summary>Replaces the built-in ward layouts after vanilla has loaded the prefabs (see WardPrefabs).</summary>
    [HarmonyPatch(typeof(PrefabManager), nameof(PrefabManager.FillInPrefabsForDirections))]
    public static class PrefabManager_FillInPrefabsForDirections
    {
        public static void Postfix(PrefabManager __instance)
        {
            WardPrefabs.Apply(__instance.m_devPrefabs);
        }
    }
}
