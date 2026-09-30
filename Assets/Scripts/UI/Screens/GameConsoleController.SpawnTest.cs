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
        // ── spawntest ─────────────────────────────────────────────────────────────

        void SpawnTestShips()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.GeneratedGalaxy == null) { Log("No galaxy loaded."); return; }

            var ctx = gm.Context;
            if (ctx == null) { Log("Context not ready."); return; }

            int total = 5000;
            if (!string.IsNullOrEmpty(_lastArg) && int.TryParse(_lastArg, out int n) && n > 0)
                total = n;

            var stars = gm.GeneratedGalaxy.StarsMap.Values.ToList();
            if (stars.Count == 0) { Log("No stars in galaxy."); return; }

            // Собираем все доступные пары (race, shipTypeId) из расовых линеек
            var pairs = new List<(string race, string shipTypeId)>();
            foreach (var kv in ctx.RaceLineups)
                foreach (var typeId in kv.Value)
                    if (ctx.AvailableShipTypes.ContainsKey(typeId))
                        pairs.Add((kv.Key, typeId));

            if (pairs.Count == 0) { Log("No valid race/ship pairs in context."); return; }

            string defaultOwner = ctx.Config?.Settings?.DefaultOwner ?? GalaxyConstants.OWNER_NONE_KEY;
            int perStar = Mathf.Max(1, total / stars.Count);
            int spawned = 0;
            int pairIdx = 0;

            foreach (var star in stars)
            {
                for (int i = 0; i < perStar && spawned < total; i++, pairIdx++)
                {
                    var (race, shipTypeId) = pairs[pairIdx % pairs.Count];
                    string ownerId = defaultOwner;

                    var ship = ShipFactory.BuildShipData(shipTypeId, ownerId, race,
                                                         ctx.AvailableShipTypes, ctx);
                    if (ship == null) continue;

                    ship.Name            = $"TestShip_{spawned}";
                    ship.CurrentStarUid  = star.Uid;
                    ship.PreviousStarUid = star.Uid;
                    ship.CurrentStar     = star;

                    Vector2 pos = new Vector2(GameRng.Range(-5f, 5f),
                                              GameRng.Range(-5f, 5f));
                    ship.Position = ship.PreviousPosition = ship.TargetPosition = pos;

                    if (ctx.AvailableShipTypes.TryGetValue(shipTypeId, out var cfg)
                        && (cfg.StarterKit != null || cfg.StarterWeapons != null)
                        && ctx.ItemsConfig != null)
                        ItemFactory.EquipStarterKit(ship, cfg.StarterKit, ctx.ItemsConfig,
                                                    ctx.Config, cfg.StarterWeapons);
                    else
                    {
                        ship.MaxHull     = 100;
                        ship.CurrentHull = ship.MaxHull;
                    }

                    star.Ships.Add(ship);
                    spawned++;
                }
            }

            Log($"Spawned {spawned} test ships across {stars.Count} stars ({perStar}/star).");
        }

        void ClearTestShips()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.GeneratedGalaxy == null) { Log("No galaxy."); return; }

            int removed = 0;
            foreach (var star in gm.GeneratedGalaxy.StarsMap.Values)
            {
                int before = star.Ships.Count;
                star.Ships.RemoveAll(s => s.Name != null &&
                                          s.Name.StartsWith("TestShip_", StringComparison.Ordinal));
                removed += before - star.Ships.Count;
            }
            Log($"Removed {removed} test ships.");
        }
    }
}
