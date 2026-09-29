using UnityEngine;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;

namespace SRG.Economy
{
    /// <summary>
    /// Расчёт цен, легальности и инициализация магазинов товаров на планетах.
    /// Все формулы и коэффициенты берутся из GalaxyConfig (секции Goods, Trade, Races, Planets.EconomyTypes, Planets.GovernmentTypes, Events).
    /// </summary>
    public static class TradeSystem
    {
        // ──────────────────────────────────────────
        // Throttle: тикаем экономику не каждый ход (раньше каждый день), а пачкой.
        // Дельта производства умножается на TickStrideTurns, чтобы суммарный темп
        // экономики совпадал с прежним.
        // ──────────────────────────────────────────
        public const int TickStrideTurns = 3;

        // ──────────────────────────────────────────
        // Публичное API
        // ──────────────────────────────────────────

        public static string GetDisplayName(string goodId, GalaxyConfig cfg)
        {
            if (cfg?.Goods != null && cfg.Goods.TryGetValue(goodId, out var g))
                return g.DisplayName ?? goodId;
            return goodId;
        }

        /// <summary>Цена покупки (игрок платит носителю). Пересчитывается по текущему стоку.</summary>
        public static int CalculateBuyPrice(ILandingSite site, string goodId, int currentStock, GalaxyConfig cfg)
        {
            float price = ComputeBasePrice(site, goodId, currentStock, cfg, InflationSystem.GetFactor(GalaxyManager.Instance?.GeneratedGalaxy));
            if (!IsLegal(site, goodId, cfg))
                price *= cfg.Trade?.ContrabandPriceMultiplier ?? 3f;
            return Mathf.Max(1, Mathf.RoundToInt(price));
        }

        /// <summary>Цена продажи (носитель платит игроку). ~70% от цены покупки.</summary>
        public static int CalculateSellPrice(ILandingSite site, string goodId, int currentStock, GalaxyConfig cfg)
        {
            float price = ComputeBasePrice(site, goodId, currentStock, cfg, InflationSystem.GetFactor(GalaxyManager.Instance?.GeneratedGalaxy)) * 0.7f;
            if (!IsLegal(site, goodId, cfg))
                price *= cfg.Trade?.ContrabandPriceMultiplier ?? 3f;
            return Mathf.Max(1, Mathf.RoundToInt(price));
        }

        /// <summary>
        /// Товар легален на посадочной цели для данной расы и правительства.
        /// </summary>
        public static bool IsLegal(ILandingSite site, string goodId, GalaxyConfig cfg)
        {
            if (cfg?.Races == null || string.IsNullOrEmpty(site.Race)) return true;
            if (!cfg.Races.TryGetValue(site.Race, out var raceCfg)) return true;

            var bannedByGov = raceCfg.Trade?.BannedGoodsByGovernment;
            if (bannedByGov == null || string.IsNullOrEmpty(site.Settlement?.Government)) return true;

            if (!bannedByGov.TryGetValue(site.Settlement.Government, out var bannedList)) return true;
            return bannedList == null || !bannedList.Contains(goodId);
        }

        /// <summary>
        /// Базовый объём товара по размеру и экономике. Кэшируется в Settlement.BaseStockCache.
        /// Размер берётся только у планет (<c>PlanetData.Size</c>) — станции/носители получают дефолт 200.
        /// </summary>
        public static int GetBaseStock(ILandingSite site, string goodId, GalaxyConfig cfg)
        {
            var settlement = site.Settlement;
            if (settlement.BaseStockCache != null && settlement.BaseStockCache.TryGetValue(goodId, out var cached))
                return cached;

            int baseStock = 200;
            string size = (site as PlanetData)?.Size;
            if (cfg?.Trade?.BaseStockBySize != null && !string.IsNullOrEmpty(size) &&
                cfg.Trade.BaseStockBySize.TryGetValue(size, out var s))
                baseStock = s;

            float mod = 1f;
            if (!string.IsNullOrEmpty(settlement.EconomyType) && cfg?.Planets?.EconomyTypes != null &&
                cfg.Planets.EconomyTypes.TryGetValue(settlement.EconomyType, out var econCfg) &&
                econCfg.Trade?.GoodStockModifiers != null &&
                econCfg.Trade.GoodStockModifiers.TryGetValue(goodId, out var sm))
                mod = sm;

            int result = Mathf.Max(1, Mathf.RoundToInt(baseStock * mod));
            settlement.BaseStockCache ??= new Dictionary<string, int>();
            settlement.BaseStockCache[goodId] = result;
            return result;
        }

        /// <summary>
        /// Инициализирует магазин товаров посадочной цели. Название сохранено (InitPlanetShop)
        /// для совместимости с callers; работает и для станций (через ILandingSite).
        /// </summary>
        public static void InitPlanetShop(ILandingSite site, GalaxyConfig cfg)
        {
            if (cfg?.Goods == null || site?.Settlement == null) return;
            site.Settlement.Shop.Goods.Clear();

            foreach (var kv in cfg.Goods)
            {
                string goodId = kv.Key;
                int baseStock = GetBaseStock(site, goodId, cfg);
                int stock = Random.Range(
                    Mathf.RoundToInt(baseStock * 0.5f),
                    Mathf.RoundToInt(baseStock * 1.5f) + 1);

                site.Settlement.Shop.Goods[goodId] = new ShopGoodEntry
                {
                    Stock = stock,
                    BuyPrice  = CalculateBuyPrice(site, goodId, stock, cfg),
                    SellPrice = CalculateSellPrice(site, goodId, stock, cfg)
                };
            }
        }

        /// <summary>
        /// Пересчитывает цены по текущему стоку. Вызывать при открытии магазина и после каждой сделки.
        /// </summary>
        public static void RecalculatePrices(ILandingSite site, GalaxyConfig cfg)
        {
            if (cfg?.Goods == null || site?.Settlement?.Shop?.Goods == null) return;
            float inflation = InflationSystem.GetFactor(GalaxyManager.Instance?.GeneratedGalaxy);
            float contrabandMul = cfg.Trade?.ContrabandPriceMultiplier ?? 3f;
            foreach (var kv in site.Settlement.Shop.Goods)
            {
                float basePrice = ComputeBasePrice(site, kv.Key, kv.Value.Stock, cfg, inflation);
                bool legal = IsLegal(site, kv.Key, cfg);
                float buy = legal ? basePrice : basePrice * contrabandMul;
                float sell = legal ? basePrice * 0.7f : basePrice * 0.7f * contrabandMul;
                kv.Value.BuyPrice  = Mathf.Max(1, Mathf.RoundToInt(buy));
                kv.Value.SellPrice = Mathf.Max(1, Mathf.RoundToInt(sell));
            }
        }

        // ──────────────────────────────────────────
        // Ежедневная динамика — производство/потребление + пересчёт цен
        // ──────────────────────────────────────────

        /// <summary>
        /// Тикает торговлю на всех обитаемых планетах. Вызывается из GalaxyData.GalaxyNextDay,
        /// но реально работает только раз в TickStrideTurns ходов (delta умножается на stride).
        /// </summary>
        public static void TickAll(GalaxyData galaxy, GalaxyConfig cfg)
        {
            if (galaxy == null || cfg?.Goods == null) return;
            if (galaxy.CurrentTurn % TickStrideTurns != 0) return;

            foreach (var star in galaxy.StarsMap.Values)
                foreach (var planet in star.Planets)
                    if (IsInhabited(planet))
                        TickDaily(planet, cfg);
        }

        /// <summary>
        /// Один тик торговой динамики для планеты:
        ///   1) для каждого товара применить ProductionDelta (Economy + Race + Events) × Gov × PopScale;
        ///   2) сток зажать в [0, BaseStock × MaxStockMultiplier];
        ///   3) пересчитать цены.
        /// </summary>
        public static void TickDaily(PlanetData planet, GalaxyConfig cfg)
        {
            if (planet?.Settlement.Shop?.Goods == null || cfg?.Goods == null) return;

            float popScale = ComputePopulationScale(planet.Settlement.Population);
            float maxMul = cfg.Trade?.MaxStockMultiplier ?? 5f;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            int turn = galaxy?.CurrentTurn ?? 0;
            var research = galaxy?.ResearchState;
            // Тик идёт раз в TickStrideTurns ходов → дельта производства умножается на stride,
            // чтобы средняя скорость насыщения/истощения не зависела от частоты тика.
            float strideScale = TickStrideTurns * popScale;

            // Подхватываем товары, добавленные изобретениями уже после инициализации магазина.
            if (research?.AddedGoods != null && research.AddedGoods.Count > 0)
                EnsureAddedGoodsInShop(planet, cfg, research);

            foreach (var kv in cfg.Goods)
            {
                string goodId = kv.Key;
                if (!planet.Settlement.Shop.Goods.TryGetValue(goodId, out var entry))
                {
                    continue;
                }

                float delta = ComputeProductionDelta(planet, goodId, cfg) * strideScale;
                // Снятые с производства товары не пополняются — постепенно вымываются.
                if (research != null && research.IsGoodRemoved(goodId) && delta > 0f) delta = 0f;
                if (Mathf.Approximately(delta, 0f)) continue;

                int baseStock = GetBaseStock(planet, goodId, cfg);
                int cap = Mathf.Max(baseStock, Mathf.RoundToInt(baseStock * maxMul));
                int oldStock = entry.Stock;
                int newStock = Mathf.Clamp(oldStock + Mathf.RoundToInt(delta), 0, cap);
                entry.Stock = newStock;

                // Гейтим форматирование под флаг — иначе аллоцируем строку на каждом товаре
                // ради записи, которую Market() сразу выбросит.
                if (EconomicLog.LogMarket)
                    EconomicLog.Market(turn, EconomicLog.Safe(planet.Name), "STOCK_DELTA",
                        $"good={goodId} delta_calc={delta:F2} stock_before={oldStock} stock_after={newStock} cap={cap}");
            }

            RecalculatePrices(planet, cfg);
            PushPriceHistory(planet);
        }

        /// <summary>Количество хранимых семплов истории цен. Каждый семпл пишется раз в TickStrideTurns ходов
        /// → 10 семплов = ~30 ходов истории. Триггеры PriceAbove/Below ForDays конвертируются в семплы делением на stride.</summary>
        public const int PriceHistoryMaxSamples = 10;

        /// <summary>Когда наука «открывает» новый товар (UnlockEffects.AddGood), уже сгенерированные
        /// магазины ничего о нём не знают — добавляем запись с базовым стоком при первом тике.</summary>
        private static void EnsureAddedGoodsInShop(PlanetData planet, GalaxyConfig cfg, GalacticResearchState research)
        {
            foreach (var kv in research.AddedGoods)
            {
                string goodId = kv.Key;
                if (planet.Settlement.Shop.Goods.ContainsKey(goodId)) continue;
                int baseStock = GetBaseStock(planet, goodId, cfg);
                int stock = UnityEngine.Random.Range(Mathf.RoundToInt(baseStock * 0.5f), Mathf.RoundToInt(baseStock * 1.5f) + 1);
                planet.Settlement.Shop.Goods[goodId] = new ShopGoodEntry
                {
                    Stock = stock,
                    BuyPrice  = CalculateBuyPrice(planet, goodId, stock, cfg),
                    SellPrice = CalculateSellPrice(planet, goodId, stock, cfg)
                };
            }
        }

        /// <summary>Пушит текущий BuyPrice по всем товарам в скользящее окно семплов.</summary>
        private static void PushPriceHistory(PlanetData planet)
        {
            if (planet?.Settlement.Shop?.Goods == null) return;
            if (planet.Settlement.PriceHistory == null) planet.Settlement.PriceHistory = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            foreach (var kv in planet.Settlement.Shop.Goods)
            {
                if (!planet.Settlement.PriceHistory.TryGetValue(kv.Key, out var list))
                {
                    list = new System.Collections.Generic.List<int>(PriceHistoryMaxSamples);
                    planet.Settlement.PriceHistory[kv.Key] = list;
                }
                list.Add(kv.Value.BuyPrice);
                if (list.Count > PriceHistoryMaxSamples)
                    list.RemoveAt(0);
            }
        }

        /// <summary>
        /// Считает суммарный ProductionDelta для пары (planet, good) с учётом Economy + Race + активных
        /// событий (аддитивно), затем умножает на GovernmentMultiplier.
        /// </summary>
        private static float ComputeProductionDelta(PlanetData planet, string goodId, GalaxyConfig cfg)
        {
            float delta = 0f;

            // Базовый поток от экономики планеты.
            if (!string.IsNullOrEmpty(planet.Settlement.EconomyType) && cfg.Planets?.EconomyTypes != null &&
                cfg.Planets.EconomyTypes.TryGetValue(planet.Settlement.EconomyType, out var econCfg) &&
                econCfg.Trade?.GoodProductionDelta != null &&
                econCfg.Trade.GoodProductionDelta.TryGetValue(goodId, out var econDelta))
                delta += econDelta;

            // Модификатор расы (аддитивный).
            if (!string.IsNullOrEmpty(planet.Race) && cfg.Races != null &&
                cfg.Races.TryGetValue(planet.Race, out var raceCfg) &&
                raceCfg.Trade?.GoodProductionDelta != null &&
                raceCfg.Trade.GoodProductionDelta.TryGetValue(goodId, out var raceDelta))
                delta += raceDelta;

            // Активные события (аддитивно).
            if (planet.Settlement.ActiveEvents != null && cfg.Events != null)
            {
                foreach (var active in planet.Settlement.ActiveEvents)
                {
                    if (!cfg.Events.TryGetValue(active.EventId, out var evtCfg)) continue;
                    if (evtCfg.Trade?.GoodStockDelta == null) continue;
                    if (evtCfg.Trade.GoodStockDelta.TryGetValue(goodId, out var evDelta))
                        delta += evDelta;
                }
            }

            // Множитель правительства (мультипликативный).
            if (!string.IsNullOrEmpty(planet.Settlement.Government) && cfg.Planets?.GovernmentTypes != null &&
                cfg.Planets.GovernmentTypes.TryGetValue(planet.Settlement.Government, out var govCfg) &&
                govCfg.GoodProductionMultiplier != null &&
                govCfg.GoodProductionMultiplier.TryGetValue(goodId, out var govMul))
                delta *= govMul;

            return delta;
        }

        /// <summary>
        /// Корневое масштабирование по населению (Population в условных единицах: ~1000 = норма).
        /// 100→0.32, 1000→1.0, 5000→2.24 (clamped 0.1..3.0). Даёт мегаполисам преимущество,
        /// но без линейной доминации, и не зануляет малые планеты.
        /// </summary>
        private static float ComputePopulationScale(int population)
        {
            if (population <= 0) return 0f;
            return Mathf.Clamp(Mathf.Sqrt(population / 1000f), 0.1f, 3f);
        }

        private static bool IsInhabited(PlanetData planet)
        {
            if (planet == null) return false;
            if (string.IsNullOrEmpty(planet.Race)) return false;
            if (string.Equals(planet.Race, GalaxyConstants.RACE_NONE_KEY, System.StringComparison.OrdinalIgnoreCase)) return false;
            return planet.Settlement.Population > 0;
        }

        // ──────────────────────────────────────────
        // Формулы
        // ──────────────────────────────────────────

        private static float ComputeBasePrice(ILandingSite site, string goodId, int currentStock, GalaxyConfig cfg, float inflationFactor)
        {
            if (cfg?.Goods == null || !cfg.Goods.TryGetValue(goodId, out var goodCfg)) return 10f;
            var trade = cfg.Trade;
            var settlement = site.Settlement;

            float price = goodCfg.BasePrice;

            // Коэффициент расы: общий PriceCoef + потоварный
            if (!string.IsNullOrEmpty(site.Race) && cfg.Races != null &&
                cfg.Races.TryGetValue(site.Race, out var raceCfg))
            {
                price *= raceCfg.PriceCoef;
                if (raceCfg.Trade?.GoodPriceModifiers != null &&
                    raceCfg.Trade.GoodPriceModifiers.TryGetValue(goodId, out var rm))
                    price *= rm;
            }

            // Коэффициент экономики
            if (settlement != null && !string.IsNullOrEmpty(settlement.EconomyType) && cfg.Planets?.EconomyTypes != null &&
                cfg.Planets.EconomyTypes.TryGetValue(settlement.EconomyType, out var econCfg) &&
                econCfg.Trade?.GoodPriceModifiers != null &&
                econCfg.Trade.GoodPriceModifiers.TryGetValue(goodId, out var em))
                price *= em;

            // Коэффициент правительства
            if (settlement != null && !string.IsNullOrEmpty(settlement.Government) && cfg.Planets?.GovernmentTypes != null &&
                cfg.Planets.GovernmentTypes.TryGetValue(settlement.Government, out var govCfg) &&
                govCfg.GoodPriceModifiers != null &&
                govCfg.GoodPriceModifiers.TryGetValue(goodId, out var gm))
                price *= gm;

            // Коэффициенты активных событий (перемножаются)
            if (settlement?.ActiveEvents != null && cfg.Events != null)
            {
                foreach (var activeEvt in settlement.ActiveEvents)
                {
                    if (cfg.Events.TryGetValue(activeEvt.EventId, out var evtCfg) &&
                        evtCfg.Trade?.GoodPriceModifiers != null &&
                        evtCfg.Trade.GoodPriceModifiers.TryGetValue(goodId, out var evm))
                        price *= evm;
                }
            }

            // Гиперболический StockModifier
            int baseStock = GetBaseStock(site, goodId, cfg);
            price *= HyperbolicStockModifier(
                currentStock, baseStock,
                trade?.MinPriceMultiplier ?? 0.4f,
                trade?.MaxPriceMultiplier ?? 3f);

            // Clamp к базовой цене × [Min, Max] (до контрабандного множителя)
            float minP = goodCfg.BasePrice * (trade?.MinPriceMultiplier ?? 0.4f);
            float maxP = goodCfg.BasePrice * (trade?.MaxPriceMultiplier ?? 3f);
            float clamped = Mathf.Clamp(price, minP, maxP);

            // Глобальный множитель цены из научных эффектов (UnlockEffects.ModifyGoodPrice).
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy?.ResearchState != null)
                clamped *= galaxy.ResearchState.GetGoodPriceMultiplier(goodId);

            // Финальный множитель — глобальная инфляция галактики (передана извне).
            return clamped * inflationFactor;
        }

        /// <summary>
        /// Гиперболическая зависимость цены от стока:
        /// stock=0 → maxMult, stock=baseStock → 1.0, stock→∞ → minMult
        /// </summary>
        private static float HyperbolicStockModifier(int current, int baseStock, float minMult, float maxMult)
        {
            if (baseStock <= 0) return 1f;
            if (current <= 0) return maxMult;
            // ratio = current / baseStock
            // t = ratio / (1 + ratio)  → [0..1) при ratio → [0..∞)
            float ratio = (float)current / baseStock;
            float t = ratio / (1f + ratio);
            return Mathf.Lerp(maxMult, minMult, t);
        }
    }
}
