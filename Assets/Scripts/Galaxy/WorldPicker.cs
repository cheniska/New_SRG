using UnityEngine;
using SRG.Config;
using SRG.Ships;
using SRG.Utils;

namespace SRG.Galaxy
{
    public enum PickKind
    {
        None,
        Star,
        Planet,
        Wormhole,
        Asteroid,
        Ship,
    }

    /// <summary>Объект системы под точкой — результат <see cref="WorldPicker.Pick"/>.
    /// Заполнено ровно одно поле по <see cref="Kind"/> (у червоточины ещё <see cref="WormholeStar"/>).</summary>
    public sealed class WorldPick
    {
        public PickKind     Kind;
        public StarData     Star;
        public PlanetData   Planet;
        public ShipData     Ship;
        public AsteroidData Asteroid;
        public WormholeData Wormhole;
        /// <summary>Звезда, в которой стоит червоточина (нужна для запроса прыжка через неё).</summary>
        public StarData     WormholeStar;

        /// <summary>Данные выбранного объекта — для сравнения «тот же ли объект».</summary>
        public object Target => (object)Ship ?? (object)Asteroid ?? (object)Wormhole ?? (object)Planet ?? Star;
    }

    /// <summary>
    /// Выбор объекта текущей системы по точке на плоскости мира — по данным, без коллайдеров и
    /// сцены. Работает при любом рендере (2D/3D) и в headless: вид только переводит курсор в точку
    /// плоскости (см. <c>SRG.Presentation.Common.WorldPointer</c>).
    ///
    /// Кликабельная область объекта — круг: позиция из данных, радиус из данных/конфига (те же
    /// размеры, что у визуала: 0.5 × масштаб объекта). Позиции — на конец хода, поэтому выбирать
    /// имеет смысл вне анимации хода (ввод игрока и так работает только в планировании).
    ///
    /// Если точка попала в несколько объектов, выигрывает тот, что рисуется поверх:
    /// корабль → астероид → червоточина → планета → звезда; внутри вида — ближайший к центру.
    /// </summary>
    public static class WorldPicker
    {
        /// <summary>Радиус кликабельной области: половина визуального размера объекта (масштаб 1 = 1 ед.).</summary>
        public const float BaseRadius = 0.5f;

        public static WorldPick Pick(StarData star, Vector2 point, GalaxyConfig config, GameSettingsConfig settings)
        {
            if (star == null) return null;
            var best = new Candidate();

            if (star.Ships != null)
                foreach (var ship in star.Ships)
                    if (IsPickable(ship))
                        best.Consider(PickKind.Ship, ship, ship.Position, ShipRadius(ship), point);

            if (star.Asteroids != null)
                foreach (var asteroid in star.Asteroids)
                    if (asteroid != null && !asteroid.IsDestroyed)
                        best.Consider(PickKind.Asteroid, asteroid, asteroid.Position, AsteroidRadius(asteroid), point);

            if (star.Wormholes != null)
            {
                float r = WormholeRadius(settings);
                foreach (var wh in star.Wormholes)
                    if (wh != null)
                        best.Consider(PickKind.Wormhole, wh, wh.Position, r, point);
            }

            if (star.Planets != null)
                foreach (var planet in star.Planets)
                    if (planet != null)
                        best.Consider(PickKind.Planet, planet, OrbitMath.GetPlanetWorldPosition(planet),
                            PlanetRadius(planet, config), point);

            best.Consider(PickKind.Star, star, Vector2.zero, StarRadius(star, config), point);

            return best.ToPick(star);
        }

        /// <summary>Виден ли корабль в космосе: севшие на планету/носитель, погибшие и стоящие
        /// невидимыми в гипертоннеле (HyperArrive) не выбираются.</summary>
        public static bool IsPickable(ShipData ship)
            => ship != null
               && ship.CurrentHull > 0
               && string.IsNullOrEmpty(ship.LandedPlanetUid)
               && string.IsNullOrEmpty(ship.LandedOnShipUid)
               && ship.HyperjumpPhase != HyperjumpPhase.HyperArrive;

        /// <summary>Визуал корабля масштабируется по корпусу (<see cref="ShipFactory.HullSizeMultiplier(ShipData)"/>).</summary>
        public static float ShipRadius(ShipData ship) => BaseRadius * ShipFactory.HullSizeMultiplier(ship);

        /// <summary>Визуал астероида — диаметр 2·CollisionRadius, зажатый в [0.2, 1.5].</summary>
        public static float AsteroidRadius(AsteroidData asteroid)
            => BaseRadius * Mathf.Clamp(asteroid.CollisionRadius * 2f, 0.2f, 1.5f);

        public static float WormholeRadius(GameSettingsConfig settings)
            => BaseRadius * (settings?.Wormhole_WorldSize ?? 1.5f);

        public static float PlanetRadius(PlanetData planet, GalaxyConfig config)
        {
            float scale = 1f;
            if (config?.Planets?.Sizes != null && planet.Size != null
                && config.Planets.Sizes.TryGetValue(planet.Size, out var size))
                scale = size.BaseScale;
            return BaseRadius * scale;
        }

        public static float StarRadius(StarData star, GalaxyConfig config)
        {
            float scale = 1f;
            if (config?.Stars?.Types != null && star.Type != null
                && config.Stars.Types.TryGetValue(star.Type, out var type))
                scale = type.GraphicSizeMult;
            return BaseRadius * scale;
        }

        private struct Candidate
        {
            private PickKind _kind;
            private object _target;
            private float _distSq;

            public void Consider(PickKind kind, object target, Vector2 center, float radius, Vector2 point)
            {
                float distSq = (point - center).sqrMagnitude;
                if (distSq > radius * radius) return;
                // PickKind упорядочен снизу вверх по отрисовке: больший — поверх.
                if (_target != null && (kind < _kind || (kind == _kind && distSq >= _distSq))) return;
                _kind = kind;
                _target = target;
                _distSq = distSq;
            }

            public WorldPick ToPick(StarData star)
            {
                if (_target == null) return null;
                var pick = new WorldPick { Kind = _kind };
                switch (_kind)
                {
                    case PickKind.Ship:     pick.Ship = (ShipData)_target; break;
                    case PickKind.Asteroid: pick.Asteroid = (AsteroidData)_target; break;
                    case PickKind.Wormhole: pick.Wormhole = (WormholeData)_target; pick.WormholeStar = star; break;
                    case PickKind.Planet:   pick.Planet = (PlanetData)_target; break;
                    case PickKind.Star:     pick.Star = (StarData)_target; break;
                }
                return pick;
            }
        }
    }
}
