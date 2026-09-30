using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using SRG.Galaxy;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Core
{
    /// <summary>Контейнер сейва для мультигалактики: словарь галактик + ключ активной.
    /// Обратно совместим со старыми сейвами, где корень = <see cref="GalaxyData"/> напрямую.</summary>
    public class GalaxySaveContainer
    {
        public string ActiveKey { get; set; }
        public Dictionary<string, GalaxyData> Galaxies { get; set; } = new();
        /// <summary>Состояние <see cref="GameRng"/> на момент сохранения — после загрузки игра
        /// продолжается той же последовательностью случайных чисел. null в старых сейвах.</summary>
        public uint[] RngState { get; set; }
    }

    public static class GalaxySaveManager
    {
        private static string SavePath => RuntimePaths.SaveFile("galaxy_save.json");

        private static readonly JsonSerializerSettings SaveSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = SRG.Utils.SaveTypeBinder.Instance
        };

        // ── Мультигалактика ───────────────────────────────────────────────────────

        public static void SaveGalaxies(Dictionary<string, GalaxyData> galaxies, string activeKey)
        {
            if (galaxies == null || galaxies.Count == 0)
            {
                Debug.LogWarning("[GalaxySaveManager] Nothing to save."); return;
            }
            var container = new GalaxySaveContainer
            {
                ActiveKey = activeKey,
                Galaxies = galaxies,
                RngState = GameRng.GetState(),
            };
            string json = JsonConvert.SerializeObject(container, Formatting.Indented, SaveSettings);
            File.WriteAllText(SavePath, json);
            Debug.Log($"[GalaxySaveManager] Saved {galaxies.Count} galaxies (active={activeKey}) to: {SavePath}");
        }

        /// <summary>Загружает все галактики из сейва. Возвращает пустой словарь при неудаче.
        /// Совместимость: старый формат (корень = GalaxyData) читается как единственная запись
        /// под ключом <see cref="Config.GalaxyConstants.DEFAULT_GALAXY_KEY"/>.</summary>
        public static Dictionary<string, GalaxyData> LoadGalaxies(out string activeKey)
        {
            activeKey = null;
            if (!File.Exists(SavePath))
            {
                Debug.LogError("[GalaxySaveManager] Save file not found.");
                return null;
            }
            try
            {
                string text = File.ReadAllText(SavePath);
                var root = JToken.Parse(text);
                // Новый формат: контейнер {ActiveKey, Galaxies:{}}.
                if (root.Type == JTokenType.Object && root["Galaxies"] != null)
                {
                    var container = JsonConvert.DeserializeObject<GalaxySaveContainer>(text, SaveSettings);
                    if (container?.Galaxies == null) return null;
                    foreach (var g in container.Galaxies.Values) g?.InitializeLookups();
                    activeKey = container.ActiveKey;
                    // Восстанавливаем ПОСЛЕ десериализации: конструкторы моделей берут UID из GameRng.
                    GameRng.SetState(container.RngState);
                    return container.Galaxies;
                }
                // Старый формат: корень = GalaxyData.
                var single = JsonConvert.DeserializeObject<GalaxyData>(text, SaveSettings);
                if (single == null) return null;
                single.InitializeLookups();
                var key = string.IsNullOrEmpty(single.Key) ? SRG.Config.GalaxyConstants.DEFAULT_GALAXY_KEY : single.Key;
                if (string.IsNullOrEmpty(single.Key)) single.Key = key;
                activeKey = key;
                return new Dictionary<string, GalaxyData> { [key] = single };
            }
            catch (Exception e)
            {
                Debug.LogError($"[GalaxySaveManager] Load failed: {e.Message}");
                return null;
            }
        }

        // ── Легаси-API (одна галактика) ──────────────────────────────────────────
        // Оставлено для внешних вызовов, если такие есть; внутренне — не используется.

        public static void SaveGalaxy(GalaxyData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[GalaxySaveManager] Nothing to save."); return;
            }
            var key = string.IsNullOrEmpty(data.Key) ? SRG.Config.GalaxyConstants.DEFAULT_GALAXY_KEY : data.Key;
            SaveGalaxies(new Dictionary<string, GalaxyData> { [key] = data }, key);
        }

        public static GalaxyData LoadGalaxy()
        {
            var all = LoadGalaxies(out var active);
            if (all == null || all.Count == 0) return null;
            if (!string.IsNullOrEmpty(active) && all.TryGetValue(active, out var g)) return g;
            foreach (var v in all.Values) return v;
            return null;
        }
    }
}
