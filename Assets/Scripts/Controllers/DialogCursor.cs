using UnityEngine;

namespace SRG.Controllers
{
    // Процедурный курсор-микрофон для режима связи.
    // Маленькая текстура рисуется один раз и кешируется.
    public static class DialogCursor
    {
        private const int W = 32;
        private const int H = 32;
        private static readonly Vector2 _hotspot = new(W / 2f, H - 6f);

        private static Texture2D _cached;

        public static void Apply()
        {
            if (_cached == null) _cached = BuildMicIcon();
            Cursor.SetCursor(_cached, _hotspot, CursorMode.Auto);
        }

        public static void Reset() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        private static Texture2D BuildMicIcon()
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear   = new Color(0f, 0f, 0f, 0f);
            var fill    = new Color(0.55f, 0.82f, 1.00f, 1f);   // голубой
            var outline = new Color(0.04f, 0.07f, 0.12f, 1f);   // тёмная обводка

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    tex.SetPixel(x, y, clear);

            // Капсула микрофона: ширина 8 (x ∈ 12..19), высота 14 (y ∈ 13..26),
            // верх скруглён.
            for (int y = 13; y <= 26; y++)
                for (int x = 12; x <= 19; x++)
                {
                    bool topCorner = y == 26 && (x == 12 || x == 19);
                    bool bottomCorner = y == 13 && (x == 12 || x == 19);
                    if (topCorner || bottomCorner) continue;
                    tex.SetPixel(x, y, fill);
                }
            // Обводка капсулы
            for (int y = 13; y <= 26; y++) { tex.SetPixel(11, y, outline); tex.SetPixel(20, y, outline); }
            for (int x = 12; x <= 19; x++)
            {
                tex.SetPixel(x, 27, outline);
                tex.SetPixel(x, 12, outline);
            }
            tex.SetPixel(11, 26, outline); tex.SetPixel(20, 26, outline);
            tex.SetPixel(11, 13, outline); tex.SetPixel(20, 13, outline);

            // Дужка-держатель под капсулой (полукруг)
            for (int x = 10; x <= 21; x++) tex.SetPixel(x, 9, outline);
            tex.SetPixel(9, 10, outline); tex.SetPixel(22, 10, outline);
            tex.SetPixel(9, 11, outline); tex.SetPixel(22, 11, outline);

            // Стойка
            for (int y = 5; y <= 8; y++)
            {
                tex.SetPixel(15, y, fill);
                tex.SetPixel(16, y, fill);
            }
            tex.SetPixel(14, 5, outline); tex.SetPixel(17, 5, outline);
            tex.SetPixel(14, 8, outline); tex.SetPixel(17, 8, outline);

            // Основание
            for (int x = 10; x <= 21; x++)
            {
                tex.SetPixel(x, 3, fill);
                tex.SetPixel(x, 4, fill);
            }
            for (int x = 9; x <= 22; x++) tex.SetPixel(x, 2, outline);
            tex.SetPixel(9, 3, outline);  tex.SetPixel(22, 3, outline);
            tex.SetPixel(9, 4, outline);  tex.SetPixel(22, 4, outline);
            for (int x = 10; x <= 21; x++) tex.SetPixel(x, 5, outline);

            tex.Apply();
            return tex;
        }
    }
}
