using System.Collections.Generic;
using UnityEngine;
using SRG.Combat;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Utils;

namespace SRG.Presentation.Effects
{
    /// <summary>
    /// Рисует визуальные «лучи» (LineRenderer) для связей между кораблями:
    /// — Буксир (TowedByUid): корабль ↔ цель уже на буксире (для не-IsItem).
    /// — Притягивание (PulledByUid): корабль ↔ цель, которая в процессе подтягивания.
    /// — Абордаж (BoardingTargetUid): атакующий ↔ абордируемый корабль.
    ///
    /// Параметры визуала берутся с соответствующего оборудования у инициатора связи:
    /// — Абордаж → BoardingHook;
    /// — Притягивание/буксир item → CargoGrabber (приоритет) → TowingRig (фолбэк);
    /// — Притягивание/буксир корабля → TowingRig (приоритет) → CargoGrabber (фолбэк).
    ///
    /// Поля визуала в JSON-конфиге оборудования (Params):
    ///   BeamColor, BeamColorSecondary — HTML-цвет.
    ///   BeamWidth — ширина (мир).
    ///   BeamSprite — путь к спрайту/текстуре в Resources (опционально). Если задан —
    ///                материал использует Tile-режим, иначе плоская цветная линия.
    /// </summary>
    public class TowBeamVisualController : MonoBehaviour
    {
        private readonly Dictionary<string, LineRenderer> _beams = new();
        private static readonly Color DefaultBeamColor = new Color(0.55f, 0.85f, 1f, 0.85f);
        private static readonly Color DefaultBeamSecondary = new Color(0.25f, 0.45f, 0.8f, 0.85f);
        private const float DefaultBeamWidth = 0.06f;

        private static Shader _spritesDefault;
        private static Shader SpritesDefault => _spritesDefault != null
            ? _spritesDefault
            : (_spritesDefault = Shader.Find("Sprites/Default"));

        public void Refresh(IReadOnlyList<(ShipData ship, Transform t)> shipTransforms)
        {
            if (shipTransforms == null) { Clear(); return; }

            var lookupT = new Dictionary<string, Transform>(shipTransforms.Count);
            var lookupS = new Dictionary<string, ShipData>(shipTransforms.Count);
            for (int i = 0; i < shipTransforms.Count; i++)
            {
                var pair = shipTransforms[i];
                if (pair.ship != null && pair.t != null)
                {
                    lookupT[pair.ship.Uid] = pair.t;
                    lookupS[pair.ship.Uid] = pair.ship;
                }
            }

            var used = new HashSet<string>();
            for (int i = 0; i < shipTransforms.Count; i++)
            {
                var src = shipTransforms[i];
                var srcShip = src.ship;
                if (srcShip == null || src.t == null) continue;

                // ── Буксир: srcShip → каждый его towed (рисуем для не-IsItem; item сидит на якоре). ─
                if (srcShip.TowedObjectUids != null)
                {
                    foreach (var towUid in srcShip.TowedObjectUids)
                    {
                        if (!lookupT.TryGetValue(towUid, out var towT)) continue;
                        if (!lookupS.TryGetValue(towUid, out var towS)) continue;
                        if (towS.IsItem) continue;
                        DrawBeam(srcShip, towS, src.t.position, towT.position, "tow:", used);
                    }
                }

                // ── Притягивание: srcShip → каждая цель с PulledByUid == srcShip.Uid. ─
                //   Луч появляется ТОЛЬКО когда цель находится в радиусе захвата срабатывающего
                //   оборудования (CargoGrabber.Range для предметов / TowingRig.PullRadius для кораблей).
                //   Если цель вышла за радиус — луч скрыт.
                foreach (var kv in lookupS)
                {
                    var cand = kv.Value;
                    if (cand == null) continue;
                    if (cand.PulledByUid != srcShip.Uid) continue;
                    if (!lookupT.TryGetValue(cand.Uid, out var candT)) continue;

                    float pullRadius = GetPullRadiusFor(srcShip, cand);
                    if (pullRadius <= 0f) continue;
                    float distSq = (cand.Position - srcShip.Position).sqrMagnitude;
                    if (distSq > pullRadius * pullRadius) continue;

                    DrawBeam(srcShip, cand, src.t.position, candT.position, "pull:", used);
                }

                // ── Абордаж: srcShip → BoardingTargetUid. ─
                if (!string.IsNullOrEmpty(srcShip.BoardingTargetUid)
                    && lookupT.TryGetValue(srcShip.BoardingTargetUid, out var boardT)
                    && lookupS.TryGetValue(srcShip.BoardingTargetUid, out var boardS))
                {
                    DrawBeam(srcShip, boardS, src.t.position, boardT.position, "board:", used);
                }
            }

            foreach (var kv in _beams)
                if (!used.Contains(kv.Key) && kv.Value != null)
                    kv.Value.enabled = false;
        }

        private void DrawBeam(ShipData src, ShipData target, Vector3 a, Vector3 b,
                              string kindPrefix, HashSet<string> used)
        {
            string key = kindPrefix + src.Uid + "→" + target.Uid;
            used.Add(key);

            var srcEquip = SelectBeamSource(src, target, kindPrefix);
            Color main  = ParseHex(srcEquip?.GetParamString("BeamColor"),          DefaultBeamColor);
            Color end   = ParseHex(srcEquip?.GetParamString("BeamColorSecondary"), DefaultBeamSecondary);
            float width = srcEquip != null ? srcEquip.GetParam("BeamWidth", DefaultBeamWidth) : DefaultBeamWidth;
            if (width <= 0f) width = DefaultBeamWidth;
            string spritePath = srcEquip?.GetParamString("BeamSprite");

            var lr = EnsureBeam(key, spritePath);
            lr.startWidth = width;
            lr.endWidth   = width;
            lr.startColor = main;
            lr.endColor   = end;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }

        /// <summary>
        /// Возвращает item на исходном корабле (src), с которого нужно брать параметры визуала луча.
        /// Приоритет зависит от типа связи и природы цели.
        /// </summary>
        private static ItemInstance SelectBeamSource(ShipData src, ShipData target, string kindPrefix)
        {
            if (src == null) return null;
            if (kindPrefix == "board:")
                return BoardingSystem.FindGrapplingHook(src); // BoardingHook

            bool targetIsItem = target != null && target.IsItem;
            // Pull/Tow: для item приоритет — CargoGrabber, для корабля — TowingRig.
            if (targetIsItem)
                return FindEquippedByCategory(src, EquipmentCategory.CargoGrabber)
                    ?? TowSystem.FindTowingRig(src);

            return TowSystem.FindTowingRig(src)
                ?? FindEquippedByCategory(src, EquipmentCategory.CargoGrabber);
        }

        private static ItemInstance FindEquippedByCategory(ShipData ship, string category)
        {
            if (ship?.Equipment == null) return null;
            foreach (var kv in ship.Equipment.Slots)
            {
                if (kv.Value == null) continue;
                if (!ship.AllItems.TryGetValue(kv.Value, out var item)) continue;
                if (item.Category == category && item.IsWorking) return item;
            }
            return null;
        }

        /// <summary>
        /// Радиус действующего захвата (мировые единицы) для src→target. Для предметов читается
        /// "Range" с CargoGrabber, для кораблей — "PullRadius" с TowingRig. Возвращает 0 если
        /// подходящего оборудования нет.
        /// </summary>
        private static float GetPullRadiusFor(ShipData src, ShipData target)
        {
            if (src == null || target == null) return 0f;
            if (target.IsItem)
            {
                var g = FindEquippedByCategory(src, EquipmentCategory.CargoGrabber);
                if (g != null)
                {
                    float r = g.GetParam("Range", 0f);
                    if (r <= 0f) r = g.GetParam("PullRadius", 0f);
                    if (r > 0f) return SRUnits.ToWorld(r);
                }
                var rig = TowSystem.FindTowingRig(src);
                if (rig != null)
                    return SRUnits.ToWorld(rig.GetParam("PullRadius", 0f));
                return 0f;
            }
            var rg = TowSystem.FindTowingRig(src);
            if (rg != null) return SRUnits.ToWorld(rg.GetParam("PullRadius", 0f));
            var cg = FindEquippedByCategory(src, EquipmentCategory.CargoGrabber);
            if (cg != null)
            {
                float r = cg.GetParam("PullRadius", 0f);
                if (r <= 0f) r = cg.GetParam("Range", 0f);
                return SRUnits.ToWorld(r);
            }
            return 0f;
        }

        public void Clear()
        {
            foreach (var lr in _beams.Values)
                if (lr != null) Destroy(lr.gameObject);
            _beams.Clear();
        }

        /// <summary>
        /// Возвращает LineRenderer для ключа, при необходимости перенастраивая материал/текстуру
        /// под текущий spritePath (или цветной режим, если spritePath пуст).
        /// </summary>
        private LineRenderer EnsureBeam(string key, string spritePath)
        {
            if (!_beams.TryGetValue(key, out var lr) || lr == null)
            {
                var go = new GameObject("Beam_" + key);
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.sortingOrder = SortingLayerRegistry.Get(SortLayer.TowBeam);
                _beams[key] = lr;
            }
            lr.enabled = true;
            ApplyMaterial(lr, spritePath);
            return lr;
        }

        private static void ApplyMaterial(LineRenderer lr, string spritePath)
        {
            if (!string.IsNullOrEmpty(spritePath))
            {
                var sprite = GraphicsManager.Instance?.TryGetSprite(spritePath);
                if (sprite != null && sprite.texture != null)
                {
                    if (lr.material == null || lr.material.mainTexture != sprite.texture)
                    {
                        var mat = new Material(SpritesDefault) { mainTexture = sprite.texture };
                        lr.material = mat;
                    }
                    lr.textureMode = LineTextureMode.Tile;
                    return;
                }
            }
            // Цветной режим: плоская линия без текстуры.
            if (lr.material == null || lr.material.mainTexture != null)
                lr.material = new Material(SpritesDefault);
            lr.textureMode = LineTextureMode.Stretch;
        }

        private static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (ColorUtility.TryParseHtmlString(hex, out var c)) { c.a = fallback.a; return c; }
            return fallback;
        }
    }
}
