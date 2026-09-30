using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SRG.Tests;

namespace SRG.Bench
{
    /// <summary>
    /// Прогон N ходов headless-мира и статистика времени хода / аллокаций / GC.
    ///   dotnet run -c Release --project tools/bench -- [days=100] [seed=20260930] [warmup=5]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            int days = args.Length > 0 ? int.Parse(args[0]) : 100;
            int seed = args.Length > 1 ? int.Parse(args[1]) : 20260930;
            int warmup = args.Length > 2 ? int.Parse(args[2]) : 5;

            var swGen = Stopwatch.StartNew();
            using var w = TestWorld.Generate(seed);
            swGen.Stop();

            int stars = 0, ships = 0, planets = 0;
            foreach (var g in w.Session.Galaxies.Values)
                foreach (var s in g.StarsMap.Values) { stars++; ships += s.Ships.Count; planets += s.Planets.Count; }
            Console.WriteLine($"seed={seed} galaxies={w.Session.Galaxies.Count} stars={stars} planets={planets} ships={ships}");
            Console.WriteLine($"generate: {swGen.ElapsedMilliseconds} ms");

            w.RunDays(warmup);

            var times = new List<double>(days);
            long alloc0 = GC.GetTotalAllocatedBytes(true);
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            var total = Stopwatch.StartNew();
            var sw = new Stopwatch();
            for (int i = 0; i < days; i++)
            {
                sw.Restart();
                w.Host.Step();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            total.Stop();
            long alloc = GC.GetTotalAllocatedBytes(true) - alloc0;

            ships = 0;
            foreach (var g in w.Session.Galaxies.Values)
                foreach (var s in g.StarsMap.Values) ships += s.Ships.Count;

            times.Sort();
            double P(double q) => times[Math.Min(times.Count - 1, (int)(q * times.Count))];
            Console.WriteLine($"days={days} total={total.ElapsedMilliseconds} ms  ships(end)={ships}");
            Console.WriteLine($"turn ms: mean={times.Average():F1} p50={P(0.5):F1} p95={P(0.95):F1} max={times[^1]:F1}");
            Console.WriteLine($"alloc/turn: {alloc / days / 1024.0 / 1024.0:F2} MB  " +
                              $"GC gen0={GC.CollectionCount(0) - gc0} gen1={GC.CollectionCount(1) - gc1} gen2={GC.CollectionCount(2) - gc2}");
            Console.WriteLine($"state hash: {w.StateHash()[..16]}");
            return 0;
        }
    }
}
