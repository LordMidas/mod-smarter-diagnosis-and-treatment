using HarmonyLib;
using Lopital;
using GLib;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;

namespace ModSmarterDiagnosisAndTreatment
{
    [HarmonyPatch(typeof(TooltipSymptom), nameof(TooltipSymptom.UpdateData))]
    public static class TooltipSymptom_UpdateData
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Tooltips.SymptomDetails, nameof(TooltipSymptom_UpdateData));

        private const string COLOR_DONE_POSITIVE = "<color=#88dd88>";
        private const string COLOR_END = "</color>";
        private static TooltipSymptom CachedTooltip;
        private static ExaminationLayoutBaseline CachedBaseline;
        private static bool BaselineResolved;

        private sealed class ExaminationLayoutBaseline
        {
            public RectTransform Top;
            public RectTransform[] Items;
            public Vector3[] Positions;
            public Vector2 TextSize;
        }

        /// <summary>
        /// Shows all examinations that can reveal the symptom and marks examinations already performed.
        /// </summary>
        public static void Postfix(TooltipSymptom __instance, GameDBSymptom symptom)
        {
            RestoreVanillaLayout(__instance);

            if (symptom == null || symptom.Examinations == null || symptom.Examinations.Length <= 1)
                return;

            var finishedExamSet = new HashSet<GameDBExamination>();
            Entity patient = PatientPanelContext.CurrentPatient;
            ProcedureComponent procedureComponent = patient != null ? patient.GetComponent<ProcedureComponent>() : null;
            if (procedureComponent != null)
            {
                var finishedExams = procedureComponent.m_state.m_procedureQueue.m_finishedExaminations;
                if (finishedExams != null)
                {
                    foreach (ExaminationState examinationState in finishedExams)
                    {
                        if (examinationState.m_examination.Entry != null)
                            finishedExamSet.Add(examinationState.m_examination.Entry);
                    }
                }
            }

            var examsBuilder = new StringBuilder();
            var listedExamSet = new HashSet<GameDBExamination>();
            int examinationCount = 0;
            GameDBExamination performedExamination = null;
            foreach (var exam in symptom.Examinations)
            {
                // Lab tests are listed by the testing examination that reveals the symptom.
                GameDBExamination examination = ExaminationEquivalence.ToTesting(exam.Entry);
                if (examination == null)
                    continue;

                if (!listedExamSet.Add(examination))
                    continue;

                bool wasPerformed = finishedExamSet.Contains(examination);

                if (examinationCount > 0)
                    examsBuilder.Append("\n");

                if (wasPerformed)
                    if (performedExamination == null)
                        performedExamination = examination;

                examsBuilder.Append(wasPerformed ? COLOR_DONE_POSITIVE + "✓ " : "- ");
                examsBuilder.Append(StringTable.GetInstance().GetLocalizedText(examination));
                if (wasPerformed)
                    examsBuilder.Append(COLOR_END);

                examinationCount++;
            }

            if (examinationCount <= 1)
                return;

            Text examinationText = __instance.m_textExamination.GetComponent<Text>();
            RectTransform examinationRect = __instance.m_textExamination.GetComponent<RectTransform>();
            RectTransform iconRect = __instance.m_examinationIcon.GetComponent<RectTransform>();
            ExaminationLayoutBaseline baseline = GetLayoutBaseline(
                __instance,
                examinationRect,
                iconRect,
                __instance.m_textTreatement.GetComponent<RectTransform>(),
                __instance.m_treatementIcon.GetComponent<RectTransform>());

            if (baseline == null)
                return;

            float vanillaTextHeight = examinationText.preferredHeight;
            examinationText.text = examsBuilder.ToString();

            if (performedExamination != null)
            {
                if (performedExamination.CustomIconSmallAssetRef != null)
                {
                    __instance.m_examinationIcon.GetComponent<Image>().sprite =
                        IconManager.Instance.GetIcon(performedExamination.CustomIconSmallAssetRef.XmlID);
                }
                else
                {
                    __instance.m_examinationIcon.GetComponent<Image>().sprite =
                        IconManager.Instance.GetIcon(performedExamination.IconIndex + 1);
                }
            }

            float expandedTextHeight = examinationText.preferredHeight;
            examinationRect.sizeDelta = new Vector2(examinationRect.sizeDelta.x, expandedTextHeight);
            float extraHeight = Mathf.Max(0f, expandedTextHeight - vanillaTextHeight);
            if (extraHeight <= 0f)
                return;

            RectTransform tooltipRect = __instance.gameObject.GetComponent<RectTransform>();
            Vector3 downwardOffset = tooltipRect.TransformVector(new Vector3(0f, -extraHeight, 0f));
            RectTransform bottomSeparator = baseline.Items[0];
            bottomSeparator.position += downwardOffset;
            for (int i = 3; i < baseline.Items.Length; i++)
            {
                if (!IsDescendantOf(baseline.Items[i], bottomSeparator))
                    baseline.Items[i].position += downwardOffset;
            }

            Vector3 topCenter = baseline.Top.position;
            Vector3 bottomCenter = bottomSeparator.position;
            Vector3 examinationCenter = (topCenter + bottomCenter) * 0.5f;
            SetRectCenterY(examinationRect, examinationCenter);
            SetRectCenterY(iconRect, examinationCenter);

            tooltipRect.sizeDelta = new Vector2(tooltipRect.sizeDelta.x, tooltipRect.sizeDelta.y + extraHeight);
            __instance.SetPositionToMouse();
        }

        private static void RestoreVanillaLayout(TooltipSymptom tooltip)
        {
            if (tooltip != CachedTooltip || !BaselineResolved || CachedBaseline == null)
                return;

            for (int i = 0; i < CachedBaseline.Items.Length; i++)
                CachedBaseline.Items[i].localPosition = CachedBaseline.Positions[i];

            tooltip.m_textExamination.GetComponent<RectTransform>().sizeDelta = CachedBaseline.TextSize;
        }

        private static ExaminationLayoutBaseline GetLayoutBaseline(
            TooltipSymptom tooltip,
            RectTransform examinationText,
            RectTransform examinationIcon,
            RectTransform treatmentText,
            RectTransform treatmentIcon)
        {
            if (tooltip == CachedTooltip && BaselineResolved)
                return CachedBaseline;

            CachedTooltip = tooltip;
            BaselineResolved = true;
            RectTransform tooltipRect = tooltip.gameObject.GetComponent<RectTransform>();
            Vector3 examinationCenter = (examinationText.position + examinationIcon.position) * 0.5f;
            float examinationCenterY = tooltipRect.InverseTransformPoint(examinationCenter).y;
            RectTransform topSeparator = null;
            RectTransform bottomSeparator = null;
            float topDistance = float.MaxValue;
            float bottomDistance = float.MaxValue;

            foreach (Image image in tooltip.GetComponentsInChildren<Image>(true))
            {
                RectTransform candidate = image.rectTransform;
                float width = candidate.rect.width;
                float height = candidate.rect.height;
                if (width < tooltipRect.rect.width * 0.5f || width < height * 8f)
                    continue;

                float candidateY = tooltipRect.InverseTransformPoint(candidate.position).y;
                if (candidateY > examinationCenterY && candidateY - examinationCenterY < topDistance)
                {
                    topSeparator = candidate;
                    topDistance = candidateY - examinationCenterY;
                }
                else if (candidateY < examinationCenterY && examinationCenterY - candidateY < bottomDistance)
                {
                    bottomSeparator = candidate;
                    bottomDistance = examinationCenterY - candidateY;
                }
            }

            if (topSeparator == null || bottomSeparator == null)
            {
                CachedBaseline = null;
                Debug.LogWarning("TooltipSymptom could not find the horizontal separators around the examination row; keeping its vanilla layout.");
                return null;
            }

            var elements = new[]
            {
                bottomSeparator, examinationText, examinationIcon, treatmentText, treatmentIcon
            };
            var positions = new Vector3[elements.Length];
            for (int i = 0; i < elements.Length; i++)
                positions[i] = elements[i].localPosition;

            CachedBaseline = new ExaminationLayoutBaseline
            {
                Top = topSeparator,
                Items = elements,
                Positions = positions,
                TextSize = examinationText.sizeDelta
            };
            return CachedBaseline;
        }

        private static void SetRectCenterY(RectTransform item, Vector3 centerInWorld)
        {
            Vector3 centerInParent = item.parent.InverseTransformPoint(centerInWorld);
            Vector3 itemPosition = item.localPosition;
            float pivotOffset = (0.5f - item.pivot.y) * item.rect.height;
            item.localPosition = new Vector3(
                itemPosition.x,
                centerInParent.y - pivotOffset,
                itemPosition.z);
        }

        private static bool IsDescendantOf(Transform item, Transform ancestor)
        {
            for (Transform parent = item.parent; parent != null; parent = parent.parent)
            {
                if (parent == ancestor)
                    return true;
            }

            return false;
        }
    }
}
