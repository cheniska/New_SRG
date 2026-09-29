using UnityEngine;
using UnityEngine.UI;

namespace SRG.UI.HUD
{
    // Простое всплывающее сообщение по центру экрана.
    // Используется для коротких уведомлений ("Объект вне зоны радара",
    // "Форсаж активирован" и т.п.). Канвас и текст создаются программно
    // при первом вызове Show(), отдельная сцена/префаб не требуются.
    public class HudMessageController : MonoBehaviour
    {
        private const float FadeIn   = 0.15f;
        private const float FadeOut  = 0.35f;
        private const float DefaultDuration = 2.0f;

        private static HudMessageController _instance;

        private Text   _label;
        private float  _showT0;
        private float  _duration;
        private bool   _visible;

        public static void Show(string message, float duration = DefaultDuration)
        {
            if (string.IsNullOrEmpty(message)) return;
            EnsureInstance();
            _instance.Display(message, duration);
        }

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("HudMessageCanvas");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HudMessageController>();
            _instance.Build(go);
        }

        private void Build(GameObject root)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            root.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;

            var lgo = new GameObject("HudMessage");
            lgo.transform.SetParent(root.transform, false);

            var rt = lgo.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 1f);
            rt.anchorMax        = new Vector2(0.5f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -90f);
            rt.sizeDelta        = new Vector2(720f, 38f);

            _label = lgo.AddComponent<Text>();
            _label.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize  = 22;
            _label.alignment = TextAnchor.UpperCenter;
            _label.color     = new Color(1f, 0.92f, 0.55f, 0f);

            var shadow = lgo.AddComponent<Shadow>();
            shadow.effectColor    = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        private void Display(string message, float duration)
        {
            _label.text = message;
            _showT0     = Time.unscaledTime;
            _duration   = Mathf.Max(FadeIn + FadeOut + 0.1f, duration);
            _visible    = true;
        }

        private void Update()
        {
            if (!_visible || _label == null) return;
            float t   = Time.unscaledTime - _showT0;
            float end = _duration;

            float a;
            if (t < FadeIn)         a = t / FadeIn;
            else if (t < end - FadeOut) a = 1f;
            else if (t < end)       a = 1f - (t - (end - FadeOut)) / FadeOut;
            else { a = 0f; _visible = false; }

            var c = _label.color; c.a = a; _label.color = c;
        }
    }
}
