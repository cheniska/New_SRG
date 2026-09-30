using System.Collections.Generic;
using NUnit.Framework;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.Simulation;

namespace SRG.Tests
{
    public class NewsServiceTests
    {
        [TearDown]
        public void TearDown() => GameWorld.ResetForTests();

        [Test]
        public void SyncNextId_ContinuesAfterMaxExistingId()
        {
            var a = new GalaxyData { News = new List<GalaxyNewsEntry> { new() { Id = 3 }, new() { Id = 41 } } };
            var b = new GalaxyData { News = new List<GalaxyNewsEntry> { new() { Id = 17 } } };
            GalaxyNewsService.SyncNextId(new[] { a, b });
            GameWorld.Attach(new FakeWorldHost { GeneratedGalaxy = a });

            GalaxyNewsService.Post(GalaxyNewsService.CAT_SYSTEM, "test");
            Assert.AreEqual(42, a.News[a.News.Count - 1].Id);
        }
    }
}
