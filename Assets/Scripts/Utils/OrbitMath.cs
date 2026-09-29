using UnityEngine;
using SRG.Core;
using SRG.Galaxy;
using SRG.Galaxy.Simulation;

namespace SRG.Utils
{
    /// <summary>
    /// Вычисления положения объектов на эллиптических орбитах.
    /// Используется в AsteroidSystem, SystemViewManager и NPC-действиях.
    /// </summary>
    public static class OrbitMath
    {
        /// <summary>
        /// Вычисляет мировую позицию планеты при заданном угле.
        /// Учитывает эксцентриситет и наклон орбиты.
        /// </summary>
        /// <param name="orbitRadiusUnits">Радиус орбиты в «игровых единицах» (делится на 100 внутри).</param>
        /// <param name="angleDeg">Текущий угол на орбите (градусы).</param>
        /// <param name="eccentricity">Эксцентриситет орбиты [0, 0.99].</param>
        /// <param name="orbitTiltDeg">Наклон плоскости орбиты (градусы).</param>
        public static Vector2 GetEllipticPosition(float orbitRadiusUnits, float angleDeg,
            float eccentricity, float orbitTiltDeg)
        {
            float a = SRUnits.ToWorld(orbitRadiusUnits);
            float e = Mathf.Clamp(eccentricity, 0f, 0.99f);
            float b = a * Mathf.Sqrt(1f - e * e);
            float c = a * e;
            float rad = angleDeg * Mathf.Deg2Rad;
            float lx = Mathf.Cos(rad) * a - c;
            float ly = Mathf.Sin(rad) * b;
            float tiltRad = orbitTiltDeg * Mathf.Deg2Rad;
            float cosT = Mathf.Cos(tiltRad), sinT = Mathf.Sin(tiltRad);
            return new Vector2(lx * cosT - ly * sinT, lx * sinT + ly * cosT);
        }

        /// <summary>
        /// Интерполирует положение планеты между двумя угловыми позициями.
        /// </summary>
        public static Vector2 GetEllipticPositionLerp(float orbitRadiusUnits,
            float fromAngleDeg, float toAngleDeg, float t,
            float eccentricity, float orbitTiltDeg)
        {
            float angle = Mathf.LerpAngle(fromAngleDeg, toAngleDeg, t);
            return GetEllipticPosition(orbitRadiusUnits, angle, eccentricity, orbitTiltDeg);
        }

        /// <summary>
        /// Вычисляет мировую позицию планеты в конкретный суб-ход.
        /// </summary>
        public static Vector2 GetPlanetPositionAtSubTurn(PlanetData planet, int subTurn)
        {
            float t = (float)subTurn / GalaxyData.SubTurnsPerTurn;
            return GetEllipticPositionLerp(
                planet.OrbitRadius,
                planet.PreviousAngle, planet.CurrentAngle, t,
                planet.OrbitEccentricity, planet.OrbitTiltDeg);
        }

        /// <summary>
        /// Вычисляет текущую мировую позицию планеты (по CurrentAngle).
        /// </summary>
        public static Vector2 GetPlanetWorldPosition(PlanetData planet)
            => GetEllipticPosition(planet.OrbitRadius, planet.CurrentAngle,
                planet.OrbitEccentricity, planet.OrbitTiltDeg);

        /// <summary>
        /// Простая позиция планеты без эксцентриситета и наклона — для NPC-логики.
        /// </summary>
        public static Vector2 GetPlanetPositionSimple(PlanetData planet)
        {
            float a = SRUnits.ToWorld(planet.OrbitRadius);
            float rad = planet.CurrentAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad) * a, Mathf.Sin(rad) * a);
        }

        /// <summary>
        /// Позиция планеты через N ходов вперёд относительно её текущего угла.
        /// </summary>
        public static Vector2 GetPlanetPositionAfterTurns(PlanetData planet, int turnsAhead)
        {
            if (planet == null) return Vector2.zero;
            float angle = planet.CurrentAngle;
            for (int i = 0; i < turnsAhead; i++)
                angle = AdvanceOrbitAngle(angle, planet.OrbitSpeed);
            return GetEllipticPosition(planet.OrbitRadius, angle,
                planet.OrbitEccentricity, planet.OrbitTiltDeg);
        }

        /// <summary>
        /// Итеративно предсказывает, где будет планета к моменту, когда корабль до неё долетит.
        /// Учитывает: ход планеты за каждый turn корабля. Сходится за 2–4 итерации.
        /// </summary>
        public static Vector2 PredictPlanetCatchUpPosition(
            PlanetData planet, Vector2 shipPos, float shipSpeedPerTurn)
        {
            if (planet == null) return Vector2.zero;
            Vector2 target = GetPlanetWorldPosition(planet);
            if (shipSpeedPerTurn < 0.0001f) return target;

            int prevN = -1;
            for (int iter = 0; iter < 5; iter++)
            {
                float dist = (target - shipPos).magnitude;
                int n = Mathf.Clamp(Mathf.CeilToInt(dist / shipSpeedPerTurn), 1, 1000);
                if (n == prevN) break;
                prevN = n;
                target = GetPlanetPositionAfterTurns(planet, n);
            }
            return target;
        }

        private static float AdvanceOrbitAngle(float current, float speed)
        {
            if (Mathf.Abs(speed) < 0.001f) return current;
            return Mathf.Repeat(current + 360f / speed, 360f);
        }
    }
}
