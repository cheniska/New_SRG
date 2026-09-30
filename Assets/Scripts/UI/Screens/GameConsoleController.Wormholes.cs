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
        // ── Червоточины ──────────────────────────────────────────────────────────

        void ListWormholes()
        {
            var g = GalaxyManager.Instance?.GeneratedGalaxy;
            if (g == null) { Log("No galaxy."); return; }
            int n = 0;
            foreach (var (wh, src) in WormholeService.All())
            {
                if (wh == null) continue;
                var dst = !string.IsNullOrEmpty(wh.TargetStarUid) && g.StarsMap.TryGetValue(wh.TargetStarUid, out var t) ? t : null;
                string dstLabel = dst != null ? dst.Name
                                  : (!string.IsNullOrEmpty(wh.TargetGalaxyId) ? $"{wh.TargetGalaxyId}:{Short(wh.TargetStarUid)}"
                                     : Short(wh.TargetStarUid));
                string ttl = wh.Phase == WormholePhase.Open ? $"{wh.OpenTurnsRemaining} ходов до закрытия" : $"{wh.DaysInPhase} в фазе {wh.Phase}";
                Log($"  {Short(wh.Uid)}  {src?.Name ?? "?"} → {dstLabel}  [{wh.Phase}]  {ttl}");
                n++;
            }
            Log($"Активных червоточин: {n} (из {WormholeService.CountActive(g)} по CountActive).");
        }

        void SpawnWormholeCmd()
        {
            string arg = _lastArg?.Trim() ?? "";
            if (arg == "?" || arg == "help") { PrintWormholeSpawnHelp(); return; }

            var g = GalaxyManager.Instance?.GeneratedGalaxy;
            if (g == null) { Log("No galaxy."); return; }

            // Разбор key=value (пробелы между парами; значение без пробелов).
            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(arg))
            {
                foreach (var token in arg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = token.IndexOf('=');
                    if (eq <= 0) { Log($"Ожидался key=value, получено: '{token}'."); return; }
                    pairs[token.Substring(0, eq).Trim()] = token.Substring(eq + 1).Trim();
                }
            }

            // Резолвим источник и цель.
            StarData srcStar = pairs.TryGetValue("src", out var srcArg) ? ResolveStarArg(srcArg, g) : null;
            if (srcArg != null && srcStar == null && !string.Equals(srcArg, "random", StringComparison.OrdinalIgnoreCase))
            { Log($"src: не нашёл звезду '{srcArg}'."); return; }

            StarData dstStar = pairs.TryGetValue("dst", out var dstArg) ? ResolveStarArg(dstArg, g) : null;
            if (dstArg != null && dstStar == null && !string.Equals(dstArg, "random", StringComparison.OrdinalIgnoreCase))
            { Log($"dst: не нашёл звезду '{dstArg}'."); return; }

            // Позиции.
            Vector2? srcPos = null, dstPos = null;
            if (pairs.TryGetValue("srcPos", out var srcPosArg))
            {
                srcPos = ResolvePosArg(srcPosArg, srcStar, out var srcErr);
                if (srcPos == null) { Log($"srcPos: {srcErr}"); return; }
            }
            if (pairs.TryGetValue("dstPos", out var dstPosArg))
            {
                dstPos = ResolvePosArg(dstPosArg, dstStar, out var dstErr);
                if (dstPos == null) { Log($"dstPos: {dstErr}"); return; }
            }

            // Собираем WormholeLocation? параметры (null = случайно у сервиса).
            WormholeLocation? source = null;
            if (srcStar != null) source = srcPos.HasValue ? new Point(srcStar, srcPos.Value) : (WormholeLocation)srcStar;
            else if (srcPos.HasValue)
            { Log("srcPos без src не имеет смысла (нужна звезда-источник)."); return; }

            WormholeLocation? target = null;
            if (dstStar != null) target = dstPos.HasValue ? new Point(dstStar, dstPos.Value) : (WormholeLocation)dstStar;
            else if (dstPos.HasValue)
            { Log("dstPos без dst не имеет смысла."); return; }

            // TTL и межгалактика.
            int? ttl = null;
            if (pairs.TryGetValue("ttl", out var ttlArg))
            {
                if (!int.TryParse(ttlArg, out var t) || t <= 0) { Log($"ttl: ожидается положительное целое, получено '{ttlArg}'."); return; }
                ttl = t;
            }
            pairs.TryGetValue("galaxy", out var galaxyId);

            // Графика.
            WormholeGraphics gfx = null;
            if (pairs.ContainsKey("opening") || pairs.ContainsKey("cycle") || pairs.ContainsKey("closing") || pairs.ContainsKey("icon"))
            {
                pairs.TryGetValue("opening", out var op);
                pairs.TryGetValue("cycle",   out var cy);
                pairs.TryGetValue("closing", out var cl);
                pairs.TryGetValue("icon",    out var ic);
                gfx = WormholeGraphics.Of(op, cy, cl, ic);
            }

            var wh = WormholeService.Spawn(source, target, ttl, gfx, galaxyId);
            if (wh == null) { Log("Спавн не удался (см. лог WormholeService)."); return; }
            Log($"Спавн: {Short(wh.Uid)}  {(srcStar?.Name ?? "рандом")} → {(dstStar?.Name ?? "рандом")}  " +
                $"[Opening]  ttl={(ttl?.ToString() ?? "default")}  gfx={(gfx != null ? "custom" : "default")}");
        }

        void PrintWormholeSpawnHelp()
        {
            Log("wh_spawn — гибкий спавн червоточины. Формат: wh_spawn key=value key=value ...");
            Log("  src=<name|uid|player|random>   — звезда-источник (без ключа = случайная).");
            Log("  srcPos=<x,y|near[:r]|edge>     — точка входа. near:5 = случ. в r=5 от звезды; edge = 40..90% радиуса.");
            Log("  dst=<name|uid|random>          — целевая звезда (без ключа = случ. в MinDist..MaxDist).");
            Log("  dstPos=<x,y|near[:r]|edge>     — фиксированная точка выхода (иначе рандом на каждый вход).");
            Log("  ttl=<turns>                    — длительность фазы Open (по умолчанию — Wormhole_OpenTurns).");
            Log("  galaxy=<id>                    — межгалактический прыжок (обязателен dst с валидным Uid).");
            Log("  opening=<path>  cycle=<path>  closing=<path>  icon=<path>  — переопределение графики.");
            Log("Примеры:");
            Log("  wh_spawn");
            Log("  wh_spawn src=player srcPos=near:5");
            Log("  wh_spawn src=Alpha dst=Beta ttl=30");
            Log("  wh_spawn src=player dst=Sol dstPos=0,0 ttl=50 opening=Effects/Wh_Open");
        }

        void SpawnWormholeNearCmd()
        {
            var p = PlayerShip.Instance?.ShipData;
            if (p?.CurrentStar == null) { Log("Игрок не в звёздной системе."); return; }
            float radius = 5f;
            if (!string.IsNullOrEmpty(_lastArg) && float.TryParse(_lastArg,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0f)
                radius = r;
            var wh = WormholeService.Spawn(Positions.Near(p, radius));
            if (wh == null) { Log("Не удалось заспавнить."); return; }
            Log($"Спавн около игрока (r={radius}): {Short(wh.Uid)} → {Short(wh.TargetStarUid)}.");
        }
    }
}
