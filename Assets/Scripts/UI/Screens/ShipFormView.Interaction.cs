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
        // ── Hover / click ─────────────────────────────────────────────────────────

        internal void OnSlotEnter(SlotWidget slot)
        {
            if (slot.IsBlocked) return;
            // Cargo-слоты: визуальной реакции/анимации нет, но popup инфо предмета
            // показываем как и для обычных слотов.
            if (slot.IsCargo)
            {
                if (slot.Item != null && _hand.Item == null)
                    ShowPopup(slot.Item);
                return;
            }
            slot.IsHovered = true;
            ApplyBg(slot, SlotVisualState.MouseEntered);
            slot.IconAnimator?.SetPlaying(true);
            if (slot.Item != null && _hand.Item == null)
                ShowPopup(slot.Item);
        }

        internal void OnSlotExit(SlotWidget slot)
        {
            if (slot.IsBlocked) return;
            if (slot.IsCargo) { HidePopup(); return; }
            slot.IsHovered = false;
            slot.IconAnimator?.SetPlaying(false);
            RestoreSlotBg(slot);
            HidePopup();
        }

        internal void OnSlotClick(SlotWidget slot, PointerEventData e)
        {
            if (!AllowInteraction) return;
            if (slot.IsBlocked) return;
            if (_ship == null) return;

            // Абордажный «грабёж»: ЛКМ переносит предмет напрямую в инвентарь получателя.
            if (BoardingLootRecipient != null && e.button == PointerEventData.InputButton.Left)
            {
                if (TryLootToRecipient(slot))
                {
                    Refresh();
                    OnEquipmentChanged?.Invoke();
                }
                return;
            }

            if (e.button == PointerEventData.InputButton.Right)
            {
                CancelHand();
                OnEquipmentChanged?.Invoke();
                return;
            }
            if (e.button != PointerEventData.InputButton.Left) return;

            if (_hand.Item == null)
                TryPickFromSlot(slot);
            else
                TryPlaceToSlot(slot);

            Refresh();
            OnEquipmentChanged?.Invoke();
        }

        private bool TryLootToRecipient(SlotWidget slot)
        {
            var recipient = BoardingLootRecipient;
            if (recipient == null || _ship == null) return false;
            var item = slot.Item;
            if (item == null) return false;
            // Корпус снимать нельзя (логика установки корпуса инвалидирует слоты).
            if (item.Category == EquipmentCategory.Hull) return false;

            if (slot.IsCargo)
            {
                InventoryService.TakeFromInventory(_ship, item.Uid);
                _cargoSlotMap.Remove(slot.CargoIndex);
            }
            else
            {
                if (InventoryService.UnequipFromSlot(_ship, slot.SlotKey) == null) return false;
            }

            InventoryService.AddToInventory(recipient, item);
            GameLog.Add($"[Абордаж] {item.Name} → инвентарь {recipient.Name}.");
            return true;
        }

        private void TryPickFromSlot(SlotWidget slot)
        {
            if (slot.Item == null) return;
            var item = slot.Item;
            if (slot.IsCargo)
            {
                InventoryService.TakeFromInventory(_ship, item.Uid);
                _cargoSlotMap.Remove(slot.CargoIndex);
            }
            else
            {
                if (InventoryService.UnequipFromSlot(_ship, slot.SlotKey) == null) return;
                // Снятие из слота меняет источники SlotCode/ShipScope — пересчитываем корабль.
                ShipBonusService.RecomputeShipEquipment(_ship);
            }
            SetHand(item, slot);
            HidePopup();
        }

        private void TryPlaceToSlot(SlotWidget targetSlot)
        {
            var handItem = _hand.Item;
            if (handItem == null) return;

            if (targetSlot.IsCargo)
            {
                PlaceToCargo(handItem, targetSlot);
                return;
            }

            if (targetSlot.SlotKey == SlotKeys.Throw)
            {
                ThrowHandIntoSpace(handItem);
                return;
            }

            if (targetSlot.SlotKey == SlotKeys.Activate)
            {
                ActivateHandItem(handItem);
                return;
            }

            if (!_ship.Equipment.Slots.ContainsKey(targetSlot.SlotKey)) return;

            // Встраиваемый предмет (микромодуль/встраиваемый артефакт) в руке + предмет в слоте,
            // разрешающий встраивание → попытка Install. Иначе идём по обычной ветке экипировки.
            if (handItem.IsEmbeddable && targetSlot.Item != null && targetSlot.Item.AllowEmbeds)
            {
                var check = EmbedService.CanInstall(targetSlot.Item, handItem);
                if (!check.Ok) return;
                if (EmbedService.Install(targetSlot.Item, handItem))
                {
                    _ship.Inventory.Remove(handItem.Uid);
                    _ship.AllItems.Remove(handItem.Uid);
                    ShipBonusService.RecomputeShipEquipment(_ship);
                    ClearHand();
                }
                return;
            }

            string targetCategory = EquipmentSystem.SlotCategory(targetSlot.SlotKey);
            if (!handItem.CanFitInSlotCategory(targetCategory)) return;

            ItemInstance displaced;
            if (handItem.Category == EquipmentCategory.Hull)
            {
                displaced = EquipmentSystem.GetEquipped(_ship, targetSlot.SlotKey);
                PlaceHull(handItem);
            }
            else
            {
                displaced = InventoryService.EquipToSlot(_ship, targetSlot.SlotKey, handItem);
            }

            // Установка в слот меняет источники SlotCode/ShipScope — пересчитываем корабль
            // (иначе Bonuses артефакта не применяются к другим слотам).
            ShipBonusService.RecomputeShipEquipment(_ship);

            if (displaced != null && displaced.Uid != handItem.Uid)
                SetHand(displaced, targetSlot);
            else
                ClearHand();
        }

        private void PlaceHull(ItemInstance hull)
        {
            var equipCfg = GalaxyManager.Instance?.Context?.ItemsConfig;
            if (equipCfg == null) return;

            string oldHullUid = _ship.Equipment.Clear(SlotKeys.Hull);
            if (oldHullUid != null && _ship.AllItems.TryGetValue(oldHullUid, out var oldHull))
            {
                _ship.AllItems.Remove(oldHullUid);
                PutToInventoryAtFreeCell(oldHull);
            }

            _ship.Equipment.Set(SlotKeys.Hull, hull.Uid);
            _ship.AllItems[hull.Uid] = hull;
            EquipmentSystem.ApplySeriesToHull(hull, equipCfg);
            var slotPlan = EquipmentSystem.GetHullSlotPlan(hull, equipCfg);
            var evicted = _ship.Equipment.RebuildForHullSlots(slotPlan, equipCfg);
            foreach (var uid in evicted)
                if (_ship.AllItems.TryGetValue(uid, out var ev))
                {
                    _ship.AllItems.Remove(uid);
                    PutToInventoryAtFreeCell(ev);
                }

            int hullHp = Mathf.RoundToInt(hull.GetParam("HP", hull.MaxDurability > 0 ? hull.MaxDurability : 100));
            _ship.MaxHull = hullHp;
            _ship.CurrentHull = Mathf.Min(_ship.CurrentHull, _ship.MaxHull);
            _ship.InvalidateWeaponSlotsCache();
            ShipFactory.RecalculateSpriteWorldSize(_ship);
        }

        /// <summary>
        /// Выкидывает предмет «из руки» в космос контейнером (CosmicItem с IsItem=true),
        /// который ставится в star.Ships рядом с кораблём. Item полностью удаляется из инвентаря/AllItems корабля.
        /// </summary>
        private void ThrowHandIntoSpace(ItemInstance item)
        {
            if (_ship == null || item == null) return;

            var star = ResolveCurrentStar();
            if (star == null)
            {
                GameLog.Add("[Выбросить] Нет звёздной системы для спавна контейнера.");
                return;
            }

            // «Теневой» предмет, представляющий стек товара: количество выбирается ползунком
            // в мини-форме; стек весом 1 кидается сразу без диалога.
            if (IsStackItem(item))
            {
                if (item.Weight <= 0)
                {
                    GameLog.Add("[Выбросить] Стек груза пуст.");
                    ClearHand();
                    return;
                }
                if (item.Weight == 1)
                {
                    ThrowStackAmount(item, 1);
                    return;
                }
                HidePopup();
                _throwDialog.Show($"Выбросить: {item.Name}", item.Weight,
                                  amount => ThrowStackAmount(item, amount),
                                  onCancel: CancelHand,
                                  confirmLabel: "Выбросить");
                return;
            }

            InventoryService.RemoveCompletely(_ship, item);
            var container = ContainerFactory.SpawnContainerWithItem(_ship, item, star);
            if (container != null)
            {
                // Контейнер должен появиться визуально немедленно — без ожидания следующего хода симуляции.
                SystemViewManager.Instance?.EnsureShipVisual(container);
                GameLog.Add($"[Выбросить] {item.Name} → контейнер в космосе.");
            }

            ClearHand();
        }

        private static bool IsStackItem(ItemInstance item) =>
            !string.IsNullOrEmpty(item?.Uid)
            && item.Uid.StartsWith(ShipInventory.StackItemUidPrefix, System.StringComparison.Ordinal);

        private StarData ResolveCurrentStar()
        {
            var star = _ship.CurrentStar;
            if (star == null && !string.IsNullOrEmpty(_ship.CurrentStarUid))
            {
                var map = GalaxyManager.Instance?.GeneratedGalaxy?.StarsMap;
                if (map != null) map.TryGetValue(_ship.CurrentStarUid, out star);
            }
            return star;
        }

        /// <summary>
        /// Выбрасывает <paramref name="amount"/> единиц из стека, представленного «теневым»
        /// предметом из руки. Стек нужно изымать через TakeStack — иначе вес из Stacks останется
        /// и груз задублируется. Остаток (если есть) TakeStack пересоздаёт новым теневым
        /// ItemInstance — возвращаем его в исходную ячейку трюма.
        /// </summary>
        private void ThrowStackAmount(ItemInstance stackItem, int amount)
        {
            var star = ResolveCurrentStar();
            if (star == null)
            {
                GameLog.Add("[Выбросить] Нет звёздной системы для спавна контейнера.");
                CancelHand();
                return;
            }

            string goodId = stackItem.ItemId;
            var taken = _ship.Inventory?.TakeStack(goodId, amount);
            if (taken == null || taken.TotalWeight <= 0)
            {
                GameLog.Add("[Выбросить] Стек груза пуст.");
                ClearHand();
                Refresh();
                return;
            }

            var goodsContainer = ContainerFactory.SpawnContainerWithStack(_ship, taken, star);
            if (goodsContainer != null)
            {
                SystemViewManager.Instance?.EnsureShipVisual(goodsContainer);
                GameLog.Add($"[Выбросить] {taken.Name} x{taken.TotalWeight} → контейнер в космосе.");
            }

            var rest = _ship.Inventory?.GetByUid(ShipInventory.StackItemUidPrefix + goodId);
            if (rest != null)
            {
                var origin = _hand.OriginSlot;
                if (origin != null && origin.IsCargo
                    && (!_cargoSlotMap.ContainsKey(origin.CargoIndex) || _cargoSlotMap[origin.CargoIndex] == null))
                    _cargoSlotMap[origin.CargoIndex] = rest;
                else
                    AssignToFirstFreeCargoCell(rest);
            }

            ClearHand();
            Refresh();
            OnEquipmentChanged?.Invoke();
        }

        /// <summary>
        /// Активация предмета: возвращает его в исходный слот/трюм и запускает
        /// EquipmentSystem.ActivateItem (форсаж → ship.ForsageActive = true; в будущем
        /// здесь будет произвольный скрипт предмета).
        /// </summary>
        private void ActivateHandItem(ItemInstance item)
        {
            if (_ship == null || item == null) return;
            var origin = _hand.OriginSlot;

            bool returned = false;
            if (origin != null && origin.IsCargo)
            {
                if (!_cargoSlotMap.ContainsKey(origin.CargoIndex)
                    || _cargoSlotMap[origin.CargoIndex] == null)
                    _cargoSlotMap[origin.CargoIndex] = item;
                else
                    AssignToFirstFreeCargoCell(item);
                if (!InventoryService.InventoryContains(_ship, item.Uid))
                    InventoryService.AddToInventory(_ship, item);
                returned = true;
            }
            else if (origin != null && _ship.Equipment.Slots.ContainsKey(origin.SlotKey)
                     && EquipmentSystem.GetEquipped(_ship, origin.SlotKey) == null
                     && item.CanFitInSlotCategory(EquipmentSystem.SlotCategory(origin.SlotKey)))
            {
                InventoryService.EquipToSlot(_ship, origin.SlotKey, item);
                returned = true;
            }
            else
            {
                PutToInventoryAtFreeCell(item);
                returned = true;
            }

            if (returned)
            {
                bool activated = EquipmentSystem.ActivateItem(_ship, item);
                // Активация форсажа меняет скорость → пересчитать метки дней на маршруте.
                if (activated && item.Category == EquipmentCategory.Forsage && PlayerShip.Instance != null)
                    PlayerShip.Instance.RefreshPlannedRoute();
                // Дрон-обёртка потребляется при развёртывании → перестроить cargo-map чтобы
                // не осталась иконка в клетке трюма.
                if (activated && item.Category == EquipmentCategory.CompanionDrone)
                {
                    RebuildCargoMap();
                    RefreshCargo();
                }
            }

            ClearHand();
        }

        /// <summary>Показывает/скрывает слот активации в зависимости от того, активируемый ли
        /// предмет сейчас в руке.</summary>
        private void UpdateActivateSlotVisibility()
        {
            if (_activateSlot == null) return;
            bool visible = _hand.Item != null && _hand.Item.Activatable;
            if (_activateSlot.Root.gameObject.activeSelf != visible)
                _activateSlot.Root.gameObject.SetActive(visible);
        }

        private void PlaceToCargo(ItemInstance item, SlotWidget targetSlot)
        {
            int idx = targetSlot.CargoIndex;
            // Swap: если в ячейке уже что-то лежит — оно уходит в руку.
            if (_cargoSlotMap.TryGetValue(idx, out var existing) && existing != null && existing != item)
            {
                // existing уходит из инвентаря в руку; item занимает её место.
                InventoryService.RemoveFromInventory(_ship, existing.Uid);
                _cargoSlotMap.Remove(idx);

                _cargoSlotMap[idx] = item;
                if (!InventoryService.InventoryContains(_ship, item.Uid))
                    InventoryService.AddToInventory(_ship, item);

                SetHand(existing, targetSlot);
                return;
            }

            _cargoSlotMap[idx] = item;
            if (!InventoryService.InventoryContains(_ship, item.Uid))
                InventoryService.AddToInventory(_ship, item);
            ClearHand();
        }

        private void SetHand(ItemInstance item, SlotWidget origin)
        {
            _hand.Item = item;
            _hand.OriginSlot = origin;
            var sprite = LoadItemStaticSprite(item);
            _handIcon.sprite = sprite;
            _handIcon.color = sprite != null
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(0.9f, 0.85f, 0.4f, 0.7f);
            _handIcon.enabled = true;
            _handIcon.preserveAspect = false;
            // В руке оборудование показывается в натуральном размере спрайта _i, без скейла.
            if (sprite != null)
                _handRoot.sizeDelta = sprite.rect.size;
            else
                _handRoot.sizeDelta = new Vector2(SlotSize, SlotSize);
            _handRoot.gameObject.SetActive(true);
            _handRoot.SetAsLastSibling();
            RefreshEmbedBadge(_handEmbedBadge, item);
            UpdateHandPosition();
            UpdateActivateSlotVisibility();
            UpdateEmbedTargetHighlight();
        }

        private void ClearHand()
        {
            _hand = default;
            RefreshEmbedBadge(_handEmbedBadge, null);
            _handRoot.gameObject.SetActive(false);
            UpdateActivateSlotVisibility();
            UpdateEmbedTargetHighlight();
            // Рука ушла — диалог количества больше не имеет смысла (в т.ч. при
            // принудительном сбросе руки извне: DropHandToSource при закрытии панели).
            _throwDialog?.CloseSilent();
        }

        /// <summary>Пока в руке встраиваемый предмет (микромодуль/встраиваемый артефакт) — слоты
        /// оборудования с носителем, куда его можно вставить, подсвечиваются зелёным (SlotGreen).
        /// При сбросе руки — визуал слотов восстанавливается через <see cref="RestoreSlotBg"/>.</summary>
        private void UpdateEmbedTargetHighlight()
        {
            var hand = _hand.Item;
            bool active = hand != null && hand.IsEmbeddable;
            foreach (var slot in _slots)
            {
                if (slot.IsCargo) continue;
                if (slot.SlotKey == SlotKeys.Throw || slot.SlotKey == SlotKeys.Activate) continue;
                if (slot.IsBlocked) continue;
                if (!active)
                {
                    RestoreSlotBg(slot);
                    continue;
                }
                if (slot.Item != null && slot.Item.AllowEmbeds
                    && EmbedService.CanInstall(slot.Item, hand).Ok)
                {
                    ApplyBg(slot, SlotVisualState.Green);
                }
                else
                {
                    RestoreSlotBg(slot);
                }
            }
        }

        private void CancelHand()
        {
            if (_hand.Item == null)
            {
                if (_handRoot != null) _handRoot.gameObject.SetActive(false);
                return;
            }

            var item = _hand.Item;
            var origin = _hand.OriginSlot;
            if (origin != null && origin.IsCargo)
            {
                // Возврат в ту же ячейку трюма, если она свободна; иначе — в первую свободную.
                if (!_cargoSlotMap.ContainsKey(origin.CargoIndex)
                    || _cargoSlotMap[origin.CargoIndex] == null)
                    _cargoSlotMap[origin.CargoIndex] = item;
                else
                    AssignToFirstFreeCargoCell(item);
                if (_ship != null && !_ship.Inventory.Contains(item.Uid))
                    _ship.Inventory.Add(item);
            }
            else if (origin != null)
            {
                if (_ship != null && _ship.Equipment.Slots.ContainsKey(origin.SlotKey)
                    && EquipmentSystem.GetEquipped(_ship, origin.SlotKey) == null)
                {
                    _ship.Equipment.Set(origin.SlotKey, item.Uid);
                    _ship.AllItems[item.Uid] = item;
                    if (item.Category == EquipmentCategory.Shield)
                        WeaponSystem.RebuildShieldState(_ship);
                    _ship.InvalidateWeaponSlotsCache();
                }
                else
                {
                    PutToInventoryAtFreeCell(item);
                }
            }
            else
            {
                PutToInventoryAtFreeCell(item);
            }

            ClearHand();
            Refresh();
        }

        private void PutToInventoryAtFreeCell(ItemInstance item)
        {
            if (item == null || _ship?.Inventory == null) return;
            if (!_ship.Inventory.Contains(item.Uid))
                _ship.Inventory.Add(item);
            AssignToFirstFreeCargoCell(item);
        }

        private void AssignToFirstFreeCargoCell(ItemInstance item)
        {
            int cap = CargoColumns * CargoRows;
            for (int i = 0; i < cap; i++)
            {
                if (!_cargoSlotMap.ContainsKey(i) || _cargoSlotMap[i] == null)
                {
                    _cargoSlotMap[i] = item;
                    return;
                }
            }
        }

        private void RestoreSlotBg(SlotWidget slot)
        {
            if (slot.IsCargo)
            {
                if (slot.Item == null)
                    ApplyBg(slot, SlotVisualState.Usual);
                else
                    ApplyBg(slot, slot.Item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
                return;
            }
            if (_ship?.Equipment == null || !_ship.Equipment.Slots.ContainsKey(slot.SlotKey))
                ApplyBg(slot, SlotVisualState.Blocked);
            else if (slot.Item == null)
                ApplyBg(slot, SlotVisualState.Usual);
            else if (IsEmbedTarget(slot))
                ApplyBg(slot, SlotVisualState.Green);
            else
                ApplyBg(slot, slot.Item.IsWorking ? SlotVisualState.Usual : SlotVisualState.Broken);
        }

        /// <summary>true, если в руке встраиваемый предмет, а на слоте — совместимый носитель.</summary>
        private bool IsEmbedTarget(SlotWidget slot)
        {
            var hand = _hand.Item;
            if (hand == null || !hand.IsEmbeddable) return false;
            if (slot.Item == null || !slot.Item.AllowEmbeds) return false;
            return EmbedService.CanInstall(slot.Item, hand).Ok;
        }
    }
}
