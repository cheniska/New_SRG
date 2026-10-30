using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SRG.Simulation
{
    /// <summary>
    /// Каталог графики (<c>Config/GraphicsCatalog.json</c>) — то, что симуляции нужно знать о
    /// ассетах, в виде данных: какие листы и картинки есть и какого размера кадр листа в мире.
    ///
    /// Симуляция не читает ассеты: размер корабля, Owner/Race-варианты корпуса, варианты атласов
    /// астероидов и случайные текстуры планет берутся отсюда. Поэтому расчёт хода не зависит ни
    /// от рендера (2D/3D), ни от главного потока, а headless-прогон даёт тот же мир, что и игра.
    ///
    /// Каталог генерирует <c>tools/graphics-catalog/gen_graphics_catalog.py</c> по
    /// <c>Resources/Graphics</c>; синхронность проверяет <c>GraphicsCatalogTests</c>.
    /// Пути — Resources-относительные без расширения, как в конфигах.
    /// </summary>
    public static class GraphicsCatalog
    {
        private sealed class SheetEntry
        {
            /// <summary>Ширина текстуры после импорта (с учётом maxTextureSize), px.</summary>
            public int Width;
            public int Cols;
        }

        private sealed class CatalogFile
        {
            public float PixelsPerUnit = 100f;
            public Dictionary<string, SheetEntry> Sheets;
            public List<string> Images;
        }

        private sealed class State
        {
            public readonly Dictionary<string, SheetEntry> Sheets = new(StringComparer.Ordinal);
            public readonly HashSet<string> Images = new(StringComparer.Ordinal);
            public float PixelsPerUnit = 100f;
            public readonly Dictionary<string, string[]> SheetFolders = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string[]> ImageFolders = new(StringComparer.Ordinal);
        }

        // Неизменяемый после Load снимок: читается из потока расчёта хода без блокировок,
        // кэши папок — под lock на самом словаре.
        private static State _state = new();

        /// <summary>Загружен ли каталог. Без него листов и картинок нет: размеры = 1, вариантов нет.</summary>
        public static bool IsLoaded => _state.Sheets.Count > 0 || _state.Images.Count > 0;

        /// <summary>Загрузить каталог из JSON. null/пустая строка — очистить.</summary>
        public static void Load(string json)
        {
            var state = new State();
            if (!string.IsNullOrEmpty(json))
            {
                var file = JsonConvert.DeserializeObject<CatalogFile>(json);
                if (file?.Sheets != null)
                    foreach (var kv in file.Sheets)
                        if (kv.Value != null && kv.Value.Cols > 0) state.Sheets[kv.Key] = kv.Value;
                if (file?.Images != null)
                    foreach (var path in file.Images)
                        if (!string.IsNullOrEmpty(path)) state.Images.Add(path);
                if (file != null && file.PixelsPerUnit > 0f) state.PixelsPerUnit = file.PixelsPerUnit;
            }
            _state = state;
        }

        /// <summary>Есть ли спрайт-лист по этому пути.</summary>
        public static bool ContainsSheet(string path)
            => !string.IsNullOrEmpty(path) && _state.Sheets.ContainsKey(path);

        /// <summary>Есть ли одиночная картинка по этому пути.</summary>
        public static bool ContainsImage(string path)
            => !string.IsNullOrEmpty(path) && _state.Images.Contains(path);

        /// <summary>Ширина кадра листа в мировых единицах; 1 — если листа нет.
        /// Та же арифметика, что у <c>GraphicsManager</c>: (ширина / колонки) / pixelsPerUnit.</summary>
        public static float FrameWorldSize(string path)
        {
            var state = _state;
            if (string.IsNullOrEmpty(path) || !state.Sheets.TryGetValue(path, out var e)) return 1.0f;
            float frameWidth = (float)e.Width / e.Cols;
            return frameWidth / state.PixelsPerUnit;
        }

        /// <summary>Листы, лежащие прямо в папке (без вложенных), по имени (Ordinal, IgnoreCase):
        /// вариант с индексом 10 идёт после 09.</summary>
        public static string[] SheetsInFolder(string folderPath)
        {
            var state = _state;
            return InFolder(state.SheetFolders, state.Sheets.Keys, folderPath);
        }

        /// <summary>Картинки, лежащие прямо в папке (без вложенных), по имени (Ordinal, IgnoreCase).</summary>
        public static string[] ImagesInFolder(string folderPath)
        {
            var state = _state;
            return InFolder(state.ImageFolders, state.Images, folderPath);
        }

        private static string[] InFolder(Dictionary<string, string[]> cache, IEnumerable<string> paths, string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return Array.Empty<string>();
            lock (cache)
            {
                if (cache.TryGetValue(folderPath, out var cached)) return cached;
                string prefix = folderPath.TrimEnd('/') + "/";
                var list = new List<string>();
                foreach (var path in paths)
                    if (path.StartsWith(prefix, StringComparison.Ordinal) && path.IndexOf('/', prefix.Length) < 0)
                        list.Add(path);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                var result = list.ToArray();
                cache[folderPath] = result;
                return result;
            }
        }
    }
}
