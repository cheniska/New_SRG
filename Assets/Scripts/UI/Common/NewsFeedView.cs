using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SRG.Galaxy.Politics;

namespace SRG.UI.Common
{
    /// <summary>
    /// Переиспользуемая лента новостей Галактического Совета: панель чипов-фильтров
    /// по категориям + вертикальный скролл с карточками. Используется оверлеем
    /// инфоцентра в космосе и экраном «Инфо-центр» формы планеты — одна реализация,
    /// чтобы оба места выглядели и вели себя одинаково.
    ///
    /// Раскладка карточки:
    ///   head — «Дата [Категория]» (одна строка, 20px)
    ///   body — текст новости, wrap, максимум ~3 строки, truncate
    ///   → карточка = 100px.
    /// </summary>
    public class NewsFeedView
    {
        private const float ROW_H      = 100f;
        private const float ROW_HEAD_H = 20f;
        private const float ROW_GAP    = 4f;
        private const float FILTER_H   = 30f;

        private readonly Font _font;
        private Transform _filterBar;
        private Transform _content;
        // null = показывать все категории
        private string _activeFilter;

        private NewsFeedView(Font font) { _font = font; }

        /// <summary>Строит ленту внутри container: чипы-фильтры сверху, скролл под ними.
        /// Позиционирование самого container — забота вызывающего кода.</summary>
        public static NewsFeedView Create(RectTransform container, Font font)
        {
            var view = new NewsFeedView(font);
            view.BuildFilterBar(container);
            view.BuildScrollArea(container);
            return view;
        }

        public void Refresh()
        {
            RebuildFilterChips();
            RebuildNewsCards();
        }

        // ── Построение ────────────────────────────────────────────

        private void BuildFilterBar(RectTransform container)
        {
            var bar = new GameObject("FilterBar");
            bar.transform.SetParent(container, false);
            var rt = bar.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, FILTER_H);
            rt.anchoredPosition = Vector2.zero;
            bar.AddComponent<Image>().color = new Color(0.06f, 0.09f, 0.14f, 1f);

            // Внутренний скролл по горизонтали — если чипов больше, чем помещается.
            var scrollGo = new GameObject("FilterScroll");
            scrollGo.transform.SetParent(bar.transform, false);
            var sRt = scrollGo.AddComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(1f, 1f);
            sRt.offsetMin = new Vector2(4f, 2f); sRt.offsetMax = new Vector2(-4f, -2f);
            scrollGo.AddComponent<RectMask2D>();

            var chipsGo = new GameObject("Chips");
            chipsGo.transform.SetParent(scrollGo.transform, false);
            var chipsRt = chipsGo.AddComponent<RectTransform>();
            chipsRt.anchorMin = new Vector2(0f, 0f); chipsRt.anchorMax = new Vector2(0f, 1f);
            chipsRt.pivot     = new Vector2(0f, 0.5f);
            chipsRt.sizeDelta = Vector2.zero;
            chipsRt.anchoredPosition = Vector2.zero;
            var hlg = chipsGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childControlWidth = false; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(2, 2, 2, 2);
            chipsGo.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = true; sr.vertical = false; sr.content = chipsRt;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;

            _filterBar = chipsRt;
        }

        private void BuildScrollArea(RectTransform container)
        {
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(container, false);
            var sRt = scrollGo.AddComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(1f, 1f);
            sRt.offsetMin = new Vector2(6f, 6f);
            sRt.offsetMax = new Vector2(-6f, -FILTER_H);
            scrollGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f); contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f); contentRt.sizeDelta = Vector2.zero;
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = ROW_GAP;
            // childControlHeight=true — иначе LayoutElement.preferredHeight карточек игнорируется
            // и высота берётся из дефолтного sizeDelta.
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(6, 6, 4, 4);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.content = contentRt; sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = 30f; sr.movementType = ScrollRect.MovementType.Clamped;
            _content = contentRt;
        }

        // ── Наполнение ────────────────────────────────────────────

        private void RebuildFilterChips()
        {
            if (_filterBar == null) return;
            UIBuilder.ClearChildren(_filterBar);

            var entries = GalaxyNewsService.Entries;
            // Уникальные категории в порядке первого появления (стабильно).
            var seen = new HashSet<string>();
            var cats = new List<string>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var c = entries[i].Category ?? "";
                    if (string.IsNullOrEmpty(c)) continue;
                    if (seen.Add(c)) cats.Add(c);
                }
            }
            cats.Sort(System.StringComparer.CurrentCulture);

            AddFilterChip("Все", null);
            foreach (var c in cats) AddFilterChip(c, c);
        }

        private void AddFilterChip(string label, string filterKey)
        {
            var go = new GameObject($"Chip_{label}");
            go.transform.SetParent(_filterBar, false);
            var rt = go.AddComponent<RectTransform>();
            // Ширина по контенту — LayoutElement.preferredWidth считаем от длины текста.
            float w = Mathf.Max(40f, 12f + label.Length * 7f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = FILTER_H - 6f;
            rt.sizeDelta = new Vector2(w, FILTER_H - 6f);

            bool active = _activeFilter == filterKey;
            var img = go.AddComponent<Image>();
            img.color = active ? UIColorPalette.ButtonSel : UIColorPalette.Button;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                _activeFilter = filterKey;
                Refresh();
            });

            var txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            var tRt = txtGo.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one; tRt.sizeDelta = Vector2.zero;
            var t = txtGo.AddComponent<Text>();
            t.font = _font; t.text = label; t.fontSize = 12;
            t.color = active ? Color.white : UIColorPalette.Text;
            t.alignment = TextAnchor.MiddleCenter;
        }

        private void RebuildNewsCards()
        {
            if (_content == null) return;
            UIBuilder.ClearChildren(_content);

            var entries = GalaxyNewsService.Entries;
            if (entries == null || entries.Count == 0)
            {
                AddEmptyRow("Новостей нет.");
                return;
            }

            int shown = 0;
            // Свежие сверху.
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (_activeFilter != null && e.Category != _activeFilter) continue;
                BuildNewsCard(e, i);
                shown++;
            }
            if (shown == 0) AddEmptyRow($"В категории «{_activeFilter}» новостей нет.");
        }

        private void AddEmptyRow(string text)
        {
            var empty = new GameObject("Empty");
            empty.transform.SetParent(_content, false);
            empty.AddComponent<RectTransform>();
            empty.AddComponent<LayoutElement>().preferredHeight = 60f;
            var txt = empty.AddComponent<Text>();
            txt.font = _font; txt.fontSize = 13; txt.color = UIColorPalette.Text;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = text;
        }

        private void BuildNewsCard(GalaxyNewsEntry e, int idx)
        {
            var row = new GameObject($"NewsRow_{idx}");
            row.transform.SetParent(_content, false);
            row.AddComponent<RectTransform>();
            row.AddComponent<LayoutElement>().preferredHeight = ROW_H;
            row.AddComponent<Image>().color = new Color(0.10f, 0.13f, 0.18f, 0.55f);

            // ⚠ childControlHeight = true, чтобы LayoutElement.preferredHeight у детей
            // реально устанавливал их RectTransform-высоту — без этого текст рисуется
            // «поверх» соседа и карточки наезжают.
            var vlg = row.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 4, 4);
            vlg.spacing = 2f;
            vlg.childControlWidth  = true; vlg.childControlHeight  = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            // Строка 1: дата + категория (заголовок). Category в entry — стабильный ключ
            // (occupation/liberation/…); отображаемое имя берём из TextsConfig.News.Categories.
            string catDisplay = SRG.Galaxy.Politics.NewsTexts.Category(e.Category);
            AddRowText(row.transform, "Head",
                $"<b>{e.Date}</b>   <color=#8fbfe6>[{catDisplay}]</color>",
                12, FontStyle.Normal, UIColorPalette.Accent, ROW_HEAD_H, wrap: false);

            // Строка 2..N: текст новости на следующей строке — VerticalLayoutGroup
            // размещает body под head автоматически. Body занимает всю оставшуюся высоту.
            float bodyH = ROW_H - ROW_HEAD_H - 4f /* padding-top */ - 4f /* padding-bottom */ - 2f /* spacing */;
            AddRowText(row.transform, "Body", e.Text ?? "", 12, FontStyle.Normal,
                UIColorPalette.Text, bodyH, wrap: true);
        }

        private void AddRowText(Transform parent, string name, string content, int size,
            FontStyle style, Color color, float preferredHeight, bool wrap)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<Text>();
            t.font = _font; t.text = content; t.fontSize = size; t.fontStyle = style;
            t.color = color; t.alignment = TextAnchor.UpperLeft; t.supportRichText = true;
            if (wrap)
            {
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
            else
            {
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            le.minHeight = preferredHeight;
        }
    }
}
