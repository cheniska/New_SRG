using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SRG.Config;
using SRG.Galaxy;
using SRG.Utils;
using UnityEngine;

namespace SRG.Tests
{
    /// <summary>
    /// Выбор объекта по точке (<see cref="WorldPicker"/>) — по данным, без коллайдеров и сцены.
    /// </summary>
    public class WorldPickerTests
    {
        private static GalaxyConfig Config() => new GalaxyConfig
        {
            Stars = new StarConfigSection { Types = new Dictionary<string, StarTypeData> { ["G"] = new StarTypeData { GraphicSizeMult = 4f } } },
            Planets = new PlanetConfigSection { Sizes = new Dictionary<string, PlanetSizeData> { ["Big"] = new PlanetSizeData { BaseScale = 2f } } },
        };

        private static StarData Star() => new StarData
        {
            Type = "G",
            Ships = new List<ShipData>(),
            Planets = new List<PlanetData>(),
            Asteroids = new List<AsteroidData>(),
        };

        private static ShipData Ship(float x, float y) => new ShipData { Position = new Vector2(x, y), CurrentHull = 10 };

        [Test]
        public void EmptySpace_ReturnsNull()
        {
            Assert.IsNull(WorldPicker.Pick(Star(), new Vector2(50f, 50f), Config(), null));
        }

        [Test]
        public void Star_RadiusScalesWithTypeSize()
        {
            var star = Star();
            // GraphicSizeMult = 4 → радиус 2.
            Assert.AreEqual(PickKind.Star, WorldPicker.Pick(star, new Vector2(1.9f, 0f), Config(), null)?.Kind);
            Assert.IsNull(WorldPicker.Pick(star, new Vector2(2.1f, 0f), Config(), null));
        }

        [Test]
        public void Planet_AtOrbitPosition_RadiusFromSize()
        {
            var star = Star();
            var planet = new PlanetData { Size = "Big", OrbitRadius = 1000f, CurrentAngle = 0f };
            star.Planets.Add(planet);
            Vector2 center = OrbitMath.GetPlanetWorldPosition(planet);

            var pick = WorldPicker.Pick(star, center + new Vector2(0.9f, 0f), Config(), null);
            Assert.AreSame(planet, pick?.Planet);
            Assert.IsNull(WorldPicker.Pick(star, center + new Vector2(1.1f, 0f), Config(), null));
        }

        [Test]
        public void Ship_DrawnAboveAsteroid_AndNearestShipWins()
        {
            var star = Star();
            var near = Ship(10.1f, 0f);
            var far = Ship(10.4f, 0f);
            star.Ships.Add(far);
            star.Ships.Add(near);
            star.Asteroids.Add(new AsteroidData { Position = new Vector2(10f, 0f), CollisionRadius = 0.75f });

            var pick = WorldPicker.Pick(star, new Vector2(10f, 0f), Config(), null);
            Assert.AreEqual(PickKind.Ship, pick.Kind);
            Assert.AreSame(near, pick.Ship);
        }

        [Test]
        public void HiddenShips_AreNotPickable()
        {
            var star = Star();
            var landed = Ship(10f, 0f); landed.LandedPlanetUid = "p";
            var docked = Ship(10f, 0f); docked.LandedOnShipUid = "c";
            var inTunnel = Ship(10f, 0f); inTunnel.HyperjumpPhase = HyperjumpPhase.HyperArrive;
            var dead = Ship(10f, 0f); dead.CurrentHull = 0;
            star.Ships.AddRange(new[] { landed, docked, inTunnel, dead });

            Assert.IsNull(WorldPicker.Pick(star, new Vector2(10f, 0f), Config(), null));
        }

        [Test]
        public void DestroyedAsteroid_IsNotPickable_WormholeKeepsItsStar()
        {
            var star = Star();
            star.Asteroids.Add(new AsteroidData { Position = new Vector2(20f, 0f), CollisionRadius = 0.5f, IsDestroyed = true });
            var wh = new WormholeData { Position = new Vector2(20f, 0f) };
            star.Wormholes = new List<WormholeData> { wh };

            var pick = WorldPicker.Pick(star, new Vector2(20.5f, 0f), Config(), null);
            Assert.AreSame(wh, pick?.Wormhole);
            Assert.AreSame(star, pick.WormholeStar);
        }

        [Test]
        public void GeneratedWorld_EveryVisibleShipIsPickableAtItsPosition()
        {
            using var w = TestWorld.Generate(2024);
            var cfg = w.Session.Context.Config;
            int checkedShips = 0;
            foreach (var star in w.Session.Galaxies.Values.SelectMany(g => g.StarsMap.Values))
                foreach (var ship in star.Ships.Where(WorldPicker.IsPickable))
                {
                    var pick = WorldPicker.Pick(star, ship.Position, cfg, null);
                    Assert.AreEqual(PickKind.Ship, pick?.Kind, ship.Name);
                    Assert.AreEqual(0f, (pick.Ship.Position - ship.Position).sqrMagnitude, 1e-6f, ship.Name);
                    checkedShips++;
                }
            Assert.Greater(checkedShips, 0);
        }
    }
}
