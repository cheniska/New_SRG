using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SRG.Config;
using SRG.Galaxy;
using SRG.Utils;

namespace SRG.Simulation
{
    /// <summary>Содержимое сейва: все галактики, активная, состояние RNG, версия формата.</summary>
    public sealed class SaveData
    {
        /// <summary>При чтении — версия, в которой файл был записан (до миграций); при записи — текущая.</summary>
        public int Version { get; set; }
        public string ActiveKey { get; set; }
        public Dictionary<string, GalaxyData> Galaxies { get; set; } = new();
        /// <summary>Состояние <see cref="GameRng"/>. null в сейвах до версии 2.</summary>
        public uint[] RngState { get; set; }
    }

    /// <summary>
    /// Формат файла сохранения: сериализация, версия и миграции старых версий.
    ///
    /// Миграции работают над JSON-деревом ДО десериализации: каждая поднимает версию на 1.
    /// При изменении формата: увеличить <see cref="CurrentVersion"/> и добавить шаг в
    /// <see cref="Migrations"/> (индекс = исходная версия). Тест SaveFormatTests проверяет,
    /// что цепочка покрывает все версии.
    ///
    /// Файловый ввод-вывод — в слое приложения (<c>SRG.Core.GalaxySaveManager</c>).
    /// </summary>
    public static class SaveSerializer
    {
        /// <summary>
        /// История версий:
        ///   0 — корень = один GalaxyData (до мультигалактики);
        ///   1 — контейнер { ActiveKey, Galaxies } без поля Version;
        ///   2 — + Version, RngState; типы в сборке SRG.Simulation.
        /// </summary>
        public const int CurrentVersion = 2;

        public static readonly JsonSerializerSettings Settings = new()
        {
            // Без Ignore Vector2.normalized/magnitude дают self-referencing loop.
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            // Полиморфные поля (Directive и т.п.) пишут $type; SaveTypeBinder ограничивает,
            // какие типы можно создать при загрузке.
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = SaveTypeBinder.Instance,
            // Состояние ИИ (NpcBrain/NpcAction/NpcOrder) — по полям, без вызова конструкторов.
            ContractResolver = new AiStateContractResolver(),
            // Vector2 — компактно {x,y}, без вычисляемых normalized/magnitude.
            Converters = { new Vector2JsonConverter() },
        };

        /// <summary>Шаг i переводит JSON версии i в версию i + 1.</summary>
        private static readonly Func<JObject, JObject>[] Migrations =
        {
            MigrateV0ToV1,
            MigrateV1ToV2,
        };

        public static int MigrationCount => Migrations.Length;

        public static string Serialize(SaveData data, Formatting formatting = Formatting.Indented)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.Version = CurrentVersion;
            return JsonConvert.SerializeObject(data, formatting, Settings);
        }

        /// <summary>
        /// Прочитать сейв любой поддерживаемой версии. Бросает <see cref="InvalidSaveException"/>,
        /// если формат не распознан или версия новее, чем умеет эта сборка игры.
        /// </summary>
        public static SaveData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidSaveException("Файл сохранения пуст.");

            JToken token;
            try { token = JToken.Parse(json); }
            catch (JsonReaderException e) { throw new InvalidSaveException($"Повреждённый JSON: {e.Message}", e); }
            if (token is not JObject root) throw new InvalidSaveException("Корень сохранения — не объект.");

            int version = DetectVersion(root);
            if (version > CurrentVersion)
                throw new InvalidSaveException(
                    $"Сохранение версии {version} создано более новой версией игры (поддерживается до {CurrentVersion}).");

            for (int v = version; v < CurrentVersion; v++)
                root = Migrations[v](root);

            var data = root.ToObject<SaveData>(JsonSerializer.Create(Settings))
                       ?? throw new InvalidSaveException("Не удалось прочитать сохранение.");
            if (data.Galaxies == null || data.Galaxies.Count == 0)
                throw new InvalidSaveException("В сохранении нет галактик.");
            data.Version = version;
            return data;
        }

        /// <summary>Версия JSON-дерева: явное поле Version, иначе по форме корня.</summary>
        public static int DetectVersion(JObject root)
        {
            var v = root["Version"];
            if (v != null && v.Type == JTokenType.Integer) return v.Value<int>();
            return root["Galaxies"] != null ? 1 : 0;
        }

        // ── Миграции ──────────────────────────────────────────────────────────

        /// <summary>v0 → v1: одиночный GalaxyData → контейнер мультигалактики.</summary>
        private static JObject MigrateV0ToV1(JObject root)
        {
            string key = root["Key"]?.Type == JTokenType.String && !string.IsNullOrEmpty((string)root["Key"])
                ? (string)root["Key"]
                : GalaxyConstants.DEFAULT_GALAXY_KEY;
            root["Key"] = key;
            return new JObject
            {
                ["ActiveKey"] = key,
                ["Galaxies"] = new JObject { [key] = root },
            };
        }

        /// <summary>v1 → v2: проставить версию. RngState отсутствует — RNG продолжит с текущего
        /// состояния; $type с "Assembly-CSharp" разрешает SaveTypeBinder.</summary>
        private static JObject MigrateV1ToV2(JObject root)
        {
            root["Version"] = 2;
            return root;
        }
    }

    public sealed class InvalidSaveException : Exception
    {
        public InvalidSaveException(string message) : base(message) { }
        public InvalidSaveException(string message, Exception inner) : base(message, inner) { }
    }
}
