using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SRG.Tests
{
    /// <summary>
    /// Правила, которые компилятор не проверяет: источники недетерминизма в симуляции.
    /// (Зависимости слоёв проверяются самими asmdef: SRG.Simulation не видит Presentation/Game.)
    /// </summary>
    public class ArchitectureTests
    {
        /// <summary>Папки сборки SRG.Simulation (см. *.asmref).</summary>
        private static readonly string[] SimulationFolders =
        {
            "Combat", "Config", "Economy", "Equipment", "Galaxy", "NpcAI", "Ships",
            "Science", "Dialog", "Scripting", "Utils", "Simulation",
        };

        private static readonly (Regex pattern, string why)[] Forbidden =
        {
            (new Regex(@"(?<![\w.])(UnityEngine\.)?Random\.(Range|value|insideUnitCircle|InitState)\b"),
                "UnityEngine.Random в симуляции — используйте GameRng"),
            (new Regex(@"\bGuid\.NewGuid\(\)"),
                "Guid.NewGuid в симуляции — используйте GameRng.NewUid()"),
            (new Regex(@"\bnew\s+System\.Random\(\s*\)"),
                "System.Random без сида — используйте GameRng.CreateSystemRandom()"),
            (new Regex(@"\.Uid\??\.GetHashCode\(\)"),
                "string.GetHashCode() зависит от рантайма — используйте StableHash.Of"),
        };

        private static IEnumerable<string> SimulationSources()
        {
            string root = Path.Combine(TestWorld.AssetsDir, "Scripts");
            return SimulationFolders
                .Select(f => Path.Combine(root, f))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.EndsWith("GameRng.cs"));
        }

        [Test]
        public void Simulation_HasNoNondeterministicApis()
        {
            var violations = new List<string>();
            foreach (var file in SimulationSources())
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimStart();
                    if (line.StartsWith("//")) continue;
                    foreach (var (pattern, why) in Forbidden)
                        if (pattern.IsMatch(line))
                            violations.Add($"{Path.GetFileName(file)}:{i + 1}: {why}");
                }
            }
            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void Simulation_SourcesAreFound()
        {
            Assert.Greater(SimulationSources().Count(), 100, $"Исходники не найдены в {TestWorld.AssetsDir}");
        }
    }
}
