using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy;
using SRG.Ships;
using SRG.Simulation;

namespace SRG.Equipment
{
    /// <summary>
    /// Подписывается на <see cref="ShipDeathBus.OnShipDestroyed"/> и катит шанс дропа
    /// микромодуля из подходящего пула. См. docs/modules/micromodules_design.md §9.1.
    /// Отбор ММ — по <see cref="ItemNpcDrop"/> (сторона/раса убитого) и по
    /// приоритетному окну от ГТУ галактики (SR2HD-формула).
    /// </summary>
    public static class EmbedDropService
    {
        private static bool _installed;

        public static void EnsureInstalled()
        {
            if (_installed) return;
            _installed = true;
            ShipDeathBus.OnShipDestroyed += OnShipDestroyed;
        }

        private static void OnShipDestroyed(ShipData victim, ShipData killer, string cause)
        {
            // Контейнеры не «умирают» так, чтобы дропать модули; астероидные столкновения — тоже.
            if (victim == null || victim.IsItem || victim.IsPlayer) return;
            if (cause == ShipDeathBus.CAUSE_COLLISION || cause == ShipDeathBus.CAUSE_ASTEROID) return;

            var ctx = GameWorld.Context;
            var itemsConfig = ctx?.ItemsConfig;
            if (itemsConfig == null) return;

            var star = victim.CurrentStar;
            if (star == null) return;

            int gtl = GameWorld.GeneratedGalaxy?.GtuLevel ?? 5;

            // Собираем пул: каждый ММ отдельно катит свой шанс (side + race), плюс проверка
            // приоритетного окна (SR2HD-формула, вычисленная от текущего ГТУ).
            var (pMin, pMax) = PriorityWindow(gtl);

            List<(ItemConfig cfg, string id, float chance)> pool = null;
            foreach (var kv in itemsConfig.EnumerateByKind(ItemKind.MicroModules))
            {
                var cfg = kv.Value;
                var drop = cfg.Sources?.NpcDrop;
                if (drop == null) continue;

                if (drop.GtlWindow != null && drop.GtlWindow.Length == 2 &&
                    drop.GtlWindow[0] > 0 && drop.GtlWindow[1] > 0)
                {
                    if (gtl < drop.GtlWindow[0] || gtl > drop.GtlWindow[1]) continue;
                }

                var tier = itemsConfig.GetTier(cfg.Embed?.Tier);
                if (tier != null && tier.ExcludeFromRandomDrop) continue;

                int priority = cfg.Embed?.Priority ?? 0;
                if (priority < pMin || priority > pMax) continue;

                float sideCh = LookupChance(drop.SideChance, victim.Owner);
                float raceCh = LookupChance(drop.RaceChance, victim.Race);
                float total = Mathf.Clamp01(sideCh + raceCh);
                if (total <= 0f) continue;

                (pool ??= new()).Add((cfg, kv.Key, total));
            }
            if (pool == null || pool.Count == 0) return;

            // Один ролл на смерть: катим шанс для каждого кандидата в стабильном порядке.
            // Первый прошедший — дропается. Не даём одному NPC уронить сразу два ММ.
            foreach (var entry in pool)
            {
                if (Random.value > entry.chance) continue;
                var inst = ItemGrantService.CreateMicroModule(entry.id, ctx);
                if (inst == null) continue;
                // ММ — не стакабельный: выкидываем «свободным» предметом со своей иконкой,
                // а не в контейнерной обёртке (см. ContainerFactory.SpawnLooseItemInSpace).
                ContainerFactory.SpawnLooseItemInSpace(victim, inst, star);
                return;
            }
        }

        /// <summary>SR2HD-формула: чем выше ГТУ, тем ниже верхняя граница окна (даёт редкие модули).</summary>
        private static (int min, int max) PriorityWindow(int gtl)
        {
            float t = Mathf.Clamp01((gtl - 3f) / 4f);
            int maxP = Mathf.RoundToInt(Mathf.Lerp(70f, 0f, t));
            int minP = Mathf.Clamp(maxP - 40, 0, 100);
            return (minP, Mathf.Max(minP, maxP + 40));
        }

        private static float LookupChance(Dictionary<string, float> table, string key)
        {
            if (table == null || string.IsNullOrEmpty(key)) return 0f;
            return table.TryGetValue(key, out var v) ? v : 0f;
        }
    }
}
