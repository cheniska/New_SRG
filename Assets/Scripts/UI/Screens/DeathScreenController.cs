using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using SRG.Config;
using SRG.Core;
using SRG.Controllers;
using SRG.Simulation;

namespace SRG.UI.Screens
{
    // Экран смерти игрока. Показывается, когда PlayerManager сообщает о подтверждённой
    // смерти (после взрыва корабля). Текст берётся из TextConfig.DeathMessages по
    // причине смерти; кнопки — «Наблюдать» (закрывает экран, игра продолжается без
    // корабля игрока) и «В главное меню» (загружает MainMenu).
    //
    // Размещение: один автогенерируемый объект Canvas; компонент подписывается на
    // PlayerManager.OnPlayerDeathConfirmed в Awake. В сцене должен присутствовать
    // один экземпляр (DeathScreenBootstrap создаёт его автоматически).
    public class DeathScreenController : MonoBehaviour
    {
        public static DeathScreenController Instance { get; private set; }

        [Tooltip("Имя сцены главного меню")] public string MainMenuSceneName = "MainMenu";

        private Canvas _canvas;
        private GameObject _panel;
        private Text _messageText;

        private static readonly Color COL_OVERLAY    = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color COL_PANEL_BG   = new Color(0.06f, 0.08f, 0.12f, 0.95f);
        private static readonly Color COL_TITLE      = new Color(1.00f, 0.35f, 0.30f, 1f);
        private static readonly Color COL_MSG        = new Color(0.90f, 0.94f, 1.00f, 1f);
        private static readonly Color COL_BTN_BG     = new Color(0.12f, 0.18f, 0.30f, 1f);
        private static readonly Color COL_BTN_TEXT   = new Color(0.85f, 0.95f, 1.00f, 1f);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            PlayerManager.OnPlayerDeathConfirmed += HandleDeath;
        }

        private void OnDestroy()
        {
            PlayerManager.OnPlayerDeathConfirmed -= HandleDeath;
            if (Instance == this) Instance = null;
        }

        private void HandleDeath(PlayerDeathInfo info)
        {
            Show(info);
        }

        /// <summary>Уже отображался для текущей смерти? Защищает от повторных Show
        /// при последующих кадрах анимации после регистрации смерти.</summary>
        private bool _shownForCurrentDeath;

        public void Show(PlayerDeathInfo info)
        {
            if (_shownForCurrentDeath) return;
            _shownForCurrentDeath = true;
            if (_canvas == null) BuildCanvas();
            if (_messageText != null) _messageText.text = ResolveMessage(info);
            if (_panel != null) _panel.SetActive(true);
        }

        public void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private static string ResolveMessage(PlayerDeathInfo info)
        {
            var texts = GalaxyManager.Instance?.Context?.TextConfig?.DeathMessages;
            string template = null;
            if (texts != null)
            {
                string key = info?.Cause.ToString() ?? "Unknown";
                if (!texts.TryGetValue(key, out template) || string.IsNullOrEmpty(template))
                    texts.TryGetValue("Default", out template);
            }
            if (string.IsNullOrEmpty(template))
                template = "Ваш корабль уничтожен.";

            string killer = string.IsNullOrEmpty(info?.KillerName) ? "неизвестный противник" : info.KillerName;
            string star   = string.IsNullOrEmpty(info?.StarName)   ? "неизвестная система"  : info.StarName;
            return template.Replace("{killer}", killer).Replace("{star}", star);
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("DeathScreenCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200; // выше HUD

            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            var overlay = new GameObject("Overlay");
            overlay.transform.SetParent(canvasGo.transform, false);
            var overlayRt = overlay.AddComponent<RectTransform>();
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.sizeDelta = Vector2.zero;
            overlay.AddComponent<Image>().color = COL_OVERLAY;

            _panel = overlay;

            var panelBg = new GameObject("Panel");
            panelBg.transform.SetParent(overlay.transform, false);
            var prt = panelBg.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot     = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(640f, 360f);
            panelBg.AddComponent<Image>().color = COL_PANEL_BG;

            BuildTitle(panelBg.transform);
            _messageText = BuildMessage(panelBg.transform);
            BuildButton(panelBg.transform, "НАБЛЮДАТЬ ЗА МИРОМ", new Vector2(-110f, -130f), Observe);
            BuildButton(panelBg.transform, "ГЛАВНОЕ МЕНЮ",       new Vector2( 110f, -130f), GoToMainMenu);
        }

        private static void BuildTitle(Transform parent)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -28f);
            rt.sizeDelta = new Vector2(600f, 44f);

            var txt = go.AddComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 32;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color     = COL_TITLE;
            txt.text      = "ВЫ ПОГИБЛИ";
        }

        private static Text BuildMessage(Transform parent)
        {
            var go = new GameObject("Message");
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(24f, 80f);
            rt.offsetMax = new Vector2(-24f, -90f);

            var txt = go.AddComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 16;
            txt.alignment = TextAnchor.UpperCenter;
            txt.color     = COL_MSG;
            txt.text      = "";
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow   = VerticalWrapMode.Overflow;
            return txt;
        }

        private static void BuildButton(Transform parent, string label, Vector2 anchored, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(anchored.x, 40f);
            rt.sizeDelta = new Vector2(200f, 48f);

            var bg = go.AddComponent<Image>();
            bg.color = COL_BTN_BG;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cb = btn.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.35f);
            cb.pressedColor     = new Color(0.7f, 0.7f, 0.75f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;
            trt.anchoredPosition = Vector2.zero;

            var txt = tgo.AddComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = 14;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color     = COL_BTN_TEXT;
            txt.text      = label;
        }

        private void Observe()
        {
            PlayerManager.Instance?.EnterObserverMode();
            Hide();
        }

        private void GoToMainMenu()
        {
            // Сбрасываем DontDestroyOnLoad-объекты: PlayerShip и DeathScreen — иначе
            // при возвращении в игру всё пойдёт мимо.
            if (PlayerShip.Instance != null)
                Destroy(PlayerShip.Instance.gameObject);
            Destroy(gameObject);

            SceneManager.LoadScene(MainMenuSceneName);
        }
    }

    /// <summary>
    /// Автоматически создаёт DeathScreenController на старте игровой сцены.
    /// Срабатывает после загрузки сцены — не нужно ручного размещения в инспекторе.
    /// </summary>
    internal static class DeathScreenBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (DeathScreenController.Instance != null) return;
            // Создаём только если в сцене есть GalaxyManager (игровая сцена, а не меню).
            if (Object.FindAnyObjectByType<GalaxyManager>() == null) return;
            var go = new GameObject("DeathScreenController");
            go.AddComponent<DeathScreenController>();
        }
    }
}
