using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Spawning.Policies;
using SRG.Utils;
using SRG.Ships;
using SRG.Utils;

namespace SRG.NpcAI.Spawning
{
    /// <summary>
    /// Главный тикер top-up системы спавна.
    /// Раз в день:
    ///   1. Сбрасывает Star.SpawnsToday в 0 (per-day квота, лимит = GameSettings.MaxShipsSpawnedPerStarPerDay).
    ///   2. Пересчитывает <see cref="GalaxyShipCounters"/> и <see cref="DominationFlags"/>.
    ///   3. Прогоняет все зарегистрированные <see cref="ISpawnPolicy"/>.
    ///
    /// Хуки <see cref="RegisterSpawn"/>/<see cref="RegisterDeath"/> вызываются из мест,
    /// где корабли реально создаются/умирают — для инкрементального обновления счётчиков.
    /// RegisterSpawn также инкрементит star.SpawnsToday.
    /// </summary>
    public static class SpawnSystem
    {
        public static GalaxyShipCounters Counters { get; private set; } = new();
        public static DominationFlags Domination { get; private set; } = new();

        private static readonly List<ISpawnPolicy> _policies = new();

        /// <summary>Регистрация политики (стандартные — в RegisterDefaultPolicies; модовые — извне).</summary>
        public static void Register(ISpawnPolicy policy)
        {
            if (policy == null) return;
            _policies.Add(policy);
        }

        public static void ClearPolicies() => _policies.Clear();

        /// <summary>Стандартный набор политик. Вызывается один раз при инициализации игры/загрузке.</summary>
        public static void RegisterDefaultPolicies()
        {
            ClearPolicies();
            Register(new CivilianSpawnPolicy("Transport"));
            Register(new CivilianSpawnPolicy("Liner"));
            Register(new CivilianSpawnPolicy("Diplomat"));
            Register(new WarriorSpawnPolicy());
            Register(new RangerSpawnPolicy());
            Register(new PirateSpawnPolicy());
            Register(new LinkorSpawnPolicy());
            Register(new DominatorSpawnPolicy());
        }

        /// <summary>Полный пересчёт счётчиков из текущей галактики — после загрузки или генерации.</summary>
        public static void RecountFromGalaxy(GalaxyData galaxy, GalaxyConfig cfg)
        {
            Counters.RecalculateFromScratch(galaxy);
            Domination = DominationCalculator.Recalculate(Counters, cfg);
        }

        /// <summary>Главный тик. Вызывается из GalaxyData.GalaxyNextDay один раз в день.</summary>
        public static void DailyTick(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            if (galaxy == null || ctx?.Config == null) return;

            // 1) Сброс per-day квоты спавнов. Лимит на систему живёт в GameSettingsConfig.
            foreach (var star in galaxy.StarsMap.Values)
                star.SpawnsToday = 0;

            // 2) Пересчёт состояния (полный O(N) — на текущий масштаб ~5к кораблей пренебрежимо дёшево)
            Counters.RecalculateFromScratch(galaxy);
            Domination = DominationCalculator.Recalculate(Counters, ctx.Config);

            // 3) Прогон политик
            var tickCtx = new SpawnTickContext
            {
                Galaxy = galaxy,
                Gen = ctx,
                Config = ctx.Config,
                Settings = GalaxyManager.Instance?.Settings,
                Counters = Counters,
                Domination = Domination,
                CurrentTurn = galaxy.CurrentTurn
            };
            var sb = new System.Text.StringBuilder("[SpawnSystem] Turn ").Append(galaxy.CurrentTurn).Append(':');
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < _policies.Count; i++)
            {
                sw.Restart();
                try { _policies[i].DailyTick(tickCtx); }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SpawnSystem] Policy {_policies[i].ShipTypeId} threw: {ex.Message}\n{ex.StackTrace}");
                }
                sw.Stop();
                sb.Append(' ').Append(_policies[i].ShipTypeId).Append('=').Append(sw.ElapsedMilliseconds);
            }
            SRG.Utils.PerfLog.Log(sb.ToString());
        }

        // ──────────────────────────────────────────
        // Хуки событий жизни корабля (инкрементальные счётчики)
        // ──────────────────────────────────────────

        public static void RegisterSpawn(ShipData ship, StarData star)
        {
            if (ship == null) return;
            Counters.OnShipSpawned(ship);
            if (star != null) star.SpawnsToday++;
        }

        public static void RegisterDeath(ShipData ship)
        {
            if (ship == null) return;
            Counters.OnShipDied(ship);
        }

        // ──────────────────────────────────────────
        // Helper: SpawnShip — общая точка создания корабля для всех политик
        // ──────────────────────────────────────────

        /// <summary>
        /// Создаёт корабль указанного типа у заданной планеты и регистрирует его в звезде.
        /// Возвращает null, если ShipFactory не смогла собрать корабль.
        /// </summary>
        public static ShipData SpawnShipAtPlanet(
            string shipTypeId, string ownerId, string raceId,
            PlanetData planet, StarData star, GalaxyGenerationContext ctx,
            string reason = null, bool landed = true)
        {
            if (planet == null || star == null || ctx == null) return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ship = ShipFactory.BuildShipData(shipTypeId, ownerId, raceId, ctx.AvailableShipTypes, ctx);
            long msBuild = sw.ElapsedMilliseconds; sw.Restart();
            if (ship == null) return null;

            Vector2 pos = landed
                ? OrbitMath.GetPlanetWorldPosition(planet)
                : Positions.Near(planet, Positions.SystemRadius(star) * 0.05f);

            ship.SpawnedFromUid    = planet.Uid;
            ship.HomePlanetUid     = planet.Uid;
            ship.HomeStarUid       = star.Uid;
            if (landed) ship.LandedPlanetUid = planet.Uid;
            ship.Position          = pos;
            ship.PreviousPosition  = pos;
            ship.TargetPosition    = pos;
            ship.CurrentStarUid    = star.Uid;
            ship.PreviousStarUid   = star.Uid;
            ship.CurrentStar       = star;

            EquipStarterKitIfAvailable(ship, shipTypeId, ctx);
            long msKit = sw.ElapsedMilliseconds; sw.Restart();
            ApplyStartingMoney(ship, shipTypeId, ctx);

            star.Ships.Add(ship);
            RegisterSpawn(ship, star);
            // Warm-up: прогреваем per-ship кэши прямо при спавне, чтобы первый прогон корабля
            // через StarSimulator/CombatSubTurn на следующем ходу не давал одномоментный спайк
            // (в дом-системах при массовом top-up было до 55 новых кораблей — Turn 23 хитч).
            WarmupNewShip(ship);
            long msRest = sw.ElapsedMilliseconds;
            if (msBuild + msKit + msRest >= 5)
                SRG.Utils.PerfLog.Log($"[SpawnShip] type={shipTypeId} race={raceId} build={msBuild} kit={msKit} rest={msRest}");

            EconomicLog.Spawn(0, EconomicLog.Safe(ship.Name ?? shipTypeId), "TOP_UP_SPAWN",
                $"type={shipTypeId} owner={ownerId} race={raceId} planet={EconomicLog.Safe(planet.Name)} reason={reason}");
            return ship;
        }

        /// <summary>Прогрев per-ship кэшей после создания и экипировки. Вызывать сразу после
        /// star.Ships.Add — все важные первичные расчёты (скорость, отсортированные weapon-слоты)
        /// уходят из первого хода в момент спавна, размазывая нагрузку. Безопасно вызывать
        /// повторно — методы идемпотентны.</summary>
        public static void WarmupNewShip(ShipData ship)
        {
            if (ship == null) return;
            ship._turnCachedSpeed = EquipmentSystem.CalculateSpeed(ship);
            ship.GetSortedWeaponSlots(); // построит _weaponSlotsCache
        }

        /// <summary>
        /// Применяет к кораблю стартовый капитал из SpawnConfig.Policies[shipTypeId].StartingMoney,
        /// масштабированный текущим GalaxyData.InflationFactor.
        /// Не перезаписывает уже выставленные деньги (если ship.Money > 0).
        /// </summary>
        public static void ApplyStartingMoney(ShipData ship, string shipTypeId, GalaxyGenerationContext ctx)
        {
            if (ship == null || ship.Money > 0) return;
            var policy = ctx?.Config?.Spawn?.GetPolicy(shipTypeId);
            int baseMoney = policy?.StartingMoney ?? 0;
            if (baseMoney <= 0) return;

            float inflation = GalaxyManager.Instance?.GeneratedGalaxy?.InflationFactor ?? 1f;
            ship.Money = Mathf.RoundToInt(baseMoney * inflation);
        }

        // ──────────────────────────────────────────
        // Станции (см. план «Космические станции»)
        // ──────────────────────────────────────────

        private static readonly string[] CoalitionStationCodes = { "BK", "CB", "MC", "PB", "RC", "RG", "WB", "SB" };
        private static readonly string[] DominatorStationCodes = { "Blazer", "Keller", "Terron" };

        /// <summary>Генерация станций для всей галактики: 0–3 станции на сектор, разложенные по случайным
        /// звёздам сектора. Вызывается один раз из GalaxyGenerator после расстановки владельцев/планет.</summary>
        public static void SpawnStationsForGalaxy(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            if (galaxy?.Sectors == null || ctx == null) return;
            int total = 0;
            foreach (var sector in galaxy.Sectors)
            {
                if (sector?.Stars == null || sector.Stars.Count == 0) continue;
                int count = Random.Range(0, 4); // 0..3
                for (int i = 0; i < count; i++)
                {
                    var star = sector.Stars[Random.Range(0, sector.Stars.Count)];
                    if (SpawnStationInStar(star, ctx) != null) total++;
                }
            }
            Debug.Log($"[SpawnSystem] Станций создано при генерации: {total} (секторов: {galaxy.Sectors.Count}).");
        }

        /// <summary>Создаёт станцию (неподвижный ShipData с IsStation) в заданной звезде: выбирает корпус
        /// (доминаторские — только в доминаторских системах), размещает вне орбит планет, экипирует
        /// (вес ×2) и инициализирует Settlement. Возвращает null при неудаче.</summary>
        public static ShipData SpawnStationInStar(StarData star, GalaxyGenerationContext ctx, string codeOverride = null)
        {
            if (star == null || ctx == null) return null;

            bool dominator = !string.IsNullOrEmpty(star.Race)
                && star.Race.StartsWith("RaceDominators", System.StringComparison.Ordinal);
            string code = codeOverride;
            if (string.IsNullOrEmpty(code))
            {
                var pool = dominator ? DominatorStationCodes : CoalitionStationCodes;
                code = pool[Random.Range(0, pool.Length)];
            }
            string shipTypeId = "Station_" + code;

            string ownerId = !string.IsNullOrEmpty(star.ResolvedOwnerId) ? star.ResolvedOwnerId : star.Owner;
            string raceId  = star.Race;

            var ship = ShipFactory.BuildShipData(shipTypeId, ownerId, raceId, ctx.AvailableShipTypes, ctx);
            if (ship == null) return null;

            // Флаг ставим ДО экипировки — от него зависит удвоение веса оборудования (ItemFactory).
            ship.IsStation = true;
            ship.TurnSpeedDeg = 0f;

            float radiusUnits = ChooseStationOrbitRadius(star);
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector2 pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * SRUnits.ToWorld(radiusUnits);
            ship.Position         = pos;
            ship.PreviousPosition = pos;
            ship.TargetPosition   = pos;
            ship.CurrentStarUid   = star.Uid;
            ship.PreviousStarUid  = star.Uid;
            ship.CurrentStar      = star;
            ship.HomeStarUid      = star.Uid;

            EquipStarterKitIfAvailable(ship, shipTypeId, ctx);
            ApplyStartingMoney(ship, shipTypeId, ctx);
            ship.FreezeRoute();

            InitStationSettlement(ship, ctx);

            star.Ships.Add(ship);
            RegisterSpawn(ship, star);
            WarmupNewShip(ship);

            EconomicLog.Spawn(0, EconomicLog.Safe(ship.Name ?? shipTypeId), "STATION_SPAWN",
                $"type={shipTypeId} owner={ownerId} race={raceId} star={EconomicLog.Safe(star.Name)} r={radiusUnits:F0}");
            return ship;
        }

        /// <summary>Выбирает радиус орбиты станции (в игровых единицах OrbitRadius): дальше ближайшей планеты
        /// и не пересекая ни одной орбиты (запас 300 по обе стороны). Без планет — 2000 ± 500.</summary>
        private static float ChooseStationOrbitRadius(StarData star)
        {
            const float Margin = 300f;
            var planets = star.Planets;
            if (planets == null || planets.Count == 0)
                return 2000f + Random.Range(-500f, 500f);

            // Занятые кольца [lo, hi] с учётом эксцентриситета и запаса.
            var bands = new List<(float lo, float hi)>(planets.Count);
            float outer = 0f;
            foreach (var p in planets)
            {
                float e = Mathf.Clamp(p.OrbitEccentricity, 0f, 0.99f);
                float lo = p.OrbitRadius * (1f - e) - Margin;
                float hi = p.OrbitRadius * (1f + e) + Margin;
                bands.Add((lo, hi));
                if (hi > outer) outer = hi;
            }
            bands.Sort((a, b) => a.lo.CompareTo(b.lo));

            // Кандидаты: зазоры между соседними кольцами + за внешним кольцом.
            var candidates = new List<float>();
            for (int i = 0; i < bands.Count - 1; i++)
            {
                float gapLo = bands[i].hi;
                float gapHi = bands[i + 1].lo;
                if (gapHi - gapLo > 100f)
                    candidates.Add(Random.Range(gapLo, gapHi));
            }
            candidates.Add(outer + Random.Range(200f, 700f)); // всегда валиден: дальше всех орбит

            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>Инициализирует инфраструктуру станции. Станции — НЕ поселения: у них нет населения,
        /// экономики, политического строя, науки и собственного ПТУ (эти поля Settlement остаются
        /// пустыми). Есть только то, что нужно игроку при стыковке: магазин товаров и магазин
        /// оборудования (наполняются лениво; ассортимент оборудования ориентируется на галактический
        /// ГТУ, а не на ПТУ), ангар и связь с командиром.</summary>
        private static void InitStationSettlement(ShipData ship, GalaxyGenerationContext ctx)
        {
            ship.Settlement ??= new SettlementData();
            // Population / EconomyType / Government / TechLevel(ПТУ) / наука — намеренно НЕ задаются.
        }

        private static void EquipStarterKitIfAvailable(ShipData ship, string shipTypeId, GalaxyGenerationContext ctx)
        {
            if (ctx.AvailableShipTypes.TryGetValue(shipTypeId, out var shipTypeCfg)
                && (shipTypeCfg.StarterKit != null || shipTypeCfg.StarterWeapons != null)
                && ctx.ItemsConfig != null)
            {
                var kit = ResolveStarterKitPlaceholders(shipTypeCfg.StarterKit);
                ItemFactory.EquipStarterKit(ship, kit, ctx.ItemsConfig, ctx.Config, shipTypeCfg.StarterWeapons);
                RollNpcImprovements(ship, ctx);
            }
            else
            {
                ship.MaxHull = 100;
                ship.CurrentHull = ship.MaxHull;
            }
        }

        /// <summary>Резолвит плейсхолдер `<GTU>` в id-шниках StarterKit — используется станциями,
        /// чей техноуровень при спавне берётся от текущего галактического ГТУ (1..10). Обычные типы
        /// не содержат плейсхолдера, для них возвращается исходный словарь без копирования.</summary>
        private static System.Collections.Generic.Dictionary<string, string> ResolveStarterKitPlaceholders(
            System.Collections.Generic.Dictionary<string, string> kit)
        {
            if (kit == null) return null;
            bool hasPlaceholder = false;
            foreach (var v in kit.Values)
                if (v != null && v.Contains("<GTU>")) { hasPlaceholder = true; break; }
            if (!hasPlaceholder) return kit;

            int gtu = Mathf.Clamp(GalaxyManager.Instance?.GeneratedGalaxy?.GtuLevel ?? 1, 1, 10);
            string suffix = gtu.ToString();
            var copy = new System.Collections.Generic.Dictionary<string, string>(kit.Count);
            foreach (var kv in kit)
                copy[kv.Key] = kv.Value?.Replace("<GTU>", suffix);
            return copy;
        }

        /// <summary>Раскатать шанс SB-апгрейда на установленное оборудование NPC при спавне.
        /// См. <see cref="SRG.Equipment.ImprovementConfig.NpcSpawnChance"/>, docs/modules/equipment_improvement.md.</summary>
        private static void RollNpcImprovements(ShipData ship, GalaxyGenerationContext ctx)
        {
            var cfg = ctx?.ItemsConfig?.Improvement;
            if (cfg == null || cfg.NpcSpawnChance <= 0f) return;
            if (ship?.Equipment?.Slots == null) return;

            var rng = new System.Random(ship.Uid?.GetHashCode() ?? System.Environment.TickCount);
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item) || item == null) continue;
                if (rng.NextDouble() >= cfg.NpcSpawnChance) continue;
                SRG.Equipment.ImprovementService.TryApplyNpcImprovement(item, ship, cfg, rng);
            }
        }
    }
}
