using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.NpcAI.Orders
{
    public class OrderFlee : NpcOrder
    {
        private readonly string _fromTargetUid;
        private ShipData _threat;           // прямая ссылка
        // FleeDistance вынесен в NpcBalance.FleeDistance.
        private bool _completed;

        public OrderFlee(string fromTargetUid) => _fromTargetUid = fromTargetUid;

        public override bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx)
        {
            // Обновляем ссылку только если ещё нет или цель умерла
            if (_threat == null || _threat.CurrentHull <= 0)
                _threat = FindAliveShip(star, _fromTargetUid);

            if (_threat == null) { _completed = true; return true; }

            Vector2 away = (ship.Position - _threat.Position).normalized;
            if (away.sqrMagnitude < 0.001f) away = GameRng.InsideUnitCircle.normalized;
            Vector2 dest = ship.Position + away * NpcBalance.FleeDistance;

            float sysR = SRUnits.ToWorld(star.SystemSize) * 0.85f;
            if (dest.magnitude > sysR) dest = dest.normalized * sysR;

            SetMoveTarget(ship, star, dest);

            _completed = (ship.Position - _threat.Position).magnitude > NpcBalance.FleeDistance * 0.8f;
            return _completed;
        }

        public override bool IsCompleted(ShipData ship, StarData star) => _completed;
        public override string DebugName => $"Flee(from {SpriteUtility.ShortId(_fromTargetUid)})";
    }
}
