using UnityEngine;

namespace SRG.Utils
{
    /// <summary>
    /// Общие утилиты для создания процедурных спрайтов.
    /// </summary>
    public static class SpriteUtility
    {
        /// <summary>
        /// Создаёт круглый спрайт заданного разрешения и цвета.
        /// Пиксели за пределами круга прозрачны; граница мягко сглажена.
        /// </summary>
        /// <param name="res">Размер текстуры (ширина = высота).</param>
        /// <param name="color">Цвет пикселей внутри круга (alpha умножается на маску).</param>
        /// <param name="filterMode">Режим фильтрации текстуры.</param>
        public static Sprite CreateCircleSprite(int res = 32, Color color = default,
            FilterMode filterMode = FilterMode.Bilinear)
        {
            if (color == default) color = Color.white;

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = filterMode;
            float center = res * 0.5f;
            float radius = center - 1f;
            var pixels = new Color[res * res];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * res + x] = new Color(color.r, color.g, color.b, color.a * alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        /// <summary>
        /// Сортирует массив спрайтов по имени с учётом числа в конце (натуральная сортировка).
        /// Пример: frame_1, frame_2, ..., frame_10, frame_11 — а не frame_1, frame_10, frame_11, frame_2.
        /// </summary>
        public static void SortByName(Sprite[] sprites)
        {
            System.Array.Sort(sprites, CompareSpriteNames);
        }

        private static int CompareSpriteNames(Sprite a, Sprite b)
        {
            int numA = ExtractTrailingNumber(a.name);
            int numB = ExtractTrailingNumber(b.name);
            if (numA >= 0 && numB >= 0) return numA.CompareTo(numB);
            return string.Compare(a.name, b.name, System.StringComparison.Ordinal);
        }

        private static int ExtractTrailingNumber(string name)
        {
            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            if (i < name.Length - 1 && int.TryParse(name.Substring(i + 1), out int n)) return n;
            return -1;
        }

        /// <summary>
        /// Безопасно возвращает первые <paramref name="len"/> символов строки.
        /// Если строка короче — возвращает её целиком.
        /// </summary>
        public static string ShortId(string id, int len = 6)
            => id == null ? "null" : id.Length >= len ? id[..len] : id;
    }
}
