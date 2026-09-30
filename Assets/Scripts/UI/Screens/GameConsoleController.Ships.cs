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
        // ── ships ─────────────────────────────────────────────────────────────────

        void LogShips()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.GeneratedGalaxy == null) { Log("No galaxy."); return; }

            int totalShips     = 0;
            int starsWithShips = 0;
            int maxInStar      = 0;
            string maxStarName = "";
            var lines = new List<string>();

            foreach (var star in gm.GeneratedGalaxy.StarsMap.Values)
            {
                int cnt = star.Ships.Count;
                if (cnt == 0) continue;
                totalShips += cnt;
                starsWithShips++;
                if (cnt > maxInStar) { maxInStar = cnt; maxStarName = star.Name; }
                lines.Add($"  {star.Name}: {cnt}");
            }

            Log($"=== Ships: {totalShips} total in {starsWithShips}/{gm.GeneratedGalaxy.StarsMap.Count} stars ===");
            Log($"  Most: {maxStarName} ({maxInStar})");
            int shown = Mathf.Min(lines.Count, 30);
            for (int i = 0; i < shown; i++) Log(lines[i]);
            if (lines.Count > shown) Log($"  ...+{lines.Count - shown} more stars");
        }

        void SpawnStationCmd()
        {
            var gm = GalaxyManager.Instance;
            var star = gm?.CurrentStar;
            if (star == null) { Log("Нет текущей звезды."); return; }
            if (gm.Context == null) { Log("GenerationContext не готов."); return; }

            string code = string.IsNullOrWhiteSpace(_lastArg) ? null : _lastArg.Trim();
            var station = SpawnSystem.SpawnStationInStar(star, gm.Context, code);
            if (station == null) { Log("Спавн не удался (см. лог Unity)."); return; }

            SystemViewManager.Instance?.EnsureShipVisual(station);
            Log($"Станция {station.Name} [{station.ShipTypeId}] в '{star.Name}' " +
                $"(pos={station.Position.x:F1},{station.Position.y:F1}) uid={SpriteUtility.ShortId(station.Uid)}");
        }
    }
}
