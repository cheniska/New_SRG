using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Dialog;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Controllers;
using SRG.Ships.Services;
using SRG.UI.Common;
using SRG.UI.HUD;
using SRG.Utils;

namespace SRG.UI.Screens
{
    public partial class PlanetUIController
    {
        // ─────────────────────────────────────────────────────────
        // Вспомогательные методы построения UI
        // ─────────────────────────────────────────────────────────

        private ScrollRect BuildScrollRect(Transform parent, out Text contentText)
        {
            var vpGo = new GameObject("VP"); vpGo.transform.SetParent(parent, false);
            SetAnchors(vpGo, 0f, 0f, 1f, 1f);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content"); contentGo.transform.SetParent(vpGo.transform, false);
            var rt = contentGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f); rt.sizeDelta = Vector2.zero;

            contentText = contentGo.AddComponent<Text>();
            contentText.font = _font; contentText.fontSize = 13; contentText.color = ColText;
            contentText.alignment = TextAnchor.UpperLeft;
            contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            contentText.verticalOverflow = VerticalWrapMode.Overflow;
            contentText.supportRichText = true;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ScrollRect добавляем на Panel (parent), viewport = vpGo
            var srComp = parent.gameObject.GetComponent<ScrollRect>();
            if (srComp == null) srComp = parent.gameObject.AddComponent<ScrollRect>();
            srComp.content = rt; srComp.viewport = vpGo.GetComponent<RectTransform>();
            srComp.horizontal = false; srComp.vertical = true;
            var sr = srComp;
            sr.scrollSensitivity = 40f; sr.movementType = ScrollRect.MovementType.Clamped;
            sr.verticalNormalizedPosition = 1f;
            return sr;
        }

        // Make*/SetAnchors мигрированы на UIBuilder (этап B5 рефакторинга). Локальные методы
        // оставлены как тонкие делегаты, которые автоматически передают `_font`, чтобы все
        // callsite'ы продолжали работать с прежней сигнатурой.
        private static GameObject MakePanel(Transform parent, string name, Color color)
            => UIBuilder.MakePanel(parent, name, color);

        private Text MakeText(Transform parent, string name, string text, int size, FontStyle style, Color color)
            => UIBuilder.MakeText(parent, name, text, _font, size, style, color);

        private Button MakeButton(Transform parent, string name, string label, int fontSize, Color color)
            => UIBuilder.MakeButton(parent, name, label, _font, fontSize, color);

        /// <summary>Шапка списка магазина: строка с теми же spacing/padding, что и строки
        /// данных, — колонки задаются последующими MakeRowLabel с теми же ширинами.</summary>
        private Transform MakeShopHeaderRow(Transform parent, float spacing, RectOffset padding)
        {
            var row = new GameObject("Header");
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            SetAnchors(row, 0f, 0.93f, 1f, 1f);
            AddShopRowLayout(row, spacing, padding);
            return row.transform;
        }

        /// <summary>Строка списка магазина фиксированной высоты с горизонтальной сеткой колонок.</summary>
        private static Transform MakeShopRow(Transform parent, string name, float height, float spacing, RectOffset padding)
        {
            var row = new GameObject(name);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            row.AddComponent<LayoutElement>().preferredHeight = height;
            AddShopRowLayout(row, spacing, padding);
            return row.transform;
        }

        // Настройки layout-группы общие для шапки и строк: только колонка с flexibleWidth
        // (название) растягивается, остальные держат заданную ширину — иначе колонки
        // расползаются и шапка перестаёт совпадать со строками.
        private static void AddShopRowLayout(GameObject row, float spacing, RectOffset padding)
        {
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing; hlg.padding = padding;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
        }

        private Text MakeRowLabel(Transform parent, string text, float width,
                                  bool flexible = false, bool bold = false, Color? color = null)
        {
            var go = new GameObject("Lbl"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width;
            le.flexibleWidth = flexible ? 1f : 0f;
            var txt = go.AddComponent<Text>();
            txt.font = _font; txt.text = text; txt.fontSize = 12; txt.color = color ?? ColText;
            txt.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            txt.alignment = TextAnchor.MiddleLeft; txt.supportRichText = true;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            return txt;
        }

        /// <summary>Ячейка-иконка товара в строке магазина. Путь — из ItemsConfig
        /// (StackGraphics.ResolveIcon); если иконки нет, ячейка остаётся пустой,
        /// чтобы колонки не съезжали.</summary>
        private static void MakeRowIcon(Transform parent, string goodId, int amount)
        {
            var go = new GameObject("Icon"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 24f; le.minWidth = 24f; le.flexibleWidth = 0;
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            var sprite = LoadGoodIcon(goodId, amount);
            img.sprite = sprite;
            img.enabled = sprite != null;
        }

        /// <summary>Загрузка инвентарной иконки товара. Спрайты нарезаны в режиме Multiple,
        /// поэтому сначала пробуем спрайтшит (первый кадр), затем одиночный Load.</summary>
        private static Sprite LoadGoodIcon(string goodId, int amount)
        {
            string path = StackGraphics.ResolveIcon(goodId, amount);
            if (string.IsNullOrEmpty(path)) return null;
            var frames = GraphicsManager.Instance?.GetSpriteSheet(path);
            if (frames != null && frames.Length > 0) return frames[0];
            return GraphicsManager.Instance?.TryGetSprite(path);
        }

        private void MakeSmallButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction action)
        {
            var btn = MakeButton(parent, label, label, 11, color);
            var le = btn.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = SHOP_COL_BTN; le.minWidth = SHOP_COL_BTN; le.flexibleWidth = 0;
            btn.onClick.AddListener(action);
        }

        private static void SetAnchors(GameObject go, float xMin, float yMin, float xMax, float yMax)
            => UIBuilder.SetAnchors(go, xMin, yMin, xMax, yMax);

        private static void ClearChildren(Transform t)
            => UIBuilder.ClearChildren(t);

        private PlanetUIConfig GetUICfg()
        {
            var cfg = GalaxyManager.Instance?.Context?.Config?.PlanetUI;
            return cfg ?? new PlanetUIConfig();
        }

        /// <summary>Возвращает подпись для заголовка экрана. Приоритет:
        /// 1) LandingUIConfig.Profile.ScreenTitles (явное задание в конфиге),
        /// 2) жёсткий Kind-based override для Overview (планета → «Планета», станция →
        ///    «Командование станции», носитель → «Мостик»),
        /// 3) PlanetUIConfig.Screens[id].Title (общий заголовок вкладки), 4) сам screenId.</summary>
        private string ResolveScreenTitle(string screenId)
        {
            var kind = _site?.Kind ?? LandingSiteKind.Planet;
            var lui = GalaxyManager.Instance?.Context?.Config?.LandingUI;
            var profile = lui?.ResolveProfile(kind);
            if (profile?.ScreenTitles != null
                && profile.ScreenTitles.TryGetValue(screenId, out var overrideTitle)
                && !string.IsNullOrEmpty(overrideTitle))
                return overrideTitle;

            // Kind-based дефолты — чтобы «Командование станции» показывалось даже без
            // подключённого LandingUIConfig.json. Меняется только заголовок Overview:
            // остальные экраны (Hangar/GoodsShop/EquipShop/InfoCenter) одинаковы для всех типов.
            if (screenId == SCR_OVERVIEW)
            {
                switch (kind)
                {
                    case LandingSiteKind.Station: return "Командование станции";
                    case LandingSiteKind.Carrier: return "Мостик";
                }
            }

            var cfg = GetUICfg();
            if (cfg.Screens != null && cfg.Screens.TryGetValue(screenId, out var scrCfg)
                && !string.IsNullOrEmpty(scrCfg?.Title))
                return scrCfg.Title;
            return screenId;
        }

        private static List<PlanetNavButtonConfig> DefaultNavButtons() => new()
        {
            new PlanetNavButtonConfig { Id = SCR_OVERVIEW,   Label = "Планета"       },
            new PlanetNavButtonConfig { Id = SCR_HANGAR,     Label = "Ангар"         },
            new PlanetNavButtonConfig { Id = SCR_GOODS,      Label = "Магазин"       },
            new PlanetNavButtonConfig { Id = SCR_EQUIP,      Label = "Оборудование"  },
            new PlanetNavButtonConfig { Id = SCR_INFO,       Label = "Инфо-центр"    },
        };
    }
}
