using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships;

namespace SRG.NpcAI.Spawning
{
    /// <summary>
    /// Контекст вызова политики спавна на один день. Содержит все ссылки и кешированные данные,
    /// чтобы политика не лезла в синглтоны.
    /// </summary>
    public class SpawnTickContext
    {
        public GalaxyData Galaxy;
        public GalaxyGenerationContext Gen;       // для ShipFactory / RaceLineups / AvailableShipTypes
        public GalaxyConfig Config;                // для Spawn-секции
        public GameSettingsConfig Settings;        // MaxShipsSpawnedPerStarPerDay и др. геймсеттинги
        public GalaxyShipCounters Counters;
        public DominationFlags Domination;
        public int CurrentTurn;
    }

    /// <summary>
    /// Политика спавна для одного типа корабля (или семейства, если UseShipTypeTable=true).
    /// Политики stateless; всё состояние — в <see cref="SpawnTickContext"/>.
    /// </summary>
    public interface ISpawnPolicy
    {
        /// <summary>Ключ ShipTypeId, на который политика отзывается. Для подтиповых политик —
        /// «корневой» тип (например, "Dom1" как точка входа; конкретный подтип выбирается через
        /// SubtypePicker). Сторонние моды добавляют свои реализации.</summary>
        string ShipTypeId { get; }

        /// <summary>Выполнить один день для этой политики. Может породить 0..N кораблей.</summary>
        void DailyTick(SpawnTickContext ctx);
    }
}
