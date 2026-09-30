using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SRG.NpcAI.Spawning;

namespace SRG.Tests
{
    /// <summary>
    /// Корабельные счётчики спавна ведутся инкрементально (спавн/смерть/миграция), полный
    /// пересчёт — только при генерации и загрузке. Тест ловит изменения кораблей, прошедшие мимо
    /// хуков (смена владельца, типа, родной планеты, удаление без RegisterDeath).
    /// </summary>
    [Category("Slow")]
    public class ShipCountersTests
    {
        private const int Seed = 20260930;

        [Test]
        public void IncrementalCounters_MatchFullRecount([Values(10, 40)] int days)
        {
            using var w = TestWorld.Generate(Seed);
            w.RunDays(days);
            // Хук миграции срабатывает при завершении прыжка (после хода), а корабль физически
            // переходит в список новой звезды в начале следующего дня — до DailyTick спавна.
            // Сравниваем в той же точке, где раньше шёл ежедневный полный пересчёт.
            foreach (var g in w.Session.Galaxies.Values) g.MigrateShipsBetweenStars();
            foreach (var kv in w.Session.Galaxies)
            {
                var galaxy = kv.Value;
                var live = galaxy.ShipCounters;
                var full = new GalaxyShipCounters();
                full.RecalculateFromScratch(galaxy);

                AssertSame(full.ShipsByType, live.ShipsByType, kv.Key, "ShipsByType");
                AssertSame(full.ShipsBySide, live.ShipsBySide, kv.Key, "ShipsBySide");
                AssertSame(full.ShipsByRace, live.ShipsByRace, kv.Key, "ShipsByRace");
                AssertSame(full.ShipsByStarAndType, live.ShipsByStarAndType, kv.Key, "ShipsByStarAndType");
                AssertSame(full.ShipsByStarAndSide, live.ShipsByStarAndSide, kv.Key, "ShipsByStarAndSide");
                AssertSame(full.ShipsByPlanetAndType, live.ShipsByPlanetAndType, kv.Key, "ShipsByPlanetAndType");
                AssertSame(full.ShipsBySectorAndType, live.ShipsBySectorAndType, kv.Key, "ShipsBySectorAndType");
                AssertSame(Uids(full.ShipsAtStarByOwnerList), Uids(live.ShipsAtStarByOwnerList), kv.Key, "ShipsAtStarByOwnerList");
                AssertSame(Joined(full.OwnersAtStar), Joined(live.OwnersAtStar), kv.Key, "OwnersAtStar");
            }
        }

        private static Dictionary<(string, string), string> Uids(Dictionary<(string, string), List<SRG.Galaxy.ShipData>> map)
            => map.ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value.Select(s => s.Uid)));

        private static Dictionary<string, string> Joined(Dictionary<string, List<string>> map)
            => map.ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value));

        private static void AssertSame<TKey, TValue>(Dictionary<TKey, TValue> expected, Dictionary<TKey, TValue> actual,
            string galaxy, string name)
        {
            var diffs = new List<string>();
            foreach (var kv in expected)
                if (!actual.TryGetValue(kv.Key, out var v) || !Equals(v, kv.Value))
                    diffs.Add($"{kv.Key}: полный={kv.Value} инкрементальный={(actual.TryGetValue(kv.Key, out var a) ? a.ToString() : "—")}");
            foreach (var kv in actual)
                if (!expected.ContainsKey(kv.Key))
                    diffs.Add($"{kv.Key}: полный=— инкрементальный={kv.Value}");
            Assert.IsEmpty(diffs, $"[{galaxy}] {name} разошёлся с полным пересчётом:\n" + string.Join("\n", diffs.Take(20)));
        }
    }
}
