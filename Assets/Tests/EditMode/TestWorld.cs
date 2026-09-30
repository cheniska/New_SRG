using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SRG.Galaxy;
using SRG.Simulation;
using SRG.Utils;

namespace SRG.Tests
{
    /// <summary>
    /// Headless-мир для тестов: реальные конфиги из Assets/Config, генерация по сиду,
    /// прогон ходов через <see cref="HeadlessWorldHost"/>.
    /// </summary>
    internal sealed class TestWorld : IDisposable
    {
        public HeadlessWorldHost Host { get; }
        public SimulationSession Session => Host.Session;

        /// <summary>Настройки сериализации — те же, что у файла сохранения.</summary>
        public static JsonSerializerSettings SaveSettings => SaveSerializer.Settings;

        private TestWorld(HeadlessWorldHost host) => Host = host;

        /// <summary>Папка Assets. В Unity тесты запускаются из корня проекта; вне Unity
        /// (tools/headless-tests) ищем вверх от папки сборки. Можно задать явно: SRG_ASSETS_DIR.</summary>
        public static string AssetsDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("SRG_ASSETS_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                {
                    for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    {
                        string candidate = Path.Combine(dir.FullName, "Assets");
                        if (File.Exists(Path.Combine(candidate, "Config", "GalaxyConfig.json"))) return candidate;
                    }
                }
                return Path.Combine(Directory.GetCurrentDirectory(), "Assets");
            }
        }

        public static ConfigSources LoadConfigs()
        {
            string dir = Path.Combine(AssetsDir, "Config");
            string Read(string name)
            {
                string p = Path.Combine(dir, name);
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            return new ConfigSources
            {
                Galaxy = Read("GalaxyConfig.json"),
                Texts = Read("TextsConfig.json"),
                Premade = Read("PremadeConfig.json"),
                Items = Read("ItemsConfig.json"),
                Dialogs = Read("DialogsConfig.json"),
                Partners = Read("PartnersConfig.json"),
            };
        }

        /// <summary>Свежий мир: сброс глобального состояния, конфиги, генерация по сиду.</summary>
        public static TestWorld Generate(int seed)
        {
            var host = CreateHost();
            if (!host.Session.GenerateAll(seed))
                throw new InvalidOperationException("Генерация галактик не удалась");
            return new TestWorld(host);
        }

        /// <summary>Мир из снимка (как загрузка сейва): галактики + состояние RNG.</summary>
        public static TestWorld FromSnapshot(Snapshot snapshot)
        {
            var host = CreateHost();
            var data = SaveSerializer.Deserialize(snapshot.Json);
            if (!host.Session.LoadFrom(data.Galaxies, data.ActiveKey))
                throw new InvalidOperationException("Снимок пуст");
            host.Session.RecountSpawns();
            GameRng.SetState(data.RngState);
            return new TestWorld(host);
        }

        private static HeadlessWorldHost CreateHost()
        {
            GameWorld.ResetForTests();
            var context = SimulationSetup.CreateContext(LoadConfigs(), settings: null)
                          ?? throw new InvalidOperationException($"Не удалось загрузить конфиги из {AssetsDir}");
            var host = new HeadlessWorldHost(new SimulationSession(context));
            GameWorld.Attach(host);
            return host;
        }

        public void RunDays(int days)
        {
            for (int i = 0; i < days; i++) Host.Step();
        }

        /// <summary>Снимок в формате файла сохранения (тот же сериализатор, что и в игре).</summary>
        public Snapshot TakeSnapshot() => new Snapshot
        {
            Json = SaveSerializer.Serialize(new SaveData
            {
                ActiveKey = Session.ActiveGalaxyKey,
                Galaxies = Session.Galaxies,
                RngState = GameRng.GetState(),
            }, Formatting.None),
        };

        /// <summary>
        /// SHA-256 состояния всех галактик в канонической форме: свойства объектов (и ключи словарей)
        /// отсортированы. Порядок обхода Dictionary после удалений зависит от истории вставок и не
        /// переживает сохранение — это не состояние игры, и код не должен от него зависеть.
        /// </summary>
        public string StateHash()
        {
            var token = Newtonsoft.Json.Linq.JToken.FromObject(Session.Galaxies, JsonSerializer.Create(SaveSettings));
            string json = Canonical(token).ToString(Formatting.None);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static Newtonsoft.Json.Linq.JToken Canonical(Newtonsoft.Json.Linq.JToken t)
        {
            switch (t)
            {
                case Newtonsoft.Json.Linq.JObject o:
                    var sorted = new Newtonsoft.Json.Linq.JObject();
                    foreach (var p in System.Linq.Enumerable.OrderBy(o.Properties(), x => x.Name, StringComparer.Ordinal))
                        sorted.Add(p.Name, Canonical(p.Value));
                    return sorted;
                case Newtonsoft.Json.Linq.JArray a:
                    var arr = new Newtonsoft.Json.Linq.JArray();
                    foreach (var x in a) arr.Add(Canonical(x));
                    return arr;
                default:
                    return t;
            }
        }

        public void Dispose() => GameWorld.ResetForTests();

        public sealed class Snapshot
        {
            public string Json;
        }
    }
}
