using UnityEngine;

namespace SRG.Utils
{
    /// <summary>
    /// Конверсия между SR-единицами (= пиксели конфига, «игровые единицы») и мировыми
    /// Unity-единицами. Правило проекта: 1 unity unit = 100 SR-единиц.
    ///
    /// Все параметры дистанций в конфигах (Range, PullRadius, Speed, OrbitRadius,
    /// SystemSize, HabitableZone*, Radius, размеры спрайтов) хранятся в SR-единицах.
    /// Позиции кораблей/планет (<c>ShipData.Position</c>, <c>transform.position</c>) —
    /// в мировых. Любая проверка «дистанция в мире &lt;= порог из конфига» проходит
    /// через этот хелпер.
    ///
    /// Не путать с процентами (BlockPercent, OxygenPercent, Aggression, …) —
    /// они тоже делятся на 100, но это доли [0..1], а не дистанции.
    /// </summary>
    public static class SRUnits
    {
        public const float PixelsPerUnit = 100f;

        /// <summary>SR-единицы → мировые. Пример: Range=400 (SR) → 4 unity units.</summary>
        public static float ToWorld(float sr) => sr / PixelsPerUnit;

        /// <summary>Мировые → SR-единицы. Пример: 2 unity units → 200 SR.</summary>
        public static float ToSR(float world) => world * PixelsPerUnit;

        /// <summary>SR-вектор → мировой.</summary>
        public static Vector2 ToWorld(Vector2 sr) => sr / PixelsPerUnit;

        /// <summary>Мировой вектор → SR.</summary>
        public static Vector2 ToSR(Vector2 world) => world * PixelsPerUnit;
    }
}
