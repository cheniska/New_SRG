using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;

namespace SRG.Science
{
    /// <summary>
    /// Симуляция научного развития галактики (диздок: docs/planetary_science_system.md).
    ///
    /// Каждый ход обитаемые планеты копят поинты по 7 категориям и социальный капитал;
    /// раз в DistributeStrideTurns ходов поинты распределяются по активным изобретениям,
    /// проверяются завершения, активируются новые узлы по prerequisites.
    ///
    /// Эффекты завершённых изобретений применяются через <see cref="UnlockEffectsService"/>
    /// и оседают в <see cref="GalacticResearchState"/> (цены, шансы событий, доступность
    /// шаблонов/товаров) — оттуда их читают TradeSystem/EquipmentShopSystem/PlanetaryEventSystem.
    /// </summary>
    public static class ScienceSystem
    {
        public static void TickAll(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            var sci = ctx?.Config?.Science;
            if (galaxy == null || sci == null || sci.Categories == null || sci.Categories.Count == 0) return;

            var state = galaxy.ResearchState ??= new GalacticResearchState();
            EnsureActivated(galaxy, sci, state);

            // 1) Производство поинтов и рост социального капитала.
            foreach (var star in galaxy.StarsMap.Values)
                foreach (var planet in star.Planets)
                    if (IsInhabited(planet))
                        ProducePoints(planet, sci, ctx.Config);

            // 2) Раз в N ходов — распределить накопленное по активным узлам и проверить завершения.
            int stride = Mathf.Max(1, sci.DistributeStrideTurns);
            if (galaxy.CurrentTurn % stride == 0)
            {
                Distribute(galaxy, ctx, sci, state);
                CheckCompletions(galaxy, ctx, sci, state);
                EnsureActivated(galaxy, sci, state);
            }
        }

        // ──────────────────────────────────────────
        // Производство поинтов
        // ──────────────────────────────────────────

        private static void ProducePoints(PlanetData planet, ScienceConfig sci, GalaxyConfig cfg)
        {
            planet.Settlement.ResearchPoints ??= new Dictionary<string, float>();
            // Социальный капитал растёт пассивно (бьётся в EventsSystem при катастрофах).
            planet.Settlement.SocialCapital = Mathf.Clamp(planet.Settlement.SocialCapital + sci.MoodGrowthPerTurn, 0f, sci.MoodCap);

            float popFactor = ComputePopFactor(planet.Settlement.Population, sci);
            if (popFactor <= 0f) return;

            // Инлайн-биасы берём прямо из существующих секций конфига, чтобы не дублировать
            // ключи рас/экономик/правительств. TechGrowthCoef (общий скаляр) применяется тоже —
            // это сохраняет смысл старого PTU-таймера: «технологичная раса учится быстрее».
            RaceConfig raceCfg = null;
            if (!string.IsNullOrEmpty(planet.Race) && cfg.Races != null)
                cfg.Races.TryGetValue(planet.Race, out raceCfg);

            EconomyTypeConfig econCfg = null;
            if (!string.IsNullOrEmpty(planet.Settlement.EconomyType) && cfg.Planets?.EconomyTypes != null)
                cfg.Planets.EconomyTypes.TryGetValue(planet.Settlement.EconomyType, out econCfg);

            GovernmentTypeConfig govCfg = null;
            if (!string.IsNullOrEmpty(planet.Settlement.Government) && cfg.Planets?.GovernmentTypes != null)
                cfg.Planets.GovernmentTypes.TryGetValue(planet.Settlement.Government, out govCfg);

            float raceMul = (raceCfg?.TechGrowthCoef ?? 1f);
            float econMul = (econCfg?.TechGrowthCoef ?? 1f);
            float govMul  = (govCfg?.TechGrowthCoef ?? 1f);
            float ptuMul  = 1f + sci.TechLevelBonusPerStep * Mathf.Max(0, planet.Settlement.TechLevel - 1);

            foreach (var category in sci.Categories)
            {
                float rate = sci.BasePerCapita
                           * popFactor
                           * raceMul * ScienceConfig.GetCategoryBias(raceCfg?.ScienceBias, category)
                           * econMul * ScienceConfig.GetCategoryBias(econCfg?.ScienceBias, category)
                           * govMul  * ScienceConfig.GetCategoryBias(govCfg?.ScienceBias,  category)
                           * ptuMul
                           * EventMultiplier(planet, cfg, category);
                if (rate <= 0f) continue;

                planet.Settlement.ResearchPoints.TryGetValue(category, out var current);
                planet.Settlement.ResearchPoints[category] = current + rate;
            }
        }

        private static float EventMultiplier(PlanetData planet, GalaxyConfig cfg, string category)
        {
            if (planet.Settlement.ActiveEvents == null || planet.Settlement.ActiveEvents.Count == 0) return 1f;
            if (cfg?.Events == null) return 1f;
            float mul = 1f;
            for (int i = 0; i < planet.Settlement.ActiveEvents.Count; i++)
            {
                string id = planet.Settlement.ActiveEvents[i].EventId;
                if (id == null) continue;
                if (!cfg.Events.TryGetValue(id, out var evtCfg) || evtCfg.Science == null) continue;
                if (evtCfg.Science.TryGetValue(category, out var m)) mul *= m;
            }
            return mul;
        }

        /// <summary>
        /// Население даёт корневой фактор: sqrt(pop / PopFactorPivot), зажат [PopFactorMin..PopFactorMax].
        /// При дефолтах: 100→0.32, 1000→1.0, 10000→3.16.
        /// </summary>
        private static float ComputePopFactor(int population, ScienceConfig sci)
        {
            if (population <= 0) return 0f;
            float pivot = sci.PopFactorPivot > 0f ? sci.PopFactorPivot : 1000f;
            return Mathf.Clamp(Mathf.Sqrt(population / pivot), sci.PopFactorMin, sci.PopFactorMax);
        }

        // ──────────────────────────────────────────
        // Активация узлов (по prerequisites)
        // ──────────────────────────────────────────

        private static void EnsureActivated(GalaxyData galaxy, ScienceConfig sci, GalacticResearchState state)
        {
            if (sci.Inventions == null) return;
            foreach (var kv in sci.Inventions)
            {
                string id = kv.Key;
                if (state.Completed.Contains(id) || state.Active.ContainsKey(id)) continue;
                if (!PrerequisitesMet(kv.Value, state)) continue;
                state.Active[id] = new InventionProgress();
            }
        }

        private static bool PrerequisitesMet(InventionNodeConfig inv, GalacticResearchState state)
        {
            if (inv?.Prerequisites == null || inv.Prerequisites.Count == 0) return true;
            for (int i = 0; i < inv.Prerequisites.Count; i++)
                if (!state.Completed.Contains(inv.Prerequisites[i])) return false;
            return true;
        }

        // ──────────────────────────────────────────
        // Распределение поинтов по активным узлам
        // ──────────────────────────────────────────

        // Переиспользуем буферы между планетами, чтобы не аллоцировать на каждом тике.
        private static readonly List<string> _candidatesBuf = new();
        private static readonly List<string> _planetCategoriesBuf = new();

        private static void Distribute(GalaxyData galaxy, GalaxyGenerationContext ctx, ScienceConfig sci, GalacticResearchState state)
        {
            if (state.Active.Count == 0) return;

            foreach (var star in galaxy.StarsMap.Values)
                foreach (var planet in star.Planets)
                {
                    if (!IsInhabited(planet)) continue;
                    if (planet.Settlement.ResearchPoints == null || planet.Settlement.ResearchPoints.Count == 0) continue;

                    // Снимаем снимок ключей — мы будем модифицировать словарь в цикле.
                    _planetCategoriesBuf.Clear();
                    _planetCategoriesBuf.AddRange(planet.Settlement.ResearchPoints.Keys);

                    foreach (var category in _planetCategoriesBuf)
                    {
                        float points = planet.Settlement.ResearchPoints[category];
                        if (points <= 0f) continue;

                        BuildCandidates(planet, category, sci, state, _candidatesBuf);
                        if (_candidatesBuf.Count == 0) continue;

                        float share = points / _candidatesBuf.Count;
                        foreach (var invId in _candidatesBuf)
                        {
                            var invCfg = sci.Inventions[invId];
                            var progress = state.Active[invId];
                            progress.Spent.TryGetValue(category, out var alreadySpent);
                            invCfg.Cost.TryGetValue(category, out var needed);
                            float remaining = needed - alreadySpent;
                            if (remaining <= 0f) continue;

                            float applied = Mathf.Min(share, remaining);
                            progress.Spent[category] = alreadySpent + applied;

                            progress.Contributors.TryGetValue(planet.Uid, out var cContrib);
                            progress.Contributors[planet.Uid] = cContrib + applied;

                            planet.Settlement.ResearchPoints[category] -= applied;

                            // Социальный капитал тратится пропорционально тиру изобретения.
                            planet.Settlement.SocialCapital = Mathf.Max(0f,
                                planet.Settlement.SocialCapital - sci.MoodSpendPerTier * invCfg.Tier * (applied / Mathf.Max(1f, needed)));
                        }
                    }
                }

            if (sci.LogDistribute)
                EconomicLog.Custom(galaxy.CurrentTurn, "SCIENCE", "Galaxy", "DISTRIBUTE",
                    $"active={state.Active.Count} completed={state.Completed.Count}");
        }

        private static void BuildCandidates(PlanetData planet, string category, ScienceConfig sci, GalacticResearchState state, List<string> result)
        {
            result.Clear();
            foreach (var kv in state.Active)
            {
                string invId = kv.Key;
                var invCfg = sci.Inventions[invId];
                if (invCfg.Cost == null || !invCfg.Cost.ContainsKey(category)) continue;
                if (invCfg.Tier > planet.Settlement.TechLevel + 1) continue;
                if (planet.Settlement.SocialCapital < invCfg.Tier * sci.MoodCostPerTier) continue;
                // Расовый фильтр: для расовых узлов вкладывает только своя раса.
                if (!string.IsNullOrEmpty(invCfg.Race) &&
                    !string.Equals(invCfg.Race, planet.Race, System.StringComparison.Ordinal)) continue;
                // Категория уже выкуплена — пропускаем.
                kv.Value.Spent.TryGetValue(category, out var spent);
                invCfg.Cost.TryGetValue(category, out var need);
                if (spent >= need) continue;
                result.Add(invId);
            }
        }

        // ──────────────────────────────────────────
        // Завершение изобретений + UnlockEffects
        // ──────────────────────────────────────────

        private static readonly List<string> _completedThisTickBuf = new();

        private static void CheckCompletions(GalaxyData galaxy, GalaxyGenerationContext ctx, ScienceConfig sci, GalacticResearchState state)
        {
            _completedThisTickBuf.Clear();
            foreach (var kv in state.Active)
            {
                var invCfg = sci.Inventions[kv.Key];
                if (!IsFullyFunded(invCfg, kv.Value)) continue;
                _completedThisTickBuf.Add(kv.Key);
            }

            for (int i = 0; i < _completedThisTickBuf.Count; i++)
            {
                string invId = _completedThisTickBuf[i];
                var invCfg = sci.Inventions[invId];
                var progress = state.Active[invId];

                state.Active.Remove(invId);
                state.Completed.Add(invId);

                AwardCredits(galaxy, sci, invCfg, progress);
                UnlockEffectsService.ApplyAll(galaxy, ctx, invId, invCfg, progress);

                string invName = string.IsNullOrEmpty(invCfg?.DisplayName) ? invId : invCfg.DisplayName;
                // Сторона изобретения определяется главным контрибутором (планета с наибольшим
                // вкладом). Если контрибуторов нет — публикуется как глобальная сводка.
                string sideOwner = FindMainContributorOwner(galaxy, progress);
                GalaxyNewsService.PostForSide(GalaxyNewsService.CAT_SCIENCE,
                    NewsTexts.Format("science.invention_completed", ("invName", invName)),
                    sideOwner);
            }
        }

        private static bool IsFullyFunded(InventionNodeConfig invCfg, InventionProgress progress)
        {
            if (invCfg?.Cost == null) return false;
            foreach (var kv in invCfg.Cost)
            {
                progress.Spent.TryGetValue(kv.Key, out var spent);
                if (spent + 0.01f < kv.Value) return false;
            }
            return true;
        }

        /// <summary>Возвращает Owner планеты-главного-контрибутора (для скоупа новостей).</summary>
        private static string FindMainContributorOwner(GalaxyData galaxy, InventionProgress progress)
        {
            if (galaxy == null || progress?.Contributors == null) return null;
            string bestUid = null;
            float bestPts = 0f;
            foreach (var kv in progress.Contributors)
            {
                if (kv.Value <= bestPts) continue;
                bestPts = kv.Value;
                bestUid = kv.Key;
            }
            if (bestUid == null) return null;
            return galaxy.PlanetsMap.TryGetValue(bestUid, out var p)
                ? SRG.Galaxy.Politics.OccupationService.GetControllingOwner(p)
                : null;
        }

        private static void AwardCredits(GalaxyData galaxy, ScienceConfig sci, InventionNodeConfig invCfg, InventionProgress progress)
        {
            if (progress.Contributors == null || progress.Contributors.Count == 0) return;

            // Считаем суммарные потраченные поинты (по всем категориям).
            int totalCost = 0;
            if (invCfg.Cost != null) foreach (var kv in invCfg.Cost) totalCost += kv.Value;
            if (totalCost <= 0) return;

            float threshold = sci.MainContributorThreshold * totalCost;
            foreach (var kv in progress.Contributors)
            {
                if (kv.Value < threshold) continue;
                if (!galaxy.PlanetsMap.TryGetValue(kv.Key, out var planet)) continue;
                planet.Settlement.InventionCredits++;
            }
        }

        private static bool IsInhabited(PlanetData planet)
        {
            if (planet == null) return false;
            if (string.IsNullOrEmpty(planet.Race)) return false;
            if (string.Equals(planet.Race, GalaxyConstants.RACE_NONE_KEY, System.StringComparison.OrdinalIgnoreCase)) return false;
            return planet.Settlement.Population > 0;
        }
    }
}
