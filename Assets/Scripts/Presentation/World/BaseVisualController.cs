using UnityEngine;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Базовый класс для визуальных контроллеров, рендерящих сферические объекты с маской
    /// (планеты, спутники). Содержит общие helpers, которые исторически дублировались в
    /// <see cref="PlanetVisualController"/> и <see cref="SatelliteVisualController"/>:
    /// <see cref="EnsureMask"/> и <see cref="EnsureRenderer"/>. Поле <see cref="ShapeMask"/>
    /// общее: ребёнок-класс должен присваивать ему результат своих GraphicsManager.GetSprite-вызовов.
    /// </summary>
    public abstract class BaseVisualController : MonoBehaviour
    {
        public SpriteMask ShapeMask;

        /// <summary>
        /// Гарантирует наличие компонента <see cref="SpriteMask"/> на этом GameObject и
        /// присваивает ему форму <paramref name="mask"/>. Если ShapeMask ещё не назначен в инспекторе —
        /// сначала пробует найти на текущем объекте, иначе добавляет новый.
        /// </summary>
        protected void EnsureMask(Sprite mask)
        {
            if (ShapeMask == null && !TryGetComponent(out ShapeMask))
                ShapeMask = gameObject.AddComponent<SpriteMask>();
            ShapeMask.sprite = mask;
        }

        /// <summary>
        /// Гарантирует наличие <see cref="SpriteRenderer"/> в дочернем слоте <paramref name="slotName"/>.
        /// Если ссылка <paramref name="r"/> уже задана — настраивает её; иначе ищет под этим именем
        /// дочерний Transform, и если такого нет — создаёт новый. Сбрасывает локальную трансформацию,
        /// выставляет sortingOrder и режим взаимодействия с маской.
        /// </summary>
        protected SpriteRenderer EnsureRenderer(SpriteRenderer r, string slotName, int sortOrder, bool useMask)
        {
            if (r == null)
            {
                Transform slot = transform.Find(slotName);
                if (slot != null)
                {
                    r = slot.GetComponent<SpriteRenderer>();
                }
                else
                {
                    var go = new GameObject(slotName);
                    go.transform.SetParent(transform, worldPositionStays: false);
                    r = go.AddComponent<SpriteRenderer>();
                }
            }

            r.transform.localPosition = Vector3.zero;
            r.transform.localRotation = Quaternion.identity;
            r.transform.localScale = Vector3.one;
            r.sortingOrder = sortOrder;
            r.maskInteraction = useMask ? SpriteMaskInteraction.VisibleInsideMask : SpriteMaskInteraction.None;
            return r;
        }
    }
}
