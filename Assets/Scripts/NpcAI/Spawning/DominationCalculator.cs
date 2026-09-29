using System.Collections.Generic;
using SRG.Config;
using SRG.NpcAI.Spawning.Policies;

namespace SRG.NpcAI.Spawning
{
    /// <summary>
    /// Снапшот доминации на один игровой день. Пересчитывается в начале DailyTick.
    /// </summary>
    public class DominationFlags
    {
        /// <summary>Стороны (Owner), у которых доля систем ≥ <see cref="DominationConfig.SideThreshold"/>.</summary>
        public readonly HashSet<string> DominatingSides = new();

        /// <summary>Расы (внутри стороны с SeparateRaceDomination=true), у которых доля систем
        /// ≥ SideThreshold × RaceCoefficient = 0.75 × 0.75 = 0.5625.</summary>
        public readonly HashSet<string> DominatingRaces = new();

        /// <summary>Ступень доминации для расы (имя из DominationConfig.Tiers, например "50-75").
        /// Считается по доле систем расы в галактике (если SeparateRaceDomination=true у её стороны)
        /// либо по доле стороны (если false). Используется DominatorSpawnPolicy для выбора подтипа.</summary>
        public readonly Dictionary<string, string> TierByRace = new();

        /// <summary>Ступень доминации для стороны (как TierByRace, но по доле всей стороны).</summary>
        public readonly Dictionary<string, string> TierBySide = new();

        public bool IsDominatingSide(string ownerId)
            => !string.IsNullOrEmpty(ownerId) && DominatingSides.Contains(ownerId);

        public bool IsDominatingRace(string raceKey)
            => !string.IsNullOrEmpty(raceKey) && DominatingRaces.Contains(raceKey);

        public string GetRaceTier(string raceKey)
            => raceKey != null && TierByRace.TryGetValue(raceKey, out var t) ? t : null;

        public string GetSideTier(string ownerId)
            => ownerId != null && TierBySide.TryGetValue(ownerId, out var t) ? t : null;
    }

    /// <summary>
    /// Пересчитывает <see cref="DominationFlags"/> из <see cref="GalaxyShipCounters"/> и конфига.
    /// </summary>
    public static class DominationCalculator
    {
        public static DominationFlags Recalculate(GalaxyShipCounters counters, GalaxyConfig galaxyConfig)
        {
            var flags = new DominationFlags();
            if (counters == null || galaxyConfig == null) return flags;

            int total = counters.InhabitedSystemsCount;
            if (total <= 0) return flags;

            var domCfg = galaxyConfig.Spawn?.Domination ?? new DominationConfig();
            float sideThreshold = domCfg.SideThreshold;
            float raceThreshold = sideThreshold * domCfg.RaceCoefficient;

            var owners = galaxyConfig.Ships?.Owners;

            // ── Стороны ───────────────────────────
            foreach (var kv in counters.SystemsBySide)
            {
                string ownerId = kv.Key;
                float ratio = (float)kv.Value / total;
                flags.TierBySide[ownerId] = ResolveTier(ratio, domCfg.Tiers);
                if (ratio >= sideThreshold) flags.DominatingSides.Add(ownerId);
            }

            // ── Расы ──────────────────────────────
            // Ступень расы считается по доле её систем в галактике.
            // Доминирующей раса считается ТОЛЬКО если у её стороны SeparateRaceDomination=true.
            foreach (var kv in counters.SystemsBySideAndRace)
            {
                var (ownerId, raceKey) = kv.Key;
                float ratio = (float)kv.Value / total;

                // TierByRace — для выбора подтипа (всегда заполняется)
                if (!flags.TierByRace.ContainsKey(raceKey))
                    flags.TierByRace[raceKey] = ResolveTier(ratio, domCfg.Tiers);

                // DominatingRaces — только при SeparateRaceDomination
                bool separate = owners != null
                                && owners.TryGetValue(ownerId, out var ownerCfg)
                                && ownerCfg != null
                                && ownerCfg.SeparateRaceDomination;
                if (separate && ratio >= raceThreshold) flags.DominatingRaces.Add(raceKey);
            }

            return flags;
        }

        private static string ResolveTier(float ratio, List<DominationTier> tiers)
        {
            if (tiers == null || tiers.Count == 0) return null;
            foreach (var t in tiers)
                if (ratio >= t.Min && ratio < t.Max) return t.Name;
            return tiers[tiers.Count - 1].Name; // на случай ratio=1.0
        }
    }
}
