# ModSmarterDiagnosisAndTreatment

General Project Hospital modding knowledge (paths, build and deploy, conventions, Harmony compatibility registry) is in the shared CLAUDE.md, vanilla engine facts in the `ph-*` skills. This file holds only what is specific to this mod.

Project Hospital mod (Harmony 2, net35, Harmony ID `lordmidas.SmarterDiagnosisAndTreatment`) that makes diagnosis and treatment smarter for NPC doctors and the player UI: log-likelihood diagnosis scoring, a relevance-filtered exam list and information-gain NPC exam choice, wrong-diagnosis correction, UI highlights and tooltips, collapse-risk treatment, fast track, treatment and exam bundling, ICU step-down, nurse bedside exams for hospitalized patients, plus some vanilla fixes.

## Build
- `dotnet build CSharpSource/ModSmarterDiagnosisAndTreatment.csproj` writes the DLL to this folder's root; deploy with Ctrl+Shift+B (`build and deploy`). `.deployignore` also excludes `/CSharpSource/`, `/tools/`, `docs/` and the dev files.

## Layout
- `CSharpSource/*.cs`: all code, namespace `ModSmarterDiagnosisAndTreatment`.
- `Database/ModAddon.xml`: addon ID `MOD_SMARTER_DIAGNOSIS_AND_TREATMENT`.
- `Database/ModLocalization/ModStringTable<Lang>.xml`: `MOD_LMSDT_*` strings, translated in 20 languages. A new string goes into every table.
- `Database/ModExaminations.xml`: **full override** of vanilla `EXM_EVALUATION` (Priority 45) and `EXM_DIFFERENTIAL_DIAGNOSIS` (Priority 31, just above the observation threshold of 30). Re-diff against `Database/Procedures/Examinations.xml` after game updates.
- `tools/ConfigStubs.cs`: stubs for the config regenerator (not compiled into the mod).

## Config
- `ModSmarterDiagnosisAndTreatment.Config.xml` in the mod root, shipped and player-editable, read once in `ModEntryPoint.Awake` before `PatchAll` (changes need a game restart; a missing file is regenerated with defaults). The pattern is described in `ph-new-mod` ("Player config file").
- `ModConfig.cs` is the single registry (section, key, default, range, description); code reads `ModConfig.Scoring.*`, `ModConfig.Icu.*`, ...
- Feature toggles are `static bool Prepare() => ModConfig.Patch(...)`; the exceptions are in the toggle table of the patch map.
- After adding or changing a setting, regenerate the shipped file with the `ph-new-mod` ConfigRegen tool, passing `-p:ConfigStubs=tools/ConfigStubs.cs`. Keep the stubs in sync with every mod type `ModConfig.cs` references (currently the `TreatmentThresholdLevel` enum).

## Patch map
[docs/PATCH_MAP.md](docs/PATCH_MAP.md) lists every patch by feature section, with the config toggles at the end. Read the section you need before changing a patch, and update it with every patch change. Sections: diagnosis scoring, exam list and NPC exam choice, UI highlights and tooltips, wrong-diagnosis correction, critical treatment and ER observation, fast track, vanilla fixes, treatment threshold UI (ordered against Hospital Porters), treatment bundling (incl. receipt hand-over across the desk), exam bundling, keep undressed, ICU step-down (ordered against Hospital Care Level Transfer), nurse bedside exams.

## Core helpers
- `ModDb`: cached DB entries.
- `ExaminationEquivalence`: lab sampling ↔ testing exams.
- `ExaminationRelevance`: the single definition of "exam X is relevant to diagnosis Y" = it reveals a not-yet-visible symptom of Y. Drives the list filter, highlights and tooltips.
- `ExaminationInformation`: "how useful is exam X" for the NPC choice and the list sort = expected information gain as a share of the candidates' entropy, rescoring outcomes with `SymptomEvidence` (the scoring's per-rule update in `MedicalCondition.cs`; change the scoring there so both stay in step). Cached per `MedicalCondition`.
- `WrongDiagnosis`: drops an AI patient's discovered wrong diagnosis and the treatments only it called for, once its popup is closed. No stored state.
- `PatientUi` / `PatientPanelContext`: shared UI helpers and the scoped current patient. `HighlightColorSet`: tint/overlay with restore.
- `CriticalTreatments`: "critical" = a symptom with collapse risk (`CollapseProcedureRef`, or a `CollapseSymptomRef` chain leading to one), memoized; not `Hazard == High`. Also `MoveCriticalAhead` (stable reorder) and `IsAvailable` / `CanStartInClinic`, shared with the threshold patch.
- `TreatmentThreshold`: per-department threshold, saved in `InsuranceManager.m_state.m_successfulEventsOfType` (keys `LMSDT_TREATMENT_THRESHOLD:<DPT_ID>`). A missing key means "never set" and follows the config `TreatmentThreshold/DefaultLevel`, so every UI choice is stored, with DiagnosisRelevant as -1.
- `FastTrack`: priority raise, stable queue order, needs skipping. No stored state (uses the saved `m_priority` and the symptoms).
- `TreatmentBundling`: groups joinable advice / medication (`GroupHospitalQueue` for hospitalized patients) and `TryGetAcrossDesk` for the receipt hand-over.
- `ExamBundling`: sampling groups (blood draw, urine/stool; data-driven, memoized), `CanJoin` (also: a lab sample must be testable here, its testing exam's procedure scene in the department; else it stays planned) / `FindNext`, `FinishCurrent` (vanilla's `ProcedureComponent.Update` finish block for a sample taken inside the session) and `SwitchTo` (script and active exam move to the next planned exam together). No stored state.
- `KeepUndressed`: `IsKeeping`, `StaysWithDoctor`, `OnPatientWalks` (only ever dresses, never undresses early), `Restore`. Stateless: the dress level is saved with the character.
- `NurseExams`: listed exams of hospitalized patients are reserved with a nurses-station nurse through a memoized procedure copy (nurse qualification, `DEFAULT_ROOMS`, `Lmsdt*` nurse script). Doctor fallback when no qualified nurse exists, or when busy with `WaitForNurse` off. No stored state.
- `IcuStepDown`: the "no longer needs the ICU" criteria (`GetBlockReason`), target level and bed search, and the transfer (`TryStepDown`), shared by the AI patches and the patient card patches. The cleared set is memory-only, so a save and load loses it until the next ICU check-up.

## Pitfalls
- Patient-dependent tooltip text must use `PatientPanelContext.CurrentPatient`, which is null outside patient-card panels. Do not use a "last seen patient" static.
- `HospitalizationComponent_TryToStartScheduledTreatment` is shared by treatment bundling (`GroupHospitalQueue`, always first) and critical treatment; keep that order when changing either feature.
- `CSharpSource/NurseExamScripts/Lmsdt*.cs` are ports of vanilla exam scripts: re-diff them against `ProcedureScriptExaminationGeneral` / `GeneralEquipment` / `UrineAnalysis` after game updates. Code that reads an exam script's performer must not assume `m_procedureScene.Doctor` (null in nurse scenes); `ExamBundling.GetPerformer` handles both.
