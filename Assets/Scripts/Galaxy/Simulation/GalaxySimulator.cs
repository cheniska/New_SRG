using SRG.Economy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships.Services;

namespace SRG.Galaxy.Simulation
{
    /// <summary>
    /// День галактики: звёзды (<see cref="StarSimulator"/>), затем галактические системы —
    /// торговля, события планет, наука, техуровни, магазины, спавн, червоточины, инфляция,
    /// новости присутствия, рейтинги, сброс груза от страха. Порядок систем значим.
    /// </summary>
    public static class GalaxySimulator
    {
        public static TurnAnimationData SimulateDay(GalaxyData galaxy, GalaxyGenerationContext ctx = null)
        {
            galaxy.CurrentTurn++;
            galaxy.MigrateShipsBetweenStars();
            var anim = new TurnAnimationData();

            SRG.Galaxy.Simulation.StarSimulator.ResetTelemetry();
            var swStars = System.Diagnostics.Stopwatch.StartNew();
            // Каждая звезда считает день в своём потоке случайности (ключ дня + Uid звезды):
            // результат звезды не зависит от порядка обхода и от других звёзд.
            var dayKey = SRG.Simulation.GameRng.NextKey();
            foreach (var star in galaxy.StarsMap.Values)
                using (SRG.Simulation.GameRng.Use(SRG.Simulation.GameRng.Derive(dayKey, star.Uid)))
                    star.StarNextDay(anim, ctx);
            swStars.Stop();
            SRG.Galaxy.Simulation.StarSimulator.LogTelemetry(galaxy.CurrentTurn);

            long tTrade = 0, tEvt = 0, tSci = 0, tTech = 0, tShop = 0, tSpawn = 0, tWorm = 0, tInf = 0, tPres = 0, tRate = 0;
            if (ctx != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                TradeSystem.TickAll(galaxy, ctx.Config);              sw.Stop(); tTrade = sw.ElapsedMilliseconds; sw.Restart();
                PlanetaryEventSystem.TickAll(galaxy, ctx);            sw.Stop(); tEvt   = sw.ElapsedMilliseconds; sw.Restart();
                ScienceSystem.TickAll(galaxy, ctx);                   sw.Stop(); tSci   = sw.ElapsedMilliseconds; sw.Restart();
                PlanetaryTechSystem.TickAll(galaxy, ctx);             sw.Stop(); tTech  = sw.ElapsedMilliseconds; sw.Restart();
                EquipmentShopSystem.TickAll(galaxy, ctx);             sw.Stop(); tShop  = sw.ElapsedMilliseconds; sw.Restart();
                SpawnSystem.DailyTick(galaxy, ctx);                   sw.Stop(); tSpawn = sw.ElapsedMilliseconds; sw.Restart();
                WormholeSystem.DailyTick(galaxy, ctx);                sw.Stop(); tWorm  = sw.ElapsedMilliseconds; sw.Restart();
                if (galaxy.CurrentTurn % 30 == 0)
                    InflationSystem.TickMonthly(galaxy, ctx.Config);
                sw.Stop(); tInf = sw.ElapsedMilliseconds; sw.Restart();
                // Аналитика/новости — плотность в звёздах (недельный тик) и рейтинги кораблей (по конфигу Ratings).
                StarPresenceService.TickIfDue(galaxy);                sw.Stop(); tPres  = sw.ElapsedMilliseconds; sw.Restart();
                ShipRatingService.TickIfDue(galaxy, ctx.Config);      sw.Stop(); tRate  = sw.ElapsedMilliseconds;
                // Fear-driven cargo drop (DropGoodsInFear): преследуемые с грузом сбрасывают часть трюма.
                SRG.Ships.Services.FearDropService.TickAll(galaxy);
            }

            SRG.Utils.PerfLog.Log($"[GND] Turn {galaxy.CurrentTurn}: stars={swStars.ElapsedMilliseconds}ms " +
                $"trade={tTrade} evt={tEvt} sci={tSci} tech={tTech} shop={tShop} " +
                $"spawn={tSpawn} worm={tWorm} inf={tInf} pres={tPres} rate={tRate}");

            // Спайк-диагностика на секции, которые дорого стреляли:
            // T184 evt=227мс (тут увидим, сколько планет тикнуло + сколько событий стартовало).
            // T322 shop=128мс (сколько магазинов оказалось в day-slot + сколько предметов
            // родилось/удалилось). Порог 30мс — чтобы шум не забивал типичный лог.
            if (tEvt >= 30)
                SRG.Utils.PerfLog.Log($"[GND-spike] Turn {galaxy.CurrentTurn} evt: " +
                    $"planetsTicked={SRG.Science.PlanetaryEventSystem._diagPlanetsTicked} " +
                    $"eventsStarted={SRG.Science.PlanetaryEventSystem._diagEventsStarted} " +
                    $"lastId={SRG.Science.PlanetaryEventSystem._diagLastStartedEventId ?? "-"}");
            if (tShop >= 30)
                SRG.Utils.PerfLog.Log($"[GND-spike] Turn {galaxy.CurrentTurn} shop: " +
                    $"shopsRefreshed={SRG.Economy.EquipmentShopSystem._diagShopsRefreshed} " +
                    $"itemsAdded={SRG.Economy.EquipmentShopSystem._diagItemsAdded} " +
                    $"itemsRemoved={SRG.Economy.EquipmentShopSystem._diagItemsRemoved}");

            return anim;
        }
    }
}
