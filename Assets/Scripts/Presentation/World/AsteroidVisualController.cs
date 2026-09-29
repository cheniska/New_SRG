using UnityEngine;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Presentation.Effects;

namespace SRG.Presentation.World
{
    public class AsteroidVisualController : MonoBehaviour
    {
        [Header("Renderer")]
        public SpriteRenderer SpriteRenderer;
        private Sprite[] _frames;
        private float _secPerFrame;
        private float _frameTimer;
        private int _curFrame;
        private float _rotationSpeed; // °/сек, знак задаёт направление тумблинга

        public void Setup(AsteroidData data)
        {
            _rotationSpeed = data.SelfRotationSpeed;

            EnsureRenderer();
            SpriteRenderer.sortingOrder = SortingLayerRegistry.Get(SortLayer.Asteroid);

            LoadSpritesheet(data);

            float scale = Mathf.Clamp(data.CollisionRadius * 2f, 0.2f, 1.5f);
            transform.localScale = Vector3.one * scale;
            transform.localPosition = new Vector3(data.Position.x, data.Position.y, 0f);
        }

        public void UpdateVisualPosition(Vector2 pos)
        {
            transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        }

        /// <summary>
        /// Прерывает обычную анимацию астероида и проигрывает на его GameObject взрыв
        /// (через ExplosionPlayback). По окончании анимации GO самоуничтожается.
        /// Вызывать только когда астероид уже удалён из _asteroidControllers — иначе
        /// AnimateSystem будет двигать ему позицию во время взрыва.
        /// </summary>
        public void PlayExplosion(string explosionPath = null)
        {
            // Отключаем наш Update — ExplosionPlayback забирает SpriteRenderer и крутит свой цикл.
            enabled = false;
            ExplosionPlayback.PlayOn(gameObject, explosionPath);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            TickAnimation(dt);
            TickRotation(dt);
        }

        private void TickAnimation(float dt)
        {
            if (_frames == null || _frames.Length <= 1) return;

            _frameTimer += dt;
            if (_frameTimer < _secPerFrame) return;

            int advance = Mathf.FloorToInt(_frameTimer / _secPerFrame);
            _frameTimer %= _secPerFrame;
            _curFrame = (_curFrame + advance) % _frames.Length;
            SpriteRenderer.sprite = _frames[_curFrame];
        }

        private void TickRotation(float dt)
        {
            transform.Rotate(0f, 0f, _rotationSpeed * dt);
        }

        private void LoadSpritesheet(AsteroidData data)
        {
            if (string.IsNullOrEmpty(data.GraphicPath))
            {
                Debug.LogWarning($"[AsteroidVisualController] Asteroid '{data.Uid[..8]}' has empty GraphicPath.");
                return;
            }

            if (GraphicsManager.Instance == null)
            {
                Debug.LogWarning("[AsteroidVisualController] GraphicsManager.Instance is null.");
                return;
            }

            var sheet = GraphicsManager.Instance.GetSheetHandle(data.GraphicPath, data.AnimFps);

            if (sheet.IsValid)
            {
                _frames = sheet.Frames;
                _secPerFrame = sheet.SecPerFrame;
                _curFrame = 0;
                _frameTimer = 0f;
                SpriteRenderer.sprite = _frames[0];
            }
            else
            {
                Debug.LogError($"[AsteroidVisualController] No frames loaded from '{data.GraphicPath}'. " +
                               $"Asteroid '{data.Uid[..8]}' renders without sprite.");
            }
        }

        private void EnsureRenderer()
        {
            if (SpriteRenderer != null) return;
            if (!TryGetComponent(out SpriteRenderer))
                SpriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
    }
}
