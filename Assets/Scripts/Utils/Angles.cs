using UnityEngine;

namespace SRG.Utils
{
    /// <summary>
    /// Мелкая угловая арифметика в радианах: направление ↔ угол, нормализация,
    /// ограниченный доворот. Общая для кораблей, ракет и визуала.
    /// </summary>
    public static class Angles
    {
        public const float TwoPi = Mathf.PI * 2f;

        /// <summary>Единичный вектор по углу.</summary>
        public static Vector2 Dir(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        /// <summary>Угол вектора. Для нулевого вектора возвращает <paramref name="fallback"/>.</summary>
        public static float Of(Vector2 v, float fallback = 0f)
            => v.sqrMagnitude > 1e-10f ? Mathf.Atan2(v.y, v.x) : fallback;

        /// <summary>Угол направления from → to.</summary>
        public static float Toward(Vector2 from, Vector2 to, float fallback = 0f) => Of(to - from, fallback);

        /// <summary>Приводит угол к диапазону (-π, π].</summary>
        public static float WrapPi(float a)
        {
            a = Mathf.Repeat(a + Mathf.PI, TwoPi) - Mathf.PI;
            return a <= -Mathf.PI ? a + TwoPi : a;
        }

        /// <summary>Поворачивает <paramref name="current"/> к <paramref name="desired"/> не более чем на
        /// <paramref name="maxStep"/> по кратчайшей дуге.</summary>
        public static float StepToward(float current, float desired, float maxStep)
        {
            float diff = WrapPi(desired - current);
            return Mathf.Abs(diff) <= maxStep ? desired : current + Mathf.Sign(diff) * maxStep;
        }
    }
}
