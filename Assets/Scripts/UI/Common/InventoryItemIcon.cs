using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SRG.UI.Common
{
    /// <summary>
    /// Иконка предмета в инвентаре. Спрайтлист хранится как массив кадров.
    /// Hover → проигрывание анимации. Уход курсора → пауза на текущем кадре.
    /// Внешний вызов ResetFrame() → возврат к нулевому кадру (при закрытии инвентаря).
    /// </summary>
    public class InventoryItemIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Target;
        public Sprite[] Frames;
        public float Fps = 12f;

        private int _currentFrame;
        private float _timer;
        private bool _playing;

        public void Configure(Image target, Sprite[] frames, float fps = 12f)
        {
            Target = target;
            Frames = frames;
            Fps = fps > 0f ? fps : 12f;
            _currentFrame = 0;
            _timer = 0f;
            _playing = false;
            Apply();
        }

        public void ResetFrame()
        {
            _currentFrame = 0;
            _timer = 0f;
            _playing = false;
            Apply();
        }

        public void OnPointerEnter(PointerEventData _)
        {
            if (Frames == null || Frames.Length <= 1) return;
            _playing = true;
        }

        public void OnPointerExit(PointerEventData _)
        {
            _playing = false;
        }

        private void Update()
        {
            if (!_playing || Frames == null || Frames.Length <= 1) return;

            _timer += Time.unscaledDeltaTime;
            float secPerFrame = 1f / Fps;
            if (_timer < secPerFrame) return;

            int advance = Mathf.FloorToInt(_timer / secPerFrame);
            _timer %= secPerFrame;
            _currentFrame = (_currentFrame + advance) % Frames.Length;
            Apply();
        }

        private void Apply()
        {
            if (Target == null || Frames == null || Frames.Length == 0) return;
            int idx = Mathf.Clamp(_currentFrame, 0, Frames.Length - 1);
            Target.sprite = Frames[idx];
        }
    }
}
