using UnityEngine;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Map;
using SRG.Ships.Movement;
using SRG.Ships.Player;
using SRG.Utils;

namespace SRG.Presentation.World
{
    [DefaultExecutionOrder(100)]
    public class RadarRangeIndicator : MonoBehaviour
    {
        [Header("Внешний вид")]
        [Tooltip("Цвет пунктирного кольца.")]
        public Color RingColor = new Color(1f, 0.92f, 0.2f, 0.55f);

        [Tooltip("Размер одной точки пунктира в мировых единицах.")]
        public float DotSize = 0.06f;

        [Tooltip("Угловой шаг между точками (градусы). Меньше = гуще.")]
        public float DotAngularStep = 4f;

        [Tooltip("Доля видимых точек в пунктире. 0.5 = каждая вторая.")]
        [Range(0.1f, 0.9f)]
        public float DashRatio = 0.5f;

        [Tooltip("Название слоя сортировки (должно совпадать с PathRenderer.sortingLayerName).")]
        public string SortingLayerName = "Default";

        [Tooltip("Порядок сортировки внутри слоя.")]
        public int SortingOrder = 26;

        private Camera _cam;
        private Sprite _dotSprite;
        private GameObject _ringRoot;
        private SpriteRenderer[] _dots;
        private int _dotCount;
        private bool _isVisible;
        private void Start()
        {
            _cam = Camera.main;
            _dotSprite = CreateCircleSprite();
            _ringRoot = new GameObject("RadarRingIndicator");
            _ringRoot.transform.SetParent(null);
            _ringRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_ringRoot != null) Destroy(_ringRoot);
            if (_dotSprite != null) { Destroy(_dotSprite.texture); Destroy(_dotSprite); }
        }

        private void Update()
        {
            if (GalaxyManager.Instance?.Phase == TurnPhase.Simulation)
            {
                SetVisible(false);
                return;
            }

            var ship = PlayerShip.Instance?.ShipData;
            if (ship == null) { SetVisible(false); return; }

            float radarRange = EquipmentSystem.GetRadarRange(ship);
            if (radarRange <= 0f) { SetVisible(false); return; }

            if (SystemMinimapController.IsMouseOverMinimap)
            { SetVisible(false); return; }

            Vector2 mouseWorld = _cam.ScreenToWorldPoint(Input.mousePosition);
            var hit = Physics2D.Raycast(mouseWorld, Vector2.zero);
            bool overObject = hit.collider != null;

            if (!overObject)
            {
                SetVisible(false);
                return;
            }

            Vector2 center = PlayerShip.Instance.transform.position;
            ShowRing(center, radarRange);
        }

        private void ShowRing(Vector2 center, float radius)
        {
            int needed = Mathf.RoundToInt(360f / DotAngularStep);
            RebuildPoolIfNeeded(needed);

            _ringRoot.SetActive(true);
            _isVisible = true;
            float dashPeriod = 1f / Mathf.Max(0.01f, DashRatio);

            for (int i = 0; i < _dotCount; i++)
            {
                float angleDeg = i * DotAngularStep;
                float angleRad = angleDeg * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;

                var sr = _dots[i];
                sr.transform.position = new Vector3(pos.x, pos.y, 0f);
                bool dash = (i % dashPeriod) < (dashPeriod * DashRatio);
                sr.enabled = dash;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_isVisible == visible) return;
            _isVisible = visible;
            if (_ringRoot != null) _ringRoot.SetActive(visible);
        }

        private void RebuildPoolIfNeeded(int needed)
        {
            if (_dots != null && _dotCount == needed) return;
            if (_dots != null)
                foreach (var d in _dots)
                    if (d != null) Destroy(d.gameObject);

            _dotCount = needed;
            _dots = new SpriteRenderer[_dotCount];

            for (int i = 0; i < _dotCount; i++)
            {
                var go = new GameObject($"RDot_{i}");
                go.transform.SetParent(_ringRoot.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _dotSprite;
                sr.color = RingColor;
                sr.sortingLayerName = SortingLayerName;
                sr.sortingOrder = SortingOrder;
                go.transform.localScale = Vector3.one * DotSize;
                _dots[i] = sr;
            }
        }

        private static Sprite CreateCircleSprite()
            => SpriteUtility.CreateCircleSprite(16);
    }
}
