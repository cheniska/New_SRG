using UnityEngine;
using UnityEngine.UI;
using SRG.UI.Screens;

namespace SRG.UI.Common
{
    /// <summary>
    /// Утилитный билдер UI-примитивов. Создан на этапе A9 рефакторинга; миграция существующих
    /// контроллеров — этап B5. Сигнатуры намеренно совпадают с приватными методами Make*/SetAnchors
    /// в <see cref="PlanetUIController"/> чтобы перенос был механическим.
    ///
    /// Не накладывает никакого layout-агрегатора (Vertical/Horizontal/GridLayoutGroup) — это
    /// решает вызывающий код. Цвета берутся через параметры; имена — через <see cref="UIColorPalette"/>.
    /// </summary>
    public static class UIBuilder
    {
        /// <summary>Создаёт пустой GameObject с RectTransform и Image указанного цвета.</summary>
        public static GameObject MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            go.AddComponent<Image>().color = color;
            return go;
        }

        /// <summary>Создаёт Text-элемент. Шрифт обязателен (legacy uGUI требует Font).</summary>
        public static Text MakeText(
            Transform parent, string name, string text, Font font,
            int size, FontStyle style, Color color,
            TextAnchor align = TextAnchor.MiddleLeft, bool richText = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.fontStyle = style;
            t.color = color; t.alignment = align;
            t.supportRichText = richText;
            return t;
        }

        /// <summary>
        /// Создаёт Button с фоном-Image и Text-меткой, растянутой по всей кнопке.
        /// Цвет метки фиксирован — белый (как в существующих контроллерах).
        /// </summary>
        public static Button MakeButton(
            Transform parent, string name, string label, Font font,
            int fontSize, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            go.AddComponent<Image>().color = bgColor;
            var btn = go.AddComponent<Button>();

            var tgo = new GameObject("Label");
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.sizeDelta = Vector2.zero;
            var txt = tgo.AddComponent<Text>();
            txt.font = font; txt.text = label; txt.fontSize = fontSize;
            txt.color = Color.white; txt.alignment = TextAnchor.MiddleCenter;
            return btn;
        }

        /// <summary>
        /// Растягивает RectTransform указанного GameObject в нормализованные анкеры родителя
        /// (xMin/yMin/xMax/yMax ∈ [0..1]) и обнуляет оффсеты. Если RectTransform нет — добавляет:
        /// молчаливый no-op оставлял объекту дефолтный прямоугольник 100×100 по центру родителя,
        /// из-за чего скролл-вьюпорты, созданные до добавления UI-компонентов, схлопывались.
        /// </summary>
        public static void SetAnchors(GameObject go, float xMin, float yMin, float xMax, float yMax)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Удалить всех потомков указанного Transform (Destroy на каждом ребёнке).</summary>
        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
