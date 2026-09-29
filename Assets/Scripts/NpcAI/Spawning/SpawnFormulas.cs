using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.NpcAI.Spawning
{
    /// <summary>
    /// Общая формула вероятности спавна для всех политик top-up.
    ///   shortage    = max(0, (target - count) / target)
    ///   probability = base_chance × (1 + shortage)
    ///   spawn_if    = shortage > 0 AND roll < probability
    /// </summary>
    public static class SpawnFormulas
    {
        /// <summary>Возвращает shortage ∈ [0..1]. 0 = на цели или выше, 1 = пусто.</summary>
        public static float Shortage(int count, int target)
        {
            if (target <= 0) return 0f;
            int delta = target - count;
            if (delta <= 0) return 0f;
            return Mathf.Clamp01((float)delta / target);
        }

        /// <summary>
        /// Возвращает true, если корабль должен спавнится в этот ход.
        /// </summary>
        public static bool ShouldSpawn(int count, int target, float baseChance)
        {
            float shortage = Shortage(count, target);
            if (shortage <= 0f) return false;
            float probability = baseChance * (1f + shortage);
            return Random.value < probability;
        }

        /// <summary>Проверка per-day-квоты спавнов: <see cref="StarData.SpawnsToday"/> &lt;
        /// <see cref="GameSettingsConfig.MaxShipsSpawnedPerStarPerDay"/>. Настройка живёт в
        /// GameSettingsConfig (не в GalaxyConfig), чтобы её можно было менять без пересборки галактики.
        /// Учитывается всеми политиками; счётчик увеличивается в <see cref="SpawnSystem.RegisterSpawn"/>.
        /// Fallback лимит = 1 если settings null (safe default).</summary>
        public static bool StarCanSpawnMore(StarData star, GameSettingsConfig settings)
        {
            if (star == null) return false;
            int limit = settings != null ? Mathf.Max(0, settings.MaxShipsSpawnedPerStarPerDay) : 1;
            return star.SpawnsToday < limit;
        }
    }
}
