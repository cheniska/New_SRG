using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Spawning;
using SRG.Ships.Movement;
using SRG.Utils;
using SRG.Economy;

namespace SRG.Simulation
{
    /// <summary>
    /// Состояние игрового мира и расчёт хода — без сцены, анимации и ввода.
    ///
    /// Владеет всеми галактиками (активная + фоновые) и контекстом генерации. Слой приложения
    /// (<c>SRG.Core.GalaxyManager</c>) добавляет поверх тайминг фаз, визуал и гиперпереходы
    /// сцены; тесты и headless-прогон используют сессию напрямую (см. <see cref="HeadlessWorldHost"/>).
    /// </summary>
    public sealed class SimulationSession
    {
        public GalaxyGenerationContext Context { get; }

        /// <summary>Все галактики (ключ = ключ в GalaxyConfig.Galaxies). Каждая тикается каждый ход.</summary>
        public Dictionary<string, GalaxyData> Galaxies { get; private set; } = new();

        /// <summary>Ключ активной галактики — той, где сейчас игрок.</summary>
        public string ActiveGalaxyKey { get; private set; }

        public GalaxyData ActiveGalaxy
            => !string.IsNullOrEmpty(ActiveGalaxyKey) && Galaxies.TryGetValue(ActiveGalaxyKey, out var g) ? g : null;

        /// <summary>Галактика, чей GalaxyNextDay сейчас исполняется (вне тика — null).
        /// Нужен реактивным подписчикам (GalaxyNewsService, DirectiveManager), чтобы события
        /// от неактивной галактики адресовались ей, а не активной.</summary>
        public GalaxyData CurrentTickingGalaxy { get; private set; }

        public int Seed { get; private set; }

        public SimulationSession(GalaxyGenerationContext context)
        {
            Context = context;
        }

        // ── Новая игра / загрузка ─────────────────────────────────────────────

        /// <summary>Сгенерировать все галактики из конфига. Активной становится DEFAULT_GALAXY_KEY,
        /// а если её нет — первая по порядку. false — в конфиге нет галактик.</summary>
        public bool GenerateAll(int seed)
        {
            Seed = seed;
            Galaxies = new Dictionary<string, GalaxyData>();
            ActiveGalaxyKey = null;

            var galaxyCfgs = Context?.Config?.Galaxies;
            if (galaxyCfgs == null || galaxyCfgs.Count == 0)
            {
                UnityEngine.Debug.LogError("[SimulationSession] GalaxyConfig.Galaxies пуст — нечего генерировать.");
                return false;
            }

            var generator = new GalaxyGenerator(Context);
            int slot = 0;
            foreach (var kv in galaxyCfgs)
            {
                // DEFAULT_GALAXY_KEY получает оригинальный seed — иначе ломается репродуктивность
                // старых сидов (до мультигалактики). Прочие галактики XOR-мутируют seed, чтобы не
                // быть копией первой.
                int gSeed = kv.Key == GalaxyConstants.DEFAULT_GALAXY_KEY
                    ? seed
                    : unchecked(seed ^ (int)(0x9E3779B1 * (slot + 1)));
                var g = generator.Generate(gSeed, kv.Key);
                g.InitSimulation();
                Galaxies[kv.Key] = g;
                GalaxyLogger.LogGalaxy(g, gSeed, Context.Config, Context.AvailableRaces);
                slot++;
            }

            ActiveGalaxyKey = galaxyCfgs.ContainsKey(GalaxyConstants.DEFAULT_GALAXY_KEY)
                ? GalaxyConstants.DEFAULT_GALAXY_KEY
                : galaxyCfgs.Keys.First();
            ApplyActiveGalaxyConfig();
            ResetTransientState();
            return true;
        }

        /// <summary>Принять галактики из сейва. false — сейв пуст.</summary>
        public bool LoadFrom(Dictionary<string, GalaxyData> loaded, string activeKey)
        {
            if (loaded == null || loaded.Count == 0) return false;
            Galaxies = loaded;
            foreach (var kv in Galaxies)
            {
                // Индексы (StarsMap/PlanetsMap/…) и обратные ссылки не сериализуются.
                kv.Value.InitializeLookups();
                // Восстанавливаем Key на случай старых сейвов, где поле было null.
                if (string.IsNullOrEmpty(kv.Value.Key)) kv.Value.Key = kv.Key;
            }
            ActiveGalaxyKey = !string.IsNullOrEmpty(activeKey) && Galaxies.ContainsKey(activeKey)
                ? activeKey
                : Galaxies.Keys.First();
            ApplyActiveGalaxyConfig();
            foreach (var g in Galaxies.Values)
            {
                g.InitSimulation(resetOrbits: false);
                foreach (var star in g.StarsMap.Values)
                    foreach (var ship in star.Ships)
                        ship?.Brain?.RestoreAfterLoad(ship);
            }
            ResetTransientState();
            return true;
        }

        /// <summary>SpawnSystem: регистрация политик и пересчёт счётчиков после загрузки.</summary>
        public void RecountSpawns()
        {
            SpawnSystem.RegisterDefaultPolicies();
            foreach (var g in Galaxies.Values)
                SpawnSystem.RecountFromGalaxy(g, Context?.Config);
        }

        /// <summary>Сменить активную галактику. Игрока не переносит.</summary>
        public bool SwitchActiveGalaxy(string galaxyKey)
        {
            if (string.IsNullOrEmpty(galaxyKey)) return false;
            if (!Galaxies.ContainsKey(galaxyKey)) return false;
            ActiveGalaxyKey = galaxyKey;
            ApplyActiveGalaxyConfig();
            // Инвалидация per-turn кэшей, ключей которых нет galaxyKey — иначе они возвращают
            // stale-данные из прошлой галактики (multi-galaxy safety):
            //   TraderAI._reachableCache хранит (originUid → List<StarData>) — StarData ссылки чужие.
            //   ShipSpatialHash — гриды на starUid, звёзды в новой галактике теже UID иметь не должны,
            //   но CleanupStale работает по turn — сбрасываем явно.
            TraderAI.ResetReachableCache();
            ShipSpatialHash.ResetAll();
            return true;
        }

        /// <summary>Ключ галактики, где находится звезда, либо null.</summary>
        public string FindGalaxyOfStar(string starUid)
        {
            if (string.IsNullOrEmpty(starUid)) return null;
            foreach (var kv in Galaxies)
                if (kv.Value != null && kv.Value.StarsMap.ContainsKey(starUid))
                    return kv.Key;
            return null;
        }

        /// <summary>
        /// Сбросить статические кэши симуляции, привязанные к объектам прошлого мира.
        /// Вызывается при новой игре и загрузке: иначе кэши (ключ — UID/номер хода) отдают
        /// ссылки на StarData/ShipData из мира, который уже не существует, а номера ходов
        /// после загрузки совпадают со старыми.
        /// </summary>
        private void ResetTransientState()
        {
            TraderAI.ResetReachableCache();
            ShipSpatialHash.ResetAll();
            SRG.Dialog.PlanetGreetings.PlanetGreetingSelector.ResetCache();
            GalaxyNewsService.SyncNextId(Galaxies.Values);
        }

        private void ApplyActiveGalaxyConfig()
        {
            if (Context?.Config?.Galaxies != null
                && Context.Config.Galaxies.TryGetValue(ActiveGalaxyKey, out var cfg))
                Context.ActiveGalaxyConfig = cfg;
        }

        // ── Ход ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Рассчитать один день: GalaxyNextDay всех галактик (сначала активная — её данные
        /// анимации и возвращаются), миграции кораблей между звёздами, AI NPC активной галактики.
        /// </summary>
        public TurnAnimationData SimulateDay()
        {
            var active = ActiveGalaxy;
            if (active == null) return null;

            var sw = Stopwatch.StartNew();
            var swNext = Stopwatch.StartNew();
            TurnAnimationData result;
            CurrentTickingGalaxy = active;
            try { result = active.GalaxyNextDay(Context); }
            finally { CurrentTickingGalaxy = null; }
            // Все прочие галактики тоже тикают экономику/оккупации/орбиты, но их анимация не показывается.
            foreach (var kv in Galaxies)
            {
                if (kv.Key == ActiveGalaxyKey) continue;
                var prev = Context.ActiveGalaxyConfig;
                if (Context.Config?.Galaxies != null && Context.Config.Galaxies.TryGetValue(kv.Key, out var cfg))
                    Context.ActiveGalaxyConfig = cfg;
                CurrentTickingGalaxy = kv.Value;
                try { kv.Value.GalaxyNextDay(Context); }
                finally { CurrentTickingGalaxy = null; Context.ActiveGalaxyConfig = prev; }
            }
            swNext.Stop();

            var swMig = Stopwatch.StartNew();
            // Применяем "ожидающие" миграции (CurrentStarUid сменился в FinalizeTurn) СРАЗУ —
            // чтобы NpcSystem.TickAllSystems увидел корабли в их новых системах и AI-приказ
            // для следующего хода считался относительно правильной (целевой) звезды.
            // Это критично для гиперперехода: корабль завершил HyperEnter (CurrentStarUid=dest),
            // и его brain.Tick на этом же ходу должен принять решение в новой системе.
            active.MigrateShipsBetweenStars();
            foreach (var kv in Galaxies)
                if (kv.Key != ActiveGalaxyKey) kv.Value.MigrateShipsBetweenStars();
            swMig.Stop();

            var swNpc = Stopwatch.StartNew();
            NpcSystem.TickAllSystems(active, Context);
            // NPC-логика для неактивных галактик — пока минимальная (движение/бой не проигрывается,
            // но состояние живо). Расширять NpcSystem до мультигалактики — задача следующего этапа.
            swNpc.Stop();
            sw.Stop();

            PerfLog.Log($"[SimulationSession] Turn {active.CurrentTurn} split: " +
                $"GalaxyNextDay={swNext.ElapsedMilliseconds}ms " +
                $"Migrate={swMig.ElapsedMilliseconds}ms " +
                $"NpcTick={swNpc.ElapsedMilliseconds}ms");
            // Раз в 30 ходов скидываем буфер EconomicLog+HighCommandLog на диск — на случай
            // аварийного выхода данные не теряются. Внутри ещё есть авто-flush по FlushThreshold.
            if (active.CurrentTurn % 30 == 0)
            {
                EconomicLog.Flush();
                HighCommandLog.Flush();
                NewsLog.Flush();
            }
            PerfLog.Log($"[SimulationSession] Turn {active.CurrentTurn} calc: {sw.ElapsedMilliseconds} ms");
            PerfLog.Flush();
            return result;
        }

        /// <summary>
        /// Финализировать фазы гиперперехода для всех кораблей всех звёзд. Вызывается после
        /// анимации хода: переход к следующей фазе не должен «съесть» визуал текущей.
        /// </summary>
        public void FinalizeHyperjumpPhases()
        {
            foreach (var g in Galaxies.Values)
            {
                if (g == null) continue;
                foreach (var star in g.StarsMap.Values)
                {
                    for (int i = 0; i < star.Ships.Count; i++)
                    {
                        var ship = star.Ships[i];
                        if (ship == null || ship.HyperjumpPhase == HyperjumpPhase.None) continue;
                        HyperjumpController.FinalizeTurn(ship, star, g);
                    }
                }
            }
        }
    }
}
