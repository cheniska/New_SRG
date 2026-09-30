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
        // ── Состояние слотов ──────────────────────────────────────────────────────

        private void UpdateSlot(SlotWidget slot)
        {
            slot.Item = null;
            slot.IconAnimator?.Configure(null);
            slot.IsBlocked = false;
            RefreshEmbedBadge(slot.EmbedBadge, null);

            if (slot.IsCargo)
            {
                SetSlotRaycastable(slot, true);
                return;
            }

            // Псевдо-слот «выбросить»: всегда активен, ничего не «хранит».
            if (slot.SlotKey == SlotKeys.Throw)
            {
                ApplyBg(slot, SlotVisualState.Usual);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, true);
                return;
            }

            // Псевдо-слот «активировать»: видимостью управляет рука (см. UpdateActivateSlotVisibility).
            // Здесь только сбрасываем визуал — раскастуем всегда, чтобы можно было кликнуть.
            if (slot.SlotKey == SlotKeys.Activate)
            {
                ApplyBg(slot, SlotVisualState.Green);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, true);
                return;
            }

            if (_ship?.Equipment == null)
            {
                slot.IsBlocked = true;
                ApplyBg(slot, SlotVisualState.Blocked);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, false);
                return;
            }

            bool slotExists = _ship.Equipment.Slots.ContainsKey(slot.SlotKey);
            if (!slotExists)
            {
                slot.IsBlocked = true;
                ApplyBg(slot, SlotVisualState.Blocked);
                slot.Icon.enabled = false;
                SetSlotRaycastable(slot, false);
                return;
            }

            SetSlotRaycastable(slot, true);

            var item = EquipmentSystem.GetEquipped(_ship, slot.SlotKey);
            slot.Item = item;

            if (item == null)
            {
                ApplyBg(slot, SlotVisualState.Usual);
                slot.Icon.enabled = false;
                return;
            }

            ApplyBg(slot, item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
            AssignAnimatedIcon(slot, item);
            RefreshEmbedBadge(slot.EmbedBadge, item);
        }

        private static void SetSlotRaycastable(SlotWidget slot, bool on)
        {
            if (slot.Bg != null) slot.Bg.raycastTarget = on;
            if (slot.RaycastFallback != null) slot.RaycastFallback.raycastTarget = on;
        }

        private void RefreshCargo()
        {
            if (_ship?.Inventory == null) return;
            foreach (var slot in _slots)
            {
                if (!slot.IsCargo) continue;
                if (_cargoSlotMap.TryGetValue(slot.CargoIndex, out var item) && item != null)
                {
                    slot.Item = item;
                    ApplyBg(slot, item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
                    AssignStaticIcon(slot, item);
                    RefreshEmbedBadge(slot.EmbedBadge, item);
                }
                else
                {
                    slot.Item = null;
                    ApplyBg(slot, SlotVisualState.Usual);
                    slot.Icon.enabled = false;
                    slot.IconAnimator?.Configure(null);
                    RefreshEmbedBadge(slot.EmbedBadge, null);
                }
            }
        }

        private void AssignAnimatedIcon(SlotWidget slot, ItemInstance item)
        {
            var frames = LoadItemSpriteSheet(item);
            if (frames != null && frames.Length > 0)
            {
                slot.Icon.sprite = frames[0];
                slot.Icon.color = Color.white;
                slot.Icon.enabled = true;
                slot.IconAnimator?.Configure(frames);
            }
            else
            {
                slot.Icon.enabled = false;
                slot.IconAnimator?.Configure(null);
            }
        }

        private void AssignStaticIcon(SlotWidget slot, ItemInstance item)
        {
            var sprite = LoadItemStaticSprite(item);
            if (sprite != null)
            {
                slot.Icon.sprite = sprite;
                slot.Icon.color = Color.white;
                slot.Icon.enabled = true;
                slot.IconAnimator?.Configure(null);
            }
            else
            {
                slot.Icon.enabled = false;
                slot.IconAnimator?.Configure(null);
            }
        }

        private void ApplyBg(SlotWidget slot, SlotVisualState state)
        {
            if (slot.Bg == null) return;
            var sprite = PickBgSprite(slot, state);
            slot.Bg.sprite = sprite;
            // Если спрайт не загружен — рисуем полупрозрачный плейсхолдер;
            // у артефактов/трюма оттенок другой, чтобы отличаться от обычных слотов.
            slot.Bg.color = sprite != null
                ? Color.white
                : PlaceholderColorFor(state, slot.IsArtefactStyle);
        }

        private static Color PlaceholderColorFor(SlotVisualState s, bool isArt)
        {
            if (isArt)
            {
                return s switch
                {
                    SlotVisualState.Blocked      => new Color(0.35f, 0.10f, 0.30f, 0.45f),
                    SlotVisualState.Broken       => new Color(0.55f, 0.20f, 0.45f, 0.55f),
                    SlotVisualState.MouseEntered => new Color(0.85f, 0.55f, 1.0f, 0.55f),
                    SlotVisualState.Green        => new Color(0.30f, 0.65f, 0.45f, 0.55f),
                    _                            => new Color(0.55f, 0.35f, 0.70f, 0.30f),
                };
            }
            return s switch
            {
                SlotVisualState.Blocked      => new Color(0.40f, 0.10f, 0.10f, 0.45f),
                SlotVisualState.Broken       => new Color(0.50f, 0.20f, 0.20f, 0.55f),
                SlotVisualState.MouseEntered => new Color(0.60f, 0.80f, 1.00f, 0.55f),
                SlotVisualState.Green        => new Color(0.20f, 0.60f, 0.20f, 0.55f),
                _                            => new Color(0.50f, 0.55f, 0.65f, 0.30f),
            };
        }

        private static Sprite PickBgSprite(SlotWidget slot, SlotVisualState s)
        {
            var set = slot.TextureSet ?? (slot.IsArtefactStyle ? _defaultArt : _defaultRegular);
            if (set == null) return null;
            return s switch
            {
                SlotVisualState.Usual        => set.Usual,
                SlotVisualState.MouseEntered => set.MouseEntered,
                SlotVisualState.Blocked      => set.Blocked,
                SlotVisualState.Broken       => set.Broken,
                SlotVisualState.Green        => set.Green,
                _                            => set.Usual,
            };
        }
    }
}
