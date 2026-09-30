using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SRG.Config;
using SRG.Core;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Presentation.Common;
using SRG.Presentation.Map;
using SRG.Ships.Movement;
using SRG.UI.Logic;

namespace SRG.UI.Screens
{
    public partial class GalaxyMapController
    {
        // ══════════════════════════════════════════════════════════════════════════
        // Построение UI

        private void BuildUI()
        {
            var cgo = new GameObject("GalaxyMapCanvas");
            DontDestroyOnLoad(cgo);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            cgo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            cgo.AddComponent<GraphicRaycaster>();

            _rootGo = new GameObject("GalaxyMapPanel", typeof(RectTransform));
            _rootGo.transform.SetParent(_canvas.transform, false);
            _rootRT = _rootGo.GetComponent<RectTransform>();
            _rootRT.anchorMin = _rootRT.anchorMax = new Vector2(0.5f, 0.5f);
            _rootRT.pivot            = new Vector2(0.5f, 0.5f);
            _rootRT.sizeDelta        = panelSize;
            _rootRT.anchoredPosition = Vector2.zero;

            var bg = _rootGo.AddComponent<Image>();
            if (panelBgSprite != null) { bg.sprite = panelBgSprite; bg.type = Image.Type.Sliced; }
            else bg.color = new Color(0.07f, 0.08f, 0.12f, 0.97f);

            BuildTitleBar();
            BuildScrollArea();
            BuildStarTooltip();
        }

        private void BuildTitleBar()
        {
            var go = new GameObject("TitleBar", typeof(RectTransform));
            go.transform.SetParent(_rootGo.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(4f, -titleHeight);
            rt.offsetMax = new Vector2(-4f, -4f);
            go.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.10f, 1f);

            // Статичная подпись слева
            Label(go.transform, "Title", "Галакт. карта", 13,
                new Vector2(6f, 0f), new Vector2(-panelSize.x + 120f, 0f),
                new Color(0.60f, 0.63f, 0.75f, 0.8f), TextAnchor.MiddleLeft);

            // Кнопки режима окраски
            BuildModeButtons(go.transform);

            // Кнопка переключения просматриваемой галактики (если галактик > 1)
            BuildSwapGalaxyButton(go.transform);

            // Кнопка запуска/отмены гиперперехода
            BuildJumpButton(go.transform);

            // Название галактики — интерактивный элемент в центре
            var nameGO = new GameObject("GalaxyName", typeof(RectTransform));
            nameGO.transform.SetParent(go.transform, false);
            var nameRT = nameGO.GetComponent<RectTransform>();
            // anchor.min.x=0.52 — чтобы область названия (raycastTarget=true для тултипа)
            // не перекрывала кнопку «▷ Галактика» (x=320..480 в TitleBar ~932 px = ~0.34..0.52).
            nameRT.anchorMin = new Vector2(0.52f, 0f);
            nameRT.anchorMax = new Vector2(0.7f, 1f);
            nameRT.offsetMin = Vector2.zero;
            nameRT.offsetMax = Vector2.zero;
            _galaxyNameText = nameGO.AddComponent<Text>();
            _galaxyNameText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _galaxyNameText.fontSize      = 16;
            _galaxyNameText.fontStyle     = FontStyle.Bold;
            _galaxyNameText.alignment     = TextAnchor.MiddleCenter;
            _galaxyNameText.color         = new Color(0.88f, 0.92f, 1f, 1f);
            _galaxyNameText.raycastTarget = true;

            // Привязать hover-события через EventTrigger
            var et = nameGO.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerEnter,
                _ => { if (_galaxyTooltipGO != null) _galaxyTooltipGO.SetActive(true); });
            AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerExit,
                _ => { if (_galaxyTooltipGO != null) _galaxyTooltipGO.SetActive(false); });

            // Тултип (изначально скрыт)
            BuildGalaxyTooltip();

            // Кнопка закрытия справа
            float bs = titleHeight - 6f;
            var btn = new GameObject("CloseBtn", typeof(RectTransform));
            btn.transform.SetParent(go.transform, false);
            var brt = btn.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-3f, 0f);
            brt.sizeDelta = new Vector2(bs, bs);
            var bi = btn.AddComponent<Image>();
            bi.color = new Color(0.5f, 0.12f, 0.12f, 1f);
            var b = btn.AddComponent<Button>();
            b.targetGraphic = bi;
            var bc = b.colors;
            bc.highlightedColor = new Color(0.8f, 0.2f, 0.2f, 1f);
            bc.pressedColor     = new Color(0.3f, 0.06f, 0.06f, 1f);
            b.colors = bc;
            b.onClick.AddListener(CloseMap);
            Label(btn.transform, "X", "×", 18, Vector2.zero, Vector2.zero, Color.white, TextAnchor.MiddleCenter);
        }

        private void BuildGalaxyTooltip()
        {
            // Тултип размещается в корне панели, под шапкой
            _galaxyTooltipGO = new GameObject("GalaxyTooltip", typeof(RectTransform));
            _galaxyTooltipGO.transform.SetParent(_rootGo.transform, false);
            var tRT = _galaxyTooltipGO.GetComponent<RectTransform>();
            tRT.anchorMin        = new Vector2(0.5f, 1f);
            tRT.anchorMax        = new Vector2(0.5f, 1f);
            tRT.pivot            = new Vector2(0.5f, 1f);
            tRT.sizeDelta        = new Vector2(200f, 48f);
            tRT.anchoredPosition = new Vector2(0f, -(4f + titleHeight + 4f));
            var tBg = _galaxyTooltipGO.AddComponent<Image>();
            tBg.color = new Color(0.06f, 0.07f, 0.12f, 0.97f);

            var tTextGO = new GameObject("Text", typeof(RectTransform));
            tTextGO.transform.SetParent(_galaxyTooltipGO.transform, false);
            var tTRT = tTextGO.GetComponent<RectTransform>();
            tTRT.anchorMin = Vector2.zero; tTRT.anchorMax = Vector2.one;
            tTRT.offsetMin = new Vector2(8f, 4f); tTRT.offsetMax = new Vector2(-8f, -4f);
            _galaxyTooltipText = tTextGO.AddComponent<Text>();
            _galaxyTooltipText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _galaxyTooltipText.fontSize      = 12;
            _galaxyTooltipText.alignment     = TextAnchor.UpperLeft;
            _galaxyTooltipText.color         = new Color(0.80f, 0.84f, 0.95f, 1f);
            _galaxyTooltipText.raycastTarget = false;

            _galaxyTooltipGO.SetActive(false);
        }

        private void BuildStarTooltip()
        {
            _starTooltipGO = new GameObject("StarTooltip", typeof(RectTransform));
            _starTooltipGO.transform.SetParent(_rootGo.transform, false);
            var rt = _starTooltipGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(220f, 100f);

            var bg = _starTooltipGO.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.12f, 0.97f);
            bg.raycastTarget = false;

            var iconGO = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(_starTooltipGO.transform, false);
            var irt = iconGO.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot     = new Vector2(0f, 1f);
            irt.anchoredPosition = new Vector2(6f, -6f);
            irt.sizeDelta        = new Vector2(24f, 24f);
            _starTooltipIcon = iconGO.AddComponent<Image>();
            _starTooltipIcon.raycastTarget = false;

            var nameGO = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(_starTooltipGO.transform, false);
            var nrt = nameGO.GetComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0f, 1f); nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot     = new Vector2(0f, 1f);
            nrt.offsetMin = new Vector2(34f, -30f); nrt.offsetMax = new Vector2(-6f, -6f);
            _starTooltipNameText = nameGO.AddComponent<Text>();
            _starTooltipNameText.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _starTooltipNameText.fontSize      = 14;
            _starTooltipNameText.fontStyle     = FontStyle.Bold;
            _starTooltipNameText.alignment     = TextAnchor.MiddleLeft;
            _starTooltipNameText.color         = new Color(0.88f, 0.92f, 1f);
            _starTooltipNameText.raycastTarget = false;
            _starTooltipNameText.supportRichText= true;

            var planetsGO = new GameObject("Planets", typeof(RectTransform));
            planetsGO.transform.SetParent(_starTooltipGO.transform, false);
            var prt = planetsGO.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(8f, 6f); prt.offsetMax = new Vector2(-6f, -36f);
            _starTooltipPlanetsText = planetsGO.AddComponent<Text>();
            _starTooltipPlanetsText.font            = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _starTooltipPlanetsText.fontSize        = 12;
            _starTooltipPlanetsText.alignment       = TextAnchor.UpperLeft;
            _starTooltipPlanetsText.color           = new Color(0.85f, 0.85f, 0.9f);
            _starTooltipPlanetsText.supportRichText = true;
            _starTooltipPlanetsText.raycastTarget   = false;

            _starTooltipGO.SetActive(false);
        }

        private void ShowStarTooltip(StarData star, RectTransform starRT)
        {
            if (_starTooltipGO == null || star == null || starRT == null) return;
            var ctx = GalaxyManager.Instance?.Context;

            var spr = GraphicsManager.Instance?.TryGetSprite(star.MapIcon);
            if (spr != null) { _starTooltipIcon.sprite = spr; _starTooltipIcon.color = Color.white; }
            else             { _starTooltipIcon.sprite = null; _starTooltipIcon.color = StarColor(star.Color); }

            _starTooltipNameText.text  = string.IsNullOrEmpty(star.Name) ? "?" : star.Name;
            _starTooltipNameText.color = StarNameColor(star, ctx);

            var sb = new System.Text.StringBuilder();
            if (star.Planets != null && star.Planets.Count > 0)
            {
                var sorted = new List<PlanetData>(star.Planets);
                sorted.Sort((a, b) => a.OrbitIndex.CompareTo(b.OrbitIndex));
                foreach (var p in sorted)
                {
                    string label   = string.IsNullOrEmpty(p.Name) ? "?" : p.Name;
                    string hex     = GetPlanetNameColorHex(p, ctx);
                    sb.AppendLine($"<color=#{hex}>{label}</color>");
                }
            }
            else
            {
                sb.AppendLine("<color=#7B7B7B>нет планет</color>");
            }

            // Дальняя антенна: если у игрока есть артефакт с Artefacts.GalaxyMapScope > 0,
            // и звезда в пределах его радиуса — показать сводку кораблей по типу и стороне.
            int shipLines = AppendProlongerShipsInfo(sb, star);

            _starTooltipPlanetsText.text = sb.ToString();

            int planetCount = star.Planets?.Count ?? 0;
            float bodyH = Mathf.Max(1, planetCount + shipLines) * 16f + 12f;
            float h     = Mathf.Min(340f, 36f + bodyH);
            var rt = _starTooltipGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(220f, h);

            // Позиция: переводим экранную позицию иконки звезды в локальные координаты панели.
            Vector3 worldPos = starRT.TransformPoint(new Vector3(starRT.rect.width * 0.5f, starRT.rect.height * 0.5f, 0));
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, worldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRT, screenPos, null, out Vector2 panelLocal);

            // Сместить вправо-вверх от звезды; если уходит за правую границу — повернуть влево.
            Vector2 offset = new Vector2(14f, 14f);
            Vector2 pos = panelLocal + offset;
            var panelHalf = _rootRT.rect.size * 0.5f;
            if (pos.x + rt.sizeDelta.x > panelHalf.x) pos.x = panelLocal.x - rt.sizeDelta.x - 14f;
            if (pos.y > panelHalf.y)                  pos.y = panelHalf.y - 2f;
            if (pos.y - rt.sizeDelta.y < -panelHalf.y) pos.y = -panelHalf.y + rt.sizeDelta.y + 2f;
            rt.anchoredPosition = pos;

            _starTooltipGO.SetActive(true);
            _starTooltipGO.transform.SetAsLastSibling();
        }

        private void HideStarTooltip()
        {
            if (_starTooltipGO != null) _starTooltipGO.SetActive(false);
        }

        private int AppendProlongerShipsInfo(System.Text.StringBuilder sb, StarData star)
            => GalaxyMapPresenter.AppendProlongerShipsInfo(sb, star,
                   PlayerManager.Instance?.GetOrFindPlayerShip(), GalaxyManager.Instance?.GeneratedGalaxy);

        private static string GetPlanetNameColorHex(PlanetData planet, GalaxyGenerationContext ctx)
            => GalaxyMapPresenter.PlanetNameColorHex(planet, ctx);

        private void BuildJumpButton(Transform parent)
        {
            float btnH = titleHeight - 10f;
            float btnW = 240f;
            float closeBtnSize = titleHeight - 6f;
            var go = new GameObject("BtnHyperjump", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            // Слева от крестика закрытия (3px + closeBtnSize + 6px зазор)
            rt.anchoredPosition = new Vector2(-(3f + closeBtnSize + 6f), 0f);
            rt.sizeDelta = new Vector2(btnW, btnH);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.18f, 0.30f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnJumpClicked);
            var labelGo = new GameObject("L", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            _jumpBtnLabel = labelGo.AddComponent<Text>();
            _jumpBtnLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _jumpBtnLabel.fontSize = 12;
            _jumpBtnLabel.alignment = TextAnchor.MiddleCenter;
            _jumpBtnLabel.color = new Color(0.85f, 0.90f, 1f, 1f);
            _jumpBtnLabel.text = "Гиперпрыжок";
            _jumpBtnImg = img;
            _jumpBtnGO = go;
        }

        private void OnJumpClicked()
        {
            var gm = GalaxyManager.Instance;
            var player = PlayerManager.Instance?.GetOrFindPlayerShip();
            if (gm == null || player == null) return;

            // Если прыжок активен — кнопка работает на отмену.
            if (CanCancelHyperjump(player))
            {
                if (gm.CancelHyperjump()) CloseMap();
                return;
            }
            if (_selectedTarget == null) return;
            if (gm.JumpToStar(_selectedTarget))
                CloseMap();
        }

        private void RefreshJumpButton()
        {
            if (_jumpBtnLabel == null || _jumpBtnImg == null) return;
            var gm = GalaxyManager.Instance;
            var player = PlayerManager.Instance?.GetOrFindPlayerShip();
            var galaxy = gm?.GeneratedGalaxy;
            if (player == null || galaxy == null) { _jumpBtnImg.color = ColorDisabled(); return; }

            if (CanCancelHyperjump(player))
            {
                _jumpBtnLabel.text = $"Отменить ({player.HyperjumpPhase})";
                _jumpBtnImg.color = new Color(0.40f, 0.18f, 0.18f, 1f);
                return;
            }

            // Просмотр другой галактики: прыжок недоступен — нужны червоточины/консольный teleport.
            if (!string.IsNullOrEmpty(_viewingGalaxyKey) && _viewingGalaxyKey != gm.ActiveGalaxyKey)
            {
                _jumpBtnLabel.text = "Прыжок в другую галактику недоступен";
                _jumpBtnImg.color = ColorDisabled();
                return;
            }

            if (_selectedTarget == null)
            {
                _jumpBtnLabel.text = "Гиперпрыжок (выберите цель)";
                _jumpBtnImg.color = ColorDisabled();
                return;
            }
            if (HyperjumpController.CanRequestJump(player, _selectedTarget, galaxy, out string reason))
            {
                int cost = HyperjumpController.CalcFuelCost(gm.CurrentStar, _selectedTarget);
                _jumpBtnLabel.text = $"Гиперпрыжок → {_selectedTarget.Name} (-{cost} топлива)";
                _jumpBtnImg.color = new Color(0.14f, 0.28f, 0.46f, 1f);
            }
            else
            {
                _jumpBtnLabel.text = $"Прыжок невозможен: {reason}";
                _jumpBtnImg.color = ColorDisabled();
            }
        }

        private static Color ColorDisabled() => new Color(0.18f, 0.20f, 0.26f, 1f);

        private static bool CanCancelHyperjump(ShipData player)
            => player != null
            && (player.HyperjumpPhase == HyperjumpPhase.Travel
                || player.HyperjumpPhase == HyperjumpPhase.HyperEnter);

        private void SelectTarget(StarData star)
        {
            _selectedTarget = star;
            foreach (var (rt, s, img) in _starButtons)
            {
                if (img == null) continue;
                bool sel = s == _selectedTarget;
                // У звёзд со спрайтом-иконкой исходный tint=white (спрайт уже окрашен).
                // У fallback-кружков tint берётся из StarColor(s.Color).
                Color baseCol = img.sprite != null ? Color.white : StarColor(s.Color);
                img.color = sel ? new Color(1f, 1f, 0.3f, 1f) : baseCol;
            }
            RefreshJumpButton();
        }

        private void BuildModeButtons(Transform parent)
        {
            float btnH = titleHeight - 10f;
            _btnModeOwner = MakeModeButton(parent, "BtnOwner", "Владелец", 122f, 68f, btnH,
                () => SetColorMode(ColorMode.Owner));
            _btnModeRace = MakeModeButton(parent, "BtnRace", "Раса", 193f, 48f, btnH,
                () => SetColorMode(ColorMode.Race));
            _btnExpansion = MakeModeButton(parent, "BtnExpansion", "Экспансия", 244f, 72f, btnH,
                () => ToggleExpansionLines());
            UpdateModeBtnColors();
        }

        private void BuildSwapGalaxyButton(Transform parent)
        {
            var gm = GalaxyManager.Instance;
            if (gm?.Galaxies == null || gm.Galaxies.Count <= 1) return;
            float btnH = titleHeight - 10f;
            var go = new GameObject("BtnSwapGalaxy", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(320f, 0f);
            rt.sizeDelta = new Vector2(160f, btnH);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.20f, 0.35f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(CycleViewingGalaxy);
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4f, 0f); lrt.offsetMax = new Vector2(-4f, 0f);
            _btnSwapGalaxyLabel = lblGo.AddComponent<Text>();
            _btnSwapGalaxyLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _btnSwapGalaxyLabel.fontSize = 11;
            _btnSwapGalaxyLabel.color = new Color(0.85f, 0.90f, 1f, 1f);
            _btnSwapGalaxyLabel.text = "▷ Галактика";
            _btnSwapGalaxyLabel.alignment = TextAnchor.MiddleCenter;
            _btnSwapGalaxyLabel.raycastTarget = false;
            _btnSwapGalaxyGO = go;
            UpdateSwapGalaxyLabel();
        }

        private void CycleViewingGalaxy()
        {
            var gm = GalaxyManager.Instance;
            if (gm?.Galaxies == null || gm.Galaxies.Count <= 1) return;
            var keys = new List<string>(gm.Galaxies.Keys);
            int idx = Mathf.Max(0, keys.IndexOf(_viewingGalaxyKey));
            string nextKey = keys[(idx + 1) % keys.Count];
            // Оба ключа должны существовать одновременно (Galaxies + Config.Galaxies), иначе
            // получится рассинхрон _galaxy vs _galCfg → некорректный масштаб карты.
            if (gm.Context?.Config?.Galaxies == null
                || !gm.Context.Config.Galaxies.TryGetValue(nextKey, out var cfg))
            {
                UnityEngine.Debug.LogWarning($"[GalaxyMap] Cycle skipped: config for '{nextKey}' missing.");
                return;
            }
            _viewingGalaxyKey = nextKey;
            _galaxy = gm.Galaxies[nextKey];
            _galCfg = cfg;
            // Пересчёт размеров и перезагрузка контента под новую галактику
            float ppp  = _galCfg.MapPixelsPerParsec > 0 ? _galCfg.MapPixelsPerParsec : 4f;
            var   grid = _galCfg.GalaxyGridSize;
            float szW  = (grid != null && grid.Length >= 2) ? grid[0] : _galaxy.Width;
            float szH  = (grid != null && grid.Length >= 2) ? grid[1] : _galaxy.Height;
            _baseMapSize = new Vector2(szW * ppp, szH * ppp);
            _zoom = Mathf.Clamp(_zoom, ZoomMin(), ZOOM_MAX);
            _selectedTarget = null;
            RefreshContent();
            UpdateSwapGalaxyLabel();
        }

        private void UpdateSwapGalaxyLabel()
        {
            if (_btnSwapGalaxyLabel == null) return;
            var gm = GalaxyManager.Instance;
            var keys = gm != null ? new List<string>(gm.Galaxies.Keys) : new List<string>();
            int idx = Mathf.Max(0, keys.IndexOf(_viewingGalaxyKey));
            int next = keys.Count > 0 ? (idx + 1) % keys.Count : 0;
            string nextName = keys.Count > 0
                && gm.Context?.Config?.Galaxies != null
                && gm.Context.Config.Galaxies.TryGetValue(keys[next], out var nextCfg)
                && !string.IsNullOrEmpty(nextCfg?.Name)
                ? nextCfg.Name : (keys.Count > 0 ? keys[next] : "—");
            _btnSwapGalaxyLabel.text = $"▷ {nextName}";
        }

        private Image MakeModeButton(Transform parent, string name, string label,
            float x, float w, float h, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot            = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta        = new Vector2(w, h);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.08f, 0.10f, 0.18f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            Label(go.transform, "L", label, 10,
                new Vector2(2f, 0f), new Vector2(-2f, 0f),
                new Color(0.75f, 0.80f, 0.95f, 1f), TextAnchor.MiddleCenter);
            return img;
        }

        private void SetColorMode(ColorMode mode)
        {
            if (_colorMode == mode) return;
            _colorMode = mode;
            UpdateModeBtnColors();
            if (_isOpen) { RebuildVoronoi(); SpawnSectorLabels(); }
        }

        private void ToggleExpansionLines()
        {
            _showExpansionLines = !_showExpansionLines;
            UpdateModeBtnColors();
            if (_isOpen) RebuildVoronoi();
        }

        private void UpdateModeBtnColors()
        {
            var active   = new Color(0.20f, 0.32f, 0.58f, 1f);
            var inactive = new Color(0.08f, 0.10f, 0.18f, 1f);
            if (_btnModeOwner != null)
                _btnModeOwner.color = _colorMode == ColorMode.Owner ? active : inactive;
            if (_btnModeRace != null)
                _btnModeRace.color = _colorMode == ColorMode.Race ? active : inactive;
            if (_btnExpansion != null)
                _btnExpansion.color = _showExpansionLines ? active : inactive;
        }

        private static void AddEventTrigger(
            UnityEngine.EventSystems.EventTrigger et,
            UnityEngine.EventSystems.EventTriggerType type,
            UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData> action)
        {
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            et.triggers.Add(entry);
        }

        private void BuildScrollArea()
        {
            var scrollGO = new GameObject("ScrollView", typeof(RectTransform));
            scrollGO.transform.SetParent(_rootGo.transform, false);
            var srt = scrollGO.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(4f, 4f);
            srt.offsetMax = new Vector2(-4f, -(4f + titleHeight + 4f));
            scrollGO.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.06f, 1f);

            var vpGO = new GameObject("Viewport", typeof(RectTransform));
            vpGO.transform.SetParent(scrollGO.transform, false);
            _vpRT = vpGO.GetComponent<RectTransform>();
            _vpRT.anchorMin = Vector2.zero; _vpRT.anchorMax = Vector2.one;
            _vpRT.offsetMin = Vector2.zero; _vpRT.offsetMax = Vector2.zero;
            vpGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("MapContent", typeof(RectTransform));
            contentGO.transform.SetParent(vpGO.transform, false);
            _mapContent = contentGO.GetComponent<RectTransform>();
            _mapContent.anchorMin = _mapContent.anchorMax = Vector2.zero;
            _mapContent.pivot = Vector2.zero;
            _mapContent.anchoredPosition = Vector2.zero;
            _mapContent.sizeDelta = new Vector2(100f, 100f);
            var cbg = contentGO.AddComponent<Image>();
            if (mapBgSprite != null) { cbg.sprite = mapBgSprite; cbg.type = Image.Type.Tiled; }
            else cbg.color = new Color(0.02f, 0.02f, 0.05f, 1f);

            // Вороной (нижний слой)
            // pivot=(0,0): локальный (0,0) совпадает с нижним левым углом rect,
            // что соответствует системе координат полигонов Вороного (0..mapSize)
            var vorGO = new GameObject("Voronoi", typeof(RectTransform));
            vorGO.transform.SetParent(contentGO.transform, false);
            var vorRT = vorGO.GetComponent<RectTransform>();
            vorRT.anchorMin = Vector2.zero;
            vorRT.anchorMax = Vector2.one;
            vorRT.offsetMin = Vector2.zero;
            vorRT.offsetMax = Vector2.zero;
            vorRT.pivot     = Vector2.zero;
            _voronoi = vorGO.AddComponent<GalaxyMapGraphics>();
            _voronoi.raycastTarget = false;

            // Подписи секторов (над Вороным, под звёздами)
            var slGO = new GameObject("SectorLabels", typeof(RectTransform));
            slGO.transform.SetParent(contentGO.transform, false);
            _sectorLabelContainer = slGO.GetComponent<RectTransform>();
            Stretch(_sectorLabelContainer);

            // Debug: координатные подписи вершин Вороного
            var dlGO = new GameObject("DebugLabels", typeof(RectTransform));
            dlGO.transform.SetParent(contentGO.transform, false);
            _debugLabelContainer = dlGO.GetComponent<RectTransform>();
            Stretch(_debugLabelContainer);

            // Подписи линий экспансии (расстояние + год)
            var elGO = new GameObject("ExpansionLabels", typeof(RectTransform));
            elGO.transform.SetParent(contentGO.transform, false);
            _expansionLabelContainer = elGO.GetComponent<RectTransform>();
            Stretch(_expansionLabelContainer);

            // Иконки звёзд (верхний слой)
            var starsGO = new GameObject("Stars", typeof(RectTransform));
            starsGO.transform.SetParent(contentGO.transform, false);
            _starContainer = starsGO.GetComponent<RectTransform>();
            Stretch(_starContainer);

            _hSbGO = MakeScrollbar(scrollGO.transform, true);
            _vSbGO = MakeScrollbar(scrollGO.transform, false);

            _scrollRect = scrollGO.AddComponent<ScrollRect>();
            _scrollRect.content           = _mapContent;
            _scrollRect.viewport          = _vpRT;
            _scrollRect.horizontal        = true;
            _scrollRect.vertical          = true;
            _scrollRect.scrollSensitivity = 0f;
            _scrollRect.movementType      = ScrollRect.MovementType.Clamped;
            _scrollRect.horizontalScrollbar = _hSbGO.GetComponent<Scrollbar>();
            _scrollRect.verticalScrollbar   = _vSbGO.GetComponent<Scrollbar>();
            _scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scrollRect.verticalScrollbarVisibility   = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private GameObject MakeScrollbar(Transform parent, bool horizontal)
        {
            var go = new GameObject(horizontal ? "HScrollbar" : "VScrollbar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (horizontal)
            {
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(-SB, SB);
                rt.anchoredPosition = Vector2.zero;
            }
            else
            {
                rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(SB, -SB);
                rt.anchoredPosition = Vector2.zero;
            }
            go.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 1f);

            var sa = new GameObject("SA", typeof(RectTransform));
            sa.transform.SetParent(go.transform, false);
            Stretch(sa.GetComponent<RectTransform>());

            var h = new GameObject("H", typeof(RectTransform));
            h.transform.SetParent(sa.transform, false);
            var hrt = h.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = Vector2.zero;
            hrt.sizeDelta = new Vector2(SB, SB);
            var hi = h.AddComponent<Image>();
            hi.color = new Color(0.35f, 0.45f, 0.65f, 1f);

            var sb = go.AddComponent<Scrollbar>();
            sb.handleRect    = hrt;
            sb.targetGraphic = hi;
            sb.direction     = horizontal ? Scrollbar.Direction.LeftToRight
                                          : Scrollbar.Direction.BottomToTop;
            var sc = sb.colors;
            sc.highlightedColor = new Color(0.5f, 0.62f, 0.88f, 1f);
            sc.pressedColor     = new Color(0.22f, 0.3f, 0.5f, 1f);
            sb.colors = sc;
            return go;
        }
    }
}
