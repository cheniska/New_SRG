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

        public string Uid { get; set; } = GameRng.NewUid();

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
        public Vector2 PreviousPosition;
        public Vector2 TargetPosition;

        // Маршрут и курс — сохраняются: без них после загрузки траектория строится заново
        // из текущей точки, и корабль идёт немного иначе, чем шёл бы без сохранения.
        public Queue<Vector2> TargetQueue = new Queue<Vector2>();
        public float CurrentHeading = float.NaN;
        public List<Vector2> Waypoints = new List<Vector2>();
        public int WaypointIndex = 0;
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
        public string LastAttackerUid { get; set; }
        public int LastAttackerTurn { get; set; } = -1;
        /// <summary>Ход последнего SOS-вызова этого корабля (ActionCallForHelp) — кулдаун.</summary>
        public int LastHelpCallTurn { get; set; } = -1;

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

        /// <summary>Фаза двухфазной посадки (SR2HD §7). Транзитное состояние, не сохраняется.
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

        /// <summary>Характер NPC. Сохраняется: после загрузки корабль остаётся «тем же» пилотом.</summary>
        public ShipPersonality Personality { get; set; }
        /// <summary>Состояние ИИ (активность, приказы, директива, страх). Сохраняется через
        /// <see cref="SRG.Simulation.AiStateContractResolver"/>; после загрузки связывается с
        /// <see cref="Personality"/> корабля (<see cref="NpcBrain.RestoreAfterLoad"/>).</summary>
        public NpcBrain Brain { get; set; }

        public int Money { get; set; }

        /// <summary>Нод-счёт корабля. Используется <see cref="SRG.Equipment.ImprovementService"/> при
        /// улучшении оборудования с флагом <see cref="ItemInstance.RequiresNodesToImprove"/>: сначала
        /// списываем физические стеки Node из трюма, если не хватает — дораскладываем с нод-счёта.
        /// Механика пополнения счёта появится позже (диздок docs/design/SB_Equipment_Improvement.md).</summary>
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
        public Vector2 TowVelocity { get; set; }

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
        /// <summary>Ход последнего «сброса груза от страха» (FearDropService) — кулдаун. Живёт в сейве.</summary>
        public int LastFearDropTurn { get; set; } = -9999;
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
                SpritesheetPath = GameWorld.ShipSheetResolver != null
                    ? GameWorld.ShipSheetResolver(hullItem.BodyGraphicPath, ownerForGfx, raceForGfx)
                    : hullItem.BodyGraphicPath;
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

        public bool ForsageActive { get; set; } = false;
        /// <summary>UID цели для выстрела игрока в следующем ходу. Сбрасывается после выстрела.</summary>
        public string ManualShootTargetUid { get; set; }
        /// <summary>true — цель является астероидом, false — кораблём.</summary>
        public bool ManualShootTargetIsAsteroid { get; set; }
        /// <summary>true — стрелять только из самой дальнобойной пушки (режим follow-attack «дальняя»).</summary>
        public bool ManualShootOnlyLongestRange { get; set; }
        /// <summary>true — стрельба назначена авто-режимом следования, не ручным кликом;
        /// подавляет сообщение «нет оружия в радиусе» при многократных тиках.</summary>
        public bool ManualShootIsAutoFollow { get; set; }
        /// <summary>Активные боевые эффекты (замедления, DoT, дренаж…) — сохраняются.</summary>
        public List<ActiveCombatEffect> ActiveEffects { get; set; } = new List<ActiveCombatEffect>();
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
            var map = GameWorld.GeneratedGalaxy?.StarsMap;
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
                var equipConfig = GameWorld.Context?.ItemsConfig;
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
}
