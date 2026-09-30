using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SRG.Combat;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships;
using SRG.Controllers;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    public partial class ShipFormView
    {
        // ── Загрузка спрайтов ─────────────────────────────────────────────────────

        private static Sprite[] LoadItemSpriteSheet(ItemInstance item)
        {
            if (item == null || string.IsNullOrEmpty(item.GraphicPath)) return null;
            var gm = GraphicsManager.Instance;
            // Путь на «_i» — статичная иконка (артефакты и др.), листа заведомо нет:
            // не зовём GetSpriteSheet, чтобы не поднимать false-positive LogError о
            // недостающем .png.json.
            if (gm != null && !item.GraphicPath.EndsWith("_i"))
            {
                var sheet = gm.GetSpriteSheet(item.GraphicPath);
                if (sheet != null && sheet.Length > 0) return sheet;
            }
            var single = gm?.TryGetSprite(item.GraphicPath);
            return single != null ? new[] { single } : null;
        }

        private static Sprite LoadItemStaticSprite(ItemInstance item)
        {
            if (item == null) return null;
            // Теневой предмет стека мог быть создан до назначения иконок в ItemsConfig
            // (старый сейв) — дорезолвиваем путь прямо при отрисовке.
            if (string.IsNullOrEmpty(item.GraphicPath) &&
                item.Uid != null && item.Uid.StartsWith(ShipInventory.StackItemUidPrefix))
                item.GraphicPath = StackGraphics.ResolveIcon(item.ItemId, item.Weight);
            if (string.IsNullOrEmpty(item.GraphicPath)) return null;
            var gm = GraphicsManager.Instance;
            string staticPath = SwapToStaticPath(item.GraphicPath);
            var sprite = gm?.TryGetSprite(staticPath);
            if (sprite != null) return sprite;
            if (!ReferenceEquals(staticPath, item.GraphicPath))
                sprite = gm?.TryGetSprite(item.GraphicPath);
            if (sprite == null)
            {
                var frames = LoadItemSpriteSheet(item);
                if (frames != null && frames.Length > 0) return frames[0];
            }
            return sprite;
        }

        /// <summary>Подменяет суффикс пути _a/_c на _i (статичная иконка вместо анимации).</summary>
        private static string SwapToStaticPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.EndsWith("_a") || path.EndsWith("_c"))
                return path.Substring(0, path.Length - 2) + "_i";
            return path;
        }

        private static void EnsureSpritesLoaded()
        {
            if (_spritesLoaded) return;
            _formSprite = GraphicsManager.Instance?.GetSprite(ResourceRoot + "FormShipMain1");
            LoadSlotConfig();
            _spritesLoaded = true;
        }

        private static void LoadSlotConfig()
        {
            _defaultRegular = BuildDefaultSet(isArt: false);
            _defaultArt     = BuildDefaultSet(isArt: true);
            _slotOverrides.Clear();

            var json = Resources.Load<TextAsset>(GalaxyConstants.PATH_SHIP_FORM_SLOTS_CFG);
            if (json == null) return;
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(json.text);
                var defReg = root["DefaultRegular"] as Newtonsoft.Json.Linq.JObject;
                if (defReg != null) _defaultRegular = ParseSet(defReg, _defaultRegular);
                var defArt = root["DefaultArt"] as Newtonsoft.Json.Linq.JObject;
                if (defArt != null) _defaultArt = ParseSet(defArt, _defaultArt);

                var slots = root["Slots"] as Newtonsoft.Json.Linq.JObject;
                if (slots != null)
                {
                    foreach (var kv in slots)
                    {
                        if (kv.Value is Newtonsoft.Json.Linq.JObject obj)
                            _slotOverrides[kv.Key] = ParseSet(obj, _defaultRegular);
                    }
                }

                // Layout-секция (B4 рефакторинга): позиции слотов и панели в абсолютных
                // координатах 1920×1080. Любая запись опциональна — отсутствующие поля
                // оставляются на code-defaults (см. верх класса).
                var layout = root["Layout"] as Newtonsoft.Json.Linq.JObject;
                if (layout != null)
                    ApplyLayoutOverrides(layout);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ShipFormView] Failed to parse Config/ShipFormSlots.json: {ex.Message}");
            }
        }

        /// <summary>Применяет «Layout»-секцию из JSON: разрешает перекрыть отдельные позиции
        /// или массивы позиций. Любой отсутствующий ключ — fallback на code-defaults.</summary>
        private static void ApplyLayoutOverrides(Newtonsoft.Json.Linq.JObject layout)
        {
            Vector2? ParseVec(Newtonsoft.Json.Linq.JToken t)
            {
                if (t == null) return null;
                float x = t["X"]?.ToObject<float?>() ?? 0f;
                float y = t["Y"]?.ToObject<float?>() ?? 0f;
                if (t["X"] == null && t["Y"] == null) return null;
                return new Vector2(x, y);
            }

            // FixedSlots: массив объектов {Key, X, Y} — каждая запись перекрывает code-default
            // по ключу slotKey. Полностью отсутствующая секция → дефолт сохраняется.
            var fixedSlots = layout["FixedSlots"] as Newtonsoft.Json.Linq.JArray;
            if (fixedSlots != null)
            {
                var dict = new System.Collections.Generic.Dictionary<string, Vector2>(FixedSlotLayout.Length);
                foreach (var entry in FixedSlotLayout) dict[entry.slotKey] = entry.pos;
                foreach (var item in fixedSlots)
                {
                    string key = item["Key"]?.ToString();
                    if (string.IsNullOrEmpty(key)) continue;
                    var v = ParseVec(item);
                    if (v.HasValue) dict[key] = v.Value;
                }
                // Преобразуем обратно с сохранением исходного порядка (важно: dock-сцена ожидает
                // порядок обхода для построения z-стека слотов).
                for (int i = 0; i < FixedSlotLayout.Length; i++)
                {
                    var k = FixedSlotLayout[i].slotKey;
                    if (dict.TryGetValue(k, out var p)) FixedSlotLayout[i] = (k, p);
                }
            }

            Vector2[] ParseVecArray(string key, Vector2[] fallback)
            {
                var arr = layout[key] as Newtonsoft.Json.Linq.JArray;
                if (arr == null) return fallback;
                var result = new Vector2[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                {
                    var v = ParseVec(arr[i]);
                    result[i] = v ?? (i < fallback.Length ? fallback[i] : Vector2.zero);
                }
                return result;
            }

            WeaponPositions   = ParseVecArray("WeaponPositions",   WeaponPositions);
            ArtefactPositions = ParseVecArray("ArtefactPositions", ArtefactPositions);

            ActivationSlotPos = ParseVec(layout["ActivationSlotPos"]) ?? ActivationSlotPos;
            CargoFirstCell    = ParseVec(layout["CargoFirstCell"])    ?? CargoFirstCell;
            ThrowSlotPos      = ParseVec(layout["ThrowSlotPos"])      ?? ThrowSlotPos;
            PilotPanelPos     = ParseVec(layout["PilotPanelPos"])     ?? PilotPanelPos;
        }

        private static SlotTextureSet BuildDefaultSet(bool isArt)
        {
            string prefix = isArt ? "SlotArt" : "Slot";
            var gm = GraphicsManager.Instance;
            return new SlotTextureSet
            {
                Usual        = gm?.GetSprite(ResourceRoot + prefix + "Usual"),
                MouseEntered = gm?.GetSprite(ResourceRoot + prefix + "MouseEntered"),
                Blocked      = gm?.GetSprite(ResourceRoot + prefix + "Blocked"),
                Broken       = gm?.GetSprite(ResourceRoot + prefix + "Broken"),
                Green        = gm?.GetSprite(ResourceRoot + prefix + "Green"),
                Width  = 96,
                Height = 96
            };
        }

        private static SlotTextureSet ParseSet(Newtonsoft.Json.Linq.JObject obj, SlotTextureSet fallback)
        {
            SlotTextureSet s = fallback;
            var gm = GraphicsManager.Instance;
            Sprite Load(string key)
            {
                var v = obj[key]?.ToString();
                return gm?.TryGetSprite(v);
            }
            var u  = Load("Usual");
            var me = Load("MouseEntered");
            var bl = Load("Blocked");
            var br = Load("Broken");
            var gr = Load("Green");
            return new SlotTextureSet
            {
                Usual        = u  != null ? u  : s.Usual,
                MouseEntered = me != null ? me : s.MouseEntered,
                Blocked      = bl != null ? bl : s.Blocked,
                Broken       = br != null ? br : s.Broken,
                Green        = gr != null ? gr : s.Green,
                Width  = obj["Width"]  != null ? obj["Width"].ToObject<float>()  : s.Width,
                Height = obj["Height"] != null ? obj["Height"].ToObject<float>() : s.Height
            };
        }

        private static SlotTextureSet GetSlotTextureSet(string slotKey, bool isArt)
        {
            if (_slotOverrides.TryGetValue(slotKey, out var s)) return s;
            return isArt ? _defaultArt : _defaultRegular;
        }

        private static Vector2 AbsToLocal(Vector2 abs) =>
            new Vector2(abs.x - RefWidth * 0.5f, RefHeight * 0.5f - abs.y);
    }
}
