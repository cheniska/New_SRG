using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;
using SRG.NpcAI.Spawning;
using SRG.Ships;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.Scripting;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.UI.Screens
{
    public partial class GameConsoleController
    {
        // ── Мультигалактика ──────────────────────────────────────────────────────

        void ListGalaxiesCmd()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null || gm.Galaxies == null || gm.Galaxies.Count == 0)
            { Log("Галактик нет (Galaxies пуст)."); return; }
            Log($"=== Galaxies ({gm.Galaxies.Count}) ===");
            foreach (var kv in gm.Galaxies)
            {
                string name = gm.Context?.Config?.Galaxies != null
                    && gm.Context.Config.Galaxies.TryGetValue(kv.Key, out var cfg)
                    && !string.IsNullOrEmpty(cfg?.Name) ? cfg.Name : kv.Key;
                string mark = kv.Key == gm.ActiveGalaxyKey ? " *" : "";
                int starCnt = kv.Value?.StarsMap?.Count ?? 0;
                Log($"  {kv.Key}: \"{name}\"  stars={starCnt}{mark}");
            }
        }

        void SwapGalaxyCmd()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null) { Log("GalaxyManager не готов."); return; }
            if (string.IsNullOrEmpty(_lastArg))
            { Log("Usage: swapgalaxy <key>. Список: 'galaxies'."); return; }
            if (!gm.SwitchActiveGalaxy(_lastArg.Trim()))
            { Log($"Не удалось переключиться на '{_lastArg}'. Проверь ключ через 'galaxies'."); return; }
            Log($"Активная галактика: {gm.ActiveGalaxyKey}");
        }

        void TeleportCmd()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null) { Log("GalaxyManager не готов."); return; }
            if (string.IsNullOrEmpty(_lastArg))
            { Log("Usage: teleport <star-name|uid> [@galaxy-key]"); return; }

            // Поддерживаем формат: "Alpha Centauri" и "Alpha Centauri @Andromeda".
            // '@<key>' в конце — явный указатель галактики. Просто "Alpha Centauri" не ломается.
            string trimmed = _lastArg.Trim();
            string galaxyKey = null;
            string arg = trimmed;
            int atIdx = trimmed.LastIndexOf('@');
            if (atIdx > 0 && atIdx < trimmed.Length - 1)
            {
                string tail = trimmed.Substring(atIdx + 1).Trim();
                if (gm.Galaxies.ContainsKey(tail))
                {
                    galaxyKey = tail;
                    arg = trimmed.Substring(0, atIdx).Trim();
                }
            }

            StarData target = null;
            string foundIn = null;

            if (!string.IsNullOrEmpty(galaxyKey))
            {
                if (!gm.Galaxies.TryGetValue(galaxyKey, out var g))
                { Log($"Галактика '{galaxyKey}' не найдена."); return; }
                target = ResolveStarArg(arg, g);
                if (target != null) foundIn = galaxyKey;
            }
            else
            {
                // Ищем по всем галактикам, приоритет — активной.
                if (gm.GeneratedGalaxy != null)
                {
                    target = ResolveStarArg(arg, gm.GeneratedGalaxy);
                    if (target != null) foundIn = gm.ActiveGalaxyKey;
                }
                if (target == null)
                    foreach (var kv in gm.Galaxies)
                    {
                        if (kv.Key == gm.ActiveGalaxyKey) continue;
                        var t = ResolveStarArg(arg, kv.Value);
                        if (t != null) { target = t; foundIn = kv.Key; break; }
                    }
            }

            if (target == null) { Log($"Звезда '{arg}' не найдена."); return; }
            gm.TeleportToStar(target);
            Log($"Телепорт → {target.Name} (галактика {foundIn}).");
        }
    }
}
