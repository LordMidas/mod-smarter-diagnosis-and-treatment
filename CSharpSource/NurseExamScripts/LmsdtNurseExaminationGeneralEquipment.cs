// Nurse-performed port of vanilla Lopital.ProcedureScriptExaminationGeneralEquipment. Re-diff against the vanilla script after game updates.
// Deviations: performer is the nurse (m_procedureScene.Nurse), no doctor-behavior use, skill from NurseExams.NurseQualifications,
// release via NURSE_FINISHED_PROCEDURE.
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment;

public class LmsdtNurseExaminationGeneralEquipment : ProcedureScript
{
	public const string STATE_DELAY = "DELAY";

	public const string STATE_IDLE = "IDLE";

	public const string STATE_STARTED = "STARTED";

	public const string STATE_EMPLOYEE_ORDERING_PATIENT_SIT = "EMPLOYEE_ORDERING_PATIENT_SIT";

	public const string STATE_PATIENT_GOING_TO_PROCEDURE = "PATIENT_GOING_TO_PROCEDURE";

	public const string STATE_EMPLOYEE_GOING_TO_PROCEDURE = "EMPLOYEE_GOING_TO_PROCEDURE";

	public const string STATE_EMPLOYEE_GOING_TO_EQUIPMENT_TO_WAIT = "EMPLOYEE_GOING_TO_EQUIPMENT_TO_WAIT";

	public const string STATE_EMPLOYEE_GOING_TO_CABINET = "EMPLOYEE_GOING_TO_CABINET";

	public const string STATE_EMPLOYEE_USING_CABINET = "EMPLOYEE_USING_CABINET";

	public const string STATE_EMPLOYEE_WAITING_FOR_PATIENT = "EMPLOYEE_WAITING_FOR_PATIENT";

	public const string STATE_EMPLOYEE_STOPPED_WAITING_FOR_PATIENT = "EMPLOYEE_STOPPED_WAITING_FOR_PATIENT";

	public const string STATE_PROCEDURE_PATIENT_DRESS = "PROCEDURE_PATIENT_DRESS";

	public const string STATE_EMPLOYEE_ORDERING_PATIENT = "EMPLOYEE_ORDERING_PATIENT";

	public const string STATE_PROCEDURE_DELAYED = "PROCEDURE_DELAYED";

	public const string STATE_PROCEDURE_STARTED = "PROCEDURE_STARTED";

	public const string STATE_PROCEDURE_IDLE = "PROCEDURE_IDLE";

	public const string STATE_PROCEDURE_IDLE_ANIMATION_IN = "PROCEDURE_IDLE_ANIMATION_IN";

	public const string STATE_PROCEDURE_IDLE_ANIMATION = "PROCEDURE_IDLE_ANIMATION";

	public const string STATE_PROCEDURE_CONSULTATION = "PROCEDURE_CONSULTATION";

	public const string STATE_GOING_TO_SINK = "GOING_TO_SINK";

	public const string STATE_USE_SINK = "USE_SINK";

	public const string STATE_PROCEDURE_FINISHED = "PROCEDURE_FINISHED";

	private const int PARAM_PROCEDURE_FINISHED = 0;

	private const int PARAM_EMPLOYEE_SKILL = 1;

	private const int PARAM_PROCEDURE_TIME = 2;

	private const int PARAM_EQUIPMENT_RETURNED = 3;

	private const int PARAM_PATIENT_SAID_OUCH = 4;

	private const int BED = 0;

	private const int EQUIPMENT = 1;

	private const int SINK = 2;

	private const int MOBILE_WORKSTATION = 3;

	public override void ScriptUpdate(float deltaTime)
	{
		m_stateData.m_timeInState += deltaTime;
		if (m_stateData.m_procedureScene.EmployeeCharacter == null && m_stateData.m_state != "IDLE")
		{
			m_stateData.m_procedureScene.m_patient.GetEntity().LogError("Cleared a broken employee in ProcedureScriptExaminationGeneralEquipment");
			SwitchState("IDLE");
		}
		if (m_stateData.m_state == "PROCEDURE_STARTED")
		{
			UpdateProcedureStarted();
		}
		else if (m_stateData.m_state == "EMPLOYEE_ORDERING_PATIENT_SIT")
		{
			UpdateStateEmployeeOrderingPatientSit();
		}
		else if (m_stateData.m_state == "PROCEDURE_DELAYED")
		{
			SetExaminationOrientation();
		}
		else if (m_stateData.m_state == "PATIENT_GOING_TO_PROCEDURE")
		{
			UpdateStatePatientGoingToProcedure();
		}
		else if (m_stateData.m_state == "EMPLOYEE_GOING_TO_EQUIPMENT_TO_WAIT")
		{
			UpdateStateEmployeeGoingToEquipmentToWait();
		}
		else if (m_stateData.m_state == "EMPLOYEE_GOING_TO_CABINET")
		{
			UpdateStateEmployeeGoingToCabinet();
		}
		else if (m_stateData.m_state == "EMPLOYEE_USING_CABINET")
		{
			UpdateStateEmployeeUsingCabinet();
		}
		else if (m_stateData.m_state == "EMPLOYEE_WAITING_FOR_PATIENT")
		{
			UpdateStateEmployeeWaitingForPatient();
		}
		else if (m_stateData.m_state == "EMPLOYEE_STOPPED_WAITING_FOR_PATIENT")
		{
			UpdateStateEmployeeStoppedWaitingForPatient();
		}
		else if (m_stateData.m_state == "EMPLOYEE_GOING_TO_PROCEDURE")
		{
			UpdateStateEmployeeGoingToProcedure();
		}
		else if (m_stateData.m_state == "PROCEDURE_PATIENT_DRESS")
		{
			UpdateStatePatientDress();
		}
		else if (m_stateData.m_state == "EMPLOYEE_ORDERING_PATIENT")
		{
			UpdateStateEmployeeOrderingPatient();
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
		else if (m_stateData.m_state == "PROCEDURE_CONSULTATION")
		{
			UpdateStateConsultation();
		}
		else if (m_stateData.m_state == "GOING_TO_SINK")
		{
			UpdateStateGoingToSink();
		}
		else if (m_stateData.m_state == "USE_SINK")
		{
			UpdateStateUseSink();
		}
		else if (m_stateData.m_state == "PROCEDURE_FINISHED")
		{
			UpdateStateProcedureFinished();
		}
	}

	public override void Activate()
	{
		Room roomAt = MapScriptInterface.Instance.GetRoomAt(GetEquipment(0).m_state.m_position, GetEquipment(0).GetFloorIndex());
		Room roomAt2 = MapScriptInterface.Instance.GetRoomAt(m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<EmployeeComponent>().m_state.m_workPlacePosition, m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<EmployeeComponent>().m_state.m_workPlaceFloorIndex);
		if (roomAt != roomAt2 && roomAt2 != null && roomAt2.m_roomPersistentData.m_roomType != Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_ON_CALL_ROOM"))
		{
			Debug.LogWarning("Wrong room, wrong object!");
		}
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (!entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			Room entity2 = employeeCharacter.GetComponent<EmployeeComponent>().m_state.m_homeRoom.GetEntity();
			if (entity2 != null)
			{
				m_stateData.m_procedureScene.m_room = entity2;
				entity2.m_roomPersistentData.m_currentProcedureOwner = this;
			}
		}
		else if (roomAt != null)
		{
			m_stateData.m_procedureScene.m_room = roomAt;
			roomAt.m_roomPersistentData.m_currentProcedureOwner = this;
		}
		BehaviorNurse nurseBehavior = employeeCharacter.GetComponent<BehaviorNurse>();
		if (nurseBehavior != null && nurseBehavior.m_state.m_nurseState != NurseState.OverridenReservedForProcedure && nurseBehavior.m_state.m_nurseState != NurseState.Idle && nurseBehavior.m_state.m_nurseState != NurseState.FinishedProcedure && nurseBehavior.m_state.m_nurseState != NurseState.GoingToWorkplace)
		{
			employeeCharacter.LogError("Nurse is in a wrong state: " + nurseBehavior.m_state.m_nurseState);
		}
		SendMessage(employeeCharacter, new Message(Messages.OVERRIDE_BY_PROCEDURE_SCRIPT));
		DiscomfortCheck();
		float num = 1f;
		if (employeeCharacter.GetComponent<BehaviorNurse>() != null)
		{
			num = m_stateData.m_procedureScene.Nurse.GetEntity().GetComponent<EmployeeComponent>().GetAnyApplicableSkillLevel(NurseExams.NurseQualifications);
			SetParam(1, num);
		}
		if (employeeCharacter.GetComponent<BehaviorLabSpecialist>() != null)
		{
			num = m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<EmployeeComponent>().GetSkillLevel(m_stateData.GetProcedure().RequiredStatLabQualificationRef.Entry);
			SetParam(1, num);
		}
		SetParam(2, GetActionTime(employeeCharacter, 20, num));
		WalkComponent component = m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<WalkComponent>();
		TileObject equipment = GetEquipment(0);
		if (employeeCharacter.GetComponent<BehaviorLabSpecialist>() != null)
		{
			if (!entity.GetComponent<HospitalizationComponent>().IsHospitalized())
			{
				component.GoSit(GetEquipment(0));
				SwitchState("PATIENT_GOING_TO_PROCEDURE");
			}
			else
			{
				SwitchState("EMPLOYEE_WAITING_FOR_PATIENT");
			}
		}
		else if (equipment != null && !component.IsSittingOn(equipment) && !entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			SpeakCharacter(employeeCharacter, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_EXAM_BED", -1f);
			SwitchState("EMPLOYEE_ORDERING_PATIENT_SIT");
		}
		else if (entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			Room roomAt3 = MapScriptInterface.Instance.GetRoomAt(employeeCharacter.GetComponent<WalkComponent>().GetCurrentTile(), employeeCharacter.GetComponent<WalkComponent>().GetFloorIndex());
			Room roomAt4 = MapScriptInterface.Instance.GetRoomAt(entity.GetComponent<WalkComponent>().GetCurrentTile(), entity.GetComponent<WalkComponent>().GetFloorIndex());
			Room entity3 = m_stateData.m_procedureScene.m_room.GetEntity();
			if (roomAt3 != entity3 || roomAt4 != entity3)
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
		}
		else
		{
			ReserveCabinet();
		}
	}

	private void UpdateStatePatientGoingToProcedure()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		if (!component.IsBusy())
		{
			Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
			if (employeeCharacter.GetComponent<BehaviorLabSpecialist>() != null)
			{
				entity.GetComponent<AnimModelComponent>().SetDirection(GetEquipment(0).GetDefaultStretcherOrientation());
				entity.GetComponent<WalkComponent>().MoveTo(GetEquipment(0).GetPartWithTag("transport_point"), takeOverUser: true);
				entity.GetComponent<WalkComponent>().LieDown();
			}
			ReserveCabinet();
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
				employeeCharacter.GetComponent<AnimModelComponent>().PlayAnimation("stand_wait");
			}
			SwitchState("EMPLOYEE_WAITING_FOR_PATIENT");
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

	private void UpdateStateEmployeeWaitingForPatient()
	{
		if (m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<StretcherComponent>().IsIdle())
		{
			Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
			if (GetEquipment(3) != null && employeeCharacter.GetComponent<BehaviorLabSpecialist>() == null)
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
			if (!m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<UseComponent>().IsBusy())
			{
				ReserveCabinet();
			}
		}
		else
		{
			ReserveCabinet();
		}
	}

	private void UpdateStateEmployeeOrderingPatientSit()
	{
		if (m_stateData.m_timeInState > 2f)
		{
			Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
			WalkComponent component = entity.GetComponent<WalkComponent>();
			component.GoSit(GetEquipment(0));
			SwitchState("PATIENT_GOING_TO_PROCEDURE");
		}
	}

	private void UpdateStateEmployeeUsingCabinet()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (employeeCharacter.GetComponent<UseComponent>().IsBusy())
		{
			return;
		}
		SilenceCharacter(employeeCharacter);
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (GetParam(0) < 1f)
		{
			if (GetEquipment(1).m_state.m_gameDBObject.Entry.AnimationOnlyForceFrame)
			{
				GetEquipment(1).GetComponent<AnimatedObjectComponent>().ForceFrame(1);
			}
			WalkComponent component = employeeCharacter.GetComponent<WalkComponent>();
			BehaviorPatient component2 = m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>();
			if (entity.GetComponent<HospitalizationComponent>().IsHospitalized() || entity.GetComponent<WalkComponent>().m_state.m_lying)
			{
				component.SetDestination(component2.GetLyingInteractionPosition(LyingInteractionPositionsCheck()), entity.GetComponent<WalkComponent>().GetFloorIndex());
			}
			else
			{
				component.SetDestination(component2.GetFacingInteractionPosition(), entity.GetComponent<WalkComponent>().GetFloorIndex());
			}
			SwitchState("EMPLOYEE_GOING_TO_PROCEDURE");
			return;
		}
		if (GetEquipment(1).m_state.m_gameDBObject.Entry.AnimationOnlyForceFrame && m_stateData.m_examination != Database.Instance.GetEntry<GameDBExamination>("EXM_HEART_MONITORING"))
		{
			GetEquipment(1).GetComponent<AnimatedObjectComponent>().ForceFrame(0);
		}
		WalkComponent component3 = employeeCharacter.GetComponent<WalkComponent>();
		Vector2i currentTile = component3.GetCurrentTile();
		TileObject tileObject = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(m_stateData.m_procedureScene.m_patient.GetEntity(), this, currentTile, MapScriptInterface.Instance.GetRoomAt(component3), "biohazard_trash", AccessRights.STAFF);
		if (m_stateData.GetProcedure().Biohazard && tileObject != null && GetParam(3) < 1f)
		{
			SetParam(3, 1f);
			SpeakCharacter(employeeCharacter, null, "BUBBLE_BIOHAZARD", -1f);
			component3.SetDestination(tileObject.GetDefaultUsePosition(), tileObject.GetFloorIndex());
			employeeCharacter.GetComponent<UseComponent>().ReserveObject(tileObject);
			SwitchState("EMPLOYEE_GOING_TO_CABINET");
		}
		else
		{
			if (m_stateData.m_examination.Entry.LabTestingExaminationRef != null)
			{
				LabProcedure procedure = LabProcedureManager.Instance.AddLabProcedure(m_stateData.m_procedureScene.m_patient.GetEntity(), m_stateData.m_examination.Entry, GetEquipment(1));
				m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>().AddLabProcedure(procedure);
			}
			CheckSink();
		}
	}

	private void UpdateStateEmployeeGoingToProcedure()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		WalkComponent component = employeeCharacter.GetComponent<WalkComponent>();
		if (!component.IsBusy())
		{
			AnimModelComponent component2 = employeeCharacter.GetComponent<AnimModelComponent>();
			Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
			BehaviorPatient component3 = entity.GetComponent<BehaviorPatient>();
			employeeCharacter.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
			if (entity.GetComponent<HospitalizationComponent>().IsHospitalized() || entity.GetComponent<WalkComponent>().m_state.m_lying)
			{
				component2.SetDirection(component3.GetLyingInteractionOrientation(LyingInteractionPositionsCheck()));
			}
			else
			{
				component2.SetDirection(component3.GetFacingInteractionOrientation());
			}
			if (entity.GetComponent<AnimModelComponent>().m_state.m_dressLevel != m_stateData.GetProcedure().DressLevel)
			{
				SilenceCharacter(entity);
				SpeakCharacter(employeeCharacter, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_DRESS", 3f);
				SwitchState("PROCEDURE_PATIENT_DRESS");
			}
			else
			{
				SilenceCharacter(employeeCharacter);
				SpeakCharacter(employeeCharacter, "DIALOGUE_STAFF_ORDER_PATIENT", m_stateData.m_examination.Entry.IconIndex, -1f);
				SwitchState("EMPLOYEE_ORDERING_PATIENT");
			}
		}
	}

	private void UpdateStatePatientDress()
	{
		if (!(m_stateData.m_timeInState > 3f))
		{
			return;
		}
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		SilenceCharacter(entity);
		bool flag = entity.GetComponent<BehaviorPatient>().MainSymptomTreated() && entity.GetComponent<BehaviorPatient>().m_state.m_medicalCondition.m_gameDBMedicalCondition.Entry.FullyBandaged && entity.GetComponent<HospitalizationComponent>().IsInOwnBed();
		if (GetParam(0) < 1f)
		{
			Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
			SilenceCharacter(employeeCharacter);
			SpeakCharacter(employeeCharacter, "DIALOGUE_STAFF_ORDER_PATIENT", m_stateData.m_examination.Entry.IconIndex, -1f);
			if (!flag)
			{
				entity.GetComponent<AnimModelComponent>().SetClothingLevel(entity.GetComponent<BehaviorPatient>().GetTreatedSymptomDressLevel(m_stateData.GetProcedure().DressLevel));
			}
			SwitchState("EMPLOYEE_ORDERING_PATIENT");
		}
		else
		{
			if (!flag)
			{
				entity.GetComponent<AnimModelComponent>().SetClothingLevel(entity.GetComponent<BehaviorPatient>().GetTreatedSymptomDressLevel(m_stateData.GetProcedure().DressLevelAfter));
			}
			ReserveCabinet();
		}
	}

	private void UpdateStateEmployeeOrderingPatient()
	{
		if (m_stateData.m_timeInState > 1f)
		{
			Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
			PerkComponent component = entity.GetComponent<PerkComponent>();
			if (component.m_perkSet.HasPerk("PERK_AGGRESSIVE"))
			{
				SpeakCharacter(entity, "DIALOGUE_PATIENT_ANGRY", "BUBBLE_PERK_AGGRESSIVE", -1f);
				SwitchState("PROCEDURE_DELAYED");
			}
			else
			{
				SwitchState("PROCEDURE_STARTED");
			}
		}
	}

	private void UpdateProcedureStarted()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		CheckPerkModifiers(entity, employeeCharacter);
		AnimModelComponent component = employeeCharacter.GetComponent<AnimModelComponent>();
		BehaviorPatient component2 = entity.GetComponent<BehaviorPatient>();
		if (employeeCharacter.GetComponent<PerkComponent>().m_perkSet.HasPerk("PERK_PEOPLE_PERSON") && entity.GetComponent<PerkComponent>().m_perkSet.GetHiddenPerkCount() > 0)
		{
			if (employeeCharacter.GetComponent<PerkComponent>().m_perkSet.HasHiddenPerk("PERK_PEOPLE_PERSON"))
			{
				employeeCharacter.GetComponent<PerkComponent>().RevealPerk("PERK_PEOPLE_PERSON", showNotifications: true);
			}
			entity.GetComponent<PerkComponent>().RevealAllPerks(component2.IsBookmarked());
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

	private void UpdateStateProcedureIdle()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (employeeCharacter.GetComponent<AnimModelComponent>().IsIdle())
		{
			if (m_stateData.m_examination == Database.Instance.GetEntry<GameDBExamination>("EXM_BLOOD_TEST_SAMPLING") && m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>().m_state.m_controlMode == PatientControlMode.PlayerControl)
			{
				PlayerStatistics.Instance.IncrementStatistic("STAT_EXM_BLOOD_DRAW");
			}
			SilenceCharacter(employeeCharacter);
			SetParam(0, 1f);
			Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
			if (entity.GetComponent<AnimModelComponent>().m_state.m_dressLevel != DressLevel.FULL)
			{
				SpeakCharacter(employeeCharacter, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_DRESS", 3f);
				SwitchState("PROCEDURE_PATIENT_DRESS");
			}
			else
			{
				ReserveCabinet();
			}
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
		// Mod: exam bundling, as the vanilla script's prefix.
		if (ExamBundling.ContinueBloodDraw(this))
		{
			return;
		}
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (m_stateData.m_timeInState > 1f && GetParam(4) < 1f)
		{
			SetParam(4, 1f);
			if (m_stateData.m_examination.Entry.DatabaseID.ToString() == "EXM_ORAL_CAVITY_INSPECTION")
			{
				entity.GetComponent<SpeechComponent>().PlayDialogue("DIALOGUE_PATIENT_AAA");
			}
			else if (m_stateData.m_examination.Entry.DiscomfortLevel > SymptomHazard.Low)
			{
				entity.GetComponent<SpeechComponent>().PlayDialogue("DIALOGUE_PATIENT_OUCH");
			}
		}
		if (m_stateData.m_timeInState > GetParam(2))
		{
			AnimationSetup animationSetup = CheckAnimationSetup();
			AnimModelComponent component = employeeCharacter.GetComponent<AnimModelComponent>();
			component.PlayAnimation(animationSetup.AnimationNameOut, looping: false);
			SwitchState("PROCEDURE_IDLE");
		}
	}

	private void UpdateStateGoingToSink()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (!employeeCharacter.GetComponent<WalkComponent>().IsBusy())
		{
			employeeCharacter.GetComponent<UseComponent>().Activate(UseComponentMode.SINGLE_USE);
			SwitchState("USE_SINK");
		}
	}

	private void UpdateStateUseSink()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (!employeeCharacter.GetComponent<UseComponent>().IsBusy())
		{
			CheckEndStep();
		}
	}

	private void UpdateStateConsultation()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		AnimModelComponent component = employeeCharacter.GetComponent<AnimModelComponent>();
		if (component.IsIdle())
		{
			employeeCharacter.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
			SwitchState("PROCEDURE_FINISHED");
		}
	}

	private void UpdateStateProcedureFinished()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (!entity.GetComponent<BehaviorPatient>().MainSymptomTreated() || !entity.GetComponent<BehaviorPatient>().m_state.m_medicalCondition.m_gameDBMedicalCondition.Entry.FullyBandaged || !entity.GetComponent<HospitalizationComponent>().IsInOwnBed())
		{
			entity.GetComponent<AnimModelComponent>().SetClothingLevel(entity.GetComponent<BehaviorPatient>().GetTreatedSymptomDressLevel());
		}
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
		SendMessage(employeeCharacter, new Message(Messages.NURSE_FINISHED_PROCEDURE));
		m_stateData.m_procedureScene.EmployeeCharacter = null;
		entity.GetComponent<BehaviorPatient>().ReceiveMessage(new Message(Messages.BOREDOM_REDUCED, 10f));
		if (m_stateData.GetProcedure().RequiredStatLabQualificationRef != null)
		{
			employeeCharacter.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(new DatabaseEntryRef<GameDBSkill>[1] { m_stateData.GetProcedure().RequiredStatLabQualificationRef }, Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_EXAMINATION_EQUIPMENT_SKILL_POINTS").Value);
		}
		else
		{
			employeeCharacter.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(NurseExams.NurseQualifications, Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_EXAMINATION_EQUIPMENT_SKILL_POINTS").Value);
		}
		if (!entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			MakePatientSitAgain();
		}
		SwitchState("IDLE");
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

	private void SetExaminationOrientation()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		AnimModelComponent component = employeeCharacter.GetComponent<AnimModelComponent>();
		BehaviorPatient component2 = entity.GetComponent<BehaviorPatient>();
		if (entity.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			component.SetDirection(component2.GetLyingInteractionOrientation(LyingInteractionPositionsCheck()));
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
		if (m_stateData.m_timeInState > 5f)
		{
			Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
			SilenceCharacter(entity);
			SwitchState("PROCEDURE_STARTED");
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

	private void CheckSink()
	{
		if (GetEquipment(2) != null)
		{
			ReserveSink();
		}
		else
		{
			CheckEndStep();
		}
	}

	private void CheckEndStep()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		// Vanilla: ambiguous-diagnosis PROCEDURE_CONSULTATION branch (doctors only). Nurses never consult.
		SwitchState("PROCEDURE_FINISHED");
	}

	private LyingInteractionPositions LyingInteractionPositionsCheck()
	{
		if (GetEquipment(1).HasTag("staff_use_close_left"))
		{
			if (m_stateData.GetProcedure().TopProcedure)
			{
				return LyingInteractionPositions.LEFT_TOP;
			}
			return LyingInteractionPositions.LEFT;
		}
		if (GetEquipment(1).HasTag("staff_use_close_right"))
		{
			if (m_stateData.GetProcedure().TopProcedure)
			{
				return LyingInteractionPositions.RIGHT_TOP;
			}
			return LyingInteractionPositions.RIGHT;
		}
		if (m_stateData.GetProcedure().BottomProcedure)
		{
			return LyingInteractionPositions.RIGHT_BOTTOM;
		}
		if (m_stateData.GetProcedure().TopProcedure)
		{
			return LyingInteractionPositions.RIGHT_TOP;
		}
		return LyingInteractionPositions.RIGHT;
	}

	private void DiscomfortCheck()
	{
		Entity entity = m_stateData.m_procedureScene.m_patient.GetEntity();
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		if (employeeCharacter.GetComponent<PerkComponent>().m_perkSet.HasPerk("PERK_COMFORTING"))
		{
			if (employeeCharacter.GetComponent<PerkComponent>().m_perkSet.HasHiddenPerk("PERK_COMFORTING"))
			{
				employeeCharacter.GetComponent<PerkComponent>().RevealPerk("PERK_COMFORTING", MainCharacterBookmarked());
			}
		}
		else
		{
			entity.GetComponent<MoodComponent>().AddDiscomfortModifier(entity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_activeExamination.Entry);
		}
	}

	private bool MainCharacterBookmarked()
	{
		Entity mainCharacter = m_stateData.m_procedureScene.MainCharacter;
		if (mainCharacter.GetComponent<BehaviorNurse>() != null && mainCharacter.GetComponent<BehaviorNurse>().IsBookmarked())
		{
			return true;
		}
		if (mainCharacter.GetComponent<BehaviorLabSpecialist>() != null && mainCharacter.GetComponent<BehaviorLabSpecialist>().m_state.m_bookmarked)
		{
			return true;
		}
		return false;
	}

	public override bool IsIdle()
	{
		return m_stateData.m_state == "IDLE";
	}
}
