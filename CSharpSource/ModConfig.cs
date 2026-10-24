using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    // Player-editable settings, read once at startup from ModSmarterDiagnosisAndTreatment.Config.xml next to the DLL.
    // The registry below is the single source of defaults, ranges and descriptions: it drives parsing, the
    // default file written when the file is missing, and the shipped file. Keep the shipped file in sync
    // when adding or changing a setting.
    internal static class ModConfig
    {
        private const string LogPrefix = "[ModSmarterDiagnosisAndTreatment] Config: ";
        private const string FileName = "ModSmarterDiagnosisAndTreatment.Config.xml";
        private const string RootName = "SmarterDiagnosisAndTreatmentConfig";
        private const string RootVersion = "1";

        private sealed class Setting
        {
            public string Section;
            public string Key;
            public char Kind; // 'b', 'i', 'f' or 'c' (choice: IntValue indexes Options)
            public string[] Options;
            public bool BoolValue;
            public int IntValue;
            public float FloatValue;
            public bool BoolDefault;
            public int IntDefault;
            public float FloatDefault;
            public float Min;
            public float Max;
            public string Description;

            public string FormatValue(bool defaultValue)
            {
                switch (Kind)
                {
                    case 'b': return (defaultValue ? BoolDefault : BoolValue) ? "true" : "false";
                    case 'i': return (defaultValue ? IntDefault : IntValue).ToString(CultureInfo.InvariantCulture);
                    case 'c': return Options[defaultValue ? IntDefault : IntValue];
                    default: return (defaultValue ? FloatDefault : FloatValue).ToString(CultureInfo.InvariantCulture);
                }
            }

            public bool IsDefault()
            {
                switch (Kind)
                {
                    case 'b': return BoolValue == BoolDefault;
                    case 'i':
                    case 'c': return IntValue == IntDefault;
                    default: return FloatValue == FloatDefault;
                }
            }

            public string Comment()
            {
                string defaultText = "Default: " + FormatValue(true) + ".";
                if (Kind == 'b')
                    return " " + Description + " " + defaultText + " ";
                if (Kind == 'c')
                    return " " + Description + " Values: " + string.Join(", ", Options) + ". " + defaultText + " ";

                string range = "Range: " + Min.ToString(CultureInfo.InvariantCulture) + "-" +
                               Max.ToString(CultureInfo.InvariantCulture) + ".";
                return " " + Description + " " + range + " " + defaultText + " ";
            }
        }

        private static readonly List<Setting> All = new List<Setting>();

        private static Setting Add(string section, string key, bool defaultValue, string description)
        {
            Setting setting = new Setting
            {
                Section = section, Key = key, Kind = 'b', Description = description,
                BoolValue = defaultValue, BoolDefault = defaultValue
            };
            All.Add(setting);
            return setting;
        }

        private static Setting Add(string section, string key, int defaultValue, int min, int max, string description)
        {
            Setting setting = new Setting
            {
                Section = section, Key = key, Kind = 'i', Description = description,
                IntValue = defaultValue, IntDefault = defaultValue, Min = min, Max = max
            };
            All.Add(setting);
            return setting;
        }

        private static Setting Add(string section, string key, float defaultValue, float min, float max, string description)
        {
            Setting setting = new Setting
            {
                Section = section, Key = key, Kind = 'f', Description = description,
                FloatValue = defaultValue, FloatDefault = defaultValue, Min = min, Max = max
            };
            All.Add(setting);
            return setting;
        }

        private static Setting Add(string section, string key, string[] options, int defaultIndex, string description)
        {
            Setting setting = new Setting
            {
                Section = section, Key = key, Kind = 'c', Description = description, Options = options,
                IntValue = defaultIndex, IntDefault = defaultIndex
            };
            All.Add(setting);
            return setting;
        }

        // ---- Registry (order here is the order in the file) ----

        private static readonly Setting EnableDiagnostics = Add("General", "EnableDiagnostics", false,
            "Writes the loaded settings that differ from the defaults, and every feature switched off here, to the game log (Player.log).");

        private static readonly Setting ScoringEnabled = Add("DiagnosisScoring", "Enabled", true,
            "Uses this mod's likelihood-based diagnosis percentages instead of the vanilla ones. When off, the doctor panel and NPC doctors use vanilla percentages.");
        private static readonly Setting NegativeEvidenceWeight = Add("DiagnosisScoring", "NegativeEvidenceWeight", 0.2f, 0f, 1f,
            "How strongly a negative exam result (a symptom tested and not found) lowers a diagnosis's likelihood. 0 ignores negative results, 1 counts them as fully as positive ones.");
        private static readonly Setting MaxDelayedAbsentProbability = Add("DiagnosisScoring", "MaxDelayedAbsentProbability", 0.95f, 0f, 0.99f,
            "Caps how much a negative result counts for a symptom that may only appear later in the illness, since it can still show up after the exam. Lower values trust those negative results less. Only matters for mods that add late-appearing symptoms; vanilla and DLC diagnoses have none.");

        private static readonly Setting EarlyDiagnosisEnabled = Add("NpcDiagnosis", "EarlyDiagnosisEnabled", true,
            "Vanilla lets NPC doctors diagnose early, below the department's certainty level, whenever the leading diagnosis is 1.5 times as likely as the next one. This adds the checks below, so early diagnoses need real evidence. When off, the vanilla rule applies.");
        private static readonly Setting MinimumRelativeShare = Add("NpcDiagnosis", "MinimumRelativeShare", 66f, 0f, 100f,
            "Smallest percentage the leading diagnosis must have for an NPC doctor to diagnose early (or the department's certainty level, if that is higher). Higher values make early diagnoses rarer and safer.");
        private static readonly Setting MinimumTopRunnerUpRatio = Add("NpcDiagnosis", "MinimumTopRunnerUpRatio", 1.5f, 1f, 10f,
            "How many times more likely the leading diagnosis must be than the second one for an NPC doctor to diagnose early. 1.5 means 50% more likely.");
        private static readonly Setting MinimumVisibleSymptoms = Add("NpcDiagnosis", "MinimumVisibleSymptoms", 2, 1, 10,
            "How many of the leading diagnosis's symptoms the doctor must have seen before diagnosing early. A main symptom of the diagnosis counts as enough on its own.");

        private static readonly Setting ExamFilterEnabled = Add("ExaminationList", "FilterEnabled", true,
            "Hides examinations that cannot reveal anything new about the possible diagnoses from the available examinations list. When off, the vanilla list is shown.");
        private static readonly Setting ExamSortEnabled = Add("ExaminationList", "SortEnabled", true,
            "Sorts the available examinations list by how useful each exam is, most useful first. When off, the list keeps its vanilla order.");
        private static readonly Setting DifferentialDiagnosisMaxDiagnoses = Add("ExaminationList", "DifferentialDiagnosisMaxDiagnoses", 6, 2, 20,
            "The Differential Diagnosis exam only counts as useful while fewer than this many diagnoses are still possible. With more candidates NPC doctors do not pick it and it sorts to the bottom of the list.");
        private static readonly Setting DifferentialDiagnosisScorePerStep = Add("ExaminationList", "DifferentialDiagnosisScorePerStep", 16.67f, 0f, 100f,
            "How much more useful the Differential Diagnosis exam looks for every possible diagnosis fewer than the limit above. Higher values push it up the list sooner as candidates are ruled out.");

        private static readonly Setting NpcExamsEnabled = Add("NpcExamChoice", "Enabled", true,
            "Makes NPC doctors choose examinations by usefulness, doctor skill and exam priority. When off, they choose as in vanilla.");
        private static readonly Setting OfficeExamMultiplier = Add("NpcExamChoice", "OfficeExamMultiplier", 1.25f, 1f, 5f,
            "How much more NPC doctors favor exams they can do themselves in the office over lab tests and referrals. 1 means no preference.");
        private static readonly Setting DifferentialDiagnosisSpecialistBonus = Add("NpcExamChoice", "DifferentialDiagnosisSpecialistBonus", 0.25f, 0f, 2f,
            "Extra preference for the Differential Diagnosis exam, scaled by the doctor's advanced diagnosis skill. 0.25 makes a top specialist 25% more likely to pick it.");
        private static readonly Setting PriorityMultiplierStrength = Add("NpcExamChoice", "PriorityMultiplierStrength", 1f, 0f, 5f,
            "How strongly an exam's priority value steers NPC doctors towards it. 0 ignores priority, higher values make high-priority exams much more likely.");
        private static readonly Setting DeferLowPriorityExamsInEr = Add("NpcExamChoice", "DeferLowPriorityExamsInEr", true,
            "In the Emergency department, vanilla sends a patient to observation as soon as the doctor picks an exam with priority 30 or lower. With this on, NPC doctors skip those exams while a higher-priority useful exam is available, so patients are not sent to observation too early.");

        private static readonly Setting ClinicEnabled = Add("CriticalTreatment", "ClinicEnabled", true,
            "Clinic patients with a symptom that risks a collapse get that symptom's treatment before any exam or transfer.");
        private static readonly Setting ClinicBlockedWaitHours = Add("CriticalTreatment", "ClinicBlockedWaitHours", 1f, 0f, 12f,
            "How many in-game hours a clinic patient with a collapse risk waits for their treatment to become possible (for example while a room or staff member is busy) before the normal routine continues without it.");
        private static readonly Setting HospitalEnabled = Add("CriticalTreatment", "HospitalEnabled", true,
            "Hospitalized patients with a collapse risk have their critical treatments scheduled ahead of a queued surgery.");
        private static readonly Setting SurgeryHoldHours = Add("CriticalTreatment", "SurgeryHoldHours", 2f, 0f, 12f,
            "How many in-game hours a queued surgery is held back so a critical treatment can go first. After this wait the surgery is no longer delayed.");
        private static readonly Setting InSurgeryEnabled = Add("CriticalTreatment", "InSurgeryEnabled", true,
            "While a patient is on the operating table, the surgical team handles a pending collapse and treats collapse-risk symptoms right away, instead of waiting until the patient is back in bed.");

        private static readonly Setting FastTrackPriorityAndQueue = Add("FastTrack", "PriorityAndQueue", true,
            "A patient's queue priority rises when an exam or lab test reveals a more hazardous symptom (a collapse risk counts as High), and waiting rooms serve patients by priority first and then by arrival in the hospital. In vanilla, priority is set once at reception and patients with the same priority can swap places.");
        private static readonly Setting FastTrackSkipNeeds = Add("FastTrack", "SkipNeeds", true,
            "Clinic patients with a visible, untreated collapse-risk symptom stay in the queue instead of leaving it to eat, drink or use the toilet, so they reach their doctor sooner.");
        private static readonly Setting FastTrackNeedsSafetyValve = Add("FastTrack", "NeedsSafetyValve", 90, 51, 100,
            "With SkipNeeds on, a need at or above this level (out of 100) is still fulfilled; lower needs wait. Vanilla patients go for a need above 50.");
        private static readonly Setting FastTrackNeedsQueueAllowance = Add("FastTrack", "NeedsQueueAllowance", 4, 0, 30,
            "With SkipNeeds on, a patient with at least this many patients ahead in the waiting-room queue fulfils needs as usual, since there is time to get back, and returns to the same queue place. 0 turns this off.");

        private static readonly Setting SkipForOtherDepartment = Add("ErObservation", "SkipForOtherDepartment", true,
            "When the ER doctor diagnoses a condition that belongs to another department that is available, the patient goes there directly instead of being forced into ER observation first.");

        private static readonly Setting ThresholdEnabled = Add("TreatmentThreshold", "Enabled", true,
            "Adds a Treatment setting to each department's management window. Doctors then also plan a treatment for every untreated symptom at or above the chosen hazard level, without escalating care. When off, treatments follow the vanilla rules.");
        // Option order matches TreatmentThresholdLevel.
        private static readonly Setting ThresholdDefaultLevel = Add("TreatmentThreshold", "DefaultLevel",
            new[] { "DiagnosisRelevant", "High", "Medium", "Low" }, 2,
            "Threshold used by every department whose Treatment setting you have not changed in this hospital. DiagnosisRelevant treats only what the diagnosis lists (vanilla). High, Medium and Low also treat every other untreated symptom of at least that hazard.");

        private static readonly Setting BundlingEnabled = Add("TreatmentBundling", "Enabled", true,
            "Gives all of a patient's pending advice and medication treatments in one doctor visit, instead of one visit and animation per treatment.");

        private static readonly Setting IcuEnabled = Add("IcuStepDown", "Enabled", true,
            "Lets you move an ICU patient to the HDU or a ward from the patient card once they no longer need intensive care (diagnosed, no high-hazard or collapse risk, no ICU-only treatment, minimum stay done). In vanilla, ICU patients cannot be moved by hand.");
        private static readonly Setting IcuAutomatic = Add("IcuStepDown", "AutomaticForAiPatients", true,
            "AI-controlled ICU patients are moved to the HDU or a ward automatically after a nurse check-up finds they no longer need intensive care. Needs Enabled above. When off, only the manual move remains.");
        private static readonly Setting MinIcuHours = Add("IcuStepDown", "MinIcuHours", 8f, 0f, 72f,
            "How many in-game hours a patient must stay in the ICU before they can be moved to a lower care level.");

        private static readonly Setting ExamsOnDiagnosisHover = Add("UiHighlights", "ExamsOnDiagnosisHover", true,
            "Hovering a possible diagnosis highlights the available examinations that can reveal one of its hidden symptoms.");
        private static readonly Setting DiagnosesOnExamHover = Add("UiHighlights", "DiagnosesOnExamHover", true,
            "Hovering an examination highlights the possible diagnoses it can reveal a hidden symptom of.");
        private static readonly Setting SymptomsOnFinishedExamHover = Add("UiHighlights", "SymptomsOnFinishedExamHover", true,
            "Hovering a finished examination highlights the symptoms it revealed, and hovering a symptom highlights the finished examinations that revealed it.");
        private static readonly Setting TreatmentInteractions = Add("UiHighlights", "TreatmentInteractions", true,
            "Hovering a treatment highlights the symptoms and diagnoses it treats, and hovering a symptom or diagnosis highlights its treatments.");
        private static readonly Setting RoundedProbabilityLabels = Add("UiHighlights", "RoundedProbabilityLabels", true,
            "Shows diagnosis percentages as whole numbers, with <1% and >99% at the extremes. When off, the vanilla labels are shown.");
        private static readonly Setting TileTintStrength = Add("UiHighlights", "TileTintStrength", 0.2f, 0f, 1f,
            "How strongly a highlighted tile is tinted. 0 leaves it unchanged, 1 fills it with the highlight color.");
        private static readonly Setting IconTintStrength = Add("UiHighlights", "IconTintStrength", 0.45f, 0f, 1f,
            "How strongly a highlighted icon is tinted. 0 leaves it unchanged, 1 fills it with the highlight color.");

        private static readonly Setting TooltipExamRelevantDiagnoses = Add("Tooltips", "ExaminationRelevantDiagnoses", true,
            "Examination tooltips list the possible diagnoses the exam can help decide.");
        private static readonly Setting TooltipMaxListedDiagnoses = Add("Tooltips", "MaxListedDiagnoses", 7, 2, 30,
            "The most lines the diagnosis list in an examination tooltip shows. Longer lists end with [...].");
        private static readonly Setting TooltipDiagnosisDetails = Add("Tooltips", "DiagnosisDetails", true,
            "Diagnosis tooltips list the examinations that can reveal the diagnosis's symptoms, colored by whether they were done and found something.");
        private static readonly Setting TooltipSymptomDetails = Add("Tooltips", "SymptomDetails", true,
            "Symptom tooltips list the examinations that can reveal the symptom and mark the ones already done.");

        // ---- Typed accessors, grouped by file section ----

        public static class General
        {
            public static bool EnableDiagnostics => ModConfig.EnableDiagnostics.BoolValue;
        }

        public static class Scoring
        {
            public static bool Enabled => ScoringEnabled.BoolValue;
            public static float NegativeEvidenceWeight => ModConfig.NegativeEvidenceWeight.FloatValue;
            public static float MaxDelayedAbsentProbability => ModConfig.MaxDelayedAbsentProbability.FloatValue;
        }

        public static class NpcDiagnosis
        {
            public static bool EarlyDiagnosisEnabled => ModConfig.EarlyDiagnosisEnabled.BoolValue;
            public static float MinimumRelativeShare => ModConfig.MinimumRelativeShare.FloatValue;
            public static float MinimumTopRunnerUpRatio => ModConfig.MinimumTopRunnerUpRatio.FloatValue;
            public static int MinimumVisibleSymptoms => ModConfig.MinimumVisibleSymptoms.IntValue;
        }

        public static class ExamList
        {
            public static bool FilterEnabled => ExamFilterEnabled.BoolValue;
            public static bool SortEnabled => ExamSortEnabled.BoolValue;
            public static int DifferentialDiagnosisMaxDiagnoses => ModConfig.DifferentialDiagnosisMaxDiagnoses.IntValue;
            public static float DifferentialDiagnosisScorePerStep => ModConfig.DifferentialDiagnosisScorePerStep.FloatValue;
        }

        public static class NpcExams
        {
            public static bool Enabled => NpcExamsEnabled.BoolValue;
            public static float OfficeExamMultiplier => ModConfig.OfficeExamMultiplier.FloatValue;
            public static float DifferentialDiagnosisSpecialistBonus => ModConfig.DifferentialDiagnosisSpecialistBonus.FloatValue;
            public static float PriorityMultiplierStrength => ModConfig.PriorityMultiplierStrength.FloatValue;
            public static bool DeferLowPriorityExamsInEr => ModConfig.DeferLowPriorityExamsInEr.BoolValue;
        }

        public static class Critical
        {
            public static bool ClinicEnabled => ModConfig.ClinicEnabled.BoolValue;
            public static float ClinicBlockedWaitHours => ModConfig.ClinicBlockedWaitHours.FloatValue;
            public static bool HospitalEnabled => ModConfig.HospitalEnabled.BoolValue;
            public static float SurgeryHoldHours => ModConfig.SurgeryHoldHours.FloatValue;
            public static bool InSurgeryEnabled => ModConfig.InSurgeryEnabled.BoolValue;
        }

        public static class FastTrack
        {
            public static bool PriorityAndQueue => FastTrackPriorityAndQueue.BoolValue;
            public static bool SkipNeeds => FastTrackSkipNeeds.BoolValue;
            public static int NeedsSafetyValve => FastTrackNeedsSafetyValve.IntValue;
            public static int NeedsQueueAllowance => FastTrackNeedsQueueAllowance.IntValue;
        }

        public static class ErObservation
        {
            public static bool SkipForOtherDepartment => ModConfig.SkipForOtherDepartment.BoolValue;
        }

        public static class Threshold
        {
            public static bool Enabled => ThresholdEnabled.BoolValue;
            public static TreatmentThresholdLevel DefaultLevel => (TreatmentThresholdLevel)ThresholdDefaultLevel.IntValue;
        }

        public static class Bundling
        {
            public static bool Enabled => BundlingEnabled.BoolValue;
        }

        public static class Icu
        {
            public static bool Enabled => IcuEnabled.BoolValue;
            public static bool AutomaticForAiPatients => IcuAutomatic.BoolValue;
            public static float MinIcuHours => ModConfig.MinIcuHours.FloatValue;
        }

        public static class Highlights
        {
            public static bool ExamsOnDiagnosisHover => ModConfig.ExamsOnDiagnosisHover.BoolValue;
            public static bool DiagnosesOnExamHover => ModConfig.DiagnosesOnExamHover.BoolValue;
            public static bool SymptomsOnFinishedExamHover => ModConfig.SymptomsOnFinishedExamHover.BoolValue;
            public static bool TreatmentInteractions => ModConfig.TreatmentInteractions.BoolValue;
            public static bool RoundedProbabilityLabels => ModConfig.RoundedProbabilityLabels.BoolValue;
            public static float TileTintStrength => ModConfig.TileTintStrength.FloatValue;
            public static float IconTintStrength => ModConfig.IconTintStrength.FloatValue;
        }

        public static class Tooltips
        {
            public static bool ExaminationRelevantDiagnoses => TooltipExamRelevantDiagnoses.BoolValue;
            public static int MaxListedDiagnoses => TooltipMaxListedDiagnoses.IntValue;
            public static bool DiagnosisDetails => TooltipDiagnosisDetails.BoolValue;
            public static bool SymptomDetails => TooltipSymptomDetails.BoolValue;
        }

        // ---- Loading ----

        // Reads the config file next to the DLL, writing the default file first if it is missing. Never throws:
        // on any error the defaults stay in effect so patching still goes ahead.
        public static void Load(string modDir)
        {
            try
            {
                string path = Path.Combine(modDir, FileName);
                if (!File.Exists(path))
                {
                    WriteDefaultFile(path);
                    Debug.Log(LogPrefix + "created default " + FileName);
                }
                else
                {
                    Read(path);
                }

                if (General.EnableDiagnostics)
                    LogNonDefaultValues();
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + "failed, using defaults. " + ex);
            }
        }

        // For Harmony Prepare(): returns the toggle and, with diagnostics on, logs patches that are skipped.
        public static bool Patch(bool enabled, string name)
        {
            if (!enabled && General.EnableDiagnostics)
                Debug.Log(LogPrefix + "patch skipped (disabled): " + name);
            return enabled;
        }

        private static void Read(string path)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(path);
            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != RootName)
            {
                Debug.LogWarning(LogPrefix + "root element must be <" + RootName + ">, using defaults");
                return;
            }

            foreach (XmlNode sectionNode in root.ChildNodes)
            {
                XmlElement section = sectionNode as XmlElement;
                if (section == null)
                    continue;

                if (!HasSection(section.Name))
                {
                    Debug.LogWarning(LogPrefix + "unknown section <" + section.Name + ">, ignored");
                    continue;
                }

                foreach (XmlNode keyNode in section.ChildNodes)
                {
                    XmlElement element = keyNode as XmlElement;
                    if (element == null)
                        continue;

                    Setting setting = Find(section.Name, element.Name);
                    if (setting == null)
                    {
                        Debug.LogWarning(LogPrefix + "unknown setting <" + section.Name + "/" + element.Name + ">, ignored");
                        continue;
                    }

                    Apply(setting, element.InnerText.Trim());
                }
            }
        }

        private static bool HasSection(string name)
        {
            foreach (Setting setting in All)
            {
                if (setting.Section == name)
                    return true;
            }

            return false;
        }

        private static Setting Find(string section, string key)
        {
            foreach (Setting setting in All)
            {
                if (setting.Section == section && setting.Key == key)
                    return setting;
            }

            return null;
        }

        private static void Apply(Setting setting, string text)
        {
            string name = setting.Section + "/" + setting.Key;
            switch (setting.Kind)
            {
                case 'b':
                    if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                        setting.BoolValue = true;
                    else if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                        setting.BoolValue = false;
                    else
                        Debug.LogWarning(LogPrefix + name + " must be true or false, got '" + text + "'; using default " + setting.FormatValue(true));
                    break;

                case 'i':
                    int intValue;
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                    {
                        Debug.LogWarning(LogPrefix + name + " must be a whole number, got '" + text + "'; using default " + setting.FormatValue(true));
                        break;
                    }

                    int clampedInt = Math.Min(Math.Max(intValue, (int)setting.Min), (int)setting.Max);
                    if (clampedInt != intValue)
                        Debug.LogWarning(LogPrefix + name + " = " + intValue + " is out of range, clamped to " + clampedInt);
                    setting.IntValue = clampedInt;
                    break;

                case 'c':
                    int index = Array.FindIndex(setting.Options, option => string.Equals(option, text, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                        Debug.LogWarning(LogPrefix + name + " must be one of " + string.Join(", ", setting.Options) + ", got '" + text + "'; using default " + setting.FormatValue(true));
                    else
                        setting.IntValue = index;
                    break;

                default:
                    float floatValue;
                    if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue) ||
                        float.IsNaN(floatValue) || float.IsInfinity(floatValue))
                    {
                        Debug.LogWarning(LogPrefix + name + " must be a number, got '" + text + "'; using default " + setting.FormatValue(true));
                        break;
                    }

                    float clampedFloat = Math.Min(Math.Max(floatValue, setting.Min), setting.Max);
                    if (clampedFloat != floatValue)
                        Debug.LogWarning(LogPrefix + name + " = " + text + " is out of range, clamped to " + clampedFloat.ToString(CultureInfo.InvariantCulture));
                    setting.FloatValue = clampedFloat;
                    break;
            }
        }

        private static void LogNonDefaultValues()
        {
            int count = 0;
            foreach (Setting setting in All)
            {
                if (setting.IsDefault())
                    continue;

                count++;
                Debug.Log(LogPrefix + setting.Section + "/" + setting.Key + " = " + setting.FormatValue(false) +
                          " (default " + setting.FormatValue(true) + ")");
            }

            if (count == 0)
                Debug.Log(LogPrefix + "all settings at their defaults");
        }

        // Writes the default file. Also used to produce the shipped file, which must match this output.
        internal static void WriteDefaultFile(string path)
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                Encoding = new UTF8Encoding(false)
            };

            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteComment(" Smarter Diagnosis and Treatment settings. Changes apply on the next game launch. ");
                writer.WriteComment(" A mod update may replace this file, so keep a copy of your edits. Delete the file to regenerate the defaults. ");
                writer.WriteStartElement(RootName);
                writer.WriteAttributeString("Version", RootVersion);

                string currentSection = null;
                foreach (Setting setting in All)
                {
                    if (setting.Section != currentSection)
                    {
                        if (currentSection != null)
                            writer.WriteEndElement();
                        writer.WriteStartElement(setting.Section);
                        currentSection = setting.Section;
                    }

                    writer.WriteComment(setting.Comment());
                    writer.WriteElementString(setting.Key, setting.FormatValue(true));
                }

                if (currentSection != null)
                    writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }
    }
}
