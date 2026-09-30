using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Utils;

namespace SRG.NpcAI.Orders
{
    public class OrderFollow : NpcOrder
    {
        private readonly string _targetUid;
        [System.NonSerialized] private ShipData _target;           // прямая ссылка, инициализируется лениво
        // FollowDistance вынесен в NpcBalance.FollowDistance.

        public OrderFollow(string targetUid) => _targetUid = targetUid;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            _target ??= FindShip(star, _targetUid);
            if (_target == null || _target.CurrentHull <= 0) return true;

            // SR2-стиль FOLLOW_SHIP (§5.4 Recheck): точка позади leader-а по его курсу
            // с per-ship jitter ±45° от строго-сзади, на дистанции FollowDistance.
            // Стабильно (не зависит от позиции follower-а — нет дребезга), и несколько
            // followers не накладываются благодаря jitter.
            float refHeading = _target.CurrentHeading;
            if (float.IsNaN(refHeading))
            {
                Vector2 toLeader = _target.Position - ship.Position;
                refHeading = toLeader.sqrMagnitude > 0.0001f
                    ? Mathf.Atan2(toLeader.y, toLeader.x)
                    : 0f;
            }
            int seed = StableHash.Of(ship.Uid);
            float jitterDeg = ((seed & 0x7FFFFFFF) % 91) - 45;  // [-45, +45]
            float angle = refHeading + Mathf.PI + jitterDeg * Mathf.Deg2Rad;
            Vector2 offset = new(Mathf.Cos(angle), Mathf.Sin(angle));
            SetMoveTarget(ship, star, _target.Position + offset * NpcBalance.FollowDistance);
            return false;
        }

        public override bool IsCompleted(ShipData ship, StarData star)
        {
            _target ??= FindShip(star, _targetUid);
            return _target == null;
        }

        public override string DebugName => $"Follow({SpriteUtility.ShortId(_targetUid)})";
    }
}
