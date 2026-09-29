using UnityEngine;
using SRG.Presentation.Common;

namespace SRG.Presentation.World
{
    public class StarVisualController : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Sprite[] _frames;
        private float _secPerFrame;
        private float _timer;
        private int _curFrame;

        public void Setup(Sprite[] frames, float animSpeed, float scale)
        {
            _sr = GetComponent<SpriteRenderer>();
            _frames = frames;
            _secPerFrame = animSpeed;
            transform.localScale = Vector3.one * scale;

            _sr.sortingOrder = SortingLayerRegistry.Get(SortLayer.Star);

            if (_frames?.Length > 0) _sr.sprite = _frames[0];
        }

        private void Update()
        {
            if (_frames == null || _frames.Length <= 1) return;

            _timer += Time.deltaTime;
            if (_timer < _secPerFrame) return;

            int advance = Mathf.FloorToInt(_timer / _secPerFrame);
            _curFrame = (_curFrame + advance) % _frames.Length;
            _sr.sprite = _frames[_curFrame];
            _timer %= _secPerFrame;
        }
    }
}
