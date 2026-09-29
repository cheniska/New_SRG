using System.Collections.Generic;
using UnityEngine;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships;
using SRG.Utils;

namespace SRG.NpcAI.Orders
{
    /// <summary>
    /// Передача груза от <c>ship</c> к получателю. Два режима:
    /// <list type="bullet">
    ///  <item><b>DirectTransfer</b> (default) — исполняющий корабль должен быть в
    ///  <see cref="NpcBalance.TransferRange"/> от получателя, груз перекладывается прямо в его
    ///  инвентарь. Один тик — доехать/передать.</item>
    ///  <item><b>SpawnContainers</b> — исполняющий корабль сбрасывает груз в космос отдельными
    ///  контейнерами рядом с собой. Дистанция до получателя не важна: контейнеры подберёт
    ///  <see cref="SRG.Combat.PickupSystem"/> получателя (или его <see cref="OrderLoot"/>).
    ///  Используется в грабежах: жертва «выкидывает» груз, а не тащит его в трюм грабителя.</item>
    /// </list>
    /// </summary>
    public class OrderTransfer : NpcOrder
    {
        private readonly string _targetUid;
        private readonly string _itemId;   // null → одиночный предмет из Items[0] (DirectTransfer)
        private readonly int _amount;
        private readonly bool _spawnContainers;
        private readonly List<string> _itemUids;    // конкретные Items к сбросу (SpawnContainers)
        private readonly List<string> _stackIds;    // конкретные Stacks к сбросу (SpawnContainers)
        private readonly float _stackFraction;      // доля веса каждого стека (SpawnContainers)
        private ShipData _target;
        private bool _done;

        /// <summary>Прямая передача одного предмета/стека. Требует близости к получателю.</summary>
        public OrderTransfer(string targetUid, string itemId = null, int amount = 0)
        {
            _targetUid = targetUid;
            _itemId = itemId;
            _amount = amount;
            _spawnContainers = false;
        }

        /// <summary>Сброс списка предметов и стеков контейнерами в космос (грабёж). Дистанция не важна —
        /// цель сбрасывает у себя, получатель подбирает <see cref="OrderLoot"/>-ом.</summary>
        public OrderTransfer(string recipientUid, List<string> itemUids, List<string> stackIds, float stackFraction)
        {
            _targetUid = recipientUid;
            _spawnContainers = true;
            _itemUids = itemUids;
            _stackIds = stackIds;
            _stackFraction = Mathf.Clamp(stackFraction, 0.05f, 1f);
        }

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_spawnContainers)
                return ExecuteSpawnContainers(ship, star);
            return ExecuteDirect(ship, star);
        }

        private bool ExecuteDirect(ShipData ship, StarData star)
        {
            _target ??= FindShip(star, _targetUid);
            if (_target == null) { _done = true; return true; }

            if ((ship.Position - _target.Position).magnitude > NpcBalance.TransferRange)
            {
                SetMoveTarget(ship, star, _target.Position);
                return false;
            }

            PerformDirectTransfer(ship, _target);
            _done = true;
            return true;
        }

        private void PerformDirectTransfer(ShipData from, ShipData to)
        {
            if (string.IsNullOrEmpty(_itemId))
            {
                if (from.Inventory.Count > 0 && from.Inventory.Items.Count > 0)
                {
                    var item = from.Inventory.Items[0];
                    from.Inventory.Remove(item.Uid);
                    InventoryService.PutItem(to, item);
                }
            }
            else
            {
                int toTransfer = _amount > 0 ? _amount : int.MaxValue;
                var stack = from.Inventory.TakeStack(_itemId, toTransfer);
                if (stack != null)
                    to.Inventory.AddStack(stack);
            }
        }

        /// <summary>Сбрасывает указанные Items и Stacks контейнерами рядом с исполняющим кораблём.
        /// Возвращает true всегда (один тик — исполнено).</summary>
        public List<string> LastSpawnedContainerUids { get; private set; }

        private bool ExecuteSpawnContainers(ShipData victim, StarData star)
        {
            LastSpawnedContainerUids = new List<string>();
            if (star == null) { _done = true; return true; }

            // Items — единичные предметы (оборудование/юзлес/микромодули).
            if (_itemUids != null)
            {
                foreach (var uid in _itemUids)
                {
                    if (string.IsNullOrEmpty(uid)) continue;
                    var taken = victim.Inventory?.TakeByUid(uid);
                    if (taken == null) continue;
                    var container = ContainerFactory.SpawnContainerWithItem(victim, taken, star);
                    if (container != null) LastSpawnedContainerUids.Add(container.Uid);
                }
            }

            // Stacks — фракция от каждого стека.
            if (_stackIds != null && _stackFraction > 0f && victim.Inventory?.Stacks != null)
            {
                foreach (var id in _stackIds)
                {
                    if (!victim.Inventory.Stacks.TryGetValue(id, out var st) || st == null || st.TotalWeight <= 0) continue;
                    int take = Mathf.Max(1, Mathf.RoundToInt(st.TotalWeight * _stackFraction));
                    var chunk = victim.Inventory.TakeStack(id, take);
                    if (chunk == null || chunk.TotalWeight <= 0) continue;
                    var container = ContainerFactory.SpawnContainerWithStack(victim, chunk, star);
                    if (container != null) LastSpawnedContainerUids.Add(container.Uid);
                }
            }

            _done = true;
            return true;
        }

        public override bool IsCompleted(ShipData ship, StarData star) => _done;
        public override string DebugName => _spawnContainers
            ? $"Transfer→drop({SpriteUtility.ShortId(_targetUid)})"
            : $"Transfer(→{SpriteUtility.ShortId(_targetUid)})";
    }
}
