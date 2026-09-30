using UnityEngine;
using System.Collections.Generic;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Ships.Services
{
    /// <summary>Причина, по которой TryHire отказал (или Accepted при успехе).
    /// Каждая причина мапится на реплику отказа в диалоге по ключу Partner.Refusal.&lt;Reason&gt;.</summary>
    public enum HireRefusalReason
    {
        Accepted = 0,
        ShipTypeNotHireable,
        AlreadyHasLeader,
        LeadersPartyFull,
        AttitudeTooLow,
        AggressionMismatch,
        PowerTooHigh,
        NotEnoughMoney,
        OfferTooLow,
    }

    /// <summary>Причина расторжения контракта (см. PartnerService.Break).</summary>
    public enum BreakReason
    {
        TermEnd,
        PlayerDismiss,
        Riot,
    }

    /// <summary>Результат вызова TryHire: Reason=Accepted и Fee=цена, либо Reason=причина отказа и Fee=0.</summary>
    public struct HireResult
    {
        public HireRefusalReason Reason;
        public int Fee;
        public bool Accepted => Reason == HireRefusalReason.Accepted;

        public static HireResult Ok(int fee) => new() { Reason = HireRefusalReason.Accepted, Fee = fee };
        public static HireResult Refuse(HireRefusalReason r) => new() { Reason = r, Fee = 0 };
    }

    /// <summary>
    /// Единая точка входа для системы партнёрства. Пять API-методов:
    ///   Preview(target, hirer, offer) — без побочек, показывает согласится ли и по какой цене.
    ///   TryHire (target, hirer, offer) — списывает деньги, устанавливает связь. Возвращает HireResult.
    ///   Break   (follower, reason)    — снимает связь в обе стороны.
    ///   MaxPartners(hirer)            — сколько может держать (учитывает Leadership).
    ///   CheckBreak(follower, turn)    — проверить, не пора ли автоматически расторгнуть.
    /// Всё остальное (загрузка конфига, синхронизация FollowerUids) — внутри.
    /// </summary>
    public static class PartnerService
    {
        // ── Публичный API ────────────────────────────────────────────────────────

        /// <summary>Проверяет, можно ли нанять target за offer; ничего не меняет.
        /// Reason=Accepted → Fee=реальная цена контракта (Fee ≤ offer). Reason=…→ причина отказа.</summary>
        public static HireResult Preview(ShipData target, ShipData hirer, int offer)
        {
            var cfg = GetConfig();
            if (cfg == null || target == null || hirer == null)
                return HireResult.Refuse(HireRefusalReason.ShipTypeNotHireable);

            // 1. Тип должен быть в whitelist.
            if (!cfg.PartnerableShipTypes.TryGetValue(target.ShipTypeId ?? string.Empty, out var typeCfg))
                return HireResult.Refuse(HireRefusalReason.ShipTypeNotHireable);

            // 2. Уже есть лидер.
            if (!string.IsNullOrEmpty(target.PartnerLeaderUid))
                return HireResult.Refuse(HireRefusalReason.AlreadyHasLeader);

            // 3. У нанимателя свита не переполнена.
            int max = MaxPartners(hirer);
            int current = hirer.PartnerFollowerUids?.Count ?? 0;
            if (current >= max)
                return HireResult.Refuse(HireRefusalReason.LeadersPartyFull);

            // 4. Attitude ≥ required(- Leadership бонус).
            int leadership = GetLeadership(hirer);
            float required = typeCfg.BaseAttitudeRequired - leadership * cfg.Global.AttitudePerLeadership;
            int attitude = OwnerRaceRelationsManager.Instance?.GetRelation(target, hirer) ?? 50;
            if (attitude < required)
                return HireResult.Refuse(HireRefusalReason.AttitudeTooLow);

            // 5. Personality-требования (пока — только Aggression min/max).
            //
            // СЕЙЧАС ОТКЛЮЧЕНО: во всех типах PartnersConfig.json стоит "Requires": {},
            // поэтому ветка не срабатывает. Проверка снята намеренно — она давала отказ,
            // который игрок не мог ни на что повлиять (характер цели неизменен), и при
            // Ranger.AggressionMax=60 против профиля Default с разбросом [30,70] отсекала
            // четверть рейнджеров без объяснимой для игрока причины.
            // Вернуть = дописать AggressionMin/Max в Requires нужного типа; код готов.
            if (target.Personality != null)
            {
                if (typeCfg.Requires?.AggressionMin.HasValue == true
                    && target.Personality.Aggression < typeCfg.Requires.AggressionMin.Value)
                    return HireResult.Refuse(HireRefusalReason.AggressionMismatch);
                if (typeCfg.Requires?.AggressionMax.HasValue == true
                    && target.Personality.Aggression > typeCfg.Requires.AggressionMax.Value)
                    return HireResult.Refuse(HireRefusalReason.AggressionMismatch);
            }

            // 6. PowerRatio — цель не сильнее нанимателя больше чем в PowerRatio раз.
            float hirerPower = NpcBrain.CalculateStrength(hirer);
            float targetPower = NpcBrain.CalculateStrength(target);
            if (hirerPower > 0.001f && targetPower / hirerPower > typeCfg.PowerRatio)
                return HireResult.Refuse(HireRefusalReason.PowerTooHigh);

            // 7. Цена, которую просит цель (кошелёк нанимателя на неё не влияет).
            int feeInt = CalcFee(target, hirer);
            int minFee = Mathf.RoundToInt(cfg.Global.BasePrice * typeCfg.PriceMultiplier * cfg.Global.MinFeeRatio);

            // 8. Offer < minFee → «оскорбление».
            if (offer < minFee)
                return HireResult.Refuse(HireRefusalReason.OfferTooLow);

            // 9. Offer < fee → «не согласится за столько», но реплика та же (OfferTooLow),
            //    диалог сам разберётся: показать «предлагает {fee}» или «отказ».
            if (offer < feeInt)
                return HireResult.Refuse(HireRefusalReason.OfferTooLow);

            // 10. У нанимателя должно быть достаточно денег.
            if (hirer.Money < feeInt)
                return HireResult.Refuse(HireRefusalReason.NotEnoughMoney);

            return HireResult.Ok(feeInt);
        }

        /// <summary>Цена, которую цель просит за контракт:
        /// <c>BasePrice × PriceMultiplier × (2 − attitude/100) × (1 − charm/100 × maxReduction)</c>.
        /// <br/>
        /// Считается БЕЗ оглядки на деньги нанимателя: пилот называет свою цену, не заглядывая
        /// в чужой кошелёк. Хватит ли у игрока средств — вопрос момента найма
        /// (<see cref="Preview"/> шаг 10 и <see cref="TryHire"/>), а не торга.
        /// Возвращает 0, если конфиг или тип корабля недоступны.</summary>
        public static int CalcFee(ShipData target, ShipData hirer)
        {
            var cfg = GetConfig();
            if (cfg == null || target == null || hirer == null) return 0;
            if (!cfg.PartnerableShipTypes.TryGetValue(target.ShipTypeId ?? string.Empty, out var typeCfg))
                return 0;

            int attitude = OwnerRaceRelationsManager.Instance?.GetRelation(target, hirer) ?? 50;
            int charm = GetCharm(hirer);
            float fee = cfg.Global.BasePrice
                      * typeCfg.PriceMultiplier
                      * (1f + (100f - attitude) / 100f)
                      * (1f - charm / 100f * cfg.Global.CharmFeeReductionMax);
            return Mathf.Max(1, Mathf.RoundToInt(fee));
        }

        /// <summary>Найм: проверяет условия, при успехе списывает деньги и устанавливает связь.</summary>
        public static HireResult TryHire(ShipData target, ShipData hirer, int offer)
        {
            var result = Preview(target, hirer, offer);
            if (!result.Accepted) return result;

            var cfg = GetConfig();
            var typeCfg = cfg.PartnerableShipTypes[target.ShipTypeId];

            hirer.Money -= result.Fee;
            target.Money += result.Fee;

            int currentTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            int endTurn = typeCfg.ContractTermYears > 0
                ? currentTurn + typeCfg.ContractTermYears * Mathf.Max(1, cfg.Global.TurnsPerYear)
                : -1;

            SetPartner(target, hirer, currentTurn, endTurn);
            return result;
        }

        /// <summary>Расторгает контракт по указанной причине. Синхронизирует обе стороны.</summary>
        public static void Break(ShipData follower, BreakReason reason)
        {
            if (follower == null) return;
            string leaderUid = follower.PartnerLeaderUid;
            if (string.IsNullOrEmpty(leaderUid)) return;

            ShipData leader = FindShipInGalaxy(leaderUid);
            leader?.PartnerFollowerUids?.Remove(follower.Uid);

            follower.PartnerLeaderUid = null;
            follower.PartnerContractEndTurn = -1;
            follower.PartnerHiredOnTurn = -1;

            Debug.Log($"[PartnerService] Break {SpriteUtility.ShortId(follower.Uid)} " +
                      $"from {SpriteUtility.ShortId(leaderUid)} ({reason})");

            // Реплика в игровую консоль. Берётся из пулов Partner.*/Pirate.* — у пирата свой
            // говор («Шеф, а срок контракта-то, тю-тю»). Хардкод остаётся страховкой на случай,
            // если пул не заведён.
            string leaderName = leader?.Name ?? SpriteUtility.ShortId(leaderUid);
            string followerName = follower.Name ?? SpriteUtility.ShortId(follower.Uid);
            string brand = IsPirateType(follower) ? "Pirate" : "Partner";
            string poolKey = reason switch
            {
                BreakReason.TermEnd       => brand + ".MateTheEnd",
                BreakReason.Riot          => brand + ".MateRiot",
                BreakReason.PlayerDismiss => brand + ".MateBreak",
                _ => null,
            };
            string line = poolKey == null ? null : SRG.Dialog.DialogService.PickFromPool(poolKey);
            if (string.IsNullOrEmpty(line))
                line = reason switch
                {
                    BreakReason.TermEnd       => $"срок контракта с {leaderName} истёк. Прощай.",
                    BreakReason.Riot          => $"больше не могу терпеть — выхожу из-под {leaderName}.",
                    BreakReason.PlayerDismiss => $"контракт с {leaderName} расторгнут.",
                    _ => null,
                };
            if (line != null)
            {
                // В пулах игрок адресуется как <Player>, лидер партнёра — это он и есть.
                line = line.Replace("<Player>", leaderName).Replace("<Ranger>", leaderName);
                GameLog.Add($"[Связь] {followerName}: {line}");
            }
        }

        /// <summary>Устанавливает партнёрство. Синхронизирует список FollowerUids на лидере.
        /// currentTurn / endTurn: если endTurn=-1, контракт бессрочный.</summary>
        public static void SetPartner(ShipData follower, ShipData leader, int currentTurn, int endTurn)
        {
            if (follower == null || leader == null) return;

            // Если follower уже с кем-то — сначала разорвать.
            if (!string.IsNullOrEmpty(follower.PartnerLeaderUid) && follower.PartnerLeaderUid != leader.Uid)
                Break(follower, BreakReason.PlayerDismiss);

            follower.PartnerLeaderUid = leader.Uid;
            follower.PartnerContractEndTurn = endTurn;
            follower.PartnerHiredOnTurn = currentTurn;

            leader.PartnerFollowerUids ??= new List<string>();
            if (!leader.PartnerFollowerUids.Contains(follower.Uid))
                leader.PartnerFollowerUids.Add(follower.Uid);
        }

        /// <summary>Максимальное количество партнёров у корабля (учитывает Leadership).</summary>
        public static int MaxPartners(ShipData hirer)
        {
            var cfg = GetConfig();
            if (cfg == null || hirer == null) return 0;
            int leadership = GetLeadership(hirer);
            return cfg.Global.MaxPartnersBase + Mathf.FloorToInt(leadership * cfg.Global.MaxPartnersPerLeadership);
        }

        /// <summary>Проверяет, не пора ли автоматически расторгнуть контракт (TermEnd/Riot).
        /// Возвращает true, если Break произошёл. Player-Dismiss делает диалог напрямую.</summary>
        public static bool CheckBreak(ShipData follower, int currentTurn)
        {
            if (follower == null || string.IsNullOrEmpty(follower.PartnerLeaderUid)) return false;

            if (follower.PartnerContractEndTurn > 0 && currentTurn >= follower.PartnerContractEndTurn)
            {
                Break(follower, BreakReason.TermEnd);
                return true;
            }

            var cfg = GetConfig();
            if (cfg == null) return false;

            ShipData leader = FindShipInGalaxy(follower.PartnerLeaderUid);
            if (leader == null)
            {
                // Лидер потерян/умер — разрыв.
                Break(follower, BreakReason.PlayerDismiss);
                return true;
            }

            int attitude = OwnerRaceRelationsManager.Instance?.GetRelation(follower, leader) ?? 50;
            float discipline = follower.Personality?.Discipline ?? 50f;
            float riotThreshold = cfg.Global.RiotBaseThreshold
                                - (discipline - 50f) * cfg.Global.RiotDisciplineScale;
            if (attitude <= riotThreshold)
            {
                Break(follower, BreakReason.Riot);
                return true;
            }

            return false;
        }

        // ── Утилиты ──────────────────────────────────────────────────────────────

        /// <summary>Батч-проверка контрактов всех followers в галактике за один обход.
        /// Вызывается из <see cref="SRG.NpcAI.NpcSystem.TickAllSystems"/> раз в LoyaltyCheckPeriodTurns
        /// вместо per-ship CheckBreak в TickStar. Одна пробежка O(N) на всю галактику даёт то же,
        /// что раньше давал O(N) per-ship × M ships.</summary>
        public static int CheckBreakAll(GalaxyData galaxy, int currentTurn)
        {
            if (galaxy == null) return 0;
            int broken = 0;
            // Собираем followers в снапшот — Break может модифицировать ship.PartnerLeaderUid
            // (и лидерский FollowerUids), поэтому итерацию с изменением делаем через копию.
            _followersScratch.Clear();
            foreach (var sector in galaxy.Sectors)
                foreach (var star in sector.Stars)
                    for (int i = 0; i < star.Ships.Count; i++)
                    {
                        var s = star.Ships[i];
                        if (s.CurrentHull <= 0) continue;
                        if (string.IsNullOrEmpty(s.PartnerLeaderUid)) continue;
                        _followersScratch.Add(s);
                    }
            for (int i = 0; i < _followersScratch.Count; i++)
                if (CheckBreak(_followersScratch[i], currentTurn)) broken++;
            _followersScratch.Clear();
            return broken;
        }

        // Переиспользуемый буфер followers — избегает per-tick аллокаций.
        private static readonly List<ShipData> _followersScratch = new();

        /// <summary>Ищет корабль по UID во всей галактике. Используется для reverse-lookup лидеров.</summary>
        /// <summary>Пиратский ли это партнёр — от этого зависит, из какого семейства пулов
        /// берутся его реплики (<c>Pirate.*</c> против <c>Partner.*</c>).</summary>
        private static bool IsPirateType(ShipData ship)
            => string.Equals(ship?.ShipTypeId, "Pirate", System.StringComparison.OrdinalIgnoreCase);

        public static ShipData FindShipInGalaxy(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            var galaxy = GameWorld.GeneratedGalaxy;
            if (galaxy == null) return null;
            foreach (var sector in galaxy.Sectors)
                foreach (var star in sector.Stars)
                    foreach (var ship in star.Ships)
                        if (ship.Uid == uid) return ship;
            return null;
        }

        private static PartnersConfig GetConfig()
            => GameWorld.Context?.Config?.Partners;

        private static int GetLeadership(ShipData ship)
        {
            var cfg = GameWorld.Context?.Config?.Skills;
            return ship?.Skills?.GetEffective(SkillType.Leadership, cfg) ?? 0;
        }

        private static int GetCharm(ShipData ship)
        {
            var cfg = GameWorld.Context?.Config?.Skills;
            return ship?.Skills?.GetEffective(SkillType.Charm, cfg) ?? 0;
        }
    }
}
