using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Presentation.World;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Movement;
using SRG.Ships.Services;

namespace SRG.Galaxy
{
    #region Interfaces

    public interface IGalaxyEntity
    {
        string Uid { get; }
        string Owner { get; }
        string Race { get; }
        Dictionary<string, string> CustomProperties { get; }
    }

    #endregion

    /// <summary>Явные приказы лидера партнёру. Обрабатываются в ActionPartnerAttend
    /// приоритетнее личностного поведения; по выполнении сбрасываются в None.</summary>
    public enum PartnerOrderKind
    {
        None      = 0,
        FlyToMe   = 1,   // TargetUid не используется — цель = сам лидер
        Attack    = 2,   // TargetUid = UID корабля-жертвы
        LandOn    = 3,   // TargetUid = UID планеты (или UID корабля, если сажать на подвижный носитель — будущий дрон)
        FlyToStar = 4,   // TargetUid = UID звезды
    }

    public class TurnAnimationData
    {
        // Симуляция: позиции на каждом субходе (для коллизий, NPC, интерполяции в SystemViewManager)
        public Dictionary<string, ShipSubTurnFrames> ShipFrames = new();
        public Dictionary<string, AsteroidSubTurnFrames> AsteroidFrames = new();
        public Dictionary<string, MissileSubTurnFrames> MissileFrames = new();

        // Рендер: плотный путь вдоль кривой Безье, заполняется на каждом вейпоинте (только для визуала)
        public Dictionary<string, List<Vector2>> ShipRenderPaths = new();

        public Dictionary<string, (float From, float To)> PlanetAngles = new();
        public List<ShotEvent> Shots = new();
        public List<string> DeathUids = new();
        public List<PlanningReason> PlanningReasons = new();
        public List<AsteroidSpawnEvent> AsteroidSpawns = new();
        public List<AsteroidDestroyEvent> AsteroidDestroys = new();
        // Ракета считается «мёртвой» начиная с этого сабтёрна (попадание/возврат/таймаут).
        // Спрайт показывается во время анимации сабтёрна смерти (подлёт к цели), затем
        // полностью гасится — иначе он висел бы до конца хода в точке impact'а.
        public Dictionary<string, int> MissileDeathUids = new();
        // Подмножество MissileDeathUids: ракеты, которые исчезли «тихо», без визуального взрыва
        // (торпеда вернулась к стрелявшему и восстановила боезапас). По умолчанию (uid отсутствует
        // в этом наборе) ракета на смерти показывает анимацию взрыва.
        public HashSet<string> MissileSilentDeathUids = new();
    }
    public class AsteroidSubTurnFrames
    {
        public Vector2[] SubTurns = new Vector2[11];
    }
    public class MissileSubTurnFrames
    {
        public Vector2[] SubTurns = new Vector2[11];
    }

    public class ShipSubTurnFrames
    {
        public Vector2[] SubTurns = new Vector2[11];
    }

    public struct ShotEvent
    {
        public string AttackerUid;
        public string TargetUid;
        public string WeaponId;
        public int DamageDealt;
        public int SubTurn;
        public int ShotDuration;
        public HitPattern HitPattern;
        public DamageType DamageType;
        public WeaponVisualConfig Visual;    // палитра или путь к спрайту; null = умолчание по DamageType
        public HitEffectConfig    HitEffect; // параметры эффекта попадания; null = умолчание по паттерну
    }
    // Значения зафиксированы явно (не переставлять!): HyperjumpPhase сериализуется как int
    // в save-файлах. HyperArrive добавлена ПОЗЖЕ HyperExit/CancelEnd — поэтому её номер 5,
    // а не в логическом порядке между HyperEnter и HyperExit. Новые фазы добавлять только
    // с новыми числами в конце, иначе старые сохранения загрузятся с искажёнными фазами.
    public enum HyperjumpPhase
    {
        None        = 0, // Прыжок не идёт.
        Travel      = 1, // Корабль летит к краю системы в направлении цели (произвольное число ходов).
        HyperEnter  = 2, // Ход 1 из 3: в системе-источнике открывается портал (begin+mid),
                         //           корабль входит в него с fade-out. В конце хода —
                         //           списание топлива и миграция в целевую систему.
        HyperExit   = 3, // Ход 3 из 3: портал на стороне цели закрывается (end). Корабль появляется
                         //           у точки выхода (fade-in) и СРАЗУ летит к цели AI-приказа.
        CancelEnd   = 4, // Игрок отменил прыжок во время HyperEnter: на следующий ход портал закрывается (end) и удаляется.
        HyperArrive = 5, // Ход 2 из 3 (логически между HyperEnter и HyperExit): корабль уже в целевой
                         //           системе, стоит невидимо у точки выхода; портал на стороне цели
                         //           открывается (begin+mid). Для игрока — этот ход идёт СРАЗУ после
                         //           scene swap: камера ставится на HyperjumpArrivalEdge и проигрывается
                         //           анимация открытия. Корабль не двигается.
    }

    /// <summary>
    /// Двухфазная посадка (docs/modules/landing.md).
    /// </summary>
    public enum LandingPhase
    {
        None,           // Не садимся (LandingPlanetUid либо пуст, либо ещё не построен курс).
        Approach,       // Подлёт к посадочному кольцу.
        Fading,         // Финальный ход: конец маршрута внутри R_land, корабль растворяется.
    }

    public enum PlanningReason
    {
        PlayerInput,
        RouteCompleted,
        LowHull,
        AsteroidImpact,
        EnemyDetected,
        QuestTrigger,
        HeavyDamage,       // за ход получено >20% MaxHull урона (в режиме автобоя)
        EnemyDestroyed,    // цель следования уничтожена
    }

    [Serializable] public class AsteroidData
    {
        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string TypeId { get; set; }
        public Vector2 Position { get; set; }
        [JsonIgnore] public Vector2 PreviousPosition { get; set; }
        public Vector2 Velocity { get; set; }
        public float Mass { get; set; }
        public float CollisionRadius { get; set; }
        public string GraphicPath { get; set; }
        public string ExplosionPath { get; set; }
        public float SelfRotationSpeed { get; set; }
        public float AnimFps { get; set; } = 12f;

        [JsonIgnore] public bool IsDestroyed { get; set; }
        [JsonIgnore] public AsteroidSubTurnFrames SubTurnFrames = new AsteroidSubTurnFrames();

        /// <summary>UID планеты, к которой астероид приближается на «опасное» расстояние в текущем
        /// ходу (пересечение траектории с зоной поражения радиусом ~<see cref="AsteroidSystem"/>
        /// DangerCloseRadius). Заполняется при спавне и каждый ход в TickAsteroids. null = не угрожает.
        /// Используется для награды/новостей: астероид считается «сбитым по заказу» планеты, если
        /// в момент уничтожения это поле не null.</summary>
        [JsonIgnore] public string PredictedDangerPlanetUid;
    }

    [Serializable] public class ItemStack
    {
        public string ItemId { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public int TotalWeight { get; set; }
        public int BasePrice { get; set; }
        /// <summary>Классификация: стек — торговый товар (участвует в TraderAI/Shop/Inflation).
        /// Задаётся при создании стека из <see cref="ItemConfig.IsGoods"/>. Стек без флага —
        /// «useless» (стакабельные находки, минералы, квестовые предметы).</summary>
        public bool IsGoods { get; set; }
        /// <summary>Стек создан «естественным» источником (астероид → минерал/ноды), а не выброшен
        /// из трюма. Влияет только на графику дропа в космосе: если у ступени задан
        /// <see cref="StackGraphicStep.NaturalSprite"/>, он используется вместо обычного Sprite.</summary>
        public bool NaturalOrigin { get; set; }
        [JsonIgnore] public int TotalPrice => TotalWeight * BasePrice;
        /// <summary>«Не товар» — считается useless (см. <see cref="IsGoods"/>).</summary>
        [JsonIgnore] public bool IsUseless => !IsGoods;
    }

    public struct AsteroidSpawnEvent
    {
        public string AsteroidUid;
    }

    public struct AsteroidDestroyEvent
    {
        public string AsteroidUid;
        public Vector2 Position;
        public List<ItemInstance> DroppedItems;
        public List<ItemStack> DroppedStacks;
        public bool HitPlayer;
        public AsteroidCollisionType CollisionType;
        public int CollisionSubTurn;
        public float CollisionT;
        public string ExplosionPath;
    }

    #region Runtime Data

    public class ActivePlanetEvent
    {
        public string EventId { get; set; }
        public int RemainingMonths { get; set; } = -1; // -1 = бессрочное (до внешнего условия)
    }

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
        /// Заполняется <see cref="Ships.Player.PlayerShip"/> при завершении посадки;
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

        public TurnAnimationData GalaxyNextDay(GalaxyGenerationContext ctx = null)
        {
            CurrentTurn++;
            MigrateShipsBetweenStars();
            var anim = new TurnAnimationData();

            SRG.Galaxy.Simulation.StarSimulator.ResetTelemetry();
            var swStars = System.Diagnostics.Stopwatch.StartNew();
            foreach (var star in StarsMap.Values)
                star.StarNextDay(anim, ctx);
            swStars.Stop();
            SRG.Galaxy.Simulation.StarSimulator.LogTelemetry(CurrentTurn);

            long tTrade = 0, tEvt = 0, tSci = 0, tTech = 0, tShop = 0, tSpawn = 0, tWorm = 0, tInf = 0, tPres = 0, tRate = 0;
            if (ctx != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                TradeSystem.TickAll(this, ctx.Config);              sw.Stop(); tTrade = sw.ElapsedMilliseconds; sw.Restart();
                PlanetaryEventSystem.TickAll(this, ctx);            sw.Stop(); tEvt   = sw.ElapsedMilliseconds; sw.Restart();
                ScienceSystem.TickAll(this, ctx);                   sw.Stop(); tSci   = sw.ElapsedMilliseconds; sw.Restart();
                PlanetaryTechSystem.TickAll(this, ctx);             sw.Stop(); tTech  = sw.ElapsedMilliseconds; sw.Restart();
                EquipmentShopSystem.TickAll(this, ctx);             sw.Stop(); tShop  = sw.ElapsedMilliseconds; sw.Restart();
                SpawnSystem.DailyTick(this, ctx);                   sw.Stop(); tSpawn = sw.ElapsedMilliseconds; sw.Restart();
                WormholeSystem.DailyTick(this, ctx);                sw.Stop(); tWorm  = sw.ElapsedMilliseconds; sw.Restart();
                if (CurrentTurn % 30 == 0)
                    InflationSystem.TickMonthly(this, ctx.Config);
                sw.Stop(); tInf = sw.ElapsedMilliseconds; sw.Restart();
                // Аналитика/новости — плотность в звёздах (недельный тик) и рейтинги кораблей (по конфигу Ratings).
                StarPresenceService.TickIfDue(this);                sw.Stop(); tPres  = sw.ElapsedMilliseconds; sw.Restart();
                ShipRatingService.TickIfDue(this, ctx.Config);      sw.Stop(); tRate  = sw.ElapsedMilliseconds;
                // Fear-driven cargo drop (DropGoodsInFear): преследуемые с грузом сбрасывают часть трюма.
                SRG.Ships.Services.FearDropService.TickAll(this);
            }

            SRG.Utils.PerfLog.Log($"[GND] Turn {CurrentTurn}: stars={swStars.ElapsedMilliseconds}ms " +
                $"trade={tTrade} evt={tEvt} sci={tSci} tech={tTech} shop={tShop} " +
                $"spawn={tSpawn} worm={tWorm} inf={tInf} pres={tPres} rate={tRate}");

            // Спайк-диагностика на секции, которые дорого стреляли:
            // T184 evt=227мс (тут увидим, сколько планет тикнуло + сколько событий стартовало).
            // T322 shop=128мс (сколько магазинов оказалось в day-slot + сколько предметов
            // родилось/удалилось). Порог 30мс — чтобы шум не забивал типичный лог.
            if (tEvt >= 30)
                SRG.Utils.PerfLog.Log($"[GND-spike] Turn {CurrentTurn} evt: " +
                    $"planetsTicked={SRG.Science.PlanetaryEventSystem._diagPlanetsTicked} " +
                    $"eventsStarted={SRG.Science.PlanetaryEventSystem._diagEventsStarted} " +
                    $"lastId={SRG.Science.PlanetaryEventSystem._diagLastStartedEventId ?? "-"}");
            if (tShop >= 30)
                SRG.Utils.PerfLog.Log($"[GND-spike] Turn {CurrentTurn} shop: " +
                    $"shopsRefreshed={SRG.Economy.EquipmentShopSystem._diagShopsRefreshed} " +
                    $"itemsAdded={SRG.Economy.EquipmentShopSystem._diagItemsAdded} " +
                    $"itemsRemoved={SRG.Economy.EquipmentShopSystem._diagItemsRemoved}");

            return anim;
        }

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

        public void InitSimulation()
        {
            foreach (var c in Sectors)
                foreach (var star in c.Stars)
                {
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

    public class SectorData
    {
        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public Vector2 Center { get; set; }
        public List<StarData> Stars { get; set; } = new();
        public int? FixedStarsCount { get; set; }
        public string Owner { get; set; }
        public string Race { get; set; }

        /// <summary>Доминирующий владелец сектора. Вычисляется при генерации, не сохраняется в JSON.</summary>
        [JsonIgnore] public string ResolvedOwnerId { get; set; }
        /// <summary>Подсказка размещения из premade-конфига ("border"/"center"/"corner"/"neighbour"/"random"). Не сохраняется.</summary>
        [JsonIgnore] public string PlaceHint { get; set; }
        /// <summary>UID целевого сектора-соседа (только для PlaceHint="neighbour"). Не сохраняется.</summary>
        [JsonIgnore] public string NeighbourSectorUid { get; set; }

        /// <summary>
        /// Вершины многоугольника ячейки Вороного в координатах сетки галактики.
        /// Вычисляется при генерации; порядок вершин — CCW.
        /// </summary>
        public List<Vector2> VoronoiPolygon { get; set; } = new();

        /// <summary>
        /// Для каждого ребра i (от VoronoiPolygon[i] до VoronoiPolygon[(i+1)%n]):
        /// UID соседнего сектора, или null если ребро — внешняя граница галактики.
        /// </summary>
        public List<string> VoronoiEdgeNeighbors { get; set; } = new();

        /// <summary>Площадь ячейки Вороного. Вычисляется при BuildVoronoi, не сохраняется.</summary>
        [JsonIgnore] public float VoronoiArea { get; set; }

        [JsonConstructor] public SectorData() { }
        public SectorData(string name, Vector2 center) { Name = name; Center = center; }
    }

    public class StarData : IGalaxyEntity
    {
        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Type { get; set; }
        public string Color { get; set; }
        public Vector2 Position { get; set; }
        public string Owner { get; set; }
        public string Race { get; set; }
        public int GraphVar { get; set; }

        /// <summary>
        /// Доминирующий владелец звезды. Вычисляется при генерации, не сохраняется в JSON.
        /// Mixed если планеты принадлежат разным владельцам.
        /// </summary>
        [JsonIgnore] public string ResolvedOwnerId { get; set; }

        /// <summary>
        /// Кэш «эффективного контроллёра системы» — если все населённые планеты управляются одним владельцем,
        /// хранится он; иначе null. Пересчитывается <see cref="OccupationService.RecomputeSystemControl"/>
        /// при изменении оккупации любой планеты системы. Слушать переходы —
        /// через <see cref="OccupationService.OnSystemControlChanged"/>.
        /// </summary>
        [JsonIgnore] public string CurrentSystemController { get; set; }

        public int SystemSize { get; set; }
        public int BackgroundSeed { get; set; }
        public string MapIcon { get; set; }
        public string PlanetsNoneCount { get; set; }
        public float StarRadius { get; set; }

        public float MassSolar { get; set; } = 1f;
        public float RadiationMult { get; set; } = 1f;
        public float HabitableZoneMin { get; set; }
        public float HabitableZoneMax { get; set; }
        public float HabitableZoneMid { get; set; }
        public float FlareRisk { get; set; } = 0f;

        public List<PlanetData> Planets { get; set; } = new();
        public List<ShipData> Ships { get; set; } = new();
        // Графа связей между системами нет: соседство и маршруты AI считает HyperNavigation
        // по физическим координатам (бывший ConnectedStarUids никогда не заполнялся — удалён).
        public Dictionary<string, string> CustomProperties { get; private set; } = new();

        // Пользовательские препятствия для планировщика траектории — задаются вручную (по координатам).
        // Корабли в этой системе будут обходить их, как и саму звезду.
        public List<CircleObstacle> CircleObstacles { get; set; } = new();
        public List<PolygonObstacle> PolygonObstacles { get; set; } = new();

        public StarNameRenderData NameRenderData { get; set; }
        public List<AsteroidData> Asteroids { get; set; } = new();
        public int MaxAsteroids { get; set; } = -1;
        public List<ActiveMissile> ActiveMissiles { get; set; } = new();
        /// <summary>Активные червоточины в этой системе. Управляются <see cref="Simulation.WormholeSystem"/>.
        /// Пустой список у большинства систем. Ходит вместе с сейвом.</summary>
        public List<WormholeData> Wormholes { get; set; } = new();

        [JsonIgnore] public SectorData ParentSector { get; set; }
        [JsonIgnore] public bool IsPremade { get; set; }

        /// <summary>Сколько кораблей уже заспавнилось в этой звезде за текущий игровой день.
        /// Сбрасывается в 0 в начале хода (SpawnSystem.DailyTick), инкрементируется в RegisterSpawn.
        /// Гейт «не больше N спавнов в системе в день» настраивается через
        /// <see cref="Config.GameSettingsConfig.MaxShipsSpawnedPerStarPerDay"/>.</summary>
        [JsonIgnore] public int SpawnsToday;

        /// <summary>Сила кораблей одного Owner с разбивкой на боевую (не-Civilian по
        /// <see cref="NpcBrain.ResolveCombatClass"/>) и гражданскую составляющие.</summary>
        public struct OwnerPower
        {
            public float Combat;
            public float Civilian;
            public float Total => Combat + Civilian;
        }

        // Кеш суммарной «силы» (NpcBrain.CalculateStrength) кораблей по Owner + агрегаты звезды
        // (криминал, пиратская/военная сила) для реактивных директив. Пересчитывается раз в день
        // в StarNextDay; читается NpcBrain (системный flee), ГШ и DirectiveManager.
        [JsonIgnore] public Dictionary<string, OwnerPower> PowerByOwner { get; private set; } = new();
        [JsonIgnore] public int PowerCacheTurn { get; set; } = -1;
        [JsonIgnore] public float CrimeSum { get; private set; }
        [JsonIgnore] public float PiratePower { get; private set; }
        [JsonIgnore] public float MilitaryPower { get; private set; }
        /// <summary>Owner первого военного корабля звезды (не None/Mixed) — адресат авто-директив.</summary>
        [JsonIgnore] public string FirstMilitaryOwner { get; private set; }

        public void RebuildPowerCache(int currentTurn)
        {
            if (PowerCacheTurn == currentTurn) return;
            PowerByOwner.Clear();
            CrimeSum = 0f;
            PiratePower = 0f;
            MilitaryPower = 0f;
            FirstMilitaryOwner = null;
            for (int i = 0; i < Ships.Count; i++)
            {
                var ship = Ships[i];
                if (ship.CurrentHull <= 0) continue;
                string owner = ship.Owner ?? GalaxyConstants.OWNER_NONE_KEY;
                float strength = NpcBrain.CalculateStrength(ship);
                var cls = NpcBrain.ResolveCombatClass(ship.ShipTypeId);

                PowerByOwner.TryGetValue(owner, out var current);
                if (cls == CombatClass.Civilian) current.Civilian += strength;
                else current.Combat += strength;
                PowerByOwner[owner] = current;

                CrimeSum += ship.CrimeRating; // включая игрока — его криминал тоже триггерит SuppressPiracy
                if (cls == CombatClass.Pirate) PiratePower += strength;
                else if (cls == CombatClass.Military)
                {
                    MilitaryPower += strength;
                    if (FirstMilitaryOwner == null && !string.IsNullOrEmpty(ship.Owner)
                        && ship.Owner != GalaxyConstants.OWNER_NONE_KEY
                        && ship.Owner != GalaxyConstants.OWNER_MIXED_KEY)
                        FirstMilitaryOwner = ship.Owner;
                }
            }
            PowerCacheTurn = currentTurn;
        }

        /// <summary>Суммарная сила враждебных Owner'ов. combatOnly=true — только боевые корабли
        /// (Military/Pirate/Mercenary), без транспортов и прочих гражданских.</summary>
        public float GetHostilePower(string myOwner, string myRace, bool combatOnly = false)
        {
            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return 0f;
            float total = 0f;
            foreach (var kv in PowerByOwner)
            {
                if (kv.Key == myOwner) continue;
                if (rel.AreHostile(myOwner, kv.Key, myRace, null))
                    total += combatOnly ? kv.Value.Combat : kv.Value.Total;
            }
            return total;
        }

        /// <summary>Суммарная сила своих и не-враждебных Owner'ов. combatOnly=true — только боевые.</summary>
        public float GetFriendlyPower(string myOwner, string myRace, bool combatOnly = false)
        {
            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null)
            {
                PowerByOwner.TryGetValue(myOwner ?? "", out var ownOnly);
                return combatOnly ? ownOnly.Combat : ownOnly.Total;
            }
            float total = 0f;
            foreach (var kv in PowerByOwner)
            {
                if (kv.Key != myOwner && rel.AreHostile(myOwner, kv.Key, myRace, null)) continue;
                total += combatOnly ? kv.Value.Combat : kv.Value.Total;
            }
            return total;
        }

        /// <summary>Единый поиск корабля звезды по UID (вместо локальных копий в NpcAction/NpcOrder/NpcBrain).</summary>
        public ShipData FindShip(string uid, bool aliveOnly = false)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            for (int i = 0; i < Ships.Count; i++)
            {
                var s = Ships[i];
                if (s.Uid != uid) continue;
                if (aliveOnly && s.CurrentHull <= 0) return null;
                return s;
            }
            return null;
        }

        // Симуляция дневного хода вынесена в StarSimulator (этап T1 рефакторинга, июнь 2026):
        // StarData теперь содержит только данные и связанные с ними утилиты (Power-кэш для NpcBrain).
        public void StarNextDay(TurnAnimationData anim, GalaxyGenerationContext ctx = null)
            => StarSimulator.SimulateTurn(this, anim, ctx);
    }

    /// <summary>
    /// «Обитаемая инфраструктура» — общая сущность для планет и станций: правительство, экономика,
    /// население, магазины (товаров и оборудования), техуровень, наука и состояние оккупации.
    /// Вынесена из <see cref="PlanetData"/>, чтобы станции (<see cref="ShipData.Settlement"/>) получали
    /// все «планетные» возможности при стыковке без дублирования логики.
    /// </summary>
    public class SettlementData
    {
        public int Population { get; set; }
        public string EconomyType { get; set; }
        public string Government { get; set; }

        /// <summary>
        /// Кто держит объект под контролем сейчас. Пусто/null = не оккупирован (управляется Owner хоста).
        /// Работать через <see cref="Galaxy.Politics.OccupationService"/>, а не выставлять напрямую.
        /// </summary>
        public string OccupiedByOwner { get; set; }
        /// <summary>Ход, когда была установлена текущая оккупация. 0 если не оккупирован.</summary>
        public int OccupationStartTurn { get; set; }
        /// <summary>Посадка/стыковка запрещена всем, кроме членов стороны-контроллёра.</summary>
        public bool LandingBlocked { get; set; }

        public int TechLevel { get; set; } = 1;
        /// <summary>УСТАРЕВШЕЕ. Осталось ради совместимости; PTU производна от <see cref="InventionCredits"/>.</summary>
        public float PtuProgress { get; set; } = 0f;
        /// <summary>Сколько изобретений закрыто как MainContributor (≥40% поинтов). Из этого выводится PTU.</summary>
        public int InventionCredits { get; set; } = 0;
        /// <summary>Накопленные научные поинты по категориям (см. ScienceSystem.Distribute).</summary>
        public Dictionary<string, float> ResearchPoints { get; set; } = new();
        /// <summary>Социальный капитал (0..100). Гейтит участие в дорогих изобретениях.</summary>
        public float SocialCapital { get; set; } = 30f;
        public List<ActivePlanetEvent> ActiveEvents { get; set; } = new();

        public PlanetShop Shop { get; set; } = new();
        public PlanetEquipmentShop EquipmentShop { get; set; } = new();

        /// <summary>История BuyPrice по каждому товару (последние ~10 семплов). Для триггеров PriceAbove/Below.</summary>
        public Dictionary<string, List<int>> PriceHistory { get; set; } = new();
        /// <summary>Кулдаун событий: ход последнего истечения для каждого EventId.</summary>
        public Dictionary<string, int> EventCooldowns { get; set; } = new();
        /// <summary>Кэш базового стока товаров. Заполняется лениво в TradeSystem; не сериализуется.</summary>
        [JsonIgnore] public Dictionary<string, int> BaseStockCache { get; set; }
    }

    public class PlanetData : IGalaxyEntity, ILandingSite
    {
        LandingSiteKind ILandingSite.Kind => LandingSiteKind.Planet;
        // Позиция и радиус посадки — единый геометрический контракт ILandingSite. Делегируем
        // в существующие утилиты, чтобы формулы (BaseScale, LandingZoneMargin) оставались
        // в одном месте (PlanetGeometry / OrbitMath).
        Vector2 ILandingSite.CenterPosition => SRG.Utils.OrbitMath.GetPlanetWorldPosition(this);
        float ILandingSite.LandingRadius => SRG.Utils.PlanetGeometry.GetLandingRadius(this);

        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Size { get; set; }
        public int OrbitIndex { get; set; }
        public float OrbitRadius { get; set; }
        public float OrbitSpeed { get; set; }
        public float InitialAngle { get; set; }
        public float OrbitEccentricity;
        public float OrbitTiltDeg;
        public string Race { get; set; }
        public string Owner { get; set; }
        public string Type { get; set; }
        public string CurrentColor { get; set; }
        public string Graphic { get; set; }
        public string MaskGraphic { get; set; }
        public string Atmosphere { get; set; }
        public string OrbitalObjects { get; set; }
        public int SatellitesCount { get; set; }
        public int DaySpeed { get; set; }
        public int CloudsSpeed { get; set; }
        public float AxialTilt { get; set; } = 0f;
        public float CurrentAngle { get; set; }
        public float PreviousAngle { get; set; }
        /// <summary>Обитаемая инфраструктура планеты (правительство/экономика/магазины/наука/оккупация).
        /// Общая сущность с станциями — см. <see cref="SettlementData"/>.</summary>
        public SettlementData Settlement { get; set; } = new();
        public float SurfaceGravity { get; set; }
        public float Density { get; set; }

        // Физические и экологические характеристики
        public float GeoActivity { get; set; }
        public float SolarFlux { get; set; }
        public float MagneticField { get; set; }
        public float SurfaceRadiation { get; set; }
        public float AtmPressure { get; set; }
        [JsonIgnore] public float? AtmPressureFixed { get; set; }
        public string AtmComposition { get; set; }
        public float OxygenPercent { get; set; }
        [JsonIgnore] public float? OxygenPercentFixed { get; set; }
        [JsonIgnore] public bool HasFixedOrbitRadius;
        public float SurfaceTemp { get; set; }
        [JsonIgnore] public float? SurfaceTempFixed { get; set; }
        public float WaterAbundance { get; set; }
        [JsonIgnore] public float? WaterAbundanceFixed { get; set; }
        public int HydrologyState { get; set; }

        // Типы поверхности (% от общей площади, сумма = 100)
        public float SurfaceLiquid { get; set; }
        public float SurfacePlains { get; set; }
        public float SurfaceMountains { get; set; }
        // Общая площадь поверхности, условные млн км²
        public float TotalSurfaceArea { get; set; }

        [JsonIgnore] public float AreaLiquid    => TotalSurfaceArea * SurfaceLiquid    / 100f;
        [JsonIgnore] public float AreaPlains    => TotalSurfaceArea * SurfacePlains    / 100f;
        [JsonIgnore] public float AreaMountains => TotalSurfaceArea * SurfaceMountains / 100f;

        [JsonIgnore] public float? SurfaceLiquidFixed    { get; set; }
        [JsonIgnore] public float? SurfaceMountainsFixed { get; set; }

        // Планета в обитаемом диапазоне расы, но требует терраформирования для колонизации
        public bool IsTerraformable { get; set; }
        // Параметры планеты были приведены к оптимальным значениям расы-колонизатора
        public bool IsTerraformed { get; set; }
        // Исходные значения параметров до терраформирования (ключ — имя свойства)
        public Dictionary<string, float> TerraformOriginals { get; set; } = new();

        public Dictionary<string, string> CustomProperties { get; private set; } = new();
        public List<SatelliteData> Satellites { get; set; } = new();

        public string MinimapIconPath { get; set; }

        /// <summary>Является ли планета «столицей» своей расы (мир-родина). Ставится генератором
        /// или сценарием; используется в приветствиях планет (CurPlanetIsHomeworld).</summary>
        public bool IsHomeworld { get; set; }

        /// <summary>Является ли планета источником активного задания (квестовая).
        /// Ставится системой квестов; используется в приветствиях планет (QuestGiver).</summary>
        public bool IsQuestGiver { get; set; }

        [JsonIgnore] public StarData ParentStar { get; set; }

        private const float NearZero = 0.001f;
        public void PlanetNextDay()
        {
            PreviousAngle = CurrentAngle;
            CurrentAngle = AdvanceAngle(CurrentAngle, OrbitSpeed);

            foreach (var sat in Satellites)
            {
                sat.CurrentAngle = AdvanceAngle(sat.CurrentAngle, sat.OrbitSpeed);
                if (sat.DaySpeed > 0) sat.CurrentRotationAngle += 360f / sat.DaySpeed;
            }
        }

        public void InitRotation() { CurrentAngle = PreviousAngle = InitialAngle; }

        string IGalaxyEntity.Owner => Owner ?? GalaxyConstants.OWNER_NONE_KEY;
        string IGalaxyEntity.Race  => Race  ?? GalaxyConstants.RACE_NONE_KEY;

        private static float AdvanceAngle(float current, float speed)
        {
            if (Mathf.Abs(speed) < NearZero) return current;
            float next = current + 360f / speed;
            return Mathf.Repeat(next, 360f);
        }
    }

    public class SatelliteData : IGalaxyEntity
    {
        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string Size { get; set; } = "Mid";

        public float OrbitRadius { get; set; }
        public float OrbitSpeed { get; set; }
        public float CurrentAngle { get; set; }
        public float OrbitInclination { get; set; }

        public string Graphic { get; set; }
        public string Atmosphere { get; set; }

        public int DaySpeed { get; set; }
        public int CloudsSpeed { get; set; }
        public float CurrentRotationAngle { get; set; }

        public string Race { get; set; }
        public string Owner { get; set; }
        public Dictionary<string, string> CustomProperties { get; private set; } = new();

        string IGalaxyEntity.Owner => Owner ?? GalaxyConstants.OWNER_NONE_KEY;
        string IGalaxyEntity.Race  => Race  ?? GalaxyConstants.RACE_NONE_KEY;
    }

    public class RaceInfo
    {
        public string Name { get; private set; }
        public string Color { get; private set; }
        public string EmblemPath { get; private set; }

        public RaceInfo(string name, string color, string emblemPath)
        {
            Name = name;
            Color = color?.ToLowerInvariant() ?? GalaxyConstants.DEFAULT_COLOR;
            EmblemPath = emblemPath;
        }
    }

    #endregion
    public class ShipData : ILandingSite
    {
        // ILandingSite: ShipData хранит Name/Owner/Race как публичные поля (историческое решение);
        // явная реализация интерфейса просто читает их. Kind = Station|Carrier по IsStation.
        string ILandingSite.Uid => Uid;
        string ILandingSite.Name => Name;
        string ILandingSite.Owner => Owner;
        string ILandingSite.Race => Race;
        SettlementData ILandingSite.Settlement => Settlement;
        LandingSiteKind ILandingSite.Kind => IsStation ? LandingSiteKind.Station : LandingSiteKind.Carrier;
        // Единый геометрический контракт ILandingSite. Для кораблей центр = Position,
        // радиус = дистанция стыковки из ShipDockingService (0.75 * SpriteWorldSize).
        Vector2 ILandingSite.CenterPosition => Position;
        float ILandingSite.LandingRadius => SRG.Ships.Services.ShipDockingService.GetDockRadius(this);

        public string Uid { get; set; } = Guid.NewGuid().ToString();

        public string ShipTypeId;
        public string Name;
        /// <summary>Текущий путь к графике корабля в космосе. По умолчанию = резолвнутая графика корпуса
        /// (с учётом Owner/Race). Может быть подменена override'ом (CustomBodyGraphicPath) — например, для маскировки.
        /// Обновляется через RefreshSpritesheetPath() при смене корпуса или установке/снятии override.</summary>
        public string SpritesheetPath;
        /// <summary>Пользовательский override графики корабля. Если задан — имеет наивысший приоритет.
        /// Сбрасывается передачей null в SetCustomBodyGraphic.</summary>
        public string CustomBodyGraphicPath;
        public string MinimapIconPath;
        public Dictionary<string, ShipAnimationClip> Animations;
        public List<ShipThrusterData> Thrusters;
        public float SpriteWorldSize = 1.0f;

        // Максимальная угловая скорость разворота (°/ход). Задаёт радиус дуги в планировщике
        // траектории: больше значение — манёвреннее корабль. По умолчанию 180°/ход.
        public float TurnSpeedDeg = 180f;

        public string Owner;
        public string Race;
        public bool IsPlayer;

        /// <summary>true — этот «корабль» является станцией: неподвижен, графика не поворачивается
        /// (по флагу корпуса), несёт <see cref="Settlement"/> (магазины/правительство) и имеет
        /// оборонительный ИИ. См. план «Космические станции».</summary>
        public bool IsStation;
        /// <summary>Обитаемая инфраструктура станции (магазины/правительство/экономика). Общая сущность
        /// с планетами (<see cref="SettlementData"/>). null у обычных кораблей.</summary>
        public SettlementData Settlement { get; set; }
        /// <summary>Графика тела не поворачивается по курсу (свойство установленного корпуса,
        /// <see cref="ItemInstance.FixedRotation"/>). Обновляется в <see cref="RefreshSpritesheetPath"/>;
        /// сериализуется (как и <see cref="SpritesheetPath"/>), чтобы станции не крутились после загрузки.</summary>
        public bool SpriteFixedRotation;

        public Vector2 Position;
        [JsonIgnore] public Vector2 PreviousPosition;
        public Vector2 TargetPosition;

        [JsonIgnore] public Queue<Vector2> TargetQueue = new Queue<Vector2>();
        [JsonIgnore] public float CurrentHeading = float.NaN;
        [JsonIgnore] public List<Vector2> Waypoints = new List<Vector2>();
        [JsonIgnore] public int WaypointIndex = 0;
        [JsonIgnore] public List<Vector2> TrailPositions = new List<Vector2>();

        // Кеш слотов оружия — строится один раз, не на каждый сабтёрн
        [JsonIgnore] private List<string> _weaponSlotsCache;
        // Кеш скорости на текущий ход — вычисляется один раз в StarNextDay, не на каждый сабтёрн
        [JsonIgnore] public float _turnCachedSpeed = -1f;
        // Переиспользуемые объекты субтёрна и рендер-пути (не сериализуются)
        [JsonIgnore] public ShipSubTurnFrames SubTurnFrames = new ShipSubTurnFrames();
        [JsonIgnore] public List<Vector2> RenderPath = new List<Vector2>();
        public List<string> GetSortedWeaponSlots()
        {
            if (_weaponSlotsCache == null)
            {
                _weaponSlotsCache = new List<string>();
                foreach (var kv in Equipment.Slots)
                    if (kv.Value != null && kv.Key.StartsWith("Weapons_", StringComparison.Ordinal))
                        _weaponSlotsCache.Add(kv.Key);
                _weaponSlotsCache.Sort();
            }
            return _weaponSlotsCache;
        }
        public void InvalidateWeaponSlotsCache() => _weaponSlotsCache = null;

        /// <summary>
        /// «Замораживает» движение корабля: очищает очередь точек, путевые точки и сбрасывает
        /// цель в текущую позицию. Используется когда корабль захвачен (Pickup/Tow/Boarding),
        /// либо после посадки/гиперпрыжка, либо когда внешняя сила перехватила управление.
        /// Поведение идентично прежним inline-копиям в PickupSystem/TowSystem/BoardingSystem.
        /// </summary>
        public void FreezeRoute()
        {
            TargetQueue?.Clear();
            if (Waypoints != null) Waypoints.Clear();
            WaypointIndex = 0;
            TargetPosition = Position;
        }

        public string CurrentStarUid;
        public string PreviousStarUid;
        [JsonIgnore] public StarData CurrentStar;

        // Гиперпереход: UID целевой звезды (пусто = не прыгает). Сохраняется для NPC и игрока.
        public string HyperjumpTargetStarUid;
        // Текущая фаза прыжка (None пока не идёт).
        public HyperjumpPhase HyperjumpPhase = HyperjumpPhase.None;
        // Точка края исходной системы, куда корабль летит и где открывается портал.
        public Vector2 HyperjumpEdge;
        // Точка появления в новой системе (на её краю напротив исходной).
        public Vector2 HyperjumpArrivalEdge;
        // Направление, в котором смотрит корабль/портал на момент входа/выхода (радианы).
        // Во время Travel/HyperEnter = курс наружу источника (к HyperjumpEdge).
        // Во время HyperExit       = курс внутрь цели (от ArrivalEdge к центру системы цели).
        public float HyperjumpHeading;
        // Курс прибытия (рассчитывается в RequestJump, применяется в CompleteJump).
        // Хранится отдельно от HyperjumpHeading, чтобы пока активен исходный портал
        // мы не теряли его направление; нужен для портала на стороне цели на ходе HyperEnter.
        public float HyperjumpArrivalHeading;
        // UID звезды, откуда прилетели (для расчёта точки прибытия — в направлении этой звезды от центра новой).
        public string HyperjumpFromStarUid;
        // UID червоточины, через которую идёт прыжок (пусто = обычный гиперпрыжок). Если задан:
        //   • не тратим топливо и не проверяем JumpRange (червоточина — «бесплатный» прыжок);
        //   • HyperjumpEdge = позиция червоточины в системе-источнике;
        //   • HyperjumpArrivalEdge = случайная точка в системе-цели (а не край).
        //   • source-портал НЕ рисуется (см. HyperjumpPortalManager) — червоточина сама является визуалом.
        // Сбрасывается в конце HyperExit вместе с прочими hyperjump-полями.
        public string HyperjumpViaWormholeUid;
        /// <summary>Оставшееся число ходов в фазе HyperArrive (визуально — «болтание в гипертоннеле»).
        /// Инициализируется в <see cref="Ships.Movement.HyperjumpController.CalcHyperArriveTurns"/> при
        /// переходе HyperEnter → HyperArrive. Пока &gt; 1 — FinalizeTurn декрементирует и держит корабль
        /// в HyperArrive. При достижении 1 — стандартный переход в HyperExit. Сериализуется в сейв,
        /// чтобы прыжок продолжался после сохранения. Значение 0 (по умолчанию) означает «моментальный
        /// проход» (обратная совместимость со старыми сейвами).</summary>
        public int HyperArriveTurnsLeft;
        public float CrimeRating { get; set; } = 0f;

        // Метрики рейтингов кораблей (см. ShipRatingService). Инкрементятся из ShipDeathBus и
        // ActionGoodsTrader. Трекаются для ВСЕХ кораблей независимо от класса — какие корабли
        // в каком рейтинге участвуют, решает конфиг GalaxyConfig.Ratings.
        public int KillsDominator { get; set; }
        public int KillsPirate { get; set; }
        public int KillsTransport { get; set; }
        public int KillsRanger { get; set; }
        /// <summary>Совокупный профит от торговых операций (закупка→продажа). Учитывается в
        /// <see cref="ActionGoodsTrader"/> при завершении легов «купить-продать».</summary>
        public int TradeProfit { get; set; }

        // Место спавна: UID планеты, станции или корабля, от которого был создан этот корабль
        public string SpawnedFromUid { get; set; }

        // Домашняя планета — обычно совпадает с SpawnedFromUid, используется для «солидарности»:
        // воины с одной HomePlanetUid считаются «земляками» и реагируют на взятку/угрозу друг другу.
        public string HomePlanetUid { get; set; }
        /// <summary>Звезда родной планеты (для тег-подстановок в диалогах).</summary>
        public string HomeStarUid { get; set; }

        /// <summary>UID планеты последнего визита (где корабль был приземлён в прошлый раз).
        /// Заполняется автоматически при взлёте через <see cref="MoveToLanded"/>/<see cref="MoveToLaunched"/>.</summary>
        public string LastPlanetUid { get; set; }
        /// <summary>UID звезды последнего визита (см. <see cref="LastPlanetUid"/>).</summary>
        public string LastStarUid { get; set; }
        /// <summary>UID следующей запланированной к посещению планеты (для тег-подстановок в диалогах).
        /// Заполняется AI-действиями (<c>ActionGoodsTrader</c>, патрули, лайнеры).</summary>
        public string NextPlanetUid { get; set; }
        /// <summary>UID звезды следующей запланированной к посещению планеты.</summary>
        public string NextStarUid { get; set; }

        // Последний агрессор (UID корабля), нанёсший урон этому кораблю.
        // Устанавливается в WeaponSystem.ProcessShot/ApplyMissileImpact. Очищается по таймауту в NpcBrain.
        [JsonIgnore] public string LastAttackerUid { get; set; }
        [JsonIgnore] public int LastAttackerTurn { get; set; } = -1;
        /// <summary>Ход последнего SOS-вызова этого корабля (ActionCallForHelp).
        /// Не сериализуется — кулдаун переживать save/load не обязан.</summary>
        [JsonIgnore] public int LastHelpCallTurn { get; set; } = -1;

        /// <summary>Ход, на котором игрок заступился за этот корабль (ProtectService.TryProtect).
        /// Пока значение не «протухло» (см. ProtectService.RewardMemoryTurns) — при следующем диалоге
        /// жертва даёт награду (деньги/груз/благодарность). -1 = награду не должен.</summary>
        public int RescuedByPlayerTurn { get; set; } = -1;

        // ── Партнёрство ─────────────────────────────────────────────────────────
        /// <summary>UID лидера, которому этот корабль подчинён по контракту (null = свободен).
        /// Устанавливается только через PartnerService.SetPartner для поддержания синхронизации.</summary>
        public string PartnerLeaderUid { get; set; }
        /// <summary>Список UID подчинённых партнёров этого корабля. Восстанавливается при загрузке
        /// одним проходом по галактике из PartnerLeaderUid (JsonIgnore).</summary>
        [JsonIgnore] public List<string> PartnerFollowerUids { get; set; } = new();
        /// <summary>Номер хода, на котором истекает контракт. -1 = бессрочно.</summary>
        public int PartnerContractEndTurn { get; set; } = -1;
        /// <summary>Номер хода, на котором заключён контракт (для аналитики/подарков).</summary>
        public int PartnerHiredOnTurn { get; set; } = -1;
        /// <summary>Явный приказ от лидера, замещающий дефолтное поведение ActionPartnerAttend.
        /// Сбрасывается в None когда приказ выполнен либо контракт разорван.</summary>
        public PartnerOrderKind PartnerOrder { get; set; } = PartnerOrderKind.None;
        /// <summary>UID цели приказа (корабль для Attack, планета для LandOn, звезда для FlyToStar).</summary>
        public string PartnerOrderTargetUid { get; set; }

        // ── Дрон ────────────────────────────────────────────────────────────────
        /// <summary>Дрон ожидает стыковки: при контакте с хозяином следующая проверка
        /// в ActionPartnerAttend упакует его обратно в предмет через DroneService.Pack.</summary>
        public bool DroneReturnPending { get; set; }
        /// <summary>ItemId предмета-обёртки, из которого дрон был развёрнут. Нужен для
        /// восстановления той же шаблонной оболочки при упаковке. Пусто = generic.</summary>
        public string PackedDroneItemId { get; set; }

        // Посадка: Uid планеты-цели во время подлёта; после посадки переносится в LandedPlanetUid
        public string LandingPlanetUid { get; set; }
        public string LandedPlanetUid  { get; set; }

        /// <summary>Uid корабля-носителя, к которому этот корабль пристыкован (ShipTypeConfig.CanBeLandedOn,
        /// у линкоров). Пристыкованный корабль скрыт, следует за носителем (перенос позиции —
        /// в StarSimulator) и не участвует в бою. null = не пристыкован. См. <see cref="ShipDockingService"/>.</summary>
        public string LandedOnShipUid { get; set; }

        /// <summary>Uid корабля-носителя, к которому этот корабль сейчас подлетает для стыковки
        /// (аналог <see cref="LandingPlanetUid"/> для планетной посадки). После касания dock-радиуса
        /// в конце хода фаза становится Fading, играется fade-out и в OnTurnComplete значение
        /// переносится в <see cref="LandedOnShipUid"/>. Сохраняется вместе с LandingPlanetUid —
        /// иначе после загрузки при активном follow-режиме LandOnShip авто-стыковка теряет цель.</summary>
        public string LandingCarrierUid { get; set; }

        /// <summary>Фаза двухфазной посадки. Транзитное состояние, не сохраняется.
        /// Сбрасывается в None при загрузке: на следующий ход PrepareForTurn пересчитает её.</summary>
        [JsonIgnore] public LandingPhase LandingPhase { get; set; } = LandingPhase.None;

        /// <summary>Запоминает текущую планету посадки как «последнюю посещённую» и обнуляет
        /// <see cref="LandedPlanetUid"/>. Вызывается в момент взлёта (Player/NPC) для поддержки
        /// тег-подстановок <c>{last_planet}</c>/<c>{last_star}</c> в диалогах.</summary>
        public void RecordLastVisitAndLaunch()
        {
            if (!string.IsNullOrEmpty(LandedPlanetUid))
            {
                LastPlanetUid = LandedPlanetUid;
                LastStarUid   = CurrentStarUid;
            }
            LandedPlanetUid = null;
        }

        // ── Состояние торговца (ActionGoodsTrader) ────────────────────────────────
        /// <summary>Что везёт сейчас (null = пустой трюм).</summary>
        public string TraderCargoGoodId { get; set; }
        /// <summary>Сколько единиц груза в трюме.</summary>
        public int TraderCargoWeight { get; set; }
        /// <summary>Цена закупки за единицу — для P/L диагностики.</summary>
        public int TraderBuyPrice { get; set; }
        /// <summary>Планета-цель текущего рейса (купить→продать), или дом для возврата.</summary>
        public string TraderTargetPlanetUid { get; set; }
        /// <summary>Звезда-цель для гиперпрыжка к TraderTargetPlanetUid.</summary>
        public string TraderTargetStarUid { get; set; }
        /// <summary>Сколько ходов «думать» прежде чем пытаться искать рейс снова (после неудачи).</summary>
        public int TraderIdleTurnsLeft { get; set; }

        [JsonIgnore] public ShipPersonality Personality { get; set; }
        [JsonIgnore] public NpcBrain Brain { get; set; }

        public int Money { get; set; }

        /// <summary>Нод-счёт корабля. Используется <see cref="SRG.Equipment.ImprovementService"/> при
        /// улучшении оборудования с флагом <see cref="ItemInstance.RequiresNodesToImprove"/>: сначала
        /// списываем физические стеки Node из трюма, если не хватает — дораскладываем с нод-счёта.
        /// Механика пополнения счёта появится позже (диздок docs/modules/equipment_improvement.md).</summary>
        public int NodeAccount { get; set; }

        // ── HP / прочность корпуса ─────────────────────────────────────────────────
        // Единый источник истины: <c>ItemInstance.Durability/MaxDurability</c> установленного
        // корпуса (слот <see cref="SlotKeys.Hull"/>). Свойства ниже — обёртки для сохранения
        // старого API (141 use-site в проекте): чтение проксирует hull.Durability, запись
        // клампит и пишет туда же. Для вырожденных кораблей без корпуса (fallback-спавн)
        // работают приватные поля <c>_currentHullFallback</c>/<c>_maxHullFallback</c>,
        // сериализуемые под старыми именами «CurrentHull»/«MaxHull» — старые сейвы читаются.
        [Newtonsoft.Json.JsonProperty("CurrentHull")] private int _currentHullFallback;
        [Newtonsoft.Json.JsonProperty("MaxHull")]     private int _maxHullFallback;

        [Newtonsoft.Json.JsonIgnore]
        public int CurrentHull
        {
            get
            {
                var hull = GetHullItem();
                return hull != null ? hull.Durability : _currentHullFallback;
            }
            set
            {
                var hull = GetHullItem();
                if (hull != null)
                {
                    int max = hull.MaxDurability > 0 ? hull.MaxDurability : value;
                    hull.Durability = UnityEngine.Mathf.Clamp(value, 0, max);
                }
                else _currentHullFallback = value;
            }
        }

        [Newtonsoft.Json.JsonIgnore]
        public int MaxHull
        {
            get
            {
                var hull = GetHullItem();
                return hull != null ? hull.MaxDurability : _maxHullFallback;
            }
            set
            {
                var hull = GetHullItem();
                if (hull != null)
                {
                    hull.MaxDurability = UnityEngine.Mathf.Max(1, value);
                    if (hull.Durability > hull.MaxDurability) hull.Durability = hull.MaxDurability;
                }
                else _maxHullFallback = value;
            }
        }

        public ItemInstance GetHullItem()
        {
            if (Equipment == null || AllItems == null) return null;
            if (!Equipment.Slots.TryGetValue(SlotKeys.Hull, out var uid) || uid == null) return null;
            AllItems.TryGetValue(uid, out var hull);
            return hull;
        }

        public Dictionary<string, ItemInstance> AllItems { get; set; } = new();
        public EquipmentSlots Equipment { get; set; } = new();
        public ShipInventory Inventory { get; set; } = new();

        /// <summary>Шесть пилот-скилов корабля, прогрессия и разбивка опыта.
        /// Инициализируется в ShipFactory из BaseSkills конфига типа корабля.</summary>
        public ShipSkills Skills { get; set; } = new();

        // ── Абордаж / буксир ──────────────────────────────────────────────────────
        /// <summary>true — этот «корабль» представляет предмет в космосе (контейнер/обломок).
        /// Может быть взят только на буксир, не на абордаж; своей анимации не имеет, замораживается на FreezeFrame.</summary>
        public bool IsItem { get; set; }
        /// <summary>Номер кадра анимации, на котором останавливается визуал предмета при буксировке. Используется только при IsItem=true.</summary>
        public int ItemFreezeFrame { get; set; }
        /// <summary>Uid атакующего, абордирующего этот корабль. Цель не может двигаться сама и всегда сопровождает атакующего.</summary>
        public string BoardedByUid { get; set; }
        /// <summary>Uid цели, которую этот корабль абордирует. Один абордаж за раз.</summary>
        public string BoardingTargetUid { get; set; }
        /// <summary>Uid буксирующего объекта (корабля или другого буксируемого, формируя цепочку «поезда»).</summary>
        public string TowedByUid { get; set; }
        /// <summary>Uid буксирующего/подбирающего, который сейчас тянет эту цель. Промежуточное состояние:
        /// цель уже не движется по своему маршруту, но и не прицеплена — каждый субход смещается к источнику.
        /// Конечное действие определяется полем <see cref="PullMode"/>: подбор (Pickup) → разгрузка в трюм,
        /// буксир (Tow) → крепление на якорь.</summary>
        public string PulledByUid { get; set; }
        /// <summary>Режим текущего притягивания (см. <see cref="PullKind"/>). Указывает, какое именно
        /// оборудование инициировало Pull (CargoGrabber → Pickup, TowingRig → Tow) и что произойдёт по
        /// достижении источника. Сбрасывается в <c>None</c> при EndPull/EndRelease.</summary>
        public PullKind PullMode { get; set; } = PullKind.None;
        /// <summary>Последняя зафиксированная дистанция до буксирующего во время Pull. -1 = не инициализирована.</summary>
        public float LastPullDistance { get; set; } = -1f;
        /// <summary>Сколько ходов подряд дистанция до буксирующего растёт. Если ≥ 2 — буксир рвётся.</summary>
        public int PullDistanceIncreaseTurns { get; set; }
        /// <summary>false — буксир ещё «садится» в позицию (плавный подход с cap target.Speed/2); true — жёсткое крепление.</summary>
        public bool IsTowSettled { get; set; }
        /// <summary>Скорость буксируемого относительно мира для физики «груз на привязи в невесомости» (PD-демпфер).</summary>
        [JsonIgnore] public Vector2 TowVelocity { get; set; }

        // ── Очередь захвата (CargoGrabber) ─────────────────────────────────────────
        /// <summary>FIFO-очередь UID'ов целей (обычно IsItem-контейнеры), которые корабль будет
        /// притягивать по одной начиная с ближайшей. Заполняется через UI/AI в planning-фазе.
        /// Притягивание текущей цели — поле PulledByUid у target. После прикрепления цель уходит
        /// из очереди автоматически. См. PickupSystem.ProcessPullQueueSubturn.</summary>
        public List<string> PulledQueue { get; set; } = new();
        /// <summary>Когда true: если хоть одна цель из PulledQueue попадает в радиус захвата
        /// во время движения корабля — корабль останавливается, подбирает цели по очереди,
        /// затем продолжает путь по сохранённому курсу.</summary>
        public bool AutoPullActive { get; set; }

        // ── Разрешения партнёра/дрона (подменю «Настроить поведение», пулы Tranclucator.Options.*)
        // Лидер ограничивает самостоятельность подчинённого. Значение по умолчанию — «разрешено»:
        // так старые сейвы, где этих полей нет, ведут себя как раньше.
        /// <summary>Можно ли самостоятельно собирать вещи/артефакты (гейт ActionScavenge и
        /// автоподбора). Запрет не мешает прямому приказу «собирай вещи».</summary>
        public bool AllowCollect { get; set; } = true;
        /// <summary>Можно ли садиться на планеты/станции (гейт приказа LandOn и ActionLandResupply).</summary>
        public bool AllowLand { get; set; } = true;
        /// <summary>Можно ли самому менять оборудование на подобранное лучшее
        /// (гейт ShipLoadoutService.TickTurn).</summary>
        public bool AllowArrange { get; set; } = true;

        /// <summary>Сохранённый «отложенный» маршрут на время автоостановки для захвата. Когда очередь
        /// в радиусе опустеет, эти waypoints переустанавливаются как новая цель движения.</summary>
        public List<Vector2> AutoPullSuspendedPath { get; set; } = new();
        /// <summary>Финальная TargetPosition сохранённого маршрута (если он был линейный без waypoints).</summary>
        public Vector2? AutoPullSuspendedTarget { get; set; }
        /// <summary>Ход, на котором корабль подобрал груз (PickupSystem). Начиная со СЛЕДУЮЩЕГО хода
        /// ShipLoadoutService попробует надеть подобранное оборудование, если оно лучше текущего.
        /// -1 = отложенной проверки нет.</summary>
        public int PendingAutoEquipTurn { get; set; } = -1;
        /// <summary>
        /// Uid'ы объектов, прикреплённых К ЭТОМУ кораблю напрямую (на его якорях TowAnchors).
        /// Дерево буксира: tug -> children -> grandchildren (Back-цепочка). Каждый ребёнок занимает ОДИН якорь.
        /// </summary>
        public List<string> TowedObjectUids { get; set; } = new();
        /// <summary>Прямой родитель в дереве буксира (буксирующий tug или другой буксируемый, если идёт «поезд»).</summary>
        public string TowParentUid { get; set; }
        /// <summary>Ключ якоря у прямого родителя ("Back" / "Right" / "Left" / "Front").</summary>
        public string TowAnchorKey { get; set; }
        /// <summary>УСТАРЕВШЕЕ. Блокировка пушек буксируемого теперь автоматически по враждебности к буксиру
        /// (см. CombatSubTurn). Поле сохранено для совместимости со старыми сейвами и больше не читается.</summary>
        public bool WeaponsBlockedByTow { get; set; } = true;

        /// <summary>Текущий путь к графике корпуса корабля в космосе. Возвращает значение SpritesheetPath
        /// (которое держится в актуальном состоянии через RefreshSpritesheetPath при смене корпуса/маскировки).</summary>
        public string GetBodyGraphicPath() => SpritesheetPath;

        /// <summary>Пересчитать SpritesheetPath: приоритет CustomBodyGraphicPath → резолвнутый BodyGraphicPath
        /// установленного корпуса → null. Вызывается ShipFactory.RecalculateSpriteWorldSize.</summary>
        public void RefreshSpritesheetPath()
        {
            // Флаг «не поворачивать» — свойство установленного корпуса (обновляем всегда, до early-return).
            var hullItem = GetHullItem();
            SpriteFixedRotation = hullItem?.FixedRotation ?? false;

            if (!string.IsNullOrEmpty(CustomBodyGraphicPath))
            {
                SpritesheetPath = CustomBodyGraphicPath;
                return;
            }

            if (hullItem != null && !string.IsNullOrEmpty(hullItem.BodyGraphicPath))
            {
                // GraphicByManufacturer=true: корпус сохраняет «фирменный» вид расы-производителя
                // вне зависимости от того, кто сейчас на нём летает.
                string ownerForGfx = hullItem.GraphicByManufacturer ? hullItem.ManufacturerSide : Owner;
                string raceForGfx  = hullItem.GraphicByManufacturer ? hullItem.ManufacturerRace : Race;
                SpritesheetPath = ShipGraphicsResolver.ResolveFromBase(hullItem.BodyGraphicPath, ownerForGfx, raceForGfx);
                return;
            }

            SpritesheetPath = null;
        }

        /// <summary>Перекрыть графику корабля пользовательским спрайтом (например, маскировка). Передайте null
        /// чтобы вернуть привязку к корпусу. После вызова — обновить визуал через ShipFactory.RecalculateSpriteWorldSize.</summary>
        public void SetCustomBodyGraphic(string path)
        {
            CustomBodyGraphicPath = path;
            ShipFactory.RecalculateSpriteWorldSize(this);
        }

        /// <summary>Активная маскировка. null = маски нет. Сохраняется в сейв, чтобы состояние маски
        /// (в т.ч. DetectedByRaces) переживало загрузку. См. <see cref="SRG.Ships.Disguise.DisguiseState"/>.</summary>
        public SRG.Ships.Disguise.DisguiseState Disguise { get; set; }

        [JsonIgnore] public bool ForsageActive { get; set; } = false;
        /// <summary>UID цели для выстрела игрока в следующем ходу. Сбрасывается после выстрела.</summary>
        [JsonIgnore] public string ManualShootTargetUid { get; set; }
        /// <summary>true — цель является астероидом, false — кораблём.</summary>
        [JsonIgnore] public bool ManualShootTargetIsAsteroid { get; set; }
        /// <summary>true — стрелять только из самой дальнобойной пушки (режим follow-attack «дальняя»).</summary>
        [JsonIgnore] public bool ManualShootOnlyLongestRange { get; set; }
        /// <summary>true — стрельба назначена авто-режимом следования, не ручным кликом;
        /// подавляет сообщение «нет оружия в радиусе» при многократных тиках.</summary>
        [JsonIgnore] public bool ManualShootIsAutoFollow { get; set; }
        [JsonIgnore] public List<ActiveCombatEffect> ActiveEffects { get; set; } = new List<ActiveCombatEffect>();
        /// <summary>Скорость корабля (формула от скорости двигателя с учётом forsage, перегруза, буксира).</summary>
        [JsonIgnore] public float ActualSpeed => EquipmentSystem.CalculateSpeed(this);
        /// <summary>Алиас на ActualSpeed: скорость корабля = f(скорость двигателя). Двигатель — оборудование в SlotKeys.Engine.</summary>
        [JsonIgnore] public float EngineSpeed => ActualSpeed;
        /// <summary>Полный вес: сумма весов установленного оборудования и инвентаря.</summary>
        [JsonIgnore] public int TotalWeight => EquipmentSystem.GetEquippedWeight(this) + Inventory.TotalWeight();
        /// <summary>Алиас на TotalWeight: вес корабля = сумма весов оборудования (+ инвентарь).</summary>
        [JsonIgnore] public int Weight => TotalWeight;

        public void ShipMoveStep(int stepDivisor, List<Vector2> trail = null)
        {
            if (CurrentHull <= 0) return;
            float fullStep = SRG.Utils.SRUnits.ToWorld(_turnCachedSpeed >= 0f ? _turnCachedSpeed : ActualSpeed);
            float remaining = fullStep / stepDivisor;

            int safetyLimit = stepDivisor * 10;
            int iterations = 0;

            while (remaining > 0.0001f)
            {
                if (++iterations > safetyLimit)
                {
                    Debug.LogWarning($"[ShipData] ShipMoveStep: safety limit for {Uid}. Breaking.");
                    break;
                }
                if (Waypoints != null && WaypointIndex < Waypoints.Count)
                {
                    Vector2 waypoint = Waypoints[WaypointIndex];
                    Vector2 dir = waypoint - Position;
                    float dist = dir.magnitude;

                    if (dist < 0.001f)
                    {
                        Position = waypoint;
                        WaypointIndex++;
                        continue;
                    }

                    CurrentHeading = Mathf.Atan2(dir.y, dir.x);
                    if (dist <= remaining)
                    {
                        remaining -= dist;
                        Position = waypoint;
                        trail?.Add(Position);
                        WaypointIndex++;
                    }
                    else
                    {
                        Position += (dir / dist) * remaining;
                        trail?.Add(Position);
                        remaining = 0f;
                    }
                }
                else
                {
                    if (TargetQueue.Count > 0)
                    {
                        Vector2 nextTarget = TargetQueue.Dequeue();
                        TargetPosition = nextTarget;
                        var star = FindCurrentStar();
                        ShipTrajectory.BuildPath(this, nextTarget, star, Waypoints);
                        WaypointIndex = 0;
                        continue;
                    }

                    Vector2 toTarget = TargetPosition - Position;
                    float toDist = toTarget.magnitude;
                    if (toDist < 0.001f) break;

                    CurrentHeading = Mathf.Atan2(toTarget.y, toTarget.x);

                    if (toDist <= remaining)
                    {
                        remaining -= toDist;
                        Position = TargetPosition;
                        trail?.Add(Position);
                    }
                    else
                    {
                        Position += toTarget.normalized * remaining;
                        trail?.Add(Position);
                        remaining = 0f;
                    }
                    break;
                }
            }
        }

        private StarData FindCurrentStar()
        {
            if (CurrentStar != null) return CurrentStar;
            if (string.IsNullOrEmpty(CurrentStarUid)) return null;
            var map = GalaxyManager.Instance?.GeneratedGalaxy?.StarsMap;
            if (map == null) return null;
            map.TryGetValue(CurrentStarUid, out var star);
            return star;
        }
        public void ShipNextDay(int subTurn, TurnAnimationData anim, bool buildRenderPath = false)
        {
            if (CurrentHull <= 0)
            {
                if (anim.ShipFrames.TryGetValue(Uid, out var deadFrames))
                    deadFrames.SubTurns[subTurn] = Position;
                return;
            }

            // Авто-отключение форсажа на 6-м субходе последнего хода перед посадкой:
            // чтобы корабль не проскочил планету, оставшиеся 5 субходов идёт штатной скоростью.
            if (subTurn == 6 && ForsageActive && EquipmentSystem.IsLandingThisTurn(this))
            {
                ForsageActive = false;
                _turnCachedSpeed = EquipmentSystem.CalculateSpeed(this);
            }

            if (subTurn == 1)
            {
                PreviousPosition = Position;
                if (buildRenderPath && anim.ShipFrames.ContainsKey(Uid))
                {
                    RenderPath.Clear();
                    RenderPath.Add(Position);
                    anim.ShipRenderPaths[Uid] = RenderPath;
                }
            }

            // Абордируемые, буксируемые и притягиваемые объекты не двигаются сами:
            // - BoardedByUid: позицию выставит SnapBoardedTarget.
            // - TowedByUid: позицию выставит UpdateTowChain.
            // - PulledByUid: позицию выставит UpdatePullStep (после движения буксирующего).
            if (!string.IsNullOrEmpty(BoardedByUid) || !string.IsNullOrEmpty(TowedByUid) || !string.IsNullOrEmpty(PulledByUid))
            {
                if (anim.ShipFrames.TryGetValue(Uid, out var f))
                    f.SubTurns[subTurn] = Position;
                return;
            }

            anim.ShipRenderPaths.TryGetValue(Uid, out var trail);
            ShipMoveStep(GalaxyData.SubTurnsPerTurn, trail);

            if (subTurn == GalaxyData.SubTurnsPerTurn)
            {
                bool movedThisTurn = (Position - PreviousPosition).sqrMagnitude > 0.0001f;
                var equipConfig = GalaxyManager.Instance?.Context?.ItemsConfig;
                EquipmentSystem.ApplyTurnEffects(this, equipConfig, movedThisTurn);
            }

            if (anim.ShipFrames.TryGetValue(Uid, out var frames))
            {
                frames.SubTurns[subTurn] = Position;
                // Гарантируем финальную позицию субхода в render-пути (если ShipMoveStep не двигался)
                if (trail != null && (trail.Count == 0 || (trail[trail.Count - 1] - Position).sqrMagnitude > 0.0001f))
                    trail.Add(Position);
            }
        }

        public void ApplyDamage(
            int rawDamage,
            string damageType,
            int armorPierce = 0,
            float equipHitChance = 0f,
            int equipDamage = 0,
            ItemsConfig equipConfig = null)
        {
            if (equipConfig != null)
                EquipmentSystem.ApplyDamage(this, rawDamage, damageType, armorPierce,
                                            equipHitChance, equipDamage, equipConfig);
            else
                CurrentHull = Mathf.Max(0, CurrentHull - rawDamage);
        }

        public void InitOnSpawn(StarData star, Vector2 spawnPos)
        {
            Position = PreviousPosition = TargetPosition = spawnPos;
            CurrentStarUid = star?.Uid;
            PreviousStarUid = star?.Uid;

            var hull = EquipmentSystem.GetEquipped(this, SlotKeys.Hull);
            if (hull != null)
                MaxHull = Mathf.RoundToInt(hull.GetParam("HP", hull.MaxDurability > 0 ? hull.MaxDurability : 100f));
            else
            {
                Debug.LogWarning($"[ShipData] InitOnSpawn: ship '{Name}' has no hull — using MaxHull=100.");
                MaxHull = 100;
            }

            CurrentHull = MaxHull;
            Inventory.RebuildIndex();
        }
    }

    public class ShipAnimationClip
    {
        [Newtonsoft.Json.JsonProperty("StartFrame")] public int StartFrame;
        [Newtonsoft.Json.JsonProperty("FrameCount")] public int FrameCount;
        [Newtonsoft.Json.JsonProperty("FPS")] public float FPS;
        [Newtonsoft.Json.JsonProperty("Loop")] public bool Loop = true;
    }

    public class ShipThrusterData
    {
        public Vector2 LocalOffset;
        public string SpritesheetPath;
        public float FPS = 12f;
    }

    [Serializable]
    public class ItemInstance
    {
        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string Category { get; set; }
        public string ItemId { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }
        public int Weight { get; set; }
        public int Price { get; set; }
        public int TechLevel { get; set; }
        public bool NoWear { get; set; }
        /// <summary>Предмет-«носитель» разрешает встраивать в себя микромодули/встраиваемые артефакты.
        /// Раньше называлось AllowModules — сохранена совместимость через <see cref="AllowModules"/>.</summary>
        public bool AllowEmbeds { get; set; }
        /// <summary>Устаревшее имя <see cref="AllowEmbeds"/>. Сеттер обновляет новое поле, чтобы старые
        /// сейвы (до июля 2026) продолжали читаться. Не сериализуется.</summary>
        [JsonProperty("AllowModules")]
        public bool AllowModules { get => AllowEmbeds; set => AllowEmbeds = value; }
        public bool ShouldSerializeAllowModules() => false;
        /// <summary>Предмет можно «активировать» (drop в слот активации справа от радара).
        /// Сейчас активация форсажа включает ship.ForsageActive; для остальных типов
        /// предусмотрен расширяемый хук (см. EquipmentSystem.ActivateItem).</summary>
        public bool Activatable { get; set; }
        /// <summary>Классификация: экземпляр — оборудование (можно установить в слот, ремонтировать,
        /// апгрейдить). Задаётся из <see cref="ItemConfig.IsEquipment"/>, унаследованного
        /// от <c>EquipmentTemplate.Defaults["IsEquipment"]=true</c>.</summary>
        public bool IsEquipment { get; set; }
        /// <summary>Классификация: предмет является встраиваемым (микромодуль или встраиваемый артефакт).
        /// При установке в предмет-носителя (<see cref="AllowEmbeds"/>=true) применяет
        /// <see cref="Embed"/>.CarrierMods/Bonuses. См. docs/modules/micromodules_design.md.</summary>
        public bool IsEmbeddable { get; set; }
        /// <summary>Блок встраиваемости. null у обычных предметов.</summary>
        public EmbedConfig Embed { get; set; }
        /// <summary>Максимальное число встроенных предметов у носителя. У встраиваемых обычно 0.</summary>
        public int MaxEmbeds { get; set; } = 0;
        /// <summary>Классификация: экземпляр не является ни оборудованием, ни товаром — «useless»
        /// (находки, статуэтки, квесты). Определяется через отсутствие флага <see cref="IsEquipment"/>.
        /// Товары живут отдельно как <see cref="ItemStack"/>, поэтому в контексте <c>ItemInstance</c>
        /// «не оборудование» ≡ «useless».</summary>
        [JsonIgnore] public bool IsUseless => !IsEquipment;
        /// <summary>Установлен ли предмет сейчас в слот корабля. Не сериализуется — пересчитывается
        /// на каждом сабтёрне в <see cref="SRG.Equipment.ArtefactTurnRegistry.RunAll"/>
        /// перед вызовом скриптов.
        /// Доступно из Lua как <c>item.IsEquipped</c> — скрипт сам решает, тикать ли и как.</summary>
        [JsonIgnore] public bool IsEquipped { get; set; }
        public string ManufacturerRace { get; set; }
        /// <summary>Сторона-производитель (Coalition/Dominators/Pirates/...). Раньше называлось Owner.</summary>
        public string ManufacturerSide { get; set; }
        /// <summary>Путь к иконке предмета (в инвентаре/магазине).</summary>
        public string GraphicPath { get; set; }
        /// <summary>Базовый путь к графике корабля в космосе (для корпусов).</summary>
        public string BodyGraphicPath { get; set; }
        /// <summary>Только для корпусов. true → SpritesheetPath резолвится по
        /// <see cref="ManufacturerSide"/>/<see cref="ManufacturerRace"/>, а не по Owner/Race корабля.</summary>
        public bool GraphicByManufacturer { get; set; }
        /// <summary>Только для корпусов станций. true → графика тела не поворачивается по курсу
        /// (переносится в <see cref="ShipData.SpriteFixedRotation"/> при RefreshSpritesheetPath).</summary>
        public bool FixedRotation { get; set; }

        public int Durability { get; set; }
        public int MaxDurability { get; set; }
        public int CurrentFuel { get; set; }
        public Dictionary<string, float> Params { get; set; } = new();
        public Dictionary<string, string> ParamStrings { get; set; } = new();
        public float HealAccumulator { get; set; } = 0f;
        public float CargoAccumulator { get; set; } = 0f;
        public string Modification { get; set; }
        /// <summary>Uid встроенных предметов (микромодули + встраиваемые артефакты). Порядок
        /// сохраняется — от него зависит порядок применения бонусов. Раньше называлось Modules.</summary>
        public List<string> Embeds { get; set; } = new();
        /// <summary>Устаревшее имя <see cref="Embeds"/>. Сеттер переносит данные из старых сейвов
        /// в новое поле. Не сериализуется.</summary>
        [JsonProperty("Modules")]
        public List<string> Modules
        {
            get => null;
            set { if (value != null && value.Count > 0) Embeds = value; }
        }
        public bool ShouldSerializeModules() => false;
        /// <summary>Контейнер экземпляров встроенных предметов (Uid → инстанс). Живут внутри носителя,
        /// в общий инвентарь/торговлю не попадают.</summary>
        public Dictionary<string, ItemInstance> EmbedItems { get; set; } = new();
        /// <summary>Оригинальные значения <see cref="Params"/>, снятые перед первой установкой
        /// встроенного предмета. При извлечении бонусы откатываются относительно этой базы.</summary>
        public Dictionary<string, float> BaseParams { get; set; } = new();

        /// <summary>Скрытые/рантайм-бонусы, наложенные через <see cref="SRG.Equipment.BonusService"/>.
        /// Ключ — стабильный id (например "quest_boss_buff"). Действуют, но скрытые не отображаются в UI.
        /// </summary>
        public Dictionary<string, RuntimeBonus> RuntimeBonuses { get; set; } = new();

        /// <summary>Счётчики срабатываний триггеров (<see cref="TriggerSpec.EveryN"/>). Ключ —
        /// стабильный id триггера (см. TriggerSpec.Id или sourceUid+On), значение — сколько
        /// событий этого типа уже произошло. Инкрементируется TriggerBus. Не читается напрямую.</summary>
        public Dictionary<string, int> TriggerCounters { get; set; } = new();

        /// <summary>Отдельная копия <see cref="Params"/> без учёта скрытых рантайм-бонусов.
        /// UI (popup/tooltip) читает отсюда, чтобы не проговариваться о скрытых модификаторах.
        /// Если пусто — фолбэк на <see cref="Params"/>.</summary>
        public Dictionary<string, float> DisplayParams { get; set; } = new();

        public Dictionary<string, float> WeaponVulnerability { get; set; }
        /// <summary>
        /// Категории слотов, в которых предмет может работать. Если пуст — работает
        /// только в слоте своей категории. Например, у абордажного крюка может быть
        /// ["BoardingHook", "CargoGrabber"] — крюк ставится и в "родной" слот, и в
        /// слот грузового захвата (но захват в слот крюка не подойдёт).
        /// </summary>
        public List<string> CompatibleSlots { get; set; } = new();

        // ── Улучшение оборудования (научная база SB, диздок docs/modules/equipment_improvement.md) ──
        /// <summary>Разрешено ли улучшение этого предмета. Ставится в true при генерации
        /// eligible-предметов (см. <see cref="EquipmentTemplate"/>.Defaults["IsImprovable"]).
        /// Сбрасывается в false навсегда, как только предмет был улучшен ЛИБО в него встроили
        /// микромодуль (см. <see cref="SRG.Equipment.EmbedService"/>). Один предмет — один апгрейд.</summary>
        public bool IsImprovable { get; set; }
        /// <summary>Требует расходования Нод при улучшении. Флаг из шаблона (обычно ставится
        /// доминаторскому/трофейному оборудованию). Формула нод — см. <see cref="SRG.Equipment.ImprovementService"/>.</summary>
        public bool RequiresNodesToImprove { get; set; }
        /// <summary>Список ключей <see cref="Params"/>, значения которых были подняты апгрейдом.
        /// Используется UI: значения окрашиваются в зелёный (см. <see cref="SRG.UI.Common.UIColorPalette"/>).
        /// Порядок ключей соответствует порядку применения (в обычном апгрейде — основной атрибут,
        /// затем вторичный).</summary>
        public List<string> ImprovedAttributes { get; set; } = new();
        /// <summary>Тир применённого апгрейда ("Min"/"Avg"/"Max") или "Advanced" для продвинутого.
        /// null — предмет не улучшался.</summary>
        public string ImprovementTier { get; set; }
        [JsonIgnore] public bool IsWorking => Durability > 0;

        /// <summary>Можно ли поставить предмет в слот указанной категории.</summary>
        public bool CanFitInSlotCategory(string slotCategory)
        {
            if (string.IsNullOrEmpty(slotCategory)) return false;
            if (Category == slotCategory) return true;
            if (CompatibleSlots != null)
                foreach (var c in CompatibleSlots)
                    if (c == slotCategory) return true;
            return false;
        }
        public float GetParam(string key, float defaultValue = 0f) =>
            Params.TryGetValue(key, out var v) ? v : defaultValue;
        public string GetParamString(string key, string defaultValue = null) =>
            ParamStrings.TryGetValue(key, out var v) ? v : defaultValue;
        public float GetVulnerability(string damageType) =>
            WeaponVulnerability != null && WeaponVulnerability.TryGetValue(damageType, out var v) ? v : 1.0f;

        public static ItemInstance FromConfig(
            string category,
            string itemId,
            ItemConfig cfg,
            ItemsConfig equipConfig,
            float priceMult = 1f,
            float durabilityMult = 1f,
            int? gtlOverride = null)
        {
            if (cfg == null) return null;

            int techLevel = gtlOverride ?? cfg.TechLevel;
            if (techLevel < 1) techLevel = 1;
            if (techLevel > 10) techLevel = 10;

            var inst = new ItemInstance
            {
                Category = category,
                ItemId = itemId,
                Name = cfg.Name,
                Description = cfg.Description,
                Weight = cfg.Weight,
                TechLevel = techLevel,
                NoWear = cfg.NoWear,
                AllowEmbeds = cfg.AllowEmbeds,
                MaxEmbeds = cfg.AllowEmbeds ? System.Math.Max(1, cfg.MaxEmbeds) : cfg.MaxEmbeds,
                Activatable = cfg.Activatable,
                IsEquipment = cfg.IsEquipment,
                IsEmbeddable = cfg.IsEmbeddable,
                Embed = cfg.Embed,
                ManufacturerRace = cfg.Manufacturer?.Race,
                ManufacturerSide = cfg.Manufacturer?.Side,
                GraphicPath = cfg.GraphicPath,
                BodyGraphicPath = cfg.BodyGraphicPath,
                GraphicByManufacturer = cfg.GraphicByManufacturer,
                FixedRotation = cfg.FixedRotation,

                Price = Mathf.RoundToInt(cfg.Price * priceMult),
                MaxDurability = Mathf.Max(1, Mathf.RoundToInt(cfg.MaxDurability * durabilityMult)),
                IsImprovable = cfg.IsImprovable,
                RequiresNodesToImprove = cfg.RequiresNodesToImprove,
            };
            inst.Durability = inst.MaxDurability;

            float[] gtlDmgMult = null;
            if (cfg.Params != null)
            {
                foreach (var kv in cfg.Params)
                {
                    if (kv.Key == "GTLDamageMultipliers" && kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                    {
                        try { gtlDmgMult = kv.Value.ToObject<float[]>(); }
                        catch { }
                        continue;
                    }

                    if (kv.Key == "CompatibleSlots" && kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                    {
                        try { inst.CompatibleSlots = new List<string>(kv.Value.ToObject<string[]>()); }
                        catch { }
                        continue;
                    }

                    try { inst.Params[kv.Key] = kv.Value.ToObject<float>(); }
                    catch
                    {
                        try
                        {
                            if (kv.Value.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                                inst.ParamStrings[kv.Key] = string.Join(",", kv.Value.ToObject<string[]>());
                            else
                                inst.ParamStrings[kv.Key] = kv.Value.ToObject<string>();
                        }
                        catch { }
                    }
                }
            }

            // Оружие: BaseMinDmg/BaseMaxDmg → MinDmg/MaxDmg, масштабированное множителем ГТУ.
            // BaseDmg трактуется как урон на ГТУ 1; множитель ГТУ 1 принимается = 1 по умолчанию.
            if (inst.Params.ContainsKey("BaseMinDmg") || inst.Params.ContainsKey("BaseMaxDmg"))
            {
                float mult = 1f;
                if (gtlDmgMult != null && gtlDmgMult.Length > 0)
                {
                    int idx = Mathf.Clamp(techLevel - 1, 0, gtlDmgMult.Length - 1);
                    mult = gtlDmgMult[idx];
                }
                if (inst.Params.TryGetValue("BaseMinDmg", out var bMin))
                    inst.Params["MinDmg"] = bMin * mult;
                if (inst.Params.TryGetValue("BaseMaxDmg", out var bMax))
                    inst.Params["MaxDmg"] = bMax * mult;
            }

            // Ракетное оружие: базовый боезапас запоминаем как максимум (MaxAmmo),
            // чтобы дозаправлять/дозаряжать до полного в ангаре. Энергооружие (Ammo=-1) пропускаем.
            if (inst.Params.TryGetValue("Ammo", out var baseAmmo) && baseAmmo >= 0f)
                inst.Params["MaxAmmo"] = baseAmmo;

            if (category == "FuelTank")
                inst.CurrentFuel = Mathf.RoundToInt(inst.GetParam("Capacity"));
            // Уязвимости к урону — у HullSeries; задаются при установке серии, а не при создании корпуса.
            return inst;
        }
    }

    [Serializable]
    public class EquipmentSlots
    {
        public Dictionary<string, string> Slots { get; set; } = new();

        public string GetItemUid(string slotKey) =>
            Slots.TryGetValue(slotKey, out var uid) ? uid : null;

        public bool IsOccupied(string slotKey) =>
            Slots.TryGetValue(slotKey, out var uid) && uid != null;

        public List<string> GetOccupiedSlotsOfCategory(string category)
        {
            var result = new List<string>();
            foreach (var kv in Slots)
                if (kv.Value != null && kv.Key.StartsWith(category + "_", StringComparison.Ordinal))
                    result.Add(kv.Key);
            return result;
        }

        public IEnumerable<string> GetAllSlotsOfCategory(string category)
        {
            foreach (var kv in Slots)
                if (kv.Key.StartsWith(category + "_", StringComparison.Ordinal))
                    yield return kv.Key;
        }

        public string Set(string slotKey, string itemUid)
        {
            Slots.TryGetValue(slotKey, out var prev);
            Slots[slotKey] = itemUid;
            return prev;
        }

        public string Clear(string slotKey)
        {
            Slots.TryGetValue(slotKey, out var prev);
            Slots[slotKey] = null;
            return prev;
        }

        /// <summary>
        /// Перестраивает слоты под новый корпус. <paramref name="slotPlan"/> — итоговое число слотов
        /// по категориям (HullSlotsHullType + HullSlotsRace, либо HullSeries.Slots перебивает).
        /// Возвращает uid'ы предметов, вытесненных из исчезнувших слотов.
        /// </summary>
        public List<string> RebuildForHullSlots(Dictionary<string, int> slotPlan, ItemsConfig equipConfig)
        {
            var evicted = new List<string>();
            var newSlots = new Dictionary<string, string>();

            if (Slots.TryGetValue(SlotKeys.Hull, out var hullUid))
                newSlots[SlotKeys.Hull] = hullUid;

            if (slotPlan != null)
            {
                foreach (var kv in slotPlan)
                {
                    if (kv.Value <= 0) continue;
                    int maxGlobal = equipConfig?.GetCategoryCommon(kv.Key)?.MaxSlots ?? 1;
                    int count = Mathf.Min(kv.Value, maxGlobal);
                    for (int i = 0; i < count; i++)
                    {
                        string key = $"{kv.Key}_{i}";
                        newSlots[key] = Slots.TryGetValue(key, out var uid) ? uid : null;
                    }
                }
            }

            foreach (var kv in Slots)
                if (kv.Value != null && !newSlots.ContainsKey(kv.Key))
                    evicted.Add(kv.Value);

            Slots = newSlots;
            return evicted;
        }

        public void InitForHullSlots(Dictionary<string, int> slotPlan, ItemsConfig equipConfig)
        {
            Slots.Clear();
            if (slotPlan == null) return;

            foreach (var kv in slotPlan)
            {
                if (kv.Value <= 0) continue;
                int maxGlobal = equipConfig?.GetCategoryCommon(kv.Key)?.MaxSlots ?? 1;
                int count = Mathf.Min(kv.Value, maxGlobal);
                for (int i = 0; i < count; i++)
                    Slots[$"{kv.Key}_{i}"] = null;
            }
        }
    }

    [Serializable]
    public class ShipInventory
    {
        public List<ItemInstance> Items { get; set; } = new();


        public Dictionary<string, ItemStack> Stacks { get; set; } = new();

        [JsonIgnore]
        private readonly Dictionary<string, ItemInstance> _index = new();
        public ItemInstance GetByUid(string uid) =>
            _index.TryGetValue(uid, out var item) ? item : null;

        public bool Contains(string uid) => _index.ContainsKey(uid);
        public int Count => Items.Count;
        public int TotalWeight()
        {
            // Стеки зеркалятся в Items как ItemInstance с тем же Weight, чтобы UI трюма видел груз
            // как обычные предметы. TotalWeight считаем только по Items — иначе будет двойной учёт.
            int w = 0;
            foreach (var item in Items) w += item.Weight;
            return w;
        }

        /// <summary>Сумма веса всех стеков (товары/минералы). Совпадает с суммой веса парных
        /// ItemInstance в <see cref="Items"/>; оставлен для совместимости с диагностикой.</summary>
        public int TotalStacksWeight()
        {
            int w = 0;
            foreach (var s in Stacks.Values) w += s.TotalWeight;
            return w;
        }

        public void Add(ItemInstance item)
        {
            if (item == null) return;
            Items.Add(item);
            _index[item.Uid] = item;
        }

        public bool Remove(string uid)
        {
            var item = GetByUid(uid);
            if (item == null) return false;
            Items.Remove(item);
            _index.Remove(uid);
            return true;
        }

        public ItemInstance TakeByUid(string uid)
        {
            var item = GetByUid(uid);
            if (item != null) Remove(uid);
            return item;
        }

        public void AddStack(ItemStack stack)
        {
            if (stack == null || string.IsNullOrEmpty(stack.ItemId)) return;

            if (Stacks.TryGetValue(stack.ItemId, out var existing))
            {
                existing.TotalWeight += stack.TotalWeight;
            }
            else
            {
                Stacks[stack.ItemId] = new ItemStack
                {
                    ItemId = stack.ItemId,
                    Category = stack.Category,
                    Name = stack.Name,
                    TotalWeight = stack.TotalWeight,
                    BasePrice = stack.BasePrice,
                    IsGoods = stack.IsGoods
                };
            }
            SyncStackItem(Stacks[stack.ItemId]);
        }

        public ItemStack TakeStack(string itemId, int amount)
        {
            if (!Stacks.TryGetValue(itemId, out var stack)) return null;

            int taken = Mathf.Min(amount, stack.TotalWeight);
            stack.TotalWeight -= taken;

            string snapId = stack.ItemId;
            string snapCategory = stack.Category;
            string snapName = stack.Name;
            int snapPrice = stack.BasePrice;
            bool snapIsGoods = stack.IsGoods;

            if (stack.TotalWeight <= 0)
            {
                Stacks.Remove(itemId);
                RemoveStackItem(itemId);
            }
            else
            {
                SyncStackItem(stack);
            }

            return new ItemStack
            {
                ItemId = snapId,
                Category = snapCategory,
                Name = snapName,
                TotalWeight = taken,
                BasePrice = snapPrice,
                IsGoods = snapIsGoods
            };
        }

        /// <summary>Префикс Uid для «теневых» ItemInstance, отражающих стеки в <see cref="Items"/>.
        /// Стабильный по ItemId — стек одного товара всегда соответствует одному ItemInstance.</summary>
        public const string StackItemUidPrefix = "stack:";

        /// <summary>Создать/обновить ItemInstance, отображающий стек в инвентаре как предмет.
        /// Weight = TotalWeight, Price = TotalWeight × BasePrice (полная стоимость кучи).</summary>
        private void SyncStackItem(ItemStack stack)
        {
            if (stack == null || string.IsNullOrEmpty(stack.ItemId)) return;
            string uid = StackItemUidPrefix + stack.ItemId;
            if (!_index.TryGetValue(uid, out var inst) || inst == null)
            {
                inst = new ItemInstance
                {
                    Uid = uid,
                    Category = stack.Category ?? "Goods",
                    ItemId = stack.ItemId,
                    Name = stack.Name ?? stack.ItemId,
                    NoWear = true,
                    Durability = 1,
                    MaxDurability = 1,
                    AllowModules = false,
                    TechLevel = 0,
                };
                Items.Add(inst);
                _index[uid] = inst;
            }
            inst.Weight = stack.TotalWeight;
            inst.Price = stack.TotalWeight * stack.BasePrice;
            inst.Name = stack.Name ?? stack.ItemId;
            // Иконка может зависеть от количества (GraphicSteps в ItemsConfig) —
            // пересчитываем при каждом изменении стека.
            inst.GraphicPath = StackGraphics.ResolveIcon(stack.ItemId, stack.TotalWeight);
        }

        private void RemoveStackItem(string itemId)
        {
            string uid = StackItemUidPrefix + itemId;
            if (!_index.TryGetValue(uid, out var inst) || inst == null) return;
            Items.Remove(inst);
            _index.Remove(uid);
        }
        public void RebuildIndex()
        {
            _index.Clear();
            foreach (var item in Items)
                if (item != null) _index[item.Uid] = item;

            // Реконсиляция Stacks → Items для старых сейвов: у каждого стека должен быть
            // теневой ItemInstance с Uid = "stack:<itemId>", чтобы UI трюма видел груз.
            if (Stacks != null)
                foreach (var s in Stacks.Values)
                    if (s != null && s.TotalWeight > 0) SyncStackItem(s);
        }
    }

    public class PlanetShop
    {
        // goodId → запись с ценой и стоком
        public Dictionary<string, ShopGoodEntry> Goods { get; set; } = new();
    }

    public class ShopGoodEntry
    {
        public int Stock    { get; set; }  // текущий сток (единицы веса)
        public int BuyPrice { get; set; }  // цена покупки у планеты (за 1 ед. веса)
        public int SellPrice{ get; set; }  // цена продажи планете (за 1 ед. веса)
    }

    public class PlanetEquipmentShop
    {
        // uid → экземпляр оборудования в продаже
        public Dictionary<string, ItemInstance> Items { get; set; } = new();
    }
}
