# ModSmarterDiagnosisAndTreatment

General Project Hospital modding knowledge (paths, build constraints, conventions, vanilla engine facts, Harmony compatibility registry) is in the shared Addons CLAUDE.md and the `ph-*` skills. This file holds only what is specific to this mod.

Project Hospital mod (Harmony 2, net35) that makes diagnosis smarter for both NPC doctors and the player UI:
- Replaces the possible-diagnosis scoring with a log-likelihood model (`MedicalCondition.cs`, "Enhanced3").
- Filters and sorts the "available examinations" list by relevance, and makes NPC doctors pick exams by weighted relevance, doctor skill and exam Priority.
- Adds UI highlights (hover an exam, diagnosis, symptom or treatment and the related items light up), plus tooltip extensions.

## Build
- `dotnet build CSharpSource/ModSmarterDiagnosisAndTreatment.csproj` (or the VS Code `build` task). The output DLL is written to the **mod root** of this development folder (`D:\Modding\Project Hospital\ModSmarterDiagnosisAndTreatment`), not into the game. The game loads the mod from `...\StreamingAssets\Addons\ModSmarterDiagnosisAndTreatment`, so deploy with the VS Code `build and deploy` task (Ctrl+Shift+B), which runs `D:\Modding\Project Hospital\Mod Deployer\Deploy-Mod.ps1`. What is not copied is listed in `.deployignore`. Close the game first, it locks the DLL.

## Config
- `ModSmarterDiagnosisAndTreatment.Config.xml` in the mod root, shipped and player-editable. It is read once in `ModEntryPoint.Awake` before `PatchAll`, so every change needs a game restart. A missing file is regenerated with defaults.
- `ModConfig.cs` holds the single registry (section, key, default, range, player-facing description). It drives parsing, clamping and warnings, and the generated file. Code reads values through the nested accessors (`ModConfig.Scoring.*`, `ModConfig.Icu.*`, ...).
- Feature toggles are Harmony `static bool Prepare() => ModConfig.Patch(...)`. Exceptions are listed in the toggle table in [docs/PATCH_MAP.md](docs/PATCH_MAP.md).
- When adding or changing a setting, regenerate the shipped file from `ModConfig.WriteDefaultFile`, for example with a throwaway net8 console project that compiles `ModConfig.cs` plus stubs for `UnityEngine.Debug` and every mod type it references (currently the `TreatmentThresholdLevel` enum). The shipped file must match its output.

## Layout
- `ModSmarterDiagnosisAndTreatment/*.cs`: all code, in namespace `ModSmarterDiagnosisAndTreatment`.
- `Database/`: mod XML. `ModAddon.xml` is the addon ID. `ModStringTableEn.xml` holds the `MOD_LMSDT_*` strings. `ModExaminations.xml` is a **full override** of vanilla `EXM_EVALUATION` (Priority 45) and `EXM_DIFFERENTIAL_DIAGNOSIS` (Priority 31, just above the observation threshold of 30). Re-diff it against `ProjectHospital_Data/StreamingAssets/Database/Procedures/Examinations.xml` after game updates.

## Patch map
The full table (vanilla method, mod class, behaviour details, ordering against other mods) is in [docs/PATCH_MAP.md](docs/PATCH_MAP.md). Read the relevant rows before changing a patch, and update them with every patch change. Areas:
- **Diagnosis scoring:** `MedicalCondition` `UpdatePossibleDiagnoses` (patient context), `GetPossibleDiagnoses`, `IsClear`, `Diagnose` (`MedicalCondition.cs`).
- **Exam list and NPC exam choice:** `ProcedureComponent` `GetAllExaminationsForMedicalCondition`, `UpdateAllExaminationsForMedicalCondition`, `SelectExaminationForMedicalCondition`.
- **UI highlights and tooltips:** Diagnosis/Examinations/Symptoms/Treatment panel controllers and `TooltipBase.Update` (`HighlightPatches.cs`), `Tooltip*.UpdateData` (`Tooltip*.cs`).
- **Critical (collapse-risk) treatment:** `BehaviorPatient.SelectNextProcedure`, `HospitalizationComponent.TryToStartScheduledTreatment` / `UpdateStateBeingTreated`, `ProcedureComponent.PlanAllTreatments`.
- **ER observation:** `BehaviorPatient.ObservationCheck`.
- **Fast track:** `BehaviorPatient.UncoverSymptomsFromLastExamination` / `UncoverSymptomsFromLabExamination` (priority raise), `Room.SortQueue` (stable priority-then-arrival order), `BehaviorPatient.CheckNeeds` (skip needs while a collapse risk is untreated) in `FastTrack.cs`. No stored state: it uses the saved `m_priority` and the symptoms.
- **Vanilla fixes (no toggle):** lab-room part of `BehaviorPatient.SelectNextProcedure`; doctor-walks-to-call release `ProcedureScriptControlDoctorCallsPatient.UpdateStateDoctorReturning` plus the office wait in `BehaviorPatient.UpdateStateGoingToDoctor` (`DoctorCallsPatient.cs`); patient stand tile next to the work chair skips the paper holder/printer use tile, `Behavior.GetInteractionPosition` (`InteractionPosition.cs`).
- **Treatment threshold UI:** `DepartmentManagementLogisticsHybridController` `Init`/`UpdateCertaintyUI`/`UpdateLayout`/`Update` (`TreatmentThresholdUi.cs`). Ordered against Hospital Porters.
- **Treatment bundling:** `BehaviorPatient.TryToStartScheduledTreatment` and the `ProcedureScriptTreatmentPrescription` / `ProcedureScriptTreatmentReceipt` state methods (`TreatmentBundling.cs`). Receipt hand-over across the doctor's own desk or chair without walking: `ProcedureScriptTreatmentReceipt.UpdateStateDoctorUsingPrinter` (toggle `TreatmentBundling/HandOverAcrossDesk`).
- **Exam bundling:** blood draw `ProcedureScriptExaminationGeneralEquipment.UpdateStateProcedureIdleAnimation`, urine/stool `ProcedureScriptExaminationUrineAnalysis` `UpdatePatientOnToilet` / `UpdateStateEmployeeUsingCabinet` (`ExamBundling.cs`).
- **Keep undressed:** `AnimModelComponent.SetClothingLevel`, `ProcedureScript.SpeakCharacter` / `SwitchState`, `ProcedureComponent.StartExamination` / `StartTreatment` / `StartReservedProcedure`, `BehaviorPatient.SwitchState`, `WalkComponent.SetDestination` / `GoSit` (`KeepUndressed.cs`). Outpatients only.
- **ICU step-down:** `HospitalizationComponent` `SelectNextStep`/`SwitchState`, `CharacterPanelController` `IsAbleToChangeDepartments`/`OnDepartmentSelected`/`Update` (`HospitalizationComponent_*.cs`, `CharacterPanelControllerPatches.cs`, helper `IcuStepDown.cs`). Ordered against Hospital Care Level Transfer.

Core helpers:
- `ModDb`: cached DB entries.
- `ExaminationEquivalence`: lab sampling ↔ testing exams.
- `ExaminationRelevance`: the single definition of "exam X is relevant to diagnosis Y" = it reveals a not-yet-visible symptom of Y.
- `PatientUi` / `PatientPanelContext`: shared UI helpers and the scoped current patient.
- `HighlightColorSet`: tint/overlay with restore.
- `CriticalTreatments`: collapse-risk treatment logic, plus `IsAvailable` / `CanStartInClinic` shared with the threshold patch.
- `TreatmentThreshold`: per-department threshold store, kept in the save via `InsuranceManager.m_state.m_successfulEventsOfType` (keys `LMSDT_TREATMENT_THRESHOLD:<DPT_ID>`). A missing key means "never set" and follows the config `TreatmentThreshold/DefaultLevel`. Every UI choice is stored, with DiagnosisRelevant as -1 because FakeMap returns 0 for a missing key.
- `ExamBundling`: sampling groups (blood draw, urine/stool; data-driven, memoized), `CanJoin` / `FindNext`, `FinishCurrent` (the vanilla `ProcedureComponent.Update` finish block for a sample taken inside the session) and `SwitchTo` (the script and the active exam move to the next planned exam together). No stored state.
- `KeepUndressed`: `IsKeeping` (undressed outpatient, own doctor's exam needs FULL or the current level, so any clothing change would only dress them), `StaysWithDoctor`, `OnPatientWalks` (dresses a patient who walks during their own doctor's exam that does not need their current level: eye chart, toilet, mod exams; never undresses early), `Restore`. Stateless: the dress level is saved with the character.
- `IcuStepDown`: the "no longer needs the ICU" criteria (`GetBlockReason`), target level and bed search, and the transfer (`TryStepDown`), shared by the AI patches (`SwitchState` marks, `SelectNextStep` transfers) and the patient card patches. The cleared set is memory-only, so a save and load loses it until the next ICU check-up.
- `CriticalTreatments`: "critical" = a symptom with collapse risk (`CollapseProcedureRef`, or a `CollapseSymptomRef` chain leading to one), memoized. Not `Hazard == High`. Also the stable reorder `MoveCriticalAhead`.

## Pitfalls
- Patient-dependent tooltip text must use `PatientPanelContext.CurrentPatient`, which is null outside patient-card panels. Do not use a "last seen patient" static.
