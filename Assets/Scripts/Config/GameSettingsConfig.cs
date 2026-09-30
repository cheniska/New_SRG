using UnityEngine;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Orders;

namespace SRG.Config
{
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Game/Settings Config")]
    public class GameSettingsConfig : ScriptableObject
    {
        [Header("Input Controls")]
        public string HorizontalAxis = "Horizontal";
        public string VerticalAxis = "Vertical";
        public string ZoomAxis = "Mouse ScrollWheel";
        public KeyCode ResetCameraKey = KeyCode.Home;
        public KeyCode PausePlayKey = KeyCode.Space;
        public int DragMouseButton = 1;

        [Tooltip("Клавиша открытия/закрытия панели снаряжения.")]
        public KeyCode InventoryKey = KeyCode.I;

        [Header("Camera Movement")]
        public float MoveSpeed = 20f;
        public float DragSpeed = 1f;
        public float EdgeThickness = 10f;
        public bool EnableEdgeScrolling = true;

        [Header("Camera Zoom")]
        [Tooltip("Минимальный зум как доля дефолтного (Home). 0.5 = ×0.5 от дефолта (макс. приближение).")]
        public float MinZoomMult = 0.5f;
        [Tooltip("Максимальный зум как доля дефолтного (Home). 4 = ×4 от дефолта (макс. отдаление).")]
        public float MaxZoomMult = 4f;
        public float ZoomSpeed = 10f;

        [Header("Display")]
        [Tooltip("Ширина окна/фуллскрина в пикселях. 0 — использовать текущее разрешение экрана.")]
        public int ResolutionWidth = 1920;
        [Tooltip("Высота окна/фуллскрина в пикселях.")]
        public int ResolutionHeight = 1080;
        [Tooltip("Режим окна: Windowed, FullScreenWindow (безрамочный), ExclusiveFullScreen.")]
        public FullScreenMode WindowMode = FullScreenMode.FullScreenWindow;

        [Header("Map Limits")]
        public Vector2 MapLimit = new Vector2(1024f, 1024f);

        [Header("Camera Centering")]
        public bool EnableCentering = true;
        public KeyCode CenterCameraKey = KeyCode.C;

        [Header("Route")]
        [Tooltip("Удерживать для добавления путевых точек вместо замены маршрута.")]
        public KeyCode MultiWaypointKey = KeyCode.LeftShift;

        [Tooltip("Клавиша включения/выключения режима стрельбы игрока.")]
        public KeyCode WeaponModeKey = KeyCode.Q;

        [Tooltip("Клавиша включения/выключения режима связи (диалога с кораблями).")]
        public KeyCode DialogModeKey = KeyCode.T;

        [Tooltip("Клавиша включения форсажа корабля игрока на текущий ход (требует установленного и рабочего форсажа).")]
        public KeyCode ForsageKey = KeyCode.F;

        [Header("Info Center (Galaxy News)")]
        [Tooltip("Максимум хранимых новостей галактики. Старые вытесняются по FIFO.")]
        public int NewsMaxCount = 100;
        [Tooltip("Количество новостей, одновременно видимых в форме инфоцентра. Остальные доступны через прокрутку.")]
        public int NewsVisibleCount = 5;

        [Header("Info Popup")]
        [Tooltip("Задержка (сек) перед появлением всплывающего окна при наведении.")]
        public float InfoPopupHoverDelay = 0.5f;
        [Tooltip("Ширина всплывающего информационного окна.")]
        public float InfoPopupWidth = 500f;
        [Tooltip("Максимальная высота области прокрутки во всплывающем окне.")]
        public float InfoPopupMaxHeight = 360f;
        [Tooltip("Показывать траекторию NPC-корабля при наведении (в будущем — только при наличии особого радара).")]
        public bool ShowNpcTrajectoryOnHover = true;
        [Tooltip("Смещение окна от позиции курсора по X (пикс).")]
        public float InfoPopupOffsetX = 18f;
        [Tooltip("Смещение окна от позиции курсора по Y (пикс).")]
        public float InfoPopupOffsetY = 18f;
        [Tooltip("Окно следует за курсором, пока объект под курсором не изменился.")]
        public bool InfoPopupFollowMouse = false;

        [Header("Performance")]
        [Tooltip("Ограничение кадров в секунду. 0 — без ограничения (или по vSync уровня качества).\n" +
                 "Без ограничения при выключенном vSync игра рисует сотни кадров впустую.")]
        public int TargetFrameRate = 60;
        [Tooltip("В фазе планирования без ввода игрока (мышь/клавиши) рисовать каждый N-й кадр.\n" +
                 "Ввод и логика по-прежнему обрабатываются каждый кадр. 1 — рисовать всегда.")]
        public int IdleRenderInterval = 2;
        [Tooltip("Сколько секунд без ввода считается простоем для IdleRenderInterval.")]
        public float IdleRenderDelay = 0.5f;

        [Header("Game Logic")]
        public float TurnDuration = 2.0f;
        [Tooltip("Останавливать авторежим когда игрок выполнил все действия маршрута.")]
        public bool SimulationInterruptOnActionDone = true;
        [Tooltip("Не ставить игру на паузу при сворачивании окна / потере фокуса.\n" +
                 "Применяется через Application.runInBackground при загрузке и при смене опции.\n" +
                 "В редакторе тоже работает: при переключении на другое окно Play Mode продолжает симуляцию.")]
        public bool RunInBackground = true;
        [Tooltip("Разрешить смену снаряжения в открытом космосе.\n" +
                 "false (по дизайн-документу) — смена только при стыковке с HasDock-объектом.\n" +
                 "Проверяется в UI-контроллере, не в симуляции.")]
        public bool AllowEquipmentChangeInSpace = false;

        [Header("NPC Spawning")]
        [Tooltip("Максимум кораблей, которые могут заспавниться в одной звёздной системе за один игровой день (за один SpawnSystem.DailyTick). " +
                 "1 = классическое поведение «не более одного корабля в систему за день», защищает от каскадных спавнов. " +
                 "Учитывается всеми политиками (Warrior/Civilian/Ranger/Pirate/Linkor/Dominator). Не влияет на InitialDistribution.")]
        public int MaxShipsSpawnedPerStarPerDay = 1;

        [Header("Player Spawn")]
        [Tooltip("ShipType для корабля игрока при генерации новой галактики.")]
        public string PlayerShipTypeId = "Ranger";
        [Tooltip("Owner (политическая принадлежность) корабля игрока.")]
        public string PlayerOwnerId = "Race3_Main";
        [Tooltip("Race (биологическая раса) пилота игрока.")]
        public string PlayerRaceId = "Race3";
        [Tooltip("Начальная позиция игрока внутри стартовой звёздной системы.")]
        public Vector2 PlayerSpawnPosition = new Vector2(1.5f, 0f);
        [Tooltip("Начальный баланс игрока (кредиты).")]
        public int PlayerStartMoney = 1000;

        [Header("Display Fallbacks")]
        [Tooltip("Fallback-цвет иконок на миникарте если цвет не определён. Формат R,G,B (0-255).")]
        public string DefaultMinimapColor = "255,255,255";
        [Tooltip("Fallback-цвет названий звёзд. Формат R,G,B (0-255).")]
        public string DefaultStarNameColor = "200,200,200";

        [Header("Asteroids")]
        [Tooltip("Дефолтное максимальное число астероидов одновременно в системе с игроком. " +
                 "Может быть переопределено в StarData.MaxAsteroids или PremadeConfig.")]
        public int DefaultMaxAsteroids = 2;

        [Tooltip("Порог «опасно близко» — мировая дистанция астероида до планеты, при которой планета " +
                 "помечается как потенциальная цель. Если игрок сбивает такой астероид, начисляется награда.")]
        public float AsteroidDangerCloseRadius = 5.0f;
        [Tooltip("Базовая награда за уничтожение угрожающего планете астероида (кредитов). " +
                 "Умножается на массу астероида.")]
        public int AsteroidRewardBase = 100;

        [Tooltip("Путь к папке со спрайтами астероидов (Resources). " +
                 "Тип астероида добавляется как подпапка: {AsteroidSpritesPath}/{TypeId}/")]
        public string AsteroidSpritesPath = "Graphics/SpaceObjects/Asteroids";

        [Tooltip("Путь к спрайту иконки астероида на миникарте.")]
        public string AsteroidMinimapIconPath = "Graphics/UI/Minimap/Icons/Asteroid";

        [Header("Explosion")]
        [Tooltip("Resources-путь к спрайт-листу взрыва без расширения (напр., " +
                 "'Graphics/Effects/Explosions/Asteroids/Default/001'). Рядом должен лежать " +
                 "одноимённый .png.json c FrameCount/Cols/Rows — читается GraphicsManager.")]
        public string ExplosionSpritePath = "Graphics/Effects/Explosions/Asteroids/Default/001";

        [Tooltip("FPS воспроизведения анимации взрыва.")]
        public float ExplosionFPS = 12f;

        [Tooltip("Масштаб визуала взрыва в мировых единицах.")]
        public float ExplosionScale = 1.5f;

        [Header("Dropped Items")]
        [Tooltip("Время жизни предмета на сцене в секундах. По истечении — исчезает.")]
        public float DroppedItemLifetime = 30f;

        [Tooltip("Базовый путь к спрайтам минералов (Resources). " +
                 "Структура: {MineralSpritesPath}/Mineral_{size}_c.png")]
        public string MineralSpritesPath = "Graphics/Items/Stackable/Goods/Minerals";

        [Tooltip("Число визуальных тиров для минеральных спрайтов (по умолчанию 5). " +
                 "Граница тира = TotalWeight / (DropWeightMax / TierCount), округлённая.")]
        public int MineralSpriteTierCount = 4;

        [Header("Galaxy Constants — Galaxy")]
        public string DefaultGalaxyKey = "MilkyWay";
        public float SectorRadius = 5.0f;

        [Header("Galaxy Constants — Fallbacks")]
        public string FallbackStarColor = "White";
        public string FallbackSatelliteSize = "Mid";
        public string FallbackEmblemPath = "Empty";
        public string FallbackPlanetTexture = "Graphics/SpaceObjects/Planets/Textures/Unic/Solar_Earth";
        public string FallbackSatelliteTexture = "Graphics/SpaceObjects/Planets/Textures/Unic/Solar_Moon";

        [Header("Galaxy Constants — Paths")]
        public string SatelliteTexturesPath = "Graphics/SpaceObjects/Planets/Textures/Satellites";
        public string CommonPlanetTexturesPath = "Graphics/SpaceObjects/Planets/Textures/Common";
        public string OrbitalObjectsPath = "Graphics/SpaceObjects/Planets/Orbital";
        public string PlanetMaskPath = "Graphics/SpaceObjects/Planets/Mask/160";
        public string CloudTexturesPath = "Graphics/SpaceObjects/Planets/Textures/Clouds";
        public int CloudTexturesCount = 3;

        [Header("Galaxy Constants — Orbits")]
        public float MinOrbitRadius = 1000f;
        public float SystemPadding = 400f;
        [Tooltip("Среднее расстояние между соседними планетами (единицы мира). Используется как центр диапазона генерации орбит.")]
        public float AvgPlanetDistanceUnits = 900f;
        [Tooltip("Разброс ±вокруг среднего расстояния между планетами. OrbitGapMin = Avg - Spread, OrbitGapMax = Avg + Spread.")]
        public float PlanetDistanceSpread = 100f;
        public float FirstOrbitRadiusMin = 1200f;
        public float FirstOrbitRadiusMax = 2000f;

        [Header("Galaxy Constants — Planet Generation")]
        [Tooltip("Шанс того, что плотность планеты будет в диапазоне [1, 2] вместо значения из типа.")]
        public float HighDensityChance = 0.05f;
        public float PlanetOrbitSpeedMin = 50f;
        public float PlanetOrbitSpeedMax = 300f;
        public int PlanetDaySpeedMin = 20;
        public int PlanetDaySpeedMax = 60;
        public float CloudSpeedFactorMin = 0.6f;
        public float CloudSpeedFactorMax = 0.8f;
        public float OrbitalObjectChance = 0.3f;
        public float NoneOrbitMaxFraction = 0.4f;
        public int FallbackMaxPlanetsPerStar = 12;
        public float FallbackPlanetPixelRadius = 120f;

        [Header("Galaxy Constants — Satellites")]
        public float SatelliteOrbitBaseOffset = 35f;
        public float SatelliteOrbitSpacing = 25f;
        public float SatelliteOrbitSpeedMin = 40f;
        public float SatelliteOrbitSpeedMax = 120f;
        public int SatelliteDaySpeedMin = 10;
        public int SatelliteDaySpeedMax = 39;
        public float SatelliteCloudSpeedFactor = 0.7f;

        [Header("Galaxy Constants — Background")]
        public int BgStarCountFar = 25;
        public int BgStarCountMid = 15;
        public int BgStarCountMidNear = 6;
        public int BgStarCountNear = 8;
        public int BgTexSizeFar = 1024;
        public int BgTexSizeNear = 512;
        public float BgParallaxFar = 0.02f;
        public float BgParallaxMid = 0.05f;
        public float BgParallaxMidNear = 0.08f;
        public float BgParallaxNear = 0.12f;
        public float BgLayerTileSize = 500f;
        public int BgVoidTextureWidth = 1920;
        public int BgVoidTextureHeight = 1080;
        public float BgVoidScaleFactor = 2.5f;

        [Header("Galaxy Constants — Log")]
        public string LogFallbackStarColor = "silver";
        public string LogNeutralOwnerLabel = "Neutral";

        // ──────────────────────────────────────────────────────────────────────────
        // NPC AI Balance — балансные параметры, загружаемые в NpcBalance.cs при старте.
        // Все поля помечены префиксом NpcAi_ чтобы группироваться в инспекторе.
        // Значения по умолчанию ТОЧНО соответствуют исходным const в коде NpcAI
        // (на момент июня 2026); менять их в этом ассете — балансит поведение AI
        // без перекомпиляции. Где используется — указано в Tooltip.
        // ──────────────────────────────────────────────────────────────────────────

        [Header("NPC AI — Дистанции (мировые ед.; *Sq — квадраты)")]
        [Tooltip("NpcBrain.FindAlliedUnderAttack — радиус² «союзник в беде». 16 = 4².")]
        public float NpcAi_AllyHelpTriggerRadiusSq = 16f;
        [Tooltip("ActionEscort.FindThreatTo — радиус² угрозы рядом с подопечным. 25 = 5².")]
        public float NpcAi_EscortThreatRadiusSq = 25f;
        [Tooltip("OrderAttack — за этой дистанцией Order считается невыполненным (преследование продолжается).")]
        public float NpcAi_ChaseRadius = 8f;
        [Tooltip("OrderFlee — норма дистанции побега от угрозы.")]
        public float NpcAi_FleeDistance = 10f;
        [Tooltip("OrderFollow — точка позади leader-а с jitter ±45° по UID.")]
        public float NpcAi_FollowDistance = 1.5f;
        [Tooltip("OrderMoveTo + OrderPatrol — порог² «прибыли в точку». 0.25 = 0.5².")]
        public float NpcAi_ArrivalThresholdSq = 0.25f;
        [Tooltip("OrderLoot — радиус² «лут собран». 0.36 = 0.6².")]
        public float NpcAi_PickupRadiusSq = 0.36f;
        [Tooltip("OrderRequestCeasefire + OrderOfferMoneyRansom — дистанция переговоров.")]
        public float NpcAi_NegotiateRange = 2.0f;
        [Tooltip("OrderTransfer — дистанция передачи предмета.")]
        public float NpcAi_TransferRange = 1.0f;
        [Tooltip("Дальность боевого контакта: дальше цель не берётся (NpcBrain) и погоня прекращается (ActionPursueAndAttack). Не действует на военные директивы ГШ.")]
        public float NpcAi_MaxEngageRange = 10f;

        [Header("NPC AI — Fear (NpcBrain.AssessFear)")]
        [Tooltip("Радиус учёта врагов в fear-оценке. Меньше MaxEngageRange — паника только по «прямо здесь».")]
        public float NpcAi_Fear_MaxThreatRadius = 6f;
        [Tooltip("Прирост порога от количества врагов: threshold_mul = 1 + (cnt-1) × этот коэф.")]
        public float NpcAi_Fear_CrowdMultiplier = 0.3f;
        [Tooltip("Множитель порога страха для Military-класса (>1 = терпит больше).")]
        public float NpcAi_Fear_ThresholdMult_Military  = 1.3f;
        [Tooltip("Множитель порога страха для Pirate-класса.")]
        public float NpcAi_Fear_ThresholdMult_Pirate    = 1.0f;
        [Tooltip("Множитель порога страха для Mercenary-класса.")]
        public float NpcAi_Fear_ThresholdMult_Mercenary = 1.0f;
        [Tooltip("Множитель порога страха для Civilian-класса (<1 = пугается легче).")]
        public float NpcAi_Fear_ThresholdMult_Civilian  = 0.7f;
        [Tooltip("Партнёр «сильного лидера» не паникует, если Strength(leader) × этот коэф ≥ Strength(self).")]
        public float NpcAi_Fear_PartnerLeaderStrengthMult = 1.0f;
        [Tooltip("Прирост давления страха от Frustration (0..100): raw ×= 1 + Frustration/100 × K. K=1 → удвоение при Frustration=100.")]
        public float NpcAi_Fear_FrustrationBoost = 1.0f;

        [Header("NPC AI — Тайминги (ходы)")]
        [Tooltip("NpcBrain — период инвалидации Power-кэша звезды.")]
        public int NpcAi_StrengthCacheTurns = 10;
        [Tooltip("NpcBrain — через сколько ходов забывается LastAttackerUid.")]
        public int NpcAi_LastAttackerMemoryTurns = 20;
        [Tooltip("ActionPursueAndAttack — период проверки прогресса по HP цели (рост Frustration).")]
        public int NpcAi_FrustrationCheckInterval = 5;
        [Tooltip("NpcBrain — пассивный decay Frustration за ход, когда нет свежего агрессора.")]
        public float NpcAi_FrustrationPassiveDecay = 1f;
        [Tooltip("ActionMine — максимум циклов добычи прежде чем действие завершится.")]
        public int NpcAi_MaxMineCycles = 3;
        [Tooltip("ActionMine — idle между циклами добычи.")]
        public int NpcAi_MineTurns = 4;
        [Tooltip("ActionDeliver — idle загрузки/выгрузки в точке A/B.")]
        public int NpcAi_LoadTurns = 2;
        [Tooltip("ActionTrade — idle у планеты между перелётами.")]
        public int NpcAi_DockDuration = 3;

        [Header("NPC AI — Trader (TraderAI + ActionGoodsTrader)")]
        [Tooltip("TraderAI — минимальная прибыль за рейс (за единицу).")]
        public int NpcAi_MinProfitPerUnit = 2;
        [Tooltip("TraderAI + ActionGoodsTrader — сколько ходов «отдыхать» при no_profit.")]
        public int NpcAi_IdleWaitTurns = 8;
        [Tooltip("TraderAI — глубина поиска маршрутов по соседним системам (1 = только прямая досягаемость).")]
        public int NpcAi_MaxJumpHops = 1;
        [Tooltip("TraderAI — топ-N кандидатов для глубокой оценки прибыли.")]
        public int NpcAi_CandidatePruneLimit = 6;

        [Header("NPC AI — Боевые проценты")]
        [Tooltip("OrderRequestCeasefire — доля стека, передаваемого как «выкуп».")]
        public float NpcAi_CeasefireOfferFraction = 0.3f;

        [Header("Repair — стоимость восстановления")]
        [Tooltip("Базовая стоимость восстановления 1 единицы Durability оборудования / HP корпуса, кредитов.")]
        public int Repair_CostPerDurabilityPoint = 10;
        [Tooltip("Прибавка на тир: finalPricePerPoint = base × (1 + TL × TLMult). Пример: TLMult=0.5, T1=×1.5, T10=×6.")]
        public float Repair_TLMultiplier = 0.5f;

        [Header("NPC AI — LandResupply / Outfitter")]
        [Tooltip("Порог улучшения при апгрейде: score(new) >= score(current) × (1 + MinImprovement). 0.10 = минимум +10%.")]
        public float Outfitter_MinImprovement = 0.10f;
        [Tooltip("Фикс-резерв «на всякий», который NPC не тратит на апгрейд/ремонт (топливо оплачивается сверху).")]
        public int Outfitter_EmergencyCashReserve = 200;
        [Tooltip("Множитель к стоимости полного бака: NPC сохраняет FuelReserve = fullTankCost × этот множитель.")]
        public float Outfitter_FuelReserveMultiplier = 2f;
        [Tooltip("Порог composite-нужды, при превышении которого включается ActionLandResupply.")]
        public float Resupply_PressureThreshold = 1.0f;
        [Tooltip("Доля HP корпуса, ниже которой pressure получает +1 (нужен ремонт).")]
        public float Resupply_HullPressureBelow = 0.80f;
        [Tooltip("Доля топлива, ниже которой pressure получает +1 (нужна дозаправка).")]
        public float Resupply_FuelPressureBelow = 0.50f;
        [Tooltip("Порог денег, выше которого pressure получает +0.5 (можно потратить на апгрейд).")]
        public int Resupply_MoneyPressureAbove = 1500;

        [Header("Wormholes")]
        [Tooltip("Сколько ходов червоточина проводит в открытом (cycle) состоянии. Opening/Closing " +
                 "продолжаются пока играют соответствующие клипы (по 1 ходу в симуляции).")]
        public int Wormhole_OpenTurns = 90;
        [Tooltip("Средний интервал между появлениями новых червоточин в галактике (ходов). " +
                 "На каждом ходу шанс спавна = 1/этого_числа. 0 — спавн отключён (используется для отладки).")]
        public int Wormhole_SpawnAvgIntervalTurns = 30;
        [Tooltip("Максимум одновременно активных червоточин в галактике. Спавн блокируется при достижении.")]
        public int Wormhole_MaxActive = 6;
        [Tooltip("Минимальная дистанция (парсеки) до целевой системы червоточины. Ближе не выбираем.")]
        public float Wormhole_MinDistancePc = 20f;
        [Tooltip("Максимальная дистанция (парсеки) до целевой системы. Дальше не выбираем.")]
        public float Wormhole_MaxDistancePc = 200f;
        [Tooltip("Мировой размер червоточины (диаметр в единицах мира) — для визуала.")]
        public float Wormhole_WorldSize = 1.5f;
        [Tooltip("Радиус в мировых единицах, при подлёте в который корабль считается вошедшим в червоточину. " +
                 "Используется как HyperjumpEdge tolerance.")]
        public float Wormhole_EnterRadius = 0.5f;
        [Tooltip("Путь к спрайтшиту фазы OPENING (Resources).")]
        public string Wormhole_OpeningPath = "Graphics/Effects/Wormhole/open";
        [Tooltip("Путь к спрайтшиту фазы OPEN/CYCLE (Resources).")]
        public string Wormhole_CyclePath   = "Graphics/Effects/Wormhole/cycle";
        [Tooltip("Путь к спрайтшиту фазы CLOSING (Resources).")]
        public string Wormhole_ClosingPath = "Graphics/Effects/Wormhole/close";
        [Tooltip("Путь к иконке червоточины для галакарты и миникарты (Resources).")]
        public string Wormhole_IconPath    = "Graphics/UI/Minimap/wormhole_map";

        // ──────────────────────────────────────────────────────────────────────────
        // Прочие Resources-пути и шейдеры. Все здесь, чтобы моддинг мог их
        // переопределить одним ассетом. Копируются в GalaxyConstants.Initialize.
        // ──────────────────────────────────────────────────────────────────────────

        [Header("Resources — UI")]
        [Tooltip("Иконка планеты на миникарте (Resources).")]
        public string PlanetMinimapIconPath = "Graphics/UI/Minimap/Icons/Planet";
        [Tooltip("Корень ресурсов формы корабля (Resources). Оканчивается на '/'.")]
        public string ShipFormResourceRoot = "Graphics/UI/ShipForm/";
        [Tooltip("Resources-путь к JSON конфигурации слотов формы корабля (без расширения).")]
        public string ShipFormSlotsConfigPath = "Config/ShipFormSlots";
        [Tooltip("Базовая папка иконок звёзд на миникарте по цветам. Полный путь = {этот путь}/{color}.")]
        public string StarMinimapIconsBasePath = "Graphics/UI/Minimap/Icons/Stars";

        [Header("Resources — Effects")]
        [Tooltip("Спрайтшит фазы BEGIN гиперпрыжка (Resources).")]
        public string HyperjumpBeginPath = "Graphics/Effects/Hyperjump/begin";
        [Tooltip("Спрайтшит фазы MID гиперпрыжка (Resources).")]
        public string HyperjumpMidPath   = "Graphics/Effects/Hyperjump/mid";
        [Tooltip("Спрайтшит фазы END гиперпрыжка (Resources).")]
        public string HyperjumpEndPath   = "Graphics/Effects/Hyperjump/end";

        [Header("Resources — Objects")]
        [Tooltip("Базовая папка спрайтов контейнеров (Resources).")]
        public string ContainersBasePath = "Graphics/Items/Containers";
        [Tooltip("Базовая папка текстур планет по размерам. Полный путь = {этот путь}/{size}.")]
        public string PlanetTexturesBasePath = "Graphics/SpaceObjects/Planets/Textures";

        [Header("Shaders")]
        [Tooltip("Шейдер туманности фона (Resources).")]
        public string NebulaShaderPath       = "Shaders/NebulaWispy";
        [Tooltip("Шейдер аддитивного слоя звёзд фона (Resources).")]
        public string StarAdditiveShaderPath = "Shaders/StarAdditive";
        [Tooltip("Шейдер крупных звёзд с мерцанием (Resources).")]
        public string StarLargeShaderPath    = "Shaders/StarLarge";
    }
}
