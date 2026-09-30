using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Ships.Movement;

namespace SRG.UI.Logic
{
    /// <summary>
    /// Логика карты галактики без UI: цвета звёзд/секторов/названий по владельцу и расе,
    /// цвета планет в подсказке, сводка кораблей «Пролонгера». Экран (<c>GalaxyMapController</c>)
    /// строит иконки, подписи и тултипы по этим данным.
    /// </summary>
    public static class GalaxyMapPresenter
    {
        public static readonly Color NeutralFill = new Color(0.2f, 0.22f, 0.28f);

        public static Color StarColor(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "red":    return new Color(1f,    0.35f, 0.25f);
                case "yellow": return new Color(1f,    0.92f, 0.45f);
                case "blue":   return new Color(0.45f, 0.70f, 1f);
                case "green":  return new Color(0.40f, 1f,    0.50f);
                case "white":  return new Color(0.90f, 0.92f, 1f);
                case "black":  return new Color(0.40f, 0.35f, 0.55f);
                case "purple": return new Color(0.75f, 0.40f, 1f);
                default:       return Color.white;
            }
        }
        public static Color ParseColor(string csv)
        {
            var p = csv.Split(',');
            if (p.Length >= 3
                && float.TryParse(p[0].Trim(), out float r)
                && float.TryParse(p[1].Trim(), out float g)
                && float.TryParse(p[2].Trim(), out float b))
                return new Color(r / 255f, g / 255f, b / 255f);
            return ColorUtility.TryParseHtmlString(csv, out var c) ? c : Color.white;
        }
        public static Color StarNameColor(StarData star, GalaxyGenerationContext ctx)
        {
            if (ctx == null) return new Color(0.82f, 0.82f, 0.82f);

            // Owner с цветом (Пираты, Синтеты)
            if (!string.IsNullOrEmpty(star.Owner)
                && star.Owner != GalaxyConstants.OWNER_NONE_KEY
                && ctx.Config.Ships?.Owners?.TryGetValue(star.Owner, out var ownerCfg) == true
                && !string.IsNullOrEmpty(ownerCfg.Color))
                return ParseColor(ownerCfg.Color);

            // Race
            if (!string.IsNullOrEmpty(star.Race)
                && star.Race != GalaxyConstants.RACE_NONE_KEY
                && star.Race != GalaxyConstants.RACE_MIXED_KEY
                && ctx.Config.Races?.TryGetValue(star.Race, out var race) == true
                && !string.IsNullOrEmpty(race.Color))
                return ParseColor(race.Color);

            return new Color(0.78f, 0.78f, 0.82f);
        }
        /// <summary>Цвет заливки ячейки Вороного для конкретной звезды.</summary>
        public static Color StarFillColor(StarData star, GalaxyGenerationContext ctx, bool byOwner)
        {
            if (star == null) return new Color(0.2f, 0.22f, 0.28f);

            // 1. Owner с заданным Color (Пираты, Синтеты)
            if (!string.IsNullOrEmpty(star.ResolvedOwnerId)
                && ctx?.Config?.Ships?.Owners?.TryGetValue(star.ResolvedOwnerId, out var owner) == true
                && !string.IsNullOrEmpty(owner.Color))
                return ParseColor(owner.Color);

            // 2. Owner без цвета (Coalition) → цвет расы звезды
            if (!string.IsNullOrEmpty(star.Race)
                && star.Race != GalaxyConstants.RACE_NONE_KEY
                && star.Race != GalaxyConstants.RACE_MIXED_KEY
                && ctx?.Config?.Races?.TryGetValue(star.Race, out var race) == true
                && !string.IsNullOrEmpty(race.Color))
                return ParseColor(race.Color);

            // 3. Mixed — доминирующая раса планет
            if (star.Race == GalaxyConstants.RACE_MIXED_KEY && star.Planets?.Count > 0)
            {
                var raceCounts = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var p in star.Planets)
                {
                    if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                    raceCounts.TryGetValue(p.Race, out int cnt);
                    raceCounts[p.Race] = cnt + 1;
                }
                string domRace = null; int maxCnt = 0;
                foreach (var kv in raceCounts) if (kv.Value > maxCnt) { maxCnt = kv.Value; domRace = kv.Key; }
                if (!string.IsNullOrEmpty(domRace)
                    && ctx?.Config?.Races?.TryGetValue(domRace, out var dr) == true
                    && !string.IsNullOrEmpty(dr.Color))
                    return ParseColor(dr.Color);
            }

            // 4. Fallback на сектор
            return SectorFillColor(star.ParentSector, ctx, byOwner);
        }
        public static Color SectorFillColor(SectorData sector, GalaxyGenerationContext ctx, bool byOwner)
        {
            if (sector == null) return new Color(0.2f, 0.22f, 0.28f);

            if (byOwner)
            {
                string ownerId = sector.ResolvedOwnerId ?? sector.Owner;
                if (!string.IsNullOrEmpty(ownerId) && ownerId != GalaxyConstants.OWNER_NONE_KEY
                    && ctx?.Config?.Ships?.Owners?.TryGetValue(ownerId, out var ownerCfg) == true
                    && !string.IsNullOrEmpty(ownerCfg.Color))
                    return ParseColor(ownerCfg.Color);
                return new Color(0.2f, 0.22f, 0.28f);
            }

            string raceId = sector.Race;
            if (!string.IsNullOrEmpty(raceId) && raceId != GalaxyConstants.RACE_NONE_KEY
                && ctx?.Config?.Races?.TryGetValue(raceId, out var raceC) == true
                && !string.IsNullOrEmpty(raceC.Color))
                return ParseColor(raceC.Color);

            return new Color(0.2f, 0.22f, 0.28f);
        }
        public static string PlanetNameColorHex(PlanetData planet, GalaxyGenerationContext ctx)
        {
            if (planet == null || string.IsNullOrEmpty(planet.Race) || planet.Race == GalaxyConstants.RACE_NONE_KEY)
                return "7B7B7B";
            if (ctx?.Config?.Races != null
                && ctx.Config.Races.TryGetValue(planet.Race, out var rc)
                && !string.IsNullOrEmpty(rc.Color))
                return ColorUtility.ToHtmlStringRGB(ParseColor(rc.Color));
            return "AAAAAA";
        }
        /// <summary>Показывает сводку кораблей в системе <paramref name="star"/>, если игрок
        /// находится в пределах Дальней антенны (<c>Artefacts.GalaxyMapScope</c>) от неё. Возвращает
        /// число дописанных строк (для расчёта высоты тултипа).</summary>
        public static int AppendProlongerShipsInfo(System.Text.StringBuilder sb, StarData star, ShipData player, GalaxyData galaxy)
        {
            if (star?.Ships == null || star.Ships.Count == 0) return 0;
            if (player == null) return 0;
            float scope = StatBus.SumShipCategory(player, EquipmentCategory.Artefacts, "GalaxyMapScope");
            if (scope <= 0f) return 0;
            if (galaxy == null || string.IsNullOrEmpty(player.CurrentStarUid)) return 0;
            if (!galaxy.StarsMap.TryGetValue(player.CurrentStarUid, out var playerStar)) return 0;
            if (HyperjumpController.CalcDistance(playerStar, star) > scope) return 0;

            // Сводим корабли по (SideKey, TypeKey), пропуская контейнеры/предметы и погибших.
            var groups = new Dictionary<string, int>();
            foreach (var s in star.Ships)
            {
                if (s == null || s.IsItem) continue;
                if (s.CurrentHull <= 0) continue;
                string side = string.IsNullOrEmpty(s.Owner) ? "?" : s.Owner;
                string type = string.IsNullOrEmpty(s.ShipTypeId) ? "?" : s.ShipTypeId;
                string key = side + " · " + type;
                groups.TryGetValue(key, out var c);
                groups[key] = c + 1;
            }
            if (groups.Count == 0) return 0;

            sb.AppendLine("<color=#8FD1FF>◈ обнаружено (Дальняя антенна):</color>");
            int lines = 1;
            foreach (var kv in groups)
            {
                sb.AppendLine($"<color=#B9C6D6>  {kv.Key}: {kv.Value}</color>");
                lines++;
                if (lines >= 8) { sb.AppendLine("<color=#B9C6D6>  …</color>"); lines++; break; }
            }
            return lines;
        }
    }
}
