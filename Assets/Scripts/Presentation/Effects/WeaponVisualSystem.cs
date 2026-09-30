using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Config;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Utils;
using SRG.Simulation;

namespace SRG.Presentation.Effects
{
    /// <summary>
    /// Renders per-shot VFX during the simulation phase.
    /// Added to SystemViewManager in Awake; Tick() called from AnimateSystem().
    ///
    /// Projectile colors come from Visual.Palette per weapon (HitEffectConfig: ColorMain/ColorSpark).
    /// ColorMain — основной цвет снаряда/Beam; ColorSpark — цвет искр/Impact.
    /// Core и Trail производные: Core = white по умолчанию, Trail = ColorMain.
    /// If Visual is null or palette is missing, falls back to DamageType defaults.
    /// If Visual.Mode == "Sprite", loads the sprite from Visual.SpritePath (Resources/).
    ///
    /// Impact effect shape/count/radius/colors берутся из ShotEffects (Equipment.Weapons.WeaponsCommon.ShotEffects[pattern][damageType]).
    /// Shapes: "Sparks" (expanding radial), "Ring" (fixed radius, fade), "Burst" (expanding ring), "Flash" (single pulse).
    /// </summary>
    public class WeaponVisualSystem : MonoBehaviour
    {
        const float kSub       = 10f;   // GalaxyData.SubTurnsPerTurn
        const int   kZ         = 35;    // sorting order (above ships @ 30)
        const float kImpactDur = 0.065f;

        // ── DamageType colour defaults ────────────────────────────────────────────────
        static Color DefaultMain(DamageType dt) => dt switch
        {
            DamageType.Kinetic   => new Color(1.00f, 0.75f, 0.10f, 1f),
            DamageType.Explosive => new Color(1.00f, 0.30f, 0.05f, 1f),
            DamageType.Energy    => new Color(0.15f, 0.85f, 1.00f, 1f),
            _                    => Color.white,
        };
        static Color DefaultImpact(DamageType dt) => dt switch
        {
            DamageType.Kinetic   => new Color(1.00f, 0.95f, 0.65f, 1f),
            DamageType.Explosive => new Color(1.00f, 0.60f, 0.10f, 1f),
            DamageType.Energy    => new Color(0.60f, 1.00f, 1.00f, 1f),
            _                    => Color.white,
        };

        // ── colour resolution ─────────────────────────────────────────────────────────
        static bool TryParseHex(string hex, out Color c)
        {
            c = Color.white;
            return hex != null && ColorUtility.TryParseHtmlString(hex, out c);
        }

        // Main projectile body color (from weapon palette ColorMain или DamageType default)
        Color ResolveMain(ShotEvent shot)
        {
            var pal = shot.Visual?.Palette;
            if (pal?.ColorMain != null && TryParseHex(pal.ColorMain, out var c)) return c;
            return DefaultMain(shot.DamageType);
        }

        // Beam core (белый блик по умолчанию)
        Color ResolveCore(ShotEvent shot) => Color.white;

        // Trail / faded-line tint (= ColorMain снаряда)
        Color ResolveTrail(ShotEvent shot, Color mainBase) => mainBase;

        // Impact / sparks color — HitEffect.ColorSpark → palette ColorSpark → DamageType default
        Color ResolveImpact(ShotEvent shot)
        {
            if (shot.HitEffect?.ColorSpark != null && TryParseHex(shot.HitEffect.ColorSpark, out var c)) return c;
            var pal = shot.Visual?.Palette;
            if (pal?.ColorSpark != null && TryParseHex(pal.ColorSpark, out var c2)) return c2;
            return DefaultImpact(shot.DamageType);
        }

        // Sprite для снаряда — тихий поиск через GraphicsManager (общий кэш).
        // TryGetSprite не спамит LogError на невалидный путь; прогрев валидных путей
        // делает GalaxyManager.PreloadAllVisuals.
        Sprite GetProjectileSprite(ShotEvent shot)
        {
            if (shot.Visual?.IsSprite == true)
            {
                var sp = GraphicsManager.Instance?.TryGetSprite(shot.Visual.SpritePath);
                if (sp != null) return sp;
            }
            return _dot;
        }

        // ── impact dot count helpers ───────────────────────────────────────────────────
        static int ImpactAlloc(HitEffectConfig he, int patternMin) =>
            he != null ? Mathf.Max(he.Count, patternMin) : patternMin;

        // ── per-shot state ────────────────────────────────────────────────────────────
        class Vfx
        {
            public ShotEvent      Shot;
            public Vector2        From, To;
            public float          T0, T1, TFade;

            public SpriteRenderer[] Dots;
            public LineRenderer[]   Lines;
            public Vector2[]        ChainPts;
            public List<GameObject> All = new();

            // Impact dots range within Dots[]
            public int ImpactDotsStart;
            public int ImpactDotsCount;

            // Lazy-build: Build() ещё не вызвался для этого шота. Откладываем создание
            // GameObject'ов до момента, когда progress впервые достигнет T0 — спайк
            // алок размазывается по сабтёрнам (особенно полезно с очередью выстрелов).
            public bool Built;
        }

        // ── runtime state ─────────────────────────────────────────────────────────────
        readonly List<Vfx> _vfx = new();
        Transform  _root;
        Sprite     _dot;
        Material   _mat;
        TurnAnimationData _anim;

        // ── VFX object pools ──────────────────────────────────────────────────────────
        // Переиспользуем SpriteRenderer/LineRenderer GameObjects между ходами и между шотами
        // в пределах хода. Заменяет `new GameObject + AddComponent` (десятки-сотни алок за
        // первый кадр хода) на SetActive(true) из стека.
        readonly Stack<SpriteRenderer> _dotPool  = new();
        readonly Stack<LineRenderer>   _linePool = new();
        const int kDotPrewarm  = 100;
        const int kLinePrewarm = 30;

        // ── API ───────────────────────────────────────────────────────────────────────
        public void Init(Transform root)
        {
            _root = root != null ? root : transform;
            _dot  = SpriteUtility.CreateCircleSprite(16);
            _mat  = new Material(Shader.Find("Sprites/Default"));
            PrewarmPools();
        }

        void PrewarmPools()
        {
            for (int i = 0; i < kDotPrewarm; i++)  _dotPool.Push(CreatePooledDot());
            for (int i = 0; i < kLinePrewarm; i++) _linePool.Push(CreatePooledLine());
        }

        public void BeginSimulation(TurnAnimationData anim)
        {
            ReleaseAll();
            _anim = anim;
            if (anim?.Shots == null) return;

            // anim.Shots содержит выстрелы из ВСЕХ звёзд галактики (один TurnAnimationData
            // на ход). Без фильтра шоты другой системы рендерятся в координатах своих
            // кораблей поверх текущего вида — "перестрелки без кораблей" в случайных точках.
            // Бой всегда внутри одной звезды (CombatSubTurn выбирает цель из star.Ships),
            // поэтому достаточно проверить, что TargetUid принадлежит текущей системе.
            //
            // Здесь только регистрируем шоты со скелетом времени (T0/T1/TFade) — GameObject'ы
            // под dots/lines строятся лениво в TickVfx при первом достижении T0. Это
            // 1) распределяет аллокации (теперь — взятия из пула) по кадрам хода,
            // 2) экономит работу для прерванных симуляций.
            var localShipUids = BuildLocalShipUidSet();
            foreach (var shot in anim.Shots)
            {
                if (localShipUids != null
                    && !string.IsNullOrEmpty(shot.TargetUid)
                    && !localShipUids.Contains(shot.TargetUid))
                    continue;
                // Шоты, исходящие от ракеты (Homing/AoE-detonate), полностью обслуживаются
                // SystemViewManager + ExplosionPlayback: спрайт ракеты в полёте и in-place
                // взрыв на её GO. Если построить здесь Vfx-скелет, BuildShot достанет из
                // пула 6 точек + линию (Homing) или 12 точек (AoE), а Tick тут же спрячет
                // всё через fromMissile-чек — чистая потеря пула при залпе/групповом импакте.
                if (!string.IsNullOrEmpty(shot.AttackerUid)
                    && anim.MissileFrames != null
                    && anim.MissileFrames.ContainsKey(shot.AttackerUid))
                    continue;
                var v = MakeSkeleton(shot, anim);
                if (v != null) _vfx.Add(v);
            }
        }

        // Создаёт Vfx-запись без GameObject'ов — только тайминги и позиции. Build()
        // вызывается позже из EnsureBuilt при первом тике, когда шот становится активным.
        Vfx MakeSkeleton(ShotEvent shot, TurnAnimationData anim)
        {
            int dur = Mathf.Max(1, shot.ShotDuration);
            int arriveFrame = Mathf.Clamp(shot.SubTurn - 1 + dur, 0, GalaxyData.SubTurnsPerTurn);
            var v = new Vfx
            {
                Shot  = shot,
                From  = ShipPos(shot.AttackerUid, shot.SubTurn - 1, anim),
                To    = ShipPos(shot.TargetUid,   arriveFrame,      anim),
                T0    = (shot.SubTurn - 1) / kSub,
                T1    = arriveFrame        / kSub,
                Built = false,
            };
            v.TFade = Mathf.Min(v.T1 + kImpactDur, 1f);
            return v;
        }

        HashSet<string> BuildLocalShipUidSet()
        {
            var star = GameWorld.CurrentStar;
            if (star?.Ships == null) return null;
            var set = new HashSet<string>(star.Ships.Count);
            foreach (var s in star.Ships)
                if (s != null && !string.IsNullOrEmpty(s.Uid)) set.Add(s.Uid);
            return set;
        }

        public void Tick(float progress, TurnAnimationData anim)
        {
            _anim = anim;
            foreach (var v in _vfx)
                TickVfx(v, progress);
        }

        public void EndSimulation() => ReleaseAll();

        // ── internals ─────────────────────────────────────────────────────────────────
        // Возвращает все занятые GameObject'ы шотов в пулы — без Destroy.
        void ReleaseAll()
        {
            foreach (var v in _vfx)
            {
                if (v.Dots != null)
                    foreach (var sr in v.Dots) ReleaseDot(sr);
                if (v.Lines != null)
                    foreach (var lr in v.Lines) ReleaseLine(lr);
                v.All.Clear();
                v.Dots = null;
                v.Lines = null;
                v.ChainPts = null;
                v.Built = false;
            }
            _vfx.Clear();
        }

        void OnDestroy()
        {
            ReleaseAll();
            // Реальное уничтожение пуловых объектов — только при сносе системы.
            foreach (var sr in _dotPool)  if (sr != null && sr.gameObject != null) Destroy(sr.gameObject);
            foreach (var lr in _linePool) if (lr != null && lr.gameObject != null) Destroy(lr.gameObject);
            _dotPool.Clear();
            _linePool.Clear();
            if (_dot != null) { Destroy(_dot.texture); Destroy(_dot); }
            if (_mat != null) Destroy(_mat);
        }

        Vector2 ShipPos(string uid, int frame, TurnAnimationData anim)
        {
            frame = Mathf.Clamp(frame, 0, GalaxyData.SubTurnsPerTurn);
            if (uid == null) return Vector2.zero;
            if (anim.ShipFrames.TryGetValue(uid, out var f)) return f.SubTurns[frame];
            if (anim.MissileFrames.TryGetValue(uid, out var mf)) return mf.SubTurns[frame];
            if (PresentationContext.Player?.ShipData?.Uid == uid &&
                GameWorld.LastTurnData?.ShipFrames.TryGetValue(uid, out var pf) == true)
                return pf.SubTurns[frame];
            return Vector2.zero;
        }

        // Smoothly interpolated ship position at fractional progress ∈ [0..1].
        Vector2 ShipPosAt(string uid, float progress, TurnAnimationData anim)
        {
            if (uid == null || anim == null) return Vector2.zero;
            float subF = Mathf.Clamp(progress * kSub, 0f, GalaxyData.SubTurnsPerTurn);
            int a = Mathf.FloorToInt(subF);
            int b = Mathf.Min(a + 1, GalaxyData.SubTurnsPerTurn);
            return Vector2.Lerp(ShipPos(uid, a, anim), ShipPos(uid, b, anim), subF - a);
        }

        // Patterns whose visual is a sustained connection between attacker and target
        // and must follow both endpoints in real time as ships keep moving.
        static bool IsSustained(HitPattern p) => p switch
        {
            HitPattern.Beam    => true,
            HitPattern.Falloff => true,
            HitPattern.Chain   => true,
            _                  => false,
        };

        static Vector3 V3(Vector2 v) => new(v.x, v.y, 0f);

        // ── object factories (pooled) ─────────────────────────────────────────────────
        SpriteRenderer CreatePooledDot()
        {
            var go = new GameObject("VfxDot");
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = kZ;
            go.SetActive(false);
            return sr;
        }

        LineRenderer CreatePooledLine()
        {
            var go = new GameObject("VfxLine");
            go.transform.SetParent(_root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material          = _mat;
            lr.useWorldSpace     = true;
            lr.sortingOrder      = kZ;
            lr.numCapVertices    = 3;
            lr.numCornerVertices = 3;
            go.SetActive(false);
            return lr;
        }

        SpriteRenderer NewDot(Vfx v, float size, Color col, Sprite customSprite = null)
        {
            var sr = _dotPool.Count > 0 ? _dotPool.Pop() : CreatePooledDot();
            if (sr == null || sr.gameObject == null) sr = CreatePooledDot(); // защита от случайно прибитых объектов
            sr.sprite       = customSprite != null ? customSprite : _dot;
            sr.color        = col;
            sr.sortingOrder = kZ;
            sr.transform.localScale = Vector3.one * size;
            sr.transform.localPosition = Vector3.zero;
            sr.gameObject.SetActive(false); // включится в Tick через Show()
            v.All.Add(sr.gameObject);
            return sr;
        }

        LineRenderer NewLine(Vfx v, int pts, float width, Color col)
        {
            var lr = _linePool.Count > 0 ? _linePool.Pop() : CreatePooledLine();
            if (lr == null || lr.gameObject == null) lr = CreatePooledLine();
            lr.startColor    = col;
            lr.endColor      = col;
            lr.startWidth    = width;
            lr.endWidth      = width;
            lr.positionCount = pts;
            lr.gameObject.SetActive(false);
            v.All.Add(lr.gameObject);
            return lr;
        }

        void ReleaseDot(SpriteRenderer sr)
        {
            if (sr == null || sr.gameObject == null) return;
            sr.gameObject.SetActive(false);
            sr.sprite = null;
            _dotPool.Push(sr);
        }

        void ReleaseLine(LineRenderer lr)
        {
            if (lr == null || lr.gameObject == null) return;
            lr.gameObject.SetActive(false);
            lr.positionCount = 0;
            _linePool.Push(lr);
        }

        static Vector2[] Zigzag(Vector2 a, Vector2 b, int seg, int seed)
        {
            var rng = new System.Random(seed);
            var pts = new Vector2[seg + 1];
            pts[0] = a; pts[seg] = b;
            Vector2 dir  = (b - a).normalized;
            Vector2 perp = new(-dir.y, dir.x);
            float   amp  = (b - a).magnitude * 0.20f;
            for (int i = 1; i < seg; i++)
            {
                float t        = (float)i / seg;
                float envelope = 1f - Mathf.Abs(t * 2f - 1f);
                float offset   = (float)(rng.NextDouble() * 2 - 1) * amp * envelope;
                pts[i] = Vector2.Lerp(a, b, t) + perp * offset;
            }
            return pts;
        }

        // ── Vfx builder ───────────────────────────────────────────────────────────────
        // Ленивая инициализация Dots/Lines для шота. Вызывается из TickVfx при первом
        // активном кадре. После Built=true повторных аллокаций нет.
        void EnsureBuilt(Vfx v)
        {
            if (v.Built) return;
            v.Built = true;
            BuildShot(v);
        }

        void BuildShot(Vfx v)
        {
            var shot = v.Shot;
            Color mc = ResolveMain(shot);
            Color ic = ResolveImpact(shot);
            Sprite proj = GetProjectileSprite(shot);
            var he = shot.HitEffect;

            switch (shot.HitPattern)
            {
                // ── Point ────────────────────────────────────────────────────────────
                case HitPattern.Point:
                {
                    int imp = ImpactAlloc(he, 5);
                    v.Dots = new SpriteRenderer[1 + imp];
                    v.Dots[0] = NewDot(v, 0.08f, mc, proj);
                    for (int i = 1; i <= imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.ImpactDotsStart = 1; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Piercing ─────────────────────────────────────────────────────────
                case HitPattern.Piercing:
                {
                    int imp = ImpactAlloc(he, 3);
                    v.Lines = new LineRenderer[2];
                    v.Lines[0] = NewLine(v, 2, 0.04f, mc);
                    v.Lines[1] = NewLine(v, 2, 0.02f, new Color(mc.r, mc.g, mc.b, 0.25f));
                    v.Dots = new SpriteRenderer[imp];
                    for (int i = 0; i < imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.ImpactDotsStart = 0; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Shotgun ──────────────────────────────────────────────────────────
                case HitPattern.Shotgun:
                {
                    int imp = ImpactAlloc(he, 6);
                    v.Dots = new SpriteRenderer[4 + imp];
                    for (int i = 0; i < 4;   i++) v.Dots[i] = NewDot(v, 0.06f, mc, proj);
                    for (int i = 4; i < 4 + imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.ImpactDotsStart = 4; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Ricochet ─────────────────────────────────────────────────────────
                case HitPattern.Ricochet:
                {
                    int imp = ImpactAlloc(he, 4);
                    v.Dots = new SpriteRenderer[1 + imp];
                    v.Dots[0] = NewDot(v, 0.07f, mc, proj);
                    for (int i = 1; i <= imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.ImpactDotsStart = 1; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Chain ────────────────────────────────────────────────────────────
                case HitPattern.Chain:
                {
                    int imp = ImpactAlloc(he, 4);
                    v.ChainPts = Zigzag(v.From, v.To, 6, shot.WeaponId?.GetHashCode() ?? 42);
                    v.Lines = new LineRenderer[1];
                    v.Lines[0] = NewLine(v, v.ChainPts.Length, 0.03f, mc);
                    v.Dots = new SpriteRenderer[imp];
                    for (int i = 0; i < imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.ImpactDotsStart = 0; v.ImpactDotsCount = imp;
                    break;
                }
                // ── AoE: expanding ring from attacker ─────────────────────────────────
                case HitPattern.AoE:
                {
                    int cnt = ImpactAlloc(he, 12);
                    v.Dots = new SpriteRenderer[cnt];
                    for (int i = 0; i < cnt; i++) v.Dots[i] = NewDot(v, 0.07f, mc);
                    v.ImpactDotsStart = 0; v.ImpactDotsCount = cnt;
                    break;
                }
                // ── PointAoE: projectile → ring + sparks ──────────────────────────────
                case HitPattern.PointAoE:
                {
                    int imp = ImpactAlloc(he, 7);
                    v.Dots = new SpriteRenderer[1 + 12 + imp];
                    v.Dots[0] = NewDot(v, 0.09f, mc, proj);
                    for (int i = 1;  i <= 12;      i++) v.Dots[i] = NewDot(v, 0.07f, mc);
                    for (int i = 13; i < 13 + imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.ImpactDotsStart = 13; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Falloff ───────────────────────────────────────────────────────────
                case HitPattern.Falloff:
                {
                    int imp = ImpactAlloc(he, 3);
                    v.Lines = new LineRenderer[1];
                    var lr  = NewLine(v, 2, 0.05f, mc);
                    lr.endColor = new Color(mc.r, mc.g, mc.b, 0.04f);
                    v.Lines[0] = lr;
                    v.Dots = new SpriteRenderer[imp];
                    for (int i = 0; i < imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.ImpactDotsStart = 0; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Beam: outer + inner lines; optional flash at target ────────────────
                case HitPattern.Beam:
                {
                    v.Lines = new LineRenderer[2];
                    v.Lines[0] = NewLine(v, 2, 0.13f, mc);
                    v.Lines[1] = NewLine(v, 2, 0.05f, ResolveCore(shot));
                    int imp = he?.Count ?? 0;
                    if (imp > 0)
                    {
                        v.Dots = new SpriteRenderer[imp];
                        for (int i = 0; i < imp; i++) v.Dots[i] = NewDot(v, 0.06f, ic);
                    }
                    v.ImpactDotsStart = 0; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Homing ────────────────────────────────────────────────────────────
                case HitPattern.Homing:
                {
                    Color trail = ResolveTrail(shot, mc);
                    int imp = ImpactAlloc(he, 5);
                    v.Dots = new SpriteRenderer[1 + imp];
                    v.Dots[0] = NewDot(v, 0.07f, mc, proj);
                    for (int i = 1; i <= imp; i++) v.Dots[i] = NewDot(v, 0.05f, ic);
                    v.Lines = new LineRenderer[1];
                    v.Lines[0] = NewLine(v, 2, 0.02f, new Color(trail.r, trail.g, trail.b, 0.40f));
                    v.ImpactDotsStart = 1; v.ImpactDotsCount = imp;
                    break;
                }
                // ── Mine: pulsing dot at attacker, explosion at target ────────────────
                case HitPattern.Mine:
                {
                    int imp = ImpactAlloc(he, 8);
                    v.Dots = new SpriteRenderer[1 + imp];
                    v.Dots[0] = NewDot(v, 0.10f, mc);
                    for (int i = 1; i <= imp; i++) v.Dots[i] = NewDot(v, 0.06f, ic);
                    v.ImpactDotsStart = 1; v.ImpactDotsCount = imp;
                    break;
                }
            }
        }

        // ── per-frame dispatch ────────────────────────────────────────────────────────
        void TickVfx(Vfx v, float progress)
        {
            bool active = progress >= v.T0 && progress < v.TFade;
            if (!active)
            {
                // Шот ещё не дошёл до T0 — GameObject'ов нет (Built=false), нечего гасить.
                // Шот уже отыграл — All пуст после ReleaseAll, но на всякий случай прячем.
                if (v.Built)
                    foreach (var obj in v.All) if (obj != null && obj.activeSelf) obj.SetActive(false);
                return;
            }

            // Активный шот — гарантируем что GameObject'ы под него созданы (берём из пула).
            EnsureBuilt(v);

            // Sustained effects (Beam/Falloff/Chain): re-anchor endpoints to the
            // attacker/target's live positions so the visual follows moving ships.
            if (IsSustained(v.Shot.HitPattern) && _anim != null)
            {
                v.From = ShipPosAt(v.Shot.AttackerUid, progress, _anim);
                if (v.Shot.TargetUid != null)
                    v.To = ShipPosAt(v.Shot.TargetUid, progress, _anim);
                if (v.Shot.HitPattern == HitPattern.Chain && v.ChainPts != null && v.Lines != null && v.Lines[0] != null)
                {
                    v.ChainPts = Zigzag(v.From, v.To, 6, v.Shot.WeaponId?.GetHashCode() ?? 42);
                    if (v.Lines[0].positionCount != v.ChainPts.Length)
                        v.Lines[0].positionCount = v.ChainPts.Length;
                }
            }

            float dt  = v.T1 - v.T0;
            float t   = dt > 0.001f ? Mathf.Clamp01((progress - v.T0) / dt) : 1f;
            float fit = v.TFade - v.T1;
            float it  = (progress >= v.T1 && fit > 0.001f)
                ? Mathf.Clamp01((progress - v.T1) / fit)
                : 0f;

            switch (v.Shot.HitPattern)
            {
                case HitPattern.Point:    TickPoint    (v, t, it); break;
                case HitPattern.Piercing: TickPiercing (v, t, it); break;
                case HitPattern.Shotgun:  TickShotgun  (v, t, it); break;
                case HitPattern.Ricochet: TickRicochet (v, t, it); break;
                case HitPattern.Chain:    TickChain    (v, t, it); break;
                case HitPattern.AoE:      TickAoe      (v, t    ); break;
                case HitPattern.PointAoE: TickPointAoe (v, t, it); break;
                case HitPattern.Falloff:  TickFalloff  (v, t, it); break;
                case HitPattern.Beam:     TickBeam     (v, t, it); break;
                case HitPattern.Homing:   TickHoming   (v, t, it); break;
                case HitPattern.Mine:     TickMine     (v, t, it); break;
            }
        }

        // ── generic helpers ───────────────────────────────────────────────────────────
        static void Show(SpriteRenderer sr, bool on)
        {
            if (sr != null && sr.gameObject != null) sr.gameObject.SetActive(on);
        }
        static void ShowLine(LineRenderer lr, bool on)
        {
            if (lr != null && lr.gameObject != null) lr.gameObject.SetActive(on);
        }

        // ── Universal impact renderer ─────────────────────────────────────────────────
        // it ∈ [0..1]: 0 = just arrived, 1 = effect done.  center = world impact pos.
        void DrawImpact(Vfx v, float it, Vector2 center)
        {
            if (v.Dots == null || v.ImpactDotsCount == 0 || it < 0.001f)
            {
                HideImpactDots(v);
                return;
            }

            var he = v.Shot.HitEffect;
            int count = he != null ? Mathf.Min(he.Count, v.ImpactDotsCount) : v.ImpactDotsCount;
            float cfgRadius = he?.Radius ?? 0.30f;
            float alpha     = 1f - it;
            bool  vis       = alpha > 0.01f;
            string shape    = he?.Shape ?? "Sparks";

            Color impCol = ResolveImpact(v.Shot);

            if (shape == "Flash")
            {
                // Single growing dot at center
                var sr = v.Dots[v.ImpactDotsStart];
                Show(sr, vis);
                if (vis && sr != null)
                {
                    sr.transform.position   = V3(center);
                    sr.transform.localScale = Vector3.one * cfgRadius * (1f + it);
                    Color c = impCol; c.a = alpha; sr.color = c;
                }
                for (int i = 1; i < v.ImpactDotsCount; i++) Show(v.Dots[v.ImpactDotsStart + i], false);
            }
            else
            {
                // Sparks: expand from center.  Ring: appear at full radius, fade.  Burst: same as Sparks.
                float radius = shape == "Ring" ? cfgRadius : cfgRadius * it;

                for (int i = 0; i < count; i++)
                {
                    var sr = v.Dots[v.ImpactDotsStart + i];
                    if (sr == null || sr.gameObject == null) continue;
                    sr.gameObject.SetActive(vis);
                    if (!vis) continue;
                    float ang = (float)i / count * Mathf.PI * 2f;
                    sr.transform.position = V3(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
                    Color c = impCol; c.a = alpha; sr.color = c;
                }
                // hide unused pre-allocated dots
                for (int i = count; i < v.ImpactDotsCount; i++)
                    Show(v.Dots[v.ImpactDotsStart + i], false);
            }
        }

        void HideImpactDots(Vfx v)
        {
            if (v.Dots == null) return;
            for (int i = v.ImpactDotsStart; i < v.ImpactDotsStart + v.ImpactDotsCount; i++)
                Show(v.Dots[i], false);
        }

        // ── Point ─────────────────────────────────────────────────────────────────────
        void TickPoint(Vfx v, float t, float it)
        {
            if (v.Dots == null) return;
            bool trav = it < 0.001f;
            Show(v.Dots[0], trav);
            if (trav)
            {
                v.Dots[0].transform.position = V3(Vector2.Lerp(v.From, v.To, t));
                float s = 1f + Mathf.Sin(t * Mathf.PI) * 0.35f;
                v.Dots[0].transform.localScale = Vector3.one * 0.08f * s;
            }
            DrawImpact(v, it, v.To);
        }

        // ── Piercing ──────────────────────────────────────────────────────────────────
        void TickPiercing(Vfx v, float t, float it)
        {
            bool trav = it < 0.001f;
            if (v.Lines != null)
            {
                ShowLine(v.Lines[0], trav);
                ShowLine(v.Lines[1], trav);
                if (trav)
                {
                    const float lead = 0.18f;
                    Vector2 tip  = Vector2.Lerp(v.From, v.To, t);
                    Vector2 tail = Vector2.Lerp(v.From, v.To, Mathf.Max(0f, t - lead));
                    v.Lines[0].SetPosition(0, V3(tail));
                    v.Lines[0].SetPosition(1, V3(tip));

                    v.Lines[1].SetPosition(0, V3(v.From));
                    v.Lines[1].SetPosition(1, V3(tip));
                    Color c = v.Lines[1].startColor;
                    c.a = (1f - t) * 0.30f;
                    v.Lines[1].startColor = c;
                    v.Lines[1].endColor   = new Color(c.r, c.g, c.b, 0f);
                }
            }
            DrawImpact(v, it, v.To);
        }

        // ── Shotgun ───────────────────────────────────────────────────────────────────
        void TickShotgun(Vfx v, float t, float it)
        {
            if (v.Dots == null) return;
            bool trav = it < 0.001f;
            Vector2 dir       = (v.To - v.From).normalized;
            float   baseAngle = Mathf.Atan2(dir.y, dir.x);
            float   halfSpread = 18f * Mathf.Deg2Rad;
            float   dist      = (v.To - v.From).magnitude;

            for (int i = 0; i < 4; i++)
            {
                Show(v.Dots[i], trav);
                if (!trav) continue;
                float frac   = (float)i / 3f - 0.5f;
                float angle  = baseAngle + frac * halfSpread * 2f;
                Vector2 dest = v.From + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                v.Dots[i].transform.position = V3(Vector2.Lerp(v.From, dest, t));
            }
            DrawImpact(v, it, v.To);
        }

        // ── Ricochet ──────────────────────────────────────────────────────────────────
        void TickRicochet(Vfx v, float t, float it)
        {
            if (v.Dots == null) return;
            bool trav = it < 0.001f;
            Show(v.Dots[0], trav);
            if (trav)
            {
                Vector2 mid  = (v.From + v.To) * 0.5f;
                Vector2 perp = new Vector2(-(v.To - v.From).y, (v.To - v.From).x).normalized * 0.40f;
                Vector2 pos  = t < 0.5f
                    ? Vector2.Lerp(v.From, mid + perp, t * 2f)
                    : Vector2.Lerp(mid + perp, v.To, (t - 0.5f) * 2f);
                v.Dots[0].transform.position = V3(pos);
            }
            DrawImpact(v, it, v.To);
        }

        // ── Chain ─────────────────────────────────────────────────────────────────────
        void TickChain(Vfx v, float t, float it)
        {
            if (v.Lines == null || v.Lines[0] == null || v.ChainPts == null) return;
            bool trav = it < 0.001f;
            ShowLine(v.Lines[0], trav);
            if (trav)
            {
                int total   = v.ChainPts.Length;
                int visible = Mathf.Clamp(Mathf.CeilToInt(t * total), 2, total);
                v.Lines[0].positionCount = visible;
                for (int i = 0; i < visible; i++)
                    v.Lines[0].SetPosition(i, V3(v.ChainPts[i]));

                Color c = v.Lines[0].startColor;
                c.a = 0.55f + 0.45f * Mathf.Sin(Time.time * 55f);
                v.Lines[0].startColor = v.Lines[0].endColor = c;
            }
            DrawImpact(v, it, v.To);
        }

        // ── AoE: expanding ring from attacker (radius & count from HitEffect) ─────────
        // Ракетные AoE (ExplodeMissile в пустоте) сюда не доходят — отфильтрованы в
        // BeginSimulation. Их визуал даёт ExplosionPlayback на GO ракеты.
        void TickAoe(Vfx v, float t)
        {
            if (v.Dots == null) return;

            var he = v.Shot.HitEffect;
            float maxRadius = he?.Radius ?? 0.60f;
            int   count     = he != null ? Mathf.Min(he.Count, v.Dots.Length) : v.Dots.Length;
            float radius    = t * maxRadius;

            for (int i = 0; i < count; i++)
            {
                var sr = v.Dots[i];
                if (sr == null || sr.gameObject == null) continue;
                sr.gameObject.SetActive(true);
                float ang = (float)i / count * Mathf.PI * 2f;
                sr.transform.position = V3(v.From + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
                Color c = sr.color; c.a = 1f - t; sr.color = c;
            }
            for (int i = count; i < v.Dots.Length; i++) Show(v.Dots[i], false);
        }

        // ── PointAoE ──────────────────────────────────────────────────────────────────
        // Dots: [0]=projectile, [1..12]=ring, [13..N]=HitEffect sparks
        void TickPointAoe(Vfx v, float t, float it)
        {
            if (v.Dots == null) return;

            bool projOn = t < 0.5f && it < 0.001f;
            Show(v.Dots[0], projOn);
            if (projOn)
                v.Dots[0].transform.position = V3(Vector2.Lerp(v.From, v.To, t / 0.5f));

            float ringT = -1f;
            if (it < 0.001f && t >= 0.5f) ringT = (t - 0.5f) / 0.5f;
            else if (it > 0f)             ringT = it;

            for (int i = 1; i <= 12; i++)
            {
                var sr = v.Dots[i];
                if (sr == null || sr.gameObject == null) continue;
                bool vis = ringT >= 0f;
                sr.gameObject.SetActive(vis);
                if (!vis) continue;
                float ang = (float)(i - 1) / 12f * Mathf.PI * 2f;
                sr.transform.position = V3(v.To + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ringT * 0.50f);
                Color c = sr.color; c.a = 1f - ringT; sr.color = c;
            }

            DrawImpact(v, it > 0f ? it : 0f, v.To);
        }

        // ── Falloff ───────────────────────────────────────────────────────────────────
        void TickFalloff(Vfx v, float t, float it)
        {
            bool trav = it < 0.001f;
            if (v.Lines != null && v.Lines[0] != null)
            {
                ShowLine(v.Lines[0], trav);
                if (trav)
                {
                    v.Lines[0].SetPosition(0, V3(v.From));
                    v.Lines[0].SetPosition(1, V3(v.To));
                    Color sc = v.Lines[0].startColor;
                    sc.a = 0.65f + 0.35f * Mathf.Sin(t * Mathf.PI);
                    v.Lines[0].startColor = sc;
                }
            }
            DrawImpact(v, it, v.To);
        }

        // ── Beam ──────────────────────────────────────────────────────────────────────
        void TickBeam(Vfx v, float t, float it)
        {
            if (v.Lines == null) return;
            float alpha = it > 0f ? Mathf.Clamp01(1f - it / 0.60f) : 1f;
            bool  on    = alpha > 0.01f;

            float[] baseWidths = { 0.13f, 0.05f };
            for (int i = 0; i < v.Lines.Length; i++)
            {
                ShowLine(v.Lines[i], on);
                if (!on || v.Lines[i] == null) continue;
                v.Lines[i].SetPosition(0, V3(v.From));
                v.Lines[i].SetPosition(1, V3(v.To));

                float pulse = 1f + 0.40f * Mathf.Sin(t * Mathf.PI * 4f);
                float w     = baseWidths[i] * pulse;
                v.Lines[i].startWidth = v.Lines[i].endWidth = w;

                Color c = v.Lines[i].startColor; c.a = alpha; v.Lines[i].startColor = c;
                c       = v.Lines[i].endColor;   c.a = alpha; v.Lines[i].endColor   = c;
            }
            // Flash effect at target during impact window
            if (v.ImpactDotsCount > 0)
                DrawImpact(v, it, v.To);
        }

        // ── Homing ────────────────────────────────────────────────────────────────────
        // Ракетные Homing-шоты сюда не доходят — отфильтрованы в BeginSimulation. В полёте
        // ракету рисует SystemViewManager (реальный спрайт), на impact — ExplosionPlayback.
        // Эта ветка остаётся для гипотетических не-ракетных оружий с HitPattern.Homing.
        void TickHoming(Vfx v, float t, float it)
        {
            if (v.Dots == null) return;
            bool trav = it < 0.001f;

            Show(v.Dots[0], trav);

            if (trav)
            {
                Vector2 ctrl = (v.From + v.To) * 0.5f
                             + new Vector2(-(v.To - v.From).y, (v.To - v.From).x).normalized * 0.35f;
                Vector2 p0  = Vector2.Lerp(v.From, ctrl, t);
                Vector2 p1  = Vector2.Lerp(ctrl,   v.To,  t);
                Vector2 pos = Vector2.Lerp(p0, p1, t);
                v.Dots[0].transform.position = V3(pos);

                if (v.Lines != null && v.Lines[0] != null)
                {
                    ShowLine(v.Lines[0], true);
                    float t0    = Mathf.Max(0f, t - 0.14f);
                    Vector2 p0b = Vector2.Lerp(v.From, ctrl, t0);
                    Vector2 p1b = Vector2.Lerp(ctrl,   v.To,  t0);
                    v.Lines[0].SetPosition(0, V3(Vector2.Lerp(p0b, p1b, t0)));
                    v.Lines[0].SetPosition(1, V3(pos));
                }
            }
            else if (v.Lines != null && v.Lines[0] != null)
            {
                ShowLine(v.Lines[0], false);
            }

            if (v.Shot.DamageDealt > 0)
                DrawImpact(v, it, v.To);
            else
                HideImpactDots(v);
        }

        // ── Mine ──────────────────────────────────────────────────────────────────────
        void TickMine(Vfx v, float t, float it)
        {
            if (v.Dots == null) return;
            bool mineOn = it < 0.001f;
            Show(v.Dots[0], mineOn);
            if (mineOn)
            {
                v.Dots[0].transform.position = V3(v.From);
                float blink = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 9f);
                v.Dots[0].transform.localScale = Vector3.one * (0.10f * (0.85f + 0.15f * blink));
                Color c = v.Dots[0].color; c.a = 0.40f + 0.60f * blink; v.Dots[0].color = c;
            }
            DrawImpact(v, it, v.To);
        }
    }
}
