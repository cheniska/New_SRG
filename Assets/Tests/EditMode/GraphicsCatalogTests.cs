using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SRG.Simulation;
using SRG.Ships;

namespace SRG.Tests
{
    /// <summary>
    /// Каталог графики (Config/GraphicsCatalog.json): симуляция берёт из него размеры кораблей
    /// и варианты листов/картинок вместо чтения ассетов. Каталог генерируется
    /// tools/graphics-catalog/gen_graphics_catalog.py — эти тесты ловят забытую перегенерацию.
    /// </summary>
    public class GraphicsCatalogTests
    {
        private static string CatalogPath => Path.Combine(TestWorld.AssetsDir, "Config", "GraphicsCatalog.json");
        private static string ResourcesDir => Path.Combine(TestWorld.AssetsDir, "Resources");

        [TearDown]
        public void TearDown() => GraphicsCatalog.Load(null);

        [Test]
        public void Catalog_MatchesResources()
        {
            var file = JObject.Parse(File.ReadAllText(CatalogPath));
            var catalog = (JObject)file["Sheets"];
            var (expected, expectedImages) = ScanResources();
            const string regen = " — перегенерируйте: python3 tools/graphics-catalog/gen_graphics_catalog.py";

            var images = file["Images"].Select(t => (string)t).ToList();
            CollectionAssert.AreEquivalent(expectedImages, images, "Картинки в каталоге не совпадают с Resources" + regen);

            var missing = expected.Keys.Where(k => catalog[k] == null).ToList();
            var stale = catalog.Properties().Select(p => p.Name).Where(k => !expected.ContainsKey(k)).ToList();
            Assert.IsEmpty(missing, "Листы есть в Resources, но не в каталоге" + regen);
            Assert.IsEmpty(stale, "Листы есть в каталоге, но не в Resources" + regen);

            var wrong = new List<string>();
            foreach (var kv in expected)
            {
                int width = (int)catalog[kv.Key]["Width"], cols = (int)catalog[kv.Key]["Cols"];
                if (width != kv.Value.width || cols != kv.Value.cols)
                    wrong.Add($"{kv.Key}: каталог {width}/{cols}, ассет {kv.Value.width}/{kv.Value.cols}");
            }
            Assert.IsEmpty(wrong, "Размеры в каталоге устарели" + regen + "\n" + string.Join("\n", wrong));
        }

        [Test]
        public void FrameWorldSize_IsImportedFrameWidthOverPpu()
        {
            GraphicsCatalog.Load(@"{ ""PixelsPerUnit"": 100, ""Sheets"": {
                ""A/ship"": { ""Width"": 2048, ""Cols"": 8 } } }");
            Assert.AreEqual(2.56f, GraphicsCatalog.FrameWorldSize("A/ship"), 1e-6f);
            Assert.AreEqual(1f, GraphicsCatalog.FrameWorldSize("A/unknown"));
            Assert.AreEqual(1f, GraphicsCatalog.FrameWorldSize(null));
        }

        [Test]
        public void SheetsInFolder_ListsDirectChildrenSortedIgnoreCase()
        {
            GraphicsCatalog.Load(@"{ ""Sheets"": {
                ""Ast/Rocky/10"": { ""Width"": 10, ""Cols"": 1 },
                ""Ast/Rocky/09"": { ""Width"": 10, ""Cols"": 1 },
                ""Ast/Rocky/b"": { ""Width"": 10, ""Cols"": 1 },
                ""Ast/Rocky/A"": { ""Width"": 10, ""Cols"": 1 },
                ""Ast/Rocky/Deep/00"": { ""Width"": 10, ""Cols"": 1 },
                ""Ast/RockyX/00"": { ""Width"": 10, ""Cols"": 1 } } }");
            CollectionAssert.AreEqual(
                new[] { "Ast/Rocky/09", "Ast/Rocky/10", "Ast/Rocky/A", "Ast/Rocky/b" },
                GraphicsCatalog.SheetsInFolder("Ast/Rocky"));
            Assert.IsEmpty(GraphicsCatalog.SheetsInFolder("Ast/None"));
        }

        [Test]
        public void ImagesInFolder_SkipsSubfolders()
        {
            GraphicsCatalog.Load(@"{ ""Images"": [ ""P/Orbital/2"", ""P/Orbital/1"", ""P/Orbital/Unic/saturn"" ] }");
            CollectionAssert.AreEqual(new[] { "P/Orbital/1", "P/Orbital/2" }, GraphicsCatalog.ImagesInFolder("P/Orbital"));
            Assert.IsTrue(GraphicsCatalog.ContainsImage("P/Orbital/Unic/saturn"));
            Assert.IsFalse(GraphicsCatalog.ContainsSheet("P/Orbital/1"));
        }

        [Test]
        public void HullSheetPath_PrefersOwnerThenRaceThenDefault()
        {
            GraphicsCatalog.Load(@"{ ""Sheets"": {
                ""H/hull/Pirates"": { ""Width"": 10, ""Cols"": 1 },
                ""H/hull/Human"": { ""Width"": 10, ""Cols"": 1 },
                ""H/hull/Default"": { ""Width"": 10, ""Cols"": 1 } } }");
            Assert.AreEqual("H/hull/Pirates", ShipFactory.ResolveHullSheetPath("H/hull", "Pirates", "Human"));
            Assert.AreEqual("H/hull/Human", ShipFactory.ResolveHullSheetPath("H/hull", "Other", "Human"));
            Assert.AreEqual("H/hull/Default", ShipFactory.ResolveHullSheetPath("H/hull", null, null));
            Assert.AreEqual("H/other", ShipFactory.ResolveHullSheetPath("H/other", "Pirates", "Human"));
        }

        [Test]
        public void HeadlessWorld_ShipsGetSizesFromCatalog()
        {
            using var w = TestWorld.Generate(12345);
            var ships = w.Session.Galaxies.Values
                .SelectMany(g => g.Sectors).SelectMany(s => s.Stars).SelectMany(s => s.Ships)
                .Where(s => !string.IsNullOrEmpty(s.SpritesheetPath)).ToList();
            Assert.IsNotEmpty(ships);
            foreach (var ship in ships)
                Assert.AreEqual(GraphicsCatalog.FrameWorldSize(ship.SpritesheetPath) * ShipFactory.HullSizeMultiplier(ship),
                    ship.SpriteWorldSize, 1e-5f, ship.SpritesheetPath);
            Assert.IsTrue(ships.Any(s => GraphicsCatalog.ContainsSheet(s.SpritesheetPath)),
                "Ни один корпус не найден в каталоге — размеры кораблей не из графики");
        }

        // ── Пересчёт каталога по ассетам (та же логика, что в gen_graphics_catalog.py) ─────

        private static (Dictionary<string, (int width, int cols)> sheets, List<string> images) ScanResources()
        {
            var sheets = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            var images = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(ResourcesDir, "Graphics"), "*",
                         SearchOption.AllDirectories))
            {
                if (file.EndsWith(".png.json", StringComparison.Ordinal))
                {
                    string png = file.Substring(0, file.Length - ".json".Length);
                    int cols = (int)JObject.Parse(File.ReadAllText(png + ".json"))["Cols"];
                    sheets[ResourceKey(png)] = (ImportedWidth(png), cols);
                }
                else if ((file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                          || file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                         && !File.Exists(file + ".json"))
                {
                    images.Add(ResourceKey(file));
                }
            }
            return (sheets, images);
        }

        private static string ResourceKey(string file)
            => Path.GetRelativePath(ResourcesDir, Path.ChangeExtension(file, null))
                .Replace(Path.DirectorySeparatorChar, '/');

        // MyTexturePostprocessor выставляет этим папкам maxTextureSize = 8192 поверх .meta.
        private static readonly string[] Postprocessor8192 =
            { "Resources/Graphics/SpaceObjects/Stars", "Resources/Graphics/SpaceObjects/Planets/Textures" };

        /// <summary>Ширина текстуры после импорта: Unity ужимает её по большей стороне до maxTextureSize.</summary>
        private static int ImportedWidth(string png)
        {
            byte[] head = new byte[24];
            using (var f = File.OpenRead(png)) f.Read(head, 0, head.Length);
            int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];

            string meta = File.ReadAllText(png + ".meta");
            int limit = int.Parse(Regex.Match(meta, @"\n  maxTextureSize: (\d+)").Groups[1].Value);
            foreach (var block in Regex.Split(meta, @"\n  - serializedVersion: \d+\n"))
                if (block.Contains("buildTarget: Standalone") && Regex.IsMatch(block, @"\n    overridden: 1"))
                    limit = int.Parse(Regex.Match(block, @"maxTextureSize: (\d+)").Groups[1].Value);

            string unixPath = png.Replace(Path.DirectorySeparatorChar, '/');
            if (Postprocessor8192.Any(unixPath.Contains)) limit = 8192;

            if (Math.Max(w, h) <= limit) return w;
            return w >= h ? limit : (int)Math.Round((double)w * limit / h, MidpointRounding.ToEven);
        }
    }
}
