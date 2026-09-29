using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.NpcAI.Actions;

namespace SRG.Ships.Services
{
    /// <summary>
    /// Единая процедура ремонта корабля у планеты: корпус (<see cref="ShipData.CurrentHull"/>)
    /// и Durability всех предметов оборудования. Считает стоимость по общей формуле
    ///     costPerPoint(TL) = ceil( Repair_CostPerDurabilityPoint × (1 + TL × Repair_TLMultiplier) )
    /// (значения — <see cref="NpcBalance.Repair_CostPerDurabilityPoint"/>,
    ///  <see cref="NpcBalance.Repair_TLMultiplier"/>, конфиг GameSettingsConfig.Repair_*).
    /// Приоритет ремонта:
    ///   1. Корпус — всегда первым.
    ///   2. Оборудование по убыванию <see cref="ShipTypeConfig.EquipmentPriority"/>[category].
    /// Тратит доступный бюджет = ship.Money − <see cref="NpcBalance.Outfitter_EmergencyCashReserve"/>.
    /// Используется <see cref="ActionLandResupply"/>.
    /// </summary>
    public static class RepairService
    {
        /// <summary>Отчёт: сколько потратили, сколько восстановили HP корпуса и Durability оборудования.</summary>
        public readonly struct RepairReport
        {
            public readonly int Spent;
            public readonly int HullRestored;
            public readonly int EquipmentRestored;
            public readonly int EquipmentItemsTouched;

            public RepairReport(int spent, int hullRestored, int equipmentRestored, int equipmentItemsTouched)
            {
                Spent = spent;
                HullRestored = hullRestored;
                EquipmentRestored = equipmentRestored;
                EquipmentItemsTouched = equipmentItemsTouched;
            }

            public bool AnythingRestored => HullRestored > 0 || EquipmentRestored > 0;
        }

        /// <summary>Стоимость восстановления одной единицы Durability для предмета данного тира.</summary>
        public static int CostPerPoint(int techLevel)
        {
            int tl = Mathf.Max(1, techLevel);
            float mult = 1f + tl * NpcBalance.Repair_TLMultiplier;
            return Mathf.Max(1, Mathf.CeilToInt(NpcBalance.Repair_CostPerDurabilityPoint * mult));
        }

        /// <summary>Оценка полной стоимости ремонта корпуса + всего оборудования до 100%.</summary>
        public static int EstimateFullRepairCost(ShipData ship)
        {
            if (ship == null) return 0;
            int total = 0;

            int hullMissing = ship.MaxHull - ship.CurrentHull;
            if (hullMissing > 0)
            {
                var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
                total += hullMissing * CostPerPoint(hull?.TechLevel ?? 1);
            }

            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (item.MaxDurability <= 0) continue;
                if (item.Category == EquipmentCategory.Hull) continue;
                int missing = item.MaxDurability - item.Durability;
                if (missing <= 0) continue;
                total += missing * CostPerPoint(item.TechLevel);
            }
            return total;
        }

        /// <summary>Оценка стоимости ремонта только корпуса до 100% (без оборудования).</summary>
        public static int EstimateHullRepairCost(ShipData ship)
        {
            if (ship == null) return 0;
            int hullMissing = ship.MaxHull - ship.CurrentHull;
            if (hullMissing <= 0) return 0;
            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            return hullMissing * CostPerPoint(hull?.TechLevel ?? 1);
        }

        /// <summary>
        /// Ремонт только корпуса на посадочной цели (кнопка «Починить корпус» в ангаре).
        /// В отличие от <see cref="RepairShipAtPlanet"/> не трогает оборудование и не резервирует
        /// EmergencyCashReserve — игрок волен потратить все кредиты. Возвращает восстановленные HP.
        /// </summary>
        public static int RepairHullOnly(ShipData ship, ILandingSite site)
        {
            if (ship == null || site == null) return 0;
            int hullMissing = ship.MaxHull - ship.CurrentHull;
            if (hullMissing <= 0) return 0;

            var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
            int perPoint = CostPerPoint(hull?.TechLevel ?? 1);
            int affordable = perPoint > 0 ? Mathf.Max(0, ship.Money) / perPoint : 0;
            int restore = Mathf.Min(hullMissing, affordable);
            if (restore <= 0) return 0;

            ship.CurrentHull += restore;
            int cost = restore * perPoint;
            ship.Money -= cost;

            int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "REPAIR_HULL",
                $"site={EconomicLog.Safe(site.Name)} hull_pts={restore} spent={cost} money_after={ship.Money}");
            return restore;
        }

        /// <summary>
        /// Ремонт корабля у планеты. Тратит до (Money − EmergencyCashReserve). Порядок:
        /// корпус → оборудование по убыванию <see cref="ShipTypeConfig.EquipmentPriority"/>.
        /// Если ShipTypeConfig не найден в контексте — все веса = 1 (порядок не важен).
        /// </summary>
        public static RepairReport RepairShipAtPlanet(ShipData ship, ILandingSite site, ShipTypeConfig shipType = null)
        {
            if (ship == null || site == null) return default;

            int budget = Mathf.Max(0, ship.Money - NpcBalance.Outfitter_EmergencyCashReserve);
            int spent = 0;
            int hullRestored = 0;
            int equipmentRestored = 0;
            int equipmentItemsTouched = 0;

            // 1) Корпус
            int hullMissing = ship.MaxHull - ship.CurrentHull;
            if (hullMissing > 0 && budget > 0)
            {
                var hull = EquipmentSystem.GetEquipped(ship, SlotKeys.Hull);
                int perPoint = CostPerPoint(hull?.TechLevel ?? 1);
                int affordable = perPoint > 0 ? budget / perPoint : 0;
                int restore = Mathf.Min(hullMissing, affordable);
                if (restore > 0)
                {
                    ship.CurrentHull += restore;
                    int cost = restore * perPoint;
                    ship.Money -= cost;
                    budget -= cost;
                    spent += cost;
                    hullRestored = restore;
                }
            }

            // 2) Оборудование в порядке приоритета
            shipType ??= ResolveShipType(ship);
            var order = SortEquipmentByPriority(ship, shipType);
            for (int i = 0; i < order.Count && budget > 0; i++)
            {
                var item = order[i].item;
                if (item.MaxDurability <= 0) continue;
                int missing = item.MaxDurability - item.Durability;
                if (missing <= 0) continue;

                int perPoint = CostPerPoint(item.TechLevel);
                int affordable = perPoint > 0 ? budget / perPoint : 0;
                int restore = Mathf.Min(missing, affordable);
                if (restore <= 0) continue;

                item.Durability += restore;
                int cost = restore * perPoint;
                ship.Money -= cost;
                budget -= cost;
                spent += cost;
                equipmentRestored += restore;
                equipmentItemsTouched++;
            }
            order.Clear();
            _sortBuffer = order;

            if (spent > 0)
            {
                int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
                EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "REPAIR",
                    $"site={EconomicLog.Safe(site.Name)} hull_pts={hullRestored} equip_pts={equipmentRestored} " +
                    $"items={equipmentItemsTouched} spent={spent} money_after={ship.Money}");
            }

            return new RepairReport(spent, hullRestored, equipmentRestored, equipmentItemsTouched);
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>Вес приоритета категории. Отсутствие ключа = 1.</summary>
        public static int GetPriorityWeight(ShipTypeConfig shipType, string category)
        {
            if (shipType?.EquipmentPriority == null) return 1;
            return shipType.EquipmentPriority.TryGetValue(category, out var w) ? w : 1;
        }

        public static ShipTypeConfig ResolveShipType(ShipData ship)
        {
            var types = GalaxyManager.Instance?.Context?.Config?.Ships?.ShipTypes;
            if (types == null || string.IsNullOrEmpty(ship?.ShipTypeId)) return null;
            return types.TryGetValue(ship.ShipTypeId, out var t) ? t : null;
        }

        // Переиспользуемый буфер сортировки — RepairShipAtPlanet вызывается на каждой посадке
        // множества NPC (потенциально 100+ раз за ход), новый List каждый раз — лишние GC.
        private static List<(string slotKey, ItemInstance item, int weight)> _sortBuffer = new();

        private static List<(string slotKey, ItemInstance item, int weight)> SortEquipmentByPriority(
            ShipData ship, ShipTypeConfig shipType)
        {
            var list = _sortBuffer;
            _sortBuffer = null;
            list ??= new List<(string, ItemInstance, int)>();

            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (item.Category == EquipmentCategory.Hull) continue;
                int weight = GetPriorityWeight(shipType, item.Category);
                list.Add((kv.Key, item, weight));
            }
            list.Sort(static (a, b) => b.weight.CompareTo(a.weight));
            return list;
        }
    }
}
