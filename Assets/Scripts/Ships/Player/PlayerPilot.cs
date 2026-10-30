using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.Simulation;
using SRG.Utils;

namespace SRG.Ships.Player
{
    /// <summary>
    /// Режимы следования за кораблём — циклически переключаются повторным кликом по той же цели.
    /// Board/Tow/LandOnShip доступны только при наличии оборудования/условий; цикл их пропускает.
    /// </summary>
    public enum PlayerFollowMode
    {
        PursueAtMaxRange    = 0, // лететь и держаться на дистанции самой дальней пушки, не стреляя
        AttackWithLongRange = 1, // та же дистанция + стрельба только из самой дальней пушки
        AttackWithAllGuns   = 2, // подойти на дистанцию самой ближней пушки и стрелять всеми
        FollowClose         = 3, // следовать вплотную (без стрельбы)
        Board               = 4, // подойти на радиус крюка и взять на абордаж
        Tow                 = 5, // подойти на радиус буксира и взять на буксир
        LandOnShip          = 6, // сесть на корабль-носитель (ShipTypeConfig.CanBeLandedOn)
    }

    /// <summary>
    /// То, что пилот просит от визуала: показать маршрут, отыграть посадку/взлёт, вывести
    /// сообщение. Реализуется <c>SRG.Controllers.PlayerShip</c>; в тестах может отсутствовать.
    /// </summary>
    public interface IPlayerPilotView
    {
        /// <summary>Запланированный маршрут: точки траектории от корабля, шаг одного дня и
        /// (для посадки) маркер конца на самой планете вместо точки упреждения.</summary>
        void DrawRoute(List<Vector2> chain, float dayStep, Vector2? endMarker);
        void ClearRoute();
        void OnLanded(PlanetData planet);
        void OnDockedOnShip();
        void OnTakeoff();
        /// <summary>Посадка сорвалась (цель исчезла) — вернуть кораблю видимость.</summary>
        void OnLandingCancelled();
        void ShowMessage(string text);
    }

    /// <summary>
    /// Управление кораблём игрока — команды и их доведение между ходами, без сцены:
    /// маршрут из точек (с отслеживанием движущейся цели), следование за кораблём в 7 режимах
    /// с автоогнём/абордажем/буксиром/стыковкой, посадка, червоточины, очередь захвата, ручной
    /// выстрел, форсаж и продолжение маршрута по итогам хода.
    ///
    /// Ввод (что под курсором, какие клавиши) и визуал живут в <c>PlayerShip</c>; он переводит
    /// клики в команды пилота и рисует то, что пилот сообщает через <see cref="IPlayerPilotView"/>.
    /// Мир — через <see cref="GameWorld"/>, поэтому пилот работает и в headless-тестах.
    /// </summary>
    public sealed class PlayerPilot
    {
        public ShipData Ship { get; }

        private readonly IPlayerPilotView _view;
        private readonly GameSettingsConfig _settings;

        private readonly Queue<Vector2> _targets = new Queue<Vector2>();
        // true при старте: маршрута ещё не было — иначе первый же ход без курса
        // ложно завершится «RouteCompleted» и прервёт симуляцию.
        private bool _routeCompleted = true;

        // Отслеживание движущейся цели
        private Func<Vector2> _trackedTargetFunc;
        private Vector2 _lastTrackedPos;
        private const float TrackUpdateThreshold = 0.01f;

        private const int FollowModeCount = 7;
        private string _followShipUid;
        private PlayerFollowMode _followMode;
        private const float FollowRangeFraction = 0.9f; // держимся чуть ближе требуемой дальности

        // Посадка: только для логики маршрута; визуальное состояние — у вида
        private PlanetData _landingPlanet;

        public PlayerPilot(ShipData ship, GameSettingsConfig settings = null, IPlayerPilotView view = null)
        {
            Ship = ship ?? throw new ArgumentNullException(nameof(ship));
            _settings = settings;
            _view = view;
            HullBeforeLastSimulation = ship.CurrentHull;
        }

        /// <summary>Активен ли режим следования за кораблём. Используется
        /// PlayerManager.CheckPauseConditions, чтобы менять правила выхода в planning:
        /// в автобое пауза запрашивается только при тяжёлых событиях.</summary>
        public bool IsFollowingShip => !string.IsNullOrEmpty(_followShipUid);
        public string FollowShipUid => _followShipUid;
        public PlayerFollowMode CurrentFollowMode => _followMode;

        /// <summary>Снимок CurrentHull игрока на момент начала текущего хода (до боевой симуляции).
        /// Используется для подсчёта урона за ход.</summary>
        public int HullBeforeLastSimulation { get; private set; }

        /// <summary>Точки маршрута, ещё не пройденные (для тестов и отладки).</summary>
        public IReadOnlyCollection<Vector2> PlannedTargets => _targets;

        // ── Ход ────────────────────────────────────────────────────────────────

        /// <summary>Между ходами (каждый кадр планирования): догнать движущуюся цель и взвести автоогонь.</summary>
        public void UpdateBetweenTurns()
        {
            UpdateTrackedTarget();
            UpdateFollowAutoFire();
        }

        /// <summary>Ход посчитан, анимация впереди.</summary>
        public void OnTurnCalculated()
        {
            // ProcessPlayerManualShot уже отработал и обнулил ManualShoot* поля — взводим их заново для
            // следующего хода, чтобы в авто-режиме (когда между ходами Update не успевает выполниться)
            // огонь по follow-цели не прерывался.
            UpdateFollowAutoFire();

            // Фиксируем HP для следующего хода: на момент CheckPauseConditions следующего тика
            // это будет «HP до боевой симуляции» — нужно для подсчёта урона за ход в автобое.
            HullBeforeLastSimulation = Ship.CurrentHull;
        }

        /// <summary>Анимация хода закончилась: довести маршрут — посадка, следующая точка,
        /// подлёт к планете, следование или конец маршрута с паузой.</summary>
        public void OnTurnCompleted()
        {
            // Истекшие модификаторы скилов (болезни/стимы/читы) удаляются и фаерится OnChanged.
            var galaxy = GameWorld.GeneratedGalaxy;
            if (galaxy != null) Ship.Skills?.TickTurn(galaxy.CurrentTurn);

            FinalizeHeadingFromAnimation();

            if (_routeCompleted) return;

            float sqDist = (Ship.TargetPosition - Ship.Position).sqrMagnitude;
            bool targetReached = sqDist <= 0.01f;

            _targets.Clear();
            if (!targetReached)
                _targets.Enqueue(Ship.TargetPosition);
            foreach (var wp in Ship.TargetQueue)
                _targets.Enqueue(wp);

            Ship.TargetQueue.Clear();

            Debug.Log($"[PlayerPilot] OnTurnCompleted: pos={Ship.Position:F2} targetReached={targetReached} remaining={_targets.Count}");

            // Двухфазная посадка: Phase 1 (Fading) выставлена в PrepareForTurn.
            if (TryFinalizeLanding()) return;

            if (_targets.Count > 0)
            {
                ContinueRouteFromTargets();
            }
            else
            {
                // Маршрут исчерпан — три варианта продолжения:
                if (ContinueLandingApproach()) return;
                if (ContinueFollow()) return;
                FinishRouteWithPause();
            }
        }

        /// <summary>По последнему кадру анимации обновляет CurrentHeading корабля (чтобы корабль
        /// смотрел в сторону движения на момент конца хода).</summary>
        private void FinalizeHeadingFromAnimation()
        {
            var lastAnim = GameWorld.LastTurnData;
            if (lastAnim == null || !lastAnim.ShipFrames.TryGetValue(Ship.Uid, out var doneFrames)) return;
            var dv = doneFrames.SubTurns[GalaxyData.SubTurnsPerTurn] - doneFrames.SubTurns[GalaxyData.SubTurnsPerTurn - 1];
            if (dv.sqrMagnitude > 0.0001f)
                Ship.CurrentHeading = Angles.Of(dv);
        }

        /// <summary>Если в этом ходу был выставлен LandingPhase=Fading — финализирует посадку
        /// на любую <see cref="ILandingSite"/>: планета/станция/корабль-носитель.
        /// Возвращает true, если посадка завершена и дальнейшая обработка хода не нужна.</summary>
        private bool TryFinalizeLanding()
        {
            if (Ship.LandingPhase != LandingPhase.Fading) return false;
            var site = LandingSiteRegistry.ResolveActiveTarget(Ship, GameWorld.CurrentStar);

            // Цель исчезла (планета/носитель уничтожены/ушли) или носитель стал недопустим —
            // отменяем посадку и возвращаем видимость (EndTurn мог обнулить alpha по Fading).
            if (site == null
                || (site is ShipData carrier && !ShipDockingService.CanLandOn(Ship, carrier, out _)))
            {
                ClearLandingIntent();
                _view?.OnLandingCancelled();
                return false;
            }
            return FinalizeLanding(site);
        }

        /// <summary>Общая финализация посадки. Планетный и корабельный варианты различаются только
        /// post-hook'ами (VisitedPlanetUids для планеты; ShipDockingService.Land для носителя).</summary>
        private bool FinalizeLanding(ILandingSite site)
        {
            // Общая часть: стоп-курс, чистка очередей и маршрута, финализация route-state.
            Ship.TargetPosition = Ship.Position;
            Ship.TargetQueue.Clear();
            _targets.Clear();
            _view?.ClearRoute();
            _routeCompleted    = true;
            _trackedTargetFunc = null;
            ClearLandingIntent();

            if (site is PlanetData planet)
            {
                Ship.LandedPlanetUid = planet.Uid;
                _landingPlanet       = planet;
                GameWorld.GeneratedGalaxy?.VisitedPlanetUids?.Add(planet.Uid);
                _view?.OnLanded(planet);
                GameWorld.Player?.LandOn(planet);
                return true;
            }

            if (site is ShipData carrier)
            {
                // Единый путь для станций и обычных носителей: Land (LandedOnShipUid + freeze),
                // визуальное скрытие, открытие UI. Отстыковка — через Takeoff → UndockFromCarrier.
                ShipDockingService.Land(Ship, carrier);
                ClearFollow();
                _view?.OnDockedOnShip();
                GameWorld.Player?.DockOnShip(carrier);

                string tag = carrier.IsStation ? "Станция" : "Стыковка";
                GameLog.Add($"[{tag}] {(carrier.IsStation ? "Стыковка со станцией" : "Посадка на")} {carrier.Name}.");
                _view?.ShowMessage($"{tag}: {carrier.Name}");
                return true;
            }
            return false;
        }

        /// <summary>Общий сброс «желания сесть»: обнуление обоих UID и фазы + локального
        /// _landingPlanet. Используется и при успешной посадке, и при откате (носитель ушёл).</summary>
        private void ClearLandingIntent()
        {
            _landingPlanet = null;
            Ship.LandingPlanetUid  = null;
            Ship.LandingCarrierUid = null;
            Ship.LandingPhase      = LandingPhase.None;
        }

        /// <summary>В _targets есть точки — применяем их к маршруту корабля (с tracked-обновлением).</summary>
        private void ContinueRouteFromTargets()
        {
            if (_trackedTargetFunc != null)
            {
                Vector2 newPos = _trackedTargetFunc();
                ReplaceFirstTarget(newPos);
                _lastTrackedPos = newPos;
            }
            ApplyTargetsToShipData();
            RedrawPath();
        }

        /// <summary>Маршрут исчерпан, но есть LandingPlanetUid (хвост хода НЕ попал в R_land):
        /// добиваем подлёт — следующий ход будет либо Approach, либо Fading.</summary>
        private bool ContinueLandingApproach()
        {
            if (Ship.LandingPlanetUid == null) return false;
            var planet = GameWorld.CurrentStar?.FindPlanet(Ship.LandingPlanetUid);
            if (planet != null)
            {
                Vector2 aim = PredictLandingTarget(planet);
                _targets.Clear();
                _targets.Enqueue(aim);
                _trackedTargetFunc = () => PredictLandingTarget(planet);
                _lastTrackedPos = aim;
                ApplyTargetsToShipData();
                RedrawPath();
                return true;
            }
            ClearLandingIntent();
            return false;
        }

        /// <summary>Режим следования — «маршрут завершён» здесь обычно ложный (точка следования
        /// совпала с позицией, но сама цель движется). Перезаряжаем путь свежей точкой.</summary>
        private bool ContinueFollow()
        {
            if (!IsFollowingShip) return false;
            var followTarget = FindFollowTarget();
            if (followTarget == null) return false;     // цель пропала — UpdateFollowAutoFire очистит follow
            Vector2 p = ComputeFollowPoint(followTarget);
            _targets.Clear();
            _targets.Enqueue(p);
            _lastTrackedPos = p;
            _routeCompleted = false;
            ApplyTargetsToShipData();
            RedrawPath();
            return true;
        }

        /// <summary>Стандартное окончание маршрута: остановка + запрос планировочной паузы
        /// (если SimulationInterruptOnActionDone == true).</summary>
        private void FinishRouteWithPause()
        {
            Ship.TargetPosition = Ship.Position;
            Ship.TargetQueue.Clear();
            _view?.ClearRoute();
            _routeCompleted = true;
            _trackedTargetFunc = null;

            bool simulationInterruptOnRoute = _settings == null || _settings.SimulationInterruptOnActionDone;
            Debug.Log($"[PlayerPilot] Route complete. SimulationInterrupt={simulationInterruptOnRoute}");
            if (simulationInterruptOnRoute)
                GameWorld.RequestPlanning(PlanningReason.RouteCompleted);
        }

        private void UpdateTrackedTarget()
        {
            if (_trackedTargetFunc == null || _targets.Count == 0) return;
            Vector2 newPos = _trackedTargetFunc();
            if ((newPos - _lastTrackedPos).sqrMagnitude < TrackUpdateThreshold * TrackUpdateThreshold) return;

            _lastTrackedPos = newPos;
            ReplaceFirstTarget(newPos);
            ApplyTargetsToShipData();
            RedrawPath();
        }

        private void ReplaceFirstTarget(Vector2 pos)
        {
            var all = new List<Vector2>(_targets);
            all[0] = pos;
            _targets.Clear();
            foreach (var p in all) _targets.Enqueue(p);
        }

        /// <summary>Переносит точки маршрута в ShipData: первая — TargetPosition с траекторией,
        /// остальные — TargetQueue. Подбор предметов идёт «на лету» (CargoGrabber), маршрут
        /// у предметов не останавливается.</summary>
        private void ApplyTargetsToShipData()
        {
            if (_targets.Count == 0) return;
            var effective = new List<Vector2>(_targets);

            Ship.TargetPosition = effective[0];
            Ship.TargetQueue.Clear();
            Ship.WaypointIndex = 0;
            Ship.Waypoints = ShipTrajectory.BuildPath(Ship, Ship.TargetPosition, GameWorld.CurrentStar);

            for (int i = 1; i < effective.Count; i++)
                Ship.TargetQueue.Enqueue(effective[i]);
        }

        /// <summary>Пересчитать показ запланированного маршрута — например, после активации
        /// форсажа, когда меняется скорость или радиус разворота.</summary>
        public void RefreshPlannedRoute()
        {
            if (_targets.Count == 0 && _landingPlanet == null) return;
            RedrawPath();
        }

        private void RedrawPath()
        {
            if (_view == null) return;
            if (_targets.Count == 0) { _view.ClearRoute(); return; }

            var star = GameWorld.CurrentStar;
            Vector2 prevPos = Ship.Position;
            float heading = Ship.CurrentHeading;
            var chain = new List<Vector2> { prevPos };

            float speed = ShipTrajectory.EffectiveSpeedPerTurn(Ship);
            float turnRad = ShipTrajectory.EffectiveTurnRadPerTurn(Ship);

            foreach (var target in _targets)
            {
                var waypoints = ShipTrajectory.BuildPath(prevPos, target, heading, speed, turnRad, star);
                if (waypoints.Count == 0) continue;
                chain.AddRange(waypoints);

                if (waypoints.Count >= 2)
                    heading = Mathf.Atan2(
                        waypoints[waypoints.Count - 1].y - waypoints[waypoints.Count - 2].y,
                        waypoints[waypoints.Count - 1].x - waypoints[waypoints.Count - 2].x);
                prevPos = target;
            }

            float step = SRUnits.ToWorld(Ship.ActualSpeed);

            // При посадке конечный маркер визуально размещаем на самой планете,
            // а не в точке упреждения, куда строится фактический маршрут перехвата.
            Vector2? endMarker = null;
            if (_landingPlanet != null)
                endMarker = OrbitMath.GetPlanetWorldPosition(_landingPlanet);

            _view.DrawRoute(chain, step, endMarker);
        }

        // ── Маршрут ────────────────────────────────────────────────────────────

        /// <summary>Есть ли распланированный маршрут / follow / посадка / гипер.</summary>
        public bool HasPlannedRoute()
        {
            if (_targets.Count > 0) return true;
            if (_landingPlanet != null) return true;
            if (!string.IsNullOrEmpty(_followShipUid)) return true;
            return Ship.HyperjumpPhase == HyperjumpPhase.Travel
                || Ship.HyperjumpPhase == HyperjumpPhase.CancelEnd;
        }

        /// <summary>Курс в точку. <paramref name="append"/> — добавить в конец маршрута, иначе заменить.
        /// Любая не-посадочная команда отменяет курс на посадку и следование не трогает
        /// (снимать его — забота вызывающего, см. <see cref="FlyTo"/>).</summary>
        public void SetWaypoint(Vector2 pos, bool append)
        {
            HyperjumpController.CancelOnRouteChange(Ship);

            if (!append) _targets.Clear();
            _targets.Enqueue(pos);

            // Иначе OnTurnCompleted переназначит маршрут обратно на планету.
            ClearLandingIntent();

            _routeCompleted = false;
            ApplyTargetsToShipData();
            RedrawPath();
        }

        /// <summary>Клик в пустое место: выйти из следования и лететь в точку.</summary>
        public void FlyTo(Vector2 pos, bool append)
        {
            ClearFollow();
            _trackedTargetFunc = null;
            SetWaypoint(pos, append);
        }

        /// <summary>Курс на астероид с отслеживанием его движения.</summary>
        public void FlyToAsteroid(AsteroidData asteroid, bool append)
        {
            if (asteroid == null || asteroid.IsDestroyed) return;
            ClearFollow();
            SetWaypoint(asteroid.Position, append);
            _trackedTargetFunc = () => asteroid.Position;
            _lastTrackedPos = asteroid.Position;
            GameLog.Add($"[Навигация] Курс на астероид {SpriteUtility.ShortId(asteroid.Uid)}.");
        }

        /// <summary>Полный сброс пользовательского маршрута: follow, посадка, очередь целей,
        /// промежуточные waypoints в ShipData, запланированный гиперпрыжок. После вызова
        /// корабль на следующей симуляции остаётся на месте.</summary>
        public void ClearRoute()
        {
            ClearFollow();
            _trackedTargetFunc = null;
            _landingPlanet = null;
            _targets.Clear();
            _routeCompleted = true;

            ClearLandingIntent();
            Ship.FreezeRoute();
            HyperjumpController.CancelOnRouteChange(Ship);

            _view?.ClearRoute();
        }

        /// <summary>Курс на посадку на планету (с упреждением её движения).</summary>
        public void SetLandingCourse(PlanetData planet)
        {
            // LandingBlocked — full-оккупация или скриптовая блокировка. Игрок допускается только если
            // он член стороны-контроллёра планеты (обычно нет — блокирует посадку).
            if (planet.Settlement.LandingBlocked)
            {
                string ctrl = OccupationService.GetControllingOwner(planet);
                if (Ship.Owner != ctrl)
                {
                    GameLog.Add($"[Навигация] Посадка запрещена: {planet.Name} закрыта для внешних кораблей.");
                    return;
                }
            }
            HyperjumpController.CancelOnRouteChange(Ship);
            ClearFollow();
            _landingPlanet = planet;
            Ship.LandingPlanetUid = planet.Uid;

            Vector2 planetPos = PredictLandingTarget(planet);
            _trackedTargetFunc = () => PredictLandingTarget(planet);
            _lastTrackedPos = planetPos;

            _targets.Clear();
            _targets.Enqueue(planetPos);
            _routeCompleted = false;

            ApplyTargetsToShipData();
            RedrawPath();
            GameLog.Add($"[Навигация] Курс на посадку: {planet.Name}.");
        }

        // Упреждение позиции планеты и точка входа в посадочное кольцо — см. PlanetGeometry.
        private Vector2 PredictLandingTarget(PlanetData planet)
            => PlanetGeometry.PredictPlayerLandingTarget(Ship, planet);

        /// <summary>
        /// Прыжок через червоточину: <see cref="HyperjumpController.RequestJumpViaWormhole"/> переводит
        /// корабль в фазу Travel с HyperjumpEdge = позиция червоточины; дальше — как обычный гиперпрыжок.
        /// </summary>
        public void SetWormholeCourse(WormholeData wormhole, StarData sourceStar)
        {
            if (wormhole == null || sourceStar == null) return;
            var galaxy = GameWorld.GeneratedGalaxy;
            if (galaxy == null) return;

            if (wormhole.Phase != WormholePhase.Open)
            {
                GameLog.Add("[Червоточина] Не пройти — червоточина ещё не стабилизировалась.");
                return;
            }

            if (!HyperjumpController.RequestJumpViaWormhole(Ship, wormhole, sourceStar, galaxy))
            {
                GameLog.Add("[Червоточина] Не удалось войти (проверьте состояние корабля).");
                return;
            }

            // Обычный маршрут отменяется — визуально мы летим к точке червоточины через ShowHyperjumpPath.
            ClearFollow();
            ClearLandingIntent();
            ShowHyperjumpPath();

            string targetName = galaxy.StarsMap.TryGetValue(wormhole.TargetStarUid, out var target)
                ? target.Name : "?";
            GameLog.Add($"[Червоточина] Курс на червоточину → {targetName}.");
        }

        /// <summary>
        /// Показывает траекторию к HyperjumpEdge во время фазы Travel — как обычный маршрут, но БЕЗ
        /// CancelOnRouteChange: это отображение уже запрошенного гиперпрыжка, а не новый курс.
        /// </summary>
        public void ShowHyperjumpPath()
        {
            if (Ship.HyperjumpPhase != HyperjumpPhase.Travel) return;
            _targets.Clear();
            _targets.Enqueue(Ship.HyperjumpEdge);
            _routeCompleted = false;
            ClearLandingIntent();
            ApplyTargetsToShipData();
            RedrawPath();
        }

        // ── Взлёт ──────────────────────────────────────────────────────────────

        /// <summary>Взлёт с текущей посадочной цели: с носителя — через Undock (позиция берётся с
        /// носителя), иначе — обычный планетный взлёт.</summary>
        public void Takeoff()
        {
            if (!string.IsNullOrEmpty(Ship.LandedOnShipUid))
            {
                UndockFromCarrier();
                return;
            }
            BeginTakeoffRoute();
        }

        /// <summary>Взлёт с корабля-носителя: позиция = позиция носителя, курс вперёд.</summary>
        public void UndockFromCarrier()
        {
            var carrier = GameWorld.CurrentStar?.FindShip(Ship.LandedOnShipUid);
            ShipDockingService.Undock(Ship, carrier);
            BeginTakeoffRoute();
            GameLog.Add($"[Стыковка] Взлёт с {carrier?.Name ?? "носителя"}.");
        }

        /// <summary>Общая часть взлёта: сброс follow/tracked, визуальный взлёт, вейпоинт по
        /// текущему курсу на 1.5 ед. вперёд. Landed*Uid снимает вызывающий.</summary>
        private void BeginTakeoffRoute()
        {
            ClearFollow();
            _trackedTargetFunc = null;
            _landingPlanet = null;
            _view?.OnTakeoff();

            float heading = Ship.CurrentHeading;
            var dir = float.IsNaN(heading) ? Vector2.right : Angles.Dir(heading);
            var target = Ship.Position + dir * 1.5f;

            _targets.Clear();
            _targets.Enqueue(target);
            _routeCompleted = false;
            ApplyTargetsToShipData();
            RedrawPath();
        }

        // ── Следование ─────────────────────────────────────────────────────────

        /// <summary>Клик по кораблю: начать следование или, если уже следуем за ним, перейти
        /// к следующему доступному режиму.</summary>
        public void FollowOrCycle(ShipData targetShip)
        {
            PlayerFollowMode mode;
            if (_followShipUid == targetShip.Uid)
            {
                int next = (int)_followMode;
                for (int i = 0; i < FollowModeCount; i++)
                {
                    next = (next + 1) % FollowModeCount;
                    if (IsFollowModeAvailable((PlayerFollowMode)next, targetShip)) break;
                }
                mode = (PlayerFollowMode)next;
            }
            else
            {
                mode = PlayerFollowMode.PursueAtMaxRange;
            }
            ApplyFollow(targetShip, mode);
        }

        /// <summary>Установить конкретный режим следования (без цикла). false — режим недоступен.</summary>
        public bool SetFollow(ShipData target, PlayerFollowMode mode)
        {
            if (target == null || target == Ship) return false;
            if (!IsFollowModeAvailable(mode, target)) return false;
            ApplyFollow(target, mode);
            return true;
        }

        /// <summary>Доступен ли режим следования для цели с текущим оборудованием.</summary>
        public bool CanUseFollowMode(ShipData target, PlayerFollowMode mode)
            => target != null && target != Ship && IsFollowModeAvailable(mode, target);

        private void ApplyFollow(ShipData targetShip, PlayerFollowMode mode)
        {
            HyperjumpController.CancelOnRouteChange(Ship);
            _followShipUid = targetShip.Uid;
            _followMode = mode;

            Vector2 dest = ComputeFollowPoint(targetShip);
            _trackedTargetFunc = () =>
            {
                var t = FindFollowTarget();
                return t != null ? ComputeFollowPoint(t) : Ship.Position;
            };
            _lastTrackedPos = dest;

            // Заменяем весь маршрут одной точкой следования (без «добавления» —
            // повторный клик по той же цели переключает режим, а не добавляет вейпоинт).
            ClearLandingIntent();
            if (mode == PlayerFollowMode.LandOnShip)
                Ship.LandingCarrierUid = targetShip.Uid;
            _routeCompleted = false;
            _targets.Clear();
            _targets.Enqueue(dest);
            ApplyTargetsToShipData();
            RedrawPath();

            GameLog.Add($"[Следование] {targetShip.Name}: {GetFollowModeName(_followMode)}.");
        }

        /// <summary>Выйти из следования (маршрут не трогается).</summary>
        public void ClearFollow()
        {
            bool wasAutoFiring = Ship.ManualShootIsAutoFollow;
            _followShipUid = null;
            _followMode = PlayerFollowMode.PursueAtMaxRange;
            Ship.ManualShootOnlyLongestRange = false;
            Ship.ManualShootIsAutoFollow = false;
            // ManualShootTargetUid сбрасываем только если он был выставлен авто-режимом,
            // чтобы не затирать активный ручной выстрел.
            if (wasAutoFiring) Ship.ManualShootTargetUid = null;
            // Курс на стыковку — часть follow-цели; выход из follow снимает его.
            Ship.LandingCarrierUid = null;
        }

        private ShipData FindFollowTarget()
        {
            if (string.IsNullOrEmpty(_followShipUid)) return null;
            var star = GameWorld.CurrentStar;
            if (star == null) return null;
            var ships = star.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (s.Uid == _followShipUid)
                    return s.CurrentHull > 0 ? s : null;
            }
            return null;
        }

        public string GetFollowTargetName() => FindFollowTarget()?.Name;

        /// <summary>Точка, в которую лететь в текущем режиме следования за целью.</summary>
        public Vector2 ComputeFollowPoint(ShipData target)
        {
            if (target == null || target.CurrentHull <= 0) return Ship.Position;

            // Минимально допустимая дистанция: преследователь никогда не должен сливаться
            // с целью — иначе корабли накладываются и игрок «стоит на» цели.
            float minStop = Mathf.Max(Ship.SpriteWorldSize, target.SpriteWorldSize) * 1.5f;

            float desired = _followMode switch
            {
                PlayerFollowMode.PursueAtMaxRange    => GetMaxWeaponRange() * FollowRangeFraction,
                PlayerFollowMode.AttackWithLongRange => GetMaxWeaponRange() * FollowRangeFraction,
                PlayerFollowMode.AttackWithAllGuns   => GetMinWorkingWeaponRange() * FollowRangeFraction,
                PlayerFollowMode.FollowClose         => minStop,
                PlayerFollowMode.Board               => GetBoardingApproachDistance(),
                PlayerFollowMode.Tow                 => GetTowingApproachDistance(),
                PlayerFollowMode.LandOnShip          => 0f, // летим в центр носителя — стыковка по радиусу
                _ => minStop
            };
            // LandOnShip — единственный режим, которому разрешено «слиться» с целью.
            if (_followMode != PlayerFollowMode.LandOnShip && desired < minStop) desired = minStop;

            Vector2 toTarget = target.Position - Ship.Position;
            float dist = toTarget.magnitude;
            Vector2 dir;
            if (dist > 0.0001f)
                dir = toTarget / dist;
            else
            {
                // Игрок прямо на цели — отступаем назад по её курсу.
                float h = target.CurrentHeading;
                dir = float.IsNaN(h)
                    ? Vector2.right
                    : new Vector2(-Mathf.Cos(h), -Mathf.Sin(h));
            }
            return target.Position - dir * desired;
        }

        public static string GetFollowModeName(PlayerFollowMode mode) => mode switch
        {
            PlayerFollowMode.PursueAtMaxRange    => "преследовать на дальней дистанции",
            PlayerFollowMode.AttackWithLongRange => "атаковать дальней пушкой",
            PlayerFollowMode.AttackWithAllGuns   => "атаковать всеми пушками",
            PlayerFollowMode.FollowClose         => "следовать вплотную",
            PlayerFollowMode.Board               => "взять на абордаж",
            PlayerFollowMode.Tow                 => "взять на буксир",
            PlayerFollowMode.LandOnShip          => "сесть на корабль",
            _ => mode.ToString()
        };

        /// <summary>
        /// Доступность режима: Board требует абордажный крюк и не работает по предметам и станциям;
        /// Tow требует буксировочную установку; LandOnShip — флаг CanBeLandedOn у типа цели
        /// и не-враждебность. Остальные доступны всегда.
        /// </summary>
        private bool IsFollowModeAvailable(PlayerFollowMode mode, ShipData target)
        {
            switch (mode)
            {
                case PlayerFollowMode.Board:
                    return target != null && !target.IsItem && !target.IsStation
                        && BoardingSystem.FindGrapplingHook(Ship) != null;
                case PlayerFollowMode.Tow:
                    // Станции — только специальной установкой (CanTowStations в ItemsConfig).
                    var rig = TowSystem.FindTowingRig(Ship);
                    if (target == null || rig == null) return false;
                    if (target.IsStation && !TowSystem.CanRigTowStations(rig)) return false;
                    return true;
                case PlayerFollowMode.LandOnShip:
                    return target != null && !target.IsItem
                        && ShipDockingService.CanLandOn(Ship, target, out _);
                default: return true;
            }
        }

        private float GetBoardingApproachDistance()
        {
            var hook = BoardingSystem.FindGrapplingHook(Ship);
            if (hook == null) return 0f;
            // Подходим чуть ближе радиуса крюка — гарантируем, что цель внутри.
            return SRUnits.ToWorld(hook.GetParam("PullRadius", 0f)) * FollowRangeFraction;
        }

        private float GetTowingApproachDistance()
        {
            var rig = TowSystem.FindTowingRig(Ship);
            if (rig == null) return 0f;
            return SRUnits.ToWorld(rig.GetParam("PullRadius", 0f)) * FollowRangeFraction;
        }

        private void UpdateFollowAutoFire()
        {
            // Мёртвый игрок не должен «довзводить» ManualShoot* поля — иначе в режиме
            // наблюдения авто-follow каждый ход переустанавливает цель выстрела.
            if (Ship.CurrentHull <= 0) return;
            if (string.IsNullOrEmpty(_followShipUid)) return;

            // Цель умерла или ушла — выходим из режима follow (для всех режимов, не только атакующих).
            var target = FindFollowTarget();
            if (target == null)
            {
                GameLog.Add("[Следование] Цель утрачена.");
                ClearFollow();
                return;
            }

            bool isAttackMode = _followMode == PlayerFollowMode.AttackWithLongRange
                             || _followMode == PlayerFollowMode.AttackWithAllGuns;

            // Снимаем «висящий» авто-выстрел, если предыдущий цикл оставил ManualShoot*
            // от Attack-режима, а текущий режим уже не стреляющий (Pursue/FollowClose/Board/Tow).
            if (!isAttackMode && Ship.ManualShootIsAutoFollow)
            {
                Ship.ManualShootTargetUid = null;
                Ship.ManualShootOnlyLongestRange = false;
                Ship.ManualShootIsAutoFollow = false;
            }

            // Авто-исполнение абордажа/буксира при попадании цели в радиус соответствующего оборудования.
            if (_followMode == PlayerFollowMode.Board || _followMode == PlayerFollowMode.Tow)
            {
                TryAutoBoardOrTow(target);
                return;
            }

            // Стыковка: сама посадка — двухфазный пайплайн (PrepareForTurn ставит Fading, а
            // TryFinalizeLanding переносит LandingCarrierUid → LandedOnShipUid). Здесь только
            // сторожевая проверка на потерю цели/враждебность.
            if (_followMode == PlayerFollowMode.LandOnShip)
            {
                if (!ShipDockingService.CanLandOn(Ship, target, out string reason))
                {
                    GameLog.Add($"[Стыковка] Посадка невозможна: {reason}.");
                    ClearFollow();
                }
                return;
            }

            if (!isAttackMode) return;

            Ship.ManualShootTargetUid = _followShipUid;
            Ship.ManualShootTargetIsAsteroid = false;
            Ship.ManualShootOnlyLongestRange = (_followMode == PlayerFollowMode.AttackWithLongRange);
            Ship.ManualShootIsAutoFollow = true;
        }

        private void TryAutoBoardOrTow(ShipData target)
        {
            var equipCfg = GameWorld.Context?.ItemsConfig;
            if (equipCfg == null) return;
            if (_followMode == PlayerFollowMode.Board)
            {
                if (BoardingSystem.CanBoard(Ship, target, equipCfg, out _)
                    && BoardingSystem.TryBoard(Ship, target, equipCfg))
                {
                    GameLog.Add($"[Абордаж] {target.Name} взят на абордаж.");
                    ClearFollow();
                }
            }
            else if (_followMode == PlayerFollowMode.Tow)
            {
                if (TowSystem.CanTow(Ship, target, equipCfg, out _)
                    && TowSystem.TryTow(Ship, target, equipCfg, BuildShipLookup()))
                {
                    if (target.TowedByUid == Ship.Uid)
                        GameLog.Add($"[Буксир] {target.Name} прицеплен.");
                    else if (target.PulledByUid == Ship.Uid)
                        GameLog.Add($"[Буксир] {target.Name} притягивается…");
                    ClearFollow();
                }
            }
        }

        private static Dictionary<string, ShipData> BuildShipLookup()
        {
            var star = GameWorld.CurrentStar;
            if (star?.Ships == null) return null;
            var dict = new Dictionary<string, ShipData>(star.Ships.Count);
            foreach (var s in star.Ships) dict[s.Uid] = s;
            return dict;
        }

        // ── Оружие ─────────────────────────────────────────────────────────────

        /// <summary>Ручной выстрел в следующем ходу по кораблю или астероиду в радиусе самой дальней
        /// исправной пушки. Выходит из следования. false — нет оружия или цель вне радиуса (в лог).</summary>
        public bool SelectShotTarget(ShipData ship, AsteroidData asteroid)
        {
            bool isShip     = ship != null && !ship.IsPlayer;
            bool isAsteroid = !isShip && asteroid != null && !asteroid.IsDestroyed;
            if (!isShip && !isAsteroid) return false;

            float maxRange = GetMaxWeaponRange();
            if (maxRange <= 0f)
            {
                GameLog.Add("[Оружие] На корабле нет исправного оружия.");
                return false;
            }

            Vector2 targetPos = isShip ? ship.Position : asteroid.Position;
            float dist = (targetPos - Ship.Position).magnitude;
            if (dist > maxRange)
            {
                string name = isShip ? ship.Name : $"астероид {SpriteUtility.ShortId(asteroid.Uid)}";
                GameLog.Add($"[Оружие] {name} вне радиуса поражения ({dist:F1} > {maxRange:F1}).");
                return false;
            }

            // Ручной выстрел отменяет активный режим следования (иначе автоогонь перепишет цель).
            ClearFollow();

            if (isShip)
            {
                Ship.ManualShootTargetUid = ship.Uid;
                Ship.ManualShootTargetIsAsteroid = false;
                GameLog.Add($"[Оружие] Цель выбрана: {ship.Name}. Выстрел — в следующем ходу.");
            }
            else
            {
                Ship.ManualShootTargetUid = asteroid.Uid;
                Ship.ManualShootTargetIsAsteroid = true;
                GameLog.Add($"[Оружие] Цель: астероид {SpriteUtility.ShortId(asteroid.Uid)}. Выстрел — в следующем ходу.");
            }
            return true;
        }

        /// <summary>Дальность самой дальнобойной исправной пушки (мировые ед.), 0 — оружия нет.</summary>
        public float GetMaxWeaponRange()
        {
            float maxRange = 0f;
            foreach (var range in WorkingWeaponRanges())
                if (range > maxRange) maxRange = range;
            return maxRange;
        }

        private float GetMinWorkingWeaponRange()
        {
            float min = float.MaxValue;
            foreach (var range in WorkingWeaponRanges())
                if (range > 0f && range < min) min = range;
            return min == float.MaxValue ? 0f : min;
        }

        private IEnumerable<float> WorkingWeaponRanges()
        {
            foreach (var slotKey in Ship.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Weapons))
            {
                string uid = Ship.Equipment.GetItemUid(slotKey);
                if (uid == null || !Ship.AllItems.TryGetValue(uid, out var weapon)) continue;
                if (!weapon.IsWorking) continue;
                yield return SRUnits.ToWorld(weapon.GetParam("Range", 0f));
            }
        }

        // ── Форсаж ─────────────────────────────────────────────────────────────

        /// <summary>Включить/выключить форсаж. Включение проверяет износ двигателя и близость к посадке.</summary>
        public void ToggleForsage()
        {
            var forsage = EquipmentSystem.GetEquipped(Ship, SlotKeys.Forsage);
            if (forsage == null)
            {
                GameLog.Add("[Форсаж] Не установлен.");
                return;
            }
            if (!forsage.IsWorking)
            {
                GameLog.Add($"[Форсаж] {forsage.Name}: повреждён, активация невозможна.");
                return;
            }

            if (!Ship.ForsageActive && !EquipmentSystem.CanActivateForsage(Ship, out string reason))
            {
                GameLog.Add($"[Форсаж] Активация невозможна: {reason}.");
                _view?.ShowMessage($"Форсаж: {reason}");
                return;
            }

            Ship.ForsageActive = !Ship.ForsageActive;
            GameLog.Add(Ship.ForsageActive ? $"[Форсаж] Включён: {forsage.Name}." : "[Форсаж] Выключен.");
            _view?.ShowMessage(Ship.ForsageActive ? "Форсаж активирован" : "Форсаж деактивирован");

            // Скорость корабля изменилась — метки дней на маршруте должны отражать новый шаг.
            RefreshPlannedRoute();
        }

        // ── Очередь захвата (CargoGrabber) ─────────────────────────────────────

        /// <summary>Добавить цель (по UID) в очередь захвата. AutoPullActive включается автоматически.</summary>
        public bool EnqueuePullTarget(string targetUid)
        {
            Ship.AutoPullActive = true;
            bool ok = PickupSystem.EnqueuePull(Ship, targetUid);
            if (ok)
            {
                GameLog.Add("[Захват] Цель добавлена в очередь.");
                RefreshRouteForPickup();
            }
            return ok;
        }

        /// <summary>Поставить в очередь все item-цели в радиусе захвата.</summary>
        public int EnqueueAllItemsInRadius()
        {
            var star = GameWorld.CurrentStar;
            if (star == null) return 0;
            Ship.AutoPullActive = true;
            int n = PickupSystem.EnqueueAllItemsInRadius(Ship, star.Ships);
            GameLog.Add($"[Захват] В очередь (радиус): {n} цел(ей).");
            if (n > 0) RefreshRouteForPickup();
            return n;
        }

        /// <summary>Поставить в очередь все item-цели в системе.</summary>
        public int EnqueueAllItemsInSystem()
        {
            var star = GameWorld.CurrentStar;
            if (star == null) return 0;
            Ship.AutoPullActive = true;
            int n = PickupSystem.EnqueueAllItemsInSystem(Ship, star.Ships);
            GameLog.Add($"[Захват] В очередь (система): {n} цел(ей).");
            if (n > 0) RefreshRouteForPickup();
            return n;
        }

        /// <summary>Очистить очередь захвата и снять AutoPullActive. Уже идущее притягивание не сбрасывается.</summary>
        public void ClearPullQueue()
        {
            Ship.PulledQueue.Clear();
            Ship.AutoPullActive = false;
            Ship.AutoPullSuspendedPath?.Clear();
            Ship.AutoPullSuspendedTarget = null;
            GameLog.Add("[Захват] Очередь очищена.");
            RefreshRouteForPickup();
        }

        private void RefreshRouteForPickup()
        {
            if (_targets.Count == 0) return;
            ApplyTargetsToShipData();
            RedrawPath();
        }
    }
}
