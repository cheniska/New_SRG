using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.NpcAI.Orders
{
    public class OrderLoot : NpcOrder
    {
        private readonly Vector2 _lootPos;
        // PickupRadiusSq вынесен в NpcBalance.PickupRadiusSq.
        private bool _done;

        public OrderLoot(Vector2 lootPos) => _lootPos = lootPos;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            SetMoveTarget(ship, star, _lootPos);

            if ((ship.Position - _lootPos).sqrMagnitude < NpcBalance.PickupRadiusSq)
            {
                _done = true;
                return true;
            }
            return false;
        }

        public override bool IsCompleted(ShipData ship, StarData star) => _done;
        public override string DebugName => $"Loot({_lootPos.x:F1},{_lootPos.y:F1})";
    }
}
