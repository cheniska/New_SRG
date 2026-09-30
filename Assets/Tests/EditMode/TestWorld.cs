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

        /// <summary>Настройки сериализации — те же, что у сейва (GalaxySaveManager).</summary>
        public static readonly JsonSerializerSettings SaveSettings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = SaveTypeBinder.Instance,
        };

        private TestWorld(HeadlessWorldHost host) => Host = host;

        /// <summary>Папка Assets. В Unity тесты запускаются из корня проекта; вне Unity
        /// путь можно задать переменной окружения SRG_ASSETS_DIR.</summary>
        public static string AssetsDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("SRG_ASSETS_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
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
            var container = JsonConvert.DeserializeObject<SnapshotContainer>(snapshot.Json, SaveSettings);
            if (!host.Session.LoadFrom(container.Galaxies, container.ActiveKey))
                throw new InvalidOperationException("Снимок пуст");
            host.Session.RecountSpawns();
            GameRng.SetState(snapshot.RngState);
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

        public Snapshot TakeSnapshot()
        {
            var container = new SnapshotContainer { ActiveKey = Session.ActiveGalaxyKey, Galaxies = Session.Galaxies };
            return new Snapshot
            {
                Json = JsonConvert.SerializeObject(container, SaveSettings),
                RngState = GameRng.GetState(),
            };
        }

        /// <summary>SHA-256 сериализованного состояния всех галактик.</summary>
        public string StateHash()
        {
            string json = JsonConvert.SerializeObject(Session.Galaxies, SaveSettings);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public void Dispose() => GameWorld.ResetForTests();

        public sealed class Snapshot
        {
            public string Json;
            public uint[] RngState;
        }

        private sealed class SnapshotContainer
        {
            public string ActiveKey { get; set; }
            public System.Collections.Generic.Dictionary<string, GalaxyData> Galaxies { get; set; }
        }
    }
}
