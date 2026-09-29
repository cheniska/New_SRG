using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.Ships;
using SRG.Ships.Player;

namespace SRG.Dialog.PlanetGreetings
{
    /// <summary>
    /// Отбор реплики приветствия капитана корабля. Схема правил единая
    /// (<see cref="GreetingRule"/>) — переиспользуем предикаты из <see cref="PlanetGreetingSelector"/>,
    /// добавляя фильтры Ship*/LastPlanet*/FlyType/ToShip*.
    /// </summary>
    public static class ShipGreetingSelector
    {
        public sealed class Pick
        {
            public GreetingRule Rule;
            public PlanetData   ToPlanet;
            public StarData     ToStar;
        }

        public static Pick Choose(ShipData player, ShipData ship,
                                  GalaxyConfig cfg, GalaxyData galaxy,
                                  System.Random rng = null)
        {
            if (ship == null) return null;
            var rules = cfg?.Dialogs?.ShipGreetings?.Rules;
            if (rules == null || rules.Count == 0) return null;

            rng ??= new System.Random();

            string playerStatus = PlayerGreetingProfile.ResolveStatus(player);
            string playerRank   = PlayerGreetingProfile.ResolveRank(player);
            string playerRating = PlayerGreetingProfile.ResolveRating(player, cfg);

            var currentStar = ResolveCurrentStar(ship, galaxy);

            var matched = new List<Pick>();
            int maxPriority = int.MinValue;

            foreach (var rule in rules)
            {
                if (rule == null || rule.Texts == null || rule.Texts.Count == 0) continue;

                if (IsAutoTriggerOnly(rule)) continue;
                if (!MatchesShip(rule, ship, player)) continue;
                if (!MatchesPlayer(rule, player, playerStatus, playerRank, playerRating)) continue;
                if (!MatchesCurStar(rule, currentStar)) continue;
                if (!MatchesFactionsGlobal(rule, galaxy)) continue;
                if (!MatchesShipGoods(rule, ship)) continue;
                if (!MatchesPlayerGoods(rule, player)) continue;
                if (!MatchesLastPlanet(rule, ship, player, galaxy)) continue;
                if (!MatchesLastPlanetGoods(rule, ship, galaxy, cfg)) continue;
                if (!MatchesLastPlanetDistance(rule, ship, galaxy)) continue;
                if (!MatchesHomePlanet(rule, ship, galaxy)) continue;
                if (!MatchesFlyType(rule, ship)) continue;
                if (!MatchesShipVsPlayer(rule, ship, player)) continue;
                if (!MatchesFear(rule, ship, currentStar)) continue;
                if (!MatchesShipOrder(rule, ship, currentStar)) continue;
                if (!MatchesShipNeedItem(rule, ship, currentStar)) continue;
                if (!MatchesToShip(rule, ship, player, currentStar)) continue;
                if (!MatchesShipBad(rule, ship, currentStar)) continue;
                if (!MatchesPlayerAttackGoodShip(rule, ship, player, currentStar)) continue;
                if (!PlanetGreetingSelector.MatchesExtrasForPlayer(rule, player, cfg)) continue;
                if (!PlanetGreetingSelector.MatchesExtrasForCurrent(rule, null, currentStar, galaxy)) continue;

                PlanetData toPlanet = null; StarData toStar = null;
                if (NeedsToPlanet(rule))
                {
                    if (!TryPickToPlanet(rule, ship, player, galaxy, cfg, rng, out toPlanet, out toStar))
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

        // Правила с AutoTalk=Yes срабатывают только когда NPC сам инициирует разговор
        // по игровому событию (атака игрока по другу, сканирование, погоня). В mануальной
        // связи с кораблём такие реплики не имеют смысла — фильтруем.
        // Any / No / (пусто) — обычные правила, всплывают в player-канале.
        private static bool IsAutoTriggerOnly(GreetingRule r)
            => string.Equals(r.AutoTalk, "Yes", System.StringComparison.OrdinalIgnoreCase);

        // ── Ship ─────────────────────────────────────────────────────

        private static bool MatchesShip(GreetingRule r, ShipData ship, ShipData player)
        {
            if (!PlanetGreetingSelector.AnyOrNull(r.ShipRace, ship.Race)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.ShipType, ship.ShipTypeId)) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.ShipRaceIsPlayerRace,
                    PlanetGreetingSelector.RacesEqual(ship.Race, player?.Race))) return false;

            // Отношение корабль→игрок
            if (r.Relations != null && r.Relations.Length > 0)
            {
                string level = player != null
                    ? PlanetGreetingSelector.RelationLevelName(Relations.GetLevelToPlayer(ship))
                    : "Normal";
                if (!PlanetGreetingSelector.ContainsRelation(r.Relations, level)) return false;
            }

            // ShipStatus (Pirate/Trader/Warrior/Ranger)
            if (r.ShipStatus != null && r.ShipStatus.Length > 0)
            {
                string status = PlayerGreetingProfile.ResolveStatus(ship);
                if (!PlanetGreetingSelector.AnyOrNull(r.ShipStatus, status)) return false;
            }

            // ShipStrength / ShipStructure — абсолютные кванты.
            if (r.ShipStrength != null && r.ShipStrength.Length > 0)
                if (!PlanetGreetingSelector.AnyOrNull(r.ShipStrength, QuantOfStrength(NpcBrain.CalculateStrength(ship))))
                    return false;
            if (r.ShipStructure != null && r.ShipStructure.Length > 0)
                if (!PlanetGreetingSelector.AnyOrNull(r.ShipStructure, QuantOfHullPct(ship))) return false;

            return true;
        }

        private static bool MatchesPlayer(GreetingRule r, ShipData player,
                                          string status, string rank, string rating)
        {
            if (!PlanetGreetingSelector.AnyOrNull(r.PlayerRace, player?.Race)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.PlayerStatus, status)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.PlayerRank, rank)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.PlayerRating, rating)) return false;

            if (r.PlayerMoney != null && r.PlayerMoney.Length > 0)
                if (!PlanetGreetingSelector.AnyOrNull(r.PlayerMoney,
                        PlanetGreetingSelector.QuantOfMoney(player?.Money ?? 0))) return false;

            if (r.PlayerStrength != null && r.PlayerStrength.Length > 0)
                if (!PlanetGreetingSelector.AnyOrNull(r.PlayerStrength,
                        QuantOfStrength(player != null ? NpcBrain.CalculateStrength(player) : 0f))) return false;

            if (r.PlayerStructure != null && r.PlayerStructure.Length > 0)
                if (!PlanetGreetingSelector.AnyOrNull(r.PlayerStructure, QuantOfHullPct(player))) return false;

            return true;
        }

        private static bool MatchesCurStar(GreetingRule r, StarData star)
        {
            if (star == null)
                return string.IsNullOrEmpty(r.CurStarInBattle) &&
                       (r.ShipsInCurStar == null || r.ShipsInCurStar.Count == 0) &&
                       (r.StarBattleWithInCurStar == null || r.StarBattleWithInCurStar.Count == 0);

            if (!PlanetGreetingSelector.YesNoAnyMatches(r.CurStarInBattle,
                    PlanetGreetingSelector.StarInBattle(star))) return false;

            if (r.ShipsInCurStar != null)
                foreach (var kv in r.ShipsInCurStar)
                    if (!PlanetGreetingSelector.Contains(kv.Value,
                            PlanetGreetingSelector.QuantOfShipCount(
                                PlanetGreetingSelector.ShipsInStarBySide(star.Uid, kv.Key))))
                        return false;

            if (r.StarBattleWithInCurStar != null)
                foreach (var kv in r.StarBattleWithInCurStar)
                    if (!PlanetGreetingSelector.YesNoMatches(kv.Value,
                            PlanetGreetingSelector.StarInBattleWith(star, kv.Key)))
                        return false;

            return true;
        }

        private static bool MatchesFactionsGlobal(GreetingRule r, GalaxyData galaxy)
        {
            if (r.FactionDefeated != null)
                foreach (var kv in r.FactionDefeated)
                    if (!PlanetGreetingSelector.YesNoMatches(kv.Value,
                            PlanetGreetingSelector.IsFactionDefeated(galaxy, kv.Key)))
                        return false;

            if (!string.IsNullOrEmpty(r.AnyMajorFactionDefeated))
            {
                bool any = galaxy?.DefeatedFactions != null && galaxy.DefeatedFactions.Count > 0;
                if (!PlanetGreetingSelector.YesNoMatches(r.AnyMajorFactionDefeated, any)) return false;
            }
            return true;
        }

        private static bool MatchesShipGoods(GreetingRule r, ShipData ship)
        {
            // Есть ли груз в трюме (любой).
            bool hasGoods = HasAnyCargo(ship);
            if (!PlanetGreetingSelector.YesNoMatches(r.ShipHaveGoods, hasGoods)) return false;
            // Кванты Cnt/TypeCnt считать пока по «главному» стеку — простая аппроксимация.
            if (r.ShipGoodsCnt != null || r.ShipGoodsTypeCnt != null)
            {
                int cnt = TotalCargo(ship);
                int types = CargoTypeCount(ship);
                if (!PlanetGreetingSelector.AnyOrNull(r.ShipGoodsCnt,
                        PlanetGreetingSelector.QuantOfStock(cnt))) return false;
                if (!PlanetGreetingSelector.AnyOrNull(r.ShipGoodsTypeCnt, types.ToString())) return false;
            }
            return true;
        }

        private static bool MatchesLastPlanet(GreetingRule r, ShipData ship, ShipData player, GalaxyData galaxy)
        {
            var last = ResolvePlanet(ship?.LastPlanetUid, galaxy);
            bool anyLp = r.LastPlanetRace != null
                      || !string.IsNullOrEmpty(r.LastPlanetRaceIsPlayerRace)
                      || !string.IsNullOrEmpty(r.LastPlanetRaceIsShipRace)
                      || !string.IsNullOrEmpty(r.LastPlanetInCurStar)
                      || !string.IsNullOrEmpty(r.LastPlanetIsHomePlanet)
                      || r.LastPlanetGovernment != null
                      || r.LastPlanetEconomy != null
                      || r.LastPlanetRelations != null;
            if (!anyLp) return true;
            if (last == null) return false;

            if (!PlanetGreetingSelector.AnyOrNull(r.LastPlanetRace, last.Race)) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.LastPlanetRaceIsPlayerRace,
                    PlanetGreetingSelector.RacesEqual(last.Race, player?.Race))) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.LastPlanetRaceIsShipRace,
                    PlanetGreetingSelector.RacesEqual(last.Race, ship.Race))) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.LastPlanetInCurStar,
                    last.ParentStar != null && last.ParentStar.Uid == ship.CurrentStarUid)) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.LastPlanetIsHomePlanet,
                    !string.IsNullOrEmpty(ship.HomePlanetUid) && last.Uid == ship.HomePlanetUid)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.LastPlanetGovernment, last.Settlement?.Government)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.LastPlanetEconomy, last.Settlement?.EconomyType)) return false;
            if (r.LastPlanetRelations != null && r.LastPlanetRelations.Length > 0)
            {
                string lvl = player != null
                    ? PlanetGreetingSelector.RelationLevelName(Relations.GetLevelToPlayer(last))
                    : "Normal";
                if (!PlanetGreetingSelector.Contains(r.LastPlanetRelations, lvl)) return false;
            }
            return true;
        }

        private static bool MatchesHomePlanet(GreetingRule r, ShipData ship, GalaxyData galaxy)
        {
            var home = ResolvePlanet(ship?.HomePlanetUid, galaxy);
            if (!string.IsNullOrEmpty(r.HomePlanetInCurStar))
            {
                bool inCur = home?.ParentStar != null && home.ParentStar.Uid == ship?.CurrentStarUid;
                if (!PlanetGreetingSelector.YesNoMatches(r.HomePlanetInCurStar, inCur)) return false;
            }
            if (!string.IsNullOrEmpty(r.HomePlanetInToStar))
            {
                // ToStar в контексте корабля = NextStarUid; если его нет — считаем «нет».
                bool inTo = home?.ParentStar != null && !string.IsNullOrEmpty(ship?.NextStarUid)
                            && home.ParentStar.Uid == ship.NextStarUid;
                if (!PlanetGreetingSelector.YesNoMatches(r.HomePlanetInToStar, inTo)) return false;
            }
            return true;
        }

        // ── Груз у игрока ─────────────────────────────────────────────
        private static bool MatchesPlayerGoods(GreetingRule r, ShipData player)
        {
            if (string.IsNullOrEmpty(r.PlayerHaveGoods)
                && (r.PlayerGoodsCnt == null || r.PlayerGoodsCnt.Length == 0)
                && (r.PlayerGoodsTypeCnt == null || r.PlayerGoodsTypeCnt.Length == 0))
                return true;

            bool has = player != null && CargoUtils.HasAnyCargo(player);
            if (!PlanetGreetingSelector.YesNoMatches(r.PlayerHaveGoods, has)) return false;

            if (r.PlayerGoodsCnt != null && r.PlayerGoodsCnt.Length > 0)
            {
                int total = 0;
                if (player != null) foreach (var (_, amt) in CargoUtils.GetAllCargo(player)) total += amt;
                if (!PlanetGreetingSelector.AnyOrNull(r.PlayerGoodsCnt,
                        PlanetGreetingSelector.QuantOfStock(total))) return false;
            }
            if (r.PlayerGoodsTypeCnt != null && r.PlayerGoodsTypeCnt.Length > 0)
            {
                int types = player != null ? CargoUtils.GetAllCargo(player).Count : 0;
                if (!PlanetGreetingSelector.AnyOrNull(r.PlayerGoodsTypeCnt, types.ToString())) return false;
            }
            return true;
        }

        // ── Товар на LastPlanet собеседника ───────────────────────────
        private static bool MatchesLastPlanetGoods(GreetingRule r, ShipData ship, GalaxyData galaxy, GalaxyConfig cfg)
        {
            bool needGoods = r.LastPlanetGoodsCnt != null
                          || r.LastPlanetGoodsSale != null
                          || r.LastPlanetGoodsBuy != null;
            if (!needGoods) return true;
            if (string.IsNullOrEmpty(r.Goods)) return true;

            var last = ResolvePlanet(ship?.LastPlanetUid, galaxy);
            var shop = last?.Settlement?.Shop;
            if (shop?.Goods == null || !shop.Goods.TryGetValue(r.Goods, out var entry) || entry == null) return false;
            var basePrice = PlanetGreetingSelector.LookupBasePrice(cfg, r.Goods);
            if (!PlanetGreetingSelector.AnyOrNull(r.LastPlanetGoodsCnt,
                    PlanetGreetingSelector.QuantOfStock(entry.Stock))) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.LastPlanetGoodsSale,
                    PlanetGreetingSelector.QuantOfPrice(entry.BuyPrice, basePrice))) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.LastPlanetGoodsBuy,
                    PlanetGreetingSelector.QuantOfPrice(entry.SellPrice, basePrice))) return false;
            return true;
        }

        // ── LastPlanetDistToShipInTurn: 1..4 ──────────────────────────
        private static bool MatchesLastPlanetDistance(GreetingRule r, ShipData ship, GalaxyData galaxy)
        {
            if (string.IsNullOrEmpty(r.LastPlanetDistToShipInTurn)) return true;
            var last = ResolvePlanet(ship?.LastPlanetUid, galaxy);
            if (last?.ParentStar == null) return false;
            int turns = EstimateTurnsBetweenStars(last.ParentStar, ResolveStar(ship?.CurrentStarUid, galaxy), ship);
            string actual = turns >= 4 ? "4" : turns.ToString();
            return string.Equals(r.LastPlanetDistToShipInTurn, actual, System.StringComparison.OrdinalIgnoreCase);
        }

        private static int EstimateTurnsBetweenStars(StarData a, StarData b, ShipData ship)
        {
            if (a == null || b == null) return 4;
            if (a == b) return 0;
            float dist = Vector2.Distance(a.Position, b.Position);
            int jumpRange = SRG.Equipment.EquipmentSystem.GetJumpRange(ship);
            if (jumpRange <= 0) jumpRange = 30;
            int hops = Mathf.Max(1, Mathf.CeilToInt(dist / jumpRange));
            return hops + 1; // ход на гиперпрыжок + ход на выход
        }

        // ── Ship vs Player: RankShipWithPlayer / StrengthShipWithPlayer /
        //    PlayerIsShipBad / ShipFlyToPlayer / PlayerFlyToShip / ShipMayScanPlayer.
        //    ShipBad* / PlayerAttackGoodShip / ShipNeedInItem — не гейтим (TODO: интеграция с NpcBrain).
        private static bool MatchesShipVsPlayer(GreetingRule r, ShipData ship, ShipData player)
        {
            if (!string.IsNullOrEmpty(r.PlayerIsShipBad))
            {
                bool bad = false;
                var mgr = OwnerRaceRelationsManager.Instance;
                if (mgr != null && player != null && ship != null)
                {
                    int rel = mgr.GetFactionRelation(player.Owner, ship.Owner, player.Race, ship.Race);
                    bad = rel <= OwnerRaceRelationsManager.HOSTILE_MAX;
                }
                if (!PlanetGreetingSelector.YesNoAnyMatches(r.PlayerIsShipBad, bad)) return false;
            }

            if (!string.IsNullOrEmpty(r.PlayerFlyToShip))
            {
                var pShip = PlayerShip.Instance;
                bool flying = pShip != null && ship != null &&
                              !string.IsNullOrEmpty(pShip.FollowShipUid) && pShip.FollowShipUid == ship.Uid;
                if (!PlanetGreetingSelector.YesNoAnyMatches(r.PlayerFlyToShip, flying)) return false;
            }

            if (!string.IsNullOrEmpty(r.ShipFlyToPlayer))
            {
                // Прокси-эвристика: корабль в состоянии атаки/партнёрства на игрока.
                bool chasing = false;
                if (ship != null && player != null)
                {
                    bool partnerOfPlayer = string.Equals(ship.PartnerLeaderUid, player.Uid, System.StringComparison.Ordinal);
                    bool partnerTargetsPlayer = string.Equals(ship.PartnerOrderTargetUid, player.Uid, System.StringComparison.Ordinal);
                    bool recentAttacker = string.Equals(ship.LastAttackerUid, player.Uid, System.StringComparison.Ordinal);
                    chasing = partnerOfPlayer || partnerTargetsPlayer || recentAttacker;
                }
                if (!PlanetGreetingSelector.YesNoAnyMatches(r.ShipFlyToPlayer, chasing)) return false;
            }

            if (!string.IsNullOrEmpty(r.ShipMayScanPlayer))
            {
                bool ok = CanShipScanPlayer(ship, player);
                if (!PlanetGreetingSelector.YesNoAnyMatches(r.ShipMayScanPlayer, ok)) return false;
            }

            if (!string.IsNullOrEmpty(r.RankShipWithPlayer))
            {
                string q = QuantOfRankDiff(ship, player);
                if (!string.Equals(r.RankShipWithPlayer, q, System.StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (!string.IsNullOrEmpty(r.StrengthShipWithPlayer))
            {
                string q = QuantOfStrengthRatio(ship, player);
                if (!string.Equals(r.StrengthShipWithPlayer, q, System.StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        // ── InFear: единая точка правды — NpcBrain.InFear (пересчитывается каждым Tick). ──
        // До июля 2026 здесь считался ad-hoc «hostile > mine * 1.2»; теперь используем тот же
        // флаг, что смотрят FearDropService / JointAttackService / EvaluateSituation, — чтобы
        // все fear-эффекты (реплика/сброс груза/отказ в совместной атаке/бегство) срабатывали
        // синхронно. Fallback на false, если Brain ещё не инициализирован (спавн-момент).
        private static bool MatchesFear(GreetingRule r, ShipData ship, StarData star)
        {
            if (r.InFear == null || r.InFear.Length == 0) return true;
            bool fear = ship?.Brain?.InFear ?? false;
            return PlanetGreetingSelector.AnyOrNull(r.InFear, fear ? "Yes" : "No");
        }

        // ── Утилиты ────────────────────────────────────────────────────
        private static string QuantOfStrength(float s)
        {
            if (s <= 0f)   return "Mini";
            if (s < 100f)  return "Small";
            if (s < 300f)  return "Average";
            if (s < 800f)  return "Big";
            return "Huge";
        }

        private static string QuantOfHullPct(ShipData s)
        {
            if (s == null || s.MaxHull <= 0) return "Mini";
            float pct = s.CurrentHull / (float)s.MaxHull;
            if (pct < 0.2f) return "Mini";
            if (pct < 0.4f) return "Small";
            if (pct < 0.7f) return "Average";
            if (pct < 0.9f) return "Big";
            return "Huge";
        }

        private static string QuantOfRankDiff(ShipData ship, ShipData player)
        {
            int shipIdx = RankIndex(ship);
            int playerIdx = RankIndex(player);
            int diff = shipIdx - playerIdx;
            if (diff <= -4) return "Mini";
            if (diff <= -1) return "Small";
            if (diff == 0)  return "Average";
            if (diff <= 3)  return "Big";
            return "Huge";
        }

        private static int RankIndex(ShipData s)
        {
            if (s == null) return 0;
            string rank = PlayerGreetingProfile.ResolveRank(s);
            // Индекс в стандартной лестнице (снизу вверх).
            var ladder = new[] { "Rookie", "Cadet", "Pilot", "Wingman", "Leader", "Ace", "Commander", "Admiral" };
            for (int i = 0; i < ladder.Length; i++)
                if (string.Equals(rank, ladder[i], System.StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        private static string QuantOfStrengthRatio(ShipData ship, ShipData player)
        {
            float a = ship != null ? NpcBrain.CalculateStrength(ship) : 0f;
            float b = player != null ? NpcBrain.CalculateStrength(player) : 0f;
            if (b < 0.001f) return "Huge";
            float r = a / b;
            if (r < 0.5f) return "Mini";
            if (r < 0.9f) return "Small";
            if (r < 1.3f) return "Average";
            if (r < 2.0f) return "Big";
            return "Huge";
        }

        /// <summary>Диалоговое условие <c>ShipMayScanPlayer</c>: сканер корабля достаточно
        /// мощный, чтобы пробить щит игрока. У сканера дальности нет — только сила (Power, %),
        /// сравнивается с BlockPercent щита игрока. Если щита нет — любой рабочий сканер проходит.</summary>
        private static bool CanShipScanPlayer(ShipData ship, ShipData player)
        {
            if (ship == null || player == null) return false;
            float scannerPow = SRG.Equipment.EquipmentSystem.GetScannerPower(ship);
            if (scannerPow <= 0f) return false;

            var shield = SRG.Equipment.EquipmentSystem.GetEquipped(player, SRG.Equipment.SlotKeys.Shield);
            float shieldBlock = (shield != null && shield.IsWorking) ? shield.GetParam("BlockPercent", 0f) : 0f;
            return scannerPow >= shieldBlock;
        }

        // ── Оценка «оставшихся ходов» текущего приказа ────────────────
        private static bool MatchesShipOrder(GreetingRule r, ShipData ship, StarData star)
        {
            if (string.IsNullOrEmpty(r.ShipTurnBeforeEndOrder)) return true;
            string actual = QuantOfOrderTurnsLeft(ship, star);
            return string.Equals(r.ShipTurnBeforeEndOrder, actual, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Грубая оценка оставшихся ходов приказа: считаем по расстоянию до текущей цели.
        /// Возвращает "1".."9", "Many" или "Far".</summary>
        private static string QuantOfOrderTurnsLeft(ShipData ship, StarData star)
        {
            if (ship == null) return "Many";
            // Гиперпрыжок или межзвёздный перелёт — считаем «Far»: цель не в текущей системе.
            if (!string.IsNullOrEmpty(ship.NextStarUid) && ship.NextStarUid != ship.CurrentStarUid)
                return "Far";
            if (ship.HyperjumpPhase != HyperjumpPhase.None) return "Far";

            Vector2 targetPos;
            if (!string.IsNullOrEmpty(ship.NextPlanetUid) && star != null)
            {
                var planet = star.Planets.Find(p => p.Uid == ship.NextPlanetUid);
                targetPos = planet != null ? PlanetPos(planet) : ship.TargetPosition;
            }
            else targetPos = ship.TargetPosition;

            float dist = Vector2.Distance(ship.Position, targetPos);
            float perTurn = Mathf.Max(0.01f, ship.ActualSpeed);
            int turns = Mathf.Clamp(Mathf.CeilToInt(dist / perTurn), 0, 999);
            if (turns >= 10) return "Many";
            if (turns <= 0)  return "1";
            return turns.ToString();
        }

        private static Vector2 PlanetPos(PlanetData p)
        {
            if (p == null) return Vector2.zero;
            float a = p.CurrentAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a) * p.OrbitRadius, Mathf.Sin(a) * p.OrbitRadius);
        }

        // ── ShipNeedInItem / ItemType ──────────────────────────────────
        private static bool MatchesShipNeedItem(GreetingRule r, ShipData ship, StarData star)
        {
            if (string.IsNullOrEmpty(r.ShipNeedInItem) && (r.ItemType == null || r.ItemType.Length == 0))
                return true;

            var item = ResolveTargetItemShip(ship, star);
            bool need = item != null;
            if (!string.IsNullOrEmpty(r.ShipNeedInItem)
                && !PlanetGreetingSelector.YesNoAnyMatches(r.ShipNeedInItem, need)) return false;

            if (r.ItemType != null && r.ItemType.Length > 0)
            {
                if (item == null) return false;
                string category = ResolveItemCategory(item);
                if (!PlanetGreetingSelector.AnyOrNull(r.ItemType, category)) return false;
            }
            return true;
        }

        private static ShipData ResolveTargetItemShip(ShipData ship, StarData star)
        {
            if (ship == null || star == null) return null;
            var uid = ship.BoardingTargetUid;
            if (string.IsNullOrEmpty(uid) && ship.PulledQueue != null && ship.PulledQueue.Count > 0)
                uid = ship.PulledQueue[0];
            if (string.IsNullOrEmpty(uid)) return null;
            var target = star.FindShip(uid, aliveOnly: true);
            return (target != null && target.IsItem) ? target : null;
        }

        private static string ResolveItemCategory(ShipData itemShip)
        {
            if (itemShip?.Inventory == null) return null;
            foreach (var it in itemShip.Inventory.Items)
                if (it != null && !string.IsNullOrEmpty(it.Category)) return it.Category;
            foreach (var kv in itemShip.Inventory.Stacks)
                if (kv.Value != null && !string.IsNullOrEmpty(kv.Value.Category)) return kv.Value.Category;
            return null;
        }

        // ── ToShip*: цель, за которой летит собеседник ─────────────────
        private static bool MatchesToShip(GreetingRule r, ShipData ship, ShipData player, StarData star)
        {
            bool needToShip = r.ToShipType != null || r.ToShipRace != null || r.ToShipRelations != null
                              || !string.IsNullOrEmpty(r.ToShipBad) || !string.IsNullOrEmpty(r.ToShipInPlanet);
            if (!needToShip) return true;

            var target = ResolveShipChaseTarget(ship, star);
            if (target == null) return false;

            if (!PlanetGreetingSelector.AnyOrNull(r.ToShipType, target.ShipTypeId)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.ToShipRace, target.Race)) return false;

            if (r.ToShipRelations != null && r.ToShipRelations.Length > 0)
            {
                string level = player != null
                    ? PlanetGreetingSelector.RelationLevelName(Relations.GetLevelToPlayer(target))
                    : "Normal";
                // "War" синоним враждебных отношений.
                if (!PlanetGreetingSelector.ContainsRelation(r.ToShipRelations, level)
                    && !(ContainsCI(r.ToShipRelations, "War")
                         && string.Equals(level, "Enemy", System.StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            if (!string.IsNullOrEmpty(r.ToShipBad))
            {
                var mgr = OwnerRaceRelationsManager.Instance;
                bool hostile = mgr != null && mgr.AreHostile(ship, target);
                if (!PlanetGreetingSelector.YesNoAnyMatches(r.ToShipBad, hostile)) return false;
            }

            if (!string.IsNullOrEmpty(r.ToShipInPlanet))
            {
                bool onPlanet = !string.IsNullOrEmpty(target.LandedPlanetUid);
                if (!PlanetGreetingSelector.YesNoAnyMatches(r.ToShipInPlanet, onPlanet)) return false;
            }
            return true;
        }

        private static bool ContainsCI(string[] arr, string val)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (string.Equals(arr[i], val, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>UID корабля-цели преследования собеседником: сначала боевая цель Brain,
        /// иначе — цель приказа партнёра, иначе — недавний обидчик.</summary>
        private static ShipData ResolveShipChaseTarget(ShipData ship, StarData star)
        {
            if (ship == null || star == null) return null;
            string uid = ship.Brain?.GetCombatTargetUid();
            if (string.IsNullOrEmpty(uid) && ship.PartnerOrder == PartnerOrderKind.Attack)
                uid = ship.PartnerOrderTargetUid;
            if (string.IsNullOrEmpty(uid)) return null;
            return star.FindShip(uid, aliveOnly: true);
        }

        // ── ShipBad*: агрессор, преследующий собеседника ───────────────
        private static bool MatchesShipBad(GreetingRule r, ShipData ship, StarData star)
        {
            bool needBad = !string.IsNullOrEmpty(r.ShipBadFlyToShip)
                        || (r.ShipBadType != null && r.ShipBadType.Length > 0)
                        || (r.ShipBadRace != null && r.ShipBadRace.Length > 0)
                        || !string.IsNullOrEmpty(r.ShipBadTurnBeforeEndOrder);
            if (!needBad) return true;

            var aggressor = FindPursuer(ship, star);
            bool hasBad = aggressor != null;
            if (!string.IsNullOrEmpty(r.ShipBadFlyToShip)
                && !PlanetGreetingSelector.YesNoAnyMatches(r.ShipBadFlyToShip, hasBad)) return false;

            if ((r.ShipBadType != null && r.ShipBadType.Length > 0)
                || (r.ShipBadRace != null && r.ShipBadRace.Length > 0)
                || !string.IsNullOrEmpty(r.ShipBadTurnBeforeEndOrder))
            {
                if (aggressor == null) return false;
                if (!PlanetGreetingSelector.AnyOrNull(r.ShipBadType, aggressor.ShipTypeId)) return false;
                if (!PlanetGreetingSelector.AnyOrNull(r.ShipBadRace, aggressor.Race)) return false;
                if (!string.IsNullOrEmpty(r.ShipBadTurnBeforeEndOrder))
                {
                    string q = QuantOfOrderTurnsLeft(aggressor, star);
                    if (!string.Equals(r.ShipBadTurnBeforeEndOrder, q, System.StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            return true;
        }

        /// <summary>Корабль в системе, у которого CombatTargetUid = ship (плюс совместимость
        /// через LastAttackerUid=атакующий, если атакующий ещё в системе).</summary>
        private static ShipData FindPursuer(ShipData ship, StarData star)
        {
            if (ship == null || star == null) return null;
            foreach (var other in star.Ships)
            {
                if (other == null || other == ship || other.CurrentHull <= 0) continue;
                string t = other.Brain?.GetCombatTargetUid();
                if (!string.IsNullOrEmpty(t) && t == ship.Uid) return other;
            }
            // Fallback: недавний агрессор ещё жив в системе.
            if (!string.IsNullOrEmpty(ship.LastAttackerUid))
            {
                var recent = star.FindShip(ship.LastAttackerUid, aliveOnly: true);
                if (recent != null) return recent;
            }
            return null;
        }

        // ── PlayerAttackGoodShip: игрок атакует союзника собеседника ──
        private static bool MatchesPlayerAttackGoodShip(GreetingRule r, ShipData ship, ShipData player, StarData star)
        {
            if (string.IsNullOrEmpty(r.PlayerAttackGoodShip)) return true;
            bool hit = false;
            if (ship != null && player != null && star != null)
            {
                string targetUid = PlayerShip.Instance?.FollowShipUid ?? player.ManualShootTargetUid;
                if (!string.IsNullOrEmpty(targetUid))
                {
                    var target = star.FindShip(targetUid, aliveOnly: true);
                    var mgr = OwnerRaceRelationsManager.Instance;
                    if (target != null && mgr != null)
                    {
                        // «Хороший» с точки зрения собеседника = не враждебный ему.
                        bool friendly = !mgr.AreHostile(ship, target);
                        hit = friendly;
                    }
                }
            }
            return PlanetGreetingSelector.YesNoAnyMatches(r.PlayerAttackGoodShip, hit);
        }

        private static bool MatchesFlyType(GreetingRule r, ShipData ship)
        {
            if (r.FlyType == null || r.FlyType.Length == 0) return true;
            string actual = InferFlyType(ship);
            return PlanetGreetingSelector.Contains(r.FlyType, actual)
                   || PlanetGreetingSelector.Contains(r.FlyType, "Any");
        }

        // ── ToPlanet (переиспользуем в целом ту же логику что и у планетного селектора) ──

        private static bool NeedsToPlanet(GreetingRule r)
        {
            return r.ToPlanetRace != null
                || !string.IsNullOrEmpty(r.ToPlanetInCurStar)
                || !string.IsNullOrEmpty(r.ToPlanetRaceIsCurPlanetRace)
                || !string.IsNullOrEmpty(r.ToPlanetRaceIsPlayerRace)
                || !string.IsNullOrEmpty(r.ToPlanetRaceIsShipRace)
                || r.ToPlanetGovernment != null || r.ToPlanetEconomy != null
                || !string.IsNullOrEmpty(r.ToPlanetGoodsPermit)
                || r.ToPlanetGoodsCnt != null || r.ToPlanetGoodsSale != null || r.ToPlanetGoodsBuy != null
                || r.ToPlanetRelation != null
                || !string.IsNullOrEmpty(r.ToStarInBattle)
                || !string.IsNullOrEmpty(r.ToStarControlByEnemy)
                || !string.IsNullOrEmpty(r.ToPlanetIsHomePlanet)
                || !string.IsNullOrEmpty(r.ToPlanetIsLastPlanet)
                || (r.ShipsInToStar != null && r.ShipsInToStar.Count > 0)
                || (r.StarBattleWithInToStar != null && r.StarBattleWithInToStar.Count > 0);
        }

        private static bool TryPickToPlanet(GreetingRule r, ShipData ship, ShipData player,
                                            GalaxyData galaxy, GalaxyConfig cfg, System.Random rng,
                                            out PlanetData picked, out StarData pickedStar)
        {
            picked = null; pickedStar = null;
            if (galaxy?.PlanetsMap == null) return false;
            List<PlanetData> candidates = null;
            // Если корабль летит на планету — она и есть первичный кандидат.
            var next = ResolvePlanet(ship?.NextPlanetUid, galaxy);
            if (next != null && MatchesToPlanet(r, next, next.ParentStar, ship, player, galaxy, cfg))
            {
                picked = next;
                pickedStar = next.ParentStar;
                return true;
            }
            foreach (var kv in galaxy.PlanetsMap)
            {
                var to = kv.Value; if (to == null) continue;
                var toStar = to.ParentStar; if (toStar == null) continue;
                if (!MatchesToPlanet(r, to, toStar, ship, player, galaxy, cfg)) continue;
                candidates ??= new List<PlanetData>();
                candidates.Add(to);
            }
            if (candidates == null) return false;
            picked = candidates[rng.Next(candidates.Count)];
            pickedStar = picked.ParentStar;
            return true;
        }

        private static bool MatchesToPlanet(GreetingRule r, PlanetData to, StarData toStar,
                                            ShipData ship, ShipData player, GalaxyData galaxy, GalaxyConfig cfg)
        {
            var curStar = ResolveStar(ship?.CurrentStarUid, galaxy);
            if (!PlanetGreetingSelector.AnyOrNull(r.ToPlanetRace, to.Race)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.ToPlanetGovernment, to.Settlement?.Government)) return false;
            if (!PlanetGreetingSelector.AnyOrNull(r.ToPlanetEconomy, to.Settlement?.EconomyType)) return false;

            if (!PlanetGreetingSelector.YesNoMatches(r.ToPlanetInCurStar, curStar != null && toStar == curStar)) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.ToPlanetRaceIsCurPlanetRace,
                    curStar != null && PlanetGreetingSelector.RacesEqual(to.Race, curStar.Race))) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.ToPlanetRaceIsPlayerRace,
                    PlanetGreetingSelector.RacesEqual(to.Race, player?.Race))) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.ToPlanetRaceIsShipRace,
                    PlanetGreetingSelector.RacesEqual(to.Race, ship.Race))) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.ToPlanetIsHomePlanet,
                    !string.IsNullOrEmpty(ship.HomePlanetUid) && to.Uid == ship.HomePlanetUid)) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.ToPlanetIsLastPlanet,
                    !string.IsNullOrEmpty(ship.LastPlanetUid) && to.Uid == ship.LastPlanetUid)) return false;

            if (r.ToPlanetRelation != null && r.ToPlanetRelation.Length > 0)
            {
                string lvl = player != null
                    ? PlanetGreetingSelector.RelationLevelName(Relations.GetLevelToPlayer(to))
                    : "Normal";
                if (!PlanetGreetingSelector.Contains(r.ToPlanetRelation, lvl)) return false;
            }

            if (!PlanetGreetingSelector.YesNoAnyMatches(r.ToStarInBattle,
                    PlanetGreetingSelector.StarInBattle(toStar))) return false;
            if (!PlanetGreetingSelector.YesNoMatches(r.ToStarControlByEnemy,
                    PlanetGreetingSelector.StarControlledByEnemy(toStar, player))) return false;

            if (r.ShipsInToStar != null)
                foreach (var kv in r.ShipsInToStar)
                    if (!PlanetGreetingSelector.Contains(kv.Value,
                            PlanetGreetingSelector.QuantOfShipCount(
                                PlanetGreetingSelector.ShipsInStarBySide(toStar.Uid, kv.Key))))
                        return false;

            if (r.StarBattleWithInToStar != null)
                foreach (var kv in r.StarBattleWithInToStar)
                    if (!PlanetGreetingSelector.YesNoMatches(kv.Value,
                            PlanetGreetingSelector.StarInBattleWith(toStar, kv.Key)))
                        return false;

            bool needGoods = !string.IsNullOrEmpty(r.ToPlanetGoodsPermit)
                          || r.ToPlanetGoodsCnt != null
                          || r.ToPlanetGoodsSale != null
                          || r.ToPlanetGoodsBuy != null;
            if (needGoods && !string.IsNullOrEmpty(r.Goods))
            {
                var shop = to.Settlement?.Shop;
                if (shop?.Goods == null || !shop.Goods.TryGetValue(r.Goods, out var entry) || entry == null)
                    return false;
                bool contraband = cfg != null && !SRG.Economy.TradeSystem.IsLegal(to, r.Goods, cfg);
                if (!PlanetGreetingSelector.YesNoAnyMatches(r.ToPlanetGoodsPermit, contraband)) return false;
                var basePrice = PlanetGreetingSelector.LookupBasePrice(cfg, r.Goods);
                if (!PlanetGreetingSelector.AnyOrNull(r.ToPlanetGoodsCnt,
                        PlanetGreetingSelector.QuantOfStock(entry.Stock))) return false;
                if (!PlanetGreetingSelector.AnyOrNull(r.ToPlanetGoodsSale,
                        PlanetGreetingSelector.QuantOfPrice(entry.BuyPrice, basePrice))) return false;
                if (!PlanetGreetingSelector.AnyOrNull(r.ToPlanetGoodsBuy,
                        PlanetGreetingSelector.QuantOfPrice(entry.SellPrice, basePrice))) return false;
            }
            return true;
        }

        // ── Утилиты ────────────────────────────────────────────────

        private static StarData ResolveCurrentStar(ShipData ship, GalaxyData galaxy)
            => ResolveStar(ship?.CurrentStarUid, galaxy);

        private static StarData ResolveStar(string uid, GalaxyData galaxy)
            => galaxy != null && !string.IsNullOrEmpty(uid) &&
               galaxy.StarsMap != null && galaxy.StarsMap.TryGetValue(uid, out var s) ? s : null;

        private static PlanetData ResolvePlanet(string uid, GalaxyData galaxy)
            => galaxy != null && !string.IsNullOrEmpty(uid) &&
               galaxy.PlanetsMap != null && galaxy.PlanetsMap.TryGetValue(uid, out var p) ? p : null;

        private static string InferFlyType(ShipData ship)
        {
            if (ship == null) return "Any";
            if (!string.IsNullOrEmpty(ship.BoardingTargetUid)) return "ToItem";
            if (!string.IsNullOrEmpty(ship.PartnerOrderTargetUid) &&
                (ship.PartnerOrder == PartnerOrderKind.Attack)) return "ToShip";
            if (!string.IsNullOrEmpty(ship.NextPlanetUid)) return "ToPlanet";
            if (!string.IsNullOrEmpty(ship.NextStarUid))   return "ToStar";
            if (!string.IsNullOrEmpty(ship.LandedPlanetUid)) return "Landed";
            return "Any";
        }

        private static bool HasAnyCargo(ShipData s) => CargoUtils.HasAnyCargo(s);

        private static int TotalCargo(ShipData s)
        {
            int total = 0;
            foreach (var (_, amt) in CargoUtils.GetAllCargo(s)) total += amt;
            return total;
        }

        private static int CargoTypeCount(ShipData s) => CargoUtils.GetAllCargo(s).Count;
    }
}
