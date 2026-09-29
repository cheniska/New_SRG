using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Ships;
using SRG.Ships.Services;

namespace SRG.Dialog
{
    /// <summary>
    /// Actions/tags для «списочных» приказов партнёру в диалоге (§9/§11 статьи):
    /// приказы «Лети в систему X» / «Садись на объект Y» / «Атакуй цель Z» выбираются не
    /// через отдельную UI-форму, а через список реплик в самом диалоге.
    ///
    /// Использование в JSON:
    /// <code>
    ///   { "OnEnter": [ { "Method": "NavListStars" } ], "Replies": [
    ///     { "Text": "→ {nav_star_0_name|—}", "Condition": "nav_star_0_name != \"\"",
    ///       "Actions": [ { "Method": "NavPickStarForPartner", "Args": ["0"] } ] },
    ///     { "Text": "→ {nav_star_1_name|—}", "Condition": "nav_star_1_name != \"\"",
    ///       "Actions": [ { "Method": "NavPickStarForPartner", "Args": ["1"] } ] }
    ///     ...
    ///   ] }
    /// </code>
    ///
    /// Actions:
    ///   NavListStars                    — соседи текущей звезды игрока (по прыжковой сетке).
    ///                                     заполняет nav_star_N_uid/name/dist на N=0..MAX-1.
    ///   NavListLandables                — все планеты и станции в текущей звезде,
    ///                                     nav_land_N_uid/name.
    ///   NavListAttackTargets            — все корабли-враги в текущей звезде,
    ///                                     nav_atk_N_uid/name.
    ///   NavPickStarForPartner &lt;idx&gt;    — приказ TargetShip: FlyToStar по nav_star_idx_uid.
    ///   NavPickLandableForPartner &lt;idx&gt; — приказ TargetShip: LandOn по nav_land_idx_uid.
    ///   NavPickAttackForPartner &lt;idx&gt;   — приказ TargetShip: Attack по nav_atk_idx_uid.
    ///
    /// Tags:
    ///   nav_star_N_uid   / nav_star_N_name / nav_star_N_dist
    ///   nav_land_N_uid   / nav_land_N_name
    ///   nav_atk_N_uid    / nav_atk_N_name
    /// </summary>
    public static class DialogNavActions
    {
        // Число слотов, зарезервированных под реплики. Больше — длиннее меню; в JSON нужны
        // соответствующие Reply'ы. 8 обычно хватает: у соседних звёзд редко бывает больше 6-7.
        private const int MAX_SLOTS = 8;

        private const string KEY_STAR_UID  = "nav_star_{0}_uid";
        private const string KEY_STAR_NAME = "nav_star_{0}_name";
        private const string KEY_STAR_DIST = "nav_star_{0}_dist";
        private const string KEY_LAND_UID  = "nav_land_{0}_uid";
        private const string KEY_LAND_NAME = "nav_land_{0}_name";
        private const string KEY_ATK_UID   = "nav_atk_{0}_uid";
        private const string KEY_ATK_NAME  = "nav_atk_{0}_name";

        // Куда пишется имя выбранной цели приказа — читают теги {order_object_name}/{order_star_name}
        // (в пулах это <ObjectName>/<Star>). Регистрируются в DialogActions.
        private const string KEY_ORDER_OBJECT = "order_object_name";
        private const string KEY_ORDER_STAR   = "order_star_name";

        public static void RegisterDefaults()
        {
            RegisterActions();
            RegisterTags();
        }

        private static void RegisterActions()
        {
            DialogService.RegisterAction("NavListStars", (ctx, _) =>
            {
                ClearSlots(ctx, KEY_STAR_UID, KEY_STAR_NAME, KEY_STAR_DIST);
                var star = ctx?.PlayerShip?.CurrentStar;
                var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
                if (star == null || galaxy?.StarsMap == null) return;

                // Ближайшие MAX_SLOTS звёзд по галакарте (без учёта прыжковой сетки — для UI
                // достаточно «куда можно приказать», выбор конкретной досягаемости остаётся
                // за партнёром/движением).
                var neighbors = new List<(StarData s, float d)>();
                foreach (var other in galaxy.StarsMap.Values)
                {
                    if (other == null || other.Uid == star.Uid) continue;
                    var dp = other.Position - star.Position;
                    neighbors.Add((other, dp.magnitude));
                }
                neighbors.Sort((a, b) => a.d.CompareTo(b.d));

                int count = Mathf.Min(neighbors.Count, MAX_SLOTS);
                for (int i = 0; i < count; i++)
                {
                    ctx.Data[string.Format(KEY_STAR_UID,  i)] = neighbors[i].s.Uid ?? "";
                    ctx.Data[string.Format(KEY_STAR_NAME, i)] = neighbors[i].s.Name ?? neighbors[i].s.Uid ?? "?";
                    ctx.Data[string.Format(KEY_STAR_DIST, i)] = Mathf.RoundToInt(neighbors[i].d).ToString(CultureInfo.InvariantCulture);
                }
            });

            DialogService.RegisterAction("NavListLandables", (ctx, _) =>
            {
                ClearSlots(ctx, KEY_LAND_UID, KEY_LAND_NAME, null);
                var star = ctx?.PlayerShip?.CurrentStar;
                if (star == null) return;

                int slot = 0;
                if (star.Planets != null)
                {
                    for (int i = 0; i < star.Planets.Count && slot < MAX_SLOTS; i++)
                    {
                        var p = star.Planets[i];
                        if (p == null) continue;
                        ctx.Data[string.Format(KEY_LAND_UID,  slot)] = p.Uid ?? "";
                        ctx.Data[string.Format(KEY_LAND_NAME, slot)] = p.Name ?? p.Uid ?? "?";
                        slot++;
                    }
                }
                if (star.Ships != null)
                {
                    for (int i = 0; i < star.Ships.Count && slot < MAX_SLOTS; i++)
                    {
                        var s = star.Ships[i];
                        if (s == null || s.CurrentHull <= 0) continue;
                        if (!s.IsStation && !CanBeLandedOnShip(s)) continue;
                        ctx.Data[string.Format(KEY_LAND_UID,  slot)] = s.Uid ?? "";
                        ctx.Data[string.Format(KEY_LAND_NAME, slot)] =
                            ListPrefix(s.IsStation) + (s.Name ?? s.Uid ?? "?");
                        slot++;
                    }
                }
            });

            DialogService.RegisterAction("NavListAttackTargets", (ctx, _) =>
            {
                ClearSlots(ctx, KEY_ATK_UID, KEY_ATK_NAME, null);
                var player = ctx?.PlayerShip;
                var star = player?.CurrentStar;
                if (player == null || star == null) return;

                var rel = SRG.Galaxy.Politics.OwnerRaceRelationsManager.Instance;
                var candidates = new List<(ShipData s, float d)>();
                for (int i = 0; i < star.Ships.Count; i++)
                {
                    var s = star.Ships[i];
                    if (s == null || s == player || s.CurrentHull <= 0) continue;
                    if (s.IsItem) continue;
                    // Отношения → только враги/недружественные, чтобы не рушить свои.
                    // Отсутствие менеджера отношений → пропускаем фильтр (в тестах / MVP).
                    if (rel != null && !rel.AreHostile(s, player)) continue;
                    float d = (s.Position - player.Position).sqrMagnitude;
                    candidates.Add((s, d));
                }
                candidates.Sort((a, b) => a.d.CompareTo(b.d));

                int count = Mathf.Min(candidates.Count, MAX_SLOTS);
                for (int i = 0; i < count; i++)
                {
                    ctx.Data[string.Format(KEY_ATK_UID,  i)] = candidates[i].s.Uid ?? "";
                    ctx.Data[string.Format(KEY_ATK_NAME, i)] = candidates[i].s.Name ?? candidates[i].s.Uid ?? "?";
                }
            });

            DialogService.RegisterAction("NavPickStarForPartner", (ctx, args) =>
            {
                var target = ctx?.TargetShip;
                if (target == null || args == null || args.Count == 0) return;
                string uid = ReadSlot(ctx, KEY_STAR_UID, args[0]);
                if (string.IsNullOrEmpty(uid)) return;
                // Имя цели пишем до проверки дисциплины: реплика отказа тоже называет систему.
                ctx.Data[KEY_ORDER_STAR] = ReadSlot(ctx, KEY_STAR_NAME, args[0]) ?? "";
                if (!DialogPartnerActions.ObeysOrder(ctx, target, "FlyToStar")) return;
                target.PartnerOrder = PartnerOrderKind.FlyToStar;
                target.PartnerOrderTargetUid = uid;
            });

            DialogService.RegisterAction("NavPickLandableForPartner", (ctx, args) =>
            {
                var target = ctx?.TargetShip;
                if (target == null || args == null || args.Count == 0) return;
                string uid = ReadSlot(ctx, KEY_LAND_UID, args[0]);
                if (string.IsNullOrEmpty(uid)) return;
                // «Совершаю посадку на <ObjectName>». Префикс «[Пл] / [Ст] / [Ко]» из списка
                // выбора в реплике не нужен — он ориентир для игрока в меню, а не часть фразы.
                ctx.Data[KEY_ORDER_OBJECT] = StripListPrefix(ReadSlot(ctx, KEY_LAND_NAME, args[0]));
                if (!DialogPartnerActions.ObeysOrder(ctx, target, "LandingToObject")) return;
                target.PartnerOrder = PartnerOrderKind.LandOn;
                target.PartnerOrderTargetUid = uid;
            });

            DialogService.RegisterAction("NavPickAttackForPartner", (ctx, args) =>
            {
                var target = ctx?.TargetShip;
                if (target == null || args == null || args.Count == 0) return;
                string uid = ReadSlot(ctx, KEY_ATK_UID, args[0]);
                if (string.IsNullOrEmpty(uid)) return;
                target.PartnerOrder = PartnerOrderKind.Attack;
                target.PartnerOrderTargetUid = uid;
                // «Я атакую <ShipName>» — реплики Pirate.AttackShipOk / Tranclucator.Attack.Ok.
                ctx.Data[KEY_ORDER_OBJECT] = ReadSlot(ctx, KEY_ATK_NAME, args[0]) ?? "";
            });
        }

        /// <summary>Пометка типа объекта в строке списка выбора. Обе фразы — в текст-конфиге:
        /// это то, что игрок читает в меню.</summary>
        private static string ListPrefix(bool isStation)
            => DialogTexts.Phrase(isStation ? "nav_list_station_prefix" : "nav_list_ship_prefix");

        /// <summary>Убирает пометку типа объекта: в готовой реплике («Совершаю посадку на …»)
        /// она смотрится мусором. Сравнение идёт с фактическими префиксами из конфига, а не с
        /// позициями символов — иначе смена формулировки в текстах ломала бы обрезку молча.</summary>
        private static string StripListPrefix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            foreach (bool station in new[] { true, false })
            {
                string prefix = ListPrefix(station);
                if (!string.IsNullOrEmpty(prefix)
                    && s.StartsWith(prefix, System.StringComparison.Ordinal))
                    return s.Substring(prefix.Length);
            }
            return s;
        }

        private static void RegisterTags()
        {
            // Слот-теги — прямые обёртки над ctx.Data с тем же именем, что и ключ.
            string[] keyFormats =
            {
                KEY_STAR_UID, KEY_STAR_NAME, KEY_STAR_DIST,
                KEY_LAND_UID, KEY_LAND_NAME,
                KEY_ATK_UID,  KEY_ATK_NAME,
            };
            for (int i = 0; i < MAX_SLOTS; i++)
                for (int k = 0; k < keyFormats.Length; k++)
                {
                    string key = string.Format(keyFormats[k], i);
                    DialogService.RegisterDataTag(key, key);
                }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static bool CanBeLandedOnShip(ShipData ship)
        {
            var types = GalaxyManager.Instance?.Context?.Config?.Ships?.ShipTypes;
            if (types == null || string.IsNullOrEmpty(ship.ShipTypeId)) return false;
            return types.TryGetValue(ship.ShipTypeId, out var cfg) && cfg.CanBeLandedOn;
        }

        private static void ClearSlots(DialogContext ctx, string keyU, string keyN, string keyExtra)
        {
            if (ctx?.Data == null) return;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                ctx.Data[string.Format(keyU, i)] = "";
                ctx.Data[string.Format(keyN, i)] = "";
                if (keyExtra != null) ctx.Data[string.Format(keyExtra, i)] = "";
            }
        }

        private static string ReadSlot(DialogContext ctx, string keyFmt, string idxArg)
        {
            if (!int.TryParse(idxArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)) return null;
            return ctx.ReadStr(string.Format(keyFmt, idx));
        }
    }
}
