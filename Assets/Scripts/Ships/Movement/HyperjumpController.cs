using UnityEngine;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.NpcAI;
using SRG.NpcAI.Actions;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Ships.Movement
{
    /// <summary>
    /// Унифицированный контроллер фаз гиперперехода (двухходовая модель).
    /// Состояние хранится в полях ShipData (HyperjumpPhase / HyperjumpTargetStarUid / HyperjumpEdge ...);
    /// здесь только переходы между фазами и расчёт целей движения.
    ///
    /// Жизненный цикл фаз:
    ///   Travel ─ N ходов ─→ HyperEnter (ход 1) → HyperArrive (ход 2) → HyperExit (ход 3) → None
    ///
    /// HyperEnter:
    ///   • Источник: открывается портал на HyperjumpEdge, корабль входит и fade-out.
    ///   • Цель:     в этот ход портал на HyperjumpArrivalEdge НЕ открывается (сторонний наблюдатель
    ///               в целевой системе увидит его на HyperArrive).
    ///   • Конец хода: списание топлива, миграция в целевую систему (ship.CurrentStarUid = target).
    ///   • Можно отменить → CancelEnd.
    ///
    /// HyperArrive:
    ///   • Корабль уже в целевой системе, стоит невидимо у HyperjumpArrivalEdge (не двигается).
    ///   • Цель:     открывается портал на HyperjumpArrivalEdge (begin+mid).
    ///   • Для игрока — этот ход идёт сразу после scene swap: камера уже стоит на ArrivalEdge,
    ///     игрок смотрит анимацию открытия портала.
    ///   • Конец хода: phase → HyperExit; для NPC тикается NpcBrain → выбирается активность,
    ///     ставится ship.TargetPosition для вылета на следующем ходу.
    ///
    /// HyperExit:
    ///   • Цель:     портал end (закрывается). Корабль появляется у HyperjumpArrivalEdge (alpha 0→1)
    ///               и сразу летит по AI-приказу (или скользит вперёд, если приказа нет).
    ///   • Конец хода: phase → None, обычное движение.
    ///
    /// Travel можно отменить мгновенно → None.
    /// </summary>
    public static class HyperjumpController
    {
        // Множитель радиуса системы — точка края, до которой долетает корабль.
        private const float SystemEdgeRadiusMul = 1.0f;
        // Дистанция "видимого пролёта" сквозь портал во время HyperEnter (мировые единицы).
        // Корабль за ход проходит от текущей позиции до HyperjumpEdge, и дополнительно
        // визуально съезжает на эту дистанцию вперёд (через AnimateHyperjumpTurn).
        public const float HyperEnterSlideDistance = 1.5f;
        // Разброс точек входа/выхода вдоль дуги края системы — чтобы при массовом
        // гиперпереходе порталы и корабли не накладывались друг на друга.
        // Угловой разброс должен быть существенно меньше радиального, иначе порталы
        // визуально «уезжают» в сторону от идеальной линии источник→цель (при радиусе
        // системы 5–10 даже 3° даёт дугу ≈0.3–0.5 ед., что сопоставимо с радиальным ±15%).
        private const float JumpEdgeArcSpreadDeg = 3f;
        private const float JumpEdgeRadialSpread = 0.15f;   // ±15% от sysRadius
        // Толерантность "корабль долетел до края" в долях turn-speed.
        private const float EdgeArrivalToleranceFraction = 0.1f;
        private const float EdgeArrivalToleranceMin = 0.05f;

        /// <summary>
        /// true, если корабль в фазе Travel и уже в пределах одного хода от HyperjumpEdge —
        /// то есть в этом ходу он доедет до края и начнётся открытие портала. Используется визуалом,
        /// чтобы заранее (с конца begin-клипа) начать fade-out корабля.
        /// </summary>
        public static bool IsApproachingHyperEdge(ShipData ship)
        {
            if (ship == null || ship.HyperjumpPhase != HyperjumpPhase.Travel) return false;
            float speedPerTurn = ShipTrajectory.EffectiveSpeedPerTurn(ship);
            if (speedPerTurn <= 0f) return false;
            return Vector2.Distance(ship.PreviousPosition, ship.HyperjumpEdge) <= speedPerTurn
                || Vector2.Distance(ship.Position, ship.HyperjumpEdge) <= speedPerTurn;
        }

        /// <summary>Возможен ли прыжок: установлен двигатель и бак, дистанция в пределах JumpRange, топлива хватает.</summary>
        public static bool CanRequestJump(ShipData ship, StarData target, GalaxyData galaxy, out string reason)
        {
            reason = null;
            if (ship == null || target == null || galaxy == null) { reason = "no ship/target"; return false; }
            if (ship.CurrentHull <= 0)       { reason = "ship destroyed"; return false; }
            if (ship.HyperjumpPhase != HyperjumpPhase.None)
            {
                reason = "уже в гиперпереходе"; return false;
            }
            if (!string.IsNullOrEmpty(ship.LandedOnShipUid))
            {
                reason = "корабль пристыкован к носителю"; return false;
            }
            if (string.IsNullOrEmpty(ship.CurrentStarUid)
                || !galaxy.StarsMap.TryGetValue(ship.CurrentStarUid, out var fromStar))
            {
                reason = "ship has no current star"; return false;
            }
            if (fromStar == target) { reason = "уже в этой системе"; return false; }

            int jumpRange = EquipmentSystem.GetJumpRange(ship);
            if (jumpRange <= 0) { reason = "двигатель не задаёт JumpRange"; return false; }

            var fuelTank = EquipmentSystem.GetEquipped(ship, SlotKeys.FuelTank);
            if (fuelTank == null) { reason = "нет топливного бака"; return false; }
            if (!fuelTank.IsWorking) { reason = "топливный бак повреждён"; return false; }

            float dist = CalcDistance(fromStar, target);
            if (dist > jumpRange) { reason = $"слишком далеко: {dist:F1} > {jumpRange} пк"; return false; }

            int needFuel = CalcFuelCost(fromStar, target);
            int curFuel = EquipmentSystem.GetCurrentFuel(ship);
            if (curFuel < needFuel) { reason = $"не хватает топлива: {curFuel}/{needFuel}"; return false; }
            return true;
        }

        /// <summary>Расход топлива на прыжок (по той же формуле, что использовал старый JumpToStar).</summary>
        public static int CalcFuelCost(StarData from, StarData to)
        {
            var cfgSettings = GameWorld.Context?.Config?.Settings;
            float costPerPc = cfgSettings != null ? cfgSettings.JumpFuelCostPerUnit : 1f;
            float distPc = CalcDistance(from, to);
            return Mathf.Max(1, Mathf.RoundToInt(distPc * costPerPc));
        }

        /// <summary>Число ходов, которое корабль проводит в фазе HyperArrive («в гипертоннеле»).
        /// База — <c>ceil(distance / HyperCrossPerTurnDistance)</c>, минус <c>Hyperjump.TurnsDelta</c>
        /// (например, Гипергенератор даёт -1). Минимум — 1 ход (гарантирует одну итерацию HyperArrive).
        /// Червоточина считается моментальным гиперпереходом — возвращает 1 независимо от расстояния.</summary>
        public static int CalcHyperArriveTurns(ShipData ship, StarData from, StarData to)
        {
            if (!string.IsNullOrEmpty(ship?.HyperjumpViaWormholeUid)) return 1;
            var cfgSettings = GameWorld.Context?.Config?.Settings;
            float perTurn = cfgSettings != null ? cfgSettings.HyperCrossPerTurnDistance : 30f;
            if (perTurn <= 0f) return 1;
            float dist = CalcDistance(from, to);
            int baseTurns = Mathf.CeilToInt(dist / perTurn);
            int delta = Mathf.RoundToInt(StatBus.SumShipCategory(ship, EquipmentCategory.Artefacts, "TurnsDelta"));
            // TODO: когда появится обособленная псевдо-категория Hyperjump.TurnsDelta с прямой
            // ship-агрегацией — заменить SumShipCategory на неё. Пока Гипергенератор кладёт TurnsDelta
            // в собственные Params, они читаются с Artefacts-слотов.
            return Mathf.Max(1, baseTurns + delta);
        }

        public static float CalcDistance(StarData a, StarData b)
        {
            if (a == null || b == null) return 0f;
            float dx = a.Position.x - b.Position.x;
            float dy = a.Position.y - b.Position.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Возвращает точку на краю системы со случайным отклонением:
        /// угол ± JumpEdgeArcSpreadDeg вдоль дуги, радиус × (1 ± JumpEdgeRadialSpread).
        /// Outward-angle (в рад) от центра к этой точке записывается в outwardAngleRad.
        /// </summary>
        private static Vector2 ApplyEdgeSpread(Vector2 dirOutward, float baseRadius, out float outwardAngleRad)
        {
            float baseAngle = Mathf.Atan2(dirOutward.y, dirOutward.x);
            float angleOffset = GameRng.Range(-JumpEdgeArcSpreadDeg, JumpEdgeArcSpreadDeg) * Mathf.Deg2Rad;
            float radiusMul = 1f + GameRng.Range(-JumpEdgeRadialSpread, JumpEdgeRadialSpread);
            outwardAngleRad = baseAngle + angleOffset;
            Vector2 dir = new Vector2(Mathf.Cos(outwardAngleRad), Mathf.Sin(outwardAngleRad));
            return dir * baseRadius * radiusMul;
        }

        /// <summary>
        /// Инициация прыжка: ставит фазу=Travel, рассчитывает HyperjumpEdge на краю системы в направлении цели.
        /// Топливо НЕ списывается — спишется при входе в портал (HyperEnter→HyperExit).
        /// Точка прибытия (HyperjumpArrivalEdge) фиксируется сразу — чтобы портал на дальней стороне
        /// мог открыться синхронно с HyperEnter, без необходимости его пересчитывать на середине.
        /// </summary>
        public static bool RequestJump(ShipData ship, StarData target, GalaxyData galaxy)
        {
            if (!CanRequestJump(ship, target, galaxy, out string reason))
            {
                Debug.LogWarning($"[Hyperjump] {ship?.Name ?? "?"}: запрос отклонён — {reason}");
                return false;
            }

            if (!galaxy.StarsMap.TryGetValue(ship.CurrentStarUid, out var fromStar)) return false;

            Vector2 toTarget = target.Position - fromStar.Position;
            if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector2.right;
            Vector2 dirOut = toTarget.normalized;

            float sysRadius = SRUnits.ToWorld(fromStar.SystemSize) * SystemEdgeRadiusMul;
            ship.HyperjumpTargetStarUid = target.Uid;
            ship.HyperjumpFromStarUid = fromStar.Uid;
            ship.HyperjumpPhase = HyperjumpPhase.Travel;
            ship.HyperjumpEdge = ApplyEdgeSpread(dirOut, sysRadius, out float departAngle);
            ship.HyperjumpHeading = departAngle;

            // Точку выхода считаем сразу — портал на стороне цели должен иметь координаты ДО HyperEnter,
            // чтобы стороннему наблюдателю в системе цели было где открыть портал на ходе 1.
            Vector2 toFrom = fromStar.Position - target.Position;
            if (toFrom.sqrMagnitude < 0.0001f) toFrom = Vector2.right;
            Vector2 dirOutArrival = toFrom.normalized;
            float targetSysRadius = SRUnits.ToWorld(target.SystemSize) * SystemEdgeRadiusMul;
            ship.HyperjumpArrivalEdge = ApplyEdgeSpread(dirOutArrival, targetSysRadius, out float arrivalOutwardAngle);
            // Курс прибытия — внутрь системы (= outward + 180°).
            // ВНИМАНИЕ: сюда не пишем сейчас — HyperjumpHeading нужен для исходного портала.
            // Сохраним arrival-heading отдельно — он применяется при миграции (см. CompleteJump).
            ship.HyperjumpArrivalHeading = arrivalOutwardAngle + Mathf.PI;

            Debug.Log($"[Hyperjump] {ship.Name}: {fromStar.Name} → {target.Name} (фаза Travel, " +
                      $"вход {ship.HyperjumpEdge:F1}, выход {ship.HyperjumpArrivalEdge:F1})");
            return true;
        }

        /// <summary>
        /// Инициация прыжка через ЧЕРВОТОЧИНУ. В отличие от обычного гиперпрыжка:
        ///   • топливо не тратится и не проверяется;
        ///   • дистанция не ограничена JumpRange (червоточина сама выбрала цель за пределами);
        ///   • HyperjumpEdge = позиция червоточины (корабль долетит до неё как до обычной точки);
        ///   • HyperjumpArrivalEdge = случайная точка внутри целевой системы (а не на краю).
        /// Дальнейшая последовательность фаз (Travel → HyperEnter → HyperExit) идёт стандартно.
        /// </summary>
        public static bool RequestJumpViaWormhole(ShipData ship, WormholeData wormhole, StarData sourceStar, GalaxyData galaxy)
        {
            if (ship == null || wormhole == null || sourceStar == null || galaxy == null) return false;
            if (ship.CurrentHull <= 0) return false;
            if (ship.HyperjumpPhase != HyperjumpPhase.None) return false;
            if (!string.IsNullOrEmpty(ship.LandedOnShipUid)) return false;
            if (wormhole.Phase != WormholePhase.Open) return false;
            if (!string.IsNullOrEmpty(wormhole.TargetGalaxyId))
            {
                // Мультигалактический runtime не реализован — червоточина-межгалактика в текущей
                // версии не проходима. Данные сохраняются, поле готово к использованию в будущем.
                Debug.LogWarning($"[Wormhole] {ship.Name}: межгалактический прыжок (target galaxy " +
                                 $"{wormhole.TargetGalaxyId}) не поддержан в этой версии — вход отклонён.");
                return false;
            }
            if (!galaxy.StarsMap.TryGetValue(wormhole.TargetStarUid, out var target)) return false;

            ship.HyperjumpTargetStarUid = target.Uid;
            ship.HyperjumpFromStarUid = sourceStar.Uid;
            ship.HyperjumpPhase = HyperjumpPhase.Travel;
            ship.HyperjumpEdge = wormhole.Position;
            ship.HyperjumpViaWormholeUid = wormhole.Uid;

            // Курс наружу — в направлении из позиции корабля к червоточине (нужен для визуала входа).
            Vector2 dirIn = wormhole.Position - ship.Position;
            if (dirIn.sqrMagnitude < 0.0001f) dirIn = Vector2.right;
            ship.HyperjumpHeading = Mathf.Atan2(dirIn.y, dirIn.x);

            // Точка выхода: приоритет — позиция парной червоточины (спавн создаёт её сразу
            // рядом с источником). Fallback — FixedTargetPosition, а если и её нет — случайная
            // в 30..80% радиуса (для старых сейвов/скриптов без пары).
            Vector2 arrivalPos;
            var pairedWormhole = !string.IsNullOrEmpty(wormhole.PairedWormholeUid)
                ? WormholeService.Find(wormhole.PairedWormholeUid)
                : null;
            if (pairedWormhole != null)
            {
                arrivalPos = pairedWormhole.Position;
            }
            else if (wormhole.FixedTargetPosition.HasValue)
            {
                arrivalPos = wormhole.FixedTargetPosition.Value;
            }
            else
            {
                float tgtRadius = SRUnits.ToWorld(target.SystemSize) * SystemEdgeRadiusMul;
                float arrR = GameRng.Range(tgtRadius * 0.3f, tgtRadius * 0.8f);
                float arrAng = GameRng.Range(0f, Mathf.PI * 2f);
                arrivalPos = new Vector2(Mathf.Cos(arrAng) * arrR, Mathf.Sin(arrAng) * arrR);
            }
            // Курс прибытия — от точки выхода к центру системы (чтобы корабль после fade-in
            // визуально выехал вглубь). Если выход строго в центре — берём arbitrary right.
            Vector2 toCenter = -arrivalPos;
            ship.HyperjumpArrivalHeading = toCenter.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(toCenter.y, toCenter.x)
                : 0f;
            ship.HyperjumpArrivalEdge = arrivalPos;

            Debug.Log($"[Wormhole] {ship.Name}: вход в червоточину {wormhole.Uid[..6]}: " +
                      $"{sourceStar.Name} → {target.Name}, выход {ship.HyperjumpArrivalEdge:F1}.");
            return true;
        }

        /// <summary>
        /// Отмена прыжка. Разрешена в Travel (мгновенно None) и HyperEnter (на следующем ходе сыграется end).
        /// В HyperExit — игнорируется.
        /// </summary>
        public static bool CancelJump(ShipData ship)
        {
            if (ship == null) return false;
            switch (ship.HyperjumpPhase)
            {
                case HyperjumpPhase.Travel:
                    ship.HyperjumpPhase = HyperjumpPhase.None;
                    ship.HyperjumpTargetStarUid = null;
                    ship.HyperjumpViaWormholeUid = null;
                    Debug.Log($"[Hyperjump] {ship.Name}: отмена в Travel.");
                    return true;
                case HyperjumpPhase.HyperEnter:
                    ship.HyperjumpPhase = HyperjumpPhase.CancelEnd;
                    Debug.Log($"[Hyperjump] {ship.Name}: отмена в HyperEnter → CancelEnd (закрытие портала).");
                    return true;
                case HyperjumpPhase.CancelEnd:
                    // Уже идёт закрытие — повторная отмена прерывает анимацию и сразу освобождает корабль.
                    ship.HyperjumpPhase = HyperjumpPhase.None;
                    ship.HyperjumpTargetStarUid = null;
                    ship.HyperjumpViaWormholeUid = null;
                    Debug.Log($"[Hyperjump] {ship.Name}: повторная отмена в CancelEnd → None.");
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Прокладка нового курса игроком должна отменить прыжок, если он в фазе Travel или CancelEnd.
        /// Возвращает true если что-то отменилось.
        /// </summary>
        public static bool CancelOnRouteChange(ShipData ship)
        {
            if (ship == null) return false;
            if (ship.HyperjumpPhase == HyperjumpPhase.Travel
                || ship.HyperjumpPhase == HyperjumpPhase.CancelEnd)
            {
                ship.HyperjumpPhase = HyperjumpPhase.None;
                ship.HyperjumpTargetStarUid = null;
                ship.HyperjumpViaWormholeUid = null;
                Debug.Log($"[Hyperjump] {ship.Name}: курс изменён игроком — прыжок отменён.");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Готовит корабль к движению/визуалу в этом ходу в зависимости от фазы.
        /// Должен вызываться в StarNextDay ДО прохода по subTurn-ам.
        /// </summary>
        public static void PrepareTurn(ShipData ship, StarData star)
        {
            switch (ship.HyperjumpPhase)
            {
                case HyperjumpPhase.Travel:
                {
                    // Травеллинг: курс прямо на портал. Никаких "коридорных" approachStart-ов —
                    // кинематика BuildPath сама красиво подведёт корабль с разворотом на дугу.
                    ship.TargetPosition = ship.HyperjumpEdge;
                    ship.TargetQueue.Clear();
                    ShipTrajectory.BuildPath(ship, ship.HyperjumpEdge, star, ship.Waypoints);
                    ship.WaypointIndex = 0;
                    break;
                }
                case HyperjumpPhase.HyperEnter:
                {
                    // Корабль уже на/около HyperjumpEdge (см. транзишн Travel→HyperEnter).
                    // Для гиперпрыжка: цель этого хода — добраться до края + визуальный slide
                    // через портал наружу.
                    // Для червоточины: корабль стоит на червоточине и тонет в ней (fade-out,
                    // без slide) — как посадка на планету. Slide не нужен.
                    bool viaWormhole = !string.IsNullOrEmpty(ship.HyperjumpViaWormholeUid);
                    Vector2 destination;
                    if (viaWormhole)
                    {
                        destination = ship.HyperjumpEdge;
                    }
                    else
                    {
                        Vector2 dir = new Vector2(Mathf.Cos(ship.HyperjumpHeading), Mathf.Sin(ship.HyperjumpHeading));
                        destination = ship.HyperjumpEdge + dir * HyperEnterSlideDistance;
                    }
                    ship.TargetPosition = destination;
                    ship.TargetQueue.Clear();
                    ship.Waypoints.Clear();
                    ship.WaypointIndex = 0;
                    // Гарантированно покрываем путь до края + slide за один ход.
                    float distToDest = Vector2.Distance(ship.Position, destination);
                    ship._turnCachedSpeed = Mathf.Max(distToDest, viaWormhole ? 0.01f : HyperEnterSlideDistance) * 100f;
                    break;
                }
                case HyperjumpPhase.CancelEnd:
                    // Корабль стоит у портала, портал закрывается через end.
                    ship.Position = ship.HyperjumpEdge;
                    ship.TargetPosition = ship.HyperjumpEdge;
                    ship.TargetQueue.Clear();
                    ship.Waypoints.Clear();
                    ship.WaypointIndex = 0;
                    break;
                case HyperjumpPhase.HyperArrive:
                    // Корабль уже мигрировал в целевую систему (см. CompleteJump). За этот ход
                    // он ДОЛЖЕН стоять неподвижно у HyperjumpArrivalEdge — параллельно на этой
                    // точке проигрывается begin+mid портала. Игрок только что видел scene swap
                    // и камеру, наведённую на ArrivalEdge; корабль пока невидим (см. ShipVisualController).
                    ship.Position = ship.HyperjumpArrivalEdge;
                    ship.PreviousPosition = ship.HyperjumpArrivalEdge;
                    ship.TargetPosition = ship.HyperjumpArrivalEdge;
                    ship.TargetQueue.Clear();
                    ship.Waypoints.Clear();
                    ship.WaypointIndex = 0;
                    break;
                case HyperjumpPhase.HyperExit:
                {
                    // Корабль в новой системе у HyperjumpArrivalEdge. AI на конце прошлого хода уже
                    // выставил TargetPosition (либо игрок при ручном управлении). Строим обычный путь:
                    // ship СРАЗУ ЛЕТИТ к цели, без простоя.
                    Vector2 target = ship.TargetPosition;
                    if ((target - ship.HyperjumpArrivalEdge).sqrMagnitude < 0.01f)
                    {
                        // Цель не задана — небольшое скольжение вглубь системы, чтобы корабль
                        // визуально выехал из портала (после этого ИИ возьмётся на следующем ходу).
                        Vector2 dir = new Vector2(Mathf.Cos(ship.HyperjumpHeading), Mathf.Sin(ship.HyperjumpHeading));
                        target = ship.HyperjumpArrivalEdge + dir * Mathf.Max(ship.SpriteWorldSize, 1f);
                        ship.TargetPosition = target;
                    }
                    ship.TargetQueue.Clear();
                    ShipTrajectory.BuildPath(ship, target, star, ship.Waypoints);
                    ship.WaypointIndex = 0;
                    break;
                }
            }
        }

        /// <summary>
        /// Завершает ход — проверяет переходы между фазами после движения.
        /// Должен вызываться в StarNextDay ПОСЛЕ всех subTurn-ов.
        /// </summary>
        public static void FinalizeTurn(ShipData ship, StarData star, GalaxyData galaxy)
        {
            switch (ship.HyperjumpPhase)
            {
                case HyperjumpPhase.Travel:
                {
                    // Travel→HyperEnter когда корабль реально добрался до края.
                    // (Раньше переход случался за 1 ход до края, и BeginOut/MidOut шли отдельно.
                    // Теперь обе старые фазы объединены в HyperEnter — 1 ход.)
                    float distToEdge = Vector2.Distance(ship.Position, ship.HyperjumpEdge);
                    float speedPerTurn = ShipTrajectory.EffectiveSpeedPerTurn(ship);
                    float arrivalEps = Mathf.Max(EdgeArrivalToleranceMin, speedPerTurn * EdgeArrivalToleranceFraction);
                    if (distToEdge <= arrivalEps)
                    {
                        // Снэп точно на край и фиксируем курс по HyperjumpHeading — портал откроется
                        // строго в HyperjumpEdge и корабль войдёт в него прямо.
                        ship.Position = ship.HyperjumpEdge;
                        ship.CurrentHeading = ship.HyperjumpHeading;
                        ship.HyperjumpPhase = HyperjumpPhase.HyperEnter;
                        Debug.Log($"[Hyperjump] {ship.Name}: Travel → HyperEnter (на краю системы).");
                    }
                    break;
                }
                case HyperjumpPhase.HyperEnter:
                {
                    // Конец хода 1: миграция в целевую систему. Топливо списывается здесь.
                    CompleteJump(ship, galaxy);
                    break;
                }
                case HyperjumpPhase.HyperArrive:
                {
                    // HyperArrive может длиться N ходов (distance-based). Декрементируем счётчик;
                    // при значении > 1 остаёмся в HyperArrive ещё ход (портал уже открыт, корабль
                    // невидимо стоит у ArrivalEdge). При 1 или 0 (обратная совместимость со старыми
                    // сейвами, где поле не задавалось) — переходим в HyperExit.
                    if (ship.HyperArriveTurnsLeft > 1)
                    {
                        ship.HyperArriveTurnsLeft--;
                        Debug.Log($"[Hyperjump] {ship.Name}: HyperArrive — осталось {ship.HyperArriveTurnsLeft} ход(ов).");
                        break;
                    }
                    ship.HyperArriveTurnsLeft = 0;
                    // Конец фазы прибытия: портал в целевой системе доиграл begin+mid, следующий ход
                    // корабль выходит из него. Тикаем AI-мозг ЗДЕСЬ, чтобы NpcBrain выбрал
                    // активность в новой системе и выставил TargetPosition для HyperExit.
                    ship.HyperjumpPhase = HyperjumpPhase.HyperExit;
                    if (!ship.IsPlayer && ship.Brain != null
                        && galaxy.StarsMap.TryGetValue(ship.CurrentStarUid, out var arriveStar))
                    {
                        ship.Brain.ResetActivity();
                        var ctx = GameWorld.Context;
                        ship.Brain.Tick(ship, arriveStar, ctx);
                    }
                    Debug.Log($"[Hyperjump] {ship.Name}: HyperArrive → HyperExit.");
                    break;
                }
                case HyperjumpPhase.HyperExit:
                    ship.HyperjumpPhase = HyperjumpPhase.None;
                    ship.HyperjumpTargetStarUid = null;
                    ship.HyperjumpFromStarUid = null;
                    ship.HyperjumpViaWormholeUid = null;
                    ship.HyperArriveTurnsLeft = 0;
                    Debug.Log($"[Hyperjump] {ship.Name}: гиперпереход завершён.");
                    break;
                case HyperjumpPhase.CancelEnd:
                    ship.HyperjumpPhase = HyperjumpPhase.None;
                    ship.HyperjumpTargetStarUid = null;
                    ship.HyperjumpViaWormholeUid = null;
                    Debug.Log($"[Hyperjump] {ship.Name}: отмена завершена.");
                    break;
            }
        }

        /// <summary>
        /// HyperEnter → HyperArrive: списать топливо, сменить CurrentStarUid, ввести корабль в точку прибытия.
        /// Физическая миграция между Star.Ships произойдёт в начале следующего хода (MigrateShipsBetweenStars).
        /// AI-приказ тикается позже — при переходе HyperArrive → HyperExit (см. FinalizeTurn).
        /// </summary>
        private static void CompleteJump(ShipData ship, GalaxyData galaxy)
        {
            if (string.IsNullOrEmpty(ship.HyperjumpTargetStarUid)) { ship.HyperjumpPhase = HyperjumpPhase.None; return; }
            if (!galaxy.StarsMap.TryGetValue(ship.HyperjumpTargetStarUid, out var target)) { ship.HyperjumpPhase = HyperjumpPhase.None; return; }
            if (!galaxy.StarsMap.TryGetValue(ship.HyperjumpFromStarUid ?? ship.CurrentStarUid, out var fromStar))
                fromStar = ship.CurrentStar;

            // Червоточина — бесплатный прыжок (не тратим и не проверяем топливо).
            bool viaWormhole = !string.IsNullOrEmpty(ship.HyperjumpViaWormholeUid);
            int cost = viaWormhole ? 0 : CalcFuelCost(fromStar, target);
            if (!viaWormhole && !EquipmentSystem.ConsumeFuel(ship, cost))
            {
                Debug.LogWarning($"[Hyperjump] {ship.Name}: внезапно нет топлива на финализацию ({cost}). Прыжок отменён.");
                ship.HyperjumpPhase = HyperjumpPhase.CancelEnd;
                return;
            }

            // Применяем заранее посчитанный (в RequestJump) курс прибытия — это курс ОТ края НАВСТРЕЧУ
            // центру системы. Корабль выезжает из портала вглубь.
            ship.HyperjumpHeading = ship.HyperjumpArrivalHeading;
            ship.PreviousStarUid = ship.CurrentStarUid;
            // Обновление материализованных списков GalaxyShipCounters — до присваивания нового UID.
            SRG.NpcAI.Spawning.SpawnSystem.CountersFor(galaxy).OnShipMigrated(ship, ship.CurrentStarUid, target.Uid);
            ship.CurrentStarUid = target.Uid;
            ship.CurrentStar = null;        // обновится в MigrateShipsBetweenStars
            ship.Position = ship.HyperjumpArrivalEdge;
            ship.PreviousPosition = ship.HyperjumpArrivalEdge;
            // Корабль весь ход HyperArrive стоит на ArrivalEdge — TargetPosition совпадает
            // с Position, чтобы симуляция не пыталась его двигать. NpcBrain.Tick при переходе
            // HyperArrive → HyperExit (см. FinalizeTurn) поставит настоящую цель в новой системе.
            ship.TargetPosition = ship.HyperjumpArrivalEdge;
            ship.Waypoints?.Clear();
            ship.WaypointIndex = 0;
            ship.TargetQueue?.Clear();
            ship.CurrentHeading = ship.HyperjumpHeading;
            ship.HyperjumpPhase = HyperjumpPhase.HyperArrive;
            // Distance-based HyperArrive: длинные прыжки тратят больше ходов «в гипертоннеле».
            ship.HyperArriveTurnsLeft = CalcHyperArriveTurns(ship, fromStar, target);
            if (ship.IsPlayer && galaxy.VisitedStarUids != null)
                galaxy.VisitedStarUids.Add(target.Uid);

            Debug.Log($"[Hyperjump] {ship.Name}: HyperEnter → HyperArrive в {target.Name} (-{cost} топлива). " +
                      $"Точка прибытия {ship.HyperjumpArrivalEdge:F1}. HyperArrive: {ship.HyperArriveTurnsLeft} ход(ов).");
        }
    }
}
