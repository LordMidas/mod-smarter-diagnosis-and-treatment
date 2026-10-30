using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Vanilla notices a wrong final diagnosis (it dropped out of the possible diagnoses) only with the
    /// NOTIF_WRONG_DIAGNOSIS popup, a mood penalty and the statistics: the diagnosis and its planned treatments stay
    /// until the player removes it on the patient card. Here the AI doctor does what that card button does, once the
    /// popup for this patient is closed, and also drops the treatments only the wrong diagnosis called for.
    /// Stateless: the trigger is vanilla's saved m_wrongDiagnosisDiscovered flag, and popups are not saved, so after
    /// a load (or with the notification set to log or off) the correction happens at the next decision point.
    /// </summary>
    public static class WrongDiagnosis
    {
        private const string NotificationId = "NOTIF_WRONG_DIAGNOSIS";

        public static bool NeedsCorrection(BehaviorPatient patient)
        {
            if (patient == null || patient.GetControlMode() != PatientControlMode.AI)
                return false;

            MedicalCondition medicalCondition = patient.m_state.m_medicalCondition;
            // The card button clears both, so a diagnosis the player already removed is never touched here.
            return medicalCondition.m_diagnosedMedicalCondition != null &&
                   medicalCondition.m_wrongDiagnosisDiscovered &&
                   !HasOpenPopup(patient.m_entity);
        }

        // Returns false when the correction has to wait, i.e. a reserved treatment that would be dropped cannot be
        // cancelled while the hospitalized patient is being moved.
        public static bool TryCorrect(BehaviorPatient patient)
        {
            Entity entity = patient.m_entity;
            MedicalCondition medicalCondition = patient.m_state.m_medicalCondition;
            GameDBMedicalCondition wrong = medicalCondition.m_diagnosedMedicalCondition.Entry;
            ProcedureComponent procedureComponent = entity.GetComponent<ProcedureComponent>();
            HospitalizationComponent hospitalization = entity.GetComponent<HospitalizationComponent>();
            List<GameDBTreatment> dropped = GetUnjustifiedTreatments(patient, wrong, procedureComponent);

            ProcedureScript reserved = procedureComponent.m_state.m_reservedProcedureScript.GetEntity();
            if (reserved != null && reserved.m_stateData.m_treatment.IsValid &&
                dropped.Contains(reserved.m_stateData.m_treatment.Entry))
            {
                // Same guard as the surgery hold in HospitalizationComponent_TryToStartScheduledTreatment.
                if (hospitalization.IsHospitalized() && hospitalization.m_state.m_oustideRoom)
                    return false;

                procedureComponent.CancelReservation();
            }

            ProcedureQueue queue = procedureComponent.m_state.m_procedureQueue;
            for (int index = 0; index < dropped.Count; index++)
                queue.RemovePlannedTreatment(dropped[index]);

            // Vanilla's own bookkeeping for a wrong diagnosis (m_wrongDiagnoses, which the scoring excludes), then
            // the flag reset of the card button. m_diagnosedByPlayer stays as it was.
            medicalCondition.UpdateWrongDiagnosis();
            medicalCondition.m_wrongDiagnosisDiscovered = false;
            if (hospitalization.IsHospitalized())
                hospitalization.m_state.m_preemptiveExaminatioPlanned = false;

            medicalCondition.UpdatePossibleDiagnoses(entity);
            if (ModConfig.General.EnableDiagnostics)
                entity.LogWarning("Wrong diagnosis " + wrong.DatabaseID + " dropped with " + dropped.Count + " planned treatment(s)");

            return true;
        }

        private static bool HasOpenPopup(Entity patient)
        {
            NotificationManager manager = NotificationManager.GetInstance();
            if (manager == null || manager.m_popups == null)
                return false;

            foreach (NotificationMessage message in manager.m_popups)
            {
                if (message.m_textTitleLocID == NotificationId &&
                    message.m_character != null &&
                    message.m_character.GetEntity() == patient)
                {
                    return true;
                }
            }

            return false;
        }

        // Planned treatments of the wrong diagnosis that no visible symptom calls for, plus a planned hospitalization
        // that only those needed.
        private static List<GameDBTreatment> GetUnjustifiedTreatments(
            BehaviorPatient patient,
            GameDBMedicalCondition wrong,
            ProcedureComponent procedureComponent)
        {
            HashSet<GameDBTreatment> wrongTreatments = new HashSet<GameDBTreatment>();
            if (wrong.Treatments != null)
            {
                foreach (DatabaseEntryRef<GameDBTreatment> treatmentRef in wrong.Treatments)
                {
                    if (treatmentRef.Entry != null)
                        wrongTreatments.Add(treatmentRef.Entry);
                }
            }

            HashSet<GameDBTreatment> symptomTreatments = new HashSet<GameDBTreatment>();
            foreach (Symptom symptom in patient.m_state.m_medicalCondition.m_symptoms)
            {
                GameDBSymptom entry = symptom.m_symptom.Entry;
                if (entry == null || !symptom.m_spawned || symptom.m_hidden || !symptom.m_active || entry.Treatments == null)
                    continue;

                foreach (DatabaseEntryRef<GameDBTreatment> treatmentRef in entry.Treatments)
                {
                    if (treatmentRef.Entry != null)
                        symptomTreatments.Add(treatmentRef.Entry);
                }
            }

            List<GameDBTreatment> dropped = new List<GameDBTreatment>();
            HashSet<GameDBTreatment> neededHospitalizations = new HashSet<GameDBTreatment>();
            List<PlannedTreatmentState> planned = procedureComponent.m_state.m_procedureQueue.m_plannedTreatmentStates;
            foreach (PlannedTreatmentState state in planned)
            {
                GameDBTreatment treatment = state.m_treatment.Entry;
                if (treatment == null || treatment.TreatmentType == TreatmentType.HOSPITALIZATION)
                    continue;

                if (wrongTreatments.Contains(treatment) && !symptomTreatments.Contains(treatment))
                {
                    if (!dropped.Contains(treatment))
                        dropped.Add(treatment);
                }
                else if (treatment.HospitalizationTreatmentRef != null && treatment.HospitalizationTreatmentRef.Entry != null)
                {
                    neededHospitalizations.Add(treatment.HospitalizationTreatmentRef.Entry);
                }
            }

            foreach (PlannedTreatmentState state in planned)
            {
                GameDBTreatment treatment = state.m_treatment.Entry;
                if (treatment != null && treatment.TreatmentType == TreatmentType.HOSPITALIZATION &&
                    wrongTreatments.Contains(treatment) && !symptomTreatments.Contains(treatment) &&
                    !neededHospitalizations.Contains(treatment) && !dropped.Contains(treatment))
                {
                    dropped.Add(treatment);
                }
            }

            return dropped;
        }
    }

    // Outpatients: before vanilla (and the mod's other Prefix) decides the next exam or treatment.
    [HarmonyPatch(typeof(BehaviorPatient), "SelectNextProcedure")]
    [HarmonyPriority(Priority.First)]
    public static class BehaviorPatient_SelectNextProcedure_WrongDiagnosis
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.WrongDiagnosis.AutoCorrect, nameof(BehaviorPatient_SelectNextProcedure_WrongDiagnosis));

        public static void Prefix(BehaviorPatient __instance)
        {
            if (WrongDiagnosis.NeedsCorrection(__instance) &&
                !__instance.m_entity.GetComponent<HospitalizationComponent>().IsHospitalized())
            {
                WrongDiagnosis.TryCorrect(__instance);
            }
        }
    }

    // Hospitalized patients: before vanilla's next step, which then diagnoses again or schedules an exam.
    [HarmonyPatch(typeof(HospitalizationComponent), "SelectNextStep")]
    [HarmonyPriority(Priority.First)]
    public static class HospitalizationComponent_SelectNextStep_WrongDiagnosis
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.WrongDiagnosis.AutoCorrect, nameof(HospitalizationComponent_SelectNextStep_WrongDiagnosis));

        public static void Prefix(HospitalizationComponent __instance)
        {
            BehaviorPatient patient = __instance.m_entity.GetComponent<BehaviorPatient>();
            if (WrongDiagnosis.NeedsCorrection(patient))
                WrongDiagnosis.TryCorrect(patient);
        }
    }
}
