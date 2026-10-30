# ModSmarterDiagnosisAndTreatment

General Project Hospital modding knowledge (paths, build and deploy, conventions, Harmony compatibility registry) is in the shared CLAUDE.md, vanilla engine facts in the `ph-*` skills. This file holds only what is specific to this mod.

Last review: 528c5ed

Project Hospital mod (Harmony 2, net35, Harmony ID `lordmidas.SmarterDiagnosisAndTreatment`) that makes diagnosis and treatment smarter for NPC doctors and the player UI: log-likelihood diagnosis scoring, a relevance-filtered exam list and information-gain NPC exam choice, wrong-diagnosis correction, UI highlights and tooltips, collapse-risk treatment, fast track, treatment and exam bundling, ICU step-down, nurse bedside exams for hospitalized patients, plus some vanilla fixes.

## Build
- `dotnet build CSharpSource/ModSmarterDiagnosisAndTreatment.csproj` writes the DLL to this folder's root; deploy with Ctrl+Shift+B (`build and deploy`). `.deployignore` also excludes `/CSharpSource/`, `/tools/`, `docs/` and the dev files.

## Layout
- `CSharpSource/*.cs`: all code, namespace `ModSmarterDiagnosisAndTreatment`.
- `Database/ModAddon.xml`: addon ID `MOD_SMARTER_DIAGNOSIS_AND_TREATMENT`.
- `Database/ModLocalization/ModStringTable<Lang>.xml`: `MOD_LMSDT_*` strings, translated in 20 languages. A new string goes into every table.
- **Full overrides** (re-diff against vanilla after game updates):
  - `Database/ModExaminations.xml`: `EXM_EVALUATION` (Priority 45) and `EXM_DIFFERENTIAL_DIAGNOSIS` (Priority 31, just above the observation threshold of 30), vs `Database/Procedures/Examinations.xml`.
  - `Database/ModRoomTypes.xml`: `ROOM_TYPE_INPATIENT_WARD` and `ROOM_TYPE_HIGH_PRIORITY_INPATIENT_WARD` (exam tags plus the equipment those exams need), so basic exams happen in the ward instead of the patient going to the diagnostic unit; vs `Database/RoomTypes.xml`.
- `CSharpSource/NurseExamScripts/Lmsdt*.cs`: ports of `ProcedureScriptExaminationGeneral` / `GeneralEquipment` / `UrineAnalysis` (re-diff after game updates).
- `tools/ConfigStubs.cs`: stubs for the config regenerator (not compiled into the mod).

## Config
- `ModSmarterDiagnosisAndTreatment.Config.xml` in the mod root, shipped and player-editable, read once in `ModEntryPoint.Awake` before `PatchAll` (changes need a game restart; a missing file is regenerated with defaults). The pattern is described in `ph-new-mod` ("Player config file").
- `ModConfig.cs` is the single registry (section, key, default, range, description); code reads `ModConfig.Scoring.*`, `ModConfig.Icu.*`, ... Kinds: bool, int, float, choice and `'l'` (comma-separated list, e.g. `NurseExams/Examinations`).
- Feature toggles are `static bool Prepare() => ModConfig.Patch(...)`; the exceptions are in the toggle table of the patch map.
- After adding or changing a setting, regenerate the shipped file with the `ph-new-mod` ConfigRegen tool, passing `-p:ConfigStubs=tools/ConfigStubs.cs`. Keep the stubs in sync with every mod type `ModConfig.cs` references (currently the `TreatmentThresholdLevel` enum).

## Patch map
[docs/PATCH_MAP.md](docs/PATCH_MAP.md) lists every patch by feature section, with the config toggles at the end. Read the section you need before changing a patch, and update it with every patch change. Sections: diagnosis scoring, exam list and NPC exam choice, UI highlights and tooltips, wrong-diagnosis correction, critical treatment and ER observation, fast track, vanilla fixes, treatment threshold UI (ordered against Hospital Porters), treatment bundling (incl. receipt hand-over across the desk), exam bundling, keep undressed, ICU step-down (ordered against Hospital Care Level Transfer), nurse bedside exams.

## Core helpers
Mechanics are in the patch map; this is what each helper is for and what it stores.
- `ModDb`: cached DB entries. `ExaminationEquivalence`: lab sampling ↔ testing exams.
- `ExaminationRelevance`: the single definition of "exam X is relevant to diagnosis Y" (reveals a not-yet-visible symptom of Y). Drives the list filter, highlights and tooltips.
- `ExaminationInformation`: "how useful is exam X" (expected information gain) for the NPC choice and the list sort. Rescoring uses `SymptomEvidence` in `MedicalCondition.cs`; change the scoring there so both stay in step. Cached per `MedicalCondition`; loops over exams fetch the entry once with `Prepare`.
- `SymptomEvidence`: the scoring's per-rule update, shared by the scoring and `ExaminationInformation`.
- `PatientUi` / `PatientPanelContext`: shared UI helpers and the scoped current patient. `HighlightColorSet`: tint/overlay with restore.
- `CriticalTreatments`: "critical" = a symptom with collapse risk (`CollapseProcedureRef`, or a `CollapseSymptomRef` chain to one), memoized; not `Hazard == High`. Shared with the threshold patch.
- `TreatmentThreshold`: per-department threshold. **Saved** in `InsuranceManager.m_state.m_successfulEventsOfType` (keys `LMSDT_TREATMENT_THRESHOLD:<DPT_ID>`, DiagnosisRelevant = -1); a missing key follows the config `TreatmentThreshold/DefaultLevel`.
- `IcuStepDown`: the ICU step-down criteria and transfer, shared by the AI and patient card patches. Its cleared set is memory-only (lost on load until the next ICU check-up).
- Stateless (derive everything from saved vanilla state): `WrongDiagnosis`, `FastTrack`, `TreatmentBundling`, `ExamBundling`, `KeepUndressed`, `NurseExams`.

## Pitfalls
- Patient-dependent tooltip text must use `PatientPanelContext.CurrentPatient`, which is null outside patient-card panels. Do not use a "last seen patient" static.
- `HospitalizationComponent_TryToStartScheduledTreatment` is shared by treatment bundling (`GroupHospitalQueue`, always first) and critical treatment; keep that order when changing either feature.
- Code that reads an exam script's performer must not assume `m_procedureScene.Doctor` (null in nurse scenes); `ExamBundling.GetPerformer` handles both.
- `BehaviorPatient_SelectNextProcedure` (wrong diagnosis, lab-room fix, critical treatment) and `HospitalizationComponent_SelectNextStep` (wrong diagnosis, ICU step-down) each hold several features. A new feature on a method this mod already patches goes into the existing class, with its toggle checked inside.
