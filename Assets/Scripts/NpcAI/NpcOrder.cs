using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.NpcAI.Orders;
using SRG.Ships.Movement;

namespace SRG.NpcAI
{
    // Атомарные приказы NPC (уровень «как выполнить»).
    // Execute() устанавливает цель/путь на текущий ход; IsCompleted() проверяет достижение.
    // Используются как строительные блоки внутри NpcAction.
    //
    // Конкретные ордера — в NpcAI/Orders/Order*.cs (по одному классу на файл).
    // Утилита CeasefireSolidarity — в Orders/OrderOfferMoneyRansom.cs (используется только оттуда
    // и из OrderRequestCeasefire).
    public abstract class NpcOrder
    {
        public abstract bool Execute(ShipData ship, StarData star, GalaxyGenerationContext ctx);
        public abstract bool IsCompleted(ShipData ship, StarData star);
        public virtual string DebugName => GetType().Name;

        protected static void SetMoveTarget(ShipData ship, StarData star, Vector2 target)
        {
            ship.TargetPosition = target;
            ship.TargetQueue.Clear();
            ShipTrajectory.BuildPath(ship, target, star, ship.Waypoints);
            ship.WaypointIndex = 0;
        }

        // Тонкие обёртки над StarData.FindShip — единая реализация поиска по UID.
        protected static ShipData FindShip(StarData star, string uid) => star?.FindShip(uid);
        protected static ShipData FindAliveShip(StarData star, string uid) => star?.FindShip(uid, aliveOnly: true);
    }
}
