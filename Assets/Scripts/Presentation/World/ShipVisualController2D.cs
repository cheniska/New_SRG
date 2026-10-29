using UnityEngine;
using System.Collections.Generic;
using SRG.Galaxy;
using SRG.Presentation.Common;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Корпус корабля спрайт-листом: клипы анимации (<see cref="ShipData.Animations"/>, иначе весь
    /// лист циклом), бесшовные цепочки клипов, заморозка кадра у предмета на буксире.
    /// Общая часть (позиция, поворот, прозрачность, посадка, выхлоп) — в <see cref="ShipVisualController"/>.
    /// </summary>
    public class ShipVisualController2D : ShipVisualController
    {
        private SpriteRenderer _bodyRenderer;
        private Sprite[] _allFrames;           // весь спрайтлист, индексируется глобально

        private ShipAnimationClip _currentClip;
        private string _currentClipName;
        private string _pendingClipName;
        private readonly Queue<string> _clipChain = new Queue<string>();   // бесшовная цепочка после текущего
        private int _localFrame;               // кадр внутри текущего клипа
        private float _frameTimer;
        private float _currentClipSecPerFrame;

        protected override void BuildBody()
        {
            if (_bodyRenderer == null && !TryGetComponent(out _bodyRenderer))
                _bodyRenderer = gameObject.AddComponent<SpriteRenderer>();
            _bodyRenderer.enabled = true;
            _bodyRenderer.sortingOrder = BodySortingOrder;
            LoadSpritesheet();
            PlayAnimation("Idle");
        }

        protected override void RebuildBody()
        {
            LoadSpritesheet();
            _currentClip = null;
            _currentClipName = null;
            PlayAnimation("Idle");
        }

        protected override void ApplyBodyAlpha(float alpha)
        {
            if (_bodyRenderer == null) return;
            var c = _bodyRenderer.color;
            _bodyRenderer.color = new Color(c.r, c.g, c.b, alpha);
        }

        protected override void ResetBodyAnimation()
        {
            _pendingClipName = null;
            PlayAnimation("Idle");
        }

        public override void Teardown()
        {
            base.Teardown();
            // SpriteRenderer остаётся на GameObject (его же забирает ExplosionPlayback) — только гасим.
            if (_bodyRenderer != null) { _bodyRenderer.sprite = null; _bodyRenderer.enabled = false; }
        }

        public override void SetAnimation(string clipName)
        {
            if (_currentClipName == clipName) { _pendingClipName = null; return; }
            _pendingClipName = clipName;
        }

        /// <summary>Поставить клип в очередь после текущего; стартует бесшовно — первый кадр сразу за последним.</summary>
        public void QueueAnimation(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return;
            _clipChain.Enqueue(clipName);
        }

        /// <summary>Заменить очередь цепочкой клипов: первый запускается немедленно, остальные — последовательно без паузы.</summary>
        public void SetAnimationChain(params string[] clipNames)
        {
            _clipChain.Clear();
            _pendingClipName = null;
            if (clipNames == null || clipNames.Length == 0) return;
            SetAnimation(clipNames[0]);
            for (int i = 1; i < clipNames.Length; i++) _clipChain.Enqueue(clipNames[i]);
        }

        protected override void TickBody(float dt)
        {
            if (_currentClip == null || _allFrames == null) return;
            if (_currentClip.FrameCount <= 1) return;

            // Предмет на буксире замораживается на ItemFreezeFrame.
            // Свободный предмет в космосе (TowedByUid пуст) — анимируется как обычно.
            if (Data != null && Data.IsItem && !string.IsNullOrEmpty(Data.TowedByUid))
            {
                int freeze = Mathf.Clamp(Data.ItemFreezeFrame, 0, _currentClip.FrameCount - 1);
                if (_localFrame != freeze)
                {
                    _localFrame = freeze;
                    ApplyFrame();
                }
                return;
            }

            _frameTimer += dt;

            if (_frameTimer < _currentClipSecPerFrame) return;

            int advance = Mathf.FloorToInt(_frameTimer / _currentClipSecPerFrame);
            _frameTimer %= _currentClipSecPerFrame;

            int nextLocal = _localFrame + advance;

            if (_currentClip.Loop)
            {
                if (nextLocal >= _currentClip.FrameCount && _pendingClipName != null)
                {
                    PlayAnimation(_pendingClipName);
                    _pendingClipName = null;
                    return;
                }
                if (nextLocal >= _currentClip.FrameCount && _clipChain.Count > 0)
                {
                    // Бесшовный переход: следующий клип стартует с frame 0 + остаточный таймер.
                    float carry = _frameTimer;
                    PlayAnimation(_clipChain.Dequeue());
                    _frameTimer = carry;
                    return;
                }
                _localFrame = nextLocal % _currentClip.FrameCount;
            }
            else
            {
                if (nextLocal >= _currentClip.FrameCount && _clipChain.Count > 0)
                {
                    float carry = _frameTimer;
                    PlayAnimation(_clipChain.Dequeue());
                    _frameTimer = carry;
                    return;
                }
                _localFrame = Mathf.Min(nextLocal, _currentClip.FrameCount - 1);
            }

            ApplyFrame();
        }

        private void LoadSpritesheet()
        {
            string path = Data?.GetBodyGraphicPath();
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"[ShipVisualController2D] Body graphic path is empty for '{Data?.Name}'.");
                return;
            }

            if (GraphicsManager.Instance == null)
            {
                Debug.LogWarning("[ShipVisualController2D] GraphicsManager.Instance is null.");
                return;
            }

            _allFrames = GraphicsManager.Instance.GetSpriteSheet(path);
            if (_allFrames == null || _allFrames.Length == 0)
                Debug.LogError($"[ShipVisualController2D] Spritesheet not found: {path}");
        }

        private void PlayAnimation(string clipName)
        {
            // Попытка найти именованный клип
            if (Data?.Animations != null && Data.Animations.TryGetValue(clipName, out var clip))
            {
                _currentClip = clip;
                _currentClipName = clipName;
                _localFrame = 0;
                _frameTimer = 0f;
                _currentClipSecPerFrame = clip.FPS > 0f ? 1f / clip.FPS : 1f / 12f;
                ApplyFrame();
                return;
            }

            // Фолбэк: если есть кадры спрайтлиста, проигрывать их как единственный цикличный клип
            if (_allFrames != null && _allFrames.Length > 0)
            {
                _currentClip = new ShipAnimationClip
                {
                    StartFrame = 0,
                    FrameCount = _allFrames.Length,
                    FPS = 12f,
                    Loop = true
                };
                _currentClipName = clipName;
                _localFrame = 0;
                _frameTimer = 0f;
                _currentClipSecPerFrame = 1f / 12f;
                ApplyFrame();
                return;
            }

            Debug.LogWarning($"[ShipVisualController2D] Animation '{clipName}' not found and no frames loaded for '{Data?.Name}'.");
        }

        private void ApplyFrame()
        {
            if (_currentClip == null || _allFrames == null || _bodyRenderer == null) return;

            int globalIndex = _currentClip.StartFrame + _localFrame;
            if (globalIndex < 0 || globalIndex >= _allFrames.Length)
            {
                Debug.LogWarning($"[ShipVisualController2D] Frame index {globalIndex} out of range ({_allFrames.Length} frames).");
                return;
            }

            _bodyRenderer.sprite = _allFrames[globalIndex];
        }
    }
}
