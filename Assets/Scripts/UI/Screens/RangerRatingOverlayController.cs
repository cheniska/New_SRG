using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;
using SRG.Ships.Services;
using SRG.UI.Common;

namespace SRG.UI.Screens
{
    /// <summary>
    /// Плавающий оверлей «Рейтинг рейнджеров» — открывается кнопкой «Рейтинг» на HUD.
    /// Показывает минимальную статистику рейнджеров галактики (место, имя, сбитые
    /// доминаторы/пираты, очки), отсортированную через <see cref="ShipRatingService"/>
    /// по конфигу рейтинга "Rangers" (GalaxyConfig.json → "Ratings").
    /// </summary>
    public class RangerRatingOverlayController : MonoBehaviour
    {
        public static RangerRatingOverlayController Instance { get; private set; }

        private const string RATING_ID = "Rangers";
        private const float PANEL_W = 620f;
        private const float PANEL_H = 520f;
        private const float TITLE_H = 36f;

        private GameObject _root;
        private Text _title;
        private Text _body;
        private ScrollRect _scroll;
        private Font _font;

        public static void Toggle()
        {
            if (Instance == null) EnsureInstance();
            Instance.ToggleInternal();
        }

        private static void EnsureInstance()
        {
            var go = new GameObject("RangerRatingOverlay");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RangerRatingOverlayController>();
        }

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void ToggleInternal()
        {
            if (_root == null)
            {
                BuildUI();
                // new GameObject создаёт панель активной — считаем её «закрытой»,
                // чтобы первый клик по кнопке HUD её показал, а не спрятал.
                _root.SetActive(false);
            }
            bool nowVisible = !_root.activeSelf;
            _root.SetActive(nowVisible);
            if (nowVisible) Refresh();
        }

        private void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        // ── Построение ────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("RangerRatingCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("RangerRatingRoot");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot     = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            rootRt.anchoredPosition = Vector2.zero;
            _root.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f, 0.96f);

            BuildTitleBar();

            // Тело — прокручиваемый текст под шапкой.
            var area = new GameObject("BodyArea");
            area.transform.SetParent(_root.transform, false);
            var areaRt = area.AddComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(0f, 0f);
            areaRt.offsetMax = new Vector2(0f, -TITLE_H);
            area.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.16f, 1f);

            BuildScrollBody(area.transform);
        }

        private void BuildScrollBody(Transform parent)
        {
            var vpGo = new GameObject("VP");
            vpGo.transform.SetParent(parent, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(10f, 10f); vpRt.offsetMax = new Vector2(-10f, -10f);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            var rt = contentGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f); rt.sizeDelta = Vector2.zero;

            _body = contentGo.AddComponent<Text>();
            _body.font = _font; _body.fontSize = 14; _body.color = UIColorPalette.Text;
            _body.alignment = TextAnchor.UpperLeft;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.supportRichText = true;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = parent.gameObject.AddComponent<ScrollRect>();
            _scroll.content = rt; _scroll.viewport = vpRt;
            _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.scrollSensitivity = 40f; _scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        private void BuildTitleBar()
        {
            var title = new GameObject("TitleBar");
            title.transform.SetParent(_root.transform, false);
            var titleRt = title.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot     = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(0f, TITLE_H);
            titleRt.anchoredPosition = Vector2.zero;
            title.AddComponent<Image>().color = new Color(0.04f, 0.06f, 0.10f, 1f);

            var titleTxtGo = new GameObject("TitleText");
            titleTxtGo.transform.SetParent(title.transform, false);
            var tRt = titleTxtGo.AddComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(1f, 1f);
            tRt.offsetMin = new Vector2(12f, 0f); tRt.offsetMax = new Vector2(-40f, 0f);
            _title = titleTxtGo.AddComponent<Text>();
            _title.font = _font; _title.text = "Рейтинг рейнджеров";
            _title.fontSize = 14; _title.color = UIColorPalette.Accent;
            _title.alignment = TextAnchor.MiddleLeft; _title.fontStyle = FontStyle.Bold;

            var closeGo = new GameObject("CloseBtn");
            closeGo.transform.SetParent(title.transform, false);
            var cRt = closeGo.AddComponent<RectTransform>();
            cRt.anchorMin = cRt.anchorMax = new Vector2(1f, 0.5f);
            cRt.pivot     = new Vector2(1f, 0.5f);
            cRt.sizeDelta = new Vector2(28f, 28f);
            cRt.anchoredPosition = new Vector2(-4f, 0f);
            var cImg = closeGo.AddComponent<Image>();
            cImg.color = new Color(0.55f, 0.10f, 0.10f, 0.92f);
            var cBtn = closeGo.AddComponent<Button>();
            cBtn.targetGraphic = cImg;
            cBtn.onClick.AddListener(Hide);

            var xGo = new GameObject("X");
            xGo.transform.SetParent(closeGo.transform, false);
            var xRt = xGo.AddComponent<RectTransform>();
            xRt.anchorMin = Vector2.zero; xRt.anchorMax = Vector2.one; xRt.sizeDelta = Vector2.zero;
            var xTxt = xGo.AddComponent<Text>();
            xTxt.font = _font; xTxt.text = "×"; xTxt.fontSize = 20;
            xTxt.color = Color.white; xTxt.alignment = TextAnchor.MiddleCenter;
        }

        // ── Наполнение ────────────────────────────────────────────

        private void Refresh()
        {
            if (_body == null) return;

            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            var config = GalaxyManager.Instance?.Context?.Config;
            var cfg = ShipRatingService.FindRating(config, RATING_ID);

            if (cfg != null && !string.IsNullOrEmpty(cfg.DisplayName))
                _title.text = cfg.DisplayName;

            if (galaxy == null || cfg == null)
            {
                _body.text = "Рейтинг недоступен.";
                return;
            }

            List<(ShipData Ship, float Score)> list = ShipRatingService.Build(galaxy, cfg);
            if (list.Count == 0)
            {
                _body.text = "Пока никто из рейнджеров не отличился.";
                return;
            }

            var sb = new StringBuilder(1024);
            sb.AppendLine($"<b>Рейнджеров в списке: {list.Count}</b>");
            sb.AppendLine("<color=#8899AA>#   Имя — сбито доминаторов / пиратов — очки</color>");
            sb.AppendLine();

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i].Ship;
                string name = string.IsNullOrEmpty(s.Name) ? "неизвестный" : s.Name;
                if (s.IsPlayer) name = $"<color=#FFCC44>{name} (вы)</color>";
                sb.AppendLine(
                    $"{i + 1}.  {name} — {s.KillsDominator} / {s.KillsPirate} — <b>{list[i].Score:F0}</b> очк.");
            }

            _body.text = sb.ToString();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }
    }
}
