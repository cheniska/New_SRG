using UnityEngine;

namespace SRG.Presentation.Common
{
    /// <summary>
    /// Процедурные спрайты для визуала (точки, маркеры).
    /// </summary>
    public static class ProceduralSprites
    {
        /// <summary>
        /// Создаёт круглый спрайт заданного разрешения и цвета.
        /// Пиксели за пределами круга прозрачны; граница мягко сглажена.
        /// </summary>
        /// <param name="res">Размер текстуры (ширина = высота).</param>
        /// <param name="color">Цвет пикселей внутри круга (alpha умножается на маску).</param>
        /// <param name="filterMode">Режим фильтрации текстуры.</param>
        public static Sprite CreateCircle(int res = 32, Color color = default,
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
    }
}
