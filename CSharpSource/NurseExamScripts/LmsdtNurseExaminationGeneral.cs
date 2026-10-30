// Nurse-performed port of vanilla Lopital.ProcedureScriptExaminationGeneral. Re-diff against the vanilla script after game updates.
// Deviations: performer is the nurse (m_procedureScene.Nurse), no doctor-behavior use, skill from NurseExams.NurseQualifications,
// release via NURSE_FINISHED_PROCEDURE.
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment;

public class LmsdtNurseExaminationGeneral : ProcedureScript
{
	public const string STATE_IDLE = "IDLE";

	public const string STATE_STARTED = "STARTED";

	public const string STATE_EMPLOYEE_ORDERING_PATIENT_SIT = "EMPLOYEE_ORDERING_PATIENT_SIT";

	public const string STATE_PATIENT_GOING_TO_PROCEDURE = "PATIENT_GOING_TO_PROCEDURE";

	public const string STATE_PROCEDURE_PATIENT_DRESS = "PROCEDURE_PATIENT_DRESS";

	public const string STATE_DOCTOR_GOING_TO_ROOM = "DOCTOR_GOING_TO_ROOM";

	public const string STATE_EMPLOYEE_WAITING_FOR_PATIENT = "EMPLOYEE_WAITING_FOR_PATIENT";

	public const string STATE_EMPLOYEE_STOPPED_WAITING_FOR_PATIENT = "EMPLOYEE_STOPPED_WAITING_FOR_PATIENT";

	public const string STATE_DOCTOR_GOING_TO_PROCEDURE = "DOCTOR_GOING_TO_PROCEDURE";

	public const string STATE_EMPLOYEE_ORDERING_PATIENT_EXAM = "EMPLOYEE_ORDERING_PATIENT";

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

	private const int PARAM_PATIENT_SAID_OUCH = 3;

	private const int BED = 0;

	private const int SINK = 1;

	private const int MOBILE_WORKSTATION = 2;

	public override void ScriptUpdate(float deltaTime)
	{
		m_stateData.m_timeInState += deltaTime;
		if (m_stateData.m_state == "EMPLOYEE_ORDERING_PATIENT_SIT")
		{
			UpdateStateEmployeeOrderingPatientSit();
		}
		else if (m_stateData.m_state == "PATIENT_GOING_TO_PROCEDURE")
		{
			UpdateStatePatientGoingToProcedure();
		}
		else if (m_stateData.m_state == "DOCTOR_GOING_TO_ROOM")
		{
			UpdateStatePatientGoingToRoom();
		}
		else if (m_stateData.m_state == "EMPLOYEE_WAITING_FOR_PATIENT")
		{
			UpdateStateEmployeeWaitingForPatient();
		}
		else if (m_stateData.m_state == "EMPLOYEE_STOPPED_WAITING_FOR_PATIENT")
		{
			UpdateStateEmployeeStoppedWaitingForPatient();
		}
		else if (m_stateData.m_state == "PROCEDURE_PATIENT_DRESS")
		{
			UpdateStatePatientDress();
		}
		else if (m_stateData.m_state == "DOCTOR_GOING_TO_PROCEDURE")
		{
			UpdateStateDoctorGoingToProcedure();
		}
		else if (m_stateData.m_state == "EMPLOYEE_ORDERING_PATIENT")
		{
			UpdateStateEmployeeOrderingPatientExam();
		}
		else if (m_stateData.m_state == "PROCEDURE_STARTED")
		{
			UpdateExamStarted();
		}
		else if (m_stateData.m_state == "PROCEDURE_DELAYED")
		{
			SetExaminationOrientation();
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
		// Unreachable for nurses (no consultation branch); kept to match vanilla for re-diffing.
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
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		BehaviorNurse nurseBehavior = entity.GetComponent<BehaviorNurse>();
		if (nurseBehavior != null && nurseBehavior.m_state.m_nurseState != NurseState.OverridenReservedForProcedure && nurseBehavior.m_state.m_nurseState != NurseState.Idle && nurseBehavior.m_state.m_nurseState != NurseState.FinishedProcedure && nurseBehavior.m_state.m_nurseState != NurseState.GoingToWorkplace)
		{
			entity.LogError("Nurse is in a wrong state: " + nurseBehavior.m_state.m_nurseState);
		}
		SendMessage(entity, new Message(Messages.OVERRIDE_BY_PROCEDURE_SCRIPT));
		WalkComponent component = entity2.GetComponent<WalkComponent>();
		DiscomfortCheck();
		TileObject equipment = GetEquipment(0);
		float anyApplicableSkillLevel = m_stateData.m_procedureScene.Nurse.GetEntity().GetComponent<EmployeeComponent>().GetAnyApplicableSkillLevel(NurseExams.NurseQualifications);
		SetParam(1, anyApplicableSkillLevel);
		SetParam(2, GetActionTime(entity, 20, anyApplicableSkillLevel));
		if (!entity2.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			Room entity3 = entity.GetComponent<EmployeeComponent>().m_state.m_homeRoom.GetEntity();
			if (entity3 != null)
			{
				m_stateData.m_procedureScene.m_room = entity3;
				entity3.m_roomPersistentData.m_currentProcedureOwner = this;
			}
		}
		if (equipment != null && !component.IsSittingOn(equipment) && !entity2.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			SpeakCharacter(entity, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_EXAM_BED", -1f);
			SwitchState("EMPLOYEE_ORDERING_PATIENT_SIT");
			return;
		}
		if (entity2.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			Room roomAt = MapScriptInterface.Instance.GetRoomAt(entity.GetComponent<WalkComponent>().GetCurrentTile(), entity.GetComponent<WalkComponent>().GetFloorIndex());
			Room roomAt2 = MapScriptInterface.Instance.GetRoomAt(entity2.GetComponent<WalkComponent>().GetCurrentTile(), entity2.GetComponent<WalkComponent>().GetFloorIndex());
			Room entity4 = m_stateData.m_procedureScene.m_room.GetEntity();
			if (entity4 != null)
			{
				m_stateData.m_procedureScene.m_room = entity4;
				entity4.m_roomPersistentData.m_currentProcedureOwner = this;
			}
			if (entity4 != null && (roomAt != entity4 || roomAt2 != entity4))
			{
				TileObject tileObject = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(entity2, null, component.GetCurrentTile(), entity4, "mobile_workstation", AccessRights.STAFF);
				if (tileObject != null)
				{
					ref EntityIDPointer<TileObject> reference = ref m_stateData.m_procedureScene.m_equipment[2];
					reference = tileObject;
					entity.GetComponent<WalkComponent>().SetDestination(tileObject.GetDefaultUsePosition(), tileObject.GetFloorIndex());
					entity.GetComponent<UseComponent>().ReserveObject(tileObject);
				}
				else
				{
					entity.GetComponent<WalkComponent>().SetDestination(MapScriptInterface.Instance.GetRandomFreePosition(entity4, AccessRights.STAFF), entity4.GetFloorIndex());
				}
				SwitchState("DOCTOR_GOING_TO_ROOM");
			}
			else
			{
				SwitchState("PATIENT_GOING_TO_PROCEDURE");
			}
			return;
		}
		if (entity2.GetComponent<AnimModelComponent>().m_state.m_dressLevel != m_stateData.GetProcedure().DressLevel)
		{
			SpeakCharacter(entity, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_DRESS", 3f);
			SwitchState("PROCEDURE_PATIENT_DRESS");
			return;
		}
		WalkComponent component2 = entity.GetComponent<WalkComponent>();
		BehaviorPatient component3 = m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>();
		Vector2i facingInteractionPosition = component3.GetFacingInteractionPosition();
		if (component2.GetCurrentTile() != facingInteractionPosition)
		{
			SilenceCharacter(entity);
			ChooseDoctorsPosition();
			SwitchState("DOCTOR_GOING_TO_PROCEDURE");
			return;
		}
		PerkComponent component4 = entity2.GetComponent<PerkComponent>();
		if (component4.m_perkSet.HasPerk("PERK_AGGRESSIVE"))
		{
			SpeakCharacter(entity2, "DIALOGUE_PATIENT_ANGRY", "BUBBLE_PERK_AGGRESSIVE", -1f);
			SwitchState("PROCEDURE_DELAYED");
		}
		else
		{
			SwitchState("PROCEDURE_STARTED");
		}
	}

	private void UpdateStatePatientGoingToRoom()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		if (!component.IsBusy())
		{
			Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
			if (GetEquipment(2) == null)
			{
				employeeCharacter.GetComponent<AnimModelComponent>().PlayAnimation("stand_wait");
			}
			else
			{
				employeeCharacter.GetComponent<UseComponent>().Activate(UseComponentMode.UNTIL_INTERRUPTED);
			}
			SwitchState("EMPLOYEE_WAITING_FOR_PATIENT");
		}
	}

	private void UpdateStateEmployeeWaitingForPatient()
	{
		if (m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<StretcherComponent>().IsIdle())
		{
			Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
			if (GetEquipment(2) != null)
			{
				employeeCharacter.GetComponent<UseComponent>().Interrupt();
			}
			SwitchState("EMPLOYEE_STOPPED_WAITING_FOR_PATIENT");
		}
	}

	private void UpdateStateEmployeeStoppedWaitingForPatient()
	{
		if (GetEquipment(2) != null)
		{
			if (m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<AnimModelComponent>().IsIdle())
			{
				SwitchState("PATIENT_GOING_TO_PROCEDURE");
			}
		}
		else if (!m_stateData.m_procedureScene.EmployeeCharacter.GetComponent<UseComponent>().IsBusy())
		{
			SwitchState("PATIENT_GOING_TO_PROCEDURE");
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

	private void UpdateStatePatientGoingToProcedure()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		WalkComponent component = entity2.GetComponent<WalkComponent>();
		if (!component.IsBusy())
		{
			SilenceCharacter(entity);
			if (entity2.GetComponent<AnimModelComponent>().m_state.m_dressLevel != m_stateData.GetProcedure().DressLevel)
			{
				SpeakCharacter(entity, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_DRESS", 3f);
				SwitchState("PROCEDURE_PATIENT_DRESS");
			}
			else
			{
				ChooseDoctorsPosition();
				SwitchState("DOCTOR_GOING_TO_PROCEDURE");
			}
		}
	}

	private void UpdateStatePatientDress()
	{
		if (!(m_stateData.m_timeInState > 3f))
		{
			return;
		}
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		SilenceCharacter(entity2);
		bool flag = entity2.GetComponent<BehaviorPatient>().MainSymptomTreated() && entity2.GetComponent<BehaviorPatient>().m_state.m_medicalCondition.m_gameDBMedicalCondition.Entry.FullyBandaged && entity2.GetComponent<HospitalizationComponent>().IsInOwnBed();
		if (GetParam(0) < 1f)
		{
			if (!flag)
			{
				entity2.GetComponent<AnimModelComponent>().SetClothingLevel(entity2.GetComponent<BehaviorPatient>().GetTreatedSymptomDressLevel(m_stateData.GetProcedure().DressLevel));
			}
			ChooseDoctorsPosition();
			SwitchState("DOCTOR_GOING_TO_PROCEDURE");
		}
		else
		{
			if (!flag)
			{
				entity2.GetComponent<AnimModelComponent>().SetClothingLevel(entity2.GetComponent<BehaviorPatient>().GetTreatedSymptomDressLevel(m_stateData.GetProcedure().DressLevelAfter));
			}
			CheckSink();
		}
	}

	private void UpdateStateDoctorGoingToProcedure()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		if (!component.IsBusy())
		{
			AnimModelComponent component2 = entity.GetComponent<AnimModelComponent>();
			Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
			BehaviorPatient component3 = entity2.GetComponent<BehaviorPatient>();
			entity.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
			SilenceCharacter(entity);
			SpeakCharacter(entity, "DIALOGUE_STAFF_ORDER_PATIENT", m_stateData.m_examination.Entry.IconIndex, -1f);
			if (entity2.GetComponent<HospitalizationComponent>().IsHospitalized())
			{
				component2.SetDirection(component3.GetLyingInteractionOrientation(LyingInteractionPositionsCheck()));
			}
			else
			{
				component2.SetDirection(component3.GetFacingInteractionOrientation());
			}
			SwitchState("EMPLOYEE_ORDERING_PATIENT");
		}
	}

	private void UpdateStateEmployeeOrderingPatientExam()
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

	private void UpdateExamStarted()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		CheckPerkModifiers(entity2, entity);
		AnimModelComponent component = entity.GetComponent<AnimModelComponent>();
		BehaviorPatient component2 = entity2.GetComponent<BehaviorPatient>();
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

	private void UpdateStateProcedureIdle()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		if (entity.GetComponent<AnimModelComponent>().IsIdle())
		{
			SetParam(0, 1f);
			SilenceCharacter(entity);
			Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
			if (entity2.GetComponent<AnimModelComponent>().m_state.m_dressLevel != DressLevel.FULL)
			{
				SpeakCharacter(entity, "DIALOGUE_STAFF_ORDER_PATIENT", "BUBBLE_DRESS", 3f);
				SwitchState("PROCEDURE_PATIENT_DRESS");
			}
			else
			{
				CheckSink();
			}
		}
	}

	private void UpdateStateProcedureIdleAnimationIn()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		AnimModelComponent component = entity.GetComponent<AnimModelComponent>();
		if (component.IsIdle())
		{
			AnimationSetup animationSetup = CheckAnimationSetup();
			component.PlayAnimation(animationSetup.AnimationNameIdle);
			SwitchState("PROCEDURE_IDLE_ANIMATION");
		}
	}

	private void UpdateStateProcedureIdleAnimation()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		if (m_stateData.m_timeInState > 1f && GetParam(3) < 1f)
		{
			SetParam(3, 1f);
			if (m_stateData.m_examination.Entry.DiscomfortLevel > SymptomHazard.Low)
			{
				entity2.GetComponent<SpeechComponent>().PlayDialogue("DIALOGUE_PATIENT_OUCH");
			}
		}
		if (m_stateData.m_timeInState > GetParam(2))
		{
			AnimModelComponent component = entity.GetComponent<AnimModelComponent>();
			AnimationSetup animationSetup = CheckAnimationSetup();
			component.PlayAnimation(animationSetup.AnimationNameOut, looping: false);
			SwitchState("PROCEDURE_IDLE");
		}
	}

	private void SetExaminationOrientation()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		AnimModelComponent component = entity.GetComponent<AnimModelComponent>();
		BehaviorPatient component2 = entity2.GetComponent<BehaviorPatient>();
		if (entity2.GetComponent<HospitalizationComponent>().IsHospitalized())
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
			CheckEndStep();
		}
	}

	private void UpdateStateConsultation()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		AnimModelComponent component = entity.GetComponent<AnimModelComponent>();
		if (component.IsIdle())
		{
			entity.GetComponent<AnimModelComponent>().PlayAnimation("stand_idle");
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
		if (m_stateData.m_examination == Database.Instance.GetEntry<GameDBExamination>("EXM_DIFFERENTIAL_DIAGNOSIS"))
		{
			entity.GetComponent<BehaviorPatient>().m_state.m_medicalCondition.EliminateDiagnoses();
		}
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		SendMessage(employeeCharacter, new Message(Messages.NURSE_FINISHED_PROCEDURE));
		m_stateData.m_procedureScene.EmployeeCharacter = null;
		entity.GetComponent<BehaviorPatient>().ReceiveMessage(new Message(Messages.BOREDOM_REDUCED, 10f));
		if (m_stateData.GetProcedure().RequiredStatLabQualificationRef != null)
		{
			employeeCharacter.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(new DatabaseEntryRef<GameDBSkill>[1] { m_stateData.GetProcedure().RequiredStatLabQualificationRef }, Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_EXAMINATION_SKILL_POINTS").Value);
		}
		else
		{
			employeeCharacter.GetComponent<EmployeeComponent>().AddPointsToAnyApplicableSkillLevel(NurseExams.NurseQualifications, Database.Instance.GetEntry<GameDBTweakableInt>("TWEAKABLE_EXAMINATION_SKILL_POINTS").Value);
		}
		SwitchState("IDLE");
	}

	private void ChooseDoctorsPosition()
	{
		Entity entity = m_stateData.m_procedureScene.Nurse.GetEntity();
		Entity entity2 = m_stateData.m_procedureScene.m_patient.GetEntity();
		WalkComponent component = entity.GetComponent<WalkComponent>();
		BehaviorPatient component2 = m_stateData.m_procedureScene.m_patient.GetEntity().GetComponent<BehaviorPatient>();
		if (entity2.GetComponent<HospitalizationComponent>().IsHospitalized())
		{
			component.SetDestination(component2.GetLyingInteractionPosition(LyingInteractionPositionsCheck()), entity2.GetComponent<WalkComponent>().GetFloorIndex());
		}
		else
		{
			component.SetDestination(component2.GetFacingInteractionPosition(), entity2.GetComponent<WalkComponent>().GetFloorIndex());
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
		if (GetEquipment(1) != null)
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
		// Vanilla: ambiguous-diagnosis PROCEDURE_CONSULTATION branch (doctors only). Nurses never consult.
		SwitchState("PROCEDURE_FINISHED");
	}

	private LyingInteractionPositions LyingInteractionPositionsCheck()
	{
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

	private void ReserveSink()
	{
		Entity employeeCharacter = m_stateData.m_procedureScene.EmployeeCharacter;
		employeeCharacter.GetComponent<UseComponent>().ReserveObject(GetEquipment(1));
		employeeCharacter.GetComponent<WalkComponent>().SetDestination(GetEquipment(1).GetDefaultUsePosition(), GetEquipment(1).GetFloorIndex());
		SwitchState("GOING_TO_SINK");
	}

	public override bool IsIdle()
	{
		return m_stateData.m_state == "IDLE";
	}
}
