// Nurse-performed port of vanilla Lopital.ProcedureScriptExaminationUrineAnalysis. Re-diff against the vanilla script after game updates.
// Deviations: performer is the nurse (m_procedureScene.Nurse), no doctor-behavior use, skill from NurseExams.NurseQualifications,
// release via NURSE_FINISHED_PROCEDURE.
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment;

public class LmsdtNurseExaminationUrineAnalysis : ProcedureScript
{
	public const string STATE_IDLE = "IDLE";

	public const string STATE_EMPLOYEE_GOING_TO_EQUIPMENT_TO_WAIT = "EMPLOYEE_GOING_TO_EQUIPMENT_TO_WAIT";

	public const string STATE_EMPLOYEE_WAITING_FOR_PATIENT = "EMPLOYEE_WAITING_FOR_PATIENT";

	public const string STATE_EMPLOYEE_STOPPED_WAITING_FOR_PATIENT = "EMPLOYEE_STOPPED_WAITING_FOR_PATIENT";

	public const string STATE_PATIENT_GOING_TO_DESK = "PATIENT_GOING_TO_DESK";

	public const string STATE_PATIENT_GOING_TO_PROCEDURE = "PATIENT_GOING_TO_PROCEDURE";

	public const string STATE_PERK_CHECK = "PERK_CHECK";

	public const string STATE_EMPLOYEE_GOING_TO_PROCEDURE = "EMPLOYEE_GOING_TO_PROCEDURE";

	public const string STATE_WAIT_FOR_WC = "WAIT_FOR_WC";

	public const string STATE_EMPLOYEE_GOING_TO_CABINET = "EMPLOYEE_GOING_TO_CABINET";

	public const string STATE_EMPLOYEE_USING_CABINET = "EMPLOYEE_USING_CABINET";

	public const string STATE_PROCEDURE_DELAYED = "PROCEDURE_DELAYED";

	public const string STATE_PROCEDURE_STARTED = "PROCEDURE_STARTED";

	public const string STATE_PATIENT_ON_TOILET = "PATIENT_ON_TOILET";

	public const string STATE_PROCEDURE_IDLE = "PROCEDURE_IDLE";

	public const string STATE_PROCEDURE_IDLE_ANIMATION_IN = "PROCEDURE_IDLE_ANIMATION_IN";

	public const string STATE_PROCEDURE_IDLE_ANIMATION = "PROCEDURE_IDLE_ANIMATION";

	public const string STATE_PROCEDURE_OUT_ANIMATION = "PROCEDURE_OUT_ANIMATION";

	public const string STATE_PATIENT_GOING_BACK_TO_DOCTOR = "PATIENT_GOING_BACK_TO_DOCTOR";

	public const string STATE_GOING_TO_SINK = "GOING_TO_SINK";

	public const string STATE_USE_SINK = "USE_SINK";

	private const int PARAM_PROCEDURE_FINISHED = 0;

	private const int PARAM_EMPLOYEE_SKILL = 1;

	private const int PARAM_PROCEDURE_TIME = 2;

	private const int PARAM_PATIENT_SAID_OUCH = 4;

	private const int PARAM_PATIENT_ICON_WAITING_WC = 5;

	private const int PARAM_NO_WC_WAIT_CYCLES = 6;

	private const int BED = 0;

	private const int EQUIPMENT = 1;

	private const int SINK = 2;

	private const int MOBILE_WORKSTATION = 3;

	public override void ScriptUpdate(float deltaTime)
	{
		m_stateData.m_timeInState += deltaTime;
		if (m_stateData.m_state == "PATIENT_ON_TOILET")
		{
			UpdatePatientOnToilet();
		}
		if (m_stateData.m_state == "PROCEDURE_STARTED")
		{
			UpdateExamStarted();
		}
		else if (m_stateData.m_state == "EMPLOYEE_WAITING_FOR_PATIENT")
		{
			UpdateStateEmployeeWaitingForPatient();
		}
		else if (m_stateData.m_state == "EMPLOYEE_GOING_TO_EQUIPMENT_TO_WAIT")
		{
			UpdateStateEmployeeGoingToEquipmentToWait();
		}
		else if (m_stateData.m_state == "EMPLOYEE_STOPPED_WAITING_FOR_PATIENT")
		{
			UpdateStateEmployeeStoppedWaitingForPatient();
		}
		else if (m_stateData.m_state == "PROCEDURE_DELAYED")
		{
			SetExaminationOrientation();
		}
		else if (m_stateData.m_state == "PATIENT_GOING_TO_PROCEDURE")
		{
			UpdateStatePatientGoingToProcedure();
		}
		else if (m_stateData.m_state == "PATIENT_GOING_TO_DESK")
		{
			UpdateStatePatientGoingToDesk();
		}
		else if (m_stateData.m_state == "EMPLOYEE_GOING_TO_CABINET")
		{
			UpdateStateEmployeeGoingToCabinet();
		}
		else if (m_stateData.m_state == "EMPLOYEE_USING_CABINET")
		{
			UpdateStateEmployeeUsingCabinet();
		}
		else if (m_stateData.m_state == "EMPLOYEE_GOING_TO_PROCEDURE")
		{
			UpdateStateEmployeeGoingToProcedure();
		}
		else if (m_stateData.m_state == "PERK_CHECK")
		{
			UpdatePerkCheck();
		}
		else if (m_stateData.m_state == "PROCEDURE_IDLE")
		{
			UpdateStateProcedureIdle();
		}
		else if (m_stateData.m_state == "PROCEDURE_IDLE_ANIMATION_IN")
		{
			UpdateStateProcedureIdleAnimationIn();
		}
		else if (m_stateData.m_state == "PROCEDURE_IDLE_ANIMATION")
		{
			UpdateStateProcedureIdleAnimation();
		}
		else if (m_stateData.m_state == "PROCEDURE_OUT_ANIMATION")
		{
			UpdateStateProcedureOutAnimation();
		}
		else if (m_stateData.m_state == "WAIT_FOR_WC")
		{
			UpdateStateWaitForWc();
		}
		else if (m_stateData.m_state == "GOING_TO_SINK")
		{
			UpdateStateGoingToSink();
		}
		else if (m_stateData.m_state == "PATIENT_GOING_BACK_TO_DOCTOR")
		{
			UpdateStateGoingBackToDoctor();
		}
		else if (m_stateData.m_state == "USE_SINK")
		{
			UpdateStateUseSink();
		}
	}

	public override void Activate()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		BehaviorNurse nurseBehavior = employeeCharacter.GetComponent<BehaviorNurse>();
		if (nurseBehavior != null && nurseBehavior.m_state.m_nurseState != NurseState.OverridenReservedForProcedure && nurseBehavior.m_state.m_nurseState != NurseState.Idle && nurseBehavior.m_state.m_nurseState != NurseState.FinishedProcedure && nurseBehavior.m_state.m_nurseState != NurseState.GoingToWorkplace)
		{
			employeeCharacter.LogError("Nurse is in a wrong state: " + nurseBehavior.m_state.m_nurseState);
		}
		SendMessage(employeeCharacter, new Message(Messages.OVERRIDE_BY_PROCEDURE_SCRIPT));
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		DiscomfortCheck();
		if (!entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			Room entity2 = employeeCharacter.GetComponent<EmployeeComponent>().m_state.m_homeRoom.GetEntity();
			if (entity2 != null)
			{
				m_stateData.m_procedureScene.m_room = entity2;
				entity2.m_roomPersistentData.m_currentProcedureOwner = this;
			}
		}
		if (entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			Room roomAt = MapScriptInterface.Instance.GetRoomAt(employeeCharacter.GetComponent<WalkComponent>().GetCurrentTile(), employeeCharacter.GetComponent<WalkComponent>().GetFloorIndex());
			Room roomAt2 = MapScriptInterface.Instance.GetRoomAt(entity.GetComponent<WalkComponent>().GetCurrentTile(), entity.GetComponent<WalkComponent>().GetFloorIndex());
			Room entity3 = m_stateData.m_procedureScene.m_room.GetEntity();
			if (entity3 != null)
			{
				m_stateData.m_procedureScene.m_room = entity3;
				entity3.m_roomPersistentData.m_currentProcedureOwner = this;
			}
			if (entity3 != null && (roomAt != entity3 || roomAt2 != entity3))
			{
				TileObject tileObject = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(entity, null, component.GetCurrentTile(), entity3, "mobile_workstation", AccessRights.STAFF);
				if (tileObject != null)
				{
					ref EntityIDPointer<TileObject> reference = ref m_stateData.m_procedureScene.m_equipment[3];
					reference = tileObject;
					employeeCharacter.GetComponent<WalkComponent>().SetDestination(tileObject.GetDefaultUsePosition(), tileObject.GetFloorIndex());
					employeeCharacter.GetComponent<UseComponent>().ReserveObject(tileObject);
				}
				else
				{
					employeeCharacter.GetComponent<WalkComponent>().SetDestination(GetEquipment(1).GetDefaultUsePosition(), GetEquipment(1).GetFloorIndex());
					employeeCharacter.GetComponent<UseComponent>().ReserveObject(GetEquipment(1));
				}
				SwitchState("EMPLOYEE_GOING_TO_EQUIPMENT_TO_WAIT");
			}
			else
			{
				ReserveCabinet();
			}
			return;
		}
		Vector2i currentTile = employeeCharacter.GetComponent<WalkComponent>().GetCurrentTile();
		TileObject tileObject2 = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(entity, this, currentTile, MapScriptInterface.Instance.GetRoomAt(employeeCharacter.GetComponent<WalkComponent>()), "sitting", AccessRights.PATIENT);
		SwitchState("PATIENT_GOING_TO_DESK");
		if (tileObject2 != null)
		{
			if (!entity.GetComponent<WalkComponent>().IsSittingOn(tileObject2))
			{
				entity.GetComponent<WalkComponent>().GoSit(tileObject2);
			}
		}
		else
		{
			entity.GetComponent<WalkComponent>().SetDestination(employeeCharacter.GetComponent<Behavior>().GetInteractionPosition(), employeeCharacter.GetComponent<WalkComponent>().GetFloorIndex());
		}
	}

	private void UpdateStateEmployeeGoingToEquipmentToWait()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (!employeeCharacter.GetComponent<WalkComponent>().IsBusy())
		{
			if (GetEquipment(3) != null)
			{
				employeeCharacter.GetComponent<UseComponent>().Activate(UseComponentMode.UNTIL_INTERRUPTED);
			}
			else
			{
				employeeCharacter.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
			}
			SwitchState("EMPLOYEE_WAITING_FOR_PATIENT");
		}
	}

	private void UpdateStateEmployeeWaitingForPatient()
	{
		if (m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<StretcherComponent>().IsIdle())
		{
			Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
			if (GetEquipment(3) != null)
			{
				employeeCharacter.GetComponent<UseComponent>().Interrupt();
			}
			SwitchState("EMPLOYEE_STOPPED_WAITING_FOR_PATIENT");
		}
	}

	private void UpdateStateEmployeeStoppedWaitingForPatient()
	{
		if (GetEquipment(3) != null)
		{
			if (m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<AnimModelComponent>().IsIdle())
			{
				ReserveCabinet();
			}
		}
		else if (!m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<UseComponent>().IsBusy())
		{
			ReserveCabinet();
		}
	}

	public void UpdateStatePatientGoingToDesk()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		if (!component.IsBusy())
		{
			if (!component.IsSitting())
			{
				entity.GetComponent<AnimModelComponent>().SetDirection(employeeCharacter.GetComponent<Behavior>().GetInteractionOrientation(entity));
				entity.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
			}
			ReserveCabinet();
		}
	}

	private void UpdateStatePatientGoingToProcedure()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		if (!component.IsBusy())
		{
			SilenceCharacter(entity);
			SwitchState("PATIENT_ON_TOILET");
		}
	}

	private void UpdateStateEmployeeGoingToCabinet()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (!employeeCharacter.GetComponent<WalkComponent>().IsBusy())
		{
			employeeCharacter.GetComponent<UseComponent>().Activate(UseComponentMode.SINGLE_USE);
			SwitchState("EMPLOYEE_USING_CABINET");
		}
	}

	private void UpdateStateEmployeeUsingCabinet()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (employeeCharacter.GetComponent<UseComponent>().IsBusy())
		{
			return;
		}
		// Mod: exam bundling, as the vanilla script's prefix.
		if (GetParam(0) >= 1f)
		{
			ExamBundling.SampleAllButLast(this, false);
		}
		SilenceCharacter(employeeCharacter);
		ChooseDoctorsPosition();
		if (GetParam(0) < 1f)
		{
			SwitchState("EMPLOYEE_GOING_TO_PROCEDURE");
			return;
		}
		if (m_stateData.m_examination.Entry.LabTestingExaminationRef != null)
		{
			LabProcedure procedure = LabProcedureManager.Instance.AddLabProcedure(m_stateData.m_procedureScene.m_patient.GetEntity(), m_stateData.m_examination.Entry, GetEquipment(1));
			m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>().AddLabProcedure(procedure);
		}
		if (GetEquipment(2) != null)
		{
			ReserveSink();
		}
		else
		{
			FinishProcedureHospitalized();
		}
	}

	private void UpdateStateEmployeeGoingToProcedure()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		WalkComponent component = employeeCharacter.GetComponent<WalkComponent>();
		if (component.IsBusy())
		{
			return;
		}
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		AnimModelComponent component2 = employeeCharacter.GetComponent<AnimModelComponent>();
		BehaviorPatient component3 = entity.GetComponent<BehaviorPatient>();
		if (entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			component2.SetDirection(component3.GetLyingInteractionOrientation(LyingInteractionPositions.RIGHT));
		}
		else
		{
			employeeCharacter.GetComponent<AnimModelComponent>().SetDirection(component3.GetInteractionOrientation(employeeCharacter));
			if (!entity.GetComponent<WalkComponent>().IsSitting())
			{
				entity.GetComponent<AnimModelComponent>().SetDirection(component3.GetInteractionOrientationSelf(employeeCharacter));
			}
		}
		component2.PlayAnimation("stand_idle");
		SilenceCharacter(employeeCharacter);
		SpeakCharacter(employeeCharacter, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_WC_GO", -1f);
		SwitchState("PERK_CHECK");
	}

	private void UpdatePerkCheck()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		PerkComponent component = entity.GetComponent<PerkComponent>();
		if (component.m_perkSet.HasPerk("PERK_AGGRESSIVE"))
		{
			if (m_stateData.m_timeInState > 2f)
			{
				SpeakCharacter(entity, "DIALOGUE_PATIENT_ANGRY", "BUBBLE_PERK_AGGRESSIVE", -1f);
				SwitchState("PROCEDURE_DELAYED");
			}
		}
		else
		{
			SwitchState("PROCEDURE_STARTED");
		}
	}

	private void UpdateExamStarted()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		AnimModelComponent component = entity.GetComponent<AnimModelComponent>();
		BehaviorPatient component2 = entity2.GetComponent<BehaviorPatient>();
		CheckPerkModifiers(entity2, entity);
		if (entity.GetComponent<PerkComponent>().m_perkSet.HasPerk("PERK_PEOPLE_PERSON") && entity2.GetComponent<PerkComponent>().m_perkSet.GetHiddenPerkCount() > 0)
		{
			if (entity.GetComponent<PerkComponent>().m_perkSet.HasHiddenPerk("PERK_PEOPLE_PERSON"))
			{
				entity.GetComponent<PerkComponent>().RevealPerk("PERK_PEOPLE_PERSON", showNotifications: true);
			}
			entity2.GetComponent<PerkComponent>().RevealAllPerks(component2.IsBookmarked());
		}
		AnimationSetup animationSetup = CheckAnimationSetup();
		if (animationSetup.AnimationNameIn != null)
		{
			component.PlayAnimation(animationSetup.AnimationNameIn, looping: false);
			SwitchState("PROCEDURE_IDLE_ANIMATION_IN");
		}
		else
		{
			component.PlayAnimation(animationSetup.AnimationNameOut, looping: false);
			SwitchState("PROCEDURE_IDLE");
		}
	}

	private void UpdateStateProcedureIdleAnimationIn()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		AnimModelComponent component = employeeCharacter.GetComponent<AnimModelComponent>();
		if (component.IsIdle())
		{
			AnimationSetup animationSetup = CheckAnimationSetup();
			component.PlayAnimation(animationSetup.AnimationNameIdle);
			SwitchState("PROCEDURE_IDLE_ANIMATION");
		}
	}

	private void UpdateStateProcedureIdleAnimation()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (m_stateData.m_timeInState > 1f && GetParam(4) < 1f)
		{
			SetParam(4, 1f);
			if (m_stateData.m_examination.Entry.DiscomfortLevel > SymptomHazard.Low)
			{
				entity.GetComponent<SpeechComponent>().PlayDialogue("DIALOGUE_PATIENT_OUCH");
			}
		}
		if (m_stateData.m_timeInState > GetParam(2))
		{
			AnimationSetup animationSetup = CheckAnimationSetup();
			AnimModelComponent component = employeeCharacter.GetComponent<AnimModelComponent>();
			component.PlayAnimation(animationSetup.AnimationNameOut, looping: false);
			SetParam(0, 1f);
			SwitchState("PROCEDURE_OUT_ANIMATION");
		}
	}

	private void UpdateStateProcedureOutAnimation()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (employeeCharacter.GetComponent<AnimModelComponent>().IsIdle())
		{
			ReserveCabinet();
		}
	}

	private void UpdateStateProcedureIdle()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (!employeeCharacter.GetComponent<AnimModelComponent>().IsIdle())
		{
			return;
		}
		SilenceCharacter(employeeCharacter);
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (employeeCharacter.GetComponent<PerkComponent>().m_perkSet.HasPerk("PERK_PEOPLE_PERSON") && entity.GetComponent<PerkComponent>().m_perkSet.GetHiddenPerkCount() > 0)
		{
			if (employeeCharacter.GetComponent<PerkComponent>().m_perkSet.HasHiddenPerk("PERK_PEOPLE_PERSON"))
			{
				employeeCharacter.GetComponent<PerkComponent>().RevealPerk("PERK_PEOPLE_PERSON", showNotifications: true);
			}
			entity.GetComponent<PerkComponent>().RevealAllPerks(entity.GetComponent<BehaviorPatient>().IsBookmarked());
		}
		if (entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			SetParam(0, 1f);
			ReserveCabinet();
		}
		else
		{
			CheckToiletAvaibility();
		}
	}

	private void CheckToiletAvaibility()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Department entity = employeeCharacter.GetComponent<EmployeeComponent>().m_state.m_department.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		TileObject tileObject;
		if (entity2.GetComponent<WalkComponent>().IsSitting() && entity2.GetComponent<WalkComponent>().m_state.m_objectSittingOn.GetEntity().HasTag("wc"))
		{
			tileObject = entity2.GetComponent<WalkComponent>().m_state.m_objectSittingOn.GetEntity();
		}
		else
		{
			int floorIndex = entity2.GetComponent<WalkComponent>().GetFloorIndex();
			tileObject = MapScriptInterface.Instance.FindClosestCenterObjectWithTag(entity2.GetComponent<WalkComponent>().GetCurrentTile(), floorIndex, "wc", AccessRights.PATIENT);
			if (tileObject == null)
			{
				tileObject = MapScriptInterface.Instance.FindClosestCenterObjectWithTag(entity2.GetComponent<WalkComponent>().GetCurrentTile(), 0, "wc", AccessRights.PATIENT);
			}
			if (tileObject == null)
			{
				tileObject = MapScriptInterface.Instance.FindClosestObjectWithTag(entity2.GetComponent<WalkComponent>().GetCurrentTile(), floorIndex, entity, "wc", AccessRights.PATIENT);
			}
			if (tileObject == null && GetParam(6) > 20f)
			{
				tileObject = MapScriptInterface.Instance.FindClosestObjectWithTag(entity2.GetComponent<WalkComponent>().GetCurrentTile(), floorIndex, entity, "wc", AccessRights.BIOHAZARD);
			}
		}
		// Vanilla: doctor walks back to his workplace chair (doctor IsAtWorkplace). Not applicable to nurses.
		if (tileObject != null)
		{
			entity2.GetComponent<WalkComponent>().GoSit(tileObject);
			SpeakCharacter(entity2, null, m_stateData.m_examination.Entry.IconIndex, 4f);
			SwitchState("PATIENT_GOING_TO_PROCEDURE");
			return;
		}
		SetParam(6, GetParam(6) + 1f);
		if (GetParam(5) < 1f)
		{
			SetParam(5, 1f);
			SpeakCharacter(entity2, null, "BUBBLE_UNHAPPY", -1f);
		}
		else
		{
			SetParam(5, 0f);
			SpeakCharacter(entity2, null, "BUBBLE_WC_GO", -1f);
		}
		entity2.GetComponent<MoodComponent>().AddSatisfactionModifier("SAT_MOD_WAITED_FOR_WC");
		SwitchState("WAIT_FOR_WC");
	}

	private void UpdateStateWaitForWc()
	{
		if (!m_stateData.m_procedureScene.m_patient.CheckEntity())
		{
			LogError("Urine analysis - patient doesn't exist anymore!");
			SwitchState("IDLE");
		}
		if (m_stateData.m_timeInState > 3f)
		{
			Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
			SilenceCharacter(entity);
			CheckToiletAvaibility();
		}
	}

	private void UpdatePatientOnToilet()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (!(m_stateData.m_timeInState > 5f))
		{
			return;
		}
		// Mod: exam bundling, as the vanilla script's prefix.
		ExamBundling.SampleAllButLast(this, true);
		entity.GetComponent<Behavior>().ReceiveMessage(new Message(Messages.BLADDER_REDUCED, 100f));
		SetParam(0, 1f);
		LabProcedure procedure = LabProcedureManager.Instance.AddLabProcedure(m_stateData.m_procedureScene.m_patient.GetEntity(), m_stateData.m_examination.Entry, GetEquipment(1));
		m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>().AddLabProcedure(procedure);
		entity.GetComponent<BehaviorPatient>().ReceiveMessage(new Message(Messages.BOREDOM_REDUCED, 10f));
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (entity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_plannedExaminationStates.Count > 0)
		{
			EntityIDPointer<Room> homeRoom = employeeCharacter.GetComponent<EmployeeComponent>().m_state.m_homeRoom;
			Vector2i currentTile = employeeCharacter.GetComponent<WalkComponent>().GetCurrentTile();
			TileObject tileObject = null;
			if (homeRoom != null)
			{
				tileObject = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(entity, null, currentTile, homeRoom.GetEntity(), "sitting", AccessRights.PATIENT);
			}
			if (tileObject != null)
			{
				entity.GetComponent<WalkComponent>().GoSit(tileObject);
			}
			else
			{
				entity.GetComponent<WalkComponent>().SetDestination(employeeCharacter.GetComponent<Behavior>().GetInteractionPosition(), employeeCharacter.GetComponent<WalkComponent>().GetFloorIndex());
			}
			SwitchState("PATIENT_GOING_BACK_TO_DOCTOR");
		}
		else
		{
			FinishProcedure();
		}
	}

	private void UpdateStateGoingToSink()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		if (!entity.GetComponent<WalkComponent>().IsBusy())
		{
			entity.GetComponent<UseComponent>().Activate(UseComponentMode.SINGLE_USE);
			SwitchState("USE_SINK");
		}
	}

	private void UpdateStateUseSink()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		if (!entity.GetComponent<UseComponent>().IsBusy())
		{
			FinishProcedureHospitalized();
		}
	}

	private void UpdateStateGoingBackToDoctor()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (!entity.GetComponent<WalkComponent>().IsBusy())
		{
			if (!entity.GetComponent<WalkComponent>().IsSitting())
			{
				entity.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
			}
			FinishProcedure();
		}
	}

	private void FinishProcedure()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		entity.GetComponent<BehaviorPatient>().ReceiveMessage(new Message(Messages.BOREDOM_REDUCED, 10f));
		employeeCharacter.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(NurseExams.NurseQualifications, Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_EXAMINATION_EQUIPMENT_SKILL_POINTS").Value);
		SendMessage(employeeCharacter, new Message(Messages.NURSE_FINISHED_PROCEDURE));
		m_stateData.m_procedureScene.EmployeeCharacter = null;
		SwitchState("IDLE");
	}

	private void FinishProcedureHospitalized()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		employeeCharacter.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(NurseExams.NurseQualifications, 10);
		entity.GetComponent<BehaviorPatient>().ReceiveMessage(new Message(Messages.BOREDOM_REDUCED, Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_EXAMINATION_EQUIPMENT_SKILL_POINTS").Value));
		SendMessage(employeeCharacter, new Message(Messages.NURSE_FINISHED_PROCEDURE));
		m_stateData.m_procedureScene.EmployeeCharacter = null;
		SwitchState("IDLE");
	}

	private void SetExaminationOrientation()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		AnimModelComponent component = employeeCharacter.GetComponent<AnimModelComponent>();
		BehaviorPatient component2 = entity.GetComponent<BehaviorPatient>();
		if (entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			component.SetDirection(component2.GetLyingInteractionOrientation(LyingInteractionPositions.RIGHT));
		}
		else
		{
			component.SetDirection(component2.GetFacingInteractionOrientation());
		}
		component.PlayAnimation("stand_idle");
		ProcedureDelayed();
	}

	private void ProcedureDelayed()
	{
		if (m_stateData.m_timeInState > 4f)
		{
			Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
			SilenceCharacter(entity);
			SwitchState("PROCEDURE_STARTED");
		}
	}

	private void DiscomfortCheck()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.Nurse.GetEntity();
		if (entity2.GetComponent<PerkComponent>().m_perkSet.HasPerk("PERK_COMFORTING"))
		{
			if (entity2.GetComponent<PerkComponent>().m_perkSet.HasHiddenPerk("PERK_COMFORTING"))
			{
				entity2.GetComponent<PerkComponent>().RevealPerk("PERK_COMFORTING", entity2.GetComponent<BehaviorNurse>().IsBookmarked());
			}
		}
		else
		{
			entity.GetComponent<MoodComponent>().AddDiscomfortModifier(entity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_activeExamination.Entry);
		}
	}

	private void ReserveCabinet()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		WalkComponent component = employeeCharacter.GetComponent<WalkComponent>();
		SilenceCharacter(employeeCharacter);
		component.SetDestination(GetEquipment(1).GetDefaultUsePosition(), GetEquipment(1).GetFloorIndex());
		employeeCharacter.GetComponent<UseComponent>().ReserveObject(GetEquipment(1));
		SwitchState("EMPLOYEE_GOING_TO_CABINET");
	}

	private void ReserveSink()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		employeeCharacter.GetComponent<UseComponent>().ReserveObject(GetEquipment(2));
		employeeCharacter.GetComponent<WalkComponent>().SetDestination(GetEquipment(2).GetDefaultUsePosition(), GetEquipment(2).GetFloorIndex());
		SwitchState("GOING_TO_SINK");
	}

	private void ChooseDoctorsPosition()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		BehaviorPatient component2 = m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>();
		if (entity2.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			component.SetDestination(component2.GetLyingInteractionPosition(LyingInteractionPositions.RIGHT), entity2.GetComponent<WalkComponent>().GetFloorIndex());
		}
		else
		{
			entity.GetComponent<WalkComponent>().SetDestination(m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>().GetInteractionPosition(), m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<WalkComponent>().GetFloorIndex());
		}
	}

	private AnimationSetup CheckAnimationSetup()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if ((m_stateData.GetProcedure().AnimationSetupLying != null && entity.GetComponent<HospitalizationComponent>().IsHospitalized()) || m_stateData.GetProcedure().AnimationSetupSitting == null)
		{
			return m_stateData.GetProcedure().AnimationSetupLying;
		}
		return m_stateData.GetProcedure().AnimationSetupSitting;
	}

	public override bool IsIdle()
	{
		return m_stateData.m_state == "IDLE";
	}
}
