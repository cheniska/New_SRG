using UnityEngine;
using UnityEngine.UI;
using SRG.UI.Common;

namespace SRG.UI.Screens
{
    /// <summary>
    /// Плавающий оверлей инфоцентра — открывается кнопкой «Инфо» на HUD и работает
    /// независимо от посадки на планету.
    ///
    /// Содержимое (чипы-фильтры + карточки новостей) строит общий <see cref="NewsFeedView"/> —
    /// тот же, что и на экране «Инфо-центр» формы планеты.
    /// </summary>
    public class InfoCenterOverlayController : MonoBehaviour
    {
        public static InfoCenterOverlayController Instance { get; private set; }

        private const float PANEL_W = 640f;
        private const float PANEL_H = 540f;
        private const float TITLE_H = 36f;

        private GameObject _root;
        private NewsFeedView _feed;
        private Font _font;

        public static void Toggle()
        {
            if (Instance == null) EnsureInstance();
            Instance.ToggleInternal();
        }

        private static void EnsureInstance()
        {
            var go = new GameObject("InfoCenterOverlay");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<InfoCenterOverlayController>();
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
            if (nowVisible) _feed.Refresh();
        }

        private void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        // ── Построение ────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("InfoCenterCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("InfoCenterRoot");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot     = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
            rootRt.anchoredPosition = Vector2.zero;
            _root.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f, 0.96f);

            BuildTitleBar();

            // Контейнер ленты — всё пространство под шапкой.
            var feedGo = new GameObject("Feed");
            feedGo.transform.SetParent(_root.transform, false);
            var feedRt = feedGo.AddComponent<RectTransform>();
            feedRt.anchorMin = Vector2.zero; feedRt.anchorMax = Vector2.one;
            feedRt.offsetMin = Vector2.zero;
            feedRt.offsetMax = new Vector2(0f, -TITLE_H);
            _feed = NewsFeedView.Create(feedRt, _font);
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
            var tTxt = titleTxtGo.AddComponent<Text>();
            tTxt.font = _font; tTxt.text = "Инфоцентр — сводки Галактического Совета";
            tTxt.fontSize = 14; tTxt.color = UIColorPalette.Accent;
            tTxt.alignment = TextAnchor.MiddleLeft; tTxt.fontStyle = FontStyle.Bold;

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
    }
}
