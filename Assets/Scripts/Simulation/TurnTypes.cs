using UnityEngine;

namespace SRG.Simulation
{
    /// <summary>Фаза цикла хода: игрок планирует ↔ проигрывается симуляция.</summary>
    public enum TurnPhase
    {
        Planning,
        Simulation,
    }

    public enum PlayerDeathCause
    {
        Unknown,
        Weapon,    // обычный выстрел
        Missile,   // ракета
        Asteroid,  // столкновение с астероидом
    }

    /// <summary>
    /// Контекст смерти игрока. Передаётся в OnPlayerDeathAttempt (где Cancelled можно
    /// поставить true для предотвращения смерти) и в OnPlayerDeathConfirmed.
    /// </summary>
    public class PlayerDeathInfo
    {
        public PlayerDeathCause Cause;
        public string KillerName;
        public string KillerOwner;
        public string StarName;
        public Vector2 Position;

        /// <summary>Если перехватчик ставит true — игрок выживает (HP восстанавливается до 1).</summary>
        public bool Cancelled;
    }
}
