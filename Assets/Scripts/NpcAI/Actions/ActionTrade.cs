using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    public class ActionTrade : NpcAction
    {
        private int _planetIndex;
        private OrderMoveTo _moveOrder;
        private int _dockTurns;
        // DockDuration вынесен в NpcBalance.DockDuration.

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (star.Planets.Count < 2) return false;

            if (_moveOrder == null || _moveOrder.IsCompleted(ship, star))
            {
                if (_dockTurns < NpcBalance.DockDuration)
                {
                    ship.TargetPosition = ship.Position;
                    ship.TargetQueue.Clear();
                    ship.Waypoints.Clear();
                    ship.WaypointIndex  = 0;
                    _dockTurns++;
                    return false;
                }

                _dockTurns = 0;
                _planetIndex = (_planetIndex + 1) % star.Planets.Count;
                var planet = star.Planets[_planetIndex];
                Vector2 orbitPos = GetPlanetPosition(planet);
                _moveOrder = new OrderMoveTo(orbitPos);
            }

            _moveOrder.Execute(ship, star, ctx);
            return false;
        }

        private static Vector2 GetPlanetPosition(PlanetData planet)
            => OrbitMath.GetPlanetWorldPosition(planet);

        public override string DebugName => "Trade";
    }
}
