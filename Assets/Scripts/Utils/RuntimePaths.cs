using System;
using System.IO;
using UnityEngine;

namespace SRG.Utils
{
    /// <summary>
    /// Единая точка склейки файловых путей рантайма (сейв, логи).
    /// В отличие от Resources-путей (см. <see cref="SRG.Config.GalaxyConstants"/> +
    /// <see cref="SRG.Config.GameSettingsConfig"/>), это не моддинг-конфиг, а способ
    /// общения игры с ФС пользователя — поэтому живёт в коде, а не в JSON.
    /// </summary>
    public static class RuntimePaths
    {
        /// <summary>Директория логов: &lt;project&gt;/logs (одинаковый нижний регистр — чтобы на case-sensitive ФС не рождались две папки).</summary>
        public static string LogsDir => Path.Combine(Application.dataPath, "..", "logs");

        /// <summary>Файл лога в <see cref="LogsDir"/> с заданным именем.</summary>
        public static string LogFile(string fileName) => Path.Combine(LogsDir, fileName);

        /// <summary>Файл сейва в persistentDataPath. Единый вход для всех сейвов игры.</summary>
        public static string SaveFile(string fileName) => Path.Combine(Application.persistentDataPath, fileName);

        /// <summary>Создать родительскую директорию файла, если её ещё нет. Ошибки не пробрасываются.</summary>
        public static void EnsureDirFor(string filePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RuntimePaths] EnsureDirFor({filePath}) failed: {e.Message}");
            }
        }
    }
}
