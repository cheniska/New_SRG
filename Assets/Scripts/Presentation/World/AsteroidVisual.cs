using UnityEngine;
using SRG.Galaxy;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Визуал астероида: 2D — <see cref="AsteroidVisualController"/> (спрайт-лист с кадрами
    /// вращения), 3D — <see cref="AsteroidVisualController3D"/> (камень). Создаётся через
    /// <see cref="VisualFactory.CreateAsteroid"/>; SystemViewManager двигает его по кадрам хода
    /// и взрывает при разрушении.
    /// </summary>
    public abstract class AsteroidVisual : MonoBehaviour
    {
        public abstract void Setup(AsteroidData data);

        public void UpdateVisualPosition(Vector2 pos)
        {
            transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        }

        /// <summary>
        /// Прерывает обычную анимацию астероида и проигрывает на его GameObject взрыв
        /// (через ExplosionPlayback). По окончании анимации GO самоуничтожается.
        /// Вызывать только когда астероид уже удалён из списка визуалов — иначе
        /// AnimateSystem будет двигать ему позицию во время взрыва.
        /// </summary>
        public abstract void PlayExplosion(string explosionPath = null);

        /// <summary>Визуальный диаметр астероида: 2·CollisionRadius, зажатый в [0.2, 1.5]
        /// (тот же размер берёт WorldPicker для клика).</summary>
        protected static float VisualScale(AsteroidData data)
            => Mathf.Clamp(data.CollisionRadius * 2f, 0.2f, 1.5f);
    }
}
