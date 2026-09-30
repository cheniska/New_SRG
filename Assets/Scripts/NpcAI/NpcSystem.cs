using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.NpcAI
{
    public static class NpcSystem
    {
        // ── Per-turn instrumentation (см. ResetTelemetry / LogTelemetry) ───────
        // NpcTick=Xms в perf-логе рос 15→50мс за 400 ходов. Разбивка помогает понять,
        // растёт ли Brain.Tick (target search / directive eval), PartnerService.CheckBreak
        // или ShipLoadoutService (обход AllItems + Equipment.Slots).
        private static long _tPartner, _tLoadout, _tBrain;
        private static int _ships, _brainTicks, _brainCreated, _hyperSkipped, _carriedSkipped;

        public static void ResetTelemetry()
        {
            _tPartner = _tLoadout = _tBrain = 0;
            _ships = _brainTicks = _brainCreated = _hyperSkipped = _carriedSkipped = 0;
        }

        public static void LogTelemetry(int currentTurn)
        {
            long sum = _tPartner + _tLoadout + _tBrain;
            if (sum < 3) return; // тихий ход
            SRG.Utils.PerfLog.Log($"[NpcTick] Turn {currentTurn}: " +
                $"ships={_ships} brainTicks={_brainTicks} brainNew={_brainCreated} " +
                $"hyperSkip={_hyperSkipped} carriedSkip={_carriedSkipped} | " +
                $"partner={_tPartner}ms loadout={_tLoadout}ms brain={_tBrain}ms");
        }

        private static readonly System.Diagnostics.Stopwatch _sw = new();

        public static void TickAllSystems(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            ResetTelemetry();
            // Loyalty-check партнёров — один батч на всю галактику, а не per-ship в TickStar.
            int turn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
            var loyaltyCfg = GameWorld.Context?.Config?.Partners?.Global;
            int loyaltyPeriod = loyaltyCfg?.LoyaltyCheckPeriodTurns ?? 5;
            if (loyaltyPeriod > 0 && turn % loyaltyPeriod == 0)
            {
                _sw.Restart();
                PartnerService.CheckBreakAll(galaxy, turn);
                _tPartner += _sw.ElapsedMilliseconds;
            }
            foreach (var star in galaxy.StarsMap.Values)
                TickStar(star, ctx);
            LogTelemetry(galaxy.CurrentTurn);
        }

        private static void TickStar(StarData star, GalaxyGenerationContext ctx)
        {
            int turn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;

            var ships = star.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                if (ship.IsPlayer || ship.CurrentHull <= 0) continue;
                _ships++;

                // Отложенное надевание подобранного оборудования (ход после подбора)
                // + сброс груза контейнерами при перегрузе по любой причине.
                _sw.Restart();
                ShipLoadoutService.TickTurn(ship, star, turn, ctx?.ItemsConfig);
                _tLoadout += _sw.ElapsedMilliseconds;

                // На ходе HyperEnter мозг не тикаем здесь — миграция произойдёт в FinalizeTurn,
                // фаза сменится на HyperArrive. На HyperArrive корабль неподвижно ждёт открытия
                // портала (см. HyperjumpController.PrepareTurn) — здесь тоже не тикаем, брейн
                // получит Tick при переходе HyperArrive → HyperExit (в FinalizeTurn), уже
                // в системе цели.
                if (ship.HyperjumpPhase == HyperjumpPhase.HyperEnter
                    || ship.HyperjumpPhase == HyperjumpPhase.HyperArrive) { _hyperSkipped++; continue; }
                if (ship.Brain == null) { ship.Brain = new NpcBrain(ship, star); _brainCreated++; }
                _sw.Restart();
                ship.Brain.Tick(ship, star, ctx);
                _tBrain += _sw.ElapsedMilliseconds;
                _brainTicks++;
            }
        }
    }
}
