using System;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Galaxy.Simulation;
using SRG.Ships.Services;

namespace SRG.Combat
{
    /// <summary>
    /// Единая шина смерти кораблей. Все источники урона (WeaponSystem, MissileSystem, AsteroidSystem)
    /// должны эмитить сюда после того, как <see cref="WeaponSystem.RegisterTargetDeath"/> подтвердил
    /// уход цели (то есть цель не была спасена Phoenix/RepairDroid).
    ///
    /// Подписчики:
    ///  - <see cref="ShipRatingService"/> — инкремент Kills* атакующего.
    ///  - <see cref="GalaxyNewsService"/>  — публикация новостей DeadShip.* (партнёр/знакомый).
    ///  - будущие: экономический лог, ачивменты игрока.
    ///
    /// Cause — короткий тег причины ("Weapon" | "Missile" | "Asteroid" | "Collision"). Астероид как
    /// СТОЛКНОВЕНИЕ идёт с cause="Collision" и в новостях о «сбитии» не участвует — таково требование
    /// (учитывать только сбитие, а не столкновение). Асстероид, сбитый пушкой, идёт как "Weapon".
    /// </summary>
    public static class ShipDeathBus
    {
        /// <summary>victim, killer (может быть null — например, столкновение с астероидом без атакующего),
        /// cause.</summary>
        public static event Action<ShipData, ShipData, string> OnShipDestroyed;

        public const string CAUSE_WEAPON    = "Weapon";
        public const string CAUSE_MISSILE   = "Missile";
        public const string CAUSE_ASTEROID  = "Asteroid";     // сбит корабль астероидом (столкновение)
        public const string CAUSE_COLLISION = "Collision";    // общий: любое НЕ-боевое столкновение

        public static void Emit(ShipData victim, ShipData killer, string cause)
        {
            if (victim == null) return;
            OnShipDestroyed?.Invoke(victim, killer, cause);
        }
    }
}
