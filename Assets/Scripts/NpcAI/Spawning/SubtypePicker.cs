using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;

namespace SRG.NpcAI.Spawning
{
    /// <summary>
    /// Выбор подтипа корабля по таблице SubtypeTables, используя ступень доминации расы.
    /// Применяется только для рас с <see cref="RaceConfig.UseShipTypeTable"/>=true (синтеты).
    /// </summary>
    public static class SubtypePicker
    {
        /// <summary>
        /// Возвращает выбранный shipTypeId по таблице расы. Если таблица отсутствует, нет ступени
        /// или сумма весов нулевая — возвращает fallback.
        /// </summary>
        public static string PickForRace(string raceKey, SpawnConfig spawnCfg, DominationFlags domination, string fallback)
        {
            if (string.IsNullOrEmpty(raceKey) || spawnCfg?.SubtypeTables == null) return fallback;
            if (!spawnCfg.SubtypeTables.TryGetValue(raceKey, out var byTier) || byTier == null) return fallback;

            string tier = domination?.GetRaceTier(raceKey);
            Dictionary<string, int> weights = null;
            if (tier == null || !byTier.TryGetValue(tier, out weights) || weights == null || weights.Count == 0)
            {
                // Возьмём первую доступную ступень как ультра-фолбэк
                foreach (var kv in byTier) { weights = kv.Value; break; }
                if (weights == null || weights.Count == 0) return fallback;
            }

            int total = 0;
            foreach (var w in weights.Values) total += w > 0 ? w : 0;
            if (total <= 0) return fallback;

            int roll = Random.Range(0, total);
            int acc = 0;
            foreach (var kv in weights)
            {
                int w = kv.Value > 0 ? kv.Value : 0;
                acc += w;
                if (roll < acc) return kv.Key;
            }
            return fallback;
        }
    }
}
