using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships.Movement;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI.Actions
{
    public class ActionScavenge : NpcAction
    {
        private Vector2? _targetPos;
        private int _idleTurns;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_targetPos == null || (ship.Position - _targetPos.Value).magnitude < 0.5f)
            {
                float r = Positions.SystemRadius(star) * GameRng.Range(0.2f, 0.7f);
                _targetPos = Positions.RandomInCircle(r);
                _idleTurns = 0;
            }

            ship.TargetPosition = _targetPos.Value;
            ship.TargetQueue.Clear();
            ShipTrajectory.BuildPath(ship, _targetPos.Value, star, ship.Waypoints);
            ship.WaypointIndex = 0;
            _idleTurns++;

            if (_idleTurns > 15) _targetPos = null;
            return false;
        }

        public override string DebugName => "Scavenge";
    }
}
