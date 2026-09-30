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
    /// Отбор ММ — по <see cref="ItemNpcDrop"/> (сторона/раса убитого); среди выпавших
    /// выбирается один с весом по «желательному приоритету» для текущего ГТУ галактики.
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

            // Каждый ММ отдельно катит свой шанс (side + race); прошедшие попадают в пул,
            // из которого берём один — взвешенно по близости приоритета к «желательному».
            float preferred = PreferredPriority(gtl);

            List<(string id, float weight)> pool = null;
            float totalWeight = 0f;
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

                float sideCh = LookupChance(drop.SideChance, victim.Owner);
                float raceCh = LookupChance(drop.RaceChance, victim.Race);
                float chance = Mathf.Clamp01(sideCh + raceCh);
                if (chance <= 0f || GameRng.Value > chance) continue;

                float weight = PriorityWeight(cfg.Embed?.Priority ?? 0, preferred);
                if (weight <= 0f) continue;
                (pool ??= new()).Add((kv.Key, weight));
                totalWeight += weight;
            }
            if (pool == null) return;

            // Один предмет на смерть — чтобы один NPC не ронял сразу несколько ММ.
            float roll = GameRng.Value * totalWeight;
            foreach (var (id, weight) in pool)
            {
                roll -= weight;
                if (roll > 0f) continue;
                var inst = ItemGrantService.CreateMicroModule(id, ctx);
                if (inst == null) return;
                // ММ — не стакабельный: выкидываем «свободным» предметом со своей иконкой,
                // а не в контейнерной обёртке (см. ContainerFactory.SpawnLooseItemInSpace).
                ContainerFactory.SpawnLooseItemInSpace(victim, inst, star);
                return;
            }
        }

        /// <summary>Желательный приоритет ММ для уровня ГТУ: в начале игры — массовые модули
        /// (высокий Priority), к концу — редкие (низкий).</summary>
        private static float PreferredPriority(int gtl)
            => Mathf.Lerp(PreferredPriorityEarly, PreferredPriorityLate, Mathf.Clamp01((gtl - 1f) / 7f));

        private const float PreferredPriorityEarly = 80f;
        private const float PreferredPriorityLate  = 15f;
        /// <summary>Ширина «колокола» предпочтения; за пределами 2.5 ширин вес обнуляется.</summary>
        private const float PriorityBellWidth = 30f;

        /// <summary>Гауссов вес модуля по удалённости его приоритета от желательного.</summary>
        private static float PriorityWeight(int priority, float preferred)
        {
            float z = (priority - preferred) / PriorityBellWidth;
            return z > 2.5f || z < -2.5f ? 0f : Mathf.Exp(-z * z);
        }

        private static float LookupChance(Dictionary<string, float> table, string key)
        {
            if (table == null || string.IsNullOrEmpty(key)) return 0f;
            return table.TryGetValue(key, out var v) ? v : 0f;
        }
    }
}
