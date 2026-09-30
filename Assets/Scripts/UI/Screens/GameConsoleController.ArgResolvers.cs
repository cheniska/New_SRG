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
        // ── Резолверы аргументов ─────────────────────────────────────────

        static StarData ResolveStarArg(string arg, GalaxyData g)
        {
            if (string.IsNullOrEmpty(arg) || string.Equals(arg, "random", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(arg, "player", StringComparison.OrdinalIgnoreCase))
                return PlayerShip.Instance?.ShipData?.CurrentStar;
            // Точный UID.
            if (g.StarsMap.TryGetValue(arg, out var byUid)) return byUid;
            // Имя (без учёта регистра).
            foreach (var s in g.StarsMap.Values)
                if (s != null && string.Equals(s.Name, arg, StringComparison.OrdinalIgnoreCase)) return s;
            // Префикс UID.
            foreach (var s in g.StarsMap.Values)
                if (s != null && s.Uid != null && s.Uid.StartsWith(arg, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        static Vector2? ResolvePosArg(string arg, StarData star, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(arg)) { error = "пусто"; return null; }
            if (string.Equals(arg, "random", StringComparison.OrdinalIgnoreCase))
            {
                if (star == null) { error = "random требует src/dst звезды"; return null; }
                return Positions.Near(star).Position;
            }
            if (string.Equals(arg, "edge", StringComparison.OrdinalIgnoreCase))
            {
                if (star == null) { error = "edge требует src/dst звезды"; return null; }
                return Positions.Near(star, -1f).Position;
            }
            if (arg.StartsWith("near", StringComparison.OrdinalIgnoreCase))
            {
                if (star == null) { error = "near требует src/dst звезды"; return null; }
                float r = -1f;
                int colon = arg.IndexOf(':');
                if (colon > 0 && float.TryParse(arg.Substring(colon + 1),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    r = parsed;
                return Positions.Near(star, r).Position;
            }
            // x,y
            int comma = arg.IndexOf(',');
            if (comma > 0)
            {
                var xs = arg.Substring(0, comma);
                var ys = arg.Substring(comma + 1);
                if (float.TryParse(xs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                 && float.TryParse(ys, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                    return new Vector2(x, y);
            }
            error = $"неизвестный формат '{arg}'";
            return null;
        }

        void CloseWormholeCmd()
        {
            if (string.IsNullOrEmpty(_lastArg)) { Log("Usage: wh_close <uid|prefix>"); return; }
            var wh = ResolveWormhole(_lastArg);
            if (wh == null) { Log($"Червоточина '{_lastArg}' не найдена."); return; }
            if (WormholeService.ForceClose(wh)) Log($"Закрываю {Short(wh.Uid)}.");
            else Log($"{Short(wh.Uid)} уже в фазе Closing.");
        }

        void DestroyWormholeCmd()
        {
            if (string.IsNullOrEmpty(_lastArg)) { Log("Usage: wh_destroy <uid|prefix>"); return; }
            var wh = ResolveWormhole(_lastArg);
            if (wh == null) { Log($"Червоточина '{_lastArg}' не найдена."); return; }
            if (WormholeService.Destroy(wh.Uid)) Log($"Удалено: {Short(wh.Uid)}.");
            else Log("Не удалось удалить.");
        }

        static WormholeData ResolveWormhole(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return null;
            var direct = WormholeService.Find(arg);
            if (direct != null) return direct;
            // Префикс — сравниваем начало UID.
            foreach (var (wh, _) in WormholeService.All())
                if (wh != null && wh.Uid != null &&
                    wh.Uid.StartsWith(arg, StringComparison.OrdinalIgnoreCase))
                    return wh;
            return null;
        }

        static string Short(string uid) => string.IsNullOrEmpty(uid) ? "—" : uid.Substring(0, Math.Min(6, uid.Length));

        // ──────────────────────────────────────────────────────────────────────────
    }
}
