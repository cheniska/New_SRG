using System.Collections.Generic;
using NUnit.Framework;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;
using SRG.Simulation;

namespace SRG.Tests
{
    public class EconomyTests
    {
        [TearDown]
        public void TearDown() => GameWorld.ResetForTests();

        [Test]
        public void Inflation_GetFactor_DefaultsToOneWithoutGalaxy()
        {
            Assert.AreEqual(1f, InflationSystem.GetFactor(null));
        }

        [Test]
        public void Inflation_TickMonthly_AddsDelta()
        {
            var galaxy = new GalaxyData { InflationFactor = 1f };
            var cfg = new GalaxyConfig { Inflation = new InflationConfig { MonthlyDelta = 0.1f, MaxFactor = 5f } };
            InflationSystem.TickMonthly(galaxy, cfg);
            Assert.AreEqual(1.1f, galaxy.InflationFactor, 1e-5f);
        }

        [Test]
        public void Inflation_TickMonthly_ClampsToMax()
        {
            var galaxy = new GalaxyData { InflationFactor = 4.95f };
            var cfg = new GalaxyConfig { Inflation = new InflationConfig { MonthlyDelta = 0.1f, MaxFactor = 5f } };
            InflationSystem.TickMonthly(galaxy, cfg);
            Assert.AreEqual(5f, galaxy.InflationFactor, 1e-5f);
        }

        [Test]
        public void Trade_IsLegal_RespectsGovernmentBans()
        {
            var cfg = new GalaxyConfig
            {
                Races = new Dictionary<string, RaceConfig>
                {
                    ["Maloc"] = new RaceConfig
                    {
                        Trade = new RaceTradeConfig
                        {
                            BannedGoodsByGovernment = new Dictionary<string, List<string>>
                            {
                                ["Monarchy"] = new List<string> { "Narcotics" },
                            },
                        },
                    },
                },
            };
            var planet = new PlanetData { Race = "Maloc" };
            planet.Settlement.Government = "Monarchy";

            Assert.IsFalse(TradeSystem.IsLegal(planet, "Narcotics", cfg));
            Assert.IsTrue(TradeSystem.IsLegal(planet, "Food", cfg));

            planet.Settlement.Government = "Democracy";
            Assert.IsTrue(TradeSystem.IsLegal(planet, "Narcotics", cfg));
        }

        [Test]
        public void Trade_GetBaseStock_UsesSizeAndEconomyModifier_AndCaches()
        {
            var cfg = new GalaxyConfig
            {
                Trade = new TradeConfig { BaseStockBySize = new Dictionary<string, int> { ["Big"] = 300 } },
                Planets = new PlanetConfigSection
                {
                    EconomyTypes = new Dictionary<string, EconomyTypeConfig>
                    {
                        ["Industrial"] = new EconomyTypeConfig
                        {
                            Trade = new EconomyTradeConfig { GoodStockModifiers = new Dictionary<string, float> { ["Tech"] = 1.5f } },
                        },
                    },
                },
            };
            var planet = new PlanetData { Size = "Big" };
            planet.Settlement.EconomyType = "Industrial";

            Assert.AreEqual(450, TradeSystem.GetBaseStock(planet, "Tech", cfg));
            Assert.AreEqual(300, TradeSystem.GetBaseStock(planet, "Food", cfg));

            // Значение кэшируется в поселении: смена конфига не влияет до сброса кэша.
            cfg.Trade.BaseStockBySize["Big"] = 1;
            Assert.AreEqual(450, TradeSystem.GetBaseStock(planet, "Tech", cfg));
        }
    }
}
