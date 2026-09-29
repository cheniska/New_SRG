using UnityEngine;
using SRG.Galaxy;
using SRG.Presentation.Common;

namespace SRG.Utils
{
    /// <summary>
    /// Геометрия посадки на планету: визуальный радиус, зона посадки, зона подхода
    /// и точки прицеливания для игрока и НПС. См. docs/modules/landing.md.
    /// </summary>
    public static class PlanetGeometry
    {
        // Доп. поля вокруг визуального диска планеты:
        //   R_land = visualRadius + LandingZoneMargin  — внутри: триггер посадки (фаза Fading).
        //   R_app  = visualRadius + ApproachZoneMargin — снаружи: «approach», планировщик
        //                                                 удерживает цель ВНЕ R_land.
        // Калибровка к нашему масштабу мира (planet.BaseScale ≈ 1.0 для средней планеты).
        public const float LandingZoneMargin  = 0.5f;
        public const float ApproachZoneMargin = 1.5f;

        private const float DefaultVisualRadius = 0.5f;

        /// <summary>Визуальный радиус планеты в мировых единицах (по BaseScale * 0.5).</summary>
        public static float GetVisualRadius(PlanetData planet)
        {
            if (planet == null) return DefaultVisualRadius;
            var cfg = GraphicsManager.Instance != null ? GraphicsManager.Instance.GetConfig() : null;
            if (cfg?.Planets?.Sizes != null
                && !string.IsNullOrEmpty(planet.Size)
                && cfg.Planets.Sizes.TryGetValue(planet.Size, out var sizeData)
                && sizeData != null
                && sizeData.BaseScale > 0f)
                return sizeData.BaseScale * 0.5f;
            return DefaultVisualRadius;
        }

        /// <summary>R_land: расстояние от центра планеты, на котором срабатывает посадка.</summary>
        public static float GetLandingRadius(PlanetData planet)
            => GetVisualRadius(planet) + LandingZoneMargin;

        /// <summary>R_app: внешняя граница «approach zone» — внутри корабль уже близко,
        /// можно целиться напрямую, а не «парковаться» снаружи R_land.</summary>
        public static float GetApproachRadius(PlanetData planet)
            => GetVisualRadius(planet) + ApproachZoneMargin;

        public static float GetLandingRadiusSq(PlanetData planet)
        {
            float r = GetLandingRadius(planet);
            return r * r;
        }

        public static float GetApproachRadiusSq(PlanetData planet)
        {
            float r = GetApproachRadius(planet);
            return r * r;
        }

        // ─── Целеуказание для посадки ──────────────────────────────────────────

        /// <summary>Число «посадочных коридоров» вокруг планеты для НПС.</summary>
        private const int NpcApproachLanes = 8;

        /// <summary>
        /// Точка прицеливания для игрока. Вблизи (внутри R_app) — прямо в упреждённую позицию
        /// планеты. Издалека — в ближнюю к кораблю точку посадочного кольца упреждённой планеты:
        /// корабль пересекает R_land на подлёте и не пролетает через диск насквозь.
        /// </summary>
        public static Vector2 PredictPlayerLandingTarget(ShipData ship, PlanetData planet)
        {
            if (planet == null || ship == null) return ship?.Position ?? Vector2.zero;

            Vector2 shipPos = ship.Position;
            Vector2 future = OrbitMath.PredictPlanetCatchUpPosition(planet, shipPos, SRUnits.ToWorld(ship.ActualSpeed));

            float rApp = GetApproachRadius(planet);
            if ((shipPos - OrbitMath.GetPlanetWorldPosition(planet)).sqrMagnitude <= rApp * rApp)
                return future;

            Vector2 fromPlanet = shipPos - future;
            float dist = fromPlanet.magnitude;
            float entryDepth = GetLandingRadius(planet) * 0.6f;
            if (dist <= entryDepth) return future;
            return future + fromPlanet / dist * entryDepth;
        }

        /// <summary>
        /// Точка прицеливания для НПС. Вокруг планеты — <see cref="NpcApproachLanes"/> коридоров
        /// подхода; корабль берёт коридор, ближайший к своему пеленгу, со сдвигом на соседний
        /// по хешу пары «корабль–планета», и садится на свою глубину внутри R_land. Так несколько
        /// НПС, летящих с одной стороны, не сходятся в одну точку, а маршрут стабилен между ходами.
        /// </summary>
        public static Vector2 PredictNpcLandingTarget(ShipData ship, PlanetData planet)
        {
            if (planet == null || ship == null) return ship?.Position ?? Vector2.zero;

            Vector2 shipPos = ship.Position;
            Vector2 future = OrbitMath.PredictPlanetCatchUpPosition(planet, shipPos, SRUnits.ToWorld(ship.ActualSpeed));

            uint hash = unchecked((uint)((ship.Uid?.GetHashCode() ?? 0) * 31 + (planet.Uid?.GetHashCode() ?? 0)));
            float laneWidth = Angles.TwoPi / NpcApproachLanes;
            float bearing = Angles.Toward(OrbitMath.GetPlanetWorldPosition(planet), shipPos);
            int lane = Mathf.RoundToInt(bearing / laneWidth) + (int)(hash % 3) - 1;   // свой или соседний
            float depth = 0.45f + 0.35f * ((hash >> 4) & 0xFF) / 255f;              // доля R_land

            return future + Angles.Dir(lane * laneWidth) * (GetLandingRadius(planet) * depth);
        }
    }
}
