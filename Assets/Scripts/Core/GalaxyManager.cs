using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SRG.Combat;
using SRG.Config;
using SRG.Dialog;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.NpcAI.Spawning;
using SRG.Presentation;
using SRG.Presentation.Common;
using SRG.Presentation.Effects;
using SRG.Presentation.World;
using SRG.Ships.Movement;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.UI.Screens;
using SRG.Simulation;

namespace SRG.Core
{
    // Центральный менеджер пошаговой симуляции.
    // Цикл хода: Planning → (StartTurn) → Simulation (анимация) → Planning.
    // События хода поднимаются через GameWorld: OnTurnCalculate (симуляция), OnTurnAnimate (каждый кадр), OnTurnComplete.
    // Симуляция видит менеджер только как IWorldHost (см. SRG.Simulation.GameWorld).
    public class GalaxyManager : MonoBehaviour, IWorldHost
    {
        public static GalaxyManager Instance { get; private set; }

        [Header("Configuration JSONs")]
        [SerializeField] private TextAsset galaxyConfigJson;
        [SerializeField] private TextAsset textConfigJson;
        [SerializeField] private TextAsset premadeConfigJson;
        [SerializeField] private TextAsset itemsConfigJson;
        [SerializeField] private TextAsset dialogsConfigJson;
        [SerializeField] private TextAsset partnersConfigJson;

        [Header("Settings")]
        public int GalaxySeed = 0;
        [Tooltip("UID стартовой звезды. Пусто — первая звезда.")]
        [SerializeField] private string startingStarUid;
        [SerializeField] private GameSettingsConfig settings;
        [Tooltip("Считать ход в фоновом потоке: кадр не замирает на время расчёта. " +
                 "Выключить — расчёт на главном потоке, как раньше (для отладки).")]
        [SerializeField] private bool asyncTurnCalculation = true;

        // Состояние мира и расчёт хода — в SimulationSession (слой симуляции). Здесь — тайминг
        // фаз, визуал, ввод и сценовые гиперпереходы.
        private SimulationSession _session;
        private static readonly Dictionary<string, GalaxyData> NoGalaxies = new();

        /// <summary>Все сгенерированные галактики (ключ = ключ в GalaxyConfig.Galaxies).
        /// Пусто до первой генерации/загрузки. Каждая галактика тикается каждый ход независимо.</summary>
        public Dictionary<string, GalaxyData> Galaxies => _session?.Galaxies ?? NoGalaxies;
        /// <summary>Ключ активной галактики — той, где сейчас игрок. Смена — через <see cref="SwitchActiveGalaxy"/>.</summary>
        public string ActiveGalaxyKey => _session?.ActiveGalaxyKey;
        /// <summary>Активная галактика — та, где сейчас игрок. Если галактик нет, вернёт null.</summary>
        public GalaxyData GeneratedGalaxy => _session?.ActiveGalaxy;

        /// <summary>Галактика, чей GalaxyNextDay сейчас исполняется (см. <see cref="SimulationSession.CurrentTickingGalaxy"/>).
        /// Вне тика равен null — подписчики должны падать обратно на GeneratedGalaxy.</summary>
        public GalaxyData CurrentTickingGalaxy => _session?.CurrentTickingGalaxy;
        public GalaxyGenerationContext Context => _context;
        public SimulationSession Session => _session;
        public GameSettingsConfig Settings => settings;
        public StarData CurrentStar { get; private set; }
        public TurnPhase Phase { get; private set; } = TurnPhase.Planning;
        public bool IsAutoMode => _planningRequests.Count == 0;
        public TurnAnimationData LastTurnData { get; private set; }
        /// <summary>Ход считается в фоновом потоке (см. <see cref="ExecuteTurnCalculation"/>).
        /// Пока true, главный поток не читает и не меняет мир: визуал и UI замирают на время расчёта.</summary>
        public bool IsCalculating => _calcTask != null;

        private GalaxyGenerationContext _context;
        private System.Threading.Tasks.Task<TurnAnimationData> _calcTask;
        private MainThreadDispatcher _dispatcher;
        private float _simulationTimer;
        private int _currentSubTurn;
        private readonly HashSet<PlanningReason> _planningRequests = new();
        private bool _simulationInterrupt;
        // Отложенный переход в новую систему (HyperEnter→HyperArrive) — обрабатывается на СЛЕДУЮЩЕМ кадре,
        // чтобы loading screen успел отрисоваться ДО тяжёлой работы RenderSystem.
        private bool _pendingArrivalTransition;

        private float SimulationDuration => settings != null ? settings.TurnDuration : 2.0f;

        private void OnDestroy()
        {
            if (Instance != this) return;
            WaitForTurnCalculation();
            if (MainThread.Dispatcher == _dispatcher) MainThread.Dispatcher = null;
            GameWorld.Detach(this);
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GameWorld.Attach(this);
            DontDestroyOnLoad(gameObject);

            // Расчёт хода идёт в фоновом потоке — Unity API/UI/визуал из него доступны только
            // через главный поток (SRG.Simulation.MainThread). Пути логов запоминаем сейчас:
            // Application.dataPath из фонового потока недоступен.
            _dispatcher = new MainThreadDispatcher();
            MainThread.Dispatcher = _dispatcher;
            SRG.Utils.RuntimePaths.Warmup();

            // Объекты симуляции (DirectiveManager, OwnerRaceRelationsManager, HighCommandRegistry)
            // и подписки сервисов создаёт SimulationSetup вместе с контекстом.
        }

        private void OnApplicationQuit()
        {
            WaitForTurnCalculation();
            // Гарантированный flush лог-буферов при выходе из приложения.
            EconomicLog.Flush();
            HighCommandLog.Flush();
            SRG.Galaxy.Politics.NewsLog.Flush();
            SRG.Utils.PerfLog.Flush();
        }

        private void Start()
        {
            EnsureContextInitialized();
            if (GeneratedGalaxy == null)
                GenerateNewGalaxy(GalaxySeed != 0 ? GalaxySeed : (int)DateTime.Now.Ticks);
        }

        private void Update()
        {
            // Пока ход считается в фоне — обслуживаем его запросы к главному потоку и больше
            // ничего не делаем: мир сейчас меняется.
            if (IsCalculating && !PollTurnCalculation())
            {
                HandleSpaceBarInput();
                return;
            }

            HandleSpaceBarInput();

            // Если на прошлом кадре мы показали loading и отложили тяжёлый scene swap —
            // выполняем его сейчас (loading уже отрисован Unity и скрывает фриз RenderSystem).
            if (_pendingArrivalTransition)
            {
                _pendingArrivalTransition = false;
                ProcessDeferredArrivalTransition();
                if (IsAutoMode) ExecuteTurnCalculation();
                return;
            }

            if (Phase == TurnPhase.Simulation) TickSimulation();
        }

        private void ProcessDeferredArrivalTransition()
        {
            var player = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (player == null || GeneratedGalaxy == null) return;
            if (player.HyperjumpPhase != HyperjumpPhase.HyperArrive) return;
            if (!GeneratedGalaxy.StarsMap.TryGetValue(player.CurrentStarUid, out var newStar)) return;

            GeneratedGalaxy.MigrateShipsBetweenStars();
            SetCurrentStar(newStar);
            CameraController.Instance?.SetPosition(player.HyperjumpArrivalEdge);
            ClearPlanning(PlanningReason.RouteCompleted);
            ClearPlanning(PlanningReason.PlayerInput);
        }

        private void HandleSpaceBarInput()
        {
            if (GameConsoleController.IsOpen) return;
            if (!Input.GetKeyDown(KeyCode.Space)) return;

            if (Phase == TurnPhase.Simulation || IsCalculating)
                RequestPlanning(PlanningReason.PlayerInput);
            else if (Phase == TurnPhase.Planning)
            {
                ClearAllPlanning();
                StartTurn();
            }
        }

        public void RequestPlanning(PlanningReason reason)
        {
            // Симуляция просит паузу и из расчёта хода (фоновый поток) — применяем после расчёта.
            if (!MainThread.IsCurrent) { MainThread.Post(() => RequestPlanning(reason)); return; }
            _planningRequests.Add(reason);
            UnityEngine.Debug.Log($"[GalaxyManager] Planning requested: {reason}. Total: {_planningRequests.Count}");
        }

        public void ClearPlanning(PlanningReason reason) => _planningRequests.Remove(reason);
        public void ClearAllPlanning() => _planningRequests.Clear();
        public void SignalRouteComplete() => _simulationInterrupt = true;

        public void StartTurn()
        {
            if (Phase == TurnPhase.Simulation || IsCalculating) return;
            ExecuteTurnCalculation();
        }

        /// <summary>
        /// Расчёт хода. По умолчанию — в фоновом потоке: главный продолжает рисовать кадры, а
        /// результат подхватывает <see cref="PollTurnCalculation"/>. Пока идёт расчёт, визуал и UI
        /// не читают мир (<see cref="GameWorld.IsCalculating"/>), ввод UI заблокирован, а обращения
        /// симуляции к Unity/UI идут через <see cref="MainThread"/>.
        /// </summary>
        private void ExecuteTurnCalculation()
        {
            if (GeneratedGalaxy == null || IsCalculating) return;
            if (!asyncTurnCalculation)
            {
                OnTurnCalculated(_session.SimulateDay());
                return;
            }

            var session = _session;
            SetUiInputBlocked(true);
            _calcTask = System.Threading.Tasks.Task.Run(() => session.SimulateDay());
        }

        /// <summary>Обслужить фоновый расчёт. true — расчёт завершён и обработан.</summary>
        private bool PollTurnCalculation()
        {
            _dispatcher.PumpSends();
            if (!_calcTask.IsCompleted) return false;

            var task = _calcTask;
            _calcTask = null;
            SetUiInputBlocked(false);
            // Отложенные уведомления расчёта (лог, новости, визуал новых кораблей, паузы) —
            // в том порядке, в каком симуляция их подняла, до событий хода.
            _dispatcher.DrainPosts();

            if (task.IsFaulted)
            {
                // Как и при расчёте на главном потоке: ошибка в лог, остаёмся в планировании.
                UnityEngine.Debug.LogException(task.Exception?.GetBaseException());
                RequestPlanning(PlanningReason.PlayerInput);
                return true;
            }
            OnTurnCalculated(task.Result);
            return true;
        }

        /// <summary>Дождаться фонового расчёта (выход из игры, уничтожение менеджера): обслуживает
        /// его запросы к главному потоку, иначе расчёт, ждущий главный поток, не завершится.</summary>
        private void WaitForTurnCalculation()
        {
            if (_calcTask == null) return;
            while (!_calcTask.IsCompleted)
            {
                _dispatcher.PumpSends();
                System.Threading.Thread.Sleep(1);
            }
            _dispatcher.PumpSends();
            _dispatcher.DrainPosts();
            _calcTask = null;
            SetUiInputBlocked(false);
        }

        // UI-клики во время расчёта изменили бы мир, который меняет поток расчёта.
        private UnityEngine.EventSystems.EventSystem _blockedEventSystem;
        private void SetUiInputBlocked(bool blocked)
        {
            if (blocked)
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && es.enabled) { es.enabled = false; _blockedEventSystem = es; }
            }
            else if (_blockedEventSystem != null)
            {
                _blockedEventSystem.enabled = true;
                _blockedEventSystem = null;
            }
        }

        private void OnTurnCalculated(TurnAnimationData data)
        {
            LastTurnData = data;
            PlayerManager.Instance?.CheckPauseConditions(LastTurnData);
            foreach (var reason in LastTurnData.PlanningReasons)
                RequestPlanning(reason);

            SystemViewManager.Instance?.BeginTurnAnimation(LastTurnData);
            GameWorld.RaiseTurnCalculate(LastTurnData);
            Phase = TurnPhase.Simulation;
            _simulationTimer = 0f;
            _currentSubTurn = 0;
        }

        private void TickSimulation()
        {
            _simulationTimer += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(_simulationTimer / SimulationDuration);
            int newSubTurn = Mathf.Clamp(
                Mathf.CeilToInt(progress * GalaxyData.SubTurnsPerTurn),
                1, GalaxyData.SubTurnsPerTurn);
            _currentSubTurn = newSubTurn;

            SystemViewManager.Instance?.AnimateSystem(progress, _currentSubTurn, LastTurnData);
            GameWorld.RaiseTurnAnimate(progress, _currentSubTurn);

            // Loading screen скрывается ровно на первом тике arrival-симуляции
            // (когда игрок уже в новой системе и фаза HyperArrive — этот ход показывает открытие портала).
            if (LoadingScreenController.IsVisible)
            {
                var pl = PlayerManager.Instance?.GetOrFindPlayerShip();
                if (pl != null && pl.HyperjumpPhase == HyperjumpPhase.HyperArrive)
                    LoadingScreenController.Hide();
            }

            if (_simulationTimer >= SimulationDuration) CompleteCurrentTurn();
        }

        private void CompleteCurrentTurn()
        {
            SystemViewManager.Instance?.UpdatePlanetPositions();
            GameWorld.RaiseTurnComplete(LastTurnData);

            if (_simulationInterrupt)
            {
                _simulationInterrupt = false;
                RequestPlanning(PlanningReason.RouteCompleted);
            }

            // Финализируем фазу гиперперехода ПОСЛЕ анимации (а не внутри StarNextDay) —
            // чтобы во время симуляции у кораблей была их «текущая» фаза, а не следующая.
            _session.FinalizeHyperjumpPhases();

            // Может выставить _pendingArrivalTransition (отложит scene swap на следующий кадр).
            HandleHyperjumpTransitions();

            if (_pendingArrivalTransition)
            {
                // Loading уже показан, scene swap и Tick(1f) выполнятся в следующем Update.
                Phase = TurnPhase.Planning;
                _simulationTimer = 0f;
                _currentSubTurn = 0;
                return;
            }

            // Толкаем PortalManager в случаях БЕЗ смены сцены, чтобы он подхватил новую фазу.
            SystemViewManager.Instance?.GetHyperPortals()?.Tick(1f);

            Phase = TurnPhase.Planning;
            _simulationTimer = 0f;
            _currentSubTurn = 0;

            if (IsAutoMode)
                ExecuteTurnCalculation();
            else
                UnityEngine.Debug.Log($"[GalaxyManager] Planning after turn {GeneratedGalaxy.CurrentTurn}. " +
                          $"Reasons: {string.Join(", ", _planningRequests)}");
        }

        /// <summary>
        /// Между ходами реагирует на смену системы игрока (HyperEnter → HyperArrive) — заменяет
        /// рендер сцены через loading screen — и не даёт планировочной паузе разорвать 3-ходовую анимацию.
        /// </summary>
        private void HandleHyperjumpTransitions()
        {
            var player = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (player == null) return;

            // HyperEnter только что закончился, игрок перешёл в HyperArrive и мигрировал.
            // Показываем loading СРАЗУ, а тяжёлую работу (migrate + RenderSystem + camera)
            // откладываем на следующий кадр — иначе Unity не успеет отрисовать loading и
            // игрок увидит фриз "до" появления экрана.
            if (player.HyperjumpPhase == HyperjumpPhase.HyperArrive
                && CurrentStar != null
                && !string.IsNullOrEmpty(player.CurrentStarUid)
                && player.CurrentStarUid != CurrentStar.Uid
                && GeneratedGalaxy != null
                && GeneratedGalaxy.StarsMap.TryGetValue(player.CurrentStarUid, out var newStar))
            {
                LoadingScreenController.Show($"Гиперпереход → {newStar.Name}");
                _pendingArrivalTransition = true;
                ClearPlanning(PlanningReason.RouteCompleted);
                ClearPlanning(PlanningReason.PlayerInput);
                return; // не запускаем следующую симуляцию сейчас — она пойдёт после deferred-перехода
            }

            // Прыжок в полёте — снимаем все автоматически выставленные планировочные паузы
            // (RouteCompleted при касании HyperjumpEdge и т.п.), чтобы 3 фазы шли подряд без Space.
            if (player.HyperjumpPhase == HyperjumpPhase.Travel
                || player.HyperjumpPhase == HyperjumpPhase.HyperEnter
                || player.HyperjumpPhase == HyperjumpPhase.HyperArrive
                || player.HyperjumpPhase == HyperjumpPhase.HyperExit)
            {
                ClearPlanning(PlanningReason.RouteCompleted);
            }

            // Прыжок ИГРОКА завершён (phase=None, но мы только что мигрировали).
            // Пауза, чтобы дать игроку осмотреться в новой системе и проложить курс.
            // ВАЖНО: после срабатывания «съедаем» PreviousStarUid, иначе условие будет матчить
            // на КАЖДОМ последующем ходу (игрок стоит без цели → TargetPosition≈Position) и каждый
            // раз будет ставиться PlayerInput-пауза без действий игрока.
            if (player.HyperjumpPhase == HyperjumpPhase.None
                && !string.IsNullOrEmpty(player.PreviousStarUid)
                && CurrentStar != null
                && player.PreviousStarUid != CurrentStar.Uid
                && (player.TargetPosition - player.Position).sqrMagnitude < 0.01f)
            {
                RequestPlanning(PlanningReason.PlayerInput);
                player.PreviousStarUid = CurrentStar.Uid;
            }
        }

        public void GenerateNewGalaxy(int seed)
        {
            WaitForTurnCalculation(); // мир сейчас заменится — результат недосчитанного хода не нужен
            EnsureContextInitialized();
            if (_context == null) return;

            GalaxySeed = seed;
            PlayerManager.Instance?.ClearPlayerShip();
            ShipGraphicsResolver.ClearCache();

            // Генерируем ВСЕ галактики, определённые в GalaxyConfig.Galaxies.
            if (!_session.GenerateAll(seed)) return;

            PreloadAllVisuals();

            var star = ResolveStartingStar();
            PreloadStarVisuals(star);
            PlayerManager.Instance?.SpawnPlayer(star, _context, settings);
            SetCurrentStar(star);
        }

        /// <summary>Переключает активную галактику (для UI/консольных команд). Не переносит игрока —
        /// игрок остаётся там, где он был. Для перемещения игрока используй <see cref="TeleportToStar"/>.</summary>
        public bool SwitchActiveGalaxy(string galaxyKey)
        {
            if (IsCalculating) return false;
            if (_session == null || !_session.SwitchActiveGalaxy(galaxyKey)) return false;
            UnityEngine.Debug.Log($"[GalaxyManager] Active galaxy: {galaxyKey}");
            return true;
        }

        /// <summary>Ключ галактики, где находится звезда, либо null. Используется командой teleport.</summary>
        public string FindGalaxyOfStar(string starUid) => _session?.FindGalaxyOfStar(starUid);

        // Прогрев графики — в Presentation (VisualPreloader); здесь только момент вызова.
        private void PreloadAllVisuals() => VisualPreloader.PreloadAll(GraphicsManager.Instance, _context, settings);

        private void PreloadStarVisuals(StarData startingStar)
            => VisualPreloader.PreloadStars(GraphicsManager.Instance, _context, GeneratedGalaxy, startingStar, this);

        public void SetCurrentStar(StarData star)
        {
            if (star == null) return;

            if (CurrentStar != null && CurrentStar != star && CurrentStar.Asteroids.Count > 0)
            {
                UnityEngine.Debug.Log($"[GalaxyManager] Clearing {CurrentStar.Asteroids.Count} asteroids from '{CurrentStar.Name}' on star switch");
                CurrentStar.Asteroids.Clear();
            }

            // Отмечаем звезду как посещённую (для галакарты)
            GeneratedGalaxy?.VisitedStarUids?.Add(star.Uid);

            CurrentStar = star;

            // Обновляем прямую ссылку CurrentStar на всех кораблях новой звезды
            var ships = star.Ships;
            int count = ships.Count;
            for (int i = 0; i < count; i++)
                ships[i].CurrentStar = star;

            SystemViewManager.Instance?.RenderSystem(star);
            UnityEngine.Debug.Log($"[GalaxyManager] Current star: {star.Name}");
        }

        /// <summary>
        /// Мгновенный перенос игрока в другую систему без расхода топлива (отладка / галакарта).
        /// Физически переносит корабль между star.Ships листами.
        /// </summary>
        public void TeleportToStar(StarData target)
        {
            if (target == null || GeneratedGalaxy == null || IsCalculating) return;
            var playerShip = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (playerShip == null) return;

            // Целевая звезда может быть в другой галактике — определяем и переключаемся.
            string targetGalaxyKey = FindGalaxyOfStar(target.Uid);
            if (!string.IsNullOrEmpty(targetGalaxyKey) && targetGalaxyKey != ActiveGalaxyKey)
            {
                CurrentStar?.Ships.Remove(playerShip);
                SwitchActiveGalaxy(targetGalaxyKey);
            }
            else
            {
                CurrentStar?.Ships.Remove(playerShip);
            }

            playerShip.Position = playerShip.PreviousPosition = playerShip.TargetPosition = Vector2.zero;
            playerShip.CurrentStarUid  = target.Uid;
            playerShip.PreviousStarUid = target.Uid;
            playerShip.CurrentStar     = target;
            target.Ships.Add(playerShip);
            SetCurrentStar(target);
        }

        /// <summary>
        /// Запрос многоходового гиперперехода игрока. Делегирует в HyperjumpController:
        /// корабль начинает фазу Travel (лететь к краю системы), топливо спишется при входе в портал.
        /// Возвращает false, если прыжок невозможен (нет топлива/двигателя/JumpRange/дистанция).
        /// </summary>
        public bool JumpToStar(StarData target)
        {
            if (target == null || GeneratedGalaxy == null || IsCalculating) return false;
            var playerShip = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (playerShip == null) return false;
            bool ok = HyperjumpController.RequestJump(playerShip, target, GeneratedGalaxy);
            if (ok)
            {
                PlayerShip.Instance?.ShowHyperjumpPath();

                // ── ТЕСТ-РЕЖИМ ────────────────────────────────────────────────────
                // Все остальные корабли стартовой системы тоже летят в ту же цель —
                // нужно для проверки NPC-прыжков. И задаём ActionHyperJumpTo как activity
                // чтобы в UI отображался корректный CurrentOrder ("HyperJumpTo:…").
                if (CurrentStar != null)
                {
                    int npcStarted = 0;
                    foreach (var npc in CurrentStar.Ships)
                    {
                        if (npc == null || npc.IsPlayer) continue;
                        if (npc.CurrentHull <= 0) continue;
                        if (HyperjumpController.RequestJump(npc, target, GeneratedGalaxy))
                        {
                            npc.Brain?.ForceActivity(new ActionHyperJumpTo(target.Uid));
                            npcStarted++;
                        }
                    }
                    if (npcStarted > 0)
                        UnityEngine.Debug.Log($"[GalaxyManager] Тест: {npcStarted} NPC отправлены в {target.Name}.");
                }
                // ──────────────────────────────────────────────────────────────────

                // Маршрут задан, но симуляцию НЕ запускаем — игрок сам нажмёт Space.
            }
            return ok;
        }

        /// <summary>Отмена активного гиперперехода игрока (если он в Travel или HyperEnter).</summary>
        public bool CancelHyperjump()
        {
            var playerShip = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (playerShip == null) return false;
            return HyperjumpController.CancelJump(playerShip);
        }

        /// <summary>Расстояние между двумя звёздами в единицах сетки.</summary>
        public float CalcDistanceParsecs(StarData a, StarData b)
        {
            if (a == null || b == null) return 0f;
            float dx = a.Position.x - b.Position.x;
            float dy = a.Position.y - b.Position.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public void SaveGame()
        {
            // Сохранение посреди фонового расчёта записало бы полуготовый мир.
            if (IsCalculating) { UnityEngine.Debug.LogWarning("[GalaxyManager] SaveGame во время расчёта хода — пропущено."); return; }
            HighCommandLog.Flush();
            GalaxySaveManager.SaveGalaxies(Galaxies, ActiveGalaxyKey);
        }

        public void LoadGame()
        {
            WaitForTurnCalculation(); // мир сейчас заменится — результат недосчитанного хода не нужен
            EnsureContextInitialized();
            if (_session == null) return;
            var loaded = GalaxySaveManager.LoadGalaxies(out string activeKey);
            if (!_session.LoadFrom(loaded, activeKey)) return;
            PlayerManager.Instance?.ClearPlayerShip();
            PreloadAllVisuals();
            var playerStar = PlayerManager.Instance?.FindPlayerStar();
            var startStar = playerStar ?? ResolveStartingStar();
            // Если игрок восстановлен в другой галактике — переключаемся туда.
            if (playerStar != null)
            {
                string playerGalaxyKey = FindGalaxyOfStar(playerStar.Uid);
                if (!string.IsNullOrEmpty(playerGalaxyKey) && playerGalaxyKey != ActiveGalaxyKey)
                    SwitchActiveGalaxy(playerGalaxyKey);
            }
            PreloadStarVisuals(startStar);
            SetCurrentStar(startStar);
            // SpawnSystem: регистрация политик и пересчёт счётчиков для загруженной галактики
            _session.RecountSpawns();
            UnityEngine.Debug.Log($"[GalaxyManager] Game loaded. Galaxies: {Galaxies.Count}, active: {ActiveGalaxyKey}");
        }

        public void StopAutoMode() => RequestPlanning(PlanningReason.PlayerInput);

        /// <summary>
        /// Мгновенный ход без анимации — используется пока игрок на планете.
        /// </summary>
        public void ExecuteInstantTurn()
        {
            if (Phase == TurnPhase.Simulation || IsCalculating || GeneratedGalaxy == null) return;
            ClearAllPlanning();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Тот же расчёт, что и в обычном ходе: миграции между звёздами применяются сразу
            // (раньше мгновенный ход их откладывал — корабли «висели» между системами, пока игрок
            // сидел на планете), фазы гиперпрыжка финализируются после событий хода.
            LastTurnData = _session.SimulateDay();
            sw.Stop();
            UnityEngine.Debug.Log($"[GalaxyManager] InstantTurn {GeneratedGalaxy.CurrentTurn}: {sw.ElapsedMilliseconds} ms");

            PlayerManager.Instance?.CheckPauseConditions(LastTurnData);
            foreach (var reason in LastTurnData.PlanningReasons)
                RequestPlanning(reason);

            // Оповещаем подписчиков, минуя анимационную фазу
            GameWorld.RaiseTurnCalculate(LastTurnData);
            SystemViewManager.Instance?.UpdatePlanetPositions();
            GameWorld.RaiseTurnComplete(LastTurnData);
            _session.FinalizeHyperjumpPhases();

            // Остаёмся в Planning — повторный запуск хода только по кнопке
            Phase = TurnPhase.Planning;
        }

        private StarData ResolveStartingStar()
        {
            if (GeneratedGalaxy == null) return null;
            // 1. Имя из GalaxyConfig.json (StartingStarName) — находим по имени среди звёзд активной галактики
            string cfgName = _context?.ActiveGalaxyConfig?.StartingStarName;
            if (!string.IsNullOrEmpty(cfgName))
            {
                foreach (var s in GeneratedGalaxy.StarsMap.Values)
                    if (s.Name == cfgName) return s;
                UnityEngine.Debug.LogWarning($"[GalaxyManager] StartingStarName '{cfgName}' not found — falling back.");
            }

            // 2. UID из инспектора (ручной override) — ищем по всем галактикам
            if (!string.IsNullOrEmpty(startingStarUid))
            {
                foreach (var g in Galaxies.Values)
                    if (g.StarsMap.TryGetValue(startingStarUid, out var byUid)) return byUid;
            }

            // 3. Первая звезда активной галактики
            return GeneratedGalaxy.StarsMap.Values.FirstOrDefault();
        }

        private void EnsureContextInitialized()
        {
            if (_context != null) return;

            _context = SimulationSetup.CreateContext(new ConfigSources
            {
                Galaxy   = galaxyConfigJson   ? galaxyConfigJson.text   : null,
                Texts    = textConfigJson     ? textConfigJson.text     : null,
                Premade  = premadeConfigJson  ? premadeConfigJson.text  : null,
                Items    = itemsConfigJson    ? itemsConfigJson.text    : null,
                Dialogs  = dialogsConfigJson  ? dialogsConfigJson.text  : null,
                Partners = partnersConfigJson ? partnersConfigJson.text : null,
            }, settings);
            if (_context == null)
            {
                UnityEngine.Debug.LogError("[GalaxyManager] Config initialization failed.");
                return;
            }

            _session = new SimulationSession(_context);
            GraphicsManager.Instance?.Init(_context);
        }
    }
}
