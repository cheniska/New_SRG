using UnityEngine;
using SRG.Presentation.Common;

namespace SRG.Presentation.Effects
{
    /// <summary>Прокручивает лист-анимацию по кругу на назначенном SpriteRenderer.
    /// Всю логику шага времени держит FrameStepper.</summary>
    public class AnimatedSpriteRenderer : MonoBehaviour
    {
        private FrameStepper _step;
        private SpriteRenderer _sr;

        public void Init(SheetHandle sheet, SpriteRenderer sr)
        {
            _step = new FrameStepper(sheet, loop: true);
            _sr = sr;
        }

        void Update()
        {
            if (_sr == null || _step.Frames == null || _step.Frames.Length <= 1) return;
            if (_step.Advance(Time.deltaTime))
                _sr.sprite = _step.CurrentSprite;
        }
    }
}
