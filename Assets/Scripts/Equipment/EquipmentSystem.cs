using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>
    /// Фасад работы с оборудованием корабля. Разделён на partial-файлы по ролям (этап T3
    /// рефакторинга, июнь 2026):
    ///   • <c>EquipmentSystem.cs</c>          — установка/снятие, базовый lookup, EquipResult
    ///   • <c>EquipmentSystem.Speed.cs</c>    — CalculateSpeed / CalculateOwnEngineSpeed
    ///   • <c>EquipmentSystem.Wear.cs</c>     — износ, damage, ApplyTurnEffects, GetRandomDamageableSlot
    ///   • <c>EquipmentSystem.Fuel.cs</c>     — топливо, JumpRange, RadarRange
    ///   • <c>EquipmentSystem.Activation.cs</c> — активация (форсаж), CanActivateForsage, IsLandingThisTurn
    /// Все методы остались static; сигнатуры не менялись.
    /// </summary>
    public static partial class EquipmentSystem
    {
        /// <summary>
        /// Устанавливает предмет из инвентаря в слот снаряжения.
        /// allowInSpace должен передаваться из GameSettingsConfig.AllowEquipmentChangeInSpace —
        /// по дизайн-документу смена снаряжения разрешена только при стыковке (HasDock).
        /// </summary>
        public static EquipResult Install(
            ShipData ship,
            string slotKey,
            string itemUid,
            ItemsConfig equipConfig,
            bool allowInSpace = false)
        {
            if (!allowInSpace)
                return EquipResult.Fail("Смена снаряжения в космосе запрещена настройками.");

            var item = ship.Inventory.TakeByUid(itemUid);
            if (item == null)
                return EquipResult.Fail($"Предмет {itemUid} не найден в инвентаре.");

            string expectedCategory = SlotCategory(slotKey);
            if (!item.CanFitInSlotCategory(expectedCategory))
            {
                ship.Inventory.Add(item); // вернуть
                return EquipResult.Fail($"Категория предмета '{item.Category}' не подходит к слоту '{slotKey}'.");
            }

            if (!ship.Equipment.Slots.ContainsKey(slotKey))
            {
                ship.Inventory.Add(item);
                return EquipResult.Fail($"Слот '{slotKey}' недоступен для текущего корпуса.");
            }

            if (item.Category == EquipmentCategory.Hull)
                return InstallHull(ship, slotKey, item, equipConfig);

            string prevUid = ship.Equipment.Set(slotKey, item.Uid);
            ship.AllItems[item.Uid] = item;

            if (prevUid != null && ship.AllItems.TryGetValue(prevUid, out var prev))
            {
                ship.AllItems.Remove(prevUid);
                ship.Inventory.Add(prev);
            }

            if (item.Category == EquipmentCategory.Shield || SlotCategory(slotKey) == EquipmentCategory.Shield)
                WeaponSystem.RebuildShieldState(ship);

            ship.InvalidateWeaponSlotsCache();
            ShipBonusService.RecomputeShipEquipment(ship);
            return EquipResult.Ok($"Установлен: {item.Name}");
        }

        private static EquipResult InstallHull(
            ShipData ship, string slotKey, ItemInstance hull, ItemsConfig equipConfig)
        {
            var slotPlan = GetHullSlotPlan(hull, equipConfig);
            if (slotPlan == null)
            {
                ship.Inventory.Add(hull);
                return EquipResult.Fail($"Не удалось определить слоты для корпуса '{hull.Name}'.");
            }

            // Захватываем HP до свопа: после рефактора HP хранится в hull.Durability, и после
            // подмены корпуса чтение ship.CurrentHull вернёт значение нового hull-предмета,
            // потеряв текущий уровень HP пилота. Сохраняем до Clear() и восстанавливаем в конце.
            int preSwapHp = ship.CurrentHull;

            string oldHullUid = ship.Equipment.Clear(slotKey);
            if (oldHullUid != null && ship.AllItems.TryGetValue(oldHullUid, out var oldHull))
            {
                ship.AllItems.Remove(oldHullUid);
                ship.Inventory.Add(oldHull);
            }

            ship.Equipment.Set(slotKey, hull.Uid);
            ship.AllItems[hull.Uid] = hull;
            ApplySeriesToHull(hull, equipConfig);
            var evicted = ship.Equipment.RebuildForHullSlots(slotPlan, equipConfig);
            foreach (var uid in evicted)
            {
                if (ship.AllItems.TryGetValue(uid, out var evictedItem))
                {
                    ship.AllItems.Remove(uid);
                    ship.Inventory.Add(evictedItem);
                }
            }

            int hullHp = Mathf.RoundToInt(hull.GetParam("HP", hull.MaxDurability > 0 ? hull.MaxDurability : 100));
            ship.MaxHull = hullHp;
            // Сохраняем pre-swap HP (не выше нового потолка). Для fresh-spawn preSwapHp == 0 →
            // ship.CurrentHull останется 0, поэтому ItemFactory.EquipStarterKit после этого
            // должен явно проставить ship.CurrentHull = ship.MaxHull (уже так и делает).
            ship.CurrentHull = Mathf.Min(preSwapHp, hullHp);

            ship.InvalidateWeaponSlotsCache();
            ShipBonusService.RecomputeShipEquipment(ship);
            string formId = hull.GetParamString("HullType");
            return EquipResult.Ok($"Корпус '{hull.Name}' установлен, тип: {formId}.");
        }

        /// <summary>
        /// Рассчитывает итоговый набор слотов корпуса: HullSlotsHullType[form] + HullSlotsRace[race].
        /// Если на корпусе выставлена серия (Params["HullSeries"]) — её Slots перебивают всё.
        /// </summary>
        public static Dictionary<string, int> GetHullSlotPlan(ItemInstance hull, ItemsConfig equipConfig)
        {
            if (hull == null || equipConfig == null) return null;

            Dictionary<string, int> plan;
            string seriesId = hull.GetParamString("HullSeries");
            if (seriesId != null)
            {
                var series = equipConfig.GetHullSeries(seriesId);
                plan = series != null ? new Dictionary<string, int>(series.Slots) : null;
            }
            else
            {
                var hullTpl = equipConfig.GetTemplate(EquipmentCategory.Hull);
                plan = hullTpl?.GetEffectiveSlotsForCost(hull.GetParamString("HullType"), hull.ManufacturerRace);
            }
            if (plan == null) return null;

            // Прибавляем SlotBonuses встроенных предметов корпуса (§7.3 micromodules_design.md).
            if (hull.Embeds != null && hull.Embeds.Count > 0)
            {
                foreach (var uid in hull.Embeds)
                {
                    if (!hull.EmbedItems.TryGetValue(uid, out var emb)) continue;
                    if (emb.Embed?.SlotBonuses == null) continue;
                    foreach (var kv in emb.Embed.SlotBonuses)
                    {
                        if (kv.Key == "Embeds") continue; // не путать с бонусом на число встроенных
                        plan.TryGetValue(kv.Key, out var cur);
                        plan[kv.Key] = Mathf.Max(0, cur + kv.Value);
                    }
                }
            }
            return plan;
        }

        /// <summary>Если на корпусе выставлена серия — переносит WeaponVulnerability на корабельный предмет.</summary>
        public static void ApplySeriesToHull(ItemInstance hull, ItemsConfig equipConfig)
        {
            if (hull == null || equipConfig == null) return;
            string seriesId = hull.GetParamString("HullSeries");
            if (seriesId == null) return;
            var series = equipConfig.GetHullSeries(seriesId);
            if (series == null) return;
            if (series.WeaponVulnerability != null && series.WeaponVulnerability.Count > 0)
                hull.WeaponVulnerability = new Dictionary<string, float>(series.WeaponVulnerability);
        }

        public static EquipResult Uninstall(ShipData ship, string slotKey, bool allowInSpace = false)
        {
            if (!allowInSpace)
                return EquipResult.Fail("Смена снаряжения в космосе запрещена настройками.");

            if (SlotCategory(slotKey) == EquipmentCategory.Hull)
                return EquipResult.Fail("Корпус нельзя снять, только заменить.");

            string uid = ship.Equipment.Clear(slotKey);
            if (uid == null)
                return EquipResult.Fail($"Слот '{slotKey}' уже пуст.");

            if (ship.AllItems.TryGetValue(uid, out var item))
            {
                ship.AllItems.Remove(uid);
                ship.Inventory.Add(item);

                if (item.Category == EquipmentCategory.Shield)
                    WeaponSystem.RebuildShieldState(ship);

                ShipBonusService.RecomputeShipEquipment(ship);
                return EquipResult.Ok($"Снято: {item.Name}");
            }

            return EquipResult.Fail($"Предмет {uid} не найден в AllItems.");
        }

        // ── Lookup / геометрия слотов ─────────────────────────────────────────

        public static int GetHullCapacity(ShipData ship)
        {
            var hull = GetEquipped(ship, SlotKeys.Hull);
            return hull?.Weight ?? 0;
        }

        public static int GetEquippedWeight(ShipData ship)
        {
            int total = 0;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (SlotCategory(kv.Key) == EquipmentCategory.Hull) continue;
                if (ship.AllItems.TryGetValue(kv.Value, out var item))
                    total += item.Weight;
            }
            return total;
        }

        public static int GetFreeSpace(ShipData ship)
        {
            return GetHullCapacity(ship) - GetEquippedWeight(ship) - ship.Inventory.TotalWeight();
        }

        public static ItemInstance GetEquipped(ShipData ship, string slotKey)
        {
            if (ship?.Equipment == null) return null;
            string uid = ship.Equipment.GetItemUid(slotKey);
            if (uid == null) return null;
            ship.AllItems.TryGetValue(uid, out var item);
            return item;
        }

        /// <summary>
        /// Возвращает первый предмет указанной категории, установленный в любом слоте корабля.
        /// При <paramref name="requireWorking"/>=true пропускает повреждённое/сломанное оборудование.
        /// Единая точка для PickupSystem.FindCargoGrabber / TowSystem.FindTowingRig /
        /// BoardingSystem.FindGrapplingHook (которые делегируют сюда).
        /// </summary>
        public static ItemInstance FindEquipmentByCategory(ShipData ship, string category, bool requireWorking = true)
        {
            if (ship?.Equipment == null) return null;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (item.Category != category) continue;
                if (requireWorking && !item.IsWorking) continue;
                return item;
            }
            return null;
        }

        public static float GetHullParam(ShipData ship, string paramKey)
        {
            var hull = GetEquipped(ship, SlotKeys.Hull);
            return hull?.GetParam(paramKey) ?? 0f;
        }

        public static float GetHullVulnerability(ShipData ship, string damageType)
        {
            var hull = GetEquipped(ship, SlotKeys.Hull);
            return hull?.GetVulnerability(damageType) ?? 1f;
        }

        public static string SlotCategory(string slotKey)
        {
            int idx = slotKey.LastIndexOf('_');
            return idx > 0 ? slotKey.Substring(0, idx) : slotKey;
        }

        /// <summary>
        /// Считает общий вес (оборудование + трюм) всех потомков по дереву буксира.
        /// Используется в Speed-партициале для добавления массы буксируемых к нагрузке буксирующего.
        /// </summary>
        private static int SumTowedSubtreeWeight(ShipData root)
        {
            if (root?.TowedObjectUids == null || root.TowedObjectUids.Count == 0) return 0;
            int total = 0;
            var stack = new System.Collections.Generic.Stack<ShipData>();
            foreach (var uid in root.TowedObjectUids)
            {
                var child = FindShipByUid(uid, root.CurrentStarUid);
                if (child != null) stack.Push(child);
            }
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                total += GetEquippedWeight(node) + node.Inventory.TotalWeight();
                foreach (var uid in node.TowedObjectUids)
                {
                    var child = FindShipByUid(uid, node.CurrentStarUid);
                    if (child != null) stack.Push(child);
                }
            }
            return total;
        }

        /// <summary>
        /// Поиск ShipData по uid: сперва в указанной звезде, затем по всем звёздам галактики.
        /// Используется для разыменования BoardedByUid/TowedByUid в CalculateSpeed.
        /// </summary>
        private static ShipData FindShipByUid(string uid, string preferStarUid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy?.StarsMap == null) return null;

            if (!string.IsNullOrEmpty(preferStarUid)
                && galaxy.StarsMap.TryGetValue(preferStarUid, out var localStar))
            {
                for (int i = 0; i < localStar.Ships.Count; i++)
                    if (localStar.Ships[i].Uid == uid) return localStar.Ships[i];
            }
            foreach (var star in galaxy.StarsMap.Values)
            {
                for (int i = 0; i < star.Ships.Count; i++)
                    if (star.Ships[i].Uid == uid) return star.Ships[i];
            }
            return null;
        }
    }

    public readonly struct EquipResult
    {
        public readonly bool Success;
        public readonly string Message;

        private EquipResult(bool success, string message) { Success = success; Message = message; }

        public static EquipResult Ok(string msg) => new EquipResult(true, msg);
        public static EquipResult Fail(string msg) => new EquipResult(false, msg);

        public override string ToString() => $"[{(Success ? "OK" : "FAIL")}] {Message}";
    }
}
