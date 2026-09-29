using SRG.Galaxy;
using SRG.Galaxy.Generation;

namespace SRG.NpcAI
{
    // Иерархия тактических действий NPC (уровень «что делать»).
    // Внутри Action держит один или несколько Order'ов и/или sub-Action'ов — конкретный
    // состав определяет само действие (ActionDeliver: Move+Idle, ActionRob: Disable+Loot,
    // ActionDefend: Hold + опциональный PursueAndAttack). Единого «одного текущего Order»
    // как обязательного слота у Action нет — раньше существовавшая пара _currentOrder /
    // ExecuteOrder оказалась мёртвой абстракцией и удалена в июле 2026.
    //
    // Tick() вызывается раз в ход из NpcController; возвращает true, пока действие активно.
    // Конкретные действия — в NpcAI/Actions/Action*.cs (по одному классу на файл).
    public abstract class NpcAction
    {
        public bool IsCompleted { get; protected set; }
        public virtual string DebugName => GetType().Name;

        // UID цели, по которой данное действие открывает огонь. null — если действие нелетальное.
        // Используется в CombatSubTurn, чтобы NPC стрелял по своему преследуемому/грабимому
        // кораблю даже если отношения владельцев не считаются враждебными.
        public virtual string CombatTargetUid => null;

        public abstract bool Tick(ShipData ship, StarData star, GalaxyGenerationContext ctx);

        // Тонкие обёртки над StarData.FindShip — единая реализация поиска по UID.
        protected static ShipData FindShip(StarData star, string uid) => star?.FindShip(uid);
        protected static ShipData FindAliveShip(StarData star, string uid) => star?.FindShip(uid, aliveOnly: true);
    }
}
