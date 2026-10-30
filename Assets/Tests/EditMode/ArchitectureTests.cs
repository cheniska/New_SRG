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

        // Симуляция не читает графику: размеры и варианты листов — из GraphicsCatalog.
        // Иначе ход зависит от рендера (2D/3D) и ждёт главный поток.
        private static readonly (Regex pattern, string why)[] GraphicsApis =
        {
            (new Regex(@"\bResources\.(Load|LoadAll|LoadAsync)\b"),
                "Resources в симуляции — данные о графике берите из GraphicsCatalog"),
            (new Regex(@"\b(Sprite\s*\[\]|Texture2D|SpriteRenderer)\b"),
                "графические типы в симуляции — данные о графике берите из GraphicsCatalog"),
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
            var violations = FindViolations(Forbidden);
            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void Simulation_DoesNotReadGraphics()
        {
            var violations = FindViolations(GraphicsApis);
            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        private static List<string> FindViolations((Regex pattern, string why)[] forbidden)
        {
            var violations = new List<string>();
            foreach (var file in SimulationSources())
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimStart();
                    if (line.StartsWith("//")) continue;
                    foreach (var (pattern, why) in forbidden)
                        if (pattern.IsMatch(line))
                            violations.Add($"{Path.GetFileName(file)}:{i + 1}: {why}");
                }
            }
            return violations;
        }

        /// <summary>Объекты под курсором выбираются по данным (WorldPicker), а не 2D-физикой:
        /// коллайдеры привязали бы выбор к спрайтовому рендеру.</summary>
        [Test]
        public void Scripts_DoNotUse2DPhysics()
        {
            var pattern = new Regex(@"\b(Physics2D|Collider2D|CircleCollider2D|BoxCollider2D|RaycastHit2D)\b");
            var violations = new List<string>();
            string root = Path.Combine(TestWorld.AssetsDir, "Scripts");
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (!lines[i].TrimStart().StartsWith("//") && pattern.IsMatch(lines[i]))
                        violations.Add($"{Path.GetFileName(file)}:{i + 1}: 2D-физика — выбирайте объекты через WorldPointer/WorldPicker");
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
