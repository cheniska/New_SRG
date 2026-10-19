using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Simulation;

namespace SRG.Ships
{
    public static class ShipFactory
    {
        public static ShipData BuildShipData(string shipTypeId, string ownerId, string raceId,
            Dictionary<string, ShipTypeConfig> availableShipTypes,
            GalaxyGenerationContext ctx = null)
        {
            if (!availableShipTypes.TryGetValue(shipTypeId, out var cfg))
            {
                Debug.LogWarning($"[ShipFactory] ShipType '{shipTypeId}' not found.");
                return null;
            }

            var data = new ShipData
            {
                Name = shipTypeId,
                ShipTypeId = shipTypeId,
                Owner = ownerId ?? string.Empty,
                Race  = raceId  ?? string.Empty,
                MinimapIconPath = cfg.MinimapIconPath,
                Skills = ShipSkills.FromBase(cfg.BaseSkills),
            };

            if (cfg.Animations != null)
                data.Animations = new Dictionary<string, ShipAnimationClip>(cfg.Animations);

            if (cfg.Thrusters != null)
            {
                data.Thrusters = new List<ShipThrusterData>();
                foreach (var t in cfg.Thrusters)
                    data.Thrusters.Add(new ShipThrusterData
                    {
                        LocalOffset = new Vector2(t.OffsetX, t.OffsetY),
                        SpritesheetPath = t.SpritesheetPath,
                        FPS = t.FPS
                    });
            }

            return data;
        }

        /// <summary>
        /// Перечитать текущую графику корабля (SpritesheetPath = override либо графика корпуса)
        /// и пересчитать SpriteWorldSize. Вызывать после установки/смены корпуса или маскировки.
        /// </summary>
        public static void RecalculateSpriteWorldSize(ShipData ship)
        {
            if (ship == null) return;
            ship.RefreshSpritesheetPath();
            float native = ResolveSpriteWorldSize(ship.SpritesheetPath);
            ship.SpriteWorldSize = native * HullSizeMultiplier(ship);
        }

        // ── Масштаб корпуса по размеру ─────────────────────────────────────────────
        // У корпуса Durability (MaxHull) == его размер/вес (см. EquipmentTemplate:
        // IsHullCategory → durability = size). На «базовом» размере спрайт равен
        // «игровому» (множитель 1); дальше растёт по степенному закону так, что при
        // удвоении размера корпуса множитель ровно HullSpriteDoubleMult (×1.25):
        //   mult = (MaxHull / base) ^ k, где k = log2(HullSpriteDoubleMult).
        // База зависит от класса: обычные корабли — Hull.BaseWeight (=500), станции —
        // их собственная база размера (=2000, см. EquipmentTemplate станционную формулу
        // 2000 + tier*250), иначе базовая станция раздувалась бы без причины.
        // Единый источник и для геймплейного SpriteWorldSize, и для визуала
        // (ShipVisualController.transform.localScale) — чтобы они не расходились.
        public const float HullSpriteBaseSize        = 500f;
        public const float HullSpriteBaseSizeStation = 2000f;
        public const float HullSpriteDoubleMult      = 1.25f;

        public static float HullSizeMultiplier(ShipData ship)
        {
            // Предметы/контейнеры (IsItem, MaxHull=1) размер по корпусу не масштабируют.
            if (ship == null || ship.IsItem) return 1f;
            float baseSize = ship.IsStation ? HullSpriteBaseSizeStation : HullSpriteBaseSize;
            return HullSizeMultiplier(ship.MaxHull, baseSize);
        }

        public static float HullSizeMultiplier(int maxHull, float baseSize = HullSpriteBaseSize)
        {
            if (maxHull <= 0 || baseSize <= 0f) return 1f;
            float k = Mathf.Log(HullSpriteDoubleMult, 2f);
            return Mathf.Pow(maxHull / baseSize, k);
        }

        /// <summary>
        /// Опциональные Owner/Race-подпапки поверх базового пути к графике корпуса
        /// (<c>hullItem.BodyGraphicPath</c>). Приоритет: Owner → Race → Default → базовый путь.
        /// Раса и тип корпуса обычно уже подставлены в имя файла, поэтому подпапок нет и
        /// возвращается базовый путь. Наличие листа — по <see cref="GraphicsCatalog"/>.
        /// </summary>
        public static string ResolveHullSheetPath(string basePath, string ownerId, string raceId)
        {
            if (string.IsNullOrEmpty(basePath)) return basePath;

            if (!string.IsNullOrEmpty(ownerId)
                && ownerId != GalaxyConstants.OWNER_NONE_KEY
                && ownerId != GalaxyConstants.OWNER_UNRESOLVED_KEY
                && ownerId != GalaxyConstants.OWNER_MIXED_KEY)
            {
                string p = $"{basePath}/{ownerId}";
                if (GraphicsCatalog.ContainsSheet(p)) return p;
            }

            if (!string.IsNullOrEmpty(raceId)
                && raceId != GalaxyConstants.RACE_NONE_KEY
                && raceId != GalaxyConstants.RACE_UNRESOLVED_KEY
                && raceId != GalaxyConstants.RACE_MIXED_KEY)
            {
                string p = $"{basePath}/{raceId}";
                if (GraphicsCatalog.ContainsSheet(p)) return p;
            }

            string defaultPath = $"{basePath}/Default";
            if (GraphicsCatalog.ContainsSheet(defaultPath)) return defaultPath;

            return basePath;
        }

        /// <summary>Мировой размер кадра листа (ширина) по каталогу; 1 — если листа нет.</summary>
        public static float ResolveSpriteWorldSize(string spritePath)
            => GraphicsCatalog.FrameWorldSize(spritePath);

        public static string GetRandomShipTypeForOwner(string ownerId,
            Dictionary<string, List<string>> ownerLineups,
            Dictionary<string, ShipTypeConfig> availableShipTypes,
            List<string> availableShipTypeKeys = null)
        {
            if (!string.IsNullOrEmpty(ownerId)
                && ownerLineups.TryGetValue(ownerId, out var lineup)
                && lineup.Count > 0)
                return lineup[GameRng.Range(0, lineup.Count)];

            if (availableShipTypeKeys != null && availableShipTypeKeys.Count > 0)
                return availableShipTypeKeys[GameRng.Range(0, availableShipTypeKeys.Count)];

            int count = availableShipTypes.Count;
            return count > 0 ? availableShipTypes.Keys.ElementAt(GameRng.Range(0, count)) : null;
        }
    }
}
