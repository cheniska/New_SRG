using UnityEngine;
using Random = UnityEngine.Random;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.Utils
{
    /// <summary>
    /// Точка в пространстве галактики: звезда-хост + позиция в её системных координатах
    /// (звезда в начале координат). Позволяет пропускать <see cref="StarData"/>-контекст сквозь
    /// цепочки <see cref="Positions.Near"/>/<see cref="Positions.At"/>. Неявно конвертируется в
    /// <see cref="Vector2"/> (просто <c>.Position</c>) — сайты, работающие в известной звезде,
    /// продолжают писать <c>Vector2 pos = At(planet)</c>.
    /// </summary>
    public readonly struct Point
    {
        public readonly StarData Star;
        public readonly Vector2 Position;

        public Point(StarData star, Vector2 position) { Star = star; Position = position; }
        public Point(StarData star, float x, float y) : this(star, new Vector2(x, y)) { }

        public static implicit operator Vector2(Point p) => p.Position;
        public void Deconstruct(out StarData star, out Vector2 position) { star = Star; position = Position; }
    }

    /// <summary>
    /// Общие функции сэмплирования позиций в мировом пространстве системы. Единая реализация
    /// «случайная точка около X» — заменяет разбросанные inline <c>Random.insideUnitCircle * r</c>
    /// и <c>(Cos ang, Sin ang) * r</c>. Работает во всех подсистемах (спавн NPC, дроны, контейнеры,
    /// патрули, червоточины и т.д.).
    ///
    /// Возвращают <see cref="Vector2"/> в мировых координатах (звезда в начале координат).
    /// Опоры (Planet/Ship) клампятся к границе системы, чтобы результат не «улетал» наружу.
    /// </summary>
    public static class Positions
    {
        /// <summary>Радиус системы в мировых единицах: <c>star.SystemSize / 100</c>. 0 если star == null.</summary>
        public static float SystemRadius(StarData star) => star != null ? SRUnits.ToWorld(star.SystemSize) : 0f;

        // ── Абстрактное сэмплирование (без опоры) ─────────────────────────────────

        /// <summary>Случайная точка внутри диска [0, radius] (равномерно).</summary>
        public static Vector2 RandomInCircle(float radius)
            => radius > 0f ? Random.insideUnitCircle * radius : Vector2.zero;

        /// <summary>Случайная точка в кольце [rMin, rMax] (в мировых единицах).</summary>
        public static Vector2 RandomInRing(float rMin, float rMax)
        {
            float r = Random.Range(rMin, rMax);
            float ang = Random.Range(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
        }

        /// <summary>Случайная точка НА окружности радиуса <paramref name="radius"/> (равномерно по углу).</summary>
        public static Vector2 RandomOnCircle(float radius)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius);
        }

        // ── Near: случайная точка около опоры (одно имя, overloads по типу) ─────

        /// <summary>Случайная точка внутри системы. <paramref name="radius"/> &gt; 0 → в диске [0, radius];
        /// иначе — в кольце 40..90% радиуса системы (естественное распределение).</summary>
        public static Point Near(StarData star, float radius = -1f)
        {
            if (star == null) return default;
            float sysR = SystemRadius(star);
            Vector2 p = radius > 0f
                ? RandomInCircle(Mathf.Min(radius, sysR))
                : RandomInRing(sysR * 0.40f, sysR * 0.90f);
            return new Point(star, p);
        }

        /// <summary>Случайная точка в диске [0, radius] от текущей мировой позиции планеты.
        /// Клампится к границе системы (0.95·SystemRadius).</summary>
        public static Point Near(PlanetData planet, float radius)
        {
            if (planet == null) return default;
            Vector2 pos = OrbitMath.GetPlanetWorldPosition(planet) + RandomInCircle(radius);
            return new Point(planet.ParentStar, ClampToSystem(pos, planet.ParentStar));
        }

        /// <summary>Случайная точка в диске [0, radius] от корабля. Клампится к границе системы.</summary>
        public static Point Near(ShipData ship, float radius)
        {
            if (ship == null) return default;
            var star = ship.CurrentStar;
            if (star == null && !string.IsNullOrEmpty(ship.CurrentStarUid))
                GameWorld.GeneratedGalaxy?.StarsMap.TryGetValue(ship.CurrentStarUid, out star);
            return new Point(star, ClampToSystem(ship.Position + RandomInCircle(radius), star));
        }

        /// <summary>Случайная точка в диске [0, radius] от заданной <see cref="Point"/>. Клампится
        /// к системе точки. Используется для композиции: <c>Near(At(planet), 5)</c> — 5 ед. вокруг
        /// текущей позиции планеты с корректным клампингом (у Point всегда есть Star).</summary>
        public static Point Near(Point anchor, float radius)
            => new Point(anchor.Star, ClampToSystem(anchor.Position + RandomInCircle(radius), anchor.Star));

        /// <summary>Runtime-диспатч (для скриптов/UI где тип известен только в момент вызова).</summary>
        public static Point Near(object anchor, float radius) => anchor switch
        {
            StarData s => Near(s, radius),
            PlanetData p => Near(p, radius),
            ShipData sh => Near(sh, radius),
            Point pt => Near(pt, radius),
            _ => default,
        };

        private static Vector2 ClampToSystem(Vector2 pos, StarData star)
        {
            if (star == null) return pos;
            float sysR = SystemRadius(star);
            return pos.magnitude > sysR ? pos.normalized * sysR * 0.95f : pos;
        }

        // ── At: текущая позиция объекта (без случайности) ────────────────────────

        /// <summary>Позиция «звезды» — начало координат системы; Star заполнен.</summary>
        public static Point At(StarData star) => new Point(star, Vector2.zero);

        /// <summary>Текущая мировая позиция планеты + её ParentStar.</summary>
        public static Point At(PlanetData planet)
            => planet != null
                ? new Point(planet.ParentStar, OrbitMath.GetPlanetWorldPosition(planet))
                : default;

        /// <summary>Позиция корабля + его CurrentStar.</summary>
        public static Point At(ShipData ship)
        {
            if (ship == null) return default;
            var star = ship.CurrentStar;
            if (star == null && !string.IsNullOrEmpty(ship.CurrentStarUid))
                GameWorld.GeneratedGalaxy?.StarsMap.TryGetValue(ship.CurrentStarUid, out star);
            return new Point(star, ship.Position);
        }

        /// <summary>Явная точка (star, x, y) — эквивалент <c>new Point(star, x, y)</c>. Удобно для DSL
        /// вида <c>Spawn(At(star, 3, 2))</c>.</summary>
        public static Point At(StarData star, float x, float y) => new Point(star, x, y);

        /// <summary>Runtime-диспатч At (для скриптов/UI).</summary>
        public static Point At(object anchor) => anchor switch
        {
            StarData s => At(s),
            PlanetData p => At(p),
            ShipData sh => At(sh),
            Point pt => pt,
            _ => default,
        };
    }
}
