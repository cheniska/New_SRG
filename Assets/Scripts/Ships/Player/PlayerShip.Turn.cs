using System.Collections.Generic;
using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.UI.HUD;
using SRG.UI.Screens;

namespace SRG.Ships.Player
{
    // PlayerShip — Turn lifecycle (этап T2 рефакторинга, июнь 2026). См. PlayerShip.cs.
    public partial class PlayerShip
    {

        private void OnTurnCalculate(TurnAnimationData anim)
        {
            _isTurnInProgress = true;
            IsMovingThisTurn = false;
            _pathRenderer.ClearPath();
            _weaponModeActive = false;
            if (_dialogModeActive) DisableDialogMode(silent: true);
            _visual?.PrepareForTurn(anim, GalaxyManager.Instance?.CurrentStar);

            // На arrival-симуляции корабль должен появиться сразу прозрачным — на случай если
            // предыдущий ход оставил alpha в другом состоянии. HyperArrive держит его невидимым
            // всё время открытия портала; HyperExit стартует fade-in c 0.
            // А также сразу повёрнут в нужную сторону (иначе плавный поворот 180° за 1 сек).
            if (ShipData != null && (ShipData.HyperjumpPhase == HyperjumpPhase.HyperArrive
                                     || ShipData.HyperjumpPhase == HyperjumpPhase.HyperExit))
            {
                _visual?.SetAlpha(0f);
                _visual?.SnapRotationToHyperjumpHeading();
            }
            // ProcessPlayerManualShot уже отработал и обнулил ManualShoot* поля — взводим их заново для
            // следующего хода, чтобы в авто-режиме (когда между ходами Update не успевает выполниться)
            // огонь по follow-цели не прерывался.
            UpdateFollowAutoFire();

            // Фиксируем HP для следующего хода: на момент CheckPauseConditions следующего тика
            // это будет «HP до боевой симуляции» — нужно для подсчёта урона за ход в автобое.
            if (ShipData != null) HullBeforeLastSimulation = ShipData.CurrentHull;
        }

        private void OnTurnAnimate(float progress, int currentSubTurn)
        {
            var animData = GalaxyManager.Instance?.LastTurnData;
            if (animData == null || _visual == null) return;

            var dir = _visual.AnimateTurn(progress, currentSubTurn, animData);
            if (dir.sqrMagnitude > 0.0001f) IsMovingThisTurn = true;
            if (progress >= 0.99f && dir.sqrMagnitude > 0.0001f)
                ShipData.CurrentHeading = Mathf.Atan2(dir.y, dir.x);
        }

        private void OnTurnComplete(TurnAnimationData anim)
        {
            _visual?.EndTurn();
            _isTurnInProgress = false;
            transform.position = new Vector3(ShipData.Position.x, ShipData.Position.y, 0f);

            // Истекшие модификаторы скилов (болезни/стимы/читы) удаляются и фаерится OnChanged.
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy != null) ShipData?.Skills?.TickTurn(galaxy.CurrentTurn);

            FinalizeHeadingFromAnimation();

            if (_routeCompleted) return;

            float sqDist = (ShipData.TargetPosition - ShipData.Position).sqrMagnitude;
            bool targetReached = sqDist <= 0.01f;

            _targets.Clear();
            if (!targetReached)
                _targets.Enqueue(ShipData.TargetPosition);
            foreach (var wp in ShipData.TargetQueue)
                _targets.Enqueue(wp);

            ShipData.TargetQueue.Clear();

            Debug.Log($"[PlayerShip] OnTurnComplete: pos={ShipData.Position:F2} targetReached={targetReached} remaining={_targets.Count}");

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

        /// <summary>По последнему кадру анимации обновляет CurrentHeading корабля (чтобы спрайт
        /// смотрел в сторону движения на момент конца хода).</summary>
        private void FinalizeHeadingFromAnimation()
        {
            var lastAnim = GalaxyManager.Instance?.LastTurnData;
            if (lastAnim == null || !lastAnim.ShipFrames.TryGetValue(ShipData.Uid, out var doneFrames)) return;
            var dv = doneFrames.SubTurns[GalaxyData.SubTurnsPerTurn] - doneFrames.SubTurns[GalaxyData.SubTurnsPerTurn - 1];
            if (dv.sqrMagnitude > 0.0001f)
                ShipData.CurrentHeading = Mathf.Atan2(dv.y, dv.x);
        }

        /// <summary>Если в этом ходу был выставлен LandingPhase=Fading — финализирует посадку
        /// на любую <see cref="ILandingSite"/>: планета/станция/корабль-носитель. Единая точка
        /// перевода Landing*Uid → Landed*Uid + открытие UI через <see cref="PlayerManager.LandOn"/>.
        /// Возвращает true, если посадка завершена и дальнейшая обработка хода не нужна.</summary>
        private bool TryFinalizeLanding()
        {
            if (ShipData.LandingPhase != LandingPhase.Fading) return false;
            var star = GalaxyManager.Instance?.CurrentStar;
            var site = LandingSiteRegistry.ResolveActiveTarget(ShipData, star);

            // Цель исчезла (планета/носитель уничтожены/ушли) или носитель стал недопустим —
            // отменяем посадку и возвращаем видимость (EndTurn мог обнулить alpha по Fading).
            if (site == null
                || (site is ShipData carrier && !ShipDockingService.CanLandOn(ShipData, carrier, out _)))
            {
                ClearLandingIntent();
                _visual?.SetAlpha(1f);
                return false;
            }
            return FinalizeLanding(site);
        }

        /// <summary>Общая финализация посадки на любую <see cref="ILandingSite"/>. Планетный
        /// и корабельный варианты различаются только post-hook'ами (VisitedPlanetUids/NotifyLanded
        /// для планеты; ShipDockingService.Land/NotifyDockedOnShip для корабля-носителя).</summary>
        private bool FinalizeLanding(ILandingSite site)
        {
            // Общая часть: стоп-курс, чистка очередей и path-рендера, финализация route-state.
            ShipData.TargetPosition = ShipData.Position;
            ShipData.TargetQueue.Clear();
            _targets.Clear();
            _pathRenderer.ClearPath();
            _routeCompleted    = true;
            _trackedTargetFunc = null;
            ClearLandingIntent();

            if (site is PlanetData planet)
            {
                ShipData.LandedPlanetUid = planet.Uid;
                _landingPlanet           = planet;
                GalaxyManager.Instance?.GeneratedGalaxy?.VisitedPlanetUids?.Add(planet.Uid);
                _visual?.NotifyLanded(planet);
                PlayerManager.Instance?.LandOn(planet);
                return true;
            }

            if (site is ShipData carrier)
            {
                // Единый путь для станций и обычных носителей: Land (LandedOnShipUid + freeze),
                // визуальное скрытие, открытие UI. Набор вкладок в UI различается по IsStation
                // самого носителя. Отстыковка (Взлёт из UI) идёт через PlayerShip.OnLeavePlanet
                // → UndockFromCarrier по LandedOnShipUid.
                ShipDockingService.Land(ShipData, carrier);
                ClearFollow();
                _visual?.NotifyDockedOnShip();
                PlayerManager.Instance?.DockOnShip(carrier);

                string tag = carrier.IsStation ? "Станция" : "Стыковка";
                GameConsoleController.AddEntry($"[{tag}] {(carrier.IsStation ? "Стыковка со станцией" : "Посадка на")} {carrier.Name}.");
                HudMessageController.Show($"{tag}: {carrier.Name}");
                return true;
            }
            return false;
        }

        /// <summary>Общий сброс «желания сесть»: обнуление обоих UID и фазы + локального
        /// _landingPlanet. Используется и при успешной посадке, и при откате (носитель ушёл).</summary>
        private void ClearLandingIntent()
        {
            _landingPlanet = null;
            if (ShipData == null) return;
            ShipData.LandingPlanetUid  = null;
            ShipData.LandingCarrierUid = null;
            ShipData.LandingPhase      = LandingPhase.None;
        }

        /// <summary>В _targets есть точки — применяем их к маршруту корабля (с tracked-обновлением,
        /// если есть _trackedTargetFunc).</summary>
        private void ContinueRouteFromTargets()
        {
            if (_trackedTargetFunc != null)
            {
                Vector2 newPos = _trackedTargetFunc();
                var all = new List<Vector2>(_targets);
                all[0] = newPos;
                _targets.Clear();
                foreach (var p in all) _targets.Enqueue(p);
                _lastTrackedPos = newPos;
            }
            ApplyTargetsToShipData();
            RedrawPath();
        }

        /// <summary>Маршрут исчерпан, но есть LandingPlanetUid (хвост хода НЕ попал в R_land):
        /// добиваем подлёт — следующий ход будет либо Approach, либо Fading.
        /// Возвращает true, если перепрокладка сделана.</summary>
        private bool ContinueLandingApproach()
        {
            if (ShipData.LandingPlanetUid == null) return false;
            var star = GalaxyManager.Instance?.CurrentStar;
            var planet = star?.Planets?.Find(p => p.Uid == ShipData.LandingPlanetUid);
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

        /// <summary>Режим следования за кораблём — «маршрут завершён» здесь обычно ложный
        /// (точка следования совпала с позицией, но сама цель движется). Перезаряжаем
        /// путь свежей tracked-точкой и продолжаем без выхода в planning.
        /// Возвращает true, если follow продолжен.</summary>
        private bool ContinueFollow()
        {
            if (!IsFollowingShip) return false;
            var followTarget = FindFollowTarget();
            if (followTarget == null) return false;     // цель пропала — UpdateFollowAutoFire очистит follow в Update
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
            ShipData.TargetPosition = ShipData.Position;
            ShipData.TargetQueue.Clear();
            _pathRenderer.ClearPath();
            _routeCompleted = true;
            _trackedTargetFunc = null;

            bool simulationInterruptOnRoute = _settings == null || _settings.SimulationInterruptOnActionDone;
            Debug.Log($"[PlayerShip] Route complete. SimulationInterrupt={simulationInterruptOnRoute}");
            if (simulationInterruptOnRoute)
                GalaxyManager.Instance?.RequestPlanning(PlanningReason.RouteCompleted);
        }

        private void UpdateTrackedTarget()
        {
            if (_trackedTargetFunc == null || _targets.Count == 0) return;
            Vector2 newPos = _trackedTargetFunc();
            if ((newPos - _lastTrackedPos).sqrMagnitude < TrackUpdateThreshold * TrackUpdateThreshold) return;

            _lastTrackedPos = newPos;
            var all = new List<Vector2>(_targets);
            all[0] = newPos;
            _targets.Clear();
            foreach (var p in all) _targets.Enqueue(p);

            ApplyTargetsToShipData();
            RedrawPath();
        }

        private void ApplyTargetsToShipData()
        {
            var effective = ComputeEffectiveTargets();
            if (effective.Count == 0) return;

            ShipData.TargetPosition = effective[0];
            ShipData.TargetQueue.Clear();

            ShipData.WaypointIndex = 0;

            var star = GalaxyManager.Instance?.CurrentStar;
            ShipData.Waypoints = ShipTrajectory.BuildPath(ShipData, ShipData.TargetPosition, star);

            for (int i = 1; i < effective.Count; i++)
                ShipData.TargetQueue.Enqueue(effective[i]);
        }

        /// <summary>
        /// Возвращает копию пользовательских целей маршрута. Подбор предметов идёт «на лету»:
        /// маршрут не модифицируется, корабль не останавливается у предметов из PulledQueue —
        /// CargoGrabber подбирает их по мере пролёта в радиусе.
        /// </summary>
        private List<Vector2> ComputeEffectiveTargets()
        {
            return new List<Vector2>(_targets);
        }
    }
}
