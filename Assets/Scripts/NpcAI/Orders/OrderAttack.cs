using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Utils;

namespace SRG.NpcAI.Orders
{
    public class OrderAttack : NpcOrder
    {
        private readonly string _targetUid;
        private ShipData _target;           // прямая ссылка
        // ChaseRadius вынесен в NpcBalance.ChaseRadius.

        public OrderAttack(string targetUid) => _targetUid = targetUid;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            _target ??= FindAliveShip(star, _targetUid);
            if (_target == null || _target.CurrentHull <= 0) return true;

            float dist = (_target.Position - ship.Position).magnitude;

            float stopDist = Mathf.Max(ship.SpriteWorldSize, _target.SpriteWorldSize) * 1.5f;
            Vector2 diff = ship.Position - _target.Position;
            Vector2 dir;
            if (diff.sqrMagnitude > 0.0001f)
                dir = diff.normalized;
            else
            {
                // Совпадаем с целью — отступаем по её курсу назад, не зависаем сверху.
                float h = _target.CurrentHeading;
                dir = float.IsNaN(h)
                    ? Vector2.right
                    : new Vector2(-Mathf.Cos(h), -Mathf.Sin(h));
            }
            SetMoveTarget(ship, star, _target.Position + dir * stopDist);

            return dist > NpcBalance.ChaseRadius;
        }

        public override bool IsCompleted(ShipData ship, StarData star)
        {
            _target ??= FindAliveShip(star, _targetUid);
            return _target == null || _target.CurrentHull <= 0;
        }

        public override string DebugName => $"Attack({SpriteUtility.ShortId(_targetUid)})";
    }
}
