using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.NpcAI.Spawning.Policies
{
    /// <summary>
    /// Top-up per-star политика для синтетов.
    /// Цель — поддерживать в каждой клинг-звезде target подтипов (Dom1..Dom6) расы синтетов.
    ///
    /// Алгоритм:
    ///   1. Только звёзды с Owner=Dominators.
    ///   2. Раса синтета берётся из Star.Race (например "RaceDominators1").
    ///   3. target = TargetByRace[star.Race].
    ///   4. count = сумма живых подтипов этой расы в этой звезде (по AvailableShipTypes минус boss).
    ///   5. Если shortage>0 и ролл прошёл — выбираем подтип через SubtypePicker и спавним.
    ///
    /// ShipTypeId = "Dominator" — это маркер политики, не реальный тип корабля. Конкретный тип
    /// (Dom1..Dom6) определяется SubtypePicker'ом по ступени доминации расы.
    /// </summary>
    public class DominatorSpawnPolicy : ISpawnPolicy
    {
        public string ShipTypeId => "Dominator";

        public void DailyTick(SpawnTickContext ctx)
        {
            var policy = ctx.Config.Spawn?.GetPolicy(ShipTypeId);
            if (policy == null || policy.BaseChance <= 0f) return;

            int stars = 0, cdSkip = 0, spawned = 0;
            long spawnMs = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var sector in ctx.Galaxy.Sectors)
                foreach (var star in sector.Stars)
                {
                    stars++;
                    if (!SpawnFormulas.StarCanSpawnMore(star, ctx.Settings)) { cdSkip++; continue; }
                    int before = ctx.Counters.GetShips(ShipTypeId); // маркер спавна — счётчик не важен, важна дельта
                    long msBefore = sw.ElapsedMilliseconds;
                    TryTickStar(star, policy, ctx);
                    long ms = sw.ElapsedMilliseconds - msBefore;
                    if (ms > 0) { spawnMs += ms; spawned++; }
                }
            SRG.Utils.PerfLog.Log($"[DomPolicy] Turn {ctx.CurrentTurn}: stars={stars} cdSkip={cdSkip} heavyIter={spawned} heavyMs={spawnMs}");
        }

        private void TryTickStar(StarData star, ShipSpawnPolicyConfig policy, SpawnTickContext ctx)
        {
            if (star == null) return;
            if (string.IsNullOrEmpty(star.Race)) return;

            // Эффективный контроллёр системы: либо родные синтеты, либо оккупант в состоянии
            // «полностью контролируют систему» (RecomputeSystemControl выставляет только при uniform).
            string controller = !string.IsNullOrEmpty(star.CurrentSystemController)
                ? star.CurrentSystemController
                : star.Owner;
            if (controller != "Dominators") return;

            if (policy.AllowedOwners != null && policy.AllowedOwners.Count > 0
                && !policy.AllowedOwners.Contains(controller)) return;

            // Target по расе синтета
            int target = ResolveTarget(star.Race, policy);
            if (target <= 0) return;

            // Считаем сумму подтипов расы в звезде
            if (!ctx.Config.Races.TryGetValue(star.Race, out var raceCfg) || raceCfg == null) return;
            if (!raceCfg.UseShipTypeTable) return; // только расы с таблицей подтипов

            int count = 0;
            if (raceCfg.AvailableShipTypes != null)
                foreach (var subtype in raceCfg.AvailableShipTypes)
                    count += ctx.Counters.GetShipsAtStar(star.Uid, subtype);

            if (!SpawnFormulas.ShouldSpawn(count, target, policy.BaseChance)) return;

            // Выбор подтипа по таблице расы + ступень доминации
            string fallback = raceCfg.AvailableShipTypes != null && raceCfg.AvailableShipTypes.Count > 0
                ? raceCfg.AvailableShipTypes[0] : null;
            string subtypeId = SubtypePicker.PickForRace(star.Race, ctx.Config.Spawn, ctx.Domination, fallback);
            if (string.IsNullOrEmpty(subtypeId)) return;

            SpawnAtStar(subtypeId, star, raceCfg, ctx, target, count);
        }

        private static int ResolveTarget(string raceKey, ShipSpawnPolicyConfig policy)
        {
            if (policy.TargetByRace != null && policy.TargetByRace.TryGetValue(raceKey, out var t)) return t;
            return policy.BaseTarget;
        }

        /// <summary>
        /// Спавн в звезде: у первой подходящей планеты (если есть Owner=Dominators), иначе в случайной
        /// точке у любой планеты. Синтеты NonPlanetary, поэтому HomePlanet — формальность.
        /// </summary>
        private static void SpawnAtStar(string subtypeId, StarData star, RaceConfig raceCfg, SpawnTickContext ctx, int target, int count)
        {
            PlanetData anchor = null;
            foreach (var p in star.Planets)
            {
                if (p == null) continue;
                if (p.Owner == "Dominators") { anchor = p; break; }
            }
            if (anchor == null && star.Planets.Count > 0) anchor = star.Planets[GameRng.Range(0, star.Planets.Count)];
            if (anchor == null) return; // дегенеративная звезда без планет — пропускаем

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ship = SpawnSystem.SpawnShipAtPlanet(subtypeId, "Dominators", star.Race, anchor, star, ctx.Gen,
                reason: $"star={star.Name} race={star.Race} target={target} count={count}", landed: false);
            sw.Stop();
            SRG.Utils.PerfLog.Log($"[DomSpawn] Turn {ctx.CurrentTurn}: subtype={subtypeId} race={star.Race} star={star.Name} ms={sw.ElapsedMilliseconds} ok={(ship != null)}");
        }
    }
}
