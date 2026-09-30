using NUnit.Framework;

namespace SRG.Tests
{
    /// <summary>
    /// Golden-тесты детерминизма: одинаковый сид + одинаковые действия = одинаковое состояние мира.
    /// Ловят и регрессии логики, и новые источники недетерминизма (UnityEngine.Random в симуляции,
    /// string.GetHashCode, Guid.NewGuid, зависимость от времени кадра, утечки статического состояния).
    /// </summary>
    [Category("Slow")]
    public class SimulationDeterminismTests
    {
        private const int Seed = 20260930;

        [Test]
        public void Generation_IsReproducible()
        {
            string a, b;
            using (var w = TestWorld.Generate(Seed)) a = w.StateHash();
            using (var w = TestWorld.Generate(Seed)) b = w.StateHash();
            Assert.AreEqual(a, b, "Два прогона генерации с одним сидом дали разные галактики");
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentGalaxies()
        {
            string a, b;
            using (var w = TestWorld.Generate(Seed)) a = w.StateHash();
            using (var w = TestWorld.Generate(Seed + 1)) b = w.StateHash();
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void Simulation_IsReproducible([Values(10)] int days)
        {
            string a, b;
            using (var w = TestWorld.Generate(Seed)) { w.RunDays(days); a = w.StateHash(); }
            using (var w = TestWorld.Generate(Seed)) { w.RunDays(days); b = w.StateHash(); }
            Assert.AreEqual(a, b, $"Состояние после {days} ходов отличается между прогонами");
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesState()
        {
            // Всё, что влияет на игру, должно пережить сохранение: орбиты планет, курсоры
            // фоновых сервисов, характеры NPC, индексы звёзд/планет (восстанавливаются при загрузке).
            TestWorld.Snapshot snapshot;
            string saved;
            using (var w = TestWorld.Generate(Seed))
            {
                w.RunDays(5);
                snapshot = w.TakeSnapshot();
                saved = w.StateHash();
            }
            using (var w = TestWorld.FromSnapshot(snapshot))
                Assert.AreEqual(saved, w.StateHash(), "Состояние после загрузки отличается от сохранённого");
        }

        [Test]
        [Ignore("Известное ограничение: текущее решение ИИ (NpcBrain: активность, приказ, директива) " +
                "не сохраняется и пересоздаётся после загрузки — NPC «передумывают». Снять Ignore, " +
                "когда NpcBrain начнёт сериализоваться (см. docs/modules/saves.md).")]
        public void SaveLoad_ContinuesIdentically()
        {
            const int before = 5, after = 5;
            string continued, reloaded;
            TestWorld.Snapshot snapshot;

            using (var w = TestWorld.Generate(Seed))
            {
                w.RunDays(before);
                snapshot = w.TakeSnapshot();
                w.RunDays(after);
                continued = w.StateHash();
            }
            using (var w = TestWorld.FromSnapshot(snapshot))
            {
                w.RunDays(after);
                reloaded = w.StateHash();
            }
            Assert.AreEqual(continued, reloaded, "После загрузки сейва игра пошла по-другому");
        }

        [Test]
        public void Simulation_AdvancesTurnCounter()
        {
            using var w = TestWorld.Generate(Seed);
            int start = w.Session.ActiveGalaxy.CurrentTurn;
            w.RunDays(3);
            Assert.AreEqual(start + 3, w.Session.ActiveGalaxy.CurrentTurn);
        }
    }
}
