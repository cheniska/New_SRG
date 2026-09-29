using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    public class ActionPatrol : NpcAction
    {
        private OrderPatrol _patrol;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_patrol == null)
            {
                float r = Positions.SystemRadius(star) * 0.5f;
                _patrol = new OrderPatrol(Positions.RandomInCircle(r), Positions.RandomInCircle(r));
            }

            _patrol.Execute(ship, star, ctx);
            return false;
        }

        public override string DebugName => "Patrol";
    }
}
