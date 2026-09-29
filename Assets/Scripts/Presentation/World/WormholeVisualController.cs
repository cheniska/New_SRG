using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;
using SRG.Presentation.Common;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Визуал одной червоточины в системе. Три клипа-фазы:
    ///   • Opening — играется один раз;
    ///   • Open    — циклический клип (пока фаза не сменится);
    ///   • Closing — один раз, после чего контроллер уничтожается менеджером.
    /// Позиция/фаза читаются из привязанного <see cref="WormholeData"/>.
    /// </summary>
    public class WormholeVisualController : MonoBehaviour
    {
        private const float Fps = 24f;
        private static readonly float SecPerFrame = 1f / Fps;

        private SpriteRenderer _renderer;
        private Sprite[] _frames;
        private WormholePhase _appliedPhase = (WormholePhase)(-1);
        private bool _loopCurrent;
        private int _frame;
        private float _timer;
        // Отложенный переход к клипу целевой фазы: например, симуляция уже сменила Opening→Open,
        // но клип open ещё не доиграл — держим его до последнего кадра, потом переключаемся.
        // Для Closing переход мгновенный (закрытие должно начаться сразу).
        private Sprite[] _pendingFrames;
        private bool _pendingLoop;

        public WormholeData Data { get; private set; }

        public void Setup(WormholeData data, float worldSize)
        {
            Data = data;
            if (!TryGetComponent(out _renderer))
                _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = SortingLayerRegistry.Get(SortLayer.Asteroid);
            transform.localScale = Vector3.one * worldSize;
            transform.position = new Vector3(data.Position.x, data.Position.y, 0f);
        }

        public void ApplyPhase(WormholePhase phase, string openingPath, string cyclePath, string closingPath)
        {
            if (phase == _appliedPhase) return;
            var prevPhase = _appliedPhase;
            _appliedPhase = phase;

            string path;
            bool loop;
            switch (phase)
            {
                case WormholePhase.Opening: path = openingPath; loop = false; break;
                case WormholePhase.Open:    path = cyclePath;   loop = true;  break;
                default:                    path = closingPath; loop = false; break;
            }

            var frames = GraphicsManager.Instance?.GetSpriteSheet(path);
            // Opening → Open: не рвём проигрывание open-клипа посередине — иначе игрок
            // никогда не увидит полную анимацию появления червоточины (TurnDuration≈2c,
            // а клип open длиннее ≈3с). Ставим cycle в очередь и переключимся, когда
            // open дойдёт до последнего кадра. Любой другой переход — сразу.
            if (prevPhase == WormholePhase.Opening
                && phase == WormholePhase.Open
                && _frames != null && _frames.Length > 0
                && _frame < _frames.Length - 1)
            {
                _pendingFrames = frames;
                _pendingLoop = loop;
                return;
            }
            _frames = frames;
            _loopCurrent = loop;
            _pendingFrames = null;
            _frame = 0;
            _timer = 0f;
            if (_renderer != null && _frames != null && _frames.Length > 0)
                _renderer.sprite = _frames[0];
        }

        private void Update()
        {
            if (_frames == null || _frames.Length == 0 || _renderer == null) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < SecPerFrame) return;
            int advance = (int)(_timer / SecPerFrame);
            _timer -= advance * SecPerFrame;
            int next = _frame + advance;
            if (next < _frames.Length)
            {
                _frame = next;
                _renderer.sprite = _frames[_frame];
                return;
            }
            if (_loopCurrent)
            {
                _frame = next % _frames.Length;
                _renderer.sprite = _frames[_frame];
                return;
            }
            // Не-loop клип доиграл. Если в очереди есть pending (Opening→Open) —
            // бесшовно переключаемся: последний кадр текущего → frame 0 нового.
            if (_pendingFrames != null && _pendingFrames.Length > 0)
            {
                _frames = _pendingFrames;
                _loopCurrent = _pendingLoop;
                _pendingFrames = null;
                _frame = 0;
                _renderer.sprite = _frames[0];
                return;
            }
            _frame = _frames.Length - 1;
            _renderer.sprite = _frames[_frame];
        }
    }

    /// <summary>
    /// Спавнит/убирает визуалы червоточин в текущей системе. Каждый тик синхронизирует
    /// внутренний словарь с <see cref="StarData.Wormholes"/>. Устанавливается в
    /// <see cref="SystemViewManager"/> вместе с HyperjumpPortalManager.
    /// </summary>
    public class WormholeVisualManager : MonoBehaviour
    {
        private readonly Dictionary<string, WormholeVisualController> _controllers = new();
        private Transform _container;
        private GameSettingsConfig _settings;

        public void Init(Transform container, GameSettingsConfig settings)
        {
            _container = container;
            _settings = settings;
        }

        public void Tick()
        {
            var star = GalaxyManager.Instance?.CurrentStar;
            if (star == null || _settings == null) { ClearAll(); return; }

            int curTurn = GalaxyManager.Instance?.GeneratedGalaxy?.CurrentTurn ?? 0;
            var alive = new HashSet<string>();
            if (star.Wormholes != null)
            {
                for (int i = 0; i < star.Wormholes.Count; i++)
                {
                    var wh = star.Wormholes[i];
                    if (wh == null) continue;
                    alive.Add(wh.Uid);

                    // Override → скрипт задал уникальную графику именно для этой червоточины.
                    var g = wh.Graphics;
                    string openingPath = !string.IsNullOrEmpty(g?.OpeningPath) ? g.OpeningPath : _settings.Wormhole_OpeningPath;
                    string cyclePath   = !string.IsNullOrEmpty(g?.CyclePath)   ? g.CyclePath   : _settings.Wormhole_CyclePath;
                    string closingPath = !string.IsNullOrEmpty(g?.ClosingPath) ? g.ClosingPath : _settings.Wormhole_ClosingPath;

                    bool freshCreated = false;
                    if (!_controllers.TryGetValue(wh.Uid, out var ctrl) || ctrl == null)
                    {
                        var go = new GameObject($"Wormhole_{wh.Uid[..6]}");
                        if (_container != null) go.transform.SetParent(_container, false);
                        ctrl = go.AddComponent<WormholeVisualController>();
                        ctrl.Setup(wh, _settings.Wormhole_WorldSize);
                        _controllers[wh.Uid] = ctrl;

                        var col = go.AddComponent<CircleCollider2D>();
                        col.isTrigger = true;
                        col.radius = 0.5f;

                        var info = go.AddComponent<SRG.UI.Common.ClickableInfo>();
                        info.Wormhole = wh;
                        info.WormholeStar = star;
                        freshCreated = true;
                    }

                    // Спавн через скрипт/консоль случается в Planning-фазе: WormholeSystem.TickExisting
                    // отработает раньше первого AnimateSystem и перекинет фазу Opening→Open — тогда
                    // визуально клип открытия не проиграется. Ловим это тут: если контроллер только что
                    // создан, а червоточина «свежая» (создана в этом или прошлом ходе), pre-roll'им
                    // Opening; pending-queue сама подхватит текущую фазу после его завершения.
                    if (freshCreated
                        && wh.Phase != WormholePhase.Opening
                        && wh.Phase != WormholePhase.Closing
                        && curTurn - wh.CreatedTurn <= 1)
                    {
                        ctrl.ApplyPhase(WormholePhase.Opening, openingPath, cyclePath, closingPath);
                    }

                    ctrl.ApplyPhase(wh.Phase, openingPath, cyclePath, closingPath);
                }
            }

            List<string> toRemove = null;
            foreach (var kv in _controllers)
            {
                if (alive.Contains(kv.Key)) continue;
                (toRemove ??= new()).Add(kv.Key);
            }
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    if (_controllers[id] != null && _controllers[id].gameObject != null)
                        Destroy(_controllers[id].gameObject);
                    _controllers.Remove(id);
                }
            }
        }

        public void ClearAll()
        {
            foreach (var kv in _controllers)
                if (kv.Value != null && kv.Value.gameObject != null)
                    Destroy(kv.Value.gameObject);
            _controllers.Clear();
        }
    }
}
