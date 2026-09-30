using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Combat
{
    /// <summary>
    /// Механика ПОДБОРА предметов (Pickup) грузовым захватом (<c>CargoGrabber</c>).
    ///
    /// Поведение:
    ///   1) <see cref="EnqueuePull"/> добавляет UID цели в <see cref="ShipData.PulledQueue"/>.
    ///   2) <see cref="ProcessPullQueueSubturn"/> каждый субход проверяет, не попала ли цель в
    ///      радиус захвата (<c>CargoGrabber.Range</c>). При попадании ставит <c>PulledByUid</c>
    ///      и <c>PullMode=Pickup</c>; при <see cref="ShipData.AutoPullActive"/>=true маршрут
    ///      буксирующего приостанавливается до завершения подбора.
    ///   3) <see cref="UpdatePullStep"/> каждый субход смещает цель к ЦЕНТРУ буксирующего со
    ///      скоростью, линейной по тиру оборудования (T1 → 10 субходов на 500 веса с MaxRange,
    ///      T10 → 2 субхода).
    ///   4) По достижении центра цель (контейнер) разгружается в инвентарь буксирующего и
    ///      исчезает (CurrentHull=0).
    ///
    /// Hard-cap: если <c>CargoGrabber.Power</c> задан и вес груза превышает его — захват
    /// отказывается тянуть (нужен более мощный CargoGrabber или TowingRig для буксира).
    ///
    /// Не путать с <see cref="TowSystem"/> (буксир на якорь, не разгружается) и
    /// <see cref="BoardingSystem"/> (захват кораблей для обмена оборудованием).
    /// </summary>
    public static class PickupSystem
    {
        /// <summary>Дистанция, при которой считаем что груз дотянут к центру и разгружается.
        /// 0.20 unit ≈ 20 px — корабль на скорости успевает «зацепить» предмет даже при пролёте мимо,
        /// меньшее значение проскакивалось за один субход.</summary>
        private const float PickupArrivalThreshold = 0.20f;

        /// <summary>Найти на корабле работающий грузовой захват (<see cref="EquipmentCategory.CargoGrabber"/>).</summary>
        public static ItemInstance FindCargoGrabber(ShipData ship)
            => EquipmentSystem.FindEquipmentByCategory(ship, EquipmentCategory.CargoGrabber);

        // ── Очередь захвата (PulledQueue) ───────────────────────────────────────

        /// <summary>Добавить uid цели в очередь захвата tug (без дублирования). Возвращает true если добавлено.</summary>
        public static bool EnqueuePull(ShipData tug, string targetUid)
        {
            if (tug == null || string.IsNullOrEmpty(targetUid)) return false;
            if (tug.PulledQueue.Contains(targetUid)) return false;
            tug.PulledQueue.Add(targetUid);
            return true;
        }

        /// <summary>Положить все item-цели в радиусе захвата (CargoGrabber.Range) в очередь tug.
        /// Возвращает кол-во добавленных. Если CargoGrabber отсутствует — 0.</summary>
        public static int EnqueueAllItemsInRadius(ShipData tug, IEnumerable<ShipData> candidates)
        {
            if (tug == null || candidates == null) return 0;
            var grabber = FindCargoGrabber(tug);
            if (grabber == null) return 0;
            float rng = SRUnits.ToWorld(grabber.GetParam("Range", 0f));
            if (rng <= 0f) return 0;
            float r2 = rng * rng;
            int added = 0;
            foreach (var c in candidates)
            {
                if (c == null || !c.IsItem) continue;
                if (c.Uid == tug.Uid) continue;
                if ((c.Position - tug.Position).sqrMagnitude > r2) continue;
                if (EnqueuePull(tug, c.Uid)) added++;
            }
            return added;
        }

        /// <summary>То же что EnqueueAllItemsInRadius, но без проверки радиуса — все item-цели текущей системы.</summary>
        public static int EnqueueAllItemsInSystem(ShipData tug, IEnumerable<ShipData> candidates)
        {
            if (tug == null || candidates == null) return 0;
            int added = 0;
            foreach (var c in candidates)
            {
                if (c == null || !c.IsItem) continue;
                if (c.Uid == tug.Uid) continue;
                if (EnqueuePull(tug, c.Uid)) added++;
            }
            return added;
        }

        /// <summary>Удалить из PulledQueue все uid'ы, которых уже нет в lookup'е (битые/удалённые),
        /// либо у которых CurrentHull&lt;=0. Вызывать в начале субхода/хода.</summary>
        public static void CleanPullQueue(ShipData tug, Dictionary<string, ShipData> uidLookup)
        {
            if (tug?.PulledQueue == null || tug.PulledQueue.Count == 0 || uidLookup == null) return;
            for (int i = tug.PulledQueue.Count - 1; i >= 0; i--)
            {
                string uid = tug.PulledQueue[i];
                if (!uidLookup.TryGetValue(uid, out var s) || s == null || s.CurrentHull <= 0)
                    tug.PulledQueue.RemoveAt(i);
            }
        }

        /// <summary>
        /// Один тик планировщика очереди захвата для tug. Вызывать в начале каждого субхода:
        ///   1) Если уже идёт подбор/буксир этим tug — ничего не делаем.
        ///   2) Иначе ищем в PulledQueue ближайшую цель в радиусе CargoGrabber.
        ///      Если есть → ставим <c>PulledByUid=tug.Uid</c> + <c>PullMode=Pickup</c> и
        ///      ПРИОСТАНАВЛИВАЕМ маршрут tug (сохраняем в AutoPullSuspended*). Корабль
        ///      стоит ровно столько субходов, сколько требуется на подбор.
        ///   3) Если PulledByUid нет, очередь пуста, и AutoPullSuspendedTarget задана — восстанавливаем маршрут.
        /// </summary>
        public static void ProcessPullQueueSubturn(ShipData tug, ItemsConfig equipConfig,
            Dictionary<string, ShipData> uidLookup)
        {
            if (tug == null || tug.CurrentHull <= 0) return;
            if (uidLookup == null) return;
            CleanPullQueue(tug, uidLookup);

            // Если уже что-то тянем — пропустим, дождёмся завершения.
            foreach (var kv in uidLookup)
                if (kv.Value != null && kv.Value.PulledByUid == tug.Uid) return;

            var grabber = FindCargoGrabber(tug);
            if (grabber == null) return;
            float rng = SRUnits.ToWorld(grabber.GetParam("Range", 0f));
            if (rng <= 0f) return;
            float r2 = rng * rng;

            string nearestUid = null;
            float nearestDist2 = float.MaxValue;
            for (int i = 0; i < tug.PulledQueue.Count; i++)
            {
                string uid = tug.PulledQueue[i];
                if (!uidLookup.TryGetValue(uid, out var t) || t == null) continue;
                if (t.CurrentHull <= 0) continue;
                float d2 = (t.Position - tug.Position).sqrMagnitude;
                if (d2 > r2) continue;
                if (d2 < nearestDist2) { nearestDist2 = d2; nearestUid = uid; }
            }

            if (nearestUid != null && uidLookup.TryGetValue(nearestUid, out var target))
            {
                target.PulledByUid = tug.Uid;
                target.PullMode = PullKind.Pickup;
                target.FreezeRoute();
                tug.PulledQueue.Remove(nearestUid);

                SuspendRouteForPickup(tug);
                return;
            }

            if (tug.AutoPullSuspendedTarget.HasValue)
                ResumeRouteAfterPickup(tug);
        }

        /// <summary>Сохраняем оригинальный маршрут tug и «замораживаем» его на месте, чтобы
        /// корабль стоял неподвижно нужное число субходов, пока идёт подбор.</summary>
        private static void SuspendRouteForPickup(ShipData tug)
        {
            if (tug.AutoPullSuspendedTarget.HasValue) return;
            tug.AutoPullSuspendedPath.Clear();
            if (tug.Waypoints != null && tug.WaypointIndex < tug.Waypoints.Count)
                for (int i = tug.WaypointIndex; i < tug.Waypoints.Count; i++)
                    tug.AutoPullSuspendedPath.Add(tug.Waypoints[i]);
            if (tug.TargetQueue != null)
                foreach (var p in tug.TargetQueue) tug.AutoPullSuspendedPath.Add(p);
            tug.AutoPullSuspendedTarget = tug.TargetPosition;

            tug.FreezeRoute();
        }

        /// <summary>Подбор завершён — возвращаем сохранённый маршрут tug.</summary>
        private static void ResumeRouteAfterPickup(ShipData tug)
        {
            if (!tug.AutoPullSuspendedTarget.HasValue) return;
            if (tug.AutoPullSuspendedPath != null && tug.AutoPullSuspendedPath.Count > 0)
            {
                tug.TargetQueue ??= new Queue<Vector2>();
                foreach (var p in tug.AutoPullSuspendedPath) tug.TargetQueue.Enqueue(p);
            }
            tug.TargetPosition = tug.AutoPullSuspendedTarget.Value;
            tug.AutoPullSuspendedPath?.Clear();
            tug.AutoPullSuspendedTarget = null;
        }

        // ── Шаг подбора ────────────────────────────────────────────────────────

        /// <summary>
        /// Один шаг подбора. Вызывается каждый субход для целей с <c>PulledByUid</c> и
        /// <c>PullMode==Pickup</c>. Цель смещается к ЦЕНТРУ буксирующего; по достижении
        /// <see cref="PickupArrivalThreshold"/> разгружается в инвентарь буксирующего и удаляется.
        ///
        /// Скорость захвата — линейная по тиру CargoGrabber: T1 за 10 субходов притягивает 500
        /// ед. веса с максимальной дистанции, T10 — за 2 субхода. Для меньшего веса быстрее.
        /// Если вес груза превышает <c>CargoGrabber.Power</c> — захват отменяется.
        /// </summary>
        public static void UpdatePullStep(ShipData target,
            Dictionary<string, ShipData> uidLookup, int subTurnsPerTurn)
        {
            if (target == null) return;
            if (target.PullMode != PullKind.Pickup) return;
            if (string.IsNullOrEmpty(target.PulledByUid)) return;
            if (!uidLookup.TryGetValue(target.PulledByUid, out var tug) || tug == null || tug.CurrentHull <= 0)
            {
                ClearPullState(target);
                return;
            }
            if (!target.IsItem)
            {
                // Pickup не работает по кораблям — это абордаж/буксир. Защита от мисроутинга.
                ClearPullState(target);
                return;
            }

            var grabber = FindCargoGrabber(tug);
            if (grabber == null) { ClearPullState(target); return; }
            float pullR = SRUnits.ToWorld(grabber.GetParam("Range", 0f));
            if (pullR <= 0f) { ClearPullState(target); return; }
            if ((tug.Position - target.Position).sqrMagnitude > pullR * pullR)
            {
                ClearPullState(target);
                return;
            }

            Vector2 delta = tug.Position - target.Position;
            float dist = delta.magnitude;

            if (dist <= PickupArrivalThreshold)
            {
                int moved = ContainerFactory.UnloadContainerInto(target, tug);
                ClearPullState(target);
                if (moved > 0)
                {
                    GameLog.Add($"[Подбор] {target.Name} → {tug.Name} ({moved} предмет(ов)).");
                    if (!tug.IsPlayer)
                    {
                        // Надеть подобранное оборудование NPC сможет только на СЛЕДУЮЩИЙ ход
                        // (ShipLoadoutService.TickTurn), а перегруз проверяется сразу после подбора.
                        tug.PendingAutoEquipTurn = GameWorld.GeneratedGalaxy?.CurrentTurn ?? 0;
                        ShipLoadoutService.ResolveOverload(tug, tug.CurrentStar);
                    }
                    else if (ShipLoadoutService.IsOverloaded(tug))
                    {
                        GameLog.Add($"[Перегруз] {tug.Name}: корабль перегружен и не может двигаться.");
                    }
                }
                return;
            }

            // Hard-cap по массе.
            float grabberPower = grabber.GetParam("Power", 0f);
            int weight = Mathf.Max(1, target.Inventory?.TotalWeight() ?? 1);
            if (grabberPower > 0f && weight > grabberPower)
            {
                ClearPullState(target);
                GameLog.Add(
                    $"[Подбор] Груз ({weight}) тяжелее силы захвата ({grabberPower:F0}).");
                return;
            }

            int tier = Mathf.Clamp(grabber.TechLevel, 1, 10);
            float subturnsFor500AtMaxRange = Mathf.Lerp(10f, 2f, (tier - 1) / 9f);
            const float ReferenceWeight = 500f;
            float perSubturnWorld = (pullR / subturnsFor500AtMaxRange) * (ReferenceWeight / weight);
            if (perSubturnWorld <= 0.0001f) return;

            // Подбор «на лету»: к собственной скорости pickup добавляем смещение tug за этот
            // субход (в проекции на направление погони). Иначе при быстром tug груз отстаёт
            // и связь рвётся по TickPullDistanceCheck.
            Vector2 dir = delta / dist;
            Vector2 tugStep = tug.Position - tug.PreviousPosition;
            float tugChaseAdd = Mathf.Max(0f, Vector2.Dot(tugStep, dir));
            float step = Mathf.Min(perSubturnWorld + tugChaseAdd, dist);

            target.PreviousPosition = target.Position;
            target.Position += dir * step;
            target.CurrentHeading = Mathf.Atan2(delta.y, delta.x);
        }

        // ── Завершение Pull-состояния ──────────────────────────────────────────

        public static void EndPull(ShipData tug, ShipData target)
        {
            if (target == null) return;
            if (tug != null && target.PulledByUid != tug.Uid) return;
            ClearPullState(target);
        }

        /// <summary>Полный сброс Pull-состояния цели (для отмены, выхода из радиуса, гибели tug, и т.п.).
        /// Используется и подбором, и буксиром, и общими очистками.</summary>
        public static void ClearPullState(ShipData target)
        {
            if (target == null) return;
            target.PulledByUid = null;
            target.PullMode = PullKind.None;
            target.LastPullDistance = -1f;
            target.PullDistanceIncreaseTurns = 0;
        }

        /// <summary>
        /// Конец хода. Для целей с PulledByUid: считает, выросла ли дистанция к источнику.
        /// Два хода подряд роста → подбор/буксир рвётся, Pull сбрасывается, в консоль идёт сообщение.
        /// Возвращает true, если связь была сорвана. Общая логика для Pickup и Tow.
        /// </summary>
        public static bool TickPullDistanceCheck(ShipData target,
            Dictionary<string, ShipData> uidLookup, ItemsConfig equipConfig)
        {
            if (target == null || string.IsNullOrEmpty(target.PulledByUid)) return false;
            if (!uidLookup.TryGetValue(target.PulledByUid, out var tug) || tug == null) return false;

            // Для буксира — дистанция считается до ближайшего ЦЕЛЕВОГО узла (родителя в цепи),
            // иначе при подлёте к концу длинной цепочки дистанция до tug растёт и связь рвётся.
            ShipData reference = tug;
            if (target.PullMode == PullKind.Tow && equipConfig != null)
            {
                var (parent, _) = TowSystem.FindFreeAnchor(tug, equipConfig, uidLookup, forShip: !target.IsItem);
                if (parent != null) reference = parent;
            }
            float dist = (reference.Position - target.Position).magnitude;
            if (target.LastPullDistance < 0f)
            {
                target.LastPullDistance = dist;
                target.PullDistanceIncreaseTurns = 0;
                return false;
            }

            const float Eps = 0.001f;
            if (dist > target.LastPullDistance + Eps)
                target.PullDistanceIncreaseTurns++;
            else
                target.PullDistanceIncreaseTurns = 0;
            target.LastPullDistance = dist;

            if (target.PullDistanceIncreaseTurns >= 2)
            {
                string targetName = target.Name;
                string label = target.PullMode == PullKind.Tow ? "Буксир" : "Подбор";
                ClearPullState(target);
                GameLog.Add(
                    $"[{label}] Связь с {targetName} оборвана — дистанция растёт два хода подряд.");
                return true;
            }
            return false;
        }
    }
}
