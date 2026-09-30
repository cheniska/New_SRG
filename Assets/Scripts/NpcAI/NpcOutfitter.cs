using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Actions;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.NpcAI
{
    /// <summary>
    /// Утилита апгрейда снаряжения NPC у планеты. Не является NpcAction — вызывается изнутри
    /// <see cref="ActionLandResupply"/> когда корабль уже приземлился и уже отремонтирован/заправлен.
    ///
    /// Скор предмета:
    ///     rawScore   = f(category, params, techLevel)   // базовая ценность, детерминированная
    ///     finalScore = rawScore × EquipmentPriority[category]  // множитель из ShipTypeConfig
    /// Апгрейд ставится только когда score(new) ≥ score(current) × (1 + <see cref="NpcBalance.Outfitter_MinImprovement"/>).
    /// За один визит — максимум один апгрейд (растягиваем прогресс, не позволяем полной перепрошивке за раз).
    ///
    /// Продажа снятого предмета: 50% Price возвращаются кораблю, предмет уходит в
    /// <see cref="PlanetEquipmentShop.Items"/> (тот же цикл, что у игрока в PlanetUIController).
    /// </summary>
    public static class NpcOutfitter
    {
        /// <summary>Оценивает «полезность» предмета для типа корабля. 0 при отсутствии предмета или поломке.</summary>
        public static float ScoreItem(ItemInstance item, ShipTypeConfig shipType)
        {
            if (item == null) return 0f;
            if (!item.IsWorking) return 0f;

            float raw = RawScore(item);
            int weight = RepairService.GetPriorityWeight(shipType, item.Category);
            if (weight <= 0) return 0f; // вес 0 — эту категорию не апгрейдим
            return raw * weight;
        }

        /// <summary>Базовая ценность без учёта весов ShipType. Формула зависит от категории.</summary>
        private static float RawScore(ItemInstance item)
        {
            float tlBonus = item.TechLevel * 2f;

            switch (item.Category)
            {
                case EquipmentCategory.Weapons:
                    return (item.GetParam("MinDmg") + item.GetParam("MaxDmg")) * 0.5f + tlBonus;
                case EquipmentCategory.Hull:
                    return item.GetParam("HP") + item.GetParam("Armor") * 0.5f + tlBonus;
                case EquipmentCategory.Shield:
                    return item.GetParam("BlockPercent") + item.MaxDurability * 0.05f + tlBonus;
                case EquipmentCategory.Engine:
                    return item.GetParam("Speed") * 10f + item.GetParam("JumpRange") * 2f + tlBonus;
                case EquipmentCategory.FuelTank:
                    return item.GetParam("Capacity") * 0.5f + tlBonus;
                case EquipmentCategory.Forsage:
                    return item.GetParam("SpeedMultiplier", 1f) * 10f + tlBonus;
                case EquipmentCategory.Radar:
                    return item.GetParam("Range") * 5f + tlBonus;
                case EquipmentCategory.Scanner:
                    return item.GetParam("Power") * 5f + tlBonus;
                case EquipmentCategory.Droid:
                    return item.GetParam("HealPerTurn") * 5f + tlBonus;
                case EquipmentCategory.CargoGrabber:
                    return item.GetParam("PullRadius") * 3f + tlBonus;
                case EquipmentCategory.Artefacts:
                    return item.MaxDurability * 0.5f + tlBonus;
                default:
                    return tlBonus;
            }
        }

        /// <summary>Резерв бюджета «на будущее топливо»: стоимость полного бака × множитель.</summary>
        public static int ComputeFuelReserve(ShipData ship)
        {
            int cap = EquipmentSystem.GetFuelCapacity(ship);
            if (cap <= 0) return 0;
            return Mathf.CeilToInt(cap * FuelService.FuelCostPerUnit * NpcBalance.Outfitter_FuelReserveMultiplier);
        }

        /// <summary>Свободный бюджет на апгрейд: Money − EmergencyReserve − FuelReserve.</summary>
        public static int ComputeUpgradeBudget(ShipData ship)
        {
            int budget = ship.Money
                         - NpcBalance.Outfitter_EmergencyCashReserve
                         - ComputeFuelReserve(ship);
            return Mathf.Max(0, budget);
        }

        /// <summary>Пытается сделать один апгрейд. Возвращает true, если оборудование заменено.</summary>
        public static bool TryUpgrade(ShipData ship, PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (ship == null || planet?.Settlement.EquipmentShop?.Items == null || ctx?.ItemsConfig == null) return false;
            int budget = ComputeUpgradeBudget(ship);
            if (budget <= 0) return false;

            var shipType = RepairService.ResolveShipType(ship);
            var candidateSlots = CollectCategorySlots(ship, shipType);
            if (candidateSlots.Count == 0)
            {
                candidateSlots.Clear();
                _slotBuffer = candidateSlots;
                return false;
            }

            // Предгруппировка магазина по категориям слотов, единожды за визит.
            // Раньше внутренний цикл сканировал ВСЕ shop.Items для каждого слота (slots × shopSize),
            // теперь — только items, влезающие в категорию слота.
            var byCat = GetShopByCategoryBuffer();
            byCat.Clear();
            foreach (var kv in planet.Settlement.EquipmentShop.Items)
            {
                var it = kv.Value;
                if (it == null || !it.IsEquipment) continue;
                if (it.Price > budget) continue;
                if (!byCat.TryGetValue(it.Category ?? "", out var list))
                    byCat[it.Category ?? ""] = list = new List<ItemInstance>(8);
                list.Add(it);
            }

            float minImprove = 1f + NpcBalance.Outfitter_MinImprovement;
            Upgrade best = default;
            bool haveBest = false;

            foreach (var slot in candidateSlots)
            {
                var current = EquipmentSystem.GetEquipped(ship, slot.slotKey);
                float currentScore = ScoreItem(current, shipType);

                if (!byCat.TryGetValue(slot.category ?? "", out var candidatesInCat)) continue;
                for (int ci = 0; ci < candidatesInCat.Count; ci++)
                {
                    var candidate = candidatesInCat[ci];
                    if (!candidate.CanFitInSlotCategory(slot.category)) continue;
                    // Свободного места в трюме должно хватить (после снятия current).
                    int weightDelta = candidate.Weight - (current?.Weight ?? 0);
                    if (weightDelta > 0 && EquipmentSystem.GetFreeSpace(ship) < weightDelta) continue;

                    float candScore = ScoreItem(candidate, shipType);
                    if (candScore < currentScore * minImprove) continue;
                    if (candScore <= currentScore) continue;

                    float delta = candScore - currentScore;
                    if (!haveBest || delta > best.Delta)
                    {
                        best = new Upgrade(slot.slotKey, slot.category, candidate.Uid, currentScore, candScore, delta);
                        haveBest = true;
                    }
                }
            }
            candidateSlots.Clear();
            _slotBuffer = candidateSlots;
            byCat.Clear(); // сохраняем ёмкость, буфер переиспользуется между визитами

            if (!haveBest) return false;

            // Выполняем замену: снимаем текущий, продаём планете, покупаем новый, ставим в слот.
            return PerformUpgrade(ship, planet, ctx, best);
        }

        private static Dictionary<string, List<ItemInstance>> _shopByCategoryBuffer;
        private static Dictionary<string, List<ItemInstance>> GetShopByCategoryBuffer()
            => _shopByCategoryBuffer ??= new Dictionary<string, List<ItemInstance>>();

        // ── internals ────────────────────────────────────────────────────────────

        private readonly struct Upgrade
        {
            public readonly string SlotKey;
            public readonly string Category;
            public readonly string CandidateUid;
            public readonly float FromScore;
            public readonly float ToScore;
            public readonly float Delta;

            public Upgrade(string slotKey, string category, string candidateUid, float fromScore, float toScore, float delta)
            {
                SlotKey = slotKey;
                Category = category;
                CandidateUid = candidateUid;
                FromScore = fromScore;
                ToScore = toScore;
                Delta = delta;
            }
        }

        private static bool PerformUpgrade(
            ShipData ship, PlanetData planet, GalaxyGenerationContext ctx, Upgrade up)
        {
            if (!planet.Settlement.EquipmentShop.Items.TryGetValue(up.CandidateUid, out var candidate)) return false;
            if (ship.Money < candidate.Price) return false;

            // 1. Списать деньги + переместить кандидата из магазина в инвентарь (шаг покупки).
            ship.Money -= candidate.Price;
            planet.Settlement.EquipmentShop.Items.Remove(up.CandidateUid);
            InventoryService.PutItem(ship, candidate);

            // 2. Установить в слот (allowInSpace=true; NPC на планете — космос не мешает).
            //    Install сам снимает предыдущее и кладёт в Inventory.
            var res = EquipmentSystem.Install(ship, up.SlotKey, candidate.Uid, ctx.ItemsConfig, allowInSpace: true);
            if (!res.Success)
            {
                // Откат: возвращаем деньги и кандидата в магазин.
                ship.AllItems.Remove(candidate.Uid);
                ship.Inventory.Remove(candidate.Uid);
                planet.Settlement.EquipmentShop.Items[candidate.Uid] = candidate;
                ship.Money += candidate.Price;
                return false;
            }

            // 3. Продать снятый предмет (если он попал в Inventory через Install).
            //    Определяем «снятый» как единственный НЕ-экипированный ItemInstance в AllItems,
            //    относящийся к той же категории, что и слот. Простая эвристика: пробегаем AllItems,
            //    ищем предмет с той же категорией, который сейчас не привязан ни к одному слоту.
            var removed = FindUnequippedItemOfCategory(ship, up.Category);
            if (removed != null)
                SellItemToShop(ship, planet, removed);

            int turn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "UPGRADE",
                $"planet={EconomicLog.Safe(planet.Name)} slot={up.SlotKey} category={up.Category} " +
                $"score={up.FromScore:F1}->{up.ToScore:F1} paid={candidate.Price} money_after={ship.Money}");
            return true;
        }

        /// <summary>Найти предмет данной категории в AllItems, который не установлен ни в один слот.</summary>
        private static ItemInstance FindUnequippedItemOfCategory(ShipData ship, string category)
        {
            foreach (var kv in ship.AllItems)
            {
                var item = kv.Value;
                if (item == null) continue;
                if (item.Category != category) continue;
                bool equipped = false;
                foreach (var slotUid in ship.Equipment.Slots.Values)
                    if (slotUid == item.Uid) { equipped = true; break; }
                if (!equipped) return item;
            }
            return null;
        }

        /// <summary>Продажа предмета оборудования обратно в магазин планеты (0.5 × Price, как у игрока).</summary>
        public static int SellItemToShop(ShipData ship, PlanetData planet, ItemInstance item)
        {
            if (ship == null || planet?.Settlement.EquipmentShop == null || item == null) return 0;
            int price = Mathf.RoundToInt(item.Price * 0.5f);
            ship.AllItems.Remove(item.Uid);
            ship.Inventory.Remove(item.Uid);
            ship.Money += price;
            planet.Settlement.EquipmentShop.Items[item.Uid] = item;
            return price;
        }

        // Буфер для сбора кандидат-слотов, чтобы не аллоцировать при каждом визите.
        private static List<(string slotKey, string category, int weight)> _slotBuffer = new();

        private static List<(string slotKey, string category, int weight)> CollectCategorySlots(
            ShipData ship, ShipTypeConfig shipType)
        {
            var list = _slotBuffer;
            _slotBuffer = null;
            list ??= new List<(string, string, int)>();

            foreach (var kv in ship.Equipment.Slots)
            {
                string category = EquipmentSystem.SlotCategory(kv.Key);
                int weight = RepairService.GetPriorityWeight(shipType, category);
                if (weight <= 0) continue;
                list.Add((kv.Key, category, weight));
            }
            list.Sort(static (a, b) => b.weight.CompareTo(a.weight));
            return list;
        }
    }
}
