using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships;
using SRG.Controllers;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    public partial class ShipFormView
    {
        // ── Постройка ─────────────────────────────────────────────────────────────

        private void BuildBackground()
        {
            var go = new GameObject("FormBackground");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            // Форма всегда отрисовывается в натуральном размере 700x900, без скейла,
            // строго по центру экрана (нативная привязка к 1920x1080 хост-канвасу).
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(FormImageWidth, FormImageHeight);
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.sprite = _formSprite;
            img.preserveAspect = false;
            img.raycastTarget = false;
        }

        private void BuildAllSlots()
        {
            foreach (var (slotKey, pos) in FixedSlotLayout)
            {
                float size = slotKey == SlotKeys.Hull ? HullIconSize : SlotSize;
                bool hideBg = slotKey == SlotKeys.Hull;
                _slots.Add(BuildSlotWidget(slotKey, pos, size, hideBg, isArtefactStyle: false, isCargo: false));
            }

            for (int i = 0; i < WeaponPositions.Length; i++)
                _slots.Add(BuildSlotWidget($"Weapons_{i}", WeaponPositions[i], SlotSize,
                                           hideBg: false, isArtefactStyle: false, isCargo: false));

            for (int i = 0; i < ArtefactPositions.Length; i++)
                _slots.Add(BuildSlotWidget($"Artefacts_{i}", ArtefactPositions[i], ArtSlotSize,
                                           hideBg: false, isArtefactStyle: true, isCargo: false));

            for (int i = 0; i < CargoColumns * CargoRows; i++)
            {
                int col = i % CargoColumns;
                int row = i / CargoColumns;
                Vector2 abs = new Vector2(
                    CargoFirstCell.x + col * CargoCellSize,
                    CargoFirstCell.y + row * CargoCellSize);
                var w = BuildSlotWidget($"Cargo_{i}", abs, CargoCellSize,
                                        hideBg: false, isArtefactStyle: true, isCargo: true);
                w.CargoIndex = i;
                _slots.Add(w);
            }

            // Псевдо-слот «выбросить»: drop любого предмета спавнит контейнер в космосе.
            _slots.Add(BuildSlotWidget(SlotKeys.Throw, ThrowSlotPos, SlotSize,
                                       hideBg: false, isArtefactStyle: false, isCargo: false));

            // Псевдо-слот «активировать»: появляется только когда в руке активируемый предмет.
            _activateSlot = BuildSlotWidget(SlotKeys.Activate, ActivationSlotPos, SlotSize,
                                            hideBg: false, isArtefactStyle: false, isCargo: false);
            _slots.Add(_activateSlot);
            _activateSlot.Root.gameObject.SetActive(false);
        }

        private SlotWidget BuildSlotWidget(string slotKey, Vector2 absPos, float size,
                                          bool hideBg, bool isArtefactStyle, bool isCargo)
        {
            // Размер слота (если не корпус) берётся из конфига текстур по slotKey.
            var texSet = GetSlotTextureSet(slotKey, isArtefactStyle);
            float w = size, h = size;
            if (!hideBg && texSet != null && texSet.Width > 0 && texSet.Height > 0)
            {
                w = texSet.Width;
                h = texSet.Height;
            }

            var go = new GameObject($"Slot_{slotKey}");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = AbsToLocal(absPos);

            Image bg = null;
            Image rcFallback = null;
            if (!hideBg)
            {
                var bgGo = new GameObject("Bg");
                bgGo.transform.SetParent(go.transform, false);
                var bgRt = bgGo.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;
                bg = bgGo.AddComponent<Image>();
                bg.preserveAspect = true;
                bg.raycastTarget = true;
            }
            else
            {
                var rcGo = new GameObject("RaycastTarget");
                rcGo.transform.SetParent(go.transform, false);
                var rcRt = rcGo.AddComponent<RectTransform>();
                rcRt.anchorMin = Vector2.zero; rcRt.anchorMax = Vector2.one;
                rcRt.offsetMin = Vector2.zero; rcRt.offsetMax = Vector2.zero;
                rcFallback = rcGo.AddComponent<Image>();
                rcFallback.color = new Color(1f, 1f, 1f, 0f);
                rcFallback.raycastTarget = true;
            }

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            float iconScale = hideBg ? 1f : 0.82f;
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(size * iconScale, size * iconScale);
            iconRt.anchoredPosition = Vector2.zero;
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            var animator = iconGo.AddComponent<SlotIconAnimator>();
            animator.Target = icon;

            var badge = BuildEmbedBadge(go.transform, size);

            var widget = new SlotWidget
            {
                SlotKey = slotKey,
                IsArtefactStyle = isArtefactStyle,
                IsCargo = isCargo,
                Bg = bg,
                RaycastFallback = rcFallback,
                Icon = icon,
                EmbedBadge = badge,
                IconAnimator = animator,
                Root = rt,
                TextureSet = texSet
            };

            var hover = go.AddComponent<SlotInputHandler>();
            hover.Owner = this;
            hover.Widget = widget;

            return widget;
        }

        /// <summary>Мини-иконка в правом нижнем углу слота — визуальный маркер вставленного
        /// в предмет-носитель встраиваемого (микромодуль/встраиваемый артефакт). Виден во всех
        /// состояниях слота (обычное/hover/broken), поверх основной иконки.</summary>
        private static Image BuildEmbedBadge(Transform parent, float slotSize)
        {
            var badgeGo = new GameObject("EmbedBadge");
            badgeGo.transform.SetParent(parent, false);
            var rt = badgeGo.AddComponent<RectTransform>();
            float badgeSize = Mathf.Max(16f, slotSize * 0.36f);
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(badgeSize, badgeSize);
            rt.anchoredPosition = new Vector2(-2f, 2f);
            var img = badgeGo.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        /// <summary>Обновляет мини-иконку встроенного модуля: показывает первый встроенный
        /// (item.Embeds[0]) если есть, иначе прячет. Значок отображается во всех состояниях
        /// слота — не гасится, когда основная иконка скрыта.</summary>
        private static void RefreshEmbedBadge(Image badge, ItemInstance item)
        {
            if (badge == null) return;
            if (item == null || item.Embeds == null || item.Embeds.Count == 0
                || item.EmbedItems == null)
            {
                badge.enabled = false;
                badge.sprite = null;
                return;
            }
            ItemInstance embed = null;
            foreach (var uid in item.Embeds)
            {
                if (item.EmbedItems.TryGetValue(uid, out embed) && embed != null) break;
            }
            var sprite = embed != null ? LoadItemStaticSprite(embed) : null;
            if (sprite == null)
            {
                badge.enabled = false;
                badge.sprite = null;
                return;
            }
            badge.sprite = sprite;
            badge.color = Color.white;
            badge.enabled = true;
            badge.transform.SetAsLastSibling();
        }

        private void BuildHandLayer()
        {
            var go = new GameObject("HandLayer");
            go.transform.SetParent(_root, false);
            _handRoot = go.AddComponent<RectTransform>();
            _handRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _handRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _handRoot.pivot = new Vector2(0.5f, 0.5f);
            _handRoot.sizeDelta = new Vector2(SlotSize, SlotSize);

            _handIcon = go.AddComponent<Image>();
            _handIcon.preserveAspect = true;
            _handIcon.raycastTarget = false;
            _handIcon.color = new Color(1f, 1f, 1f, 0.9f);
            _handEmbedBadge = BuildEmbedBadge(go.transform, SlotSize);
            go.SetActive(false);
        }

        private void BuildPopup()
        {
            var go = new GameObject("ItemInfoPopup");
            go.transform.SetParent(_root, false);
            _popupRoot = go.AddComponent<RectTransform>();
            // Anchor по центру _root, чтобы anchoredPosition напрямую
            // совпадал с локальной системой координат (origin = pivot центра _root).
            _popupRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _popupRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _popupRoot.pivot = new Vector2(0f, 1f);
            _popupRoot.sizeDelta = new Vector2(340f, 200f);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.06f, 0.10f, 0.96f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 10f);
            trt.offsetMax = new Vector2(-10f, -10f);

            _popupText = textGo.AddComponent<Text>();
            _popupText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _popupText.fontSize = 14;
            _popupText.color = new Color(0.92f, 0.92f, 0.95f);
            _popupText.alignment = TextAnchor.UpperLeft;
            _popupText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _popupText.verticalOverflow = VerticalWrapMode.Overflow;
            _popupText.raycastTarget = false;

            go.SetActive(false);
        }
    }
}
