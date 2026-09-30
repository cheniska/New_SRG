using UnityEngine;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Actions;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Utils;

namespace SRG.NpcAI
{
    /// <summary>Базовый класс директив. Все конкретные директивы — OwnerDirective (по Owner);
    /// адресация конкретной расе внутри Owner — через поле RaceId директивы.</summary>
    public abstract class Directive
    {
        public string Uid { get; } = System.Guid.NewGuid().ToString();
        public bool IsExpired { get; protected set; }
        public int TurnsRemaining { get; protected set; } = -1;

        public abstract string DebugName { get; }

        /// <summary>Применяется ли директива к данному кораблю.</summary>
        public abstract bool Matches(ShipData ship);

        public abstract NpcAction GetActionFor(ShipData ship, StarData star);

        public virtual void Tick(GalaxyData galaxy)
        {
            if (TurnsRemaining > 0)
            {
                TurnsRemaining--;
                if (TurnsRemaining <= 0) IsExpired = true;
            }
        }

        /// <summary>Подчиняется ли корабль директиве. Делегирует в <see cref="DisciplineCheck"/>.</summary>
        public bool ShouldObey(ShipData ship)
            => DisciplineCheck.ShouldObey(ship?.Personality, DisciplineCheck.OrderKind.FactionDirective);
    }

    /// <summary>Директива, адресованная конкретному Owner (политическому субъекту).</summary>
    public abstract class OwnerDirective : Directive
    {
        public string OwnerId { get; protected set; }
        public override bool Matches(ShipData ship) => ship?.Owner == OwnerId;
    }




    public class DirectiveAttackSystem : OwnerDirective
    {
        /// <summary>Атака закончилась. reason = "captured" | "no_enemies" | "timeout_or_prior" | "star_missing".
        /// Слушает <see cref="GalaxyNewsService"/> — публикует «атака отражена» или «система захвачена».</summary>
        public static event System.Action<DirectiveAttackSystem, string> OnResolved;

        /// <summary>Uid системы-плацдарма, откуда собирают силы для атаки. null = любой корабль стороны
        /// (глобальная директива — для мелких/дебаг сценариев).</summary>
        public string StagingStarUid { get; }
        public string TargetStarUid { get; }
        /// <summary>Если задано — директива обслуживает только эту Race (per-race ГШ).</summary>
        public string RaceId { get; }
        /// <summary>Порог: как только staging.FriendlyPower ≥ RequiredStagingPower — переход из фазы
        /// сбора в фазу удара. 0 = удар сразу.</summary>
        public float RequiredStagingPower { get; }
        public DirectiveAttackSystem(string ownerId, string targetStarUid, int durationTurns = -1,
            string stagingStarUid = null, string raceId = null,
            float requiredStagingPower = 0f)
        {
            OwnerId = ownerId;
            StagingStarUid = stagingStarUid;
            TargetStarUid = targetStarUid;
            RaceId = raceId;
            TurnsRemaining = durationTurns;
            RequiredStagingPower = requiredStagingPower;
        }

        public override string DebugName =>
            $"AttackSystem({(StagingStarUid != null ? StagingStarUid[..6] + "→" : "*→")}{TargetStarUid[..6]})";

        public override bool Matches(ShipData ship)
        {
            if (ship?.Owner != OwnerId) return false;
            if (!string.IsNullOrEmpty(RaceId) && ship.Race != RaceId) return false;
            // К директиве привлекаются только боевые и наёмники (военные+вольные пилоты),
            // мирные и пираты сохраняют своё поведение.
            var cls = NpcBrain.ResolveCombatClass(ship.ShipTypeId);
            return cls == CombatClass.Military || cls == CombatClass.Mercenary;
        }

        /// <summary>true = плацдарм накопил достаточно силы, можно бить. false = ещё фаза сбора.
        /// Если staging не указан или порог 0 — считается всегда ready (старое поведение).</summary>
        public bool IsStagingReady()
        {
            if (string.IsNullOrEmpty(StagingStarUid)) return true;
            if (RequiredStagingPower <= 0f) return true;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy?.StarsMap == null) return true;
            if (!galaxy.StarsMap.TryGetValue(StagingStarUid, out var s)) return true;
            return s.GetFriendlyPower(OwnerId, RaceId, combatOnly: true) >= RequiredStagingPower;
        }

        public override NpcAction GetActionFor(ShipData ship, StarData star)
        {
            // Уже в целевой системе — бой (независимо от фазы).
            if (ship.CurrentStarUid == TargetStarUid)
            {
                string enemy = NpcTargeting.FindAnyHostileUid(star, ship);
                if (enemy != null) return new ActionPursueAndAttack(enemy);
                return new ActionPatrol();
            }

            bool ready = IsStagingReady();

            // Цель прыжка на этом ходу: target (если ready или нет плацдарма) либо staging.
            string destUid = (ready || string.IsNullOrEmpty(StagingStarUid)) ? TargetStarUid : StagingStarUid;

            // На плацдарме в фазе сбора: сидим и охраняем.
            if (!ready && ship.CurrentStarUid == StagingStarUid)
                return new ActionDefend(Vector2.zero, SRUnits.ToWorld(star.SystemSize) * 0.8f);

            // Multi-hop: прямой прыжок или промежуточная система (HyperNavigation).
            return HyperNavigation.ActionToward(ship, star, destUid);
        }

        // Каждые сколько ходов пишем PROGRESS-снапшот в лог. 5 — компромисс между шумом и видимостью.
        private const int ProgressLogInterval = 5;
        // На каком ходу выдана директива — известно косвенно через TurnsRemaining в base. Отсчитываем
        // от первого Tick, чтобы шаг снапшота был предсказуемым.
        private int _ticksSinceIssue;

        public override void Tick(GalaxyData galaxy)
        {
            base.Tick(galaxy);
            if (IsExpired) { LogExpire(galaxy, "timeout_or_prior"); return; }
            if (!galaxy.StarsMap.TryGetValue(TargetStarUid, out var star))
            {
                IsExpired = true;
                LogExpire(galaxy, "star_missing");
                return;
            }
            if (star.CurrentSystemController == OwnerId)
            {
                IsExpired = true;
                LogExpire(galaxy, "captured");
                return;
            }

            // Диагностический снапшот — раз в ProgressLogInterval ходов. Пишем ДО проверки no_enemies,
            // чтобы видеть картину даже в последний тик.
            _ticksSinceIssue++;
            if (_ticksSinceIssue % ProgressLogInterval == 0) LogProgress(galaxy, star);

            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return;
            bool anyMineAtTarget = false;
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s.CurrentHull <= 0) continue;
                if (rel.AreHostile(OwnerId, s.Owner)) return; // враг ещё в системе — атака продолжается
                if (Matches(s)) anyMineAtTarget = true;
            }
            // «Врагов нет» засчитываем только когда наш флот УЖЕ в целевой системе — иначе
            // директива на слабозащищённую (пустую) систему умирала бы на первом же тике,
            // задолго до прилёта кораблей, и атака вечно перевыдавалась без результата.
            if (!anyMineAtTarget) return;
            IsExpired = true;
            LogExpire(galaxy, "no_enemies");
        }

        private void LogExpire(GalaxyData galaxy, string reason)
        {
            string factionKey = string.IsNullOrEmpty(RaceId) ? OwnerId : $"{OwnerId}|{RaceId}";
            string starName = galaxy.StarsMap.TryGetValue(TargetStarUid, out var star) ? star.Name : "?";
            HighCommandLog.Expire(galaxy.CurrentTurn, factionKey, "EXPIRE_ATTACK",
                $"target={HighCommandLog.Safe(starName)} reason={reason}");
            OnResolved?.Invoke(this, reason);
        }

        /// <summary>Снапшот директивы: где мои корабли, кто в staging, кто в target, сколько
        /// могут гиперпрыгать, кто из типов (Military/Mercenary) участвует. Помогает понять,
        /// почему при огромной total_power атака не завершается за 25 ходов.
        ///
        /// Оптимизация июль 2026: перебор идёт через <see cref="Spawning.GalaxyShipCounters.ShipsAtStarByOwnerList"/>
        /// (только звёзды, где реально есть наши корабли этой стороны), а не по всей StarsMap × ships.
        /// При 4+ активных attack-директивах и 200-звёздной галактике это ~5× быстрее.
        /// Тяжёлые проверки CanRequestJump/PickNextHopToward остаются, но гоняются только по нашим
        /// боевым (Military/Mercenary), а не по всем гражданским тоже.</summary>
        private void LogProgress(GalaxyData galaxy, StarData targetStar)
        {
            StarData stagingStar = null;
            if (StagingStarUid != null) galaxy.StarsMap.TryGetValue(StagingStarUid, out stagingStar);

            int myAtStaging = 0, myAtTarget = 0, myElsewhere = 0;
            int myWithHyper = 0, myHyperInStaging = 0;
            int myMilitary = 0, myMercenary = 0;
            int canJumpToTarget = 0, canJumpToTargetFromStaging = 0;
            int canProgressToTarget = 0;
            int sumJumpRange = 0, jumpRangeCount = 0;

            var counters = SRG.NpcAI.Spawning.SpawnSystem.Counters;
            foreach (var star in galaxy.StarsMap.Values)
            {
                var ownerShips = counters?.GetShipsAtStarByOwner(star.Uid, OwnerId);
                if (ownerShips == null || ownerShips.Count == 0) continue;
                for (int i = 0; i < ownerShips.Count; i++)
                {
                    var s = ownerShips[i];
                    if (s.CurrentHull <= 0) continue;
                    if (!Matches(s)) continue;   // фильтр по Race + Military/Mercenary
                    var cls = NpcBrain.ResolveCombatClass(s.ShipTypeId);
                    if (cls == CombatClass.Military) myMilitary++;
                    else if (cls == CombatClass.Mercenary) myMercenary++;

                    bool hasHyper = EquipmentSystem.HasHyperjumpCapability(s);
                    if (hasHyper) myWithHyper++;

                    bool atStaging = s.CurrentStarUid == StagingStarUid;
                    bool atTarget = s.CurrentStarUid == TargetStarUid;
                    if (atStaging) { myAtStaging++; if (hasHyper) myHyperInStaging++; }
                    else if (atTarget) myAtTarget++;
                    else myElsewhere++;

                    int jr = EquipmentSystem.GetJumpRange(s);
                    if (jr > 0) { sumJumpRange += jr; jumpRangeCount++; }

                    if (hasHyper && !atTarget && HyperjumpController.CanRequestJump(s, targetStar, galaxy, out _))
                    {
                        canJumpToTarget++;
                        if (atStaging) canJumpToTargetFromStaging++;
                    }
                    if (hasHyper && !atTarget)
                    {
                        var nh = HyperNavigation.PickNextHopToward(s, star, targetStar, galaxy);
                        if (nh != null) canProgressToTarget++;
                    }
                }
            }

            float stagingPower = stagingStar != null ? stagingStar.GetFriendlyPower(OwnerId, RaceId, combatOnly: true) : 0f;
            float targetHostile = targetStar.GetHostilePower(OwnerId, RaceId, combatOnly: true);
            bool ready = IsStagingReady();
            float distStagingTarget = stagingStar != null ? HyperjumpController.CalcDistance(stagingStar, targetStar) : -1f;
            float avgJumpRange = jumpRangeCount > 0 ? (float)sumJumpRange / jumpRangeCount : 0f;

            string factionKey = string.IsNullOrEmpty(RaceId) ? OwnerId : $"{OwnerId}|{RaceId}";
            HighCommandLog.Progress(galaxy.CurrentTurn, factionKey, "ATTACK_PROGRESS",
                $"target={HighCommandLog.Safe(targetStar.Name)} staging={(stagingStar != null ? HighCommandLog.Safe(stagingStar.Name) : "-")} " +
                $"phase={(ready ? "READY" : "GATHERING")} " +
                $"staging_power={stagingPower:F0}/{RequiredStagingPower:F0} target_hostile={targetHostile:F0} " +
                $"my_ships={myMilitary + myMercenary}(mil={myMilitary},merc={myMercenary}) " +
                $"my_hyper={myWithHyper} " +
                $"at_staging={myAtStaging}(hyper={myHyperInStaging}) at_target={myAtTarget} elsewhere={myElsewhere} " +
                $"dist_st→tg={distStagingTarget:F1}pc avg_jump_range={avgJumpRange:F1}pc " +
                $"can_jump_to_target={canJumpToTarget}(from_staging={canJumpToTargetFromStaging}) " +
                $"can_progress={canProgressToTarget} " +
                $"ticks={_ticksSinceIssue} remaining={TurnsRemaining}");
        }
    }




    public class DirectiveDefendSystem : OwnerDirective
    {
        public string StarUid { get; }
        /// <summary>Если задано — призывает только корабли этой Race (per-race ГШ).</summary>
        public string RaceId { get; }

        public DirectiveDefendSystem(string ownerId, string starUid, int durationTurns = -1, string raceId = null)
        {
            OwnerId = ownerId;
            StarUid = starUid;
            RaceId = raceId;
            TurnsRemaining = durationTurns;
        }

        public override string DebugName => $"DefendSystem({StarUid[..6]})";

        public override bool Matches(ShipData ship)
        {
            if (ship?.Owner != OwnerId) return false;
            if (!string.IsNullOrEmpty(RaceId) && ship.Race != RaceId) return false;
            var cls = NpcBrain.ResolveCombatClass(ship.ShipTypeId);
            return cls == CombatClass.Military || cls == CombatClass.Mercenary;
        }

        public override NpcAction GetActionFor(ShipData ship, StarData star)
        {
            if (ship.CurrentStarUid != StarUid)
                return HyperNavigation.ActionToward(ship, star, StarUid); // multi-hop, как у Attack
            return new ActionDefend(Vector2.zero, SRUnits.ToWorld(star.SystemSize) * 0.8f);
        }

        public override void Tick(GalaxyData galaxy)
        {
            base.Tick(galaxy);
            if (IsExpired) { LogExpire(galaxy, "timeout_or_prior"); return; }
            if (!galaxy.StarsMap.TryGetValue(StarUid, out var star))
            {
                IsExpired = true;
                LogExpire(galaxy, "star_missing");
                return;
            }
            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return;
            float hostile = star.GetHostilePower(OwnerId, RaceId, combatOnly: true);
            if (hostile <= 0f)
            {
                IsExpired = true;
                LogExpire(galaxy, "threat_gone");
            }
        }

        private void LogExpire(GalaxyData galaxy, string reason)
        {
            string factionKey = string.IsNullOrEmpty(RaceId) ? OwnerId : $"{OwnerId}|{RaceId}";
            string starName = galaxy.StarsMap.TryGetValue(StarUid, out var star) ? star.Name : "?";
            HighCommandLog.Expire(galaxy.CurrentTurn, factionKey, "EXPIRE_DEFEND",
                $"star={HighCommandLog.Safe(starName)} reason={reason}");
        }
    }




    public class DirectiveSuppressPiracy : OwnerDirective
    {
        private readonly string _starUid;
        private readonly float _crimeThreshold;

        public string TargetStarUid => _starUid;
        public bool AutoIssued { get; set; }

        public DirectiveSuppressPiracy(string ownerId, string starUid, float crimeThreshold = 20f)
        {
            OwnerId = ownerId;
            _starUid = starUid;
            _crimeThreshold = crimeThreshold;
            TurnsRemaining = 30;
        }

        public override string DebugName => $"SuppressPiracy({_starUid[..6]})";

        public override NpcAction GetActionFor(ShipData ship, StarData star)
        {
            if (ship.CurrentStarUid != _starUid)
                return HyperNavigation.ActionToward(ship, star, _starUid); // multi-hop, как у Attack

            ShipData highCrime = null;
            float maxCrime = _crimeThreshold;
            foreach (var s in star.Ships)
            {
                if (s.Uid == ship.Uid || s.CurrentHull <= 0) continue;
                if (s.CrimeRating > maxCrime) { maxCrime = s.CrimeRating; highCrime = s; }
            }

            if (highCrime != null) return new ActionPursueAndAttack(highCrime.Uid);
            return new ActionPatrol();
        }

        public override void Tick(GalaxyData galaxy)
        {
            base.Tick(galaxy);
            if (!galaxy.StarsMap.TryGetValue(_starUid, out var star)) return;
            bool hasCriminal = false;
            foreach (var s in star.Ships)
                if (s.CrimeRating >= _crimeThreshold) { hasCriminal = true; break; }
            if (!hasCriminal) IsExpired = true;
        }
    }
}
