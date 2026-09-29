using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SRG.Config;
using SRG.Core;
using SRG.Galaxy;
using SRG.Presentation.Common;
using SRG.Ships;
using SRG.UI.Screens;

namespace SRG.UI.HUD
{
    /// <summary>
    /// Панель пилота (слева от инвентаря на форме корабля). Рендерит фон FormPilot.png,
    /// портрет (placeholder), имя пилота, сетку 2×3 из 6 скилов с кликабельными «+»
    /// и счётчик свободных очков опыта. Зона «инфошки» (программы/болезни/документы)
    /// зарезервирована, заполнится позже.
    ///
    /// Все координаты зон (портрет/имя/скилы/инфошки) заданы напрямую в пикселях
    /// исходного PNG 404×1080 от его левого верхнего угла. PngToLocal() переводит
    /// (x,y) в локальные UI-координаты RectTransform (origin = центр PNG).
    /// </summary>
    public class PilotPanelController : MonoBehaviour
    {
        private const float RefWidth  = 1920f;
        private const float RefHeight = 1080f;

        private const float PngWidth  = 404f;
        private const float PngHeight = 1080f;

        private static string ResourceRoot => GalaxyConstants.PATH_SHIP_FORM_RES_ROOT;
        private static Sprite _formSprite;

        private RectTransform _root;
        private ShipData _ship;
        private Text _nameText;
        private Text _freePointsText;
        private SkillCell[] _cells = new SkillCell[SkillTypeExtensions.SkillCount];

        /// <summary>Раскладка 6 скилов: 2 ряда × 3 колонки.
        /// Ряд 0: Accuracy / Mobility / Technical (боевая тройка).
        /// Ряд 1: Trader / Charm / Leadership (социальная тройка).</summary>
        private static readonly (int col, int row, SkillType skill)[] SkillLayout =
        {
            (0, 0, SkillType.Accuracy),
            (1, 0, SkillType.Mobility),
            (2, 0, SkillType.Technical),
            (0, 1, SkillType.Trader),
            (1, 1, SkillType.Charm),
            (2, 1, SkillType.Leadership),
        };

        private static SkillsConfig SkillsCfg =>
            GalaxyManager.Instance?.Context?.Config?.Skills;

        // ── API ───────────────────────────────────────────────────────────────────

        public void Build(RectTransform parent, Vector2 absPos)
        {
            EnsureSpritesLoaded();

            var go = new GameObject("PilotPanel");
            go.transform.SetParent(parent, false);
            _root = go.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot     = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(PngWidth, PngHeight);
            _root.anchoredPosition = AbsToLocal(absPos);

            BuildBackground();
            BuildPortraitPlaceholder();
            BuildNameText();
            BuildSkillGrid();
            BuildFreePointsText();
        }

        public void SetShip(ShipData ship)
        {
            // Отписка от старого Skills, подписка на новый — чтобы мутации (addexp, upskill,
            // setskill, начисление exp из бой/торговли) триггерили перерисовку сразу.
            if (_ship?.Skills != null) _ship.Skills.OnChanged -= Refresh;
            _ship = ship;
            if (_ship?.Skills != null) _ship.Skills.OnChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_ship?.Skills != null) _ship.Skills.OnChanged -= Refresh;
        }

        public void Refresh()
        {
            if (_root == null || _ship == null) return;
            var cfg = SkillsCfg;

            _nameText.text = _ship.Name ?? "";
            int free = _ship.Skills?.FreePoints ?? 0;
            _freePointsText.text = $"Свободно: {free}";

            for (int i = 0; i < _cells.Length; i++)
            {
                var (_, _, sk) = SkillLayout[i];
                int b    = _ship.Skills?.GetBase(sk) ?? 0;
                int e    = _ship.Skills?.GetEffective(sk, cfg) ?? b;
                int cost = _ship.Skills?.GetUpgradeCost(sk, cfg) ?? int.MaxValue;
                _cells[i].UpdateDisplay(b, e, cost, free);
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────────

        private void BuildBackground()
        {
            var go = new GameObject("Background");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = _formSprite;
            img.preserveAspect = false;
            img.raycastTarget = false;
        }

        private void BuildPortraitPlaceholder()
        {
            // Зона портрета (PNG-пиксели): (72,180) → (202,324). 130×144.
            var (center, size) = PngRectToLocal(72, 180, 202, 324);
            var go = new GameObject("Portrait");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = center;
            var img = go.AddComponent<Image>();
            // Placeholder: тёмный полупрозрачный квадрат. Будущая Face-система зальёт сюда портрет.
            img.color = new Color(0.10f, 0.14f, 0.20f, 0.40f);
            img.raycastTarget = false;
        }

        private void BuildNameText()
        {
            // Зона текста о пилоте (PNG-пиксели): (210,190) → (350,320). 140×130.
            var (center, size) = PngRectToLocal(210, 190, 350, 320);
            var go = new GameObject("NameText");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = center;
            _nameText = go.AddComponent<Text>();
            _nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _nameText.fontSize = 16;
            _nameText.fontStyle = FontStyle.Bold;
            _nameText.color = new Color(0.95f, 0.95f, 0.98f);
            _nameText.alignment = TextAnchor.UpperLeft;
            _nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _nameText.verticalOverflow   = VerticalWrapMode.Overflow;
            _nameText.raycastTarget = false;
            _nameText.text = "";
        }

        private void BuildSkillGrid()
        {
            // Зона скилов (PNG-пиксели): (50,330) → (350,530). 300×200. 3 кол × 2 ряда = 100×100 на клетку.
            const float skillsLeft = 50f;
            const float skillsTop  = 330f;
            const float cellW = 100f;
            const float cellH = 100f;

            for (int i = 0; i < SkillLayout.Length; i++)
            {
                var (col, row, sk) = SkillLayout[i];
                float pCx = skillsLeft + (col + 0.5f) * cellW;
                float pCy = skillsTop  + (row + 0.5f) * cellH;
                Vector2 cellCenter = PngToLocal(pCx, pCy);
                _cells[i] = BuildSkillCell(sk, cellCenter, cellW, cellH);
            }
        }

        private SkillCell BuildSkillCell(SkillType sk, Vector2 centerLocal, float w, float h)
        {
            var go = new GameObject($"Skill_{sk}");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = centerLocal;

            // Значение — большой digit, центр клетки, чуть выше середины (над "+" в PNG).
            var valGo = new GameObject("Value");
            valGo.transform.SetParent(go.transform, false);
            var vrt = valGo.AddComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0.5f, 0.5f);
            vrt.anchorMax = new Vector2(0.5f, 0.5f);
            vrt.pivot     = new Vector2(0.5f, 0.5f);
            vrt.sizeDelta = new Vector2(w, 24f);
            vrt.anchoredPosition = new Vector2(0f, 18f);
            var valText = valGo.AddComponent<Text>();
            valText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            valText.fontSize = 18;
            valText.fontStyle = FontStyle.Bold;
            valText.alignment = TextAnchor.MiddleCenter;
            valText.color = new Color(0.98f, 0.98f, 0.98f);
            valText.raycastTarget = false;
            valText.text = "0";

            // Кнопка "+" — кликабельный прозрачный прямоугольник над "+" в PNG (нижняя часть клетки).
            var btnGo = new GameObject("PlusButton");
            btnGo.transform.SetParent(go.transform, false);
            var brt = btnGo.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot     = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(38f, 22f);
            brt.anchoredPosition = new Vector2(0f, -36f);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0f, 0f, 0f, 0f);
            btnImg.raycastTarget = true;

            var clickHandler = btnGo.AddComponent<PlusClickHandler>();
            clickHandler.Owner = this;
            clickHandler.Skill = sk;

            return new SkillCell { ValueText = valText, PlusButton = btnImg };
        }

        private void BuildFreePointsText()
        {
            var go = new GameObject("FreePoints");
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(280f, 28f);
            // Прямо под зоной скилов (нижняя кромка skills = y=530, чуть ниже = 552).
            rt.anchoredPosition = PngToLocal(200f, 552f);
            _freePointsText = go.AddComponent<Text>();
            _freePointsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _freePointsText.fontSize = 14;
            _freePointsText.fontStyle = FontStyle.Bold;
            _freePointsText.color = new Color(0.85f, 0.95f, 0.85f);
            _freePointsText.alignment = TextAnchor.MiddleCenter;
            _freePointsText.raycastTarget = false;
            _freePointsText.text = "";
        }

        // ── Click ─────────────────────────────────────────────────────────────────

        internal void OnPlusClicked(SkillType skill)
        {
            if (_ship?.Skills == null) return;
            var cfg = SkillsCfg;
            int costBefore = _ship.Skills.GetUpgradeCost(skill, cfg);

            if (_ship.Skills.TryUpgrade(skill, cfg))
                GameConsoleController.AddEntry(
                    $"+{skill.DisplayNameRu()}: {_ship.Skills.GetBase(skill)} (потрачено {costBefore})");
            else
            {
                string reason = costBefore == int.MaxValue
                    ? "уже потолок прокачки"
                    : $"нужно {costBefore}, есть {_ship.Skills.FreePoints}";
                GameConsoleController.AddEntry($"Не могу прокачать {skill.DisplayNameRu()}: {reason}");
            }

            Refresh();
        }

        // ── Coord helpers ─────────────────────────────────────────────────────────

        private static Vector2 AbsToLocal(Vector2 abs) =>
            new Vector2(abs.x - RefWidth * 0.5f, RefHeight * 0.5f - abs.y);

        /// <summary>Перевод PNG-пиксельных координат (404×1080, origin top-left)
        /// в локальные UI-координаты внутри _root (origin = центр PNG).</summary>
        private static Vector2 PngToLocal(float pngX, float pngY) =>
            new Vector2(pngX - PngWidth * 0.5f, PngHeight * 0.5f - pngY);

        private static (Vector2 center, Vector2 size) PngRectToLocal(float x1, float y1, float x2, float y2)
        {
            Vector2 center = PngToLocal((x1 + x2) * 0.5f, (y1 + y2) * 0.5f);
            Vector2 size   = new Vector2(x2 - x1, y2 - y1);
            return (center, size);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private static void EnsureSpritesLoaded()
        {
            if (_formSprite == null)
                _formSprite = GraphicsManager.Instance?.GetSprite(ResourceRoot + "FormPilot");
        }

        // ── Inner ─────────────────────────────────────────────────────────────────

        private class SkillCell
        {
            public Text ValueText;
            public Image PlusButton;

            public void UpdateDisplay(int b, int e, int cost, int free)
            {
                string txt = b.ToString();
                if (e != b)
                    txt += $" ({(e - b >= 0 ? "+" : "")}{e - b})";
                ValueText.text = txt;

                // Цвет цифры: на потолке прокачки — жёлтый, базовый — белый.
                ValueText.color = cost == int.MaxValue
                    ? new Color(0.95f, 0.85f, 0.40f)
                    : new Color(0.98f, 0.98f, 0.98f);
            }
        }

        private class PlusClickHandler : MonoBehaviour, IPointerClickHandler
        {
            public PilotPanelController Owner;
            public SkillType Skill;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left)
                    Owner?.OnPlusClicked(Skill);
            }
        }
    }
}
