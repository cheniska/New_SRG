using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using SRG.Galaxy;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Core
{
    /// <summary>
    /// Файловый слой сохранений. Формат, версии и миграции — в <see cref="SaveSerializer"/>
    /// (слой симуляции, покрыт тестами); здесь только путь, атомарная запись и логирование.
    /// </summary>
    public static class GalaxySaveManager
    {
        private static string SavePath => RuntimePaths.SaveFile("galaxy_save.json");

        // ── Мультигалактика ───────────────────────────────────────────────────────

        public static void SaveGalaxies(Dictionary<string, GalaxyData> galaxies, string activeKey)
        {
            if (galaxies == null || galaxies.Count == 0)
            {
                Debug.LogWarning("[GalaxySaveManager] Nothing to save."); return;
            }
            string json = SaveSerializer.Serialize(new SaveData
            {
                ActiveKey = activeKey,
                Galaxies = galaxies,
                RngState = GameRng.GetState(),
            });
            WriteAtomically(SavePath, json);
            Debug.Log($"[GalaxySaveManager] Saved {galaxies.Count} galaxies (active={activeKey}, v{SaveSerializer.CurrentVersion}) to: {SavePath}");
        }

        /// <summary>Загружает все галактики из сейва любой поддерживаемой версии и восстанавливает
        /// состояние RNG. Возвращает null при отсутствии файла или ошибке (причина — в логе).</summary>
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
                var data = SaveSerializer.Deserialize(File.ReadAllText(SavePath));
                if (data.Version < SaveSerializer.CurrentVersion)
                    Debug.Log($"[GalaxySaveManager] Save migrated v{data.Version} → v{SaveSerializer.CurrentVersion}.");
                activeKey = data.ActiveKey;
                // Восстанавливаем ПОСЛЕ десериализации: конструкторы моделей берут UID из GameRng.
                GameRng.SetState(data.RngState);
                return data.Galaxies;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GalaxySaveManager] Load failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Запись через временный файл: сбой посреди записи не портит прежний сейв.</summary>
        private static void WriteAtomically(string path, string contents)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
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
            foreach (var galaxy in all.Values) galaxy?.InitializeLookups();
            if (!string.IsNullOrEmpty(active) && all.TryGetValue(active, out var g)) return g;
            foreach (var v in all.Values) return v;
            return null;
        }
    }
}
