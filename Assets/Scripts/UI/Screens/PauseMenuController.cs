using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using SRG.Config;
using SRG.Core;
using SRG.Ships.Player;

namespace SRG.UI.Screens
{
    // Меню паузы игровой сцены. Открывается по ESC, ставит Time.timeScale = 0.
    // Кнопки: Продолжить / Сохранить / Загрузить / Настройки / Главное меню.
    // Автосоздаётся через RuntimeInitializeOnLoadMethod в игровой сцене.
    public class PauseMenuController : MonoBehaviour
    {
        public static PauseMenuController Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

        [Tooltip("Имя сцены главного меню")] public string MainMenuSceneName = "MainMenu";

        private Canvas _canvas;
        private GameObject _root;      // overlay
        private GameObject _pausePanel;
        private GameObject _settingsPanel;
        private GameObject _toast;
        private Text _toastText;
        private float _toastUntil;

        private float _savedTimeScale = 1f;

        // Панели HUD, которые прячем на время паузы (сохраняем их предыдущее состояние).
        private readonly List<(GameObject go, bool wasActive)> _hiddenHud = new();

        // Отложенные значения настроек (применяются по «Принять»).
        private readonly Dictionary<string, object> _settingsPending = new();
        private SettingsBackup _settingsBackup;

        private static readonly Color COL_OVERLAY  = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color COL_PANEL_BG = new Color(0.06f, 0.08f, 0.12f, 0.96f);
        private static readonly Color COL_TITLE    = new Color(0.80f, 0.92f, 1.00f, 1f);
        private static readonly Color COL_SUBTITLE = new Color(0.29f, 0.42f, 0.53f, 1f);
        private static readonly Color COL_BTN_BG   = new Color(0.10f, 0.16f, 0.28f, 1f);
        private static readonly Color COL_BTN_TXT  = new Color(0.85f, 0.95f, 1.00f, 1f);
        private static readonly Color COL_DANGER   = new Color(0.55f, 0.12f, 0.14f, 1f);
        private static readonly Color COL_TOAST_BG = new Color(0.05f, 0.09f, 0.14f, 0.95f);
        private static readonly Color COL_LINE     = new Color(0f, 0.83f, 1f, 1f);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (IsSuppressed()) return;
                if (IsOpen) Close();
                else Open();
            }

            if (_toast != null && _toast.activeSelf && Time.unscaledTime >= _toastUntil)
                _toast.SetActive(false);
        }

        // Не открываем меню паузы поверх других модальных окон / когда идёт анимация смерти.
        private static bool IsSuppressed()
        {
            if (GameConsoleController.IsOpen) return true;
            // экран смерти показан — ESC пусть его игнорирует
            var death = DeathScreenController.Instance;
            if (death != null && death.gameObject.activeInHierarchy)
            {
                // если экран смерти реально виден (canvas активен), не перебиваем
                var canvas = death.GetComponentInChildren<Canvas>();
                if (canvas != null && canvas.gameObject.activeInHierarchy) return true;
            }
            return false;
        }

        public void Open()
        {
            if (_canvas == null) BuildCanvas();
            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            HideHudPanels();
            _root.SetActive(true);
            _pausePanel.SetActive(true);
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            RestoreHudPanels();
            Time.timeScale = _savedTimeScale;
        }

        // Скрываем нижний HUD (кнопки, лента уведомлений) на время паузы,
        // чтобы меню не соседствовало с элементами игрового интерфейса.
        private void HideHudPanels()
        {
            _hiddenHud.Clear();
            HideByName("HUDCanvas");
            HideByName("NotificationCanvas");
        }

        private void HideByName(string rootName)
        {
            var go = GameObject.Find(rootName);
            if (go == null) return;
            _hiddenHud.Add((go, go.activeSelf));
            go.SetActive(false);
        }

        private void RestoreHudPanels()
        {
            foreach (var (go, wasActive) in _hiddenHud)
                if (go != null) go.SetActive(wasActive);
            _hiddenHud.Clear();
        }

        // ─── Canvas build ────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("PauseMenuCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 150; // выше HUD (60), ниже DeathScreen (200)

            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Overlay");
            _root.transform.SetParent(canvasGo.transform, false);
            var rrt = _root.AddComponent<RectTransform>();
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.sizeDelta = Vector2.zero;
            _root.AddComponent<Image>().color = COL_OVERLAY;
            _root.SetActive(false);

            BuildPausePanel(_root.transform);
            // Settings-панель строится лениво в ShowSettings — актуальными значениями gs.
            BuildToast(_root.transform);
        }

        private void BuildPausePanel(Transform parent)
        {
            _pausePanel = MakePanel(parent, "PausePanel", new Vector2(420f, 460f));

            BuildTitle(_pausePanel.transform, "ПАУЗА", "GALACTIC COMMAND // STANDBY");

            float y0 = 130f;
            float step = -60f;
            BuildButton(_pausePanel.transform, "ПРОДОЛЖИТЬ",     new Vector2(0f, y0 + step * 0), Close);
            BuildButton(_pausePanel.transform, "СОХРАНИТЬ",      new Vector2(0f, y0 + step * 1), OnSave);
            BuildButton(_pausePanel.transform, "ЗАГРУЗИТЬ",      new Vector2(0f, y0 + step * 2), OnLoad);
            BuildButton(_pausePanel.transform, "НАСТРОЙКИ",      new Vector2(0f, y0 + step * 3), ShowSettings);
            BuildButton(_pausePanel.transform, "ГЛАВНОЕ МЕНЮ",   new Vector2(0f, y0 + step * 4), OnMainMenu, danger: true);
        }

        private void BuildSettingsPanel(Transform parent)
        {
            _settingsPanel = MakePanel(parent, "SettingsPanel", new Vector2(560f, 560f));
            _settingsPanel.SetActive(false);

            BuildTitle(_settingsPanel.transform, "НАСТРОЙКИ", "SYSTEM CONFIGURATION");

            var gs = GalaxyManager.Instance?.Settings;

            float y = 140f;
            float rowStep = -46f;

            if (gs != null)
            {
                _settingsPending["TurnDuration"] = gs.TurnDuration;
                _settingsPending["MoveSpeed"] = gs.MoveSpeed;
                _settingsPending["ZoomSpeed"] = gs.ZoomSpeed;
                _settingsPending["EnableEdgeScrolling"] = gs.EnableEdgeScrolling;
                _settingsPending["EnableCentering"] = gs.EnableCentering;
                _settingsPending["SimulationInterruptOnActionDone"] = gs.SimulationInterruptOnActionDone;
                _settingsPending["RunInBackground"] = gs.RunInBackground;

                BuildSliderRow(_settingsPanel.transform, "Длительность хода", gs.TurnDuration, 0.5f, 6f, 0.5f, 1,
                    new Vector2(0f, y + rowStep * 0), v => _settingsPending["TurnDuration"] = v);

                BuildSliderRow(_settingsPanel.transform, "Скорость камеры", gs.MoveSpeed, 5f, 60f, 1f, 0,
                    new Vector2(0f, y + rowStep * 1), v => _settingsPending["MoveSpeed"] = v);

                BuildSliderRow(_settingsPanel.transform, "Скорость зума", gs.ZoomSpeed, 2f, 30f, 1f, 0,
                    new Vector2(0f, y + rowStep * 2), v => _settingsPending["ZoomSpeed"] = v);

                BuildToggleRow(_settingsPanel.transform, "Edge Scrolling", gs.EnableEdgeScrolling,
                    new Vector2(0f, y + rowStep * 3), v => _settingsPending["EnableEdgeScrolling"] = v);

                BuildToggleRow(_settingsPanel.transform, "Center on Player (C)", gs.EnableCentering,
                    new Vector2(0f, y + rowStep * 4), v => _settingsPending["EnableCentering"] = v);

                BuildToggleRow(_settingsPanel.transform, "Стоп симуляции на конце маршрута", gs.SimulationInterruptOnActionDone,
                    new Vector2(0f, y + rowStep * 5), v => _settingsPending["SimulationInterruptOnActionDone"] = v);

                BuildToggleRow(_settingsPanel.transform, "Работа в фоне", gs.RunInBackground,
                    new Vector2(0f, y + rowStep * 6), v => _settingsPending["RunInBackground"] = v);
            }
            else
            {
                var lbl = BuildLabel(_settingsPanel.transform, "GameSettings недоступны", new Vector2(0f, y),
                    new Vector2(400f, 28f), 14, TextAnchor.MiddleCenter);
                lbl.color = COL_SUBTITLE;
            }

            // Две кнопки внизу панели: Отмена и Принять.
            BuildButton(_settingsPanel.transform, "ОТМЕНА",  new Vector2(-90f, -240f), CancelSettings);
            BuildButton(_settingsPanel.transform, "ПРИНЯТЬ", new Vector2( 90f, -240f), ApplySettings);
        }

        private void ShowSettings()
        {
            var gs = GalaxyManager.Instance?.Settings;
            _settingsBackup = SettingsBackup.Capture(gs);
            _settingsPending.Clear();

            // Перестраиваем панель, чтобы контролы отразили актуальные значения gs.
            if (_settingsPanel != null) UnityEngine.Object.Destroy(_settingsPanel);
            BuildSettingsPanel(_root.transform);

            _pausePanel.SetActive(false);
            _settingsPanel.SetActive(true);
        }

        private void ApplySettings()
        {
            var gs = GalaxyManager.Instance?.Settings;
            if (gs != null)
            {
                ApplyToConfig(gs, _settingsPending);
                PersistToPlayerPrefs(gs);
                PlayerPrefs.Save();
                ShowToast("Настройки применены");
            }
            _settingsPending.Clear();
            _settingsBackup = null;
            _settingsPanel.SetActive(false);
            _pausePanel.SetActive(true);
        }

        private void CancelSettings()
        {
            var gs = GalaxyManager.Instance?.Settings;
            _settingsBackup?.Restore(gs);
            _settingsPending.Clear();
            _settingsBackup = null;
            _settingsPanel.SetActive(false);
            _pausePanel.SetActive(true);
        }

        private static void ApplyToConfig(GameSettingsConfig gs, Dictionary<string, object> v)
        {
            if (v.TryGetValue("TurnDuration", out var d1))   gs.TurnDuration   = Convert.ToSingle(d1);
            if (v.TryGetValue("MoveSpeed", out var d2))      gs.MoveSpeed      = Convert.ToSingle(d2);
            if (v.TryGetValue("ZoomSpeed", out var d3))      gs.ZoomSpeed      = Convert.ToSingle(d3);
            if (v.TryGetValue("EnableEdgeScrolling", out var d4)) gs.EnableEdgeScrolling = Convert.ToBoolean(d4);
            if (v.TryGetValue("EnableCentering", out var d5))     gs.EnableCentering     = Convert.ToBoolean(d5);
            if (v.TryGetValue("SimulationInterruptOnActionDone", out var d6))
                gs.SimulationInterruptOnActionDone = Convert.ToBoolean(d6);
            if (v.TryGetValue("RunInBackground", out var d7))
            {
                gs.RunInBackground = Convert.ToBoolean(d7);
                Application.runInBackground = gs.RunInBackground;
            }
        }

        private static void PersistToPlayerPrefs(GameSettingsConfig gs)
        {
            PlayerPrefs.SetFloat("TurnDuration", gs.TurnDuration);
            PlayerPrefs.SetFloat("MoveSpeed",    gs.MoveSpeed);
            PlayerPrefs.SetFloat("ZoomSpeed",    gs.ZoomSpeed);
            PlayerPrefs.SetInt("EnableEdgeScrolling",             gs.EnableEdgeScrolling ? 1 : 0);
            PlayerPrefs.SetInt("EnableCentering",                 gs.EnableCentering ? 1 : 0);
            PlayerPrefs.SetInt("SimulationInterruptOnActionDone", gs.SimulationInterruptOnActionDone ? 1 : 0);
            PlayerPrefs.SetInt("RunInBackground",                 gs.RunInBackground ? 1 : 0);
        }

        private class SettingsBackup
        {
            public float TurnDuration, MoveSpeed, ZoomSpeed;
            public bool EnableEdgeScrolling, EnableCentering, SimulationInterruptOnActionDone, RunInBackground;

            public static SettingsBackup Capture(GameSettingsConfig gs)
            {
                if (gs == null) return null;
                return new SettingsBackup
                {
                    TurnDuration = gs.TurnDuration,
                    MoveSpeed = gs.MoveSpeed,
                    ZoomSpeed = gs.ZoomSpeed,
                    EnableEdgeScrolling = gs.EnableEdgeScrolling,
                    EnableCentering = gs.EnableCentering,
                    SimulationInterruptOnActionDone = gs.SimulationInterruptOnActionDone,
                    RunInBackground = gs.RunInBackground,
                };
            }

            public void Restore(GameSettingsConfig gs)
            {
                if (gs == null) return;
                gs.TurnDuration = TurnDuration;
                gs.MoveSpeed = MoveSpeed;
                gs.ZoomSpeed = ZoomSpeed;
                gs.EnableEdgeScrolling = EnableEdgeScrolling;
                gs.EnableCentering = EnableCentering;
                gs.SimulationInterruptOnActionDone = SimulationInterruptOnActionDone;
                gs.RunInBackground = RunInBackground;
                Application.runInBackground = RunInBackground;
            }
        }

        // ─── Действия ────────────────────────────────────────────────────────

        private void OnSave()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null) { ShowToast("Игра не активна"); return; }
            try
            {
                gm.SaveGame();
                ShowToast("Сохранено");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PauseMenu] Save failed: {e.Message}");
                ShowToast("Ошибка сохранения");
            }
        }

        private void OnLoad()
        {
            var gm = GalaxyManager.Instance;
            if (gm == null) { ShowToast("Игра не активна"); return; }
            try
            {
                gm.LoadGame();
                ShowToast("Загружено");
                Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PauseMenu] Load failed: {e.Message}");
                ShowToast("Ошибка загрузки");
            }
        }

        private void OnMainMenu()
        {
            // Восстанавливаем timeScale перед сменой сцены — иначе анимации главного меню замрут.
            Time.timeScale = 1f;

            // Как и DeathScreenController — чистим DontDestroyOnLoad-объекты игровой сессии.
            if (PlayerShip.Instance != null)
                Destroy(PlayerShip.Instance.gameObject);
            if (GalaxyManager.Instance != null)
                Destroy(GalaxyManager.Instance.gameObject);
            if (DeathScreenController.Instance != null)
                Destroy(DeathScreenController.Instance.gameObject);

            Destroy(gameObject);
            SceneManager.LoadScene(MainMenuSceneName);
        }

        // ─── UI builders ─────────────────────────────────────────────────────

        private static GameObject MakePanel(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = COL_PANEL_BG;

            // Тонкая акцентная полоса сверху панели
            var line = new GameObject("AccentLine");
            line.transform.SetParent(go.transform, false);
            var lrt = line.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot     = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, 0f);
            lrt.sizeDelta = new Vector2(0f, 2f);
            line.AddComponent<Image>().color = COL_LINE;

            return go;
        }

        private static void BuildTitle(Transform parent, string title, string subtitle)
        {
            var t = BuildLabel(parent, title, new Vector2(0f, -34f), new Vector2(0f, 40f), 26, TextAnchor.UpperCenter);
            t.fontStyle = FontStyle.Bold;
            t.color = COL_TITLE;
            var st = t.GetComponent<RectTransform>();
            st.anchorMin = new Vector2(0f, 1f);
            st.anchorMax = new Vector2(1f, 1f);
            st.pivot     = new Vector2(0.5f, 1f);
            st.anchoredPosition = new Vector2(0f, -24f);
            st.sizeDelta = new Vector2(0f, 36f);

            var s = BuildLabel(parent, subtitle, new Vector2(0f, -60f), new Vector2(0f, 16f), 10, TextAnchor.UpperCenter);
            s.color = COL_SUBTITLE;
            var srt = s.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot     = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -60f);
            srt.sizeDelta = new Vector2(0f, 16f);
        }

        private static Text BuildLabel(Transform parent, string text, Vector2 pos, Vector2 size,
                                       int fontSize, TextAnchor anchor)
        {
            var go = new GameObject($"Lbl_{text}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var txt = go.AddComponent<Text>();
            txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize  = fontSize;
            txt.alignment = anchor;
            txt.color     = COL_BTN_TXT;
            txt.text      = text;
            return txt;
        }

        private static Button BuildButton(Transform parent, string label, Vector2 pos,
                                          Action onClick, bool danger = false)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(320f, 44f);

            var bg = go.AddComponent<Image>();
            bg.color = danger ? COL_DANGER : COL_BTN_BG;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cb = btn.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
            cb.pressedColor     = new Color(0.7f, 0.7f, 0.7f);
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
            txt.fontSize  = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color     = COL_BTN_TXT;
            txt.text      = label;
            return btn;
        }

        private static void BuildToggleRow(Transform parent, string label, bool value, Vector2 pos, Action<bool> onChange)
        {
            var row = new GameObject($"Row_{label}");
            row.transform.SetParent(parent, false);
            var rrt = row.AddComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.anchoredPosition = pos;
            rrt.sizeDelta = new Vector2(440f, 36f);

            var lbl = BuildLabel(row.transform, label, new Vector2(-90f, 0f), new Vector2(260f, 24f), 13, TextAnchor.MiddleLeft);
            var lrt = lbl.GetComponent<RectTransform>();
            lrt.anchoredPosition = new Vector2(-90f, 0f);

            var box = new GameObject("Toggle");
            box.transform.SetParent(row.transform, false);
            var brt = box.AddComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(160f, 0f);
            brt.sizeDelta = new Vector2(60f, 24f);
            var bg = box.AddComponent<Image>();
            bool state = value;
            bg.color = state ? new Color(0.15f, 0.55f, 0.30f, 1f) : new Color(0.20f, 0.22f, 0.28f, 1f);

            var stateLbl = BuildLabel(box.transform, state ? "ON" : "OFF",
                Vector2.zero, new Vector2(60f, 24f), 12, TextAnchor.MiddleCenter);

            var btn = box.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                state = !state;
                bg.color = state ? new Color(0.15f, 0.55f, 0.30f, 1f) : new Color(0.20f, 0.22f, 0.28f, 1f);
                stateLbl.text = state ? "ON" : "OFF";
                onChange?.Invoke(state);
            });
        }

        private static void BuildSliderRow(Transform parent, string label, float value,
                                           float min, float max, float step, int decimals,
                                           Vector2 pos, Action<float> onChange)
        {
            var row = new GameObject($"Row_{label}");
            row.transform.SetParent(parent, false);
            var rrt = row.AddComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.anchoredPosition = pos;
            rrt.sizeDelta = new Vector2(440f, 36f);

            BuildLabel(row.transform, label, new Vector2(-140f, 0f), new Vector2(200f, 24f), 13, TextAnchor.MiddleLeft);

            // Slider (Unity UI Slider)
            var sgo = new GameObject("Slider");
            sgo.transform.SetParent(row.transform, false);
            var srt = sgo.AddComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(80f, 0f);
            srt.sizeDelta = new Vector2(200f, 18f);

            // background
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(sgo.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            bgGo.AddComponent<Image>().color = new Color(0.15f, 0.18f, 0.24f, 1f);

            // fill area
            var fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(sgo.transform, false);
            var faRt = fillArea.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.25f);
            faRt.anchorMax = new Vector2(1f, 0.75f);
            faRt.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fRt = fill.AddComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero;
            fRt.anchorMax = Vector2.one;
            fRt.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.00f, 0.55f, 0.85f, 1f);

            // handle
            var handleArea = new GameObject("HandleArea");
            handleArea.transform.SetParent(sgo.transform, false);
            var haRt = handleArea.AddComponent<RectTransform>();
            haRt.anchorMin = new Vector2(0f, 0f);
            haRt.anchorMax = new Vector2(1f, 1f);
            haRt.offsetMin = new Vector2(8f, 0f);
            haRt.offsetMax = new Vector2(-8f, 0f);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var hRt = handle.AddComponent<RectTransform>();
            hRt.sizeDelta = new Vector2(14f, 20f);
            handle.AddComponent<Image>().color = new Color(0.85f, 0.95f, 1f, 1f);

            var slider = sgo.AddComponent<Slider>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.fillRect      = fRt;
            slider.handleRect    = hRt;
            slider.direction     = Slider.Direction.LeftToRight;
            slider.minValue      = min;
            slider.maxValue      = max;
            slider.value         = Mathf.Clamp(value, min, max);

            // value label
            var valLbl = BuildLabel(row.transform,
                decimals == 0 ? Mathf.RoundToInt(value).ToString() : value.ToString("F" + decimals),
                new Vector2(200f, 0f), new Vector2(50f, 24f), 12, TextAnchor.MiddleRight);
            valLbl.color = COL_SUBTITLE;

            slider.onValueChanged.AddListener(v =>
            {
                float snapped = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
                if (Mathf.Abs(snapped - v) > 0.0001f) slider.SetValueWithoutNotify(snapped);
                valLbl.text = decimals == 0 ? Mathf.RoundToInt(snapped).ToString() : snapped.ToString("F" + decimals);
                onChange?.Invoke(snapped);
            });
        }

        // ─── Toast ───────────────────────────────────────────────────────────

        private void BuildToast(Transform parent)
        {
            _toast = new GameObject("Toast");
            _toast.transform.SetParent(parent, false);
            var rt = _toast.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -280f);
            rt.sizeDelta = new Vector2(320f, 34f);
            _toast.AddComponent<Image>().color = COL_TOAST_BG;

            _toastText = BuildLabel(_toast.transform, "", Vector2.zero, new Vector2(320f, 34f), 13, TextAnchor.MiddleCenter);
            _toastText.color = COL_BTN_TXT;
            _toast.SetActive(false);
        }

        private void ShowToast(string message)
        {
            if (_toast == null) return;
            _toastText.text = message;
            _toast.SetActive(true);
            _toastUntil = Time.unscaledTime + 1.8f;
        }
    }

    /// <summary>
    /// Автоматически создаёт PauseMenuController при загрузке игровой сцены.
    /// Не создаётся в главном меню (нет GalaxyManager).
    /// </summary>
    internal static class PauseMenuBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (PauseMenuController.Instance != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<GalaxyManager>() == null) return;
            var go = new GameObject("PauseMenuController");
            go.AddComponent<PauseMenuController>();
        }
    }
}
