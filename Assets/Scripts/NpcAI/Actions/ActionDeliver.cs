using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;

namespace SRG.NpcAI.Actions
{
    public class ActionDeliver : NpcAction
    {
        private readonly Vector2 _pointA;
        private readonly Vector2 _pointB;
        private enum Phase { MoveToA, LoadWait, MoveToB, UnloadWait, Done }
        private Phase _phase = Phase.MoveToA;
        private OrderMoveTo _move;
        private OrderIdle _wait;
        // LoadTurns вынесен в NpcBalance.LoadTurns.

        public ActionDeliver(Vector2 a, Vector2 b) { _pointA = a; _pointB = b; }

        public override bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            switch (_phase)
            {
                case Phase.MoveToA:
                    if (_move == null) _move = new OrderMoveTo(_pointA);
                    if (_move.Execute(ship, star, ctx)) { _phase = Phase.LoadWait; _wait = new OrderIdle(NpcBalance.LoadTurns); }
                    break;
                case Phase.LoadWait:
                    if (_wait.Execute(ship, star, ctx)) { _phase = Phase.MoveToB; _move = new OrderMoveTo(_pointB); }
                    break;
                case Phase.MoveToB:
                    if (_move.Execute(ship, star, ctx)) { _phase = Phase.UnloadWait; _wait = new OrderIdle(NpcBalance.LoadTurns); }
                    break;
                case Phase.UnloadWait:
                    if (_wait.Execute(ship, star, ctx)) { _phase = Phase.Done; IsCompleted = true; return true; }
                    break;
            }
            return false;
        }

        public override string DebugName => $"Deliver(phase={_phase})";
    }
}
