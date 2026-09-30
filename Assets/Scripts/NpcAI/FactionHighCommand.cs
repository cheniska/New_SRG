using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Simulation;

namespace SRG.NpcAI
{
    /// <summary>
    /// Стратегия ГШ (см. <see cref="FactionHighCommand"/>). Стратегия определяет способ выбора цели атаки.
    /// </summary>
    public enum HighCommandStrategy
    {
        /// <summary>Слабейшая (по HostilePower) вражеская система в радиусе <see cref="FactionHighCommand.NearbyHopsRadius"/> хопов от плацдармов.</summary>
        WeakestNearby,
        /// <summary>Слабейшая вражеская система во всей галактике, независимо от дальности.</summary>
        WeakestAnywhere,
        /// <summary>Приоритет: враг, у которого ≥50% соседних систем уже под нашим контролем — «душитель».</summary>
        Encirclement,
        /// <summary>Приоритет: враг с наибольшим Population — идёт по столицам.</summary>
        HighValue,
    }

    /// <summary>
    /// Сериализуемое состояние ГШ (для GalaxyData.HighCommandStates).
    /// </summary>
    public class HighCommandStateData
    {
        public string OwnerId;
        public string RaceId;                    // null = ГШ на весь Owner
        public HighCommandStrategy Strategy;
        public int NextEvaluationTurn;
    }

    /// <summary>
    /// «Генштаб» одной фракции. Раз в 20-40 ходов оценивает обстановку и выдаёт директивы:
    /// <see cref="DirectiveDefendSystem"/> под свои системы, где идёт бой; <see cref="DirectiveAttackSystem"/>
    /// на выбранную по стратегии вражескую систему.
    /// Если у Owner <see cref="OwnerConfig.SeparatePerRaceHighCommand"/>=true — ГШ на каждую Race
    /// (синтеты: RaceDominators1/2/3 = 3 отдельных генштаба).
    /// </summary>
    public class FactionHighCommand
    {
        public string OwnerId { get; }
        /// <summary>null = ГШ на весь Owner; иначе ГШ на конкретную Race.</summary>
        public string RaceId { get; }
        public HighCommandStrategy Strategy { get; set; }
        public int NextEvaluationTurn { get; set; }

        // Балансные параметры. Позже вынести в GameSettingsConfig, если нужно тюнить в UI.
        public const int EvaluationIntervalMin = 20;
        public const int EvaluationIntervalMax = 40;
        public const int MaxConcurrentDirectives = 3;
        public const int NearbyHopsRadius = 3;
        // Оборона: если Hostile > Friendly * этой доли — призываем подкрепление.
        public const float DefenseTriggerRatio = 0.7f;
        // Атака: my staging power >= этот × Hostile(target) — идём в бой.
        public const float AttackReadyRatio = 1.3f;

        /// <summary>Ключ для реестра и сохранения. Owner|Race либо просто Owner.</summary>
        public string Key => string.IsNullOrEmpty(RaceId) ? OwnerId : $"{OwnerId}|{RaceId}";

        public FactionHighCommand(string ownerId, string raceId, HighCommandStrategy strategy, int nextEvalTurn)
        {
            OwnerId = ownerId;
            RaceId = raceId;
            Strategy = strategy;
            NextEvaluationTurn = nextEvalTurn;
        }

        public HighCommandStateData ToState() => new HighCommandStateData
        {
            OwnerId = OwnerId, RaceId = RaceId, Strategy = Strategy, NextEvaluationTurn = NextEvaluationTurn
        };

        public static FactionHighCommand FromState(HighCommandStateData s) =>
            new FactionHighCommand(s.OwnerId, s.RaceId, s.Strategy, s.NextEvaluationTurn);

        /// <summary>Один тик. Если счётчик подошёл — переоценивает и переставляет счётчик.</summary>
        public void Tick(GalaxyData galaxy)
        {
            if (galaxy == null) return;
            if (galaxy.CurrentTurn < NextEvaluationTurn) return;
            Evaluate(galaxy);
            NextEvaluationTurn = galaxy.CurrentTurn + GameRng.Range(EvaluationIntervalMin, EvaluationIntervalMax + 1);
        }

        private void Evaluate(GalaxyData galaxy)
        {
            var dm = DirectiveManager.Instance;
            if (dm == null) return;

            // Пересобрать Power-кэш системы под сегодня (за компанию: обычно уже сделан DirectiveManager).
            foreach (var s in galaxy.StarsMap.Values) s.RebuildPowerCache(galaxy.CurrentTurn);

            var myStars = CollectOwnedStars(galaxy);
            int myActive = CountMyDirectives(dm);
            int turn = galaxy.CurrentTurn;

            HighCommandLog.Eval(turn, Key, "EVAL_START",
                $"strategy={Strategy} owned_systems={myStars.Count} active_directives={myActive}");

            // 1) Оборона: если система под угрозой — Defend перекрывает всё.
            // Все оценки силы здесь и ниже — combatOnly: транспорты и прочие гражданские
            // не участвуют ни в ударном кулаке, ни в гарнизоне.
            foreach (var star in myStars)
            {
                float hostile = star.GetHostilePower(OwnerId, RaceId, combatOnly: true);
                if (hostile <= 0f) continue;
                float friendly = star.GetFriendlyPower(OwnerId, RaceId, combatOnly: true);
                if (hostile <= friendly * DefenseTriggerRatio) continue;
                if (HasDefendOn(dm, star.Uid))
                {
                    HighCommandLog.Eval(turn, Key, "DEFEND_ALREADY",
                        $"star={HighCommandLog.Safe(star.Name)} hostile={hostile:F0} friendly={friendly:F0}");
                    continue;
                }
                // Оборона обходит бюджет — важнее атаки.
                dm.Issue(new DirectiveDefendSystem(OwnerId, star.Uid, durationTurns: 25, raceId: RaceId));
                HighCommandLog.Directive(turn, Key, "DEFEND_ISSUED",
                    $"star={HighCommandLog.Safe(star.Name)} hostile={hostile:F0} friendly={friendly:F0} ratio={(hostile / Mathf.Max(friendly, 0.1f)):F2}");
                myActive++;
            }

            // 2) Атака: если бюджет позволяет — выбираем цель по стратегии.
            if (myActive >= MaxConcurrentDirectives)
            {
                HighCommandLog.Eval(turn, Key, "BUDGET_FULL", $"active={myActive} max={MaxConcurrentDirectives}");
                return;
            }

            var target = SelectTarget(galaxy, myStars);
            if (target == null)
            {
                int hostileTotal = 0;
                foreach (var s in galaxy.StarsMap.Values) if (IsHostileSystem(s)) hostileTotal++;
                HighCommandLog.Eval(turn, Key, "NO_TARGET",
                    $"strategy={Strategy} hostile_in_galaxy={hostileTotal}");
                return;
            }
            if (HasAttackOn(dm, target.Uid))
            {
                HighCommandLog.Eval(turn, Key, "ATTACK_ALREADY", $"target={HighCommandLog.Safe(target.Name)}");
                return;
            }

            float targetHostile = target.GetHostilePower(OwnerId, RaceId, combatOnly: true);
            HighCommandLog.Eval(turn, Key, "TARGET_SELECTED",
                $"target={HighCommandLog.Safe(target.Name)} target_uid={HighCommandLog.ShortId(target.Uid)} " +
                $"hostile={targetHostile:F0} strategy={Strategy}");

            var staging = SelectStagingFor(target, myStars, galaxy);
            if (staging == null)
            {
                float sumPower = 0f;
                int nonZero = 0;
                foreach (var s in myStars) { float p = s.GetFriendlyPower(OwnerId, RaceId, combatOnly: true); if (p > 0f) { sumPower += p; nonZero++; } }
                HighCommandLog.Eval(turn, Key, "NO_STAGING",
                    $"target={HighCommandLog.Safe(target.Name)} own_powered={nonZero}/{myStars.Count} total_power={sumPower:F0}");
                return;
            }

            float stagingPower = staging.GetFriendlyPower(OwnerId, RaceId, combatOnly: true);
            float requiredPower = AttackReadyRatio * (targetHostile + 1f);
            // Санити-чек: если у всей фракции суммарно мизер сил — не тратить бюджет директив
            // на бессмысленный сбор. Копить будем при следующей переоценке, когда top-up подкинет.
            float totalPower = 0f;
            foreach (var s in myStars) totalPower += s.GetFriendlyPower(OwnerId, RaceId, combatOnly: true);
            if (totalPower < 0.3f * requiredPower)
            {
                HighCommandLog.Eval(turn, Key, "TOO_WEAK",
                    $"staging={HighCommandLog.Safe(staging.Name)} total_power={totalPower:F0} " +
                    $"required={requiredPower:F0} target_hostile={targetHostile:F0}");
                return;
            }

            // Директива выдаётся всегда: если staging уже накопил силу — атака стартует сразу;
            // иначе директива стягивает воинов из соседних своих систем в staging (фаза сбора),
            // и удар происходит, как только staging.FriendlyPower ≥ requiredPower.
            // durationTurns=60 — учитывает multi-hop путь через несколько промежуточных систем:
            // каждый прыжок это HyperEnter+HyperArrive+HyperExit (3 хода) + время до края системы, плюс сам бой.
            dm.Issue(new DirectiveAttackSystem(OwnerId, target.Uid, durationTurns: 60,
                stagingStarUid: staging.Uid, raceId: RaceId,
                requiredStagingPower: requiredPower));

            string phase = stagingPower >= requiredPower ? "READY" : "GATHERING";
            // Разбивка флота в staging по классам и наличию гиперпрыжка — через материализованный
            // индекс ShipsAtStarByOwnerList (только наши корабли данной стороны в этой звезде).
            int stagingMil = 0, stagingMerc = 0, stagingHyper = 0;
            var stagingOwnerShips = SRG.NpcAI.Spawning.SpawnSystem.Counters?.GetShipsAtStarByOwner(staging.Uid, OwnerId);
            if (stagingOwnerShips != null)
                for (int si = 0; si < stagingOwnerShips.Count; si++)
                {
                    var s = stagingOwnerShips[si];
                    if (s.CurrentHull <= 0) continue;
                    if (!string.IsNullOrEmpty(RaceId) && s.Race != RaceId) continue;
                    var cls = NpcBrain.ResolveCombatClass(s.ShipTypeId);
                    if (cls != CombatClass.Military && cls != CombatClass.Mercenary) continue;
                    if (cls == CombatClass.Military) stagingMil++; else stagingMerc++;
                    if (EquipmentSystem.HasHyperjumpCapability(s)) stagingHyper++;
                }
            HighCommandLog.Directive(turn, Key, $"ATTACK_ISSUED_{phase}",
                $"staging={HighCommandLog.Safe(staging.Name)} target={HighCommandLog.Safe(target.Name)} " +
                $"my={stagingPower:F0} required={requiredPower:F0} their={targetHostile:F0} " +
                $"total_power={totalPower:F0} " +
                $"staging_ships={stagingMil + stagingMerc}(mil={stagingMil},merc={stagingMerc}) staging_hyper={stagingHyper} " +
                $"strategy={Strategy}");

            Debug.Log($"[HighCommand] {Key}/{Strategy}: {phase} {staging.Name}→{target.Name} " +
                      $"(my={stagingPower:F0}/{requiredPower:F0} vs their={targetHostile:F0}).");
        }

        // ── Сбор своих систем ────────────────────────────────────────────────────

        private List<StarData> CollectOwnedStars(GalaxyData galaxy)
        {
            var list = new List<StarData>();
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (OwnsStar(star)) list.Add(star);
            }
            return list;
        }

        /// <summary>Наша ли система: хотя бы одна населённая планета управляется нами
        /// (учитывая Race, если ГШ per-race).</summary>
        private bool OwnsStar(StarData star)
        {
            for (int i = 0; i < star.Planets.Count; i++)
            {
                var p = star.Planets[i];
                if (p.Settlement.Population <= 0) continue;
                string ctrl = OccupationService.GetControllingOwner(p);
                if (ctrl != OwnerId) continue;
                if (!string.IsNullOrEmpty(RaceId) && p.Race != RaceId) continue;
                return true;
            }
            return false;
        }

        // ── Выбор цели по стратегии ─────────────────────────────────────────────

        private StarData SelectTarget(GalaxyData galaxy, List<StarData> myStars)
        {
            return Strategy switch
            {
                HighCommandStrategy.WeakestNearby   => TargetWeakestNearby(galaxy, myStars),
                HighCommandStrategy.WeakestAnywhere => TargetWeakestAnywhere(galaxy),
                HighCommandStrategy.Encirclement    => TargetEncirclement(galaxy, myStars),
                HighCommandStrategy.HighValue       => TargetHighValue(galaxy),
                _ => TargetWeakestNearby(galaxy, myStars),
            };
        }

        private StarData TargetWeakestNearby(GalaxyData galaxy, List<StarData> myStars)
        {
            // BFS по физическому соседству (HyperNavigation.GetNeighbors): графа связей в данных
            // нет, «сосед» = система в радиусе медианного nearest-neighbor × запас.
            var visited = new HashSet<string>();
            var reachable = new List<StarData>();
            foreach (var seed in myStars) BfsHops(galaxy, seed, NearbyHopsRadius, visited, reachable);
            var pick = PickWeakestHostile(reachable);
            if (pick != null) return pick;
            // Fallback: если в радиусе никого — расширяемся до всей галактики. Иначе фракция,
            // окружённая своими или пиратами, никогда не атакует.
            return PickWeakestHostile(galaxy.StarsMap.Values);
        }

        private StarData TargetWeakestAnywhere(GalaxyData galaxy)
            => PickWeakestHostile(galaxy.StarsMap.Values);

        private StarData TargetEncirclement(GalaxyData galaxy, List<StarData> myStars)
        {
            var mySet = new HashSet<string>();
            foreach (var s in myStars) mySet.Add(s.Uid);

            StarData best = null;
            float bestScore = -1f;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (mySet.Contains(star.Uid)) continue;
                if (!IsHostileSystem(star)) continue;
                var neighbors = HyperNavigation.GetNeighbors(star, galaxy);
                if (neighbors.Count == 0) continue;
                int owned = 0;
                for (int i = 0; i < neighbors.Count; i++)
                    if (mySet.Contains(neighbors[i].Uid)) owned++;
                float ratio = (float)owned / neighbors.Count;
                if (ratio < 0.5f) continue;
                // Слабейший среди «окружённых».
                float hostile = star.GetHostilePower(OwnerId, RaceId, combatOnly: true);
                float score = ratio * 100f - hostile;
                if (score > bestScore) { bestScore = score; best = star; }
            }
            return best;
        }

        private StarData TargetHighValue(GalaxyData galaxy)
        {
            StarData best = null;
            int bestPop = -1;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (!IsHostileSystem(star)) continue;
                int pop = 0;
                for (int i = 0; i < star.Planets.Count; i++) pop += star.Planets[i].Settlement.Population;
                if (pop > bestPop) { bestPop = pop; best = star; }
            }
            return best;
        }

        private StarData PickWeakestHostile(IEnumerable<StarData> stars)
        {
            StarData best = null;
            float bestHostile = float.MaxValue;
            foreach (var star in stars)
            {
                if (!IsHostileSystem(star)) continue;
                float h = star.GetHostilePower(OwnerId, RaceId, combatOnly: true);
                if (h < bestHostile) { bestHostile = h; best = star; }
            }
            return best;
        }

        private bool IsHostileSystem(StarData star)
        {
            // Есть ли в системе населённые планеты, контроллёр которых нам враждебен.
            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return false;
            for (int i = 0; i < star.Planets.Count; i++)
            {
                var p = star.Planets[i];
                if (p.Settlement.Population <= 0) continue;
                string ctrl = OccupationService.GetControllingOwner(p);
                if (ctrl == OwnerId) continue;
                if (rel.AreHostile(OwnerId, ctrl)) return true;
            }
            return false;
        }

        // ── Плацдарм ────────────────────────────────────────────────────────────

        private StarData SelectStagingFor(StarData target, List<StarData> myStars, GalaxyData galaxy)
        {
            // Ближайшая ко цели своя система по физической дистанции, с ненулевой FriendlyPower
            // (та же евклидова метрика, что HyperjumpController.CanRequestJump и HyperNavigation).
            StarData best = null;
            float bestDist = float.MaxValue;
            float bestPower = 0f;
            foreach (var st in myStars)
            {
                float p = st.GetFriendlyPower(OwnerId, RaceId, combatOnly: true);
                if (p <= 0f) continue;
                float d = HyperjumpController.CalcDistance(st, target);
                if (d < bestDist || (Mathf.Approximately(d, bestDist) && p > bestPower))
                {
                    bestDist = d; bestPower = p; best = st;
                }
            }
            if (best != null) return best;

            // Финальный fallback: сила есть, но нашли 0 систем с ненулевой силой — тогда самая сильная
            // независимо от дистанции. Практически недостижимо (проверка выше уже отсеяла).
            foreach (var st in myStars)
            {
                float p = st.GetFriendlyPower(OwnerId, RaceId, combatOnly: true);
                if (p > bestPower) { bestPower = p; best = st; }
            }
            return best;
        }

        private static void BfsHops(GalaxyData galaxy, StarData seed, int maxHops,
            HashSet<string> visited, List<StarData> outList)
        {
            var queue = new Queue<(StarData s, int d)>();
            queue.Enqueue((seed, 0));
            while (queue.Count > 0)
            {
                var (cur, d) = queue.Dequeue();
                if (!visited.Add(cur.Uid)) continue;
                if (d > 0) outList.Add(cur);
                if (d >= maxHops) continue;
                var neighbors = HyperNavigation.GetNeighbors(cur, galaxy);
                for (int i = 0; i < neighbors.Count; i++)
                    if (!visited.Contains(neighbors[i].Uid))
                        queue.Enqueue((neighbors[i], d + 1));
            }
        }

        // ── Проверки директив ───────────────────────────────────────────────────

        private int CountMyDirectives(DirectiveManager dm)
        {
            int n = 0;
            foreach (var d in dm.GetOwnerDirectives(OwnerId))
            {
                if (d.IsExpired) continue;
                if (d is DirectiveAttackSystem a && (RaceId == null || a.RaceId == RaceId)) n++;
                else if (d is DirectiveDefendSystem def && (RaceId == null || def.RaceId == RaceId)) n++;
            }
            return n;
        }

        private bool HasAttackOn(DirectiveManager dm, string targetStarUid)
        {
            foreach (var d in dm.GetOwnerDirectives(OwnerId))
                if (d is DirectiveAttackSystem a && !a.IsExpired && a.TargetStarUid == targetStarUid) return true;
            return false;
        }

        private bool HasDefendOn(DirectiveManager dm, string starUid)
        {
            foreach (var d in dm.GetOwnerDirectives(OwnerId))
                if (d is DirectiveDefendSystem def && !def.IsExpired && def.StarUid == starUid
                    && (RaceId == null || def.RaceId == RaceId)) return true;
            return false;
        }
    }
}
