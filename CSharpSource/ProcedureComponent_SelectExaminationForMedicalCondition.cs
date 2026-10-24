using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    [HarmonyPatch(typeof(ProcedureComponent), nameof(ProcedureComponent.SelectExaminationForMedicalCondition))]
    // Vanilla returns the first selectable exam of the availability map, i.e. always the highest Procedure.Priority.
    // Here NPC doctors make a weighted random choice by relevance, sharpened by diagnosis skill and scaled by
    // priority. Returning true (vanilla) is the fallback when no exam scores above zero.
    public static class ProcedureComponent_SelectExaminationForMedicalCondition
    {
        private const float MaxDiagnosisSkillLevel = 4f;
        private const int ObservationCheckPriorityThreshold = 30;

        private static bool Prepare() => ModConfig.Patch(ModConfig.NpcExams.Enabled, nameof(ProcedureComponent_SelectExaminationForMedicalCondition));

        public static bool Prefix(
            ProcedureComponent __instance,
            MedicalCondition medicalCondition,
            Department department,
            ref GameDBExamination __result)
        {
            // Refresh the map first so the weighted choice uses current availability and diagnosis scores.
            __instance.UpdateAllExaminationsForMedicalCondition(medicalCondition, -1, false);
            if (medicalCondition.m_possibleDiagnoses.Count == 0)
                return true;

            FakeMap<GameDBExamination, ProcedureSceneAvailability> availability =
                __instance.m_examinationAvailability;
            GetDoctorDiagnosticSkills(
                __instance,
                out float diagnosisSkill,
                out float advancedDiagnosisSkill,
                out bool hasDiagnosticGenius);
            float diagnosisExpertise = Mathf.Clamp01(
                (diagnosisSkill + (hasDiagnosticGenius ? 1f : 0f)) / MaxDiagnosisSkillLevel);
            float differentialDiagnosisBonus =
                ModConfig.NpcExams.DifferentialDiagnosisSpecialistBonus *
                Mathf.Clamp01(advancedDiagnosisSkill / MaxDiagnosisSkillLevel);

            List<GameDBExamination> candidates = new List<GameDBExamination>();
            List<float> scores = new List<float>();
            float totalScore = 0f;
            float highestScore = 0f;
            bool hasHigherPriorityCandidate = false;
            float highestPriority = 1f;

            foreach (GameDBExamination examination in availability.m_keys)
            {
                if (!IsSelectable(examination, availability[examination], __instance.m_state.m_procedureQueue))
                {
                    continue;
                }

                float score = ExaminationRelevance.GetRelevanceScore(examination, medicalCondition);
                if (score <= 0f)
                    continue;

                if (examination == ModDb.DifferentialDiagnosis)
                    score *= 1f + differentialDiagnosisBonus;

                // Favor exams the assigned doctor can perform in-office over lab or referral work.
                if (examination.Procedure.RequiredDoctorQualifications != null &&
                    examination.Procedure.RequiredStatLabQualificationRef == null &&
                    examination.LabTestingExaminationRef == null)
                {
                    score *= ModConfig.NpcExams.OfficeExamMultiplier;
                }

                candidates.Add(examination);
                scores.Add(score);
                highestScore = Mathf.Max(highestScore, score);
                highestPriority = Mathf.Max(highestPriority, examination.Procedure.Priority);
                if (examination.Procedure.Priority > ObservationCheckPriorityThreshold)
                    hasHigherPriorityCandidate = true;
            }
            
            for (int index = 0; index < scores.Count; index++)
            {
                // Higher diagnosis expertise sharpens the weights toward the strongest relevance score.
                float relativeScore = scores[index] / highestScore;
                scores[index] *= Mathf.Pow(relativeScore, diagnosisExpertise);

                // Prioritize exams which have a higher defined Priority for them.
                if (candidates[index] != ModDb.DifferentialDiagnosis)
                {
                    float normalizedPriority = Mathf.Clamp01(candidates[index].Procedure.Priority / highestPriority);
                    scores[index] *= 1f + ModConfig.NpcExams.PriorityMultiplierStrength * normalizedPriority;
                }                

                totalScore += scores[index];
            }

            // Vanilla's non-forced observation check only applies in the Emergency department.
            // Defer low-priority exams there while a relevant, higher-priority exam is available.
            if (hasHigherPriorityCandidate &&
                ModConfig.NpcExams.DeferLowPriorityExamsInEr &&
                CanPatientBeSentToObservation(__instance, medicalCondition, department))
            {
                for (int index = candidates.Count - 1; index >= 0; index--)
                {
                    if (candidates[index].Procedure.Priority <= ObservationCheckPriorityThreshold)
                    {
                        totalScore -= scores[index];
                        candidates.RemoveAt(index);
                        scores.RemoveAt(index);
                    }
                }
            }

            if (candidates.Count == 0 || totalScore <= 0f)
                return true;

            // Use each exam's score as its weight: higher-scoring exams are likelier, not guaranteed.
            float roll = Random.Range(0f, totalScore);
            for (int index = 0; index < candidates.Count; index++)
            {
                roll -= scores[index];
                if (roll < 0f)
                {
                    __result = candidates[index];
                    return false;
                }
            }

            __result = candidates[candidates.Count - 1];
            return false;
        }

        private static void GetDoctorDiagnosticSkills(
            ProcedureComponent procedureComponent,
            out float diagnosisSkill,
            out float advancedDiagnosisSkill,
            out bool hasDiagnosticGenius)
        {
            diagnosisSkill = 0f;
            advancedDiagnosisSkill = 0f;
            hasDiagnosticGenius = false;

            BehaviorPatient patient = procedureComponent.m_entity.GetComponent<BehaviorPatient>();
            if (patient == null || !patient.m_state.m_doctor.CheckEntity())
                return;

            Entity doctor = patient.m_state.m_doctor.GetEntity();
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            if (employee == null)
                return;

            diagnosisSkill = employee.GetSkillLevel("SKILL_DOC_QUALIF_DIAGNOSIS");
            advancedDiagnosisSkill = employee.GetSkillLevel("SKILL_DOC_SPEC_ADVANCED_DIAGNOSIS");

            PerkComponent perks = doctor.GetComponent<PerkComponent>();
            hasDiagnosticGenius = perks != null &&
                perks.m_perkSet.HasPerk("PERK_DIAGNOSTIC_GENIUS");
        }

        private static bool IsSelectable(
            GameDBExamination examination,
            ProcedureSceneAvailability examinationAvailability,
            ProcedureQueue procedureQueue)
        {
            if (examination.AlternativeExaminationRef != null &&
                (procedureQueue.HasPlannedExamination(examination.AlternativeExaminationRef.Entry) ||
                 (procedureQueue.HasFinishedExamination(examination.AlternativeExaminationRef.Entry) &&
                  !procedureQueue.HadSurgery())))
            {
                // Vanilla suppresses alternatives when the original exam is already queued or completed.
                return false;
            }

            return examinationAvailability == ProcedureSceneAvailability.AVAILABLE ||
                   examinationAvailability == ProcedureSceneAvailability.STAFF_BUSY ||
                   examinationAvailability == ProcedureSceneAvailability.EQUIPMENT_BUSY;
        }

        private static bool CanPatientBeSentToObservation(
            ProcedureComponent procedureComponent,
            MedicalCondition medicalCondition,
            Department department)
        {
            if (department == null ||
                department.GetDepartmentType() != ModDb.EmergencyDepartment)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                procedureComponent.m_entity.GetComponent<HospitalizationComponent>();
            GameDBTreatment observation = ModDb.ObservationTreatment;
            if (hospitalization.GetHospitalizationTreatment() == ModDb.TraumaTreatment)
            {
                return false;
            }

            SymptomHazard worstHazard = medicalCondition.GetWorstPossibleHazard();
            if (worstHazard == SymptomHazard.Low ||
                (worstHazard != SymptomHazard.High && !medicalCondition.AnySymptomNeedsHospitalization()))
            {
                return false;
            }

            ProcedureQueue queue = procedureComponent.m_state.m_procedureQueue;
            if (queue.HasPlannedTreatment(observation) ||
                queue.HasActiveTreatment(observation) ||
                (hospitalization.m_state.m_hospitalizationTreatment != null &&
                 hospitalization.m_state.m_hospitalizationTreatment.Entry == observation))
            {
                return false;
            }

            // CanHospitalize includes inpatient staffing/room checks and requires a free matching bed.
            return ProcedureScene.IsProcedureAvailable(department.CanHospitalize(observation.Procedure));
        }
    }
}
