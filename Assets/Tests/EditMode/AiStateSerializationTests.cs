using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.Simulation;

namespace SRG.Tests
{
    [Category("Slow")]
    public class AiStateSerializationTests
    {
        private static readonly Type[] WorldObjectTypes =
            { typeof(ShipData), typeof(StarData), typeof(PlanetData), typeof(GalaxyData), typeof(SectorData) };

        /// <summary>Все типы состояния ИИ (мозг, действия, приказы и вложенные типы).</summary>
        private static IEnumerable<Type> AiStateTypes() =>
            typeof(NpcBrain).Assembly.GetTypes().Where(t => !t.IsAbstract && AiStateContractResolver.IsAiStateType(t)
                && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false));

        [Test]
        public void AiState_HoldsWorldObjectsOnlyAsNonSerializedCaches()
        {
            // Ссылка на корабль/звезду в состоянии ИИ после загрузки указывала бы на копию объекта,
            // а не на объект мира. Правило: хранить UID, прямую ссылку — только как [NonSerialized]-кэш.
            var violations = new List<string>();
            foreach (var t in AiStateTypes())
                foreach (var f in AiStateContractResolver.StateFields(t))
                    if (WorldObjectTypes.Any(w => w.IsAssignableFrom(f.FieldType) || IsCollectionOf(f.FieldType, w)))
                        violations.Add($"{t.Name}.{f.Name} ({f.FieldType.Name})");
            Assert.IsEmpty(violations, "Поля ИИ со ссылками на объекты мира без [NonSerialized]:\n" + string.Join("\n", violations));
        }

        private static bool IsCollectionOf(Type t, Type element) =>
            t.IsGenericType && t.GetGenericArguments().Any(a => element.IsAssignableFrom(a));

        [Test]
        public void Brains_SurviveSaveLoad_WithSameActivities()
        {
            TestWorld.Snapshot snapshot;
            Dictionary<string, string> before;
            using (var w = TestWorld.Generate(4242))
            {
                w.RunDays(3);
                before = Activities(w);
                snapshot = w.TakeSnapshot();
            }
            Assert.IsNotEmpty(before, "В тестовом мире нет NPC с ИИ");

            using (var w = TestWorld.FromSnapshot(snapshot))
            {
                var after = Activities(w);
                CollectionAssert.AreEquivalent(before, after);
                // Характер общий с кораблём, а не копия.
                foreach (var ship in AllShips(w).Where(s => s.Brain != null))
                    Assert.AreSame(ship.Personality, ship.Brain.Personality, ship.Name);
            }
        }

        [Test]
        public void IncrementalShipCounters_MatchFullRecount()
        {
            using var w = TestWorld.Generate(777);
            w.RunDays(6);
            foreach (var g in w.Session.Galaxies.Values)
            {
                var live = g.ShipCounters;
                var fresh = new SRG.NpcAI.Spawning.GalaxyShipCounters();
                fresh.RecalculateFromScratch(g);
                CollectionAssert.AreEquivalent(fresh.ShipsBySectorAndType, live.ShipsBySectorAndType, g.Key + ": по секторам");
                CollectionAssert.AreEquivalent(fresh.ShipsByStarAndType, live.ShipsByStarAndType, g.Key + ": по звёздам");
            }
        }

        private static IEnumerable<ShipData> AllShips(TestWorld w) =>
            w.Session.Galaxies.Values.SelectMany(g => g.StarsMap.Values).SelectMany(s => s.Ships).Where(s => s != null);

        private static Dictionary<string, string> Activities(TestWorld w) =>
            AllShips(w).Where(s => s.Brain != null)
                .ToDictionary(s => s.Uid, s => s.Brain.CurrentActivity?.DebugName + "|" + s.Brain.CurrentOrder);
    }
}
