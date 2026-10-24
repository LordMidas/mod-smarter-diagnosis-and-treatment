using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModSmarterDiagnosisAndTreatment
{
    internal sealed class HighlightColorSet
    {
        private static float TileTintStrength => ModConfig.Highlights.TileTintStrength;
        private static float IconTintStrength => ModConfig.Highlights.IconTintStrength;
        private const string OverlayName = "LMMHighlightOverlay";

        private readonly Dictionary<Graphic, Color> originalColors = new Dictionary<Graphic, Color>();
        private readonly Dictionary<RectTransform, RawImage> overlaysByTile = new Dictionary<RectTransform, RawImage>();

        public void Apply(SegmentController segment, int index)
        {
            HoverTooltipDelay item = segment.GetItemComponent<HoverTooltipDelay>(index);
            if (item == null)
                return;

            Image[] images = item.GetComponentsInChildren<Image>();
            if (images.Length == 0)
                return;

            Color highlightColor = UISettings.Instance.EXAMINATION_COLOR_SUCCESSFUL;
            if (!HasVisibleTile(images[0]))
            {
                RectTransform tileRect = IsHiddenTile(images[0])
                    ? images[0].rectTransform
                    : item.GetComponent<RectTransform>();
                if (tileRect == null)
                    return;

                RawImage overlay = GetOrCreateOverlay(tileRect);
                overlay.gameObject.SetActive(true);
                SetOverlayTint(overlay, highlightColor);
                Tint(IsHiddenTile(images[0]) && images.Length > 1 ? images[1] : images[0],
                    highlightColor,
                    IconTintStrength);
                return;
            }

            Tint(images[0], highlightColor, TileTintStrength);
            if (images.Length > 1)
                Tint(images[1], highlightColor, IconTintStrength);
        }

        public void Restore()
        {
            foreach (KeyValuePair<Graphic, Color> entry in originalColors)
            {
                if (entry.Key != null)
                    entry.Key.color = entry.Value;
            }

            foreach (KeyValuePair<RectTransform, RawImage> entry in overlaysByTile)
            {
                if (entry.Value != null)
                    entry.Value.gameObject.SetActive(false);
            }

            // Items are destroyed and recreated on layout changes; drop overlays of tiles that are gone.
            List<RectTransform> destroyedTiles = null;
            foreach (KeyValuePair<RectTransform, RawImage> entry in overlaysByTile)
            {
                if (entry.Key != null)
                    continue;

                if (entry.Value != null)
                    UnityEngine.Object.Destroy(entry.Value.gameObject);

                if (destroyedTiles == null)
                    destroyedTiles = new List<RectTransform>();
                destroyedTiles.Add(entry.Key);
            }

            if (destroyedTiles != null)
            {
                foreach (RectTransform tile in destroyedTiles)
                    overlaysByTile.Remove(tile);
            }

            originalColors.Clear();
        }

        /// <summary>
        /// Only valid when vanilla rewrites every tinted graphic's color each frame (icon colors); otherwise tints compound.
        /// </summary>
        public void DiscardSavedColors()
        {
            originalColors.Clear();
        }

        private RawImage GetOrCreateOverlay(RectTransform tileRect)
        {
            RawImage overlay;
            if (overlaysByTile.TryGetValue(tileRect, out overlay) && overlay != null)
                return overlay;

            // A RawImage without a texture renders as a flat color, and it is invisible to
            // GetComponentsInChildren<Image>(), which vanilla item indexing relies on.
            GameObject overlayObject = new GameObject(
                OverlayName,
                typeof(RectTransform),
                typeof(RawImage));
            Transform overlayTransform = overlayObject.transform;
            overlayTransform.SetParent(tileRect.parent, false);
            overlayTransform.SetSiblingIndex(tileRect.GetSiblingIndex());

            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.anchorMin = tileRect.anchorMin;
            overlayRect.anchorMax = tileRect.anchorMax;
            overlayRect.anchoredPosition = tileRect.anchoredPosition;
            overlayRect.sizeDelta = tileRect.sizeDelta;
            overlayRect.pivot = tileRect.pivot;
            overlayRect.localRotation = tileRect.localRotation;
            overlayRect.localScale = tileRect.localScale;
            overlay = overlayObject.GetComponent<RawImage>();
            overlay.raycastTarget = false;
            overlaysByTile[tileRect] = overlay;
            return overlay;
        }

        private static bool HasVisibleTile(Image image)
        {
            return image.sprite == IconManager.Instance.GetIcon(IconManager.ICON_BUTTON);
        }

        private static bool IsHiddenTile(Image image)
        {
            return image.sprite == IconManager.Instance.GetIcon(IconManager.ICON_BUTTON_HIDDEN);
        }

        private void Tint(Graphic image, Color highlightColor, float strength)
        {
            if (!originalColors.ContainsKey(image))
                originalColors.Add(image, image.color);

            image.color = Color.Lerp(image.color, highlightColor, strength);
        }

        private static void SetOverlayTint(RawImage overlay, Color highlightColor)
        {
            overlay.color = new Color(
                highlightColor.r,
                highlightColor.g,
                highlightColor.b,
                TileTintStrength);
        }
    }
}
