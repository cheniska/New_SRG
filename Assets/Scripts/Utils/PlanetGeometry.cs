using UnityEngine;
using SRG.Galaxy;
using SRG.Simulation;
using SRG.Utils;

namespace SRG.Utils
{
    /// <summary>
    /// SR2HD landing-pipeline geometry: visual radius, landing zone, approach zone.
    /// См. docs/Ship_Landing_Pipeline.txt §3, §5.2.B.
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
            var cfg = GameWorld.Context?.Config;
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

        /// <summary>
        /// Точка прицеливания для игрока (SR2HD §5.2.B). Если ship ВНУТРИ R_app —
        /// целимся в catch-up позицию напрямую. Если ВНЕ — парковка на луче «ship → future»
        /// ВНЕ R_land с epsilon 0.05, чтобы не «въезжать в орбиту».
        /// </summary>
        public static Vector2 PredictPlayerLandingTarget(ShipData ship, PlanetData planet)
        {
            if (planet == null || ship == null) return ship?.Position ?? Vector2.zero;

            Vector2 shipPos = ship.Position;
            float speedPerTurn = SRUnits.ToWorld(ship.ActualSpeed);
            Vector2 future = OrbitMath.PredictPlanetCatchUpPosition(planet, shipPos, speedPerTurn);

            Vector2 currentPlanetPos = OrbitMath.GetPlanetWorldPosition(planet);
            float rApp = GetApproachRadius(planet);
            if ((shipPos - currentPlanetPos).sqrMagnitude <= rApp * rApp)
                return future;

            Vector2 toFuture = future - shipPos;
            float fullDist = toFuture.magnitude;
            if (fullDist < 0.001f) return future;

            float parkOffset = GetLandingRadius(planet) + 0.05f;
            if (fullDist <= parkOffset) return future;
            return future - (toFuture / fullDist) * parkOffset;
        }

        /// <summary>
        /// Точка прицеливания для НПС (SR2HD §5.2.C). approach_angle = угол (planet_now → ship)
        /// + детерминированный jitter ±20° (по seedFactor корабля), target = future_planet + dir * step.
        /// step = R_land снаружи R_app, R_land/2 + случайная половина внутри R_app.
        /// Это даёт разнообразие заходов (несколько НПС не подлетают строго в одну точку).
        /// </summary>
        public static Vector2 PredictNpcLandingTarget(ShipData ship, PlanetData planet)
        {
            if (planet == null || ship == null) return ship?.Position ?? Vector2.zero;

            Vector2 shipPos = ship.Position;
            float speed = SRUnits.ToWorld(ship.ActualSpeed);
            Vector2 curPlanet = OrbitMath.GetPlanetWorldPosition(planet);
            Vector2 futurePlanet = OrbitMath.PredictPlanetCatchUpPosition(planet, shipPos, speed);

            Vector2 fromPlanet = shipPos - curPlanet;
            float approachAngle = fromPlanet.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(fromPlanet.y, fromPlanet.x)
                : 0f;

            int seed = StableHash.Of(ship.Uid) ^ StableHash.Of(planet.Uid);
            float jitterDeg = ((seed & 0x7FFFFFFF) % 41) - 20;   // [-20, +20]
            approachAngle += jitterDeg * Mathf.Deg2Rad;

            float rLand = GetLandingRadius(planet);
            float rAppSq = GetApproachRadiusSq(planet);
            float distSq = fromPlanet.sqrMagnitude;
            float step = distSq > rAppSq
                ? rLand
                : rLand * 0.5f + rLand * 0.5f * (((seed >> 8) & 0xFF) / 255f);

            return futurePlanet
                 + new Vector2(Mathf.Cos(approachAngle), Mathf.Sin(approachAngle)) * step;
        }
    }
}
