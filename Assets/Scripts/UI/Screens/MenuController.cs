using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SRG.Config;

namespace SRG.UI.Screens
{
    [RequireComponent(typeof(UIDocument))]
    public class MenuController : MonoBehaviour
    {

        [Header("References")] public GameSettingsConfig GameSettings;

        [Header("UXML Assets")]
        public VisualTreeAsset MainMenuUxml;
        public VisualTreeAsset SettingsMenuUxml;

        [Header("Scene")] public string GameSceneName = "GameScene";

        private UIDocument _doc;
        private VisualElement _root;
        private SettingsSnapshot _snapshot;
        private readonly Dictionary<string, object> _currentValues = new();
        private string _rebindingId = null;
        private Label _rebindingLabel = null;

        private void Awake()
        {
            _doc = GetComponent<UIDocument>();
            _root = _doc.rootVisualElement;

            if (GameSettings == null)
                Debug.LogError("[MenuController] GameSettings not assigned!");
            else
                LoadSavedSettings(GameSettings);
        }

        private void Start()
        {
            ShowMainMenu();
        }

        private void Update()
        {
            if (_rebindingId == null || !Input.anyKeyDown) return;

            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None) continue;
                if (!Input.GetKeyDown(kc)) continue;
                FinishRebind(kc);
                break;
            }
        }

        private void ShowMainMenu()
        {
            SwapDocument(MainMenuUxml);
            BindMainMenu();
            AnimateMenuIn();
        }

        private void ShowSettings()
        {
            _snapshot = SettingsSnapshot.Capture(GameSettings);
            SwapDocument(SettingsMenuUxml);
            BuildSettingsRows();
            BindSettingsButtons();
        }

        private void SwapDocument(VisualTreeAsset asset)
        {
            _root.Clear();
            asset.CloneTree(_root);
        }

        private void BindMainMenu()
        {
            Q<Button>("btn-new-game").clicked += () => OnMenuAction("new_game");
            Q<Button>("btn-settings").clicked += () => OnMenuAction("settings");
            Q<Button>("btn-quit").clicked += () => OnMenuAction("quit");
            var coordsLabel = Q<Label>("hud-coords");
            _root.schedule.Execute(() =>
            {
                coordsLabel.text = $"SYS : MAIN_MENU · {DateTime.Now:HH:mm:ss} · STATUS : STANDBY";
            }).Every(1000);

            var starfield = Q("starfield");
            if (starfield != null)
            {
                var stars = new StarfieldElement();
                starfield.Add(stars);
                stars.StretchToParentSize();
            }
        }

        private void AnimateMenuIn()
        {
            AnimateFadeSlide(Q("logo"), delayMs: 50);

            var buttons = _root.Query<Button>(className: "menu-btn").ToList();
            for (int i = 0; i < buttons.Count; i++)
                AnimateFadeSlide(buttons[i], delayMs: 150 + i * 80);

            AnimateFadeSlide(Q("hud-corner"), delayMs: 300);
            AnimateFadeSlide(Q("hud-bottom"), delayMs: 400);
        }

        private static void AnimateFadeSlide(VisualElement el, int delayMs)
        {
            if (el == null) return;
            el.style.opacity = 0;
            el.style.translate = new Translate(-24, 0);
            el.schedule.Execute(() =>
            {
                el.style.opacity = 1;
                el.style.translate = new Translate(0, 0);
            }).StartingIn(delayMs);
        }

        private void BuildSettingsRows()
        {
            var scroll = Q<ScrollView>("settings-scroll");
            var content = scroll.contentContainer;
            content.Clear();
            _currentValues.Clear();

            string lastSection = null;

            foreach (var e in MakeSettingEntries())
            {
                _currentValues[e.Id] = e.Value;

                if (e.Section != lastSection)
                {
                    lastSection = e.Section;
                    var lbl = new Label(e.Section.ToUpper());
                    lbl.AddToClassList("section-label");
                    content.Add(lbl);
                }

                content.Add(BuildRow(e));
            }
        }

        private VisualElement BuildRow(SettingEntry e)
        {
            var row = new VisualElement();
            row.AddToClassList("s-row");

            var labelBlock = new VisualElement();
            labelBlock.AddToClassList("s-row-label");

            var name = new Label(e.Label);
            name.AddToClassList("s-row-name");
            labelBlock.Add(name);

            if (!string.IsNullOrEmpty(e.Desc))
            {
                var desc = new Label(e.Desc);
                desc.AddToClassList("s-row-desc");
                labelBlock.Add(desc);
            }

            row.Add(labelBlock);
            row.Add(BuildControl(e));
            return row;
        }

        private VisualElement BuildControl(SettingEntry e) => e.Type switch
        {
            SettingType.Toggle => BuildToggle(e),
            SettingType.Slider => BuildSlider(e),
            SettingType.Select => BuildSelect(e),
            SettingType.KeyBind => BuildKeybind(e),
            _ => new VisualElement()
        };

        private VisualElement BuildToggle(SettingEntry e)
        {
            bool isOn = Convert.ToBoolean(e.Value);

            var track = new VisualElement();
            track.AddToClassList("toggle-track");
            if (isOn) track.AddToClassList("toggle-track--on");

            var thumb = new VisualElement();
            thumb.AddToClassList("toggle-thumb");
            if (isOn) thumb.AddToClassList("toggle-thumb--on");
            track.Add(thumb);

            track.RegisterCallback<ClickEvent>(_ =>
            {
                isOn = !isOn;
                _currentValues[e.Id] = isOn;

                track.EnableInClassList("toggle-track--on", isOn);
                thumb.EnableInClassList("toggle-thumb--on", isOn);
            });

            return track;
        }

        private VisualElement BuildSlider(SettingEntry e)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("s-slider-wrap");

            float initial = Convert.ToSingle(e.Value);
            var slider = new Slider(e.SliderMin, e.SliderMax) { value = initial };
            slider.AddToClassList("s-slider");

            var valLabel = new Label(FmtFloat(initial, e.Decimals));
            valLabel.AddToClassList("s-slider-val");

            slider.RegisterValueChangedCallback(evt =>
            {
                float snapped = Mathf.Round(evt.newValue / e.SliderStep) * e.SliderStep;
                snapped = Mathf.Clamp(snapped, e.SliderMin, e.SliderMax);
                if (Mathf.Abs(snapped - evt.newValue) > 0.0001f)
                    slider.SetValueWithoutNotify(snapped);

                _currentValues[e.Id] = snapped;
                valLabel.text = FmtFloat(snapped, e.Decimals);
            });

            wrap.Add(slider);
            wrap.Add(valLabel);
            return wrap;
        }

        private VisualElement BuildSelect(SettingEntry e)
        {
            var choices = new List<string>();
            var values = new List<string>();
            foreach (var opt in e.Options) { choices.Add(opt.Label); values.Add(opt.Value); }

            int curIdx = values.IndexOf(e.Value.ToString());
            var dropdown = new DropdownField(choices, Mathf.Max(0, curIdx));
            dropdown.AddToClassList("s-select");

            dropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = choices.IndexOf(evt.newValue);
                _currentValues[e.Id] = idx >= 0 ? values[idx] : evt.newValue;
            });

            return dropdown;
        }

        private VisualElement BuildKeybind(SettingEntry e)
        {
            var btn = new Label(e.Value.ToString());
            btn.AddToClassList("keybind-btn");

            btn.RegisterCallback<ClickEvent>(_ =>
            {
                if (_rebindingId != null) return;
                _rebindingId = e.Id;
                _rebindingLabel = btn;
                btn.text = "...";
                btn.AddToClassList("keybind-btn--listening");
            });

            return btn;
        }

        private void FinishRebind(KeyCode kc)
        {
            if (_rebindingLabel == null) return;
            _currentValues[_rebindingId] = kc.ToString();
            _rebindingLabel.text = kc.ToString();
            _rebindingLabel.RemoveFromClassList("keybind-btn--listening");
            _rebindingId = null;
            _rebindingLabel = null;
        }

        private void BindSettingsButtons()
        {
            var back = Q<Button>("btn-back");
            var apply = Q<Button>("btn-apply");
            back.clicked += () => OnMenuAction("back");
            apply.clicked += ApplyAndClose;

            // USS-стили меню в проекте не подключены к PanelSettings, поэтому раскладку
            // задаём инлайн. Без этого кнопки Unity Button растягиваются во всю ширину
            // футера (default flex-grow:1), а панель настроек не имеет высоты и футер
            // с кнопкой APPLY уезжает за экран.
            ApplySettingsInlineLayout(back, apply);
        }

        private void ApplySettingsInlineLayout(Button back, Button apply)
        {
            // Панель — фиксированная высота, вертикальная колонка, без переполнения.
            var panel = Q("settings-panel");
            if (panel != null)
            {
                panel.style.position = Position.Absolute;
                panel.style.left = new Length(50, LengthUnit.Percent);
                panel.style.top = new Length(50, LengthUnit.Percent);
                panel.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
                panel.style.width = 720;
                panel.style.height = 640;
                panel.style.maxHeight = new Length(90, LengthUnit.Percent);
                panel.style.flexDirection = FlexDirection.Column;
                panel.style.backgroundColor = new StyleColor(new Color(8f/255f, 18f/255f, 36f/255f, 0.95f));
                panel.style.borderTopWidth = panel.style.borderBottomWidth =
                    panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
                panel.style.borderTopColor = panel.style.borderBottomColor =
                    panel.style.borderLeftColor = panel.style.borderRightColor =
                    new StyleColor(new Color(0f/255f, 212f/255f, 255f/255f, 0.15f));
                panel.style.overflow = Overflow.Hidden;
            }

            // Шапка и футер не должны сжиматься.
            var header = panel?.Q(className: "s-header");
            if (header != null)
            {
                header.style.flexShrink = 0;
                header.style.paddingTop = 20; header.style.paddingBottom = 12;
                header.style.paddingLeft = 24; header.style.paddingRight = 24;
            }

            var scroll = Q<ScrollView>("settings-scroll");
            if (scroll != null)
            {
                scroll.style.flexGrow = 1;
                scroll.style.flexShrink = 1;
                scroll.style.flexBasis = 0;
                scroll.style.paddingLeft = 24;
                scroll.style.paddingRight = 24;
            }

            var footer = panel?.Q(className: "s-footer");
            if (footer != null)
            {
                footer.style.flexDirection = FlexDirection.Column;
                footer.style.flexShrink = 0;
                footer.style.paddingTop = 12; footer.style.paddingBottom = 20;
                footer.style.paddingLeft = 24; footer.style.paddingRight = 24;
            }

            var buttons = panel?.Q(className: "s-footer-buttons");
            if (buttons != null)
            {
                buttons.style.flexDirection = FlexDirection.Row;
                buttons.style.justifyContent = Justify.FlexEnd;
                buttons.style.alignItems = Align.Center;
            }

            // Сами кнопки — компактные, не растягиваются.
            foreach (var b in new[] { back, apply })
            {
                if (b == null) continue;
                b.style.flexGrow = 0;
                b.style.flexShrink = 0;
                b.style.alignSelf = Align.Center;
                b.style.minWidth = 120;
                b.style.height = 34;
                b.style.marginLeft = 12;
                b.style.paddingLeft = 20; b.style.paddingRight = 20;
                b.style.fontSize = 12;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
                    b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 0;
                b.style.borderTopWidth = b.style.borderBottomWidth =
                    b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            }

            back.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            back.style.color = new StyleColor(new Color(200f/255f, 221f/255f, 240f/255f, 1f));
            back.style.borderTopColor = back.style.borderBottomColor =
                back.style.borderLeftColor = back.style.borderRightColor =
                new StyleColor(new Color(200f/255f, 221f/255f, 240f/255f, 0.35f));

            apply.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            apply.style.color = new StyleColor(new Color(0f, 1f, 170f/255f, 1f));
            apply.style.borderTopColor = apply.style.borderBottomColor =
                apply.style.borderLeftColor = apply.style.borderRightColor =
                new StyleColor(new Color(0f, 1f, 170f/255f, 0.7f));
        }

        public void OnMenuAction(string action)
        {
            switch (action)
            {
                case "new_game":
                    SceneManager.LoadScene(GameSceneName);
                    break;

                case "settings":
                    ShowSettings();
                    break;

                case "quit":
    #if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
    #else
                    Application.Quit();
    #endif
                    break;

                case "back":
                    _snapshot?.Restore(GameSettings);
                    ShowMainMenu();
                    break;

                default:
                    Debug.LogWarning($"[MenuController] Unknown action: {action}");
                    break;
            }
        }

        private void ApplyAndClose()
        {
            ApplyCurrentValues();
            PersistSettings();
            ShowMainMenu();
        }

        private void ApplyCurrentValues()
        {
            var gs = GameSettings;
            var v = _currentValues;

            GetBool(v, "EnableEdgeScrolling", x => gs.EnableEdgeScrolling = x);
            GetBool(v, "EnableCentering", x => gs.EnableCentering = x);
            GetFloat(v, "MoveSpeed", x => gs.MoveSpeed = x);
            GetFloat(v, "ZoomSpeed", x => gs.ZoomSpeed = x);
            GetFloat(v, "MinZoomMult", x => gs.MinZoomMult = x);
            GetFloat(v, "MaxZoomMult", x => gs.MaxZoomMult = x);
            GetFloat(v, "TurnDuration", x => gs.TurnDuration = x);
            GetBool(v, "SimulationInterruptOnActionDone", x => gs.SimulationInterruptOnActionDone = x);
            GetBool(v, "RunInBackground", x => { gs.RunInBackground = x; Application.runInBackground = x; });
            GetInt(v, "DragMouseButton", x => gs.DragMouseButton = x);
            GetKeyCode(v, "PausePlayKey", x => gs.PausePlayKey = x);
            GetKeyCode(v, "ResetCameraKey", x => gs.ResetCameraKey = x);
            GetKeyCode(v, "CenterCameraKey", x => gs.CenterCameraKey = x);
            GetKeyCode(v, "MultiWaypointKey", x => gs.MultiWaypointKey = x);

            bool displayChanged = false;
            if (v.TryGetValue("Resolution", out var resObj) && TryParseResolution(resObj.ToString(), out var w, out var h))
            {
                if (gs.ResolutionWidth != w || gs.ResolutionHeight != h) displayChanged = true;
                gs.ResolutionWidth = w;
                gs.ResolutionHeight = h;
            }
            if (v.TryGetValue("WindowMode", out var wmObj) && Enum.TryParse(wmObj.ToString(), out FullScreenMode wm))
            {
                if (gs.WindowMode != wm) displayChanged = true;
                gs.WindowMode = wm;
            }
            if (displayChanged) ApplyDisplaySettings(gs);
        }

        public static void ApplyDisplaySettings(GameSettingsConfig gs)
        {
            int w = gs.ResolutionWidth  > 0 ? gs.ResolutionWidth  : Screen.currentResolution.width;
            int h = gs.ResolutionHeight > 0 ? gs.ResolutionHeight : Screen.currentResolution.height;
            Screen.SetResolution(w, h, gs.WindowMode);
        }

        private static bool TryParseResolution(string s, out int w, out int h)
        {
            w = h = 0;
            if (string.IsNullOrEmpty(s)) return false;
            int x = s.IndexOf('x');
            if (x <= 0 || x >= s.Length - 1) return false;
            return int.TryParse(s.Substring(0, x), out w) && int.TryParse(s.Substring(x + 1), out h);
        }

        private static readonly (int W, int H)[] SupportedResolutions =
        {
            (800, 600),
            (1024, 768),
            (1280, 720),
            (1366, 768),
            (1600, 900),
            (1920, 1080),
            (2560, 1440),
            (3840, 2160),
        };

        private List<SettingEntry> MakeSettingEntries()
        {
            var gs = GameSettings;

            var resOpts = new SelectOption[SupportedResolutions.Length];
            for (int i = 0; i < SupportedResolutions.Length; i++)
            {
                var r = SupportedResolutions[i];
                resOpts[i] = new SelectOption($"{r.W}x{r.H}", $"{r.W} × {r.H}");
            }
            string curRes = $"{gs.ResolutionWidth}x{gs.ResolutionHeight}";

            var modeOpts = new[]
            {
                new SelectOption(FullScreenMode.Windowed.ToString(),          "Оконный"),
                new SelectOption(FullScreenMode.FullScreenWindow.ToString(),  "Безрамочный"),
                new SelectOption(FullScreenMode.ExclusiveFullScreen.ToString(),"Полноэкранный"),
            };

            return new List<SettingEntry>
            {
                SE_Select ("Display",  "Resolution",                 "Разрешение",         "Размер окна/экрана в пикселях",
                    curRes, resOpts),
                SE_Select ("Display",  "WindowMode",                 "Режим окна",         "Оконный / Безрамочный / Полноэкранный",
                    gs.WindowMode.ToString(), modeOpts),
                SE_Toggle ("Camera",   "EnableEdgeScrolling",        "Edge Scrolling",     "Двигать камеру при наведении на край экрана",  gs.EnableEdgeScrolling),
                SE_Toggle ("Camera",   "EnableCentering",            "Center on Player",   "Клавиша C центрирует камеру на корабле",       gs.EnableCentering),
                SE_Slider ("Camera",   "MoveSpeed",                  "Camera Speed",       null, gs.MoveSpeed,     5,    60,  1f,   0),
                SE_Slider ("Camera",   "ZoomSpeed",                  "Zoom Speed",         null, gs.ZoomSpeed,     2,    30,  1f,   0),
                SE_Slider ("Camera",   "MinZoomMult",                "Min Zoom (×default)","Макс. приближение как доля от дефолтного зума (Home)",
                    gs.MinZoomMult, 0.1f, 1.0f, 0.05f, 2),
                SE_Slider ("Camera",   "MaxZoomMult",                "Max Zoom (×default)","Макс. отдаление как доля от дефолтного зума",
                    gs.MaxZoomMult, 1.0f, 10.0f, 0.5f, 1),
                SE_Slider ("Game",     "TurnDuration",               "Turn Duration",      "Длительность анимации хода (сек)", gs.TurnDuration, 0.5f, 6f, 0.5f, 1),
                SE_Toggle ("Game",     "SimulationInterruptOnActionDone", "Stop Simulation on Route End", "Авто-пауза при завершении маршрута", gs.SimulationInterruptOnActionDone),
                SE_Toggle ("Game",     "RunInBackground",            "Run In Background",  "Не ставить игру на паузу при сворачивании окна", gs.RunInBackground),
                SE_Select ("Controls", "DragMouseButton",            "Drag Button",        "Кнопка мыши для перетаскивания камеры",
                    gs.DragMouseButton.ToString(),
                    new[] { new SelectOption("0","Left"), new SelectOption("1","Right"), new SelectOption("2","Middle") }),
                SE_Key    ("Controls", "PausePlayKey",               "Pause / Play",       null, gs.PausePlayKey.ToString()),
                SE_Key    ("Controls", "ResetCameraKey",             "Reset Camera",       null, gs.ResetCameraKey.ToString()),
                SE_Key    ("Controls", "CenterCameraKey",            "Center Camera",      null, gs.CenterCameraKey.ToString()),
                SE_Key    ("Controls", "MultiWaypointKey",           "Multi-Waypoint",     "Удерживать для цепочки путевых точек", gs.MultiWaypointKey.ToString()),
            };
        }

        private void PersistSettings()
        {
            var gs = GameSettings;
            PlayerPrefs.SetInt("EnableEdgeScrolling", gs.EnableEdgeScrolling ? 1 : 0);
            PlayerPrefs.SetInt("EnableCentering", gs.EnableCentering ? 1 : 0);
            PlayerPrefs.SetFloat("MoveSpeed", gs.MoveSpeed);
            PlayerPrefs.SetFloat("ZoomSpeed", gs.ZoomSpeed);
            PlayerPrefs.SetFloat("MinZoomMult", gs.MinZoomMult);
            PlayerPrefs.SetFloat("MaxZoomMult", gs.MaxZoomMult);
            PlayerPrefs.SetFloat("TurnDuration", gs.TurnDuration);
            PlayerPrefs.SetInt("SimulationInterruptOnActionDone", gs.SimulationInterruptOnActionDone ? 1 : 0);
            PlayerPrefs.SetInt("RunInBackground", gs.RunInBackground ? 1 : 0);
            PlayerPrefs.SetInt("DragMouseButton", gs.DragMouseButton);
            PlayerPrefs.SetString("PausePlayKey", gs.PausePlayKey.ToString());
            PlayerPrefs.SetString("ResetCameraKey", gs.ResetCameraKey.ToString());
            PlayerPrefs.SetString("CenterCameraKey", gs.CenterCameraKey.ToString());
            PlayerPrefs.SetString("MultiWaypointKey", gs.MultiWaypointKey.ToString());
            PlayerPrefs.SetInt("ResolutionWidth",  gs.ResolutionWidth);
            PlayerPrefs.SetInt("ResolutionHeight", gs.ResolutionHeight);
            PlayerPrefs.SetString("WindowMode",    gs.WindowMode.ToString());
            PlayerPrefs.Save();
        }
        public static void LoadSavedSettings(GameSettingsConfig gs)
        {
            // Дисплей загружается независимо от остальных настроек и всегда применяется —
            // иначе смена разрешения в меню не переживёт рестарт.
            LoadDisplaySettings(gs);
            ApplyDisplaySettings(gs);

            if (!PlayerPrefs.HasKey("TurnDuration")) return;

            gs.EnableEdgeScrolling = PlayerPrefs.GetInt("EnableEdgeScrolling", 1) == 1;
            gs.EnableCentering = PlayerPrefs.GetInt("EnableCentering", 1) == 1;
            gs.MoveSpeed = PlayerPrefs.GetFloat("MoveSpeed", gs.MoveSpeed);
            gs.ZoomSpeed = PlayerPrefs.GetFloat("ZoomSpeed", gs.ZoomSpeed);
            gs.MinZoomMult = PlayerPrefs.GetFloat("MinZoomMult", gs.MinZoomMult);
            gs.MaxZoomMult = PlayerPrefs.GetFloat("MaxZoomMult", gs.MaxZoomMult);
            gs.TurnDuration = PlayerPrefs.GetFloat("TurnDuration", gs.TurnDuration);
            gs.SimulationInterruptOnActionDone = PlayerPrefs.GetInt("SimulationInterruptOnActionDone", 1) == 1;
            gs.RunInBackground = PlayerPrefs.GetInt("RunInBackground", 1) == 1;
            Application.runInBackground = gs.RunInBackground;
            gs.DragMouseButton = PlayerPrefs.GetInt("DragMouseButton", gs.DragMouseButton);

            TryParseKey("PausePlayKey", gs.PausePlayKey, kc => gs.PausePlayKey = kc);
            TryParseKey("ResetCameraKey", gs.ResetCameraKey, kc => gs.ResetCameraKey = kc);
            TryParseKey("CenterCameraKey", gs.CenterCameraKey, kc => gs.CenterCameraKey = kc);
            TryParseKey("MultiWaypointKey", gs.MultiWaypointKey, kc => gs.MultiWaypointKey = kc);
        }

        private static void LoadDisplaySettings(GameSettingsConfig gs)
        {
            gs.ResolutionWidth  = PlayerPrefs.GetInt("ResolutionWidth",  gs.ResolutionWidth);
            gs.ResolutionHeight = PlayerPrefs.GetInt("ResolutionHeight", gs.ResolutionHeight);
            var wmStr = PlayerPrefs.GetString("WindowMode", gs.WindowMode.ToString());
            if (Enum.TryParse(wmStr, out FullScreenMode wm)) gs.WindowMode = wm;
        }

        private static void TryParseKey(string pref, KeyCode fallback, Action<KeyCode> set)
        {
            var s = PlayerPrefs.GetString(pref, fallback.ToString());
            if (Enum.TryParse(s, out KeyCode kc)) set(kc);
        }

        private T Q<T>(string name) where T : VisualElement => _root.Q<T>(name);
        private VisualElement Q(string name) => _root.Q(name);

        private static string FmtFloat(float v, int dec) =>
            dec == 0 ? Mathf.RoundToInt(v).ToString() : v.ToString("F" + dec);

        private static void GetBool(Dictionary<string, object> d, string k, Action<bool> s) { if (d.TryGetValue(k, out var v)) s(Convert.ToBoolean(v)); }
        private static void GetFloat(Dictionary<string, object> d, string k, Action<float> s) { if (d.TryGetValue(k, out var v)) s(Convert.ToSingle(v)); }
        private static void GetInt(Dictionary<string, object> d, string k, Action<int> s) { if (d.TryGetValue(k, out var v)) s(Convert.ToInt32(v)); }
        private static void GetKeyCode(Dictionary<string, object> d, string k, Action<KeyCode> s)
        { if (d.TryGetValue(k, out var v) && Enum.TryParse(v.ToString(), out KeyCode kc)) s(kc); }
        private static SettingEntry SE_Toggle(string sec, string id, string lbl, string desc, bool val)
            => new() { Section = sec, Id = id, Label = lbl, Desc = desc, Type = SettingType.Toggle, Value = val };

        private static SettingEntry SE_Slider(string sec, string id, string lbl, string desc,
            float val, float min, float max, float step, int dec)
            => new()
            {
                Section = sec,
                Id = id,
                Label = lbl,
                Desc = desc,
                Type = SettingType.Slider,
                Value = val,
                SliderMin = min,
                SliderMax = max,
                SliderStep = step,
                Decimals = dec
            };

        private static SettingEntry SE_Select(string sec, string id, string lbl, string desc,
            string val, SelectOption[] opts)
            => new() { Section = sec, Id = id, Label = lbl, Desc = desc, Type = SettingType.Select, Value = val, Options = opts };

        private static SettingEntry SE_Key(string sec, string id, string lbl, string desc, string val)
            => new() { Section = sec, Id = id, Label = lbl, Desc = desc, Type = SettingType.KeyBind, Value = val };
    }

    internal class SettingsSnapshot
    {
        private bool EnableEdgeScrolling, EnableCentering, SimulationInterruptOnActionDone, RunInBackground;
        private float MoveSpeed, ZoomSpeed, MinZoomMult, MaxZoomMult, TurnDuration;
        private int DragMouseButton;
        private int ResolutionWidth, ResolutionHeight;
        private FullScreenMode WindowMode;
        private KeyCode PausePlayKey, ResetCameraKey, CenterCameraKey, MultiWaypointKey;

        public static SettingsSnapshot Capture(GameSettingsConfig gs) => new()
        {
            EnableEdgeScrolling = gs.EnableEdgeScrolling,
            EnableCentering = gs.EnableCentering,
            SimulationInterruptOnActionDone = gs.SimulationInterruptOnActionDone,
            RunInBackground = gs.RunInBackground,
            MoveSpeed = gs.MoveSpeed,
            ZoomSpeed = gs.ZoomSpeed,
            MinZoomMult = gs.MinZoomMult,
            MaxZoomMult = gs.MaxZoomMult,
            TurnDuration = gs.TurnDuration,
            DragMouseButton = gs.DragMouseButton,
            ResolutionWidth = gs.ResolutionWidth,
            ResolutionHeight = gs.ResolutionHeight,
            WindowMode = gs.WindowMode,
            PausePlayKey = gs.PausePlayKey,
            ResetCameraKey = gs.ResetCameraKey,
            CenterCameraKey = gs.CenterCameraKey,
            MultiWaypointKey = gs.MultiWaypointKey,
        };

        public void Restore(GameSettingsConfig gs)
        {
            gs.EnableEdgeScrolling = EnableEdgeScrolling;
            gs.EnableCentering = EnableCentering;
            gs.SimulationInterruptOnActionDone = SimulationInterruptOnActionDone;
            gs.RunInBackground = RunInBackground;
            Application.runInBackground = RunInBackground;
            gs.MoveSpeed = MoveSpeed;
            gs.ZoomSpeed = ZoomSpeed;
            gs.MinZoomMult = MinZoomMult;
            gs.MaxZoomMult = MaxZoomMult;
            gs.TurnDuration = TurnDuration;
            gs.DragMouseButton = DragMouseButton;
            bool displayChanged = gs.ResolutionWidth != ResolutionWidth
                || gs.ResolutionHeight != ResolutionHeight
                || gs.WindowMode != WindowMode;
            gs.ResolutionWidth = ResolutionWidth;
            gs.ResolutionHeight = ResolutionHeight;
            gs.WindowMode = WindowMode;
            if (displayChanged) MenuController.ApplyDisplaySettings(gs);
            gs.PausePlayKey = PausePlayKey;
            gs.ResetCameraKey = ResetCameraKey;
            gs.CenterCameraKey = CenterCameraKey;
            gs.MultiWaypointKey = MultiWaypointKey;
        }
    }

    internal enum SettingType { Toggle, Slider, Select, KeyBind }

    internal class SettingEntry
    {
        public string Section;
        public string Id;
        public string Label;
        public string Desc;
        public SettingType Type;
        public object Value;
        public float SliderMin, SliderMax, SliderStep;
        public int Decimals;
        public SelectOption[] Options;
    }

    internal class SelectOption
    {
        public string Value;
        public string Label;
        public SelectOption(string v, string l) { Value = v; Label = l; }
    }

    public class StarfieldElement : VisualElement
    {
        private struct Star { public float X, Y, R, Phase, Speed; }

        private const int Count = 240;
        private readonly Star[] _stars = new Star[Count];
        private float _time;

        public StarfieldElement()
        {
            style.position = Position.Absolute;
            pickingMode = PickingMode.Ignore;

            var rng = new System.Random(42);
            for (int i = 0; i < Count; i++)
                _stars[i] = new Star
                {
                    X = (float)rng.NextDouble(),
                    Y = (float)rng.NextDouble(),
                    R = (float)(rng.NextDouble() * 1.3 + 0.2),
                    Phase = (float)(rng.NextDouble() * Math.PI * 2),
                    Speed = (float)(rng.NextDouble() * 0.008 + 0.002),
                };

            generateVisualContent += Draw;
            schedule.Execute(() => { _time += 0.05f; MarkDirtyRepaint(); }).Every(50);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            float w = resolvedStyle.width;
            float h = resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            foreach (var s in _stars)
            {
                float a = 0.5f + 0.5f * Mathf.Sin(_time * s.Speed * 10f + s.Phase);
                p.fillColor = new Color(0.7f, 0.87f, 1f, a * 0.85f);
                p.BeginPath();
                p.Arc(new Vector2(s.X * w, s.Y * h), s.R, 0f, 360f);
                p.Fill();
            }
        }
    }
}
