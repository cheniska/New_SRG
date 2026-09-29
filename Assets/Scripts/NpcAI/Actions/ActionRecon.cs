using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Utils;

namespace SRG.NpcAI.Actions
{
    public class ActionRecon : NpcAction
    {
        private readonly List<Vector2> _targets = new();
        private int _targetIdx;
        private OrderMoveTo _move;
        private bool _initialized;

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            if (!_initialized)
            {
                foreach (var p in star.Planets)
                    _targets.Add(OrbitMath.GetPlanetPositionSimple(p));
                if (_targets.Count == 0)
                    _targets.Add(Vector2.zero);
                _initialized = true;
            }

            if (_targetIdx >= _targets.Count) { IsCompleted = true; return true; }

            if (_move == null) _move = new OrderMoveTo(_targets[_targetIdx]);
            if (_move.Execute(ship, star, ctx))
            {
                Debug.Log($"[ActionRecon] {ship.Name} scouted target {_targetIdx}");
                _targetIdx++;
                _move = null;
            }
            return false;
        }

        public override string DebugName => $"Recon({_targetIdx}/{_targets.Count})";
    }
}
