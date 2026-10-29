using System.Linq;
using NUnit.Framework;
using SRG.Equipment;
using SRG.Galaxy;

namespace SRG.Tests
{
    /// <summary>
    /// Рукописное оружие и артефакты — оборудование: ставятся в слоты, а не уходят в трюм.
    /// </summary>
    public class ItemFlagsTests
    {
        [Test]
        public void HandWrittenWeaponsAndArtefacts_AreEquipment()
        {
            using var w = TestWorld.Generate(31337);
            var cfg = w.Session.Context.ItemsConfig;
            foreach (var kind in new[] { "Weapons", "Artefacts" })
                foreach (var id in cfg.Items[kind].Keys)
                    Assert.IsTrue(cfg.GetItem(kind, id).IsEquipment, $"{kind}/{id}");
            foreach (var id in cfg.Items["Goods"].Keys)
                Assert.IsFalse(cfg.GetItem("Goods", id).IsEquipment, $"Goods/{id}");
        }

        [Test]
        public void GeneratedWarriors_HaveStarterWeaponsInSlots()
        {
            using var w = TestWorld.Generate(31337);
            var warriors = w.Session.ActiveGalaxy.StarsMap.Values.SelectMany(s => s.Ships)
                .Where(s => s.ShipTypeId == "Warrior").ToList();
            Assert.IsNotEmpty(warriors);
            foreach (var ship in warriors)
                Assert.IsNotEmpty(ship.Equipment.GetOccupiedSlotsOfCategory("Weapons"), ship.Name);
        }

        [Test]
        public void Reconciler_FixesItemsFromOldSaves()
        {
            using var w = TestWorld.Generate(31337);
            var galaxy = w.Session.ActiveGalaxy;
            var ship = galaxy.StarsMap.Values.SelectMany(s => s.Ships).First(s => s.ShipTypeId == "Warrior");
            var weapon = ship.AllItems.Values.First(i => i.Category == "Weapons");
            weapon.IsEquipment = false; // как в сейве, созданном до исправления

            Assert.Greater(ItemFlagsReconciler.Apply(galaxy, w.Session.Context.ItemsConfig), 0);
            Assert.IsTrue(weapon.IsEquipment);
        }
    }
}
