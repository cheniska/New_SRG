using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.Ships.Movement;
using SRG.Ships.Services;
using SRG.UI.HUD;
using SRG.UI.Screens;
using SRG.Utils;

namespace SRG.Ships.Player
{
    // PlayerShip — Маршрут / Follow / Pickup / Landing (этап T2 рефакторинга, июнь 2026). См. PlayerShip.cs.
    public partial class PlayerShip
    {

        private void BeginOrCycleFollow(ShipData targetShip)
        {
            FollowMode mode;
            if (_followShipUid == targetShip.Uid)
            {
                int next = (int)_followMode;
                for (int i = 0; i < FollowModeCount; i++)
                {
                    next = (next + 1) % FollowModeCount;
                    if (IsFollowModeAvailable((FollowMode)next, targetShip)) break;
                }
                mode = (FollowMode)next;
            }
            else
            {
                mode = FollowMode.PursueAtMaxRange;
            }
            ApplyFollow(targetShip, mode);
        }

        /// <summary>Установить конкретный режим следования по кораблю (без циклического перебора).
        /// Используется контекстным меню правого клика. Возвращает false, если режим недоступен.</summary>
        public bool SetFollow(ShipData target, FollowMode mode)
        {
            if (target == null || target == ShipData) return false;
            if (!IsFollowModeAvailable(mode, target)) return false;
            ApplyFollow(target, mode);
            return true;
        }

        /// <summary>Доступен ли данный режим следования для указанной цели с текущим оборудованием.</summary>
        public bool CanUseFollowMode(ShipData target, FollowMode mode)
            => target != null && target != ShipData && IsFollowModeAvailable(mode, target);

        private void ApplyFollow(ShipData targetShip, FollowMode mode)
        {
            HyperjumpController.CancelOnRouteChange(ShipData);
            _followShipUid = targetShip.Uid;
            _followMode = mode;

            Vector2 dest = ComputeFollowPoint(targetShip);
            _trackedTargetFunc = () =>
            {
                var t = FindFollowTarget();
                return t != null ? ComputeFollowPoint(t) : ShipData.Position;
            };
            _lastTrackedPos = dest;

            // Заменяем весь маршрут одной точкой следования (не учитываем Shift —
            // повторный клик по той же цели должен переключать режим, а не добавлять вейпоинт).
            ClearLandingIntent();
            if (mode == FollowMode.LandOnShip)
                ShipData.LandingCarrierUid = targetShip.Uid;
            _routeCompleted = false;
            _targets.Clear();
            _targets.Enqueue(dest);
            ApplyTargetsToShipData();
            RedrawPath();
            OnRouteAssigned?.Invoke();

            GameConsoleController.AddEntry(
                $"[Следование] {targetShip.Name}: {GetFollowModeName(_followMode)}.");
        }

        private void ClearFollow()
        {
            bool wasAutoFiring = ShipData != null && ShipData.ManualShootIsAutoFollow;
            _followShipUid = null;
            _followMode = FollowMode.PursueAtMaxRange;
            if (ShipData != null)
            {
                ShipData.ManualShootOnlyLongestRange = false;
                ShipData.ManualShootIsAutoFollow = false;
                // ManualShootTargetUid сбрасываем только если он был выставлен авто-режимом,
                // чтобы не затирать активный ручной выстрел (HandleWeaponClick).
                if (wasAutoFiring) ShipData.ManualShootTargetUid = null;
                // Курс на стыковку — часть follow-цели; выход из follow снимает его.
                ShipData.LandingCarrierUid = null;
            }
        }

        /// <summary>Есть ли распланированный маршрут / follow / посадка / гипер.
        /// Используется для решения, надо ли реагировать на клик по своему кораблю.</summary>
        private bool HasPlannedRoute()
        {
            if (_targets.Count > 0) return true;
            if (_landingPlanet != null) return true;
            if (!string.IsNullOrEmpty(_followShipUid)) return true;
            if (ShipData != null && (ShipData.HyperjumpPhase == HyperjumpPhase.Travel
                                     || ShipData.HyperjumpPhase == HyperjumpPhase.CancelEnd))
                return true;
            return false;
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

            if (ShipData != null)
            {
                ClearLandingIntent();
                ShipData.FreezeRoute();
                HyperjumpController.CancelOnRouteChange(ShipData);
            }

            _pathRenderer?.ClearPath();
        }

        private ShipData FindFollowTarget()
        {
            if (string.IsNullOrEmpty(_followShipUid)) return null;
            var star = GalaxyManager.Instance?.CurrentStar;
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

        private Vector2 ComputeFollowPoint(ShipData target)
        {
            if (target == null || target.CurrentHull <= 0) return ShipData.Position;

            // Минимально допустимая дистанция: преследователь никогда не должен сливаться
            // с целью — иначе корабли накладываются и игрок «стоит на» цели.
            float minStop = Mathf.Max(ShipData.SpriteWorldSize, target.SpriteWorldSize) * 1.5f;

            float desired = _followMode switch
            {
                FollowMode.PursueAtMaxRange    => GetMaxWeaponRange() * FollowRangeFraction,
                FollowMode.AttackWithLongRange => GetMaxWeaponRange() * FollowRangeFraction,
                FollowMode.AttackWithAllGuns   => GetMinWorkingWeaponRange() * FollowRangeFraction,
                FollowMode.FollowClose         => minStop,
                FollowMode.Board               => GetBoardingApproachDistance(),
                FollowMode.Tow                 => GetTowingApproachDistance(),
                FollowMode.LandOnShip          => 0f, // летим в центр носителя — стыковка по радиусу
                _ => minStop
            };
            // LandOnShip — единственный режим, которому разрешено «слиться» с целью.
            if (_followMode != FollowMode.LandOnShip && desired < minStop) desired = minStop;

            Vector2 toTarget = target.Position - ShipData.Position;
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

        private float GetMinWorkingWeaponRange()
        {
            float min = float.MaxValue;
            var ship = ShipData;
            if (ship == null) return 0f;
            foreach (var slotKey in ship.Equipment.GetOccupiedSlotsOfCategory(EquipmentCategory.Weapons))
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid == null || !ship.AllItems.TryGetValue(uid, out var weapon)) continue;
                if (!weapon.IsWorking) continue;
                float range = SRUnits.ToWorld(weapon.GetParam("Range", 0f));
                if (range > 0f && range < min) min = range;
            }
            return min == float.MaxValue ? 0f : min;
        }

        public static string GetFollowModeName(FollowMode mode) => mode switch
        {
            FollowMode.PursueAtMaxRange    => "преследовать на дальней дистанции",
            FollowMode.AttackWithLongRange => "атаковать дальней пушкой",
            FollowMode.AttackWithAllGuns   => "атаковать всеми пушками",
            FollowMode.FollowClose         => "следовать вплотную",
            FollowMode.Board               => "взять на абордаж",
            FollowMode.Tow                 => "взять на буксир",
            FollowMode.LandOnShip          => "сесть на корабль",
            _ => mode.ToString()
        };

        // ── Очередь захвата (CargoGrabber) ─────────────────────────────────────

        /// <summary>Добавить конкретную цель (по UID) в очередь захвата игрока. AutoPullActive
        /// включается автоматически, чтобы корабль останавливался при попадании цели в радиус.</summary>
        public bool EnqueuePullTarget(string targetUid)
        {
            if (ShipData == null) return false;
            ShipData.AutoPullActive = true;
            bool ok = PickupSystem.EnqueuePull(ShipData, targetUid);
            if (ok)
            {
                GameConsoleController.AddEntry("[Захват] Цель добавлена в очередь.");
                RefreshRouteForPickup();
            }
            return ok;
        }

        /// <summary>Поставить в очередь все item-цели (контейнеры), уже находящиеся в радиусе захвата.
        /// AutoPullActive включается.</summary>
        public int EnqueueAllItemsInRadius()
        {
            if (ShipData == null) return 0;
            var star = GalaxyManager.Instance?.CurrentStar;
            if (star == null) return 0;
            ShipData.AutoPullActive = true;
            int n = PickupSystem.EnqueueAllItemsInRadius(ShipData, star.Ships);
            GameConsoleController.AddEntry($"[Захват] В очередь (радиус): {n} цел(ей).");
            if (n > 0) RefreshRouteForPickup();
            return n;
        }

        /// <summary>Поставить в очередь все item-цели в системе. По мере подлёта корабль будет
        /// останавливаться и подбирать их по одной (если AutoPullActive).</summary>
        public int EnqueueAllItemsInSystem()
        {
            if (ShipData == null) return 0;
            var star = GalaxyManager.Instance?.CurrentStar;
            if (star == null) return 0;
            ShipData.AutoPullActive = true;
            int n = PickupSystem.EnqueueAllItemsInSystem(ShipData, star.Ships);
            GameConsoleController.AddEntry($"[Захват] В очередь (система): {n} цел(ей).");
            if (n > 0) RefreshRouteForPickup();
            return n;
        }

        /// <summary>Очистить очередь захвата и снять AutoPullActive. Уже идущее притягивание не сбрасывается.</summary>
        public void ClearPullQueue()
        {
            if (ShipData == null) return;
            ShipData.PulledQueue.Clear();
            ShipData.AutoPullActive = false;
            ShipData.AutoPullSuspendedPath?.Clear();
            ShipData.AutoPullSuspendedTarget = null;
            GameConsoleController.AddEntry("[Захват] Очередь очищена.");
            RefreshRouteForPickup();
        }

        /// <summary>Пересчитывает waypoints и визуал пути — нужно после изменения PulledQueue, чтобы
        /// в маршрут добавились/убрались остановки рядом с предметами захвата.</summary>
        private void RefreshRouteForPickup()
        {
            if (_targets.Count == 0) return;
            ApplyTargetsToShipData();
            RedrawPath();
        }

        /// <summary>
        /// Доступность режима: Board требует абордажный крюк и не работает по предметам;
        /// Tow требует буксировочную установку; LandOnShip — флаг CanBeLandedOn у типа цели
        /// (только линкоры) и не-враждебность. Остальные доступны всегда.
        /// </summary>
        private bool IsFollowModeAvailable(FollowMode mode, ShipData target)
        {
            switch (mode)
            {
                case FollowMode.Board:
                    // Станции не абордируются (см. BoardingSystem.CanBoard).
                    return target != null && !target.IsItem && !target.IsStation
                        && BoardingSystem.FindGrapplingHook(ShipData) != null;
                case FollowMode.Tow:
                    // Станции — только специальной установкой (CanTowStations в ItemsConfig).
                    var rig = TowSystem.FindTowingRig(ShipData);
                    if (target == null || rig == null) return false;
                    if (target.IsStation && !TowSystem.CanRigTowStations(rig)) return false;
                    return true;
                case FollowMode.LandOnShip:
                    return target != null && !target.IsItem
                        && ShipDockingService.CanLandOn(ShipData, target, out _);
                default: return true;
            }
        }

        private float GetBoardingApproachDistance()
        {
            var hook = BoardingSystem.FindGrapplingHook(ShipData);
            if (hook == null) return 0f;
            // Подходим чуть ближе радиуса крюка — гарантируем, что цель внутри.
            return SRUnits.ToWorld(hook.GetParam("PullRadius", 0f)) * FollowRangeFraction;
        }

        private float GetTowingApproachDistance()
        {
            var rig = TowSystem.FindTowingRig(ShipData);
            if (rig == null) return 0f;
            return SRUnits.ToWorld(rig.GetParam("PullRadius", 0f)) * FollowRangeFraction;
        }

        public string GetFollowTargetName() => FindFollowTarget()?.Name;

        private void UpdateFollowAutoFire()
        {
            if (ShipData == null) return;
            // Мёртвый игрок не должен «довзводить» ManualShoot* поля — иначе в режиме
            // наблюдения авто-follow каждый ход переустанавливает цель выстрела.
            if (ShipData.CurrentHull <= 0) return;
            if (string.IsNullOrEmpty(_followShipUid)) return;

            // Цель умерла или ушла — выходим из режима follow (для всех режимов, не только атакующих).
            var target = FindFollowTarget();
            if (target == null)
            {
                GameConsoleController.AddEntry("[Следование] Цель утрачена.");
                ClearFollow();
                return;
            }

            bool isAttackMode = _followMode == FollowMode.AttackWithLongRange
                             || _followMode == FollowMode.AttackWithAllGuns;

            // Снимаем «висящий» авто-выстрел, если предыдущий цикл оставил ManualShoot*
            // от Attack-режима, а текущий режим уже не стреляющий (Pursue/FollowClose/Board/Tow).
            // Без этого следующий ход откроет огонь по follow-цели даже в Tow/Board режиме.
            if (!isAttackMode && ShipData.ManualShootIsAutoFollow)
            {
                ShipData.ManualShootTargetUid = null;
                ShipData.ManualShootOnlyLongestRange = false;
                ShipData.ManualShootIsAutoFollow = false;
            }

            // Авто-исполнение абордажа/буксира при попадании цели в радиус соответствующего оборудования.
            if (_followMode == FollowMode.Board || _followMode == FollowMode.Tow)
            {
                TryAutoBoardOrTow(target);
                return;
            }

            // Авто-исполнение стыковки: сближение до радиуса стыковки носителя = посадка.
            if (_followMode == FollowMode.LandOnShip)
            {
                TryAutoLandOnShip(target);
                return;
            }

            if (!isAttackMode) return;

            ShipData.ManualShootTargetUid = _followShipUid;
            ShipData.ManualShootTargetIsAsteroid = false;
            ShipData.ManualShootOnlyLongestRange = (_followMode == FollowMode.AttackWithLongRange);
            ShipData.ManualShootIsAutoFollow = true;
        }

        private Dictionary<string, ShipData> BuildShipLookup()
        {
            var star = GalaxyManager.Instance?.CurrentStar;
            if (star?.Ships == null) return null;
            var dict = new Dictionary<string, ShipData>(star.Ships.Count);
            foreach (var s in star.Ships) dict[s.Uid] = s;
            return dict;
        }

        private void TryAutoBoardOrTow(ShipData target)
        {
            var equipCfg = GalaxyManager.Instance?.Context?.ItemsConfig;
            if (equipCfg == null) return;
            if (_followMode == FollowMode.Board)
            {
                if (BoardingSystem.CanBoard(ShipData, target, equipCfg, out _))
                {
                    if (BoardingSystem.TryBoard(ShipData, target, equipCfg))
                    {
                        GameConsoleController.AddEntry($"[Абордаж] {target.Name} взят на абордаж.");
                        ClearFollow();
                    }
                }
            }
            else if (_followMode == FollowMode.Tow)
            {
                if (TowSystem.CanTow(ShipData, target, equipCfg, out _))
                {
                    if (TowSystem.TryTow(ShipData, target, equipCfg, BuildShipLookup()))
                    {
                        if (target.TowedByUid == ShipData.Uid)
                            GameConsoleController.AddEntry($"[Буксир] {target.Name} прицеплен.");
                        else if (target.PulledByUid == ShipData.Uid)
                            GameConsoleController.AddEntry($"[Буксир] {target.Name} притягивается…");
                        ClearFollow();
                    }
                }
            }
        }

        /// <summary>Проверка возможности стыковки. Сама посадка на корабль-носитель идёт
        /// через тот же двухфазный пайплайн, что и посадка на планету: PrepareForTurn ставит
        /// LandingPhase=Fading при попадании в dock-радиус в конце хода, а TryFinalizeLanding
        /// в OnTurnComplete переносит LandingCarrierUid → LandedOnShipUid (или открывает UI
        /// станции). Здесь только сторожевая проверка на потерю цели/враждебность.</summary>
        private void TryAutoLandOnShip(ShipData carrier)
        {
            if (!ShipDockingService.CanLandOn(ShipData, carrier, out string reason))
            {
                GameConsoleController.AddEntry($"[Стыковка] Посадка невозможна: {reason}.");
                ClearFollow();
                return;
            }
            // LandingCarrierUid уже проставлен ApplyFollow — фактическая посадка/UI-станция
            // решаются в TryFinalizeLanding по факту касания dock-радиуса в конце хода.
        }

        /// <summary>Взлёт с корабля-носителя (аналог OnLeavePlanet): позиция = текущая позиция
        /// носителя, fade-in, автоматический вейпоинт по текущему курсу.</summary>
        private void UndockFromCarrier()
        {
            var star = GalaxyManager.Instance?.CurrentStar;
            var carrier = star?.Ships?.Find(s => s.Uid == ShipData.LandedOnShipUid);
            ShipDockingService.Undock(ShipData, carrier);
            BeginTakeoffRoute();
            GameConsoleController.AddEntry($"[Стыковка] Взлёт с {carrier?.Name ?? "носителя"}.");
        }

        /// <summary>Общая часть взлёта с любой посадочной цели: сброс follow/tracked,
        /// визуальный fade-in, автоматический вейпоинт по текущему курсу на 1.5 ед. вперёд.
        /// Не трогает LandedPlanetUid/LandedOnShipUid — их снимает вызывающий (BeginTakeoff/Undock).</summary>
        private void BeginTakeoffRoute()
        {
            ClearFollow();
            _trackedTargetFunc = null;
            _landingPlanet = null;
            _visual?.BeginTakeoff();

            float heading = ShipData.CurrentHeading;
            var dir = float.IsNaN(heading)
                ? Vector2.right
                : new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
            var target = ShipData.Position + dir * 1.5f;

            _targets.Clear();
            _targets.Enqueue(target);
            _routeCompleted = false;
            ApplyTargetsToShipData();
            RedrawPath();
            OnRouteAssigned?.Invoke();
        }

        /// <summary>
        /// Показывает траекторию к HyperjumpEdge во время фазы Travel — рисуется тем же PathRenderer,
        /// что и обычный маршрут, но БЕЗ вызова CancelOnRouteChange (этот метод не считается
        /// «прокладкой нового курса», а отображением уже запрошенного гиперпрыжка).
        /// </summary>
        public void ShowHyperjumpPath()
        {
            if (ShipData == null || ShipData.HyperjumpPhase != HyperjumpPhase.Travel) return;
            _targets.Clear();
            _targets.Enqueue(ShipData.HyperjumpEdge);
            _routeCompleted = false;
            ClearLandingIntent();
            ApplyTargetsToShipData();
            RedrawPath();
        }

        private void SetWaypointToPosition(Vector2 pos)
        {
            if (ShipData == null) return;
            HyperjumpController.CancelOnRouteChange(ShipData);
            KeyCode shiftKey = _settings != null ? _settings.MultiWaypointKey : KeyCode.LeftShift;
            bool addMode = Input.GetKey(shiftKey);

            if (addMode)
                _targets.Enqueue(pos);
            else
            {
                _targets.Clear();
                _targets.Enqueue(pos);
            }

            // Любая не-посадочная команда отменяет ранее назначенный курс на посадку,
            // иначе OnTurnComplete переназначит маршрут обратно на планету.
            ClearLandingIntent();

            _routeCompleted = false;
            ApplyTargetsToShipData();
            RedrawPath();
            OnRouteAssigned?.Invoke();


        }
        private void OnLeavePlanet()
        {
            // Единая точка взлёта: если пристыкованы к кораблю-носителю — идёт через Undock
            // (позиция берётся с носителя, снимается LandedOnShipUid). Иначе — обычный планетный
            // BeginTakeoff (позиция обновляется до текущей позиции планеты, fade-in).
            if (ShipData != null && !string.IsNullOrEmpty(ShipData.LandedOnShipUid))
            {
                UndockFromCarrier();
                return;
            }
            BeginTakeoffRoute();
        }

        private void SetLandingCourse(PlanetData planet)
        {
            // LandingBlocked — full-оккупация или скриптовая блокировка. Игрок допускается только если
            // он член стороны-контроллёра планеты (обычно нет — блокирует посадку).
            if (planet.Settlement.LandingBlocked)
            {
                string ctrl = OccupationService.GetControllingOwner(planet);
                if (ShipData.Owner != ctrl)
                {
                    GameConsoleController.AddEntry($"[Навигация] Посадка запрещена: {planet.Name} закрыта для внешних кораблей.");
                    return;
                }
            }
            HyperjumpController.CancelOnRouteChange(ShipData);
            ClearFollow();
            _landingPlanet = planet;
            ShipData.LandingPlanetUid = planet.Uid;

            Vector2 planetPos = PredictLandingTarget(planet);
            _trackedTargetFunc = () => PredictLandingTarget(planet);
            _lastTrackedPos = planetPos;

            _targets.Clear();
            _targets.Enqueue(planetPos);
            _routeCompleted = false;

            ApplyTargetsToShipData();
            RedrawPath();
            OnRouteAssigned?.Invoke();
            GameConsoleController.AddEntry($"[Навигация] Курс на посадку: {planet.Name}.");
        }

        // SR2HD §5.2.B: catch-up upreждение + парковка ВНЕ R_land на дальнем подлёте.
        // Реализация в PlanetGeometry (общая с НПС-вариантом §5.2.C).
        private Vector2 PredictLandingTarget(PlanetData planet)
            => PlanetGeometry.PredictPlayerLandingTarget(ShipData, planet);

        /// <summary>
        /// Запрос прыжка через червоточину. Внутри вызывает <see cref="HyperjumpController.RequestJumpViaWormhole"/>,
        /// который переводит корабль в фазу Travel с HyperjumpEdge = позиция червоточины. Дальнейшая кинематика
        /// (пилотирование, вход в HyperEnter, миграция систем, HyperExit) идёт стандартно как у гиперпрыжка.
        /// </summary>
        private void SetWormholeCourse(WormholeData wormhole, StarData sourceStar)
        {
            if (wormhole == null || sourceStar == null) return;
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            if (galaxy == null) return;

            if (wormhole.Phase != WormholePhase.Open)
            {
                GameConsoleController.AddEntry("[Червоточина] Не пройти — червоточина ещё не стабилизировалась.");
                return;
            }

            if (!HyperjumpController.RequestJumpViaWormhole(ShipData, wormhole, sourceStar, galaxy))
            {
                GameConsoleController.AddEntry("[Червоточина] Не удалось войти (проверьте состояние корабля).");
                return;
            }

            // Обычный маршрут отменяется — визуально мы летим к точке червоточины через ShowHyperjumpPath.
            ClearFollow();
            ClearLandingIntent();
            ShowHyperjumpPath();
            OnRouteAssigned?.Invoke();

            string targetName = galaxy.StarsMap.TryGetValue(wormhole.TargetStarUid, out var target)
                ? target.Name : "?";
            GameConsoleController.AddEntry($"[Червоточина] Курс на червоточину → {targetName}.");
        }

        private void RedrawPath()
        {
            var effective = ComputeEffectiveTargets();
            if (effective.Count == 0) { _pathRenderer.ClearPath(); return; }

            var star = GalaxyManager.Instance?.CurrentStar;

            Vector2 prevPos = ShipData.Position;
            float heading = ShipData.CurrentHeading;

            var chain = new List<Vector2> { prevPos };

            float speed = ShipTrajectory.EffectiveSpeedPerTurn(ShipData);
            float turnRad = ShipTrajectory.EffectiveTurnRadPerTurn(ShipData);

            foreach (var target in effective)
            {
                var waypoints = ShipTrajectory.BuildPath(
                    prevPos, target, heading, speed, turnRad, star);

                if (waypoints.Count == 0) continue;
                chain.AddRange(waypoints);

                if (waypoints.Count >= 2)
                    heading = Mathf.Atan2(
                        waypoints[waypoints.Count - 1].y - waypoints[waypoints.Count - 2].y,
                        waypoints[waypoints.Count - 1].x - waypoints[waypoints.Count - 2].x);
                prevPos = target;
            }

            float step = SRUnits.ToWorld(ShipData.ActualSpeed);

            // При посадке конечный маркер визуально размещаем на самой планете,
            // а не в точке упреждения, куда строится фактический маршрут перехвата.
            Vector2? endMarker = null;
            if (_landingPlanet != null)
                endMarker = OrbitMath.GetPlanetWorldPosition(_landingPlanet);

            _pathRenderer.DrawPath(chain, step, preSampled: true, endMarkerOverride: endMarker);
        }
    }
}
