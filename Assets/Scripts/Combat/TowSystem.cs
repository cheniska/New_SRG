using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Utils;

namespace SRG.Combat
{
    /// <summary>
    /// Механика буксира (Tow). Корабль с установленной буксировочной установкой (<c>TowingRig</c>)
    /// удерживает прицепленные объекты на якорях <c>HullTypeDef.TowAnchors</c>, формируя «поезд»:
    /// второй буксируемый цепляется к первому, третий — ко второму, и т.д. Корабли крепятся ТОЛЬКО
    /// на Back-якорь, тяжёлые предметы — на любой свободный якорь по приоритету <c>TowOrder</c>.
    /// Шаг «приближение к якорю» — PD-controller (критическое демпфирование) внутри субходов.
    ///
    /// Не путать с <see cref="PickupSystem"/> (подбор лёгких контейнеров CargoGrabber'ом —
    /// разовая операция, контейнер уходит в инвентарь) и <see cref="BoardingSystem"/> (абордаж).
    /// </summary>
    public static class TowSystem
    {
        /// <summary>Относительное смещение «привязки» буксируемого корабля относительно якоря буксирующего.</summary>
        private const float TowedShipFollowDistance = 0.6f;
        /// <summary>Расстояние, на котором буксируемый считается «достигнутым» и переходит из Pull в Tow.</summary>
        public const float TowAttachRadius = 1.5f;
        /// <summary>Дистанция, при которой считаем что буксируемый «сел» на якорь (для AttachToAnchor).</summary>
        public const float TowSettleThreshold = 0.08f;
        /// <summary>
        /// Натуральная частота PD-демпфера. ω·dt должно быть «не очень большим» для устойчивости
        /// явного Эйлера (dt = 1/SubTurnsPerTurn = 0.1, ω·dt = 0.5).
        /// </summary>
        private const float TowSpringOmega = 5.0f;

        // ── Поиск оборудования ─────────────────────────────────────────────────

        public static ItemInstance FindTowingRig(ShipData ship)
            => EquipmentSystem.FindEquipmentByCategory(ship, EquipmentCategory.TowingRig);

        /// <summary>Способна ли данная установка буксировать станции. Флаг задаётся в
        /// ItemsConfig на уровне тира (параметр <c>CanTowStations</c>, 1 = да).</summary>
        public static bool CanRigTowStations(ItemInstance rig)
            => rig != null && rig.GetParam("CanTowStations", 0f) >= 0.5f;

        // ── Ручная команда буксировки ──────────────────────────────────────────

        public static bool CanTow(ShipData tug, ShipData targetOrItem, ItemsConfig equipConfig, out string reason)
        {
            reason = null;
            if (tug == null || targetOrItem == null) { reason = "Нет буксирующего или цели."; return false; }
            if (!string.IsNullOrEmpty(targetOrItem.TowedByUid)) { reason = "Цель уже на буксире."; return false; }
            if (!string.IsNullOrEmpty(targetOrItem.PulledByUid)) { reason = "Цель уже притягивается."; return false; }
            if (!targetOrItem.IsItem && !string.IsNullOrEmpty(targetOrItem.BoardedByUid)) { reason = "Цель в абордаже."; return false; }
            if (!targetOrItem.IsItem && targetOrItem.CurrentHull <= 0) { reason = "Корабль-цель уничтожен."; return false; }
            var rig = FindTowingRig(tug);
            if (rig == null) { reason = "Нет работающей буксировочной установки."; return false; }
            if (targetOrItem.IsStation && !CanRigTowStations(rig))
            { reason = "Эта установка не рассчитана на буксировку станций."; return false; }
            int max = Mathf.Max(2, Mathf.RoundToInt(rig.GetParam("MaxTowed", 2f)));
            // Лимит MaxTowed считается по ВСЕМУ дереву буксира (включая цепочки).
            if (CountTowedSubtreeFromDirectList(tug) >= max) { reason = "Достигнут лимит буксируемых объектов."; return false; }
            if (HullAnchorMath.GetHullTypeDef(tug, equipConfig)?.TowAnchors == null) { reason = "Корпус не задаёт точек буксировки."; return false; }
            float pullR = SRUnits.ToWorld(rig.GetParam("PullRadius", 0f));
            float distSq = (targetOrItem.Position - tug.Position).sqrMagnitude;
            if (distSq > pullR * pullR) { reason = "Цель вне радиуса буксира."; return false; }
            return true;
        }

        /// <summary>
        /// Если цель близко (≤ <see cref="TowAttachRadius"/>) — крепит сразу. Иначе включает «притягивание»
        /// в режиме <see cref="PullKind.Tow"/>: каждый субход цель будет смещаться к якорю буксирующего
        /// со скоростью не более target.Speed/2 (для кораблей) или линейной по тиру (для предметов).
        /// </summary>
        public static bool TryTow(ShipData tug, ShipData targetOrItem, ItemsConfig equipConfig,
            Dictionary<string, ShipData> uidLookup = null)
        {
            if (!CanTow(tug, targetOrItem, equipConfig, out _)) return false;
            float dist = (targetOrItem.Position - tug.Position).magnitude;
            if (dist <= TowAttachRadius)
                return AttachToAnchor(tug, targetOrItem, equipConfig, uidLookup);

            // Притягивание: цель замораживает свой маршрут, но не прицеплена. Состояние длится несколько субходов.
            targetOrItem.PulledByUid = tug.Uid;
            targetOrItem.PullMode = PullKind.Tow;
            targetOrItem.FreezeRoute();
            return true;
        }

        /// <summary>
        /// Один шаг буксирного притягивания. Вызывается каждый субход для целей с
        /// <c>PulledByUid</c> и <c>PullMode==Tow</c>. Цель смещается к свободному ЯКОРЮ буксирующего;
        /// по достижении <see cref="TowSettleThreshold"/> крепится через <see cref="AttachToAnchor"/>.
        /// Не разгружается в инвентарь — для подбора используйте <see cref="PickupSystem"/>.
        /// </summary>
        public static void UpdateTowPullStep(ShipData target, ItemsConfig equipConfig,
            Dictionary<string, ShipData> uidLookup, int subTurnsPerTurn)
        {
            if (target == null) return;
            if (target.PullMode != PullKind.Tow) return;
            if (string.IsNullOrEmpty(target.PulledByUid)) return;
            if (!uidLookup.TryGetValue(target.PulledByUid, out var tug) || tug == null || tug.CurrentHull <= 0)
            {
                PickupSystem.ClearPullState(target);
                return;
            }

            var rig = FindTowingRig(tug);
            if (rig == null) { PickupSystem.ClearPullState(target); return; }
            float pullR = SRUnits.ToWorld(rig.GetParam("PullRadius", 0f));
            if (pullR <= 0f) { PickupSystem.ClearPullState(target); return; }
            if ((tug.Position - target.Position).sqrMagnitude > pullR * pullR)
            {
                PickupSystem.ClearPullState(target);
                return;
            }

            var (parent, anchorKey) = FindFreeAnchor(tug, equipConfig, uidLookup, forShip: !target.IsItem);
            if (parent == null || anchorKey == null) { PickupSystem.ClearPullState(target); return; }
            var parentDef = HullAnchorMath.GetHullTypeDef(parent, equipConfig);
            if (parentDef == null || !parentDef.TowAnchors.TryGetValue(anchorKey, out var localAnchor))
            { PickupSystem.ClearPullState(target); return; }

            Vector2 anchorWorld = HullAnchorMath.LocalToWorldAt(parent.Position, parent.CurrentHeading, localAnchor);
            Vector2 desired = ComputeChainAttachPos(parent, target, anchorWorld);
            Vector2 delta = desired - target.Position;
            float dist = delta.magnitude;

            if (dist <= TowSettleThreshold)
            {
                if (!AttachToAnchor(tug, target, equipConfig, uidLookup))
                    PickupSystem.ClearPullState(target);
                return;
            }

            // Скорость: для предметов — линейная по тиру TowingRig (та же формула, что у захвата);
            // для кораблей — собственная подвижность / 2.
            float perSubturnWorld;
            if (target.IsItem)
            {
                int weight = Mathf.Max(1, target.Inventory?.TotalWeight() ?? 1);
                int tier = Mathf.Clamp(rig.TechLevel, 1, 10);
                float subturnsFor500AtMaxRange = Mathf.Lerp(10f, 2f, (tier - 1) / 9f);
                const float ReferenceWeight = 500f;
                perSubturnWorld = (pullR / subturnsFor500AtMaxRange) * (ReferenceWeight / weight);
            }
            else
            {
                float ownSpeed = EquipmentSystem.CalculateOwnEngineSpeed(target);
                float perTurnWorld = SRUnits.ToWorld(ownSpeed * 0.5f);
                perSubturnWorld = perTurnWorld / Mathf.Max(1, subTurnsPerTurn);
            }
            if (perSubturnWorld <= 0.0001f) return;

            float step = Mathf.Min(perSubturnWorld, dist);
            target.PreviousPosition = target.Position;
            target.Position += (delta / dist) * step;
            target.CurrentHeading = Mathf.Atan2(delta.y, delta.x);
        }

        /// <summary>
        /// Крепит объект на свободный якорь дерева буксира (или его потомков). Корабли — только Back,
        /// предметы — любой свободный по <c>TowOrder</c>. Возвращает true, если объект был прицеплен.
        /// </summary>
        public static bool AttachToAnchor(ShipData tug, ShipData target,
            ItemsConfig equipConfig, Dictionary<string, ShipData> uidLookup)
        {
            var (parent, anchorKey) = FindFreeAnchor(tug, equipConfig, uidLookup, forShip: !target.IsItem);
            if (parent == null) return false;

            parent.TowedObjectUids.Add(target.Uid);
            target.TowParentUid = parent.Uid;
            target.TowAnchorKey = anchorKey;
            target.TowedByUid = tug.Uid;
            target.PulledByUid = null;
            target.PullMode = PullKind.None;
            target.LastPullDistance = -1f;
            target.PullDistanceIncreaseTurns = 0;
            // Как только Pull завершён — корабль/предмет крепко на буксире и двигается со скоростью буксирующего.
            target.IsTowSettled = true;
            target.TowVelocity = Vector2.zero;
            target.FreezeRoute();
            return true;
        }

        /// <summary>
        /// Ищет свободный якорь в дереве для нового буксируемого.
        /// — Корабль (forShip=true): только Back-якорь на каждом узле; если Back занят, спуск в Back-ребёнка.
        /// — Предмет (forShip=false): любой свободный якорь по TowOrder; при полной занятости — спуск в Back.
        /// Возвращает (parent, anchorKey) либо (null, null), если свободных нет.
        /// </summary>
        public static (ShipData parent, string anchorKey) FindFreeAnchor(
            ShipData node, ItemsConfig equipConfig, Dictionary<string, ShipData> uidLookup, bool forShip)
        {
            if (node == null) return (null, null);
            var def = HullAnchorMath.GetHullTypeDef(node, equipConfig);
            if (def?.TowAnchors == null || def.TowAnchors.Count == 0) return (null, null);
            var order = def.TowOrder ?? new List<string>(def.TowAnchors.Keys);
            if (order.Count == 0) return (null, null);

            // Соберём занятые якоря этого узла.
            var occupied = new Dictionary<string, ShipData>();
            if (uidLookup != null)
            {
                foreach (var uid in node.TowedObjectUids)
                    if (uidLookup.TryGetValue(uid, out var c) && c != null && !string.IsNullOrEmpty(c.TowAnchorKey))
                        occupied[c.TowAnchorKey] = c;
            }
            string backKey = order[0];

            if (forShip)
            {
                if (!occupied.ContainsKey(backKey)) return (node, backKey);
                if (occupied.TryGetValue(backKey, out var backChild) && backChild != null)
                    return FindFreeAnchor(backChild, equipConfig, uidLookup, forShip);
                return (null, null);
            }

            foreach (var key in order)
                if (!occupied.ContainsKey(key)) return (node, key);
            if (occupied.TryGetValue(backKey, out var backNode) && backNode != null)
                return FindFreeAnchor(backNode, equipConfig, uidLookup, forShip);
            return (null, null);
        }

        public static void ReleaseTow(ShipData tug, ShipData towed,
            Dictionary<string, ShipData> uidLookup = null,
            ItemsConfig equipConfig = null)
        {
            if (towed == null) return;
            ShipData parent = null;
            if (uidLookup != null && !string.IsNullOrEmpty(towed.TowParentUid))
                uidLookup.TryGetValue(towed.TowParentUid, out parent);
            if (parent == null) parent = tug;
            parent?.TowedObjectUids.Remove(towed.Uid);

            DetachSubtree(towed, uidLookup);

            if (parent != null && equipConfig != null && uidLookup != null)
                RepackChildren(parent, equipConfig, uidLookup);
        }

        /// <summary>Отцепляет узел и всё его поддерево, очищая поля Tow*.</summary>
        public static void DetachSubtree(ShipData root, Dictionary<string, ShipData> uidLookup)
        {
            if (root == null) return;
            if (uidLookup != null)
                foreach (var d in EnumerateTowedSubtreeInclusive(root, uidLookup))
                {
                    d.TowedByUid = null;
                    d.TowParentUid = null;
                    d.TowAnchorKey = null;
                    d.IsTowSettled = false;
                    d.TowedObjectUids.Clear();
                }
            else
            {
                root.TowedByUid = null;
                root.TowParentUid = null;
                root.TowAnchorKey = null;
                root.IsTowSettled = false;
                root.TowedObjectUids.Clear();
            }
        }

        private static IEnumerable<ShipData> EnumerateTowedSubtreeInclusive(
            ShipData root, Dictionary<string, ShipData> uidLookup)
        {
            if (root == null) yield break;
            var stack = new Stack<ShipData>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                foreach (var uid in n.TowedObjectUids)
                    if (uidLookup.TryGetValue(uid, out var c) && c != null) stack.Push(c);
            }
        }

        /// <summary>Все потомки tug по дереву буксира (без самого tug).</summary>
        public static IEnumerable<ShipData> EnumerateTowedDescendants(
            ShipData tug, Dictionary<string, ShipData> uidLookup)
        {
            if (tug == null) yield break;
            var stack = new Stack<ShipData>();
            foreach (var uid in tug.TowedObjectUids)
                if (uidLookup.TryGetValue(uid, out var c) && c != null) stack.Push(c);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                foreach (var uid in n.TowedObjectUids)
                    if (uidLookup.TryGetValue(uid, out var c) && c != null) stack.Push(c);
            }
        }

        /// <summary>Считает все вершины поддерева буксируемых ниже tug (грубая оценка без lookup).</summary>
        private static int CountTowedSubtreeFromDirectList(ShipData node)
        {
            if (node?.TowedObjectUids == null) return 0;
            return node.TowedObjectUids.Count;
        }

        /// <summary>Точный подсчёт поддерева включая цепочку (требует lookup).</summary>
        public static int CountTowedSubtree(ShipData root, Dictionary<string, ShipData> uidLookup)
        {
            if (root?.TowedObjectUids == null || uidLookup == null) return 0;
            int total = 0;
            var stack = new Stack<ShipData>();
            foreach (var uid in root.TowedObjectUids)
                if (uidLookup.TryGetValue(uid, out var c) && c != null) stack.Push(c);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                total++;
                foreach (var uid in n.TowedObjectUids)
                    if (uidLookup.TryGetValue(uid, out var c) && c != null) stack.Push(c);
            }
            return total;
        }

        /// <summary>
        /// «Уплотнение»: после удаления одного из якорей у parent остальные дети сдвигаются
        /// по TowOrder, занимая освободившиеся якоря (Back ← Right ← Left ← Front). У сместившихся
        /// кораблей сбрасывается IsTowSettled, чтобы они плавно подъехали к новому якорю.
        /// </summary>
        private static void RepackChildren(ShipData parent, ItemsConfig equipConfig,
            Dictionary<string, ShipData> uidLookup)
        {
            var def = HullAnchorMath.GetHullTypeDef(parent, equipConfig);
            if (def?.TowAnchors == null) return;
            var order = def.TowOrder ?? new List<string>(def.TowAnchors.Keys);
            if (order.Count == 0) return;

            var children = new List<ShipData>();
            foreach (var uid in parent.TowedObjectUids)
                if (uidLookup.TryGetValue(uid, out var c) && c != null) children.Add(c);
            children.Sort((a, b) =>
            {
                int ia = order.IndexOf(a.TowAnchorKey); if (ia < 0) ia = int.MaxValue;
                int ib = order.IndexOf(b.TowAnchorKey); if (ib < 0) ib = int.MaxValue;
                return ia.CompareTo(ib);
            });

            for (int i = 0; i < children.Count && i < order.Count; i++)
            {
                string newKey = order[i];
                if (children[i].TowAnchorKey != newKey)
                    children[i].TowAnchorKey = newKey;
            }
        }

        // ── Каждосубходный апдейт цепочки ──────────────────────────────────────

        /// <summary>
        /// Каждый субход вызывается для корня дерева буксира (tug). Рекурсивно обходит дерево,
        /// для каждого ребёнка считает позицию у своего родителя по TowAnchorKey, плавно «садится»
        /// (PD-controller). Должна вызываться только для корней (tug.TowedByUid == null) —
        /// иначе дочерние узлы обрабатываются повторно.
        /// </summary>
        public static void UpdateTowChain(ShipData tug, ItemsConfig equipConfig,
            Dictionary<string, ShipData> uidLookup, int subTurnsPerTurn)
        {
            if (tug == null) return;
            if (!string.IsNullOrEmpty(tug.TowedByUid)) return;
            UpdateTowNode(tug, equipConfig, uidLookup, subTurnsPerTurn);
        }

        private static void UpdateTowNode(ShipData parent, ItemsConfig equipConfig,
            Dictionary<string, ShipData> uidLookup, int subTurnsPerTurn)
        {
            if (parent?.TowedObjectUids == null || parent.TowedObjectUids.Count == 0) return;
            var def = HullAnchorMath.GetHullTypeDef(parent, equipConfig);
            if (def?.TowAnchors == null) return;

            foreach (var uid in parent.TowedObjectUids)
            {
                if (!uidLookup.TryGetValue(uid, out var towed) || towed == null) continue;
                string key = towed.TowAnchorKey;
                if (string.IsNullOrEmpty(key) || !def.TowAnchors.TryGetValue(key, out var localAnchor))
                    continue;
                Vector2 anchorWorld = HullAnchorMath.LocalToWorldAt(parent.Position, parent.CurrentHeading, localAnchor);
                Vector2 desired = ComputeChainAttachPos(parent, towed, anchorWorld);
                ApplyTowedPosition(parent, towed, desired, subTurnsPerTurn);
                UpdateTowNode(towed, equipConfig, uidLookup, subTurnsPerTurn);
            }
        }

        /// <summary>
        /// Точка, где должен «сидеть» буксируемый: предмет — на якоре, корабль — чуть назад по линии
        /// «prev → anchor». Доступна другим системам (PickupSystem.UpdatePullStep), чтобы плавно
        /// подвести притягиваемого к финальному положению на цепи.
        /// </summary>
        public static Vector2 ComputeChainAttachPos(ShipData prev, ShipData towed, Vector2 anchor)
        {
            if (towed.IsItem) return anchor;
            Vector2 dir = prev.Position - anchor;
            if (dir.sqrMagnitude < 1e-6f) return anchor;
            return anchor - dir.normalized * TowedShipFollowDistance;
        }

        private static void ApplyTowedPosition(ShipData prev, ShipData towed, Vector2 desired, int subTurnsPerTurn)
        {
            // Физика «груз на привязи в невесомости» — критически демпфированная пружина (PD-controller):
            //   a = ω² · (desired - x) - 2ω · v
            towed.PreviousPosition = towed.Position;
            float dt = 1.0f / Mathf.Max(1, subTurnsPerTurn);
            Vector2 delta = desired - towed.Position;
            Vector2 v = towed.TowVelocity;
            Vector2 acc = TowSpringOmega * TowSpringOmega * delta - 2f * TowSpringOmega * v;
            v += acc * dt;
            towed.TowVelocity = v;
            towed.Position += v * dt;
            towed.IsTowSettled = true;
            towed.CurrentHeading = prev.CurrentHeading;
        }

        // ── Плотный render-путь буксируемых для красивой анимации ──────────────

        /// <summary>
        /// После завершения симуляции хода: для каждой буксирующей цепочки строит плотный render-путь
        /// буксируемого объекта вдоль пути буксира. Без этого визуал интерполируется по 11 кадрам субходов
        /// и кажется «рваным», особенно на криволинейных траекториях.
        /// </summary>
        public static void RebuildTowedRenderPaths(
            ShipData tug, ItemsConfig equipConfig,
            Dictionary<string, ShipData> uidLookup,
            TurnAnimationData anim)
        {
            if (tug == null) return;
            if (!string.IsNullOrEmpty(tug.TowedByUid)) return; // не корень — обработается выше
            if (anim == null) return;
            if (!anim.ShipRenderPaths.TryGetValue(tug.Uid, out var rootPath)) return;
            if (rootPath == null || rootPath.Count < 2) return;
            RebuildNodeChildren(tug, rootPath, equipConfig, uidLookup, anim);
        }

        private static void RebuildNodeChildren(ShipData parent, List<Vector2> parentPath,
            ItemsConfig equipConfig, Dictionary<string, ShipData> uidLookup, TurnAnimationData anim)
        {
            if (parent?.TowedObjectUids == null || parent.TowedObjectUids.Count == 0) return;
            var def = HullAnchorMath.GetHullTypeDef(parent, equipConfig);
            if (def?.TowAnchors == null) return;

            foreach (var uid in parent.TowedObjectUids)
            {
                if (!uidLookup.TryGetValue(uid, out var towed) || towed == null) continue;
                if (!towed.IsTowSettled) continue;
                string key = towed.TowAnchorKey;
                if (string.IsNullOrEmpty(key) || !def.TowAnchors.TryGetValue(key, out var localAnchor)) continue;

                var towedPath = towed.RenderPath;
                towedPath.Clear();
                anim.ShipRenderPaths[towed.Uid] = towedPath;

                for (int k = 0; k < parentPath.Count; k++)
                {
                    Vector2 p = parentPath[k];
                    float heading = HullAnchorMath.ComputeHeadingAt(parentPath, k, parent.CurrentHeading);
                    Vector2 anchor = HullAnchorMath.LocalToWorldAt(p, heading, localAnchor);
                    Vector2 finalPos;
                    if (towed.IsItem) finalPos = anchor;
                    else
                    {
                        Vector2 dir = p - anchor;
                        finalPos = dir.sqrMagnitude < 1e-6f
                            ? anchor
                            : anchor - dir.normalized * TowedShipFollowDistance;
                    }
                    towedPath.Add(finalPos);
                }

                RebuildNodeChildren(towed, towedPath, equipConfig, uidLookup, anim);
            }
        }

        /// <summary>Возвращает мировые координаты якоря по индексу буксируемого (0..N-1).</summary>
        public static Vector2 GetTowAnchorWorld(ShipData tug, int index, ItemsConfig equipConfig)
        {
            var def = HullAnchorMath.GetHullTypeDef(tug, equipConfig);
            if (def?.TowAnchors == null || def.TowAnchors.Count == 0) return tug.Position;
            var order = def.TowOrder ?? new List<string>(def.TowAnchors.Keys);
            if (order.Count == 0) return tug.Position;
            string key = order[Mathf.Clamp(index, 0, order.Count - 1)];
            if (!def.TowAnchors.TryGetValue(key, out var local)) return tug.Position;
            return HullAnchorMath.LocalToWorld(tug, local);
        }
    }
}
