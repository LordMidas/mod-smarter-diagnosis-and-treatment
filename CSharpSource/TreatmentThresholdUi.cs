using System.Collections.Generic;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace ModSmarterDiagnosisAndTreatment
{
    /// <summary>
    /// Treatment section in the Department management window, below the whole vanilla "Certainty | Chief doctor"
    /// block: a full-width "Treatment" header cloned from the vanilla certainty header, and under it the icon row built
    /// by cloning the certainty buttons (Diagnosis-relevant | Low | Medium | High, plus a label). The icons keep the
    /// vanilla size and spacing and are centered on the header. The window is made taller to hold the section:
    /// everything below the vanilla block moves down, and everything that spans it (its containers, the window) grows.
    /// The section and the extra space follow the vanilla row's visibility (hidden for Radiology, Lab and Pathology).
    /// </summary>
    internal static class TreatmentThresholdUi
    {
        // Vanilla certainty buttons are 30 units, 1.3x when selected (DepartmentManagementLogisticsHybridController.SetButtonSize).
        private const float VanillaButtonSize = 30f;
        private const float SelectedScale = 1.3f;
        private const float RowGap = 4f;
        private const float Epsilon = 1f;
        private const float LayoutTolerance = 0.01f;
        private const float LabelWidth = 90f;
        // A child that spans the row is stretched only if it is at least this many row heights tall (a background or a
        // divider); smaller ones that overlap the row a little are kept as they are.
        private const float SpanningMinRows = 3f;
        private const int NeutralHazardIcon = 213;

        // Harmony ID of the Hospital Porters BepInEx plugin, which also resizes this window (see the patches below).
        public const string HospitalPortersHarmonyId = "Loupito75:HospitalPorters";
        private const string CertaintyHeaderId = "SEGMENT_HEADING_EMPLOYEES_CERTAINTY";
        private const string ChiefDoctorHeaderId = "SEGMENT_HEADING_EMPLOYEES_CHIEF_DOCTOR";
        private const string TreatHeaderId = "MOD_LMSDT_TREAT_HEADER";

        private static readonly TreatmentThresholdLevel[] Levels =
        {
            TreatmentThresholdLevel.DiagnosisRelevant,
            TreatmentThresholdLevel.Low,
            TreatmentThresholdLevel.Medium,
            TreatmentThresholdLevel.High
        };

        private static readonly int[] Icons = { NeutralHazardIcon, 166, 169, 170 };

        private static readonly string[] LabelIds =
        {
            "MOD_LMSDT_TREAT_DIAGNOSIS",
            "MOD_LMSDT_TREAT_LOW",
            "MOD_LMSDT_TREAT_MEDIUM",
            "MOD_LMSDT_TREAT_HIGH"
        };

        private static readonly string[] TooltipIds =
        {
            "MOD_LMSDT_TREAT_TOOLTIP_DIAGNOSIS",
            "MOD_LMSDT_TREAT_TOOLTIP_LOW",
            "MOD_LMSDT_TREAT_TOOLTIP_MEDIUM",
            "MOD_LMSDT_TREAT_TOOLTIP_HIGH"
        };

        private static readonly string[] ButtonNames =
        {
            "LMSDT_TreatButtonDiagnosis",
            "LMSDT_TreatButtonLow",
            "LMSDT_TreatButtonMedium",
            "LMSDT_TreatButtonHigh"
        };

        private static readonly GameObject[] buttons = new GameObject[4];
        private static readonly Vector3[] corners = new Vector3[4];
        private static readonly Dictionary<RectTransform, Adjustment> adjustments = new Dictionary<RectTransform, Adjustment>();
        private static DepartmentManagementLogisticsHybridController controller;
        private static GameObject label;
        private static GameObject header;
        private static RectTransform headerRect;
        private static RectTransform certaintyBar;
        private static RectTransform chiefDoctorBar;

        private enum Placement
        {
            Above,
            Below,
            Spanning
        }

        // The vanilla value we built on, and the value we last wrote, so vanilla resets are detected and nothing is
        // applied twice.
        private class Adjustment
        {
            public Vector2 BasePosition;
            public Vector2 AppliedPosition;
            public Vector2 BaseSize;
            public Vector2 AppliedSize;
        }

        public static void Create(DepartmentManagementLogisticsHybridController instance)
        {
            if (controller == instance && controller != null)
                return;

            controller = instance;
            adjustments.Clear();

            CreateHeader(instance);

            label = UnityEngine.Object.Instantiate(instance.m_textCertainty, instance.m_textCertainty.transform.parent);
            label.name = "LMSDT_TreatLabel";
            LocalizedTextController localized = label.GetComponent<LocalizedTextController>();
            if (localized != null)
                UnityEngine.Object.Destroy(localized);

            IgnoreLayout(label);

            // The vanilla label is 48 units wide, which breaks "Diagnosis" mid-word. It is center-anchored, so widening
            // keeps it centered; the certainty panel is about 100 units wide.
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(LabelWidth, labelRect.sizeDelta.y);

            GameObject[] sources =
            {
                instance.m_buttonCertaintyLow,
                instance.m_buttonCertaintyLow,
                instance.m_buttonCertaintyMedium,
                instance.m_buttonCertaintyHigh
            };

            for (int index = 0; index < buttons.Length; index++)
            {
                GameObject button = UnityEngine.Object.Instantiate(sources[index], sources[index].transform.parent);
                button.name = ButtonNames[index];
                IgnoreLayout(button);

                IconButtonController iconButton = button.GetComponent<IconButtonController>();
                iconButton.SetTextLocID(TooltipIds[index]);
                TreatmentThresholdLevel level = Levels[index];
                iconButton.RemoveOnClickDelegate();
                iconButton.SetOnClickedDelegate(delegate { Select(level); });
                buttons[index] = button;
            }
        }

        // The header texts are prefab objects inside the statistics panel. A text sits in its own bar (an object with an
        // Image) when its parent has one; the parent only counts if it is not the panel itself and not a container of
        // the certainty buttons. Otherwise the text object is the bar.
        private static void CreateHeader(DepartmentManagementLogisticsHybridController instance)
        {
            header = null;
            headerRect = null;
            certaintyBar = null;
            chiefDoctorBar = null;

            LocalizedTextController certaintyText = null;
            LocalizedTextController chiefDoctorText = null;
            LocalizedTextController[] texts = instance.m_panelStatistics.GetComponentsInChildren<LocalizedTextController>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                if (texts[index].m_textLocalizationID == CertaintyHeaderId)
                    certaintyText = texts[index];
                else if (texts[index].m_textLocalizationID == ChiefDoctorHeaderId)
                    chiefDoctorText = texts[index];
            }

            if (certaintyText == null)
                return;

            certaintyBar = FindBar(instance, certaintyText);
            if (chiefDoctorText != null)
                chiefDoctorBar = FindBar(instance, chiefDoctorText);

            header = UnityEngine.Object.Instantiate(certaintyBar.gameObject, certaintyBar.transform.parent);
            header.name = "LMSDT_TreatHeader";
            IgnoreLayout(header);

            LocalizedTextController keep = null;
            LocalizedTextController[] cloneTexts = header.GetComponentsInChildren<LocalizedTextController>(true);
            for (int index = 0; index < cloneTexts.Length; index++)
            {
                if (cloneTexts[index].m_textLocalizationID == CertaintyHeaderId)
                    keep = cloneTexts[index];
            }

            if (keep == null)
            {
                UnityEngine.Object.Destroy(header);
                header = null;
                return;
            }

            keep.m_textLocalizationID = TreatHeaderId;

            // The bar may hold the "Chief doctor" text too, or other texts: keep only the one we retitled.
            Text[] extraTexts = header.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < extraTexts.Length; index++)
            {
                if (!keep.transform.IsChildOf(extraTexts[index].transform))
                    UnityEngine.Object.Destroy(extraTexts[index].gameObject);
            }

            LocalizedTextController[] extraControllers = header.GetComponentsInChildren<LocalizedTextController>(true);
            for (int index = 0; index < extraControllers.Length; index++)
            {
                if (extraControllers[index] != keep && !keep.transform.IsChildOf(extraControllers[index].transform))
                    UnityEngine.Object.Destroy(extraControllers[index].gameObject);
            }

            // Centered anchors and pivot, so the size and world position can be set directly whatever the prefab used.
            headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.5f, 0.5f);
            headerRect.anchorMax = new Vector2(0.5f, 0.5f);
            headerRect.pivot = new Vector2(0.5f, 0.5f);

            // The vanilla text sits over the Certainty column: stretch it over the whole header and center it.
            for (Transform current = keep.transform; current != header.transform; current = current.parent)
            {
                RectTransform rect = (RectTransform)current;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            Text headerText = keep.GetComponent<Text>();
            if (headerText != null)
                headerText.alignment = CenterHorizontally(headerText.alignment);
        }

        private static TextAnchor CenterHorizontally(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperRight:
                    return TextAnchor.UpperCenter;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerRight:
                    return TextAnchor.LowerCenter;
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleRight:
                    return TextAnchor.MiddleCenter;
                default:
                    return anchor;
            }
        }

        private static RectTransform FindBar(DepartmentManagementLogisticsHybridController instance, LocalizedTextController text)
        {
            Transform parent = text.transform.parent;
            if (parent != null
                && parent != instance.m_panelStatistics.transform
                && parent.GetComponent<Image>() != null
                && !instance.m_buttonCertaintyLow.transform.IsChildOf(parent))
            {
                return parent.GetComponent<RectTransform>();
            }

            return text.GetComponent<RectTransform>();
        }

        public static void UpdateRow(DepartmentManagementLogisticsHybridController instance)
        {
            if (controller == null || controller != instance || label == null)
                return;

            for (int index = 0; index < buttons.Length; index++)
            {
                if (buttons[index] == null)
                    return;
            }

            bool visible = IsVanillaRowVisible(instance);
            label.SetActive(visible);
            if (header != null)
                header.SetActive(visible);
            for (int index = 0; index < buttons.Length; index++)
                buttons[index].SetActive(visible);

            RestoreVanilla();

            float rowTop;
            float rowBottom;
            GetVanillaRowBounds(instance, out rowTop, out rowBottom);
            float rowHeightWorld = rowTop - rowBottom;
            float gap = RowGap * instance.m_textCertainty.transform.lossyScale.y;
            float headerHeight = GetHeaderHeight();
            float sectionHeight = headerHeight + gap + rowHeightWorld;
            // Classified against the certainty row, not the portrait: the chief doctor segment's rect reaches past the
            // top of the bottom panel, which would then count as "above" and not move down.
            MakeRoom(instance, rowBottom, visible ? sectionHeight : 0f);

            if (!visible)
                return;

            Department department = Hospital.Instance.GetActiveDepartment();
            if (department == null)
                return;

            Place(instance, rowTop, GetBlockBottom(instance, rowBottom, sectionHeight), headerHeight, gap);
            TreatmentThresholdLevel selected = TreatmentThreshold.Get(department.GetDepartmentType());
            float iconSize = VanillaButtonSize;
            for (int index = 0; index < buttons.Length; index++)
            {
                bool isSelected = Levels[index] == selected;
                float size = isSelected ? iconSize * SelectedScale : iconSize;
                buttons[index].GetComponent<RectTransform>().sizeDelta = new Vector2(size, size);
                buttons[index].GetComponent<IconButtonController>().SetIcon(Icons[index]);
                if (isSelected)
                {
                    label.GetComponent<Text>().text = StringTable.GetInstance().GetLocalizedText(LabelIds[index]);
                    UIManager.UpdateFont(label);
                }
            }
        }

        // In world space, so it does not depend on how the vanilla block is anchored. The header hangs from the bottom of
        // the vanilla block and spans from the Certainty header to the Chief doctor header. The icons are centered on
        // it, with the vanilla spacing and the vanilla offset from the top of the row to the buttons.
        private static void Place(DepartmentManagementLogisticsHybridController instance, float rowTop, float sectionBottom, float headerHeight, float gap)
        {
            Vector3 low = instance.m_buttonCertaintyLow.transform.position;
            Vector3 medium = instance.m_buttonCertaintyMedium.transform.position;
            Vector3 text = instance.m_textCertainty.transform.position;
            float spacing = medium.x - low.x;

            float left;
            float right;
            GetSectionSpan(instance, out left, out right);
            float centerX = (left + right) * 0.5f;
            float headerBottom = sectionBottom - headerHeight;

            if (headerRect != null)
            {
                Vector3 scale = headerRect.parent.lossyScale;
                headerRect.sizeDelta = new Vector2((right - left) / Mathf.Max(scale.x, 0.0001f), headerHeight / Mathf.Max(scale.y, 0.0001f));
                headerRect.position = new Vector3(centerX, sectionBottom - headerHeight * 0.5f, certaintyBar.position.z);
            }

            float y = headerBottom - gap - (rowTop - medium.y);
            for (int index = 0; index < buttons.Length; index++)
            {
                float x = centerX + (index - (buttons.Length - 1) * 0.5f) * spacing;
                buttons[index].transform.position = new Vector3(x, y, medium.z);
            }

            label.transform.position = new Vector3(centerX, y + (text.y - medium.y), text.z);
        }

        private static float GetHeaderHeight()
        {
            if (header == null || certaintyBar == null)
                return 0f;

            certaintyBar.GetWorldCorners(corners);
            return corners[1].y - corners[0].y;
        }

        // Bottom of the whole vanilla block = where the bottom panel started before MakeRoom moved it down by the section
        // height. Called after MakeRoom. Falls back to the certainty row when the bottom panel is not shown.
        private static float GetBlockBottom(DepartmentManagementLogisticsHybridController instance, float rowBottom, float sectionHeight)
        {
            if (instance.m_panelBottom == null || !instance.m_panelBottom.activeInHierarchy)
                return rowBottom;

            instance.m_panelBottom.GetComponent<RectTransform>().GetWorldCorners(corners);
            return Mathf.Min(rowBottom, corners[1].y + sectionHeight);
        }

        private static void GetSectionSpan(DepartmentManagementLogisticsHybridController instance, out float left, out float right)
        {
            RectTransform leftRect = certaintyBar != null ? certaintyBar : instance.m_textCertainty.GetComponent<RectTransform>();
            leftRect.GetWorldCorners(corners);
            left = corners[0].x;

            RectTransform rightRect = chiefDoctorBar != null ? chiefDoctorBar : instance.m_chiefDoctorSegmentStatic.GetComponent<RectTransform>();
            rightRect.GetWorldCorners(corners);
            right = corners[2].x;
        }

        // World-space top and bottom of the vanilla certainty row (label + buttons at their unselected size) plus the gap.
        private static void GetVanillaRowBounds(DepartmentManagementLogisticsHybridController instance, out float top, out float bottom)
        {
            top = float.MinValue;
            bottom = float.MaxValue;

            RectTransform text = instance.m_textCertainty.GetComponent<RectTransform>();
            text.GetWorldCorners(corners);
            top = Mathf.Max(top, corners[1].y);
            bottom = Mathf.Min(bottom, corners[0].y);

            IncludeButton(instance.m_buttonCertaintyLow, ref top, ref bottom);
            IncludeButton(instance.m_buttonCertaintyMedium, ref top, ref bottom);
            IncludeButton(instance.m_buttonCertaintyHigh, ref top, ref bottom);

            bottom -= RowGap * instance.m_textCertainty.transform.lossyScale.y;
        }

        private static void IncludeButton(GameObject button, ref float top, ref float bottom)
        {
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.GetWorldCorners(corners);
            float center = (corners[0].y + corners[1].y) * 0.5f;
            float half = VanillaButtonSize * 0.5f * rect.lossyScale.y;
            top = Mathf.Max(top, center + half);
            bottom = Mathf.Min(bottom, center - half);
        }

        // Every container from the row's parent up to the window grows by the row height with its top edge fixed. At
        // each of those levels, each child is corrected for its own anchors so that on screen:
        // - children above the row (including the vanilla row itself) stay where they are,
        // - children below the row move down by the row height,
        // - large children that span the row (the container on the way up, backgrounds, dividers) keep their top and
        //   extend their bottom. Small ones that only overlap it a little (a portrait) are treated as "above".
        private static void MakeRoom(DepartmentManagementLogisticsHybridController instance, float rowBottomWorld, float heightWorld)
        {
            RectTransform window = instance.GetComponent<RectTransform>();
            Transform child = instance.m_buttonCertaintyLow.transform;
            if (window == null || !child.IsChildOf(window) || heightWorld <= 0f)
                return;

            Transform parent = child.parent;
            while (parent != null)
            {
                float height = heightWorld / Mathf.Max(parent.lossyScale.y, 0.0001f);
                for (int index = 0; index < parent.childCount; index++)
                {
                    RectTransform rect = parent.GetChild(index) as RectTransform;
                    if (rect == null || IsClone(rect))
                        continue;

                    Placement placement = rect == child && child != instance.m_buttonCertaintyLow.transform
                        ? Placement.Spanning
                        : Classify(rect, rowBottomWorld, heightWorld);

                    if (placement == Placement.Below)
                        Apply(rect, -height, -height, height);
                    else if (placement == Placement.Spanning)
                        Apply(rect, 0f, -height, height);
                    else
                        Apply(rect, 0f, 0f, height);
                }

                if (parent == window)
                {
                    // The window's own parent does not grow: only extend its height (its top stays put when the pivot is at the top).
                    ApplyWindow(window, heightWorld / Mathf.Max(window.lossyScale.y, 0.0001f));
                    break;
                }

                child = parent;
                parent = parent.parent;
            }
        }

        private static Placement Classify(RectTransform rect, float rowBottomWorld, float heightWorld)
        {
            rect.GetWorldCorners(corners);
            float top = corners[1].y;
            float bottom = corners[0].y;
            if (top <= rowBottomWorld + Epsilon)
                return Placement.Below;

            if (bottom < rowBottomWorld - Epsilon && top - bottom >= SpanningMinRows * heightWorld)
                return Placement.Spanning;

            return Placement.Above;
        }

        // Radiology and Lab hide the whole Certainty / Chief doctor panel (not the buttons), Pathology hides the
        // buttons, so the row is visible only if the buttons and every container up to the window are active.
        private static bool IsVanillaRowVisible(DepartmentManagementLogisticsHybridController instance)
        {
            Transform window = instance.transform;
            for (Transform current = instance.m_buttonCertaintyLow.transform; current != null && current != window; current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                    return false;
            }

            return true;
        }

        // Back to the vanilla values before UpdateLayout, so other mods that snapshot the layout around it do not
        // capture our offsets.
        public static void RemoveOffsets(DepartmentManagementLogisticsHybridController instance)
        {
            if (controller == null || controller != instance)
                return;

            RestoreVanilla();
        }

        // Re-applies the layout if anything (vanilla UpdateLayout or otherwise) reset an adjusted element. Runs every
        // frame while the window updates, so it only compares values and does not allocate.
        public static void EnsureLayout(DepartmentManagementLogisticsHybridController instance)
        {
            if (controller == null || controller != instance)
                return;

            foreach (KeyValuePair<RectTransform, Adjustment> pair in adjustments)
            {
                RectTransform rect = pair.Key;
                if (rect == null)
                    continue;

                if (WasReset(rect, pair.Value))
                {
                    UpdateRow(instance);
                    return;
                }
            }
        }

        private static bool IsClone(Transform target)
        {
            if (target == label.transform || (header != null && target == header.transform))
                return true;

            for (int index = 0; index < buttons.Length; index++)
            {
                if (target == buttons[index].transform)
                    return true;
            }

            return false;
        }

        // Moves the top edge by topDelta and the bottom edge by bottomDelta on screen, while the parent grows by
        // parentGrowth with its top fixed. A child anchored at height a moves down by (1 - a) * parentGrowth with its
        // parent, so the offsets add that back. All values are in the parent's local units.
        private static void Apply(RectTransform rect, float topDelta, float bottomDelta, float parentGrowth)
        {
            float offsetMaxDelta = topDelta + (1f - rect.anchorMax.y) * parentGrowth;
            float offsetMinDelta = bottomDelta + (1f - rect.anchorMin.y) * parentGrowth;
            float sizeDelta = offsetMaxDelta - offsetMinDelta;
            float positionDelta = (1f - rect.pivot.y) * offsetMinDelta + rect.pivot.y * offsetMaxDelta;
            SetAdjusted(rect, positionDelta, sizeDelta);
        }

        private static void ApplyWindow(RectTransform window, float height)
        {
            SetAdjusted(window, 0f, height);
        }

        // Applies the deltas to the last vanilla value. A value that differs from what was last written was reset by
        // vanilla (UpdateLayout), so it becomes the new base; this keeps the adjustment from being applied twice.
        private static Adjustment GetAdjustment(RectTransform rect)
        {
            Adjustment adjustment;
            if (!adjustments.TryGetValue(rect, out adjustment))
            {
                adjustment = new Adjustment
                {
                    BasePosition = rect.anchoredPosition,
                    AppliedPosition = rect.anchoredPosition,
                    BaseSize = rect.sizeDelta,
                    AppliedSize = rect.sizeDelta
                };
                adjustments[rect] = adjustment;
            }

            return adjustment;
        }

        // Writes the vanilla value back to every adjusted element (taking any value vanilla reset as the new base), so
        // the layout is measured and classified in a pure vanilla state. Vanilla UpdateLayout resets only some of them
        // (window height, statistics and bottom panel positions), and a mix of vanilla and adjusted values misplaces
        // the row and misclassifies its neighbours.
        private static void RestoreVanilla()
        {
            foreach (KeyValuePair<RectTransform, Adjustment> pair in adjustments)
            {
                if (pair.Key != null)
                    SetAdjusted(pair.Key, 0f, 0f);
            }
        }

        private static bool WasReset(RectTransform rect, Adjustment adjustment)
        {
            return !Matches(rect.anchoredPosition, adjustment.AppliedPosition) || !Matches(rect.sizeDelta, adjustment.AppliedSize);
        }

        private static void SetAdjusted(RectTransform rect, float positionDelta, float sizeDelta)
        {
            Adjustment adjustment = GetAdjustment(rect);
            if (!Matches(rect.anchoredPosition, adjustment.AppliedPosition))
                adjustment.BasePosition = rect.anchoredPosition;

            if (!Matches(rect.sizeDelta, adjustment.AppliedSize))
                adjustment.BaseSize = rect.sizeDelta;

            rect.sizeDelta = adjustment.BaseSize + new Vector2(0f, sizeDelta);
            rect.anchoredPosition = adjustment.BasePosition + new Vector2(0f, positionDelta);

            // Read back what Unity stored: it keeps the edges internally, so the values can differ by rounding.
            adjustment.AppliedSize = rect.sizeDelta;
            adjustment.AppliedPosition = rect.anchoredPosition;
        }

        // Tolerates float rounding, so a value we wrote is never mistaken for a vanilla reset (which would add the offset again).
        private static bool Matches(Vector2 current, Vector2 applied)
        {
            return Mathf.Abs(current.x - applied.x) < LayoutTolerance && Mathf.Abs(current.y - applied.y) < LayoutTolerance;
        }

        private static void IgnoreLayout(GameObject target)
        {
            // A layout group on the parent would otherwise move the clones back onto the vanilla row.
            LayoutElement layoutElement = target.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = target.AddComponent<LayoutElement>();

            layoutElement.ignoreLayout = true;
        }

        private static void Select(TreatmentThresholdLevel level)
        {
            Department department = Hospital.Instance.GetActiveDepartment();
            if (department == null)
                return;

            TreatmentThreshold.Set(department.GetDepartmentType(), level);
            UpdateRow(controller);
            UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_BEEP");
        }
    }

    [HarmonyPatch(typeof(DepartmentManagementLogisticsHybridController), "Init")]
    public static class DepartmentManagementLogisticsHybridController_Init
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Threshold.Enabled, nameof(DepartmentManagementLogisticsHybridController_Init));

        public static void Postfix(DepartmentManagementLogisticsHybridController __instance)
        {
            TreatmentThresholdUi.Create(__instance);
            TreatmentThresholdUi.UpdateRow(__instance);
        }
    }

    [HarmonyPatch(typeof(DepartmentManagementLogisticsHybridController), nameof(DepartmentManagementLogisticsHybridController.UpdateCertaintyUI))]
    public static class DepartmentManagementLogisticsHybridController_UpdateCertaintyUI
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Threshold.Enabled, nameof(DepartmentManagementLogisticsHybridController_UpdateCertaintyUI));

        public static void Postfix(DepartmentManagementLogisticsHybridController __instance)
        {
            TreatmentThresholdUi.UpdateRow(__instance);
        }
    }

    // UpdateLayout writes the window height and the bottom panel position for the department type.
    // Hospital Porters (Loupito75) snapshots the layout after UpdateLayout as "native" (Postfix, priority 0) and
    // rewrites "snapshot + its shift" every frame (Update Postfix, priority 0). The Prefix runs before its Prefix (800)
    // and removes our offsets so its snapshot stays clean; the Postfixes run after it so ours always go on top.
    [HarmonyPatch(typeof(DepartmentManagementLogisticsHybridController), "UpdateLayout")]
    public static class DepartmentManagementLogisticsHybridController_UpdateLayout
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Threshold.Enabled, nameof(DepartmentManagementLogisticsHybridController_UpdateLayout));

        [HarmonyPriority(Priority.First + 100)]
        public static void Prefix(DepartmentManagementLogisticsHybridController __instance)
        {
            TreatmentThresholdUi.RemoveOffsets(__instance);
        }

        [HarmonyAfter(TreatmentThresholdUi.HospitalPortersHarmonyId)]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(DepartmentManagementLogisticsHybridController __instance)
        {
            TreatmentThresholdUi.UpdateRow(__instance);
        }
    }

    [HarmonyPatch(typeof(DepartmentManagementLogisticsHybridController), nameof(DepartmentManagementLogisticsHybridController.Update))]
    public static class DepartmentManagementLogisticsHybridController_Update
    {
        private static bool Prepare() => ModConfig.Patch(ModConfig.Threshold.Enabled, nameof(DepartmentManagementLogisticsHybridController_Update));

        [HarmonyAfter(TreatmentThresholdUi.HospitalPortersHarmonyId)]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(DepartmentManagementLogisticsHybridController __instance)
        {
            TreatmentThresholdUi.EnsureLayout(__instance);
        }
    }
}
