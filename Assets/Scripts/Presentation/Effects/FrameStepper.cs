using UnityEngine;
using SRG.Presentation.Common;

namespace SRG.Presentation.Effects
{
    /// <summary>
    /// Общий покадровый счётчик для покадровых анимаций: копит dt, продвигает CurrentFrame,
    /// умеет либо циклиться (Loop=true — AnimatedSpriteRenderer), либо один раз доиграть до
    /// последнего кадра и поднять Finished (ExplosionPlayback). floor(timer/secPerFrame)
    /// нужен, чтобы при низком framerate не пропускать по несколько кадров подряд.
    /// </summary>
    public struct FrameStepper
    {
        public Sprite[] Frames;
        public float SecPerFrame;
        public bool Loop;
        public int CurrentFrame;
        public bool Finished;
        private float _timer;

        public FrameStepper(SheetHandle sheet, bool loop)
        {
            Frames = sheet.Frames;
            SecPerFrame = sheet.SecPerFrame > 0f ? sheet.SecPerFrame : 1f / 12f;
            Loop = loop;
            CurrentFrame = 0;
            Finished = false;
            _timer = 0f;
        }

        public Sprite CurrentSprite =>
            Frames != null && CurrentFrame >= 0 && CurrentFrame < Frames.Length
                ? Frames[CurrentFrame] : null;

        /// <summary>Копит dt, продвигает индекс. Возвращает true, если CurrentFrame изменился —
        /// caller может обновить рендерер только по факту смены.</summary>
        public bool Advance(float dt)
        {
            if (Frames == null || Frames.Length == 0 || SecPerFrame <= 0f || Finished) return false;
            _timer += dt;
            if (_timer < SecPerFrame) return false;

            int adv = Mathf.FloorToInt(_timer / SecPerFrame);
            _timer -= adv * SecPerFrame;

            int prev = CurrentFrame;
            if (Loop)
            {
                CurrentFrame = (CurrentFrame + adv) % Frames.Length;
            }
            else
            {
                CurrentFrame += adv;
                if (CurrentFrame >= Frames.Length)
                {
                    CurrentFrame = Frames.Length - 1;
                    Finished = true;
                }
            }
            return CurrentFrame != prev;
        }
    }
}
