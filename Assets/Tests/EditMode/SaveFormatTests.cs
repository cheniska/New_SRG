using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SRG.Simulation;

namespace SRG.Tests
{
    public class SaveFormatTests
    {
        [Test]
        public void MigrationChain_CoversEveryVersion()
        {
            // Шаг i переводит версию i в i+1 — поднимая CurrentVersion, нужно добавить шаг.
            Assert.AreEqual(SaveSerializer.CurrentVersion, SaveSerializer.MigrationCount);
        }

        [Test]
        public void DetectVersion_ByShape()
        {
            Assert.AreEqual(0, SaveSerializer.DetectVersion(JObject.Parse("{\"Sectors\":[]}")));
            Assert.AreEqual(1, SaveSerializer.DetectVersion(JObject.Parse("{\"Galaxies\":{}}")));
            Assert.AreEqual(7, SaveSerializer.DetectVersion(JObject.Parse("{\"Version\":7,\"Galaxies\":{}}")));
        }

        [Test]
        public void V0_SingleGalaxyRoot_IsMigrated()
        {
            var data = SaveSerializer.Deserialize("{\"Key\":\"Andromeda\",\"CurrentTurn\":12,\"Sectors\":[]}");
            Assert.AreEqual(0, data.Version);
            Assert.AreEqual("Andromeda", data.ActiveKey);
            Assert.AreEqual(12, data.Galaxies["Andromeda"].CurrentTurn);
            Assert.IsNull(data.RngState);
        }

        [Test]
        public void V1_ContainerWithoutVersion_IsMigrated()
        {
            var data = SaveSerializer.Deserialize(
                "{\"ActiveKey\":\"MilkyWay\",\"Galaxies\":{\"MilkyWay\":{\"Key\":\"MilkyWay\",\"CurrentTurn\":3}}}");
            Assert.AreEqual(1, data.Version);
            Assert.AreEqual(3, data.Galaxies["MilkyWay"].CurrentTurn);
        }

        [Test]
        public void CurrentVersion_RoundTrips_WithRngState()
        {
            var original = new SaveData
            {
                ActiveKey = "MilkyWay",
                Galaxies = { ["MilkyWay"] = new SRG.Galaxy.GalaxyData { Key = "MilkyWay", CurrentTurn = 42 } },
                RngState = new uint[] { 1, 2, 3, 4 },
            };
            string json = SaveSerializer.Serialize(original);
            StringAssert.Contains($"\"Version\": {SaveSerializer.CurrentVersion}", json);

            var back = SaveSerializer.Deserialize(json);
            Assert.AreEqual(SaveSerializer.CurrentVersion, back.Version);
            Assert.AreEqual(42, back.Galaxies["MilkyWay"].CurrentTurn);
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4 }, back.RngState);
        }

        [Test]
        public void FutureVersion_IsRejected()
        {
            string json = $"{{\"Version\":{SaveSerializer.CurrentVersion + 1},\"Galaxies\":{{\"A\":{{}}}}}}";
            Assert.Throws<InvalidSaveException>(() => SaveSerializer.Deserialize(json));
        }

        [TestCase("")]
        [TestCase("{ not json")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"Version\":2,\"Galaxies\":{}}")]
        public void BrokenSaves_AreRejected(string json)
        {
            Assert.Throws<InvalidSaveException>(() => SaveSerializer.Deserialize(json));
        }
    }
}
