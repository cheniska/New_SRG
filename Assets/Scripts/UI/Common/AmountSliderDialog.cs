using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SRG.UI.Common
{
    /// <summary>
    /// Модальная мини-форма выбора количества — ОБЩИЙ элемент для любого действия со
    /// стакабельным предметом / любой операции «выбери число N из диапазона»: выбросить
    /// часть стека (ShipFormView), купить/продать товар (PlanetUIController), в будущем —
    /// передача груза, дозаправка и т.п. Ползунок min..max, счётчик, кнопки
    /// подтверждения и отмены. Строится кодом, без ассетов; цвета — из <see cref="UIColorPalette"/>.
    ///
    /// Использование: один раз при построении экрана
    /// <code>_dialog = AmountSliderDialog.Create(fullscreenRoot);</code>
    /// затем в момент действия
    /// <code>_dialog.Show("Продать: Руда", max, n => Sell(n), confirmLabel: "Продать");</code>
    ///
    /// Клик по фону мимо панели и Esc — отмена. Собственный GameObject служит модальным
    /// backdrop'ом на весь родительский Rect и перехватывает клики под собой; при показе
    /// диалог сам поднимается наверх (SetAsLastSibling). Если экран-владелец прячется
    /// целиком, вызовите <see cref="CloseSilent"/>, чтобы сбросить устаревшее состояние.
    /// </summary>
    public class AmountSliderDialog : MonoBehaviour
    {
        private Slider _slider;
        private Text _titleText;
        private Text _amountText;
        private Text _confirmText;
        private int _max;
        private System.Action<int> _onConfirm;
        private System.Action _onCancel;
        private System.Func<int, string> _formatter;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Текущее значение ползунка (актуально, пока диалог открыт).</summary>
        public int CurrentAmount => _slider != null ? (int)_slider.value : 0;

        /// <summary>Создаёт диалог (скрытым) дочерним объектом <paramref name="parent"/>.
        /// Родитель должен покрывать область, которую нужно модально заблокировать
        /// (обычно fullscreen-корень экрана).</summary>
        public static AmountSliderDialog Create(RectTransform parent)
        {
            var go = new GameObject("AmountSliderDialog");
            go.transform.SetParent(parent, false);
            var dlg = go.AddComponent<AmountSliderDialog>();
            dlg.BuildUI();
            go.SetActive(false);
            return dlg;
        }

        /// <summary>Открывает диалог. Ползунок <paramref name="min"/>..<paramref name="max"/>
        /// (по умолчанию 1..max), стартовое значение — <paramref name="initial"/>
        /// (по умолчанию max). <paramref name="amountFormatter"/> — необязательный формат
        /// счётчика (например, с ценой); по умолчанию «N / max».</summary>
        public void Show(string title, int max, System.Action<int> onConfirm,
                         System.Action onCancel = null, string confirmLabel = "OK",
                         System.Func<int, string> amountFormatter = null,
                         int min = 1, int initial = -1)
        {
            min = Mathf.Max(0, min);
            _max = Mathf.Max(min, max);
            _onConfirm = onConfirm;
            _onCancel = onCancel;
            _formatter = amountFormatter;
            _titleText.text = title;
            _confirmText.text = confirmLabel;
            _slider.minValue = min;
            _slider.maxValue = _max;
            _slider.value = initial < 0 ? _max : Mathf.Clamp(initial, min, _max);
            UpdateAmountText();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>Закрыть с вызовом onCancel (кнопка «Отмена», Esc, клик мимо панели).</summary>
        public void Cancel()
        {
            if (!IsOpen) return;
            var cb = _onCancel;
            HideInternal();
            cb?.Invoke();
        }

        /// <summary>Закрыть без каких-либо колбэков — например, когда экран-владелец
        /// прячется целиком и состояние диалога больше не актуально.</summary>
        public void CloseSilent() => HideInternal();

        private void Confirm()
        {
            if (!IsOpen) return;
            int amount = (int)_slider.value;
            var cb = _onConfirm;
            HideInternal();
            cb?.Invoke(amount);
        }

        private void HideInternal()
        {
            _onConfirm = null;
            _onCancel = null;
            _formatter = null;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                Cancel();
        }

        private void UpdateAmountText()
        {
            int v = (int)_slider.value;
            _amountText.text = _formatter != null ? _formatter(v) : $"{v} / {_max}";
        }

        // ── Построение ────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Модальный фон на весь родительский Rect: перехватывает клики, клик мимо панели = отмена.
            var rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var backdrop = gameObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.55f);
            backdrop.raycastTarget = true;
            gameObject.AddComponent<BackdropClickHandler>().Owner = this;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(transform, false);
            var prt = panelGo.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(440f, 190f);
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(UIColorPalette.Bg.r, UIColorPalette.Bg.g, UIColorPalette.Bg.b, 0.97f);
            panelImg.raycastTarget = true;
            // Гасим всплытие клика с панели до backdrop-отмены.
            panelGo.AddComponent<ClickBlocker>();

            _titleText = BuildText(panelGo.transform, font, new Vector2(0f, 62f),
                                   new Vector2(410f, 26f), 16, FontStyle.Bold);
            _amountText = BuildText(panelGo.transform, font, new Vector2(0f, 32f),
                                    new Vector2(410f, 22f), 15, FontStyle.Normal);

            _slider = BuildSlider(panelGo.transform, new Vector2(0f, 0f), new Vector2(380f, 26f));
            _slider.onValueChanged.AddListener(_ => UpdateAmountText());

            _confirmText = BuildButton(panelGo.transform, font, "OK", new Vector2(-85f, -58f),
                                       UIColorPalette.ButtonAlt, Confirm);
            BuildButton(panelGo.transform, font, "Отмена", new Vector2(85f, -58f),
                        UIColorPalette.Button, Cancel);
        }

        private static Text BuildText(Transform parent, Font font, Vector2 pos, Vector2 size,
                                      int fontSize, FontStyle style)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = UIColorPalette.Text;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private static Slider BuildSlider(Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("AmountSlider");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 8f);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.22f, 0.28f, 0.36f, 1f);
            bgImg.raycastTarget = true;

            var fillAreaGo = new GameObject("FillArea");
            fillAreaGo.transform.SetParent(go.transform, false);
            var faRt = fillAreaGo.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.5f);
            faRt.anchorMax = new Vector2(1f, 0.5f);
            faRt.sizeDelta = new Vector2(-20f, 8f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.sizeDelta = new Vector2(10f, 0f);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = new Color(0.60f, 0.80f, 1.00f, 0.9f);
            fillImg.raycastTarget = false;

            var handleAreaGo = new GameObject("HandleArea");
            handleAreaGo.transform.SetParent(go.transform, false);
            var haRt = handleAreaGo.AddComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero;
            haRt.anchorMax = Vector2.one;
            haRt.offsetMin = new Vector2(10f, 0f);
            haRt.offsetMax = new Vector2(-10f, 0f);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            var hRt = handleGo.AddComponent<RectTransform>();
            hRt.sizeDelta = new Vector2(20f, 26f);
            var hImg = handleGo.AddComponent<Image>();
            hImg.color = new Color(0.85f, 0.90f, 0.98f, 1f);
            hImg.raycastTarget = true;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = hRt;
            slider.targetGraphic = hImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;
            return slider;
        }

        private static Text BuildButton(Transform parent, Font font, string label, Vector2 pos,
                                        Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(140f, 34f);
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var tgo = new GameObject("Label");
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var txt = tgo.AddComponent<Text>();
            txt.font = font;
            txt.text = label;
            txt.fontSize = 15;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            return txt;
        }

        // ── Обработчики кликов ────────────────────────────────────────────────────

        /// <summary>Клик по модальному фону = отмена.</summary>
        private class BackdropClickHandler : MonoBehaviour, IPointerClickHandler
        {
            public AmountSliderDialog Owner;
            public void OnPointerClick(PointerEventData _) => Owner?.Cancel();
        }

        /// <summary>Пустой обработчик клика: не даёт клику по панели диалога всплыть
        /// до backdrop-отмены (ExecuteEvents ищет ближайший IPointerClickHandler вверх по иерархии).</summary>
        private class ClickBlocker : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData _) { }
        }
    }
}
