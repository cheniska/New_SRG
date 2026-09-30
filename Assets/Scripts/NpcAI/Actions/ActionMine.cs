using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Simulation;

namespace SRG.NpcAI.Actions
{
    public class ActionMine : NpcAction
    {
        private readonly Vector2 _zone;
        private OrderMoveTo _moveOrder;
        private OrderIdle _mineIdle;
        private bool _mining;
        private int _cyclesDone;
        // MaxCycles/MineTurns вынесены в NpcBalance.MaxMineCycles / NpcBalance.MineTurns.

        public ActionMine(Vector2 zone) => _zone = zone;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (_cyclesDone >= NpcBalance.MaxMineCycles) { IsCompleted = true; return true; }

            if (!_mining)
            {
                Vector2 asteroidPos = FindNearestAsteroid(star) ?? (_zone + GameRng.InsideUnitCircle * 1f);
                if (_moveOrder == null) _moveOrder = new OrderMoveTo(asteroidPos);
                bool arrived = _moveOrder.Execute(ship, star, ctx);
                if (arrived) { _mining = true; _mineIdle = new OrderIdle(NpcBalance.MineTurns); }
            }
            else
            {
                bool done = _mineIdle.Execute(ship, star, ctx);
                if (done)
                {
                    _mining = false;
                    _moveOrder = null;
                    _cyclesDone++;
                }
            }

            return false;
        }

        private static Vector2? FindNearestAsteroid(StarData star)
        {
            if (star.Asteroids == null || star.Asteroids.Count == 0) return null;
            AsteroidData nearest = null;
            float bestDist = float.MaxValue;
            foreach (var a in star.Asteroids)
            {
                if (a.IsDestroyed) continue;
                float d = a.Position.magnitude;
                if (d < bestDist) { bestDist = d; nearest = a; }
            }
            return nearest?.Position;
        }

        public override string DebugName => $"Mine(cycles={_cyclesDone}/{NpcBalance.MaxMineCycles})";
    }
}
