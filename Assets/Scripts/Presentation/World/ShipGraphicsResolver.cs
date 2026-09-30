using SRG.Simulation;
using System.Collections.Generic;
using UnityEngine;
using SRG.Config;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Резолвит опциональные Owner/Race-подпапки поверх базового пути к графике корабля.
    ///
    /// Приоритет: Owner > Race > Default > базовый путь без суффикса.
    ///
    /// Основной кейс — <c>ShipData.RefreshSpritesheetPath</c>: базовым путём выступает
    /// <c>hullItem.BodyGraphicPath</c> (см. <see cref="EquipmentTemplate.GraphicCosmicTemplate"/>,
    /// шаблон <c>Graphics/Items/Equipment/Hull/Hull_&lt;Race&gt;_&lt;HullType&gt;_c</c>).
    /// Раса и HullType уже подставлены в имя файла, поэтому подпапки Owner/Race обычно
    /// отсутствуют — резолвер тогда возвращает базовый путь как есть.
    /// </summary>
    public static class ShipGraphicsResolver
    {
        private static readonly Dictionary<(string, string, string), string> _fromBaseCache = new();
        // Кэш HasSprites: путь → есть ли там графика. Одна проверка на путь через Resources.Load
        // вместо LoadAll<Sprite> (последнее стоило ~550ms при первом холодном хите).
        private static readonly Dictionary<string, bool> _hasSpritesCache = new();

        /// <summary>Очистить кэш (например, при перезагрузке игры).</summary>
        public static void ClearCache()
        {
            lock (_fromBaseCache) _fromBaseCache.Clear();
            _hasSpritesCache.Clear();
        }

        private static bool HasSprites(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (_hasSpritesCache.TryGetValue(path, out var cached)) return cached;
            // Дешёвая проверка: одна текстура вместо всего листа сплайсов. Если каталог
            // содержит любой Texture2D-ассет — считаем, что графика там есть. Никаких
            // Sprite.Create × N кадров при первом холодном хите.
            var tex = Resources.Load<Texture2D>(path);
            bool has = tex != null;
            _hasSpritesCache[path] = has;
            return has;
        }

        /// <summary>
        /// Резолвит подпапку к произвольному базовому пути.
        /// Приоритет: Owner → Race → Default → базовый путь без суффикса.
        /// </summary>
        public static string ResolveFromBase(string basePath, string ownerId, string raceId)
        {
            if (string.IsNullOrEmpty(basePath)) return basePath;
            var cacheKey = (basePath, ownerId ?? "", raceId ?? "");
            // Кэш — до обращения к главному потоку: из расчёта хода каждый Send стоит ожидания
            // кадра, а резолвер зовётся на каждый спавн/смену корпуса.
            lock (_fromBaseCache)
                if (_fromBaseCache.TryGetValue(cacheKey, out var cached)) return cached;
            // Resources — только с главного потока; из расчёта хода — через него.
            if (!MainThread.IsCurrent) return MainThread.Send(() => ResolveFromBase(basePath, ownerId, raceId));

            string result = ResolveFromBaseInternal(basePath, ownerId, raceId);
            lock (_fromBaseCache) _fromBaseCache[cacheKey] = result;
            return result;
        }

        private static string ResolveFromBaseInternal(string basePath, string ownerId, string raceId)
        {
            if (!string.IsNullOrEmpty(ownerId)
                && ownerId != GalaxyConstants.OWNER_NONE_KEY
                && ownerId != GalaxyConstants.OWNER_UNRESOLVED_KEY
                && ownerId != GalaxyConstants.OWNER_MIXED_KEY)
            {
                string p = $"{basePath}/{ownerId}";
                if (HasSprites(p)) return p;
            }

            if (!string.IsNullOrEmpty(raceId)
                && raceId != GalaxyConstants.RACE_NONE_KEY
                && raceId != GalaxyConstants.RACE_UNRESOLVED_KEY
                && raceId != GalaxyConstants.RACE_MIXED_KEY)
            {
                string p = $"{basePath}/{raceId}";
                if (HasSprites(p)) return p;
            }

            string defaultPath = $"{basePath}/Default";
            if (HasSprites(defaultPath)) return defaultPath;

            return basePath;
        }
    }
}
