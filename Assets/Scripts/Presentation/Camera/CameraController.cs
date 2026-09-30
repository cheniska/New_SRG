using UnityEngine;
using UnityEngine.EventSystems;
using SRG.Config;
using SRG.Presentation.Common;

namespace SRG.Presentation
{
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private GameSettingsConfig settings;

        private Camera _cam;
        private Transform _transform;
        private Vector3 _defaultPosition;
        private float _defaultZoom;
        private Vector3 _dragOrigin;
        private float _targetZoom;

        private bool _isCentering;
        private Vector3 _centerFrom;
        private float _centerTimer;
        private const float CenterDuration = 0.4f;

        // Все спрайты в Resources/Graphics/** импортированы с PPU=100. При таком дефолтном зуме
        // 1 мировая единица = 100 экранных пикселей, т.е. спрайт NxN c localScale=1 показывается
        // ровно NxN пикселями. Формула: orthoSize = Screen.height / (2 * PPU).
        private const float ReferencePixelsPerUnit = 100f;

        private int _lastScreenHeight;

        private float CurrentZoom
        {
            get => _cam.orthographic ? _cam.orthographicSize : _cam.fieldOfView;
            set { if (_cam.orthographic) _cam.orthographicSize = value; else _cam.fieldOfView = value; }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            _cam = GetComponent<Camera>();
            _transform = transform;

            if (settings == null)
            {
                Debug.LogWarning("[CameraController] GameSettingsConfig не назначен — используются дефолты.");
                settings = ScriptableObject.CreateInstance<GameSettingsConfig>();
            }
        }

        private void Start()
        {
            _defaultPosition = new Vector3(0f, 0f, -10f);
            _transform.position = _defaultPosition;
            _defaultZoom = ComputeDefaultZoom();
            _lastScreenHeight = Screen.height;
            CurrentZoom = _defaultZoom;
            _targetZoom = _defaultZoom;
        }

        private float ComputeDefaultZoom()
        {
            if (_cam == null || !_cam.orthographic) return _defaultZoom > 0f ? _defaultZoom : 5f;
            return Screen.height * 0.5f / ReferencePixelsPerUnit;
        }

        private void Update()
        {
            if (Screen.height != _lastScreenHeight)
            {
                _lastScreenHeight = Screen.height;
                float newDefault = ComputeDefaultZoom();
                bool wasAtDefault = Mathf.Approximately(_targetZoom, _defaultZoom);
                _defaultZoom = newDefault;
                if (wasAtDefault) _targetZoom = _defaultZoom;
            }

            HandleKeyboardMovement();

            if (!IsPointerOverUI())
            {
                HandleEdgeScrolling();
                HandleMouseWheel();
            }

            CurrentZoom = Mathf.Lerp(CurrentZoom, _targetZoom, Time.deltaTime * 10f);
            HandleMouseDrag();
            HandleHotkeys();
            TickCentering();
            ClampPosition();
        }

        private void HandleKeyboardMovement()
        {
            float x = Input.GetAxis(settings.HorizontalAxis), y = Input.GetAxis(settings.VerticalAxis);
            if (x == 0f && y == 0f) return;
            StopCentering();
            _transform.Translate(new Vector3(x, y) * settings.MoveSpeed * Time.deltaTime, Space.World);
        }

        private void HandleMouseWheel()
        {
            float scroll = Input.GetAxis(settings.ZoomAxis);
            if (Mathf.Abs(scroll) < 0.01f) return;
            float minZoom = _defaultZoom * settings.MinZoomMult;
            float maxZoom = _defaultZoom * settings.MaxZoomMult;
            _targetZoom = Mathf.Clamp(_targetZoom - scroll * settings.ZoomSpeed, minZoom, maxZoom);
        }

        private void HandleEdgeScrolling()
        {
            if (!settings.EnableEdgeScrolling) return;
            Vector3 move = Vector3.zero;
            Vector3 mp = Input.mousePosition;
            if (mp.x >= Screen.width - settings.EdgeThickness) move.x += 1f;
            else if (mp.x <= settings.EdgeThickness) move.x -= 1f;
            if (mp.y >= Screen.height - settings.EdgeThickness) move.y += 1f;
            else if (mp.y <= settings.EdgeThickness) move.y -= 1f;
            if (move == Vector3.zero) return;
            StopCentering();
            _transform.Translate(move.normalized * settings.MoveSpeed * Time.deltaTime, Space.World);
        }

        private void HandleMouseDrag()
        {
            if (Input.GetMouseButtonDown(settings.DragMouseButton))
                _dragOrigin = _cam.ScreenToWorldPoint(Input.mousePosition);

            if (Input.GetMouseButton(settings.DragMouseButton))
            {
                Vector3 diff = _dragOrigin - _cam.ScreenToWorldPoint(Input.mousePosition);
                if (diff.sqrMagnitude > 0.0001f) { StopCentering(); _transform.position += diff; }
            }
        }

        private void HandleHotkeys()
        {
            if (PresentationContext.IsTextInputActive()) return;
            if (Input.GetKeyDown(settings.ResetCameraKey)) ResetCamera();
            if (Input.GetKeyDown(settings.CenterCameraKey)) StartCenterOnPlayer();
        }

        /// <summary>Принудительная установка позиции камеры (для гиперперехода — точка прибытия в новой системе).</summary>
        public void SetPosition(Vector2 worldPos)
        {
            StopCentering();
            _transform.position = new Vector3(worldPos.x, worldPos.y, _defaultPosition.z);
        }

        public void StartCenterOnPlayer()
        {
            if (PresentationContext.Player == null) return;
            _centerFrom = _transform.position;
            _centerTimer = 0f;
            _isCentering = true;
        }

        private void TickCentering()
        {
            if (!_isCentering) return;
            if (PresentationContext.Player == null) { _isCentering = false; return; }

            _centerTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_centerTimer / CenterDuration);
            t = t * t * (3f - 2f * t); // smoothstep

            var pp = PresentationContext.Player.Transform.position;
            _transform.position = Vector3.Lerp(_centerFrom,
                new Vector3(pp.x, pp.y, _transform.position.z), t);

            if (t >= 1f) _isCentering = false;
        }

        private void StopCentering() { _isCentering = false; }

        private void ResetCamera()
        {
            StopCentering();
            _transform.position = _defaultPosition;
            _targetZoom = _defaultZoom;
        }

        private void ClampPosition()
        {
            Vector3 pos = _transform.position;
            pos.x = Mathf.Clamp(pos.x, -settings.MapLimit.x, settings.MapLimit.x);
            pos.y = Mathf.Clamp(pos.y, -settings.MapLimit.y, settings.MapLimit.y);
            _transform.position = pos;
        }

        private bool IsPointerOverUI() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
