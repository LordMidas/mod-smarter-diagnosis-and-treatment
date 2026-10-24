using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyTitle("MOD_SMARTER_DIAGNOSIS_AND_TREATMENT")] // ENTER MOD TITLE
[assembly: AssemblyFileVersion("1.0.0.0")] // MOD VERSION
[assembly: AssemblyVersion("1.2.2.548")] // GAME VERSION
namespace ModSmarterDiagnosisAndTreatment
{
    public class ModEntryPoint : MonoBehaviour // ModEntryPoint - RESERVED LOOKUP NAME
    {
        void Awake()
        {
            var assembly = GetType().Assembly;
            string modName = assembly.GetName().Name;
            string dir = System.IO.Path.GetDirectoryName(assembly.Location);

            Debug.Log($"Mod Init: {modName} ({dir})");

            // Must run before PatchAll: the patch classes read their toggles in Prepare().
            ModConfig.Load(dir);

            try
            {
                Debug.Log($"[{GetType().Assembly.GetName().Name}] : Booting up Harmony");
                var harmony = new Harmony("lordmidas.SmarterDiagnosisAndTreatment");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                var methods = harmony.GetPatchedMethods();
                foreach (var m in methods)
                {
                    Debug.Log($"[{GetType().Assembly.GetName().Name}] : Patched method: {m.DeclaringType.FullName}.{m.Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Assembly.GetName().Name}] : Harmony patch fail. Error : {ex}");
            }
        }
    }
}
