using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Galaxy
{
    public class ExpansionEdge
    {
        public string RaceKey { get; set; }
        public Vector2 From { get; set; }
        public Vector2 To { get; set; }
        public int ColonizationYear { get; set; }
    }

    public class GalaxyData
    {
        /// <summary>Ключ галактики (соответствует ключу в GalaxyConfig.Galaxies и PremadeConfig.Galaxies).
        /// Заполняется генератором; для сейвов, созданных до появления мультигалактики, остаётся null
        /// и трактуется как <see cref="GalaxyConstants.DEFAULT_GALAXY_KEY"/>.</summary>
        public string Key { get; set; }
        public int Width;
        public int Height;
        public int CurrentTurn = 0;
        public string StartDateString = "01.01.0001";
        public int GtuLevel { get; set; } = 1;
        /// <summary>Глобальный множитель ко всем ценам. 1.0 на старте, линейно растёт (~×2 за 40 лет).
        /// Применяется в TradeSystem (цены товаров) и ItemFactory (цены оборудования при спавне).</summary>
        public float InflationFactor { get; set; } = 1.0f;

        public List<SectorData> Sectors = new List<SectorData>();
        public List<ExpansionEdge> ExpansionEdges { get; set; } = new();

        /// <summary>
        /// Uid звёзд, которые игрок посетил хотя бы раз.
        /// Используется галакартой для определения «открытых» секторов.
        /// </summary>
        public HashSet<string> VisitedStarUids { get; set; } = new HashSet<string>();

        /// <summary>Uid планет, на которые игрок хоть раз садился.
        /// Заполняется <see cref="SRG.Controllers.PlayerShip"/> при завершении посадки;
        /// используется в приветствиях планет (CurPlanetVisited).</summary>
        public HashSet<string> VisitedPlanetUids { get; set; } = new HashSet<string>();

        /// <summary>
        /// Персональные дельты отношений между конкретными парами сущностей (ship/planet/star UID).
        /// Ключ: нормализованная пара "uidA-uidB" (sorted). Значение: смещение -99..+99, накладывается
        /// поверх фракционного отношения (Owner+Race). Sparse: запись существует только когда между
        /// конкретной парой что-то происходило. Управляется через OwnerRaceRelationsManager / Relations API.
        /// </summary>
        public Dictionary<string, int> PersonalRelations { get; set; } = new();

        /// <summary>Глобальное состояние науки: завершённые/активные изобретения и динамические
        /// модификаторы цен/событий/шаблонов (см. ScienceSystem, диздок docs/planetary_science_system.md).</summary>
        public GalacticResearchState ResearchState { get; set; } = new();

        /// <summary>Активные фракционные директивы (Attack/Defend/etc). Живут вместе с сейвом.
        /// Управляются через <see cref="DirectiveManager"/> — прямая правка не нужна.
        /// TypeNameHandling.Auto в GalaxySaveManager сохраняет конкретный подтип Directive.</summary>
        public List<Directive> Directives { get; set; } = new();

        /// <summary>Состояния генштабов фракций (стратегия + счётчик до следующей оценки).
        /// Ключ = OwnerId либо OwnerId|RaceId. Восстанавливаются через <see cref="HighCommandRegistry"/>.</summary>
        public Dictionary<string, HighCommandStateData> HighCommandStates { get; set; } = new();

        /// <summary>Лента новостей галактики (инфоцентр планетарной формы). FIFO-буфер,
        /// ограничен <see cref="GameSettingsConfig.NewsMaxCount"/>. Заполняется через
        /// <see cref="GalaxyNewsService"/> из подписок на игровые события.</summary>
        public List<GalaxyNewsEntry> News { get; set; } = new();

        /// <summary>Курсор chunked-анализа <see cref="StarPresenceService"/> (какая звезда следующая).
        /// Живёт в сейве и у каждой галактики свой — иначе фоновые галактики сбивали курсор активной.</summary>
        public int PresenceCursor { get; set; }

        /// <summary>Последние опубликованные «корзины» присутствия (ключ = starUid|категория) —
        /// чтобы не постить одну и ту же новость каждый тик. Живёт в сейве.</summary>
        public Dictionary<string, string> PresenceLastPost { get; set; } = new();

        /// <summary>Полностью разгромленные «сюжетные» фракции (Owner-ключи). Заполняется
        /// внешними подсистемами (кампания, скрипты); используется в приветствиях планет
        /// (PlanetGreetingRule.FactionDefeated / AnyMajorFactionDefeated).</summary>
        public HashSet<string> DefeatedFactions { get; set; } = new();

        [JsonIgnore] public Dictionary<string, StarData> StarsMap = new();
        [JsonIgnore] public Dictionary<string, PlanetData> PlanetsMap = new();
        [JsonIgnore] public Dictionary<string, SectorData> SectorsMap = new();

        private static readonly System.Globalization.DateTimeStyles _dateStyles = System.Globalization.DateTimeStyles.None;
        [JsonIgnore] private DateTime _cachedStartDate;
        [JsonIgnore] private string _cachedStartDateString;

        private DateTime GetStartDate()
        {
            if (_cachedStartDateString == StartDateString) return _cachedStartDate;
            _cachedStartDateString = StartDateString;
            if (!DateTime.TryParseExact(StartDateString, "dd.MM.yyyy", null, _dateStyles, out _cachedStartDate))
                _cachedStartDate = new DateTime(1, 1, 1);
            return _cachedStartDate;
        }

        public string GetCurrentDate() => GetStartDate().AddDays(CurrentTurn).ToString("dd.MM.yyyy");

        public void InitializeLookups()
        {
            StarsMap.Clear(); PlanetsMap.Clear(); SectorsMap.Clear();

            foreach (var c in Sectors)
            {
                TryAdd(SectorsMap, c.Uid, c);
                foreach (var star in c.Stars)
                {
                    star.ParentSector = c;
                    TryAdd(StarsMap, star.Uid, star);
                    foreach (var planet in star.Planets) { planet.ParentStar = star; TryAdd(PlanetsMap, planet.Uid, planet); }
                }
            }
        }
        public const int SubTurnsPerTurn = 10;

        /// <summary>Рассчитать один день галактики (см. <see cref="SRG.Galaxy.Simulation.GalaxySimulator"/>).</summary>
        public TurnAnimationData GalaxyNextDay(GalaxyGenerationContext ctx = null)
            => SRG.Galaxy.Simulation.GalaxySimulator.SimulateDay(this, ctx);

        public void MigrateShipsBetweenStars()
        {
            var migrants = new System.Collections.Generic.List<(StarData from, ShipData ship)>();
            foreach (var star in StarsMap.Values)
                for (int i = star.Ships.Count - 1; i >= 0; i--)
                {
                    var ship = star.Ships[i];
                    if (string.IsNullOrEmpty(ship.CurrentStarUid) || ship.CurrentStarUid == star.Uid) continue;
                    migrants.Add((star, ship));
                }
            foreach (var (fromStar, ship) in migrants)
            {
                fromStar.Ships.Remove(ship);
                if (StarsMap.TryGetValue(ship.CurrentStarUid, out var toStar))
                {
                    ship.CurrentStar = toStar;
                    // Гиперпереход: корабль появляется в точке прибытия на краю новой системы.
                    // Миграция происходит после CompleteJump (HyperEnter → HyperArrive), поэтому
                    // проверяем именно фазу HyperArrive. На следующем ходу (HyperExit) корабль
                    // уже физически лежит в новой звезде — сюда не заходим.
                    if (ship.HyperjumpPhase == HyperjumpPhase.HyperArrive)
                    {
                        ship.Position = ship.HyperjumpArrivalEdge;
                        ship.PreviousPosition = ship.HyperjumpArrivalEdge;
                        ship.CurrentHeading = ship.HyperjumpHeading;
                        // TargetPosition сброшен в ArrivalEdge (см. CompleteJump) — корабль
                        // стоит невидимо весь ход HyperArrive. Настоящую цель Brain.Tick
                        // выставит при переходе HyperArrive → HyperExit.
                        ship.Waypoints?.Clear();
                        ship.WaypointIndex = 0;
                    }
                    toStar.Ships.Add(ship);
                }
            }
        }

        /// <param name="resetOrbits">true — после генерации: планеты ставятся в стартовую точку
        /// орбиты (InitialAngle). false — после загрузки сейва: сохранённые углы не трогаем,
        /// иначе планеты «прыгают» в начальное положение и игра идёт иначе, чем без сохранения.</param>
        public void InitSimulation(bool resetOrbits = true)
        {
            foreach (var c in Sectors)
                foreach (var star in c.Stars)
                {
                    if (resetOrbits)
                        foreach (var planet in star.Planets)
                            planet.InitRotation();

                    // Реконсиляция инвентарей после десериализации: uid-индекс и теневые
                    // ItemInstance стеков (включая пересчёт инвентарных иконок по ItemsConfig).
                    foreach (var ship in star.Ships)
                        ship.Inventory?.RebuildIndex();
                }

            RebuildPartnerFollowerLists();
        }

        /// <summary>Пересобирает PartnerFollowerUids на каждом корабле-лидере из
        /// сериализованных PartnerLeaderUid всех кораблей галактики. Вызывается
        /// после десериализации, а также после SetPartner для поддержания синхронизации.</summary>
        public void RebuildPartnerFollowerLists()
        {
            var byUid = new Dictionary<string, ShipData>();
            foreach (var sector in Sectors)
                foreach (var star in sector.Stars)
                    foreach (var ship in star.Ships)
                    {
                        ship.PartnerFollowerUids ??= new List<string>();
                        ship.PartnerFollowerUids.Clear();
                        byUid[ship.Uid] = ship;
                    }

            foreach (var ship in byUid.Values)
            {
                if (string.IsNullOrEmpty(ship.PartnerLeaderUid)) continue;
                if (byUid.TryGetValue(ship.PartnerLeaderUid, out var leader))
                    leader.PartnerFollowerUids.Add(ship.Uid);
                else
                    ship.PartnerLeaderUid = null; // «висячая» ссылка — сбрасываем
            }
        }

        private static void TryAdd<T>(Dictionary<string, T> map, string key, T value)
        {
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key)) map.Add(key, value);
        }
    }
}
