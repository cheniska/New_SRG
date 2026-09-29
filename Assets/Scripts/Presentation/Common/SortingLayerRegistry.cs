using UnityEngine;
using System.Collections.Generic;
using SRG.Ships.Movement;

namespace SRG.Presentation.Common
{
    public enum SortLayer
    {
        BackgroundVoid = -100,   // чёрная подложка
        BackgroundStarsFar = -95,      // дальние звёзды параллакса
        BackgroundStarsMid = -90,      // средние звёзды параллакса
        BackgroundNebulaFar = -89,     // процедурная туманность (дальний слой)
        BackgroundStarsMidNear = -87,  // промежуточные звёзды (крупнее, ближе)
        BackgroundNebulaMid = -86,     // процедурная туманность (средний слой)
        BackgroundStarsNear = -85,     // ближние звёзды параллакса
        BackgroundStarsBig     = -83,  // крупные фоновые звёзды с мерцанием
        Planet = 0,      // корневой объект планеты (не рендерит сам)
        SatelliteDown = 2,      // спутник «за» планетой
        PlanetSurface = 5,      // поверхность планеты (шейдер Custom/PlanetRotation)
        PlanetAtmosphere = 6,      // атмосфера / облака
        PlanetOrbital = 7,      // кольца / орбитальные объекты
        SatelliteUp = 8,      // спутник «перед» планетой
        Star = 10,     // корневой объект звезды
        Asteroid = 15,     // астероиды — между звездой (10) и кораблями (19+)
        TowBeam = 16,     // луч буксира/захвата — ниже предметов и кораблей
        DroppedItem = 17,     // контейнеры и свободные предметы в космосе — ниже кораблей
        ShipThruster = 19,     // шлейф двигателя (под корпусом)
        ShipNpc = 20,     // NPC-корабли, транспорты
        ShipBig = 22,     // крупные корабли / станции (было 25 — дублировало PathDot)
        ShipPlayer = 30,     // корабль игрока (всегда поверх NPC)

        PathDot = 25,     // точки маршрута (PathRenderer)
        HelperOverlay = 45,     // прочие helper-визуализации

        Interface = 100,    // все UI-элементы поверх всего
    }

    public static class SortingLayerRegistry
    {
        public static int Get(SortLayer layer) => (int)layer;
        private static readonly Dictionary<string, int> _byName = BuildNameMap();

        public static int Get(string layerName)
        {
            if (_byName.TryGetValue(layerName, out int value)) return value;
            UnityEngine.Debug.LogWarning($"[SortingLayerRegistry] Unknown layer name: '{layerName}'. Returning 0.");
            return 0;
        }
        public static int Offset(SortLayer layer, int delta) => (int)layer + delta;

        private static Dictionary<string, int> BuildNameMap()
        {
            var map = new Dictionary<string, int>();
            foreach (SortLayer layer in System.Enum.GetValues(typeof(SortLayer)))
                map[layer.ToString()] = (int)layer;
            return map;
        }
    }
}
