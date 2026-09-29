using UnityEngine;
using SRG.Dialog;
using SRG.UI.Screens;

namespace SRG.UI.Common
{
    /// <summary>
    /// Единая палитра цветов для UI. Создана на этапе A9 рефакторинга; миграция существующих
    /// контроллеров (PlanetUIController, GalaxyMapController, DialogUIController, …) — этап B5.
    ///
    /// Цвета выровнены по сейчас-действующим значениям <see cref="PlanetUIController"/>, чтобы
    /// миграция не меняла визуал: ColBg/ColPanel/… → <see cref="Bg"/>/<see cref="Panel"/>/…
    /// Если контроллеру нужен «свой» оттенок — он остаётся локальным; палитра не заменяет всё подряд.
    /// </summary>
    public static class UIColorPalette
    {
        public static Color Bg          { get; } = new Color(0.05f, 0.07f, 0.12f, 1f);
        public static Color Panel       { get; } = new Color(0.08f, 0.10f, 0.16f, 1f);
        public static Color Nav         { get; } = new Color(0.06f, 0.09f, 0.14f, 1f);
        public static Color Button      { get; } = new Color(0.13f, 0.22f, 0.35f, 1f);
        public static Color ButtonSel   { get; } = new Color(0.18f, 0.38f, 0.60f, 1f);
        public static Color ButtonAlt   { get; } = new Color(0.28f, 0.16f, 0.12f, 1f);
        public static Color Accent      { get; } = new Color(0.55f, 0.82f, 1.00f, 1f);
        public static Color Text        { get; } = new Color(0.88f, 0.88f, 0.93f, 1f);
        public static Color Green       { get; } = new Color(0.30f, 0.85f, 0.45f, 1f);
        public static Color Red         { get; } = new Color(0.95f, 0.35f, 0.30f, 1f);
    }
}
