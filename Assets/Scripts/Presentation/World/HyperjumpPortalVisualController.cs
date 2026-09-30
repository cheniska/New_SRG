using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships.Movement;
using SRG.Simulation;

namespace SRG.Presentation.World
{
    /// <summary>
    /// Визуал портала гиперперехода. Один экземпляр на корабль, активный в данный момент в системе игрока.
    /// Управляется HyperjumpPortalManager: тот спавнит/убивает портал в зависимости от ShipData.HyperjumpPhase.
    ///
    /// Правила воспроизведения:
    ///   • mid-клип зацикливается: если он доиграл до конца, а следующий этап ещё не начался, mid идёт по новому кругу.
    ///   • При смене фазы новый клип не стартует мгновенно — он встаёт в очередь и стартует с кадра 0
    ///     только когда текущий клип доиграет до своего ПОСЛЕДНЕГО кадра. Это даёт бесшовный стык
    ///     "последний кадр первой → первый кадр второй".
    /// </summary>
    public class HyperjumpPortalVisualController : MonoBehaviour
    {
        public static string PathBegin => GalaxyConstants.PATH_HYPERJUMP_BEGIN;
        public static string PathMid   => GalaxyConstants.PATH_HYPERJUMP_MID;
        public static string PathEnd   => GalaxyConstants.PATH_HYPERJUMP_END;

        private const float Fps = 24f;
        private const float WorldSize = 1.25f;  // диаметр портала в мировых единицах
        private static readonly float SecPerFrame = 1f / Fps;

        private SpriteRenderer _renderer;
        private Sprite[] _currentFrames;
        private string _currentPath;   // путь текущего клипа — нужен для авто-перехода begin→mid
        private int _frame;
        private float _timer;
        private bool _frozen;          // дошли до последнего кадра не-loop клипа без pending → стоим
        private bool _loopCurrent;     // текущий клип должен зацикливаться
        private Sprite[] _pendingFrames;  // клип, который запустится после того, как текущий доиграет до конца
        private string _pendingPath;
        private bool _pendingLoop;

        /// <summary>Логическое состояние портала (под какой клип он сейчас сконфигурирован).</summary>
        public enum PortalState { Hidden, Opening, Closing }
        public PortalState State { get; private set; } = PortalState.Hidden;
        public ShipData Ship { get; private set; }

        public void Setup(ShipData ship)
        {
            Ship = ship;
            if (TryGetComponent(out _renderer) == false)
                _renderer = gameObject.AddComponent<SpriteRenderer>();
            transform.localScale = Vector3.one * WorldSize;
        }

        /// <summary>Устанавливает позицию портала и переключает клип под нужное состояние.</summary>
        public void Apply(PortalState state, Vector2 worldPos, float heading, bool flip180)
        {
            transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
            // Спрайт портала нарисован "входом вверх" → доворачиваем на -90° чтобы вход смотрел
            // по направлению heading. Для портала-выхода нужен дополнительный 180°.
            float rotDeg = heading * Mathf.Rad2Deg - 90f;
            if (flip180) rotDeg += 180f;
            transform.rotation = Quaternion.Euler(0f, 0f, rotDeg);

            if (State == state) return;
            bool freshClosing = state == PortalState.Closing && (_currentFrames == null || _currentFrames.Length == 0);
            State = state;
            switch (state)
            {
                case PortalState.Opening: QueueOrPlay(PathBegin, loop: false); break;
                case PortalState.Closing:
                    // Свежий портал, который сразу попадает в Closing — это случай ИГРОКА на HyperExit:
                    // в системе-источнике портал открылся во время HyperEnter, но игрок этого не видел
                    // (был в старой системе). После scene swap портал создаётся с нуля прямо «закрывающимся»,
                    // и end-клип стартует с почти-закрытого состояния — корабль будто появляется из ничего.
                    // Чтобы игрок увидел, КАК он выходит из портала, проиграем полную цепочку: begin → end.
                    if (freshClosing)
                    {
                        QueueOrPlay(PathBegin, loop: false);
                        QueueOrPlay(PathEnd, loop: false);
                    }
                    else
                    {
                        QueueOrPlay(PathEnd, loop: false);
                    }
                    break;
                default:
                    _currentFrames = null;
                    _currentPath = null;
                    _pendingFrames = null;
                    _pendingPath = null;
                    _loopCurrent = false;
                    _frozen = false;
                    if (_renderer != null) _renderer.sprite = null;
                    break;
            }
        }

        /// <summary>
        /// Принудительно перевести портал в Closing — используется менеджером, когда фаза корабля
        /// больше не требует портала, но текущий клип ещё играет (например, mid-loop). Гарантирует,
        /// что портал доиграет end и корректно закроется, а не зависнет в бесконечной mid-петле.
        /// </summary>
        public void ForceClosing()
        {
            if (State == PortalState.Closing) return;
            State = PortalState.Closing;
            QueueOrPlay(PathEnd, loop: false);
        }

        /// <summary>
        /// Если ничего не играет — запускаем сразу. Иначе встаём в очередь и ждём, пока текущий
        /// клип дойдёт до последнего кадра (если он зациклен — снимаем зацикливание, чтобы он не крутился вечно).
        /// </summary>
        private void QueueOrPlay(string path, bool loop)
        {
            var frames = GraphicsManager.Instance?.GetSpriteSheet(path);
            if (frames == null || frames.Length == 0) return;

            if (_currentFrames == null || _currentFrames.Length == 0)
            {
                StartClip(path, frames, loop);
                return;
            }

            _pendingFrames = frames;
            _pendingPath = path;
            _pendingLoop = loop;
            _loopCurrent = false;

            // Если текущий клип уже заморожен на последнем кадре — переключаемся немедленно.
            if (_frozen && _frame >= _currentFrames.Length - 1)
                SwitchToPending();
        }

        private void StartClip(string path, Sprite[] frames, bool loop)
        {
            _currentFrames = frames;
            _currentPath = path;
            _loopCurrent = loop;
            _pendingFrames = null;
            _pendingPath = null;
            _frame = 0;
            _timer = 0f;
            _frozen = false;
            ApplyFrame();
        }

        private void SwitchToPending()
        {
            if (_pendingFrames == null) return;
            StartClip(_pendingPath, _pendingFrames, _pendingLoop);
        }

        /// <summary>true — нет ничего в очереди, текущий клип не зациклен и стоит на последнем кадре.</summary>
        public bool IsFinished()
        {
            if (_currentFrames == null || _currentFrames.Length == 0) return true;
            return _pendingFrames == null
                && !_loopCurrent
                && _frame >= _currentFrames.Length - 1
                && _timer >= SecPerFrame;
        }

        private void Update()
        {
            if (_currentFrames == null || _currentFrames.Length == 0) return;
            if (_frozen) return;

            _timer += Time.unscaledDeltaTime;
            if (_timer < SecPerFrame) return;

            int advance = (int)(_timer / SecPerFrame);
            _timer -= advance * SecPerFrame;
            int next = _frame + advance;

            if (next < _currentFrames.Length)
            {
                _frame = next;
                ApplyFrame();
                return;
            }

            // Дошли до конца клипа.
            if (_pendingFrames != null)
            {
                // Бесшовный стык: текущий показал последний кадр → следующий стартует с frame 0.
                SwitchToPending();
                return;
            }
            if (_loopCurrent)
            {
                _frame = next % _currentFrames.Length;
                ApplyFrame();
                return;
            }
            // Авто-переход: begin доиграл, состояние всё ещё Opening — запускаем mid в цикле.
            // Без этого портал «зависал» на последнем кадре begin, не показывая активное состояние.
            if (State == PortalState.Opening && _currentPath == PathBegin)
            {
                var midFrames = GraphicsManager.Instance?.GetSpriteSheet(PathMid);
                if (midFrames != null && midFrames.Length > 0)
                {
                    StartClip(PathMid, midFrames, loop: true);
                    return;
                }
            }
            // Иначе фиксируемся на последнем кадре — например, end доиграл, ждём удаления менеджером.
            _frame = _currentFrames.Length - 1;
            _frozen = true;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (_renderer == null || _currentFrames == null) return;
            if (_frame < 0 || _frame >= _currentFrames.Length) return;
            _renderer.sprite = _currentFrames[_frame];
        }
    }

    /// <summary>
    /// Спавнит/удаляет порталы в текущей сцене (CurrentStar). Каждый корабль в гипере имеет
    /// 2 портала — на стороне источника (HyperjumpFromStarUid) и на стороне цели
    /// (HyperjumpTargetStarUid). Если текущая звезда — источник, видим source-портал;
    /// если цель — видим arrival-портал.
    ///
    /// Travel (1 ход до края) → source-портал Opening (предупредительное открытие);
    ///                        dest-портал НЕ показывается (откроется только на HyperArrive).
    /// HyperEnter → source-портал продолжает Opening (begin → mid); dest-портал по-прежнему скрыт.
    /// HyperArrive → source-портал Closing (корабль ушёл); dest-портал Opening (begin+mid) — этот
    ///               ход после scene swap игрок смотрит открытие портала прибытия.
    /// HyperExit  → source-портал закрыт (уже удалён); dest-портал Closing (end) — корабль вылетает.
    /// CancelEnd  → source-портал Closing.
    ///
    /// Ключ в _portals: "{shipUid}_src" или "{shipUid}_dst".
    /// </summary>
    public class HyperjumpPortalManager : MonoBehaviour
    {
        private readonly Dictionary<string, HyperjumpPortalVisualController> _portals = new();
        private Transform _container;

        public void Init(Transform container)
        {
            _container = container;
        }

        /// <summary>Должен вызываться каждый OnTurnAnimate. Создаёт/удаляет порталы, обновляет позицию и фазу.</summary>
        public void Tick(float progress)
        {
            var star = GameWorld.CurrentStar;
            var galaxy = GameWorld.GeneratedGalaxy;
            if (star == null || galaxy == null) { ClearAll(); return; }

            var alive = new HashSet<string>();

            // Сканируем все звёзды галактики — у любого корабля в HyperEnter/HyperExit/CancelEnd
            // нужно поднять портал в текущей сцене, если её UID совпадает с from- или target-uid.
            foreach (var s in galaxy.StarsMap.Values)
            {
                for (int i = 0; i < s.Ships.Count; i++)
                {
                    var ship = s.Ships[i];
                    if (ship == null) continue;
                    if (!NeedsAnyPortal(ship)) continue;

                    // Source-портал (на краю системы-источника). Для червоточины source-портал НЕ рисуется —
                    // сама червоточина уже является видимым порталом (WormholeVisualManager).
                    if (ship.HyperjumpFromStarUid == star.Uid && string.IsNullOrEmpty(ship.HyperjumpViaWormholeUid))
                    {
                        string id = $"{ship.Uid}_src";
                        alive.Add(id);
                        var portal = EnsurePortal(id, ship);
                        var state = SourcePortalState(ship.HyperjumpPhase);
                        portal.Apply(state, ship.HyperjumpEdge, ship.HyperjumpHeading, flip180: false);
                    }

                    // Arrival-портал (на краю системы-цели). CancelEnd не должен показывать arrival
                    // (отмена прыжка → точку выхода не открывали). Для червоточины arrival-портал
                    // тоже НЕ рисуется — в целевой системе стоит парная червоточина, из неё же
                    // корабль и появится fade-in-ом (см. ShipVisualController).
                    if (ship.HyperjumpTargetStarUid == star.Uid
                        && ship.HyperjumpPhase != HyperjumpPhase.CancelEnd
                        && string.IsNullOrEmpty(ship.HyperjumpViaWormholeUid))
                    {
                        string id = $"{ship.Uid}_dst";
                        alive.Add(id);
                        var portal = EnsurePortal(id, ship);
                        var state = DestPortalState(ship.HyperjumpPhase);
                        // Для arrival-портала heading хранится в HyperjumpArrivalHeading
                        // (Travel/HyperEnter — до CompleteJump), либо в HyperjumpHeading
                        // (HyperArrive/HyperExit — после CompleteJump). В обоих случаях это курс
                        // ВНУТРЬ системы цели; flip180 разворачивает «вход» наружу.
                        float arrivalHeading = ship.HyperjumpPhase == HyperjumpPhase.HyperArrive
                            || ship.HyperjumpPhase == HyperjumpPhase.HyperExit
                            ? ship.HyperjumpHeading
                            : ship.HyperjumpArrivalHeading;
                        portal.Apply(state, ship.HyperjumpArrivalEdge, arrivalHeading, flip180: true);
                    }
                }
            }

            // Устаревшие порталы (фаза корабля больше не требует портал):
            //   • Opening (begin или mid-loop) — форсируем переход в Closing, чтобы доиграл end и не зависал в цикле.
            //   • Closing с недоигравшим end — НЕ удаляем, ждём IsFinished (см. Update этого менеджера).
            //   • Hidden или IsFinished — удаляем сейчас.
            List<string> toRemove = null;
            foreach (var kv in _portals)
            {
                if (alive.Contains(kv.Key)) continue;
                var portal = kv.Value;
                if (portal == null) { (toRemove ??= new List<string>()).Add(kv.Key); continue; }
                if (portal.State == HyperjumpPortalVisualController.PortalState.Opening)
                    portal.ForceClosing();
                if (portal.State == HyperjumpPortalVisualController.PortalState.Hidden
                    || portal.IsFinished())
                    (toRemove ??= new List<string>()).Add(kv.Key);
            }
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    if (_portals[id] != null) Destroy(_portals[id].gameObject);
                    _portals.Remove(id);
                }
            }
        }

        /// <summary>
        /// Каждый кадр чистим порталы, которые доиграли end вне Tick (например, во время Planning-паузы
        /// между ходами — там Tick не вызывается, и портал иначе бы навсегда замёрз на последнем кадре).
        /// Удаляем ТОЛЬКО когда фаза корабля больше не требует портал — иначе Tick тут же пересоздаст его
        /// и end проиграется заново (визуальное «моргание»).
        /// </summary>
        private void Update()
        {
            if (_portals.Count == 0) return;
            List<string> toRemove = null;
            foreach (var kv in _portals)
            {
                var portal = kv.Value;
                if (portal == null) { (toRemove ??= new List<string>()).Add(kv.Key); continue; }
                if (portal.State != HyperjumpPortalVisualController.PortalState.Closing) continue;
                if (!portal.IsFinished()) continue;
                // end доигран — но если фаза корабля ещё держит портал в живых, не трогаем (Tick сам решит).
                var ship = portal.Ship;
                if (ship != null && NeedsAnyPortal(ship)) continue;
                (toRemove ??= new List<string>()).Add(kv.Key);
            }
            if (toRemove == null) return;
            foreach (var id in toRemove)
            {
                if (_portals[id] != null) Destroy(_portals[id].gameObject);
                _portals.Remove(id);
            }
        }

        public void ClearAll()
        {
            foreach (var kv in _portals)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _portals.Clear();
        }

        /// <summary>
        /// Нужен ли портал. HyperEnter/HyperArrive/HyperExit/CancelEnd — всегда. Travel — только если
        /// корабль уже в пределах одного хода от точки входа (предупредительное открытие).
        /// Проверяем И PreviousPosition (старт хода), И Position (конец) — это покрывает оба варианта
        /// обновления данных корабля (NPC и игрок).
        /// </summary>
        private static bool NeedsAnyPortal(ShipData ship)
        {
            switch (ship.HyperjumpPhase)
            {
                case HyperjumpPhase.HyperEnter:
                case HyperjumpPhase.HyperArrive:
                case HyperjumpPhase.HyperExit:
                case HyperjumpPhase.CancelEnd:
                    return true;
                case HyperjumpPhase.Travel:
                    float speedPerTurn = ShipTrajectory.EffectiveSpeedPerTurn(ship);
                    if (speedPerTurn <= 0f) return false;
                    float distStart = Vector2.Distance(ship.PreviousPosition, ship.HyperjumpEdge);
                    float distEnd = Vector2.Distance(ship.Position, ship.HyperjumpEdge);
                    return distStart <= speedPerTurn || distEnd <= speedPerTurn;
                default:
                    return false;
            }
        }

        // Source-портал: открыт пока корабль летит и входит (Travel + HyperEnter). После HyperEnter
        // корабль мигрировал в целевую систему → source закрывается на HyperArrive/HyperExit/CancelEnd.
        private static HyperjumpPortalVisualController.PortalState SourcePortalState(HyperjumpPhase p)
            => p == HyperjumpPhase.HyperEnter || p == HyperjumpPhase.Travel
                ? HyperjumpPortalVisualController.PortalState.Opening
                : HyperjumpPortalVisualController.PortalState.Closing;  // HyperArrive | HyperExit | CancelEnd

        // Dest-портал: открыт всё время подлёта корабля к hyperjump-edge (Travel — предупреждение
        // для наблюдателя в целевой системе), в HyperEnter, и в HyperArrive (это ход,
        // когда игрок-владелец портала уже в целевой системе и смотрит открытие). На HyperExit
        // играет end и удаляется. Для игрока во время его собственного HyperArrive портал стартует
        // «с чистого листа» — сцена только что засвопнута, портала до этого не было.
        private static HyperjumpPortalVisualController.PortalState DestPortalState(HyperjumpPhase p)
            => p == HyperjumpPhase.HyperEnter || p == HyperjumpPhase.Travel || p == HyperjumpPhase.HyperArrive
                ? HyperjumpPortalVisualController.PortalState.Opening
                : HyperjumpPortalVisualController.PortalState.Closing;  // HyperExit

        private HyperjumpPortalVisualController EnsurePortal(string id, ShipData ship)
        {
            if (_portals.TryGetValue(id, out var p) && p != null) return p;
            var go = new GameObject($"HyperjumpPortal_{id}");
            if (_container != null) go.transform.SetParent(_container, false);
            p = go.AddComponent<HyperjumpPortalVisualController>();
            p.Setup(ship);
            _portals[id] = p;
            return p;
        }
    }
}
