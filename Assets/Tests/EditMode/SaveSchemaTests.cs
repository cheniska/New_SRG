using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SRG.Simulation;

namespace SRG.Tests
{
    /// <summary>
    /// Фиксирует формат файла сохранения. Если тест упал — сериализуемая часть моделей изменилась
    /// (переименовано/удалено/добавлено поле, сменился тип, снят [JsonIgnore]):
    ///   1. Прочитает ли новая версия игры старые сейвы? Переименование или смена типа — нет:
    ///      увеличить SaveSerializer.CurrentVersion и добавить миграцию (см. docs/modules/saves.md).
    ///      Добавление нового поля обычно безопасно (в старом сейве будет значение по умолчанию).
    ///   2. Обновить эталон: запустить тесты с переменной окружения SRG_UPDATE_SAVE_SCHEMA=1
    ///      и закоммитить изменившийся SaveSchema.txt вместе с кодом.
    /// </summary>
    public class SaveSchemaTests
    {
        private static string SchemaPath => Path.Combine(TestWorld.AssetsDir, "Tests", "EditMode", "SaveSchema.txt");

        [Test]
        public void SaveFormat_MatchesCommittedSchema()
        {
            string current = SaveSchema.Describe();

            if (Environment.GetEnvironmentVariable("SRG_UPDATE_SAVE_SCHEMA") == "1")
            {
                File.WriteAllText(SchemaPath, current);
                Assert.Pass($"Эталон схемы обновлён: {SchemaPath}");
            }

            Assert.IsTrue(File.Exists(SchemaPath), $"Нет эталона схемы {SchemaPath} — запустите с SRG_UPDATE_SAVE_SCHEMA=1");
            string committed = File.ReadAllText(SchemaPath).Replace("\r\n", "\n");

            if (committed == current) return;

            var was = committed.Split('\n').Where(l => l.Length > 0).ToHashSet();
            var now = current.Split('\n').Where(l => l.Length > 0).ToHashSet();
            string removed = string.Join("\n", was.Except(now).OrderBy(x => x, StringComparer.Ordinal).Select(l => "  - " + l));
            string added = string.Join("\n", now.Except(was).OrderBy(x => x, StringComparer.Ordinal).Select(l => "  + " + l));
            Assert.Fail("Формат сохранения изменился. Нужна ли миграция старых сейвов? " +
                        "Затем обновите эталон (SRG_UPDATE_SAVE_SCHEMA=1).\n" +
                        (removed.Length > 0 ? "Удалено/изменено:\n" + removed + "\n" : "") +
                        (added.Length > 0 ? "Добавлено:\n" + added : ""));
        }

        [Test]
        public void Schema_CoversKeyModels()
        {
            string s = SaveSchema.Describe();
            foreach (var expected in new[] { "SaveData.Galaxies", "GalaxyData.Sectors", "StarData.Ships",
                                             "ShipData.Brain", "NpcBrain._currentActivity", "PlanetData.Settlement" })
                StringAssert.Contains(expected, s);
        }
    }
}
