using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Two jobs, in this order:
    /// 1. Vanilla fix (always on): at a lab specialist's room only the planned exam starts, see <see cref="StartLabExamOnly"/>.
    /// 2. Clinic patients (AI controlled) with a collapse-risk symptom get its treatment before any exam or transfer.
    /// Vanilla only plans critical treatments at diagnosis, so a symptom revealed by an exam is otherwise ignored
    /// and the patient is transferred or sent away untreated.
    /// </summary>
    [HarmonyPatch(typeof(BehaviorPatient), "SelectNextProcedure")]
    public static class BehaviorPatient_SelectNextProcedure
    {
        private const double PlanRetryHours = 0.1;
        private const double DropRetryHours = 1.0;
        private const int PruneThreshold = 32;
        private const double PruneAgeHours = 24.0;

        private static readonly Dictionary<uint, double> nextPlanTime = new Dictionary<uint, double>();
        private static readonly Dictionary<uint, double> blockedSince = new Dictionary<uint, double>();

        // Always patched because of the lab-room fix; CriticalTreatment/ClinicEnabled gates the critical part in Prefix.
        private static bool Prepare() => true;

        public static bool Prefix(BehaviorPatient __instance)
        {
            if (StartLabExamOnly(__instance))
                return false;

            if (!ModConfig.Critical.ClinicEnabled)
                return true;

            BehaviorPatient patient = __instance;
            BehaviorPatientStateData state = patient.m_state;
            if (state.m_sentHome || state.m_deathTriggered ||
                state.m_doctor.GetEntity() == null ||
                patient.GetControlMode() != PatientControlMode.AI)
            {
                return true;
            }

            if (!CriticalTreatments.HasCriticalSymptom(state.m_medicalCondition))
                return true;

            if (patient.m_entity.GetComponent<HospitalizationComponent>().IsHospitalized())
                return true;

            uint id = patient.m_entity.GetEntityID();
            double now = CriticalTreatments.GetGameHours();
            ProcedureComponent procedureComponent = patient.m_entity.GetComponent<ProcedureComponent>();
            List<PlannedTreatmentState> queue = procedureComponent.m_state.m_procedureQueue.m_plannedTreatmentStates;

            if (CriticalTreatments.HasUntreatedCriticalSymptom(patient, false))
            {
                double due;
                if (!nextPlanTime.TryGetValue(id, out due) || now >= due)
                {
                    PlanCriticalTreatments(patient, procedureComponent);
                    nextPlanTime[id] = now + PlanRetryHours;
                    Prune(now);
                }
            }

            CriticalTreatments.MoveCriticalAhead(queue, patient, false);

            if (queue.Count == 0)
                return true;

            GameDBTreatment first = queue[0].m_treatment.Entry;
            if (!CriticalTreatments.IsCriticalTreatment(patient, first) ||
                first.TreatmentType == TreatmentType.RECEIPT ||
                CriticalTreatments.CanStartInClinic(patient, procedureComponent, first))
            {
                blockedSince.Remove(id);
                return true;
            }

            double since;
            if (!blockedSince.TryGetValue(id, out since))
            {
                since = now;
                blockedSince[id] = since;
            }

            if (now - since > ModConfig.Critical.ClinicBlockedWaitHours)
            {
                // Waited long enough: give up on this entry so vanilla continues with an exam or a transfer.
                queue.RemoveAt(0);
                blockedSince.Remove(id);
                nextPlanTime[id] = now + DropRetryHours;
                return true;
            }

            // Vanilla would send the patient away untreated when the first treatment cannot start yet.
            patient.GoToWaitingRoom();
            return false;
        }

        // Vanilla fix. When a clinic patient walks into a lab specialist's room (radiology, cardio unit, ...),
        // UpdateStateGoingToDoctor calls SelectNextProcedure with the clinic doctor still assigned (FreeDoctor only
        // clears the doctor's CurrentPatient). So the absent doctor diagnoses, plans critical treatments and tries to
        // start a treatment there: a prescription sends the patient to the waiting room, then the planned exam starts
        // anyway. The patient comes back later with a treatment chosen without this exam's result.
        // Here only the planned exam starts; the doctor decides at the next office visit, with every result in.
        private static bool StartLabExamOnly(BehaviorPatient patient)
        {
            BehaviorPatientStateData state = patient.m_state;
            if (state.m_labSpecialist == null || state.m_patientState != PatientState.GoingToDoctor)
                return false;

            if (patient.m_entity.GetComponent<HospitalizationComponent>().IsHospitalized())
                return false;

            if (patient.m_entity.GetComponent<ProcedureComponent>().m_state.m_procedureQueue.m_plannedExaminationStates.Count == 0)
                return false;

            patient.TryToStartScheduledExamination();
            return true;
        }

        private static void PlanCriticalTreatments(BehaviorPatient patient, ProcedureComponent procedureComponent)
        {
            MedicalCondition medicalCondition = patient.m_state.m_medicalCondition;
            FakeMap<GameDBTreatment, ProcedureSceneAvailability> availability =
                procedureComponent.GetAllTreatmentsForMedicalCondition(medicalCondition, TreatmentPlanningMode.ALL_SYMPTOMS);
            ProcedureQueue procedureQueue = procedureComponent.m_state.m_procedureQueue;

            List<Symptom> symptoms = medicalCondition.m_symptoms;
            for (int index = 0; index < symptoms.Count; index++)
            {
                Symptom symptom = symptoms[index];
                if (!CriticalTreatments.IsCriticalSymptom(symptom))
                    continue;

                GameDBSymptom entry = symptom.m_symptom.Entry;
                if (entry.Treatments == null ||
                    procedureComponent.HasTreatmentForSymptom(entry) ||
                    procedureComponent.HasPlannedTreatmentForSymptom(entry))
                {
                    continue;
                }

                for (int treatmentIndex = 0; treatmentIndex < entry.Treatments.Length; treatmentIndex++)
                {
                    GameDBTreatment treatment = entry.Treatments[treatmentIndex].Entry;
                    if (treatment.TreatmentType == TreatmentType.SURGERY ||
                        treatment.TreatmentType == TreatmentType.HOSPITALIZATION ||
                        !CriticalTreatments.IsAvailable(availability, treatment))
                    {
                        continue;
                    }

                    GameDBTreatment hospitalization = treatment.HospitalizationTreatmentRef == null
                        ? null
                        : treatment.HospitalizationTreatmentRef.Entry;
                    if (hospitalization != null)
                    {
                        if (!CriticalTreatments.IsAvailable(availability, hospitalization))
                            continue;

                        procedureQueue.AddPlannedTreatment(hospitalization);
                    }

                    procedureQueue.AddPlannedTreatment(treatment);
                    break;
                }
            }
        }

        private static void Prune(double now)
        {
            if (nextPlanTime.Count > PruneThreshold)
                PruneOlderThan(nextPlanTime, now - PruneAgeHours);

            if (blockedSince.Count > PruneThreshold)
                PruneOlderThan(blockedSince, now - PruneAgeHours);
        }

        private static void PruneOlderThan(Dictionary<uint, double> times, double cutoff)
        {
            List<uint> stale = null;
            foreach (KeyValuePair<uint, double> pair in times)
            {
                if (pair.Value >= cutoff)
                    continue;

                if (stale == null)
                    stale = new List<uint>();

                stale.Add(pair.Key);
            }

            if (stale == null)
                return;

            for (int index = 0; index < stale.Count; index++)
                times.Remove(stale[index]);
        }
    }
}
