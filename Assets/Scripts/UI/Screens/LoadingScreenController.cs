using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SRG.UI.Screens
{
    /// <summary>
    /// Полноэкранный чёрный оверлей для бесшовного перехода между системами при гиперпрыжке.
    /// Создаётся лениво по первому вызову Show(); один экземпляр на всю игру (DontDestroyOnLoad).
    /// В сцене ничего настраивать не нужно — всё спавнится из кода.
    /// Минимальное время показа — MinDisplayTime секунд (иначе экран мелькает на один кадр).
    /// </summary>
    public class LoadingScreenController : MonoBehaviour
    {
        private const float MinDisplayTime = 0.6f;

        private static LoadingScreenController _instance;
        private Canvas _canvas;
        private Text _label;
        private Text _sub;
        private float _shownAt;
        private bool _hideScheduled;

        public static bool IsVisible => _instance != null && _instance._canvas != null
                                        && _instance._canvas.gameObject.activeSelf;

        public static void Show(string text = "ГИПЕРПЕРЕХОД")
        {
            EnsureInstance();
            if (_instance._label != null) _instance._label.text = text;
            _instance._shownAt = Time.unscaledTime;
            _instance._hideScheduled = false;
            _instance._canvas.gameObject.SetActive(true);
        }

        /// <summary>Скрывает оверлей. Если с момента Show() прошло меньше MinDisplayTime — отложит скрытие.</summary>
        public static void Hide()
        {
            if (_instance == null || _instance._canvas == null) return;
            if (!_instance._canvas.gameObject.activeSelf) return;

            float elapsed = Time.unscaledTime - _instance._shownAt;
            if (elapsed >= MinDisplayTime)
            {
                _instance._canvas.gameObject.SetActive(false);
                _instance._hideScheduled = false;
                return;
            }
            if (_instance._hideScheduled) return;
            _instance._hideScheduled = true;
            _instance.StartCoroutine(_instance.HideAfter(MinDisplayTime - elapsed));
        }

        private IEnumerator HideAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            _hideScheduled = false;
        }

        private static void EnsureInstance()
        {
            if (_instance != null && _instance._canvas != null) return;
            var go = new GameObject("LoadingScreenController");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LoadingScreenController>();
            _instance.Build();
        }

        private void Build()
        {
            var cgo = new GameObject("LoadingCanvas");
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;
            cgo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cgo.AddComponent<GraphicRaycaster>();

            var bgGo = new GameObject("BG", typeof(RectTransform));
            bgGo.transform.SetParent(cgo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var img = bgGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0.02f, 1f);
            img.raycastTarget = true;              // блокируем клики под оверлеем

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(cgo.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.5f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(900f, 90f);
            lrt.anchoredPosition = new Vector2(0f, 20f);
            _label = labelGo.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 56;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = new Color(0.78f, 0.88f, 1f, 1f);
            _label.text = "ГИПЕРПЕРЕХОД";

            var subGo = new GameObject("Sub", typeof(RectTransform));
            subGo.transform.SetParent(cgo.transform, false);
            var srt = subGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(600f, 30f);
            srt.anchoredPosition = new Vector2(0f, -40f);
            _sub = subGo.AddComponent<Text>();
            _sub.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _sub.fontSize = 18;
            _sub.alignment = TextAnchor.MiddleCenter;
            _sub.color = new Color(0.50f, 0.62f, 0.85f, 0.9f);
            _sub.text = "загрузка системы…";

            _canvas.gameObject.SetActive(false);
        }
    }
}
