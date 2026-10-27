# Patch map

Every Harmony patch in this mod, grouped by feature: the vanilla method, the mod class and what the mod does. Read only the section you need, and update it with every patch change. One row per vanilla method (or group of panel methods). Vanilla behaviour these patches rely on is in the `ph-engine-reference` skill; the config toggles are at the end.

## Diagnosis scoring (`MedicalCondition.cs`)

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `MedicalCondition.UpdatePossibleDiagnoses` | `MedicalCondition_UpdatePossibleDiagnosesPatientContext` | Exposes the patient to `GetPossibleDiagnoses` via `[ThreadStatic] DiagnosisPatientContext` |
| `MedicalCondition.GetPossibleDiagnoses` | `MedicalCondition_GetPossibleDiagnoses` | Log-likelihood scoring ("Enhanced3"), replaces vanilla |
| `MedicalCondition.IsClear(float,int)` | `MedicalCondition_IsClearWithCertaintyThreshold` | Uses `m_probabilityWeighted` |
| `MedicalCondition.Diagnose` | `MedicalCondition_Diagnose` | NPC diagnoses early only with enough positive evidence |

## Exam list and NPC exam choice

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `ProcedureComponent.GetAllExaminationsForMedicalCondition` | `ProcedureComponent_GetAllExaminationsForMedicalCondition` | Keeps only exams that reveal a hidden symptom of a possible diagnosis (`ExaminationRelevance`), plus interview, Differential Diagnosis and surgery complications. Clears stale availability during UpdateAll |
| `ProcedureComponent.UpdateAllExaminationsForMedicalCondition` | `ProcedureComponent_UpdateAllExaminationsForMedicalCondition` | Sorts the availability map by relevance. Its Prefix/Finalizer set `UpdatingComponent` for the filter above |
| `ProcedureComponent.SelectExaminationForMedicalCondition` | `ProcedureComponent_SelectExaminationForMedicalCondition` | NPC weighted exam choice (relevance, doctor skill, exam Priority); defers low-priority exams in the ER |

## UI highlights and tooltips (`HighlightPatches.cs`, `Tooltip*.cs`)

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `DiagnosisPanelController.UpdateDiagnoses`, `ExaminationsPanelController.UpdateExaminations` / `CheckTooltip` / `UnavailableTooltip`, `SymptomsPanelController.UpdateSymptoms`, `TreatmentPanelController.UpdateTreatments` / `CheckTooltip` / `ShowUnavailableTreatmentTooltip`, `TooltipBase.Update` | `HighlightPatches.cs` (one class per method, explicit order) | Sets `PatientPanelContext`, tracks hover and applies the highlights (`*Highlight.cs` helpers). Restore in the Prefix, apply in the Postfix |
| `TooltipExamination` / `TooltipDiagnosis` / `TooltipSymptom` `.UpdateData` | `Tooltip*.cs` | Tooltip extensions; patient-dependent text only through `PatientPanelContext.CurrentPatient` |

## Critical (collapse-risk) treatment and ER observation

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `BehaviorPatient.SelectNextProcedure` (private) | `BehaviorPatient_SelectNextProcedure` | First the lab-room vanilla fix (always on): a non-hospitalized patient in `GoingToDoctor` with `m_labSpecialist` set and a planned exam only gets `TryToStartScheduledExamination`. Then, with `ClinicEnabled`: AI clinic patients plan and start treatments for collapse-risk symptoms before any exam or transfer, and hold in the waiting room (about 1 h) instead of leaving untreated |
| `HospitalizationComponent.TryToStartScheduledTreatment` | `HospitalizationComponent_TryToStartScheduledTreatment` | First `TreatmentBundling.GroupHospitalQueue`. Then AI hospitalized: collapse-risk treatments go ahead of a queued surgery; cancels a surgery reservation and hides surgeries from the skip rule (Prefix `out __state` + Finalizer re-inserts them), for up to 2 h |
| `HospitalizationComponent.UpdateStateBeingTreated` (private) | `HospitalizationComponent_UpdateStateBeingTreated` | AI patient in surgery: resolves a pending collapse with the survival roll (anesthesiologist bonus, no collapse script) and gives collapse-risk treatments instantly |
| `ProcedureComponent.PlanAllTreatments` | `ProcedureComponent_PlanAllTreatments` | Postfix: AI doctors also plan one non-surgery, non-escalating treatment for each untreated visible symptom at or above the department's treatment threshold. Independent of `CriticalTreatments` |
| `BehaviorPatient.ObservationCheck` (private) | `BehaviorPatient_ObservationCheck` | At ER diagnosis, skips forced observation when the diagnosis belongs to another existing department, so the AI transfers instead |

## Fast track (`FastTrack.cs`)
No stored state: everything derives from vanilla's saved `m_priority` and the symptoms.

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `BehaviorPatient.UncoverSymptomsFromLastExamination`, `.UncoverSymptomsFromLabExamination` | `BehaviorPatient_UncoverSymptomsFromLastExamination`, `BehaviorPatient_UncoverSymptomsFromLabExamination` | Postfix: `FastTrack.RaisePriority` re-applies reception's rule (a visible untreated collapse risk counts as High) and raises `m_priority` via `SetPriority`. Only raises; skips Code Blue (`m_codeBlue` or `Positive`), hospitalized patients and patients not finished at reception. AI and player-controlled patients |
| `Room.SortQueue` | `Room_SortQueue` | Prefix, replaces vanilla: stable insertion sort of the main, lab and units queues by `m_priority`, then larger `m_fVisitDuration` (earlier arrival), then current order. Non-patients last |
| `BehaviorPatient.CheckNeeds` | `BehaviorPatient_CheckNeeds` | Acts while `FastTrack.IsUrgent`: finished at reception, not sent home, not hospitalized, a visible collapse-risk symptom without an active treatment (planned counts as untreated). Vanilla runs unchanged when `FastTrack.HasTimeForNeeds` (at least `NeedsQueueAllowance` patients ahead in the main queue; the stable sort restores the place on return). Otherwise Prefix sets `__result = false`, unless a need with a procedure is at or above `NeedsSafetyValve`; then vanilla runs with every other need above 50 held at 50 (Prefix `out __state`, Finalizer restores). Hospital Patient Life has a trace-only Postfix here |

## Vanilla fixes (no toggle)
The lab-room part of `BehaviorPatient_SelectNextProcedure` is in the critical treatment section.

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `ProcedureScriptControlDoctorCallsPatient.UpdateStateDoctorReturning` (private) | `ProcedureScriptControlDoctorCallsPatient_UpdateStateDoctorReturning` (`DoctorCallsPatient.cs`) | Ends the doctor-walks-to-call script as soon as the doctor turns back, so the patient follows at once. The doctor's `GoToWorkPlace` walk continues |
| `BehaviorPatient.UpdateStateGoingToDoctor` (private) | `BehaviorPatient_UpdateStateGoingToDoctor` (`DoctorCallsPatient.cs`) | Companion: a patient who reached the office (no lab specialist) waits while the assigned doctor is `Idle`, still walking and has this patient as `CurrentPatient`. Capped at 0.25 in-game hours of `m_timeInState` |
| `Behavior.GetInteractionPosition` | `Behavior_GetInteractionPosition` (`InteractionPosition.cs`) | Postfix: when the chosen stand tile is the use tile of an adjacent `receipt` object (paper holder, printer), takes the next free chair neighbour with vanilla's conditions instead. Keeps the vanilla result if no other tile qualifies |

## Treatment threshold UI (`TreatmentThresholdUi.cs`)

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `DepartmentManagementLogisticsHybridController.Init` (private) | `DepartmentManagementLogisticsHybridController_Init` | Postfix: builds the Treatment section (`TreatmentThresholdUi.Create`) and updates its row |
| `.UpdateCertaintyUI` | `..._UpdateCertaintyUI` | Postfix: updates the row to the active department's threshold |
| `.UpdateLayout` (private) | `..._UpdateLayout` | Prefix (priority 900) removes our offsets; Postfix (`[HarmonyAfter]` + `Priority.Last`) re-applies the layout |
| `.Update` | `..._Update` | Postfix (`[HarmonyAfter]` + `Priority.Last`): per-frame check that re-applies the layout if an adjusted element was reset |

**Section.** A "Treatment" section below the whole Certainty / Chief doctor block (its header starts where the vanilla `BottomPanel` started): a full-width header cloned from the vanilla certainty header bar (found by the text's `LocalizedTextController` ID; the bar is the text's parent if that has an `Image` and is not the panel or a container of the buttons), retitled with `MOD_LMSDT_TREAT_HEADER`, spanning from the Certainty bar to the Chief doctor bar. Under it the Diagnosis-relevant / Low / Medium / High threshold icons of the active department at vanilla size and spacing, centered on the header. Placed in world space.

**Layout pass.** First write the vanilla value back to every adjusted element, then measure and classify against the certainty row (above / below / spanning) in that pure vanilla state, then re-apply. The window grows from world positions: every container from the row up to the window grows with its top fixed, and each child is corrected for its own anchors (above the row: stays; below: moves down; large spanning ones: stretch). Tracked as base + offset so vanilla's `UpdateLayout` resets are re-applied once. Panel hierarchy and reset behaviour: `ph-engine-reference` ui.md.

**Hospital Porters** (`Loupito75:HospitalPorters`, see the shared registry): our `UpdateLayout` Prefix runs before its Prefix (800) to keep its snapshot clean, and our Postfixes go after its own. In ward departments it rewrites the layout every frame, so ours is re-applied every frame there.

## Treatment bundling (`TreatmentBundling.cs`)
Joinable items: advice (PRESCRIPTION) with advice, medication (RECEIPT) with medication. An urgent surgery is a barrier: behind it only urgent treatments are grouped or chained.

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `BehaviorPatient.TryToStartScheduledTreatment` | `BehaviorPatient_TryToStartScheduledTreatment` | Outpatients: Prefix groups the queue from `treatmentIndex` (all joinable items together, order by walking cost, collapse-risk medicine first). Never replaces vanilla. Hospitalized patients are grouped by `GroupHospitalQueue` from `HospitalizationComponent_TryToStartScheduledTreatment`, before its loop. Their medication-or-advice order is decided only while the assigned doctor `IsFree()` (throttled to once per second per patient), otherwise the current first type is kept |
| `ProcedureScriptTreatmentPrescription.UpdateStateDoctorStartTalking` | `ProcedureScriptTreatmentPrescription_UpdateStateDoctorStartTalking` | Prefix: skips the 5 s opening wait once both are in place and the doctor is idle (not before 0.5 s) |
| `ProcedureScriptTreatmentPrescription.UpdateStateDoctorStopTalking` | `ProcedureScriptTreatmentPrescription_UpdateStateDoctorStopTalking` | Prefix: after the item time, a further joinable advice item anywhere in the live queue continues the talking loop (new speech icon, skill points, timer reset, no animation change) |
| `ProcedureScriptTreatmentPrescription.UpdateStateDoctorFinished` (private) | `ProcedureScriptTreatmentPrescription_UpdateStateDoctorFinished` | Prefix: an advice item added after the last boundary goes back to `DOCTOR_START_TALKING`. Ends the 2 s wait early once both are in place and the doctor is idle (not before 0.5 s) |
| `ProcedureScriptTreatmentReceipt.UpdateStateDoctorStartPrescribing` | `ProcedureScriptTreatmentReceipt_UpdateStateDoctorStartPrescribing` | Prefix: the same opening skip for medication; while the doctor's use component is busy (workstation starting) it waits without calling `IsIdle` |
| `ProcedureScriptTreatmentReceipt.UpdateStateDoctorStopPrescribing` | `ProcedureScriptTreatmentReceipt_UpdateStateDoctorStopPrescribing` | Prefix: same loop for medication after the item's typing time (keeps vanilla's 30 s abandon check); applies the per-item nurse-delivery flags via `ApplyReceiptStart` |
| `ProcedureScriptTreatmentReceipt.UpdateStateDoctorPrepareReceipt` (private) | `ProcedureScriptTreatmentReceipt_UpdateStateDoctorPrepareReceipt` | Prefix: a medication added after the last boundary goes back to `DOCTOR_START_PRESCRIBING` before the receipt is printed or saved. Waits for the use component first, and on a ward re-reserves and re-activates the mobile workstation |
| `ProcedureScriptTreatmentReceipt.UpdateStateDoctorUsingPrinter` (private) | `ProcedureScriptTreatmentReceipt_UpdateStateDoctorUsingPrinter` | Hand-over across the desk (own toggle), outpatients. Prefix: when the doctor stands next to their own work chair, desk or receipt equipment and the patient is right behind it on the far side (straight across or one tile to the side along it, no walls; `TreatmentBundling.TryGetAcrossDesk`), the doctor turns across it, plays `stand_doc_give_receipt` and goes to `DOCTOR_GIVING_RECEIPT`, skipping the walk to the patient. Otherwise vanilla |

## Exam bundling (`ExamBundling.cs`)
Joinable exams belong to the same sampling group (blood draw, or urine/stool), data-driven and memoized.

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `ProcedureScriptExaminationGeneralEquipment.UpdateStateProcedureIdleAnimation` (private) | `ProcedureScriptExaminationGeneralEquipment_UpdateStateProcedureIdleAnimation` | Blood draw. Prefix: when the idle time is up and `ExamBundling.FindNext` finds a joinable queued exam, creates the running exam's lab sample, records it finished (`FinishCurrent`), moves the script and the queue's active exam to the next one (`SwitchTo`: skill points, reservation, discomfort, new time) and resets the timer, so the idle loop continues. The last exam ends through vanilla. Joinable also needs the same resolved idle and out animation |
| `ProcedureScriptExaminationUrineAnalysis.UpdatePatientOnToilet` (private) | `ProcedureScriptExaminationUrineAnalysis_UpdatePatientOnToilet` | Urine/stool, clinic. Prefix: once the toilet time is up, `SampleAllButLast` creates the samples of the running exam and every joinable queued one, leaving the last for vanilla, which then finishes with the shortened queue |
| `ProcedureScriptExaminationUrineAnalysis.UpdateStateEmployeeUsingCabinet` (private) | `ProcedureScriptExaminationUrineAnalysis_UpdateStateEmployeeUsingCabinet` | Urine/stool, in bed. Prefix: at the point where vanilla creates the sample, the same `SampleAllButLast` |

## Keep undressed (`KeepUndressed.cs`)
Outpatients only, stateless. `KeepUndressed.IsKeeping`: outpatient not at `GetTreatedSymptomDressLevel()` (undressed), current script is an exam by the patient's own doctor, and the exam needs FULL or exactly the current level. `Restore` = instant `SetClothingLevel(GetTreatedSymptomDressLevel())`, which the mod's own call bypasses in the `SetClothingLevel` patch.

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `AnimModelComponent.SetClothingLevel` | `AnimModelComponent_SetClothingLevel` | Prefix: skips a clothing change while `IsKeeping` (the re-dress after every exam, and the dress-up before a fully clothed exam). An exam needing other undressing is not kept |
| `ProcedureScript.SpeakCharacter(Entity, string, string, float)` | `ProcedureScript_SpeakCharacter` | Prefix: drops the `BUBBLE_DRESS` order while `IsKeeping` |
| `ProcedureScript.SwitchState` | `ProcedureScript_SwitchState` | Postfix: entering `PROCEDURE_PATIENT_DRESS` while `IsKeeping` skips the 3 s dress wait |
| `ProcedureComponent.StartExamination` (both overloads) | `ProcedureComponent_StartExamination_Staff` / `_Department` | Prefix: `Restore` unless the staff overload starts an exam by the patient's own doctor without nurse or lab specialist |
| `ProcedureComponent.StartTreatment` (both overloads), `StartReservedProcedure` | `ProcedureComponent_StartTreatment` (`TargetMethods`) | Prefix: `Restore`, so treatments start dressed and undress through their own scripts |
| `WalkComponent.SetDestination` (both overloads), `WalkComponent.GoSit` | `WalkComponent_Walk` | Prefix, patients only: `OnPatientWalks` restores when an undressed outpatient starts walking during their own doctor's exam and the current level is not the one the exam needs (eye chart, toilet, mod exams; or an exam needing other undressing, which undresses on arrival as in vanilla). Only ever dresses. Treatment and collapse scripts are ignored |
| `BehaviorPatient.SwitchState` | `BehaviorPatient_SwitchState` | Prefix: keeps the old state. Postfix: `Restore` on every new state except `GoingToDoctor`, `BeingExamined`, `BeingTreated` (still with the doctor) and `GoingToCollapse`, `Collapsing`, `Dead` (those set the clothing themselves). `OverriddenByHospitalization` dresses (`OnHospitalizedFromDoctor`, bypassing `Restore`'s hospitalized check, as vanilla's `CheckHospitalization` calls `SetHospitalized` first) only when coming from `GoingToDoctor`, `BeingExamined` or `BeingTreated`; from collapse, ambulance or load it is left alone |

## ICU step-down (`HospitalizationComponent_*.cs`, `CharacterPanelControllerPatches.cs`, `IcuStepDown.cs`)

| Vanilla method | Mod class | What the mod does |
|---|---|---|
| `HospitalizationComponent.SwitchState` | `HospitalizationComponent_SwitchState` | Prefix (`out __state` = old state) + Postfix: a finished nurse check-up on an AI ICU patient calls `IcuStepDown.MarkCleared` when `GetBlockReason(patient, false)` is null, else `ClearMark` (the newest check-up counts). Never transfers here. Shared hook with Hospital Care Level Transfer, which excludes the ICU |
| `HospitalizationComponent.SelectNextStep` (private) | `HospitalizationComponent_SelectNextStep` | Prefix, AI ICU patients marked cleared with the assigned doctor on shift. Not cleared: vanilla. `GetBlockReason(patient, false)` set: drop the mark, vanilla. `GetBlockReason(patient, true)` set (busy) or doctor off shift: keep the mark, vanilla. Otherwise `TryStepDown`: on success drop the mark, `__result = true`, skip vanilla; on failure (no bed or stretcher) keep the mark and retry later |
| `CharacterPanelController.IsAbleToChangeDepartments` | `CharacterPanelController_IsAbleToChangeDepartments` | Postfix: an ICU patient with no block reason may change department, so the selector opens. Every other vanilla blocker stays |
| `CharacterPanelController.OnDepartmentSelected` | `CharacterPanelController_OnDepartmentSelected` | Prefix (`[HarmonyAfter("Loupito75:HospitalCareLevelTransfer")]`, `Priority.Last`, honours `__runOriginal`): for an ICU patient runs `TryStepDown` and the vanilla exam refresh, or plays the forbidden sound with a floating reason. Always skips vanilla for ICU patients |
| `CharacterPanelController.Update` | `CharacterPanelController_Update` | Postfix: for an ICU patient, replaces the tooltip of the big department button only (block reason, min-stay hours, or ready). Never touches the small button |

## Config toggles

Settings in `ModSmarterDiagnosisAndTreatment.Config.xml` (registry in `ModConfig.cs`) that switch patches off. "Prepare" means the patch class is not applied at all.

| Setting | Effect |
|---|---|
| `DiagnosisScoring/Enabled` | Prepare: `MedicalCondition_UpdatePossibleDiagnosesPatientContext`, `_GetPossibleDiagnoses`, `_IsClearWithCertaintyThreshold` |
| `NpcDiagnosis/EarlyDiagnosisEnabled` | Prepare: `MedicalCondition_Diagnose` |
| `ExaminationList/FilterEnabled` | Prepare: `ProcedureComponent_GetAllExaminationsForMedicalCondition` |
| `ExaminationList/SortEnabled` | Early return in the `UpdateAll...` Postfix only. Its Prefix/Finalizer always run because they set `UpdatingComponent` for the filter |
| `NpcExamChoice/Enabled` | Prepare: `ProcedureComponent_SelectExaminationForMedicalCondition`. `DeferLowPriorityExamsInEr` gates its ER block |
| `CriticalTreatment/ClinicEnabled` | Early return in `BehaviorPatient_SelectNextProcedure.Prefix` after the lab-room fix (the class is always patched) |
| `CriticalTreatment/HospitalEnabled` | Return in `HospitalizationComponent_TryToStartScheduledTreatment.Prefix` right after `GroupHospitalQueue`, so bundling still runs |
| `CriticalTreatment/InSurgeryEnabled` | Prepare: `HospitalizationComponent_UpdateStateBeingTreated` |
| `FastTrack/PriorityAndQueue` | Prepare: `BehaviorPatient_UncoverSymptomsFromLastExamination`, `_UncoverSymptomsFromLabExamination`, `Room_SortQueue` |
| `FastTrack/SkipNeeds` | Prepare: `BehaviorPatient_CheckNeeds`. `NeedsSafetyValve` is the need level that is still fulfilled; `NeedsQueueAllowance` (0 = off) is the number of patients ahead from which needs work as in vanilla |
| `ErObservation/SkipForOtherDepartment` | Prepare: `BehaviorPatient_ObservationCheck` |
| `TreatmentThreshold/Enabled` | Prepare: `ProcedureComponent_PlanAllTreatments` and the four `DepartmentManagementLogisticsHybridController_*` classes |
| `TreatmentBundling/Enabled` | Prepare: `BehaviorPatient_TryToStartScheduledTreatment` and the six `ProcedureScriptTreatment*` start/stop/finish/prepare classes; `GroupHospitalQueue` returns early |
| `TreatmentBundling/HandOverAcrossDesk` | Prepare: `ProcedureScriptTreatmentReceipt_UpdateStateDoctorUsingPrinter` (independent of `Enabled`) |
| `ExamBundling/BloodDraw`, `/WasteSamples` | Prepare: the blood-draw class and the two urine/stool classes of `ExamBundling.cs`; `CanJoin` also checks the group's toggle |
| `ExamBundling/KeepUndressed` | Prepare: the eight classes of `KeepUndressed.cs` |
| `IcuStepDown/Enabled` | Prepare: the three `CharacterPanelController_*` classes, and with `AutomaticForAiPatients` also `HospitalizationComponent_SelectNextStep` / `_SwitchState` |
| `UiHighlights/*` | Early return in each highlight class's track/apply entry points (not in `Restore*`). The `HighlightPatches.cs` classes stay patched, since they also set `PatientPanelContext` for the tooltips |
| `Tooltips/*` | Prepare: `TooltipExamination_UpdateData`, and the `TooltipDiagnosis` / `TooltipSymptom` patches |

Always patched (vanilla fixes): the lab-room part of `BehaviorPatient_SelectNextProcedure`, both classes in `DoctorCallsPatient.cs`, `Behavior_GetInteractionPosition`.
