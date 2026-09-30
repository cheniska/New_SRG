using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Spawning;
using SRG.Ships;
using SRG.Ships.Services;

namespace SRG.Dialog.PlanetGreetings
{
    /// <summary>
    /// Отбор реплики приветствия под текущий контекст (планета+игрок+галактика).
    /// Возвращает целый выбранный <see cref="GreetingRule"/> и, при наличии,
    /// «подкинутую» ToPlanet, чтобы плейсхолдеры <c>{to_planet}</c>/<c>{to_star}</c> знали, куда указывать.
    /// </summary>
    public static class PlanetGreetingSelector
    {
        public sealed class Pick
        {
            public GreetingRule Rule;
            public PlanetData   ToPlanet;
            public StarData     ToStar;
        }

        public static Pick Choose(ShipData player, PlanetData planet,
                                  GalaxyConfig cfg, GalaxyData galaxy,
                                  System.Random rng = null)
        {
            if (planet == null) return null;
            var rules = cfg?.Dialogs?.PlanetGreetings?.Rules;
            if (rules == null || rules.Count == 0) return null;

            rng ??= new System.Random();

            string playerStatus = PlayerGreetingProfile.ResolveStatus(player);
            string playerRank   = PlayerGreetingProfile.ResolveRank(player);
            string playerRating = PlayerGreetingProfile.ResolveRating(player, cfg);

            var currentStar = planet.ParentStar;

            var matched = new List<Pick>();
            int maxPriority = int.MinValue;

            foreach (var rule in rules)
            {
                if (rule == null || rule.Texts == null || rule.Texts.Count == 0) continue;

                if (!MatchesCurPlanet(rule, planet, player, galaxy)) continue;
                if (!MatchesPlayer(rule, player, playerStatus, playerRank, playerRating)) continue;
                if (!MatchesCurStar(rule, currentStar, galaxy)) continue;
                if (!MatchesFactionsGlobal(rule, galaxy)) continue;
                if (!MatchesGoodsInPlanet(rule, planet, cfg)) continue;
                if (!MatchesExtrasForPlayer(rule, player, cfg)) continue;
                if (!MatchesExtrasForCurrent(rule, planet, currentStar, galaxy)) continue;

                PlanetData toPlanet = null; StarData toStar = null;
                if (NeedsToPlanet(rule))
                {
                    if (!TryPickToPlanet(rule, planet, player, galaxy, cfg, rng, out toPlanet, out toStar))
                        continue;
                }

                if (rule.Priority > maxPriority)
                {
                    matched.Clear();
                    maxPriority = rule.Priority;
                }
                if (rule.Priority == maxPriority)
                    matched.Add(new Pick { Rule = rule, ToPlanet = toPlanet, ToStar = toStar });
            }

            return matched.Count == 0 ? null : matched[rng.Next(matched.Count)];
        }

        // ── CurPlanet + relation ──────────────────────────────────────

        private static bool MatchesCurPlanet(GreetingRule r, PlanetData planet, ShipData player, GalaxyData galaxy)
        {
            if (!AnyOrNull(r.PlanetRace, planet.Race)) return false;
            if (!AnyOrNull(r.PlanetGovernment, planet.Settlement?.Government)) return false;
            if (!AnyOrNull(r.PlanetEconomy, planet.Settlement?.EconomyType)) return false;

            if (!YesNoMatches(r.PlanetRaceIsPlayerRace, PlayerRaceEquals(planet.Race, player))) return false;

            if (r.PlanetRelation != null && r.PlanetRelation.Length > 0)
            {
                string level = player != null
                    ? RelationLevelName(Relations.GetLevelToPlayer(planet))
                    : "Normal";
                if (!ContainsRelation(r.PlanetRelation, level)) return false;
            }

            // Флаги планеты.
            if (!YesNoMatches(r.CurPlanetIsHomeworld, planet.IsHomeworld)) return false;
            if (!YesNoMatches(r.QuestGiver,           planet.IsQuestGiver)) return false;
            if (!YesNoMatches(r.CurPlanetVisited,
                    galaxy?.VisitedPlanetUids != null && galaxy.VisitedPlanetUids.Contains(planet.Uid))) return false;

            // Население / техуровень.
            if (r.CurPlanetPopulation != null && r.CurPlanetPopulation.Length > 0)
            {
                if (!AnyOrNull(r.CurPlanetPopulation, QuantOfPopulation(planet.Settlement?.Population ?? 0)))
                    return false;
            }
            if (r.CurPlanetTechLevel != null && r.CurPlanetTechLevel.Length > 0)
            {
                if (!Contains(r.CurPlanetTechLevel, (planet.Settlement?.TechLevel ?? 0).ToString()))
                    return false;
            }
            return true;
        }

        private static bool MatchesPlayer(GreetingRule r, ShipData player,
                                          string status, string rank, string rating)
        {
            if (!AnyOrNull(r.PlayerRace, player?.Race)) return false;
            if (!AnyOrNull(r.PlayerStatus, status)) return false;
            if (!AnyOrNull(r.PlayerRank, rank)) return false;
            if (!AnyOrNull(r.PlayerRating, rating)) return false;
            if (r.PlayerMoney != null && r.PlayerMoney.Length > 0)
                if (!AnyOrNull(r.PlayerMoney, QuantOfMoney(player?.Money ?? 0))) return false;
            return true;
        }

        // ── CurStar (bat­tle, ship counters, faction battles) ─────────

        private static bool MatchesCurStar(GreetingRule r, StarData star, GalaxyData galaxy)
        {
            if (star == null)
                return string.IsNullOrEmpty(r.CurStarInBattle) &&
                       (r.ShipsInCurStar == null || r.ShipsInCurStar.Count == 0) &&
                       (r.StarBattleWithInCurStar == null || r.StarBattleWithInCurStar.Count == 0) &&
                       string.IsNullOrEmpty(r.CurStarHasWormHole);

            if (!YesNoAnyMatches(r.CurStarInBattle, StarInBattle(star))) return false;
            if (!YesNoMatches(r.CurStarHasWormHole,  star.Wormholes != null && star.Wormholes.Count > 0)) return false;

            if (r.ShipsInCurStar != null)
                foreach (var kv in r.ShipsInCurStar)
                    if (!Contains(kv.Value, QuantOfShipCount(ShipsInStarBySide(star.Uid, kv.Key))))
                        return false;

            if (r.StarBattleWithInCurStar != null)
                foreach (var kv in r.StarBattleWithInCurStar)
                    if (!YesNoMatches(kv.Value, StarInBattleWith(star, kv.Key)))
                        return false;

            return true;
        }

        // ── Глобальные предикаты: побеждённые фракции ─────────────────

        private static bool MatchesFactionsGlobal(GreetingRule r, GalaxyData galaxy)
        {
            if (r.FactionDefeated != null)
                foreach (var kv in r.FactionDefeated)
                    if (!YesNoMatches(kv.Value, IsFactionDefeated(galaxy, kv.Key)))
                        return false;

            if (r.Extras != null)
                foreach (var kv in r.Extras)
                {
                    if (kv.Key == null || !kv.Key.EndsWith("Defeated", System.StringComparison.OrdinalIgnoreCase)) continue;
                    string side = kv.Key.Substring(0, kv.Key.Length - "Defeated".Length);
                    if (string.IsNullOrEmpty(side)) continue;
                    if (!YesNoMatches(ExtraAsString(kv.Value), IsFactionDefeated(galaxy, side)))
                        return false;
                }

            if (!string.IsNullOrEmpty(r.AnyMajorFactionDefeated))
            {
                bool any = galaxy?.DefeatedFactions != null && galaxy.DefeatedFactions.Count > 0;
                if (!YesNoMatches(r.AnyMajorFactionDefeated, any)) return false;
            }
            return true;
        }

        // ── Шаблон-ключи, зависящие только от игрока: PlayerRating<Name>, PlayerRank<Name>. ──
        internal static bool MatchesExtrasForPlayer(GreetingRule r, ShipData player, GalaxyConfig cfg)
        {
            if (r.Extras == null || r.Extras.Count == 0) return true;
            foreach (var kv in r.Extras)
            {
                var key = kv.Key; if (string.IsNullOrEmpty(key)) continue;

                // PlayerRating<Name>: значение — квант Mini/Small/Average/Big/Huge.
                if (key.StartsWith("PlayerRating", System.StringComparison.OrdinalIgnoreCase)
                    && key.Length > "PlayerRating".Length)
                {
                    string ratingId = key.Substring("PlayerRating".Length);
                    string quant = ResolveRatingQuant(player, cfg, ratingId);
                    if (!Contains(ExtraAsArray(kv.Value), quant)) return false;
                    continue;
                }

                // PlayerRank<Name|N>: значение Yes/No. <N> — числовой порог в лестнице (0..). <Name> — имя ступени.
                if (key.StartsWith("PlayerRank", System.StringComparison.OrdinalIgnoreCase)
                    && key.Length > "PlayerRank".Length)
                {
                    string threshold = key.Substring("PlayerRank".Length);
                    bool reached = PlayerReachedRank(player, threshold);
                    if (!YesNoAnyMatches(ExtraAsString(kv.Value), reached)) return false;
                }
            }
            return true;
        }

        private static string ResolveRatingQuant(ShipData player, GalaxyConfig cfg, string ratingId)
        {
            if (player == null || cfg == null || string.IsNullOrEmpty(ratingId)) return "Mini";
            var ratingCfg = ShipRatingService.FindRating(cfg, ratingId);
            float score = ratingCfg != null ? ShipRatingService.GetScore(player, ratingCfg) : 0f;
            var ladder = cfg.Dialogs?.Tuning?.RatingLadder;
            if (ladder != null && ladder.Count > 0)
            {
                string best = null; float bestMin = float.NegativeInfinity;
                foreach (var step in ladder)
                {
                    if (step == null || string.IsNullOrEmpty(step.Level)) continue;
                    if (score >= step.MinScore && step.MinScore >= bestMin) { best = step.Level; bestMin = step.MinScore; }
                }
                if (best != null) return best;
            }
            if (score >= 300f) return "Huge";
            if (score >= 100f) return "Big";
            if (score >= 30f)  return "Average";
            if (score >= 10f)  return "Small";
            return "Mini";
        }

        private static bool PlayerReachedRank(ShipData player, string threshold)
        {
            if (player == null || string.IsNullOrEmpty(threshold)) return false;
            var ladder = new[] { "Rookie", "Cadet", "Pilot", "Wingman", "Leader", "Ace", "Commander", "Admiral" };
            string playerRank = PlayerGreetingProfile.ResolveRank(player);
            int playerIdx = System.Array.FindIndex(ladder,
                x => string.Equals(x, playerRank, System.StringComparison.OrdinalIgnoreCase));
            if (playerIdx < 0) playerIdx = 0;

            int needIdx;
            if (int.TryParse(threshold, out int n)) needIdx = n;
            else needIdx = System.Array.FindIndex(ladder,
                x => string.Equals(x, threshold, System.StringComparison.OrdinalIgnoreCase));
            if (needIdx < 0) return false;
            return playerIdx >= needIdx;
        }

        // ── Плоские ключи: <Side>InCurStar, <Ship>InCurStar,
        //    CurStarInBattle<Side>, CurPlanetOccupiedBy<Side>. ─────────
        internal static bool MatchesExtrasForCurrent(GreetingRule r, PlanetData planet, StarData star, GalaxyData galaxy)
        {
            if (r.Extras == null || r.Extras.Count == 0) return true;
            foreach (var kv in r.Extras)
            {
                var key = kv.Key; if (string.IsNullOrEmpty(key)) continue;

                // CurStarInBattle<Side>
                if (key.StartsWith("CurStarInBattle", System.StringComparison.OrdinalIgnoreCase)
                    && key.Length > "CurStarInBattle".Length)
                {
                    string side = key.Substring("CurStarInBattle".Length);
                    if (!YesNoAnyMatches(ExtraAsString(kv.Value), StarInBattleWith(star, side))) return false;
                    continue;
                }

                // CurPlanetOccupiedBy<Side>
                if (key.StartsWith("CurPlanetOccupiedBy", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (planet == null) continue; // Ship-greeting context — планеты нет, фильтр пропускаем.
                    string side = key.Substring("CurPlanetOccupiedBy".Length);
                    bool occupied = !string.IsNullOrEmpty(side) &&
                                    string.Equals(planet.Settlement?.OccupiedByOwner, side,
                                                  System.StringComparison.OrdinalIgnoreCase);
                    if (!YesNoAnyMatches(ExtraAsString(kv.Value), occupied)) return false;
                    continue;
                }

                // <Side>InCurStar / <ShipType>InCurStar
                if (key.EndsWith("InCurStar", System.StringComparison.OrdinalIgnoreCase))
                {
                    string tag = key.Substring(0, key.Length - "InCurStar".Length);
                    if (string.IsNullOrEmpty(tag)) continue;
                    int cnt = ShipsInStarBySide(star?.Uid, tag);
                    if (cnt == 0) cnt = SpawnSystem.Counters?.GetShipsAtStar(star?.Uid, tag) ?? 0;
                    if (!Contains(ExtraAsArray(kv.Value), QuantOfShipCount(cnt))) return false;
                }
                // Ключи Cur*/*Defeated обрабатываются в других шагах.
            }
            return true;
        }

        internal static bool MatchesExtrasForTo(GreetingRule r, StarData toStar, PlanetData to)
        {
            if (r.Extras == null || r.Extras.Count == 0) return true;
            foreach (var kv in r.Extras)
            {
                var key = kv.Key; if (string.IsNullOrEmpty(key)) continue;

                // ToStarControlBy<Side>
                if (key.StartsWith("ToStarControlBy", System.StringComparison.OrdinalIgnoreCase))
                {
                    string side = key.Substring("ToStarControlBy".Length);
                    bool controlled = !string.IsNullOrEmpty(side) &&
                                      string.Equals(toStar?.Owner, side, System.StringComparison.OrdinalIgnoreCase);
                    if (!YesNoAnyMatches(ExtraAsString(kv.Value), controlled)) return false;
                    continue;
                }

                if (key.StartsWith("ToStarInBattle", System.StringComparison.OrdinalIgnoreCase)
                    && key.Length > "ToStarInBattle".Length)
                {
                    string side = key.Substring("ToStarInBattle".Length);
                    if (!YesNoAnyMatches(ExtraAsString(kv.Value), StarInBattleWith(toStar, side))) return false;
                    continue;
                }

                if (key.EndsWith("InToStar", System.StringComparison.OrdinalIgnoreCase))
                {
                    string tag = key.Substring(0, key.Length - "InToStar".Length);
                    if (string.IsNullOrEmpty(tag)) continue;
                    int cnt = ShipsInStarBySide(toStar?.Uid, tag);
                    if (cnt == 0) cnt = SpawnSystem.Counters?.GetShipsAtStar(toStar?.Uid, tag) ?? 0;
                    if (!Contains(ExtraAsArray(kv.Value), QuantOfShipCount(cnt))) return false;
                }
            }
            return true;
        }

        private static string ExtraAsString(JToken tok)
        {
            if (tok == null) return null;
            if (tok.Type == JTokenType.String || tok.Type == JTokenType.Integer || tok.Type == JTokenType.Boolean)
                return tok.ToString();
            return tok.ToString();
        }

        private static string[] ExtraAsArray(JToken tok)
        {
            if (tok == null) return null;
            if (tok.Type == JTokenType.Array)
            {
                var arr = (JArray)tok;
                var result = new string[arr.Count];
                for (int i = 0; i < arr.Count; i++) result[i] = arr[i]?.ToString();
                return result;
            }
            return new[] { tok.ToString() };
        }

        // ── Товар на планете ──────────────────────────────────────────

        private static bool MatchesGoodsInPlanet(GreetingRule r, PlanetData planet, GalaxyConfig cfg)
        {
            if (string.IsNullOrEmpty(r.Goods) &&
                r.PlanetGoodsCnt == null && r.PlanetGoodsSale == null &&
                r.PlanetGoodsBuy == null && string.IsNullOrEmpty(r.PlanetGoodsPermit))
                return true;

            var shop = planet?.Settlement?.Shop;
            if (shop == null || shop.Goods == null) return false;
            if (string.IsNullOrEmpty(r.Goods)) return true; // фильтров по товару нет — уже прошло

            if (!shop.Goods.TryGetValue(r.Goods, out var entry) || entry == null) return false;

            // Семантика по diздоку: Yes = контрабанда, No = легально.
            bool contraband = cfg != null && !TradeSystem.IsLegal(planet, r.Goods, cfg);
            if (!YesNoAnyMatches(r.PlanetGoodsPermit, contraband)) return false;

            var basePrice = LookupBasePrice(cfg, r.Goods);
            if (!AnyOrNull(r.PlanetGoodsCnt,  QuantOfStock(entry.Stock))) return false;
            if (!AnyOrNull(r.PlanetGoodsSale, QuantOfPrice(entry.BuyPrice,  basePrice))) return false;
            if (!AnyOrNull(r.PlanetGoodsBuy,  QuantOfPrice(entry.SellPrice, basePrice))) return false;
            return true;
        }

        // ── ToPlanet: подобрать конкретную планету под условия ───────

        private static bool NeedsToPlanet(GreetingRule r)
        {
            if (r.ToPlanetRace != null
                || !string.IsNullOrEmpty(r.ToPlanetInCurStar)
                || !string.IsNullOrEmpty(r.ToPlanetRaceIsCurPlanetRace)
                || !string.IsNullOrEmpty(r.ToPlanetRaceIsPlayerRace)
                || r.ToPlanetGovernment != null || r.ToPlanetEconomy != null
                || !string.IsNullOrEmpty(r.ToPlanetGoodsPermit)
                || r.ToPlanetGoodsCnt != null || r.ToPlanetGoodsSale != null || r.ToPlanetGoodsBuy != null
                || r.ToPlanetRelation != null
                || !string.IsNullOrEmpty(r.ToStarInBattle)
                || !string.IsNullOrEmpty(r.ToStarControlByEnemy)
                || (r.ShipsInToStar != null && r.ShipsInToStar.Count > 0)
                || (r.StarBattleWithInToStar != null && r.StarBattleWithInToStar.Count > 0))
                return true;
            // Плоские ключи, требующие подбора ToPlanet: *InToStar, ToStarControledBy*, ToStarInBattle*.
            if (r.Extras != null)
                foreach (var k in r.Extras.Keys)
                {
                    if (string.IsNullOrEmpty(k)) continue;
                    if (k.EndsWith("InToStar", System.StringComparison.OrdinalIgnoreCase)) return true;
                    if (k.StartsWith("ToStar", System.StringComparison.OrdinalIgnoreCase)) return true;
                }
            return false;
        }

        private static bool TryPickToPlanet(GreetingRule r, PlanetData cur, ShipData player,
                                            GalaxyData galaxy, GalaxyConfig cfg, System.Random rng,
                                            out PlanetData picked, out StarData pickedStar)
        {
            picked = null; pickedStar = null;
            if (galaxy?.PlanetsMap == null) return false;

            // Локальность ToStar: текущий сектор + соседние Вороного.
            var starSet = BuildLocalStarSet(cur.ParentStar);
            // ToPlanet по умолчанию — той же расы, если ToPlanetRace явно не задан.
            bool restrictSameRace = r.ToPlanetRace == null || r.ToPlanetRace.Length == 0;

            List<PlanetData> candidates = null;
            foreach (var kv in galaxy.PlanetsMap)
            {
                var to = kv.Value; if (to == null || to == cur) continue;
                var toStar = to.ParentStar; if (toStar == null) continue;
                if (starSet != null && !starSet.Contains(toStar.Uid)) continue;
                if (restrictSameRace && !RacesEqual(to.Race, cur.Race)) continue;
                if (!MatchesToPlanet(r, to, toStar, cur, player, galaxy, cfg)) continue;
                if (!MatchesExtrasForTo(r, toStar, to)) continue;
                candidates ??= new List<PlanetData>();
                candidates.Add(to);
            }
            if (candidates == null) return false;
            picked = candidates[rng.Next(candidates.Count)];
            pickedStar = picked.ParentStar;
            return true;
        }

        /// <summary>UID-звёзд для «текущий сектор + соседние». null означает «без ограничения» (fallback).</summary>
        private static HashSet<string> BuildLocalStarSet(StarData curStar)
        {
            var sector = curStar?.ParentSector;
            if (sector == null) return null;
            var set = new HashSet<string>();
            foreach (var s in sector.Stars) if (s != null) set.Add(s.Uid);
            var mgr = GalaxyManager.Instance?.GeneratedGalaxy;
            if (mgr != null && sector.VoronoiEdgeNeighbors != null)
                foreach (var neighbourUid in sector.VoronoiEdgeNeighbors)
                {
                    if (string.IsNullOrEmpty(neighbourUid)) continue;
                    if (!mgr.SectorsMap.TryGetValue(neighbourUid, out var n) || n == null) continue;
                    foreach (var s in n.Stars) if (s != null) set.Add(s.Uid);
                }
            return set;
        }

        private static bool MatchesToPlanet(GreetingRule r, PlanetData to, StarData toStar,
                                            PlanetData cur, ShipData player, GalaxyData galaxy, GalaxyConfig cfg)
        {
            if (!AnyOrNull(r.ToPlanetRace, to.Race)) return false;
            if (!AnyOrNull(r.ToPlanetGovernment, to.Settlement?.Government)) return false;
            if (!AnyOrNull(r.ToPlanetEconomy,    to.Settlement?.EconomyType)) return false;

            if (!YesNoMatches(r.ToPlanetInCurStar, toStar == cur.ParentStar)) return false;
            if (!YesNoMatches(r.ToPlanetRaceIsCurPlanetRace, RacesEqual(to.Race, cur.Race))) return false;
            if (!YesNoMatches(r.ToPlanetRaceIsPlayerRace, PlayerRaceEquals(to.Race, player))) return false;

            if (r.ToPlanetRelation != null && r.ToPlanetRelation.Length > 0)
            {
                string level = player != null
                    ? RelationLevelName(Relations.GetLevelToPlayer(to))
                    : "Normal";
                if (!ContainsRelation(r.ToPlanetRelation, level)) return false;
            }

            if (!YesNoAnyMatches(r.ToStarInBattle, StarInBattle(toStar))) return false;
            if (!YesNoMatches(r.ToStarControlByEnemy, StarControlledByEnemy(toStar, player))) return false;

            if (r.ShipsInToStar != null)
                foreach (var kv in r.ShipsInToStar)
                    if (!Contains(kv.Value, QuantOfShipCount(ShipsInStarBySide(toStar.Uid, kv.Key))))
                        return false;

            if (r.StarBattleWithInToStar != null)
                foreach (var kv in r.StarBattleWithInToStar)
                    if (!YesNoMatches(kv.Value, StarInBattleWith(toStar, kv.Key)))
                        return false;

            // Товар в ToPlanet — только если рулет по нему хоть что-то требует.
            bool needGoods = !string.IsNullOrEmpty(r.ToPlanetGoodsPermit)
                          || r.ToPlanetGoodsCnt != null
                          || r.ToPlanetGoodsSale != null
                          || r.ToPlanetGoodsBuy != null;
            if (needGoods && !string.IsNullOrEmpty(r.Goods))
            {
                var shop = to.Settlement?.Shop;
                if (shop?.Goods == null || !shop.Goods.TryGetValue(r.Goods, out var entry) || entry == null)
                    return false;
                bool contraband = cfg != null && !TradeSystem.IsLegal(to, r.Goods, cfg);
                if (!YesNoAnyMatches(r.ToPlanetGoodsPermit, contraband)) return false;

                var basePrice = LookupBasePrice(cfg, r.Goods);
                if (!AnyOrNull(r.ToPlanetGoodsCnt,  QuantOfStock(entry.Stock))) return false;
                if (!AnyOrNull(r.ToPlanetGoodsSale, QuantOfPrice(entry.BuyPrice,  basePrice))) return false;
                if (!AnyOrNull(r.ToPlanetGoodsBuy,  QuantOfPrice(entry.SellPrice, basePrice))) return false;
            }
            return true;
        }

        // ── Утилиты матчинга ──────────────────────────────────────────

        /// <summary>
        /// Правило матчинга массива с поддержкой отрицаний ("!X" — исключить).
        /// Проходит, если:
        ///   • массив null/пустой (условия нет);
        ///   • среди положительных элементов есть совпадение
        ///     ИЛИ положительных нет вовсе (и тогда достаточно, чтобы actual не был в отрицаниях);
        ///   • actual не совпал ни с одним из "!"-элементов.
        /// </summary>
        public static bool AnyOrNull(string[] allowed, string actual)
        {
            if (allowed == null || allowed.Length == 0) return true;
            bool anyPositive = false, positiveHit = false, negativeHit = false;
            for (int i = 0; i < allowed.Length; i++)
            {
                var e = allowed[i]; if (string.IsNullOrEmpty(e)) continue;
                if (e[0] == '!')
                {
                    if (!string.IsNullOrEmpty(actual) &&
                        string.Equals(e.Substring(1), actual, System.StringComparison.OrdinalIgnoreCase))
                        negativeHit = true;
                }
                else
                {
                    anyPositive = true;
                    if (!string.IsNullOrEmpty(actual) &&
                        string.Equals(e, actual, System.StringComparison.OrdinalIgnoreCase))
                        positiveHit = true;
                }
            }
            if (negativeHit) return false;
            return !anyPositive || positiveHit;
        }

        public static bool Contains(string[] arr, string val)
        {
            if (arr == null) return true;
            if (string.IsNullOrEmpty(val)) return false;
            for (int i = 0; i < arr.Length; i++)
            {
                var e = arr[i]; if (string.IsNullOrEmpty(e) || e[0] == '!') continue;
                if (string.Equals(e, val, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Матч для отношений с алиасом Enemy ↔ Hostile.</summary>
        public static bool ContainsRelation(string[] allowed, string level)
        {
            if (AnyOrNull(allowed, level)) return true;
            // Проверим ещё раз с алиасом.
            string alias = string.Equals(level, "Enemy", System.StringComparison.OrdinalIgnoreCase) ? "Hostile"
                         : string.Equals(level, "Hostile", System.StringComparison.OrdinalIgnoreCase) ? "Enemy"
                         : null;
            return alias != null && AnyOrNull(allowed, alias);
        }

        // "Yes"/"No" сравнение с булевым состоянием. Пустая строка — не фильтр.
        public static bool YesNoMatches(string filter, bool actual)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.Equals(filter, "Yes", System.StringComparison.OrdinalIgnoreCase)) return actual;
            if (string.Equals(filter, "No",  System.StringComparison.OrdinalIgnoreCase)) return !actual;
            return true; // «Any» и произвольные значения — считаем «не фильтром».
        }

        // Как YesNo, но допускает "Any" — сохранено для совместимости со старым форматом.
        public static bool YesNoAnyMatches(string filter, bool actual) => YesNoMatches(filter, actual);

        // ── Специфика игры ────────────────────────────────────────────

        public static bool PlayerRaceEquals(string race, ShipData player)
            => RacesEqual(race, player?.Race);

        public static bool RacesEqual(string a, string b)
            => !string.IsNullOrEmpty(a) && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        public static string RelationLevelName(RelationLevel lvl) => lvl switch
        {
            RelationLevel.Hostile => "Enemy",   // "Hostile" тоже принимается через ContainsRelation.
            RelationLevel.Bad     => "Bad",
            RelationLevel.Normal  => "Normal",
            RelationLevel.Good    => "Good",
            RelationLevel.Best    => "Best",
            _                     => "Normal",
        };

        // Per-turn кэш StarInBattle: (starUid, turn) → результат. Инвалидируется автоматически по ходу.
        private static readonly Dictionary<string, (int turn, bool value)> _starInBattleCache = new();

        public static bool StarInBattle(StarData star)
        {
            if (star?.Ships == null || string.IsNullOrEmpty(star.Uid)) return false;
            int turn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            if (_starInBattleCache.TryGetValue(star.Uid, out var cached) && cached.turn == turn)
                return cached.value;

            // «Бой» — хотя бы одна пара живых кораблей из разных враждующих owner'ов в системе.
            var mgr = OwnerRaceRelationsManager.Instance;
            bool inBattle = false;
            if (mgr != null)
            {
                var ships = star.Ships;
                for (int i = 0; i < ships.Count && !inBattle; i++)
                {
                    var a = ships[i]; if (a == null || a.CurrentHull <= 0) continue;
                    for (int j = i + 1; j < ships.Count; j++)
                    {
                        var b = ships[j]; if (b == null || b.CurrentHull <= 0) continue;
                        if (mgr.AreHostile(a, b)) { inBattle = true; break; }
                    }
                }
            }
            _starInBattleCache[star.Uid] = (turn, inBattle);
            return inBattle;
        }

        public static bool StarInBattleWith(StarData star, string faction)
        {
            if (star?.Ships == null || string.IsNullOrEmpty(faction)) return false;
            bool sideHere = false, sideEnemyHere = false;
            var mgr = OwnerRaceRelationsManager.Instance;
            foreach (var s in star.Ships)
            {
                if (s == null || s.CurrentHull <= 0) continue;
                if (string.Equals(s.Owner, faction, System.StringComparison.OrdinalIgnoreCase)) sideHere = true;
                else if (mgr != null && mgr.GetFactionRelation(s.Owner, faction, s.Race, null)
                                        <= OwnerRaceRelationsManager.HOSTILE_MAX) sideEnemyHere = true;
            }
            return sideHere && sideEnemyHere;
        }

        public static bool StarControlledByEnemy(StarData star, ShipData player)
        {
            if (star == null || player == null) return false;
            var mgr = OwnerRaceRelationsManager.Instance;
            if (mgr == null) return false;
            int rel = mgr.GetFactionRelation(star.Owner, player.Owner, star.Race, player.Race);
            return rel <= OwnerRaceRelationsManager.HOSTILE_MAX;
        }

        public static bool IsFactionDefeated(GalaxyData galaxy, string faction)
        {
            if (galaxy?.DefeatedFactions == null || string.IsNullOrEmpty(faction)) return false;
            return galaxy.DefeatedFactions.Contains(faction);
        }

        public static int ShipsInStarBySide(string starUid, string sideId)
        {
            if (string.IsNullOrEmpty(starUid) || string.IsNullOrEmpty(sideId)) return 0;
            var counters = SpawnSystem.Counters;
            return counters == null ? 0 : counters.GetShipsAtStarBySide(starUid, sideId);
        }

        // ── Кванты ────────────────────────────────────────────────────
        // Пороги вынесены в DialogTuning (Assets/Config/DialogsConfig.json → "Tuning").

        public static string QuantOfShipCount(int n)
        {
            int many = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning?.ShipCountMany ?? 10;
            if (n <= 0) return "0";
            if (n >= many) return "Many";
            return n.ToString();
        }

        public static string QuantOfStock(int n)
        {
            var q = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning?.StockQuants;
            int mini = q?.Mini ?? 50, small = q?.Small ?? 200, avg = q?.Average ?? 500, big = q?.Big ?? 1000;
            if (n <= 0)     return "Zero";
            if (n < mini)   return "Mini";
            if (n < small)  return "Small";
            if (n < avg)    return "Average";
            if (n < big)    return "Big";
            return "Huge";
        }

        public static string QuantOfPrice(int price, int basePrice)
        {
            if (price <= 0) return "Zero";
            if (basePrice <= 0) return "Average";
            var q = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning?.PriceQuants;
            float mini = q?.Mini ?? 0.6f, small = q?.Small ?? 0.9f, avg = q?.Average ?? 1.3f, big = q?.Big ?? 2.0f;
            float ratio = price / (float)basePrice;
            if (ratio < mini)  return "Mini";
            if (ratio < small) return "Small";
            if (ratio < avg)   return "Average";
            if (ratio < big)   return "Big";
            return "Huge";
        }

        /// <summary>Квантование населения планеты (Mini/Small/Average/Big/Huge).</summary>
        public static string QuantOfPopulation(int n)
        {
            var q = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning?.PopulationQuants;
            int mini = q?.Mini ?? 1_000_000,
                small = q?.Small ?? 100_000_000,
                avg = q?.Average ?? 1_000_000_000,
                big = q?.Big ?? 2_000_000_000;
            if (n < mini)  return "Mini";
            if (n < small) return "Small";
            if (n < avg)   return "Average";
            if (n < big)   return "Big";
            return "Huge";
        }

        /// <summary>Квантование денег игрока (Mini/Small/Average/Big/Huge).</summary>
        public static string QuantOfMoney(int m)
        {
            var q = GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning?.MoneyQuants;
            int mini = q?.Mini ?? 1_000,
                small = q?.Small ?? 10_000,
                avg = q?.Average ?? 100_000,
                big = q?.Big ?? 1_000_000;
            if (m < mini)  return "Mini";
            if (m < small) return "Small";
            if (m < avg)   return "Average";
            if (m < big)   return "Big";
            return "Huge";
        }

        public static int LookupBasePrice(GalaxyConfig cfg, string goodId)
        {
            if (cfg?.Goods == null || string.IsNullOrEmpty(goodId)) return 0;
            return cfg.Goods.TryGetValue(goodId, out var g) && g != null ? g.BasePrice : 0;
        }
    }
}
