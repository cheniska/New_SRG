using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy.Generation;

namespace SRG.Galaxy.Simulation
{
    /// <summary>
    /// Дневной тик червоточин: продвигает фазы (Opening → Open → Closing → удаление),
    /// пробует заспавнить новую червоточину по шансу из настроек. Вызывается из
    /// <see cref="GalaxyData.GalaxyNextDay"/> после общих тиков. Естественный спавн
    /// — <see cref="WormholeService.Spawn"/> без аргументов.
    /// </summary>
    public static class WormholeSystem
    {
        public static void DailyTick(GalaxyData galaxy, GalaxyGenerationContext ctx)
        {
            if (galaxy == null) return;
            var settings = GalaxyManager.Instance?.Settings;
            if (settings == null) return;

            TickExisting(galaxy, settings);
            TrySpawnNew(galaxy, settings);
        }

        // ── Продвижение фаз всех активных ──────────────────────────────────────────

        private static void TickExisting(GalaxyData galaxy, GameSettingsConfig settings)
        {
            List<(StarData star, WormholeData wh)> toRemove = null;
            foreach (var star in galaxy.StarsMap.Values)
            {
                if (star.Wormholes == null || star.Wormholes.Count == 0) continue;

                for (int i = 0; i < star.Wormholes.Count; i++)
                {
                    var wh = star.Wormholes[i];
                    wh.DaysInPhase++;

                    switch (wh.Phase)
                    {
                        case WormholePhase.Opening:
                            // Клип открытия проигрывается быстро (24fps × ~70 кадров ≈ 3с реального времени),
                            // но в терминах симуляции — один ход. После — переход в стабильную фазу Open.
                            if (wh.DaysInPhase >= 1)
                            {
                                wh.Phase = WormholePhase.Open;
                                wh.DaysInPhase = 0;
                                int lifetime = wh.LifetimeOverride > 0
                                    ? wh.LifetimeOverride
                                    : settings.Wormhole_OpenTurns;
                                wh.OpenTurnsRemaining = Mathf.Max(1, lifetime);
                            }
                            break;

                        case WormholePhase.Open:
                            wh.OpenTurnsRemaining--;
                            if (wh.OpenTurnsRemaining <= 0)
                            {
                                wh.Phase = WormholePhase.Closing;
                                wh.DaysInPhase = 0;
                                // Пара закрывается синхронно — если по каким-то причинам её lifetime
                                // разошёлся (скрипт вручную корректировал OpenTurnsRemaining),
                                // подтягиваем её в Closing тоже. Двусторонний тик безопасен: пара
                                // уже могла тикнуться раньше или тикнется этим же проходом.
                                if (!string.IsNullOrEmpty(wh.PairedWormholeUid))
                                {
                                    var pair = WormholeService.Find(wh.PairedWormholeUid);
                                    if (pair != null && pair.Phase == WormholePhase.Open)
                                    {
                                        pair.Phase = WormholePhase.Closing;
                                        pair.DaysInPhase = 0;
                                    }
                                }
                            }
                            break;

                        case WormholePhase.Closing:
                            if (wh.DaysInPhase >= 1)
                                (toRemove ??= new()).Add((star, wh));
                            break;
                    }
                }
            }
            if (toRemove == null) return;
            foreach (var (star, wh) in toRemove)
                star.Wormholes.Remove(wh);
        }

        // ── Спавн новой (по шансу) ─────────────────────────────────────────────────

        private static void TrySpawnNew(GalaxyData galaxy, GameSettingsConfig settings)
        {
            int interval = settings.Wormhole_SpawnAvgIntervalTurns;
            if (interval <= 0) return;
            // Wormhole_MaxActive измеряет активные ПАРЫ (спавн атомарно создаёт вход+выход);
            // CountActive считает записи → делим на 2.
            if (WormholeService.CountActive(galaxy) / 2 >= settings.Wormhole_MaxActive) return;

            // 1/interval вероятность в день = средний интервал ~interval ходов между спавнами.
            if (Random.Range(0, interval) != 0) return;

            WormholeService.Spawn();
        }
    }
}
