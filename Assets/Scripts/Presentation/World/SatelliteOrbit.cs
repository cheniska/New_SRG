using UnityEngine;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Utils;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Видимая орбита спутника вокруг планеты — общая для 2D и 3D визуалов: сплющенный эллипс
    /// (вид на орбиту под углом), повёрнутый на наклон орбиты; «глубина» на орбите меняет масштаб
    /// и слой отрисовки (за планетой / перед ней).
    /// </summary>
    public static class SatelliteOrbit
    {
        /// <summary>Ставит спутник в точку орбиты <paramref name="angleDeg"/>. Возвращает слой
        /// отрисовки поверхности (SatelliteUp/SatelliteDown).</summary>
        public static int Place(Transform t, SatelliteData data, float angleDeg, float baseScale)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float r = SRUnits.ToWorld(data.OrbitRadius);
            float incl = data.OrbitInclination * Mathf.Deg2Rad;

            float xBase = Mathf.Cos(rad) * r;
            float yBase = Mathf.Sin(rad) * r * 0.4f;

            t.localPosition = new Vector3(
                xBase * Mathf.Cos(incl) - yBase * Mathf.Sin(incl),
                xBase * Mathf.Sin(incl) + yBase * Mathf.Cos(incl),
                0f
            );

            float depthFactor = Mathf.Sin(rad);
            float scaleMult = 1f - depthFactor * 0.25f;
            t.localScale = Vector3.one * (baseScale * scaleMult);

            return depthFactor > 0
                ? SortingLayerRegistry.Get(SortLayer.SatelliteUp)
                : SortingLayerRegistry.Get(SortLayer.SatelliteDown);
        }

        /// <summary>Шаг угла орбиты за <paramref name="dt"/> секунд визуального времени.</summary>
        public static float Advance(SatelliteData data, float angleDeg, float dt)
        {
            float step = Mathf.Abs(data.OrbitSpeed) < 0.01f ? 0f : 360f / data.OrbitSpeed;
            return Mathf.Repeat(angleDeg + step * dt, 360f);
        }

        /// <summary>Базовый масштаб спутника из конфига по ключу размера.</summary>
        public static float BaseScale(string sizeKey)
        {
            var config = GraphicsManager.Instance != null ? GraphicsManager.Instance.GetConfig() : null;
            if (config?.Satellites?.Sizes != null && config.Satellites.Sizes.TryGetValue(sizeKey, out var data))
                return data.BaseScale;
            return 1f;
        }
    }
}
