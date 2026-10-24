using HarmonyLib;
using Lopital;
using GLib;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Postfix on TooltipExamination.UpdateData.
    ///
    /// After vanilla populates the tooltip for an examination, we append a list of
    /// all currently-possible diagnoses (from the viewed patient's Possible Diagnoses
    /// list) that have an unrevealed symptom this examination could reveal.
    ///
    /// If no patient is tracked yet, or the patient has no possible diagnoses, the
    /// tooltip is left unchanged.
    /// </summary>
    [HarmonyPatch(typeof(TooltipExamination), nameof(TooltipExamination.UpdateData))]
    public static class TooltipExamination_UpdateData
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Tooltips.ExaminationRelevantDiagnoses, nameof(TooltipExamination_UpdateData));

        // Colour used for the section header and list items – a soft blue that
        // contrasts with the vanilla orange/red used for warnings.
        private const string DIAGNOSES_COLOR_TAG = "<color=#aaddff>";
        private const string COLOR_END = "</color>";
        private static int MaxListedDiagnoses => ModConfig.Tooltips.MaxListedDiagnoses;

        /// <summary>
        /// If an examination can reveal an unrevealed symptom for any currently listed possible diagnosis,
        /// we add those diagnoses to the tooltip to help the player plan further tests.
        /// </summary>
        public static void Postfix(
            TooltipExamination __instance,
            GameDBExamination examination,
            ProcedureSceneAvailability availability,
            ProcedureReservationStatus reservationStatus,
            bool skipped)
        {
            MedicalCondition medicalCondition = PatientUi.GetMedicalCondition(PatientPanelContext.CurrentPatient);
            if (medicalCondition == null || medicalCondition.m_possibleDiagnoses == null || medicalCondition.m_possibleDiagnoses.Count == 0)
                return;

            List<GameDBMedicalCondition> matchingDiagnoses =
                ExaminationRelevance.FindMatchingDiagnoses(medicalCondition, examination);
            if (matchingDiagnoses.Count == 0)
                return;

            // --- Append diagnosis list to the tooltip text ---
            var diagsBuilder = new StringBuilder();
            diagsBuilder.Append("\n\n");
            diagsBuilder.Append(DIAGNOSES_COLOR_TAG);

            diagsBuilder.Append(StringTable.GetInstance().GetLocalizedText("MOD_LMSDT_RELEVANT_DIAGNOSES", new string[0]));
            diagsBuilder.Append(COLOR_END);

            bool truncated = matchingDiagnoses.Count > MaxListedDiagnoses;
            int listedCount = truncated ? MaxListedDiagnoses - 1 : matchingDiagnoses.Count;
            for (int i = 0; i < listedCount; i++)
            {
                diagsBuilder.Append("\n");
                diagsBuilder.Append(DIAGNOSES_COLOR_TAG);
                diagsBuilder.Append(" - ");
                diagsBuilder.Append(StringTable.GetInstance().GetLocalizedText(matchingDiagnoses[i]));
                diagsBuilder.Append(COLOR_END);
            }

            if (truncated)
            {
                diagsBuilder.Append("\n");
                diagsBuilder.Append(DIAGNOSES_COLOR_TAG);
                diagsBuilder.Append(" - ");
                diagsBuilder.Append("[...]");
                diagsBuilder.Append(COLOR_END);
            }

            // Append to whatever vanilla already set in m_textMain
            Text textComponent = __instance.m_textMain.GetComponent<Text>();
            textComponent.text += diagsBuilder.ToString();

            // --- Resize the tooltip to fit the new content ---
            // We replicate vanilla's own resize logic so the extra lines are visible.
            RectTransform mainRect = __instance.m_textMain.GetComponent<RectTransform>();
            if (textComponent.preferredHeight >= 30f)
            {
                mainRect.sizeDelta = new Vector2(mainRect.sizeDelta.x, textComponent.preferredHeight);

                RectTransform tooltipRect = __instance.gameObject.GetComponent<RectTransform>();
                tooltipRect.sizeDelta = new Vector2(tooltipRect.sizeDelta.x, textComponent.preferredHeight + 100f);
            }
        }
    }
}
