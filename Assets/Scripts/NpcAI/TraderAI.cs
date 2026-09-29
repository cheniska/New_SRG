using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.NpcAI.Actions;
using SRG.Ships.Movement;
using SRG.Ships.Services;

namespace SRG.NpcAI
{
    /// <summary>
    /// Поведение транспортного NPC: поиск выгодного межпланетного рейса и операции
    /// купли-продажи у планет. Состояние хранится в полях ShipData (TraderCargoGoodId,
    /// TraderTargetPlanetUid, ...). См. ActionGoodsTrader для машины состояний движения.
    /// </summary>
    public static class TraderAI
    {
        // Балансные константы вынесены в NpcBalance.cs. IdleWaitTurnsConst оставлен как алиас
        // для backwards-compat (используется ActionGoodsTrader).
        public static int IdleWaitTurnsConst => NpcBalance.IdleWaitTurns;

        // ──────────────────────────────────────────
        // Per-turn кэш reachable-stars — общий для всех трейдеров одного хода
        // ──────────────────────────────────────────

        private static int _cacheTurn = -1;
        private static readonly Dictionary<string, List<(StarData star, int totalFuelCost)>> _reachableCache
            = new Dictionary<string, List<(StarData, int)>>();

        /// <summary>Полный сброс кэша reachable-stars. Вызывается из <see cref="SRG.Core.GalaxyManager.SwitchActiveGalaxy"/>,
        /// чтобы устаревшие StarData refs из прошлой галактики не всплыли при multi-galaxy.</summary>
        public static void ResetReachableCache()
        {
            _reachableCache.Clear();
            _cacheTurn = -1;
        }

        private static List<(StarData star, int totalFuelCost)> GetReachableCached(
            StarData origin, GalaxyData galaxy, int jumpRange, int maxHops)
        {
            int turn = galaxy?.CurrentTurn ?? 0;
            if (turn != _cacheTurn)
            {
                _reachableCache.Clear();
                _cacheTurn = turn;
            }
            string key = origin.Uid + "|" + jumpRange;
            if (_reachableCache.TryGetValue(key, out var cached)) return cached;
            var fresh = CollectReachableStars(origin, galaxy, jumpRange, maxHops);
            _reachableCache[key] = fresh;
            return fresh;
        }

        public struct TradeRoute
        {
            public bool   Valid;
            public string GoodId;
            public int    Amount;
            public int    BuyPrice;     // цена покупки за единицу
            public int    SellPrice;    // цена продажи за единицу
            public int    ProfitTotal;  // прогноз чистой прибыли (после fuel cost обеих ног)
            public string BuyPlanetUid;  // где покупать (может быть текущая или удалённая)
            public string BuyStarUid;
            public string SellPlanetUid; // где продавать
            public string SellStarUid;
            public int    FuelLeg1;     // топливо: текущая → buy
            public int    FuelLeg2;     // топливо: buy → sell
        }

        // ──────────────────────────────────────────
        // Поиск выгодного рейса
        // ──────────────────────────────────────────

        /// <summary>
        /// Рассматривает ВСЕ пары (buy_planet, sell_planet) в пределах NpcBalance.MaxJumpHops × jumpRange
        /// от текущей звезды корабля. Включает возможность лететь сначала в чужую систему за покупкой,
        /// потом в третью на продажу. Учитывает топливо двух ног и наличность корабля.
        /// </summary>
        public static TradeRoute PlanRoute(ShipData ship, StarData currentStar, GalaxyData galaxy, GalaxyConfig cfg)
        {
            var result = new TradeRoute { Valid = false };
            if (ship == null || currentStar == null || galaxy == null || cfg?.Goods == null) return result;

            int freeSpace = Mathf.Max(0, EquipmentSystem.GetFreeSpace(ship));
            if (freeSpace <= 0) return result;

            int money = Mathf.Max(0, ship.Money);
            int jumpRange = Mathf.Max(1, EquipmentSystem.GetJumpRange(ship));

            // Стоимость топлива из текущей звезды до любой досягаемой (вкл. саму себя = 0).
            // Кэш per-turn общий между всеми трейдерами — экономит 90% BFS.
            var fuelFromCurrent = GetReachableCached(currentStar, galaxy, jumpRange, NpcBalance.MaxJumpHops);

            // Все планеты, доступные как буй-точки (включая текущую систему).
            // NpcBalance.MaxJumpHops=1 → fuelToBuy либо 0 (своя система) либо прямой прыжок.
            var candidates = new List<(PlanetData planet, StarData star, int fuelToHere)>();
            foreach (var p in currentStar.Planets)
                if (IsViableTradePlanet(p)) candidates.Add((p, currentStar, 0));
            foreach (var (star, fuelCost) in fuelFromCurrent)
                foreach (var p in star.Planets)
                    if (IsViableTradePlanet(p)) candidates.Add((p, star, fuelCost));

            // Эвристический прунинг: считаем максимальную SellPrice для каждого товара
            // среди ВСЕХ кандидатов и отбираем NpcBalance.CandidatePruneLimit лучших (good, sellPlanet).
            // Глубокий поиск ведём только по этим парам — экономит ~80% итераций.
            var topSells = new List<(string goodId, PlanetData sellPlanet, StarData sellStar, int sellPrice)>();
            foreach (var (sellPlanet, sellStar, _) in candidates)
            {
                if (sellPlanet.Settlement.Shop?.Goods == null) continue;
                foreach (var entry in sellPlanet.Settlement.Shop.Goods)
                {
                    if (entry.Value == null) continue;
                    int sp = entry.Value.SellPrice;
                    if (sp <= 0) continue;
                    topSells.Add((entry.Key, sellPlanet, sellStar, sp));
                }
            }
            topSells.Sort((a, b) => b.sellPrice.CompareTo(a.sellPrice));
            if (topSells.Count > NpcBalance.CandidatePruneLimit) topSells.RemoveRange(NpcBalance.CandidatePruneLimit, topSells.Count - NpcBalance.CandidatePruneLimit);

            int bestProfit = NpcBalance.MinProfitPerUnit * 4;
            TradeRoute best = result;

            // По топ-кандидатам ищем лучшую buy-планету. При NpcBalance.MaxJumpHops=1 fuelLeg2 — прямой
            // прыжок (или 0, если та же звезда), без вложенного BFS.
            foreach (var (goodId, sellPlanet, sellStar, sellPrice) in topSells)
            {
                foreach (var (buyPlanet, buyStar, fuelToBuy) in candidates)
                {
                    if (buyPlanet == sellPlanet) continue;
                    if (buyPlanet.Settlement.Shop?.Goods == null) continue;
                    if (!buyPlanet.Settlement.Shop.Goods.TryGetValue(goodId, out var buyEntry) || buyEntry == null) continue;

                    int buyPrice = buyEntry.BuyPrice;
                    if (buyPrice <= 0 || sellPrice <= buyPrice + NpcBalance.MinProfitPerUnit) continue;
                    if (buyEntry.Stock <= 0) continue;

                    int affordable = money / buyPrice;
                    int amount = Mathf.Min(buyEntry.Stock, Mathf.Min(freeSpace, affordable));
                    if (amount <= 0) continue;

                    int fuelToSell = DirectFuelCost(buyStar, sellStar, jumpRange);
                    if (fuelToSell < 0) continue;

                    int perUnit = sellPrice - buyPrice;
                    int net = perUnit * amount - fuelToBuy - fuelToSell;
                    if (net <= bestProfit) continue;

                    bestProfit = net;
                    best = new TradeRoute
                    {
                        Valid = true,
                        GoodId = goodId,
                        Amount = amount,
                        BuyPrice = buyPrice,
                        SellPrice = sellPrice,
                        ProfitTotal = net,
                        BuyPlanetUid = buyPlanet.Uid,
                        BuyStarUid = buyStar.Uid,
                        SellPlanetUid = sellPlanet.Uid,
                        SellStarUid = sellStar.Uid,
                        FuelLeg1 = fuelToBuy,
                        FuelLeg2 = fuelToSell
                    };
                }
            }

            return best;
        }

        /// <summary>Цена прямого прыжка a→b, либо -1 если a/b в разных системах не в пределах jumpRange.</summary>
        private static int DirectFuelCost(StarData a, StarData b, int jumpRange)
        {
            if (a == b) return 0;
            float dist = HyperjumpController.CalcDistance(a, b);
            if (dist > jumpRange) return -1;
            return HyperjumpController.CalcFuelCost(a, b);
        }

        private static bool IsViableTradePlanet(PlanetData p)
        {
            if (p == null) return false;
            if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) return false;
            if (p.Settlement.Population <= 0) return false;
            return p.Settlement.Shop?.Goods != null;
        }

        /// <summary>
        /// BFS по галактике: возвращает все звёзды (кроме исходной) в пределах maxHops прыжков,
        /// где каждый прыжок ≤ jumpRange. Для каждой найденной звезды отдаётся приблизительная
        /// суммарная стоимость топлива (для пары соседних = расстояние × CostPerUnit).
        /// </summary>
        private static List<(StarData star, int totalFuelCost)> CollectReachableStars(
            StarData origin, GalaxyData galaxy, int jumpRange, int maxHops)
        {
            var result = new List<(StarData, int)>();
            if (origin == null || galaxy == null || maxHops <= 0) return result;

            var bestCost = new Dictionary<string, int> { [origin.Uid] = 0 };
            var frontier = new Queue<(StarData node, int hops)>();
            frontier.Enqueue((origin, 0));

            while (frontier.Count > 0)
            {
                var (node, hops) = frontier.Dequeue();
                if (hops >= maxHops) continue;
                int nodeCost = bestCost[node.Uid];

                foreach (var other in galaxy.StarsMap.Values)
                {
                    if (other == node) continue;
                    float dist = HyperjumpController.CalcDistance(node, other);
                    if (dist > jumpRange) continue;

                    int hopCost = HyperjumpController.CalcFuelCost(node, other);
                    int totalCost = nodeCost + hopCost;

                    if (bestCost.TryGetValue(other.Uid, out var prev) && prev <= totalCost) continue;
                    bestCost[other.Uid] = totalCost;
                    frontier.Enqueue((other, hops + 1));
                }
            }

            foreach (var kv in bestCost)
            {
                if (kv.Key == origin.Uid) continue;
                if (galaxy.StarsMap.TryGetValue(kv.Key, out var s))
                    result.Add((s, kv.Value));
            }
            return result;
        }

        // ──────────────────────────────────────────
        // Покупка / продажа
        // ──────────────────────────────────────────

        /// <summary>
        /// Списывает товар со стока планеты, кладёт в инвентарь корабля. Деньги корабля уменьшаются;
        /// планетная казна считается бездонной (см. диздок).
        /// </summary>
        public static bool BuyCargo(ShipData ship, PlanetData planet, string goodId, int amount, GalaxyConfig cfg)
        {
            if (amount <= 0) return false;
            if (planet?.Settlement.Shop?.Goods == null) return false;
            if (!planet.Settlement.Shop.Goods.TryGetValue(goodId, out var entry) || entry == null) return false;

            int free = Mathf.Max(0, EquipmentSystem.GetFreeSpace(ship));
            int affordable = entry.BuyPrice > 0 ? Mathf.Max(0, ship.Money) / entry.BuyPrice : 0;
            int take = Mathf.Min(amount, Mathf.Min(entry.Stock, Mathf.Min(free, affordable)));
            if (take <= 0) return false;

            int unit = entry.BuyPrice;
            int totalCost = unit * take;

            entry.Stock = Mathf.Max(0, entry.Stock - take);
            ship.Money -= totalCost;
            ship.TraderCargoGoodId = goodId;
            ship.TraderCargoWeight += take;
            ship.TraderBuyPrice = unit;

            ship.Inventory.AddStack(ItemGrantService.BuildStack(
                goodId, SRG.Ships.CargoUtils.CargoCategory,
                TradeSystem.GetDisplayName(goodId, cfg), unit, take, isGoods: true));

            TradeSystem.RecalculatePrices(planet, cfg);

            int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "BUY",
                $"planet={EconomicLog.Safe(planet.Name)} good={goodId} amount={take} unit_price={unit} " +
                $"total={totalCost} money_after={ship.Money} planet_stock_after={entry.Stock}");
            return true;
        }

        /// <summary>
        /// Продаёт всё, что трюм везёт по этому goodId. Сток планеты растёт, корабль получает деньги.
        /// </summary>
        public static int SellCargo(ShipData ship, PlanetData planet, GalaxyConfig cfg)
        {
            if (string.IsNullOrEmpty(ship.TraderCargoGoodId)) return 0;
            if (planet?.Settlement.Shop?.Goods == null) return 0;

            string goodId = ship.TraderCargoGoodId;
            if (!planet.Settlement.Shop.Goods.TryGetValue(goodId, out var entry) || entry == null) return 0;

            int weight = ship.TraderCargoWeight;
            if (weight <= 0) return 0;

            int buyUnit = ship.TraderBuyPrice;
            int sellUnit = entry.SellPrice;
            int payout = sellUnit * weight;
            int profit = (sellUnit - buyUnit) * weight;

            entry.Stock += weight;
            ship.Money += payout;
            ship.Inventory.TakeStack(goodId, weight);
            // Учёт совокупного трейдерского профита — метрика TradeProfit для рейтингов (ShipRatingService).
            if (profit > 0) ship.TradeProfit += profit;

            ship.TraderCargoGoodId = null;
            ship.TraderCargoWeight = 0;
            ship.TraderBuyPrice = 0;

            TradeSystem.RecalculatePrices(planet, cfg);

            int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            EconomicLog.Trade(turn, EconomicLog.Safe(ship.Name), "SELL",
                $"planet={EconomicLog.Safe(planet.Name)} good={goodId} amount={weight} " +
                $"unit_price={sellUnit} buy_was={buyUnit} payout={payout} profit_gross={profit} " +
                $"money_after={ship.Money} planet_stock_after={entry.Stock}");
            return payout;
        }
    }
}
