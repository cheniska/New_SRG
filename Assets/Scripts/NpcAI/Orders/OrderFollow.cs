using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Utils;

namespace SRG.NpcAI.Orders
{
    public class OrderFollow : NpcOrder
    {
        private readonly string _targetUid;
        private ShipData _target;           // прямая ссылка, инициализируется лениво
        // FollowDistance вынесен в NpcBalance.FollowDistance.

        public OrderFollow(string targetUid) => _targetUid = targetUid;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            _target ??= FindShip(star, _targetUid);
            if (_target == null || _target.CurrentHull <= 0) return true;

            // Строй «клин»: у каждого ведомого своё место за ведущим — ряд и борт по хешу UID.
            // Место привязано к курсу ведущего (а не к позиции ведомого), поэтому цель не дрожит
            // от хода к ходу, а несколько ведомых не сходятся в одну точку.
            float leaderHeading = float.IsNaN(_target.CurrentHeading)
                ? Angles.Toward(ship.Position, _target.Position)
                : _target.CurrentHeading;
            Vector2 offset = FormationSlot(ship.Uid, leaderHeading);
            SetMoveTarget(ship, star, _target.Position + offset * NpcBalance.FollowDistance);
            return false;
        }

        /// <summary>Смещение места в строю в долях FollowDistance: ряд 1..3 назад, борт слева/справа,
        /// с разносом вбок, растущим с номером ряда.</summary>
        private static Vector2 FormationSlot(string uid, float leaderHeading)
        {
            uint hash = unchecked((uint)(uid?.GetHashCode() ?? 0));
            int row  = 1 + (int)(hash % 3);
            int side = ((hash >> 2) & 1) == 0 ? 1 : -1;
            Vector2 back = -Angles.Dir(leaderHeading);
            Vector2 flank = new Vector2(-back.y, back.x) * side;
            return (back * (0.6f + 0.4f * row) + flank * (0.5f * row)).normalized * (0.8f + 0.2f * row);
        }

        public override bool IsCompleted(ShipData ship, StarData star)
        {
            _target ??= FindShip(star, _targetUid);
            return _target == null;
        }

        public override string DebugName => $"Follow({SpriteUtility.ShortId(_targetUid)})";
    }
}
