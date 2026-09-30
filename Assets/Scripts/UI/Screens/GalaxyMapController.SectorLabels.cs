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
        // ── Подписи секторов ──────────────────────────────────────────────────────
        private void SpawnSectorLabels()
        {
            foreach (var (rt, _) in _sectorLabelRTs)
                if (rt != null) Destroy(rt.gameObject);
            _sectorLabelRTs.Clear();

            if (_sectorLabelContainer == null || _galaxy?.Sectors == null) return;

            var ctx = GalaxyManager.Instance?.Context;

            foreach (var sector in _galaxy.Sectors)
            {
                if (string.IsNullOrEmpty(sector.Name)) continue;

                var go = new GameObject($"SL_{sector.Uid}", typeof(RectTransform));
                go.transform.SetParent(_sectorLabelContainer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin        = rt.anchorMax = Vector2.zero;
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(140f, 60f);
                rt.anchoredPosition = GridToMap(sector.Center);

                // Название сектора
                var nameGO = new GameObject("Name", typeof(RectTransform));
                nameGO.transform.SetParent(go.transform, false);
                var nrt = nameGO.GetComponent<RectTransform>();
                nrt.anchorMin        = nrt.anchorMax = new Vector2(0.5f, 1f);
                nrt.pivot            = new Vector2(0.5f, 1f);
                nrt.sizeDelta        = new Vector2(140f, 20f);
                nrt.anchoredPosition = Vector2.zero;
                var nameT = nameGO.AddComponent<Text>();
                nameT.text          = sector.Name;
                nameT.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                nameT.fontSize      = 15;
                nameT.fontStyle     = FontStyle.Bold;
                nameT.alignment     = TextAnchor.MiddleCenter;
                nameT.color         = new Color(1f, 1f, 1f, 0.38f);
                nameT.raycastTarget = false;

                // Раса сектора
                string raceName = GetSectorRaceName(sector, ctx);
                if (!string.IsNullOrEmpty(raceName))
                {
                    Color _rc = SectorFillColor(sector, ctx);
                    Color raceCol = sector.Race == GalaxyConstants.RACE_MIXED_KEY
                        ? new Color(0.85f, 0.85f, 0.85f, 0.55f)
                        : new Color(_rc.r, _rc.g, _rc.b, 0.55f);

                    var raceGO = new GameObject("Race", typeof(RectTransform));
                    raceGO.transform.SetParent(go.transform, false);
                    var rrt = raceGO.GetComponent<RectTransform>();
                    rrt.anchorMin        = rrt.anchorMax = new Vector2(0.5f, 1f);
                    rrt.pivot            = new Vector2(0.5f, 1f);
                    rrt.sizeDelta        = new Vector2(140f, 16f);
                    rrt.anchoredPosition = new Vector2(0f, -20f);
                    var raceT = raceGO.AddComponent<Text>();
                    raceT.text          = raceName;
                    raceT.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    raceT.fontSize      = 11;
                    raceT.fontStyle     = FontStyle.Normal;
                    raceT.alignment     = TextAnchor.MiddleCenter;
                    raceT.color         = raceCol;
                    raceT.raycastTarget = false;
                }

                // Владелец сектора
                string ownerName = GetSectorOwnerName(sector, ctx);
                if (!string.IsNullOrEmpty(ownerName))
                {
                    Color ownerCol = SectorFillColor(sector, ctx);
                    ownerCol.a = 0.60f;

                    var ownerGO = new GameObject("Owner", typeof(RectTransform));
                    ownerGO.transform.SetParent(go.transform, false);
                    var ort = ownerGO.GetComponent<RectTransform>();
                    ort.anchorMin        = ort.anchorMax = new Vector2(0.5f, 0f);
                    ort.pivot            = new Vector2(0.5f, 0f);
                    ort.sizeDelta        = new Vector2(140f, 18f);
                    ort.anchoredPosition = Vector2.zero;
                    var ownerT = ownerGO.AddComponent<Text>();
                    ownerT.text          = ownerName;
                    ownerT.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    ownerT.fontSize      = 12;
                    ownerT.fontStyle     = FontStyle.Italic;
                    ownerT.alignment     = TextAnchor.MiddleCenter;
                    ownerT.color         = ownerCol;
                    ownerT.raycastTarget = false;
                }

                _sectorLabelRTs.Add((rt, sector.Center));
            }
        }

        private void SpawnExpansionLabels()
        {
            foreach (var (rt, _) in _expansionLabelRTs)
                if (rt != null) Destroy(rt.gameObject);
            _expansionLabelRTs.Clear();

            if (_expansionLabelContainer == null || !_showExpansionLines) return;
            if (_galaxy?.ExpansionEdges == null || _galaxy.ExpansionEdges.Count == 0) return;

            var ectx = GalaxyManager.Instance?.Context;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            foreach (var edge in _galaxy.ExpansionEdges)
            {
                float dist = (edge.To - edge.From).magnitude;
                Vector2 gridMid = (edge.From + edge.To) * 0.5f;

                Color ec = Color.white;
                if (ectx?.Config?.Races?.TryGetValue(edge.RaceKey, out var rc) == true && !string.IsNullOrEmpty(rc.Color))
                    ec = ParseColor(rc.Color);

                var go = new GameObject($"EL_{edge.RaceKey}", typeof(RectTransform));
                go.transform.SetParent(_expansionLabelContainer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin        = rt.anchorMax = Vector2.zero;
                rt.pivot            = new Vector2(0.5f, 0f);
                rt.sizeDelta        = new Vector2(64f, 24f);
                rt.anchoredPosition = GridToMap(gridMid) + new Vector2(4f, 3f);

                var distGO = new GameObject("Dist", typeof(RectTransform));
                distGO.transform.SetParent(go.transform, false);
                var drt = distGO.GetComponent<RectTransform>();
                drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 1f);
                drt.pivot     = new Vector2(0.5f, 1f);
                drt.sizeDelta = new Vector2(64f, 12f);
                drt.anchoredPosition = Vector2.zero;
                var dt = distGO.AddComponent<Text>();
                dt.text          = $"{Mathf.RoundToInt(dist)} пк";
                dt.font          = font;
                dt.fontSize      = 10;
                dt.alignment     = TextAnchor.MiddleCenter;
                dt.color         = new Color(ec.r, ec.g, ec.b, 0.65f);
                dt.raycastTarget = false;

                if (edge.ColonizationYear > 0)
                {
                    var yearGO = new GameObject("Year", typeof(RectTransform));
                    yearGO.transform.SetParent(go.transform, false);
                    var yrt = yearGO.GetComponent<RectTransform>();
                    yrt.anchorMin        = yrt.anchorMax = new Vector2(0.5f, 1f);
                    yrt.pivot            = new Vector2(0.5f, 1f);
                    yrt.sizeDelta        = new Vector2(64f, 12f);
                    yrt.anchoredPosition = new Vector2(0f, -12f);
                    var yt = yearGO.AddComponent<Text>();
                    yt.text          = $"{edge.ColonizationYear} г.";
                    yt.font          = font;
                    yt.fontSize      = 9;
                    yt.alignment     = TextAnchor.MiddleCenter;
                    yt.color         = new Color(ec.r, ec.g, ec.b, 0.50f);
                    yt.raycastTarget = false;
                }

                _expansionLabelRTs.Add((rt, gridMid));
            }
        }

        private static string GetSectorOwnerName(SectorData sector, GalaxyGenerationContext ctx)
        {
            if (sector == null || ctx == null) return null;

            if (!string.IsNullOrEmpty(sector.ResolvedOwnerId))
                return sector.ResolvedOwnerId;

            string owner = sector.Owner;
            if (!string.IsNullOrEmpty(owner) && owner != GalaxyConstants.OWNER_NONE_KEY)
                return owner;

            return null;
        }

        private static string GetSectorRaceName(SectorData sector, GalaxyGenerationContext ctx)
        {
            if (sector == null) return null;
            string race = sector.Race;
            if (string.IsNullOrEmpty(race) || race == GalaxyConstants.RACE_NONE_KEY) return null;
            if (race == GalaxyConstants.RACE_MIXED_KEY) return "Mixed";
            if (ctx?.TextConfig?.RaceNames?.TryGetValue(race, out string displayName) == true
                && !string.IsNullOrEmpty(displayName))
                return displayName;
            return race;
        }

        private Color StarFillColor(StarData star, GalaxyGenerationContext ctx)
            => GalaxyMapPresenter.StarFillColor(star, ctx, _colorMode == ColorMode.Owner);

        private Color SectorFillColor(SectorData sector, GalaxyGenerationContext ctx)
            => GalaxyMapPresenter.SectorFillColor(sector, ctx, _colorMode == ColorMode.Owner);

        private void SpawnStarIcons()
        {
            foreach (var (rt, _) in _starRTs)
                if (rt != null) Destroy(rt.gameObject);
            _starRTs.Clear();
            _starButtons.Clear();

            if (_galaxy?.Sectors == null) return;

            var ctx = GalaxyManager.Instance?.Context;

            foreach (var sector in _galaxy.Sectors)
            foreach (var star in sector.Stars)
            {
                var go = new GameObject($"S_{star.Uid}", typeof(RectTransform));
                go.transform.SetParent(_starContainer, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(16f, 16f);
                rt.anchoredPosition = GridToMap(star.Position);

                // Иконка миникарты
                var img = go.AddComponent<Image>();
                var spr = GraphicsManager.Instance?.TryGetSprite(star.MapIcon);
                if (spr != null) { img.sprite = spr; img.color = Color.white; }
                else img.color = StarColor(star.Color);

                // Клик по звезде — выбор цели прыжка (не телепорт).
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var bc = btn.colors;
                bc.highlightedColor = new Color(1f, 1f, 1f, 0.75f);
                bc.pressedColor     = new Color(0.6f, 0.6f, 0.6f, 1f);
                btn.colors = bc;
                var capturedStar = star;
                btn.onClick.AddListener(() => SelectTarget(capturedStar));
                _starButtons.Add((rt, star, img));

                // Тултип при наведении: иконка, имя, список планет в цветах рас
                var et = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var capturedRT = rt;
                AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerEnter,
                    _ => ShowStarTooltip(capturedStar, capturedRT));
                AddEventTrigger(et, UnityEngine.EventSystems.EventTriggerType.PointerExit,
                    _ => HideStarTooltip());

                // Подпись (имя звезды) — используем NameRenderData как в SystemViewManager
                var labelGO = new GameObject("Name", typeof(RectTransform));
                labelGO.transform.SetParent(go.transform, false);
                var lrt = labelGO.GetComponent<RectTransform>();
                lrt.anchorMin        = new Vector2(0.5f, 0f);
                lrt.anchorMax        = new Vector2(0.5f, 0f);
                lrt.pivot            = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, -2f);
                lrt.sizeDelta        = new Vector2(90f, 16f);
                var t = labelGO.AddComponent<Text>();
                var rd = star.NameRenderData;
                if (rd != null && rd.Segments.Count > 0)
                {
                    t.supportRichText = true;
                    t.text  = rd.ToRichText();
                    t.color = Color.white; // цвет задаётся тегами внутри текста
                }
                else
                {
                    t.text  = star.Name;
                    t.color = StarNameColor(star, ctx);
                }
                t.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                t.fontSize      = 12;
                t.alignment     = TextAnchor.UpperCenter;
                t.raycastTarget = false;

                // Координаты в парсеках
                var coordGO = new GameObject("Coords", typeof(RectTransform));
                coordGO.transform.SetParent(go.transform, false);
                var crt = coordGO.GetComponent<RectTransform>();
                crt.anchorMin        = new Vector2(0.5f, 0f);
                crt.anchorMax        = new Vector2(0.5f, 0f);
                crt.pivot            = new Vector2(0.5f, 1f);
                crt.anchoredPosition = new Vector2(0f, -15f);
                crt.sizeDelta        = new Vector2(80f, 10f);
                var ct = coordGO.AddComponent<Text>();
                ct.text          = $"({Mathf.RoundToInt(star.Position.x)}, {Mathf.RoundToInt(star.Position.y)}) пк";
                ct.font          = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ct.fontSize      = 10;
                ct.alignment     = TextAnchor.UpperCenter;
                ct.color         = new Color(0.55f, 0.75f, 0.55f, 0.85f);
                ct.raycastTarget = false;

                SpawnWormholeMarker(go.transform, star);

                _starRTs.Add((rt, star.Position));
            }
        }

        /// <summary>Иконка активной червоточины у звезды на галакарте. Рисуется справа-сверху
        /// от иконки звезды, если у неё есть хотя бы одна червоточина в фазе Open.</summary>
        private void SpawnWormholeMarker(Transform parent, StarData star)
        {
            if (star?.Wormholes == null || star.Wormholes.Count == 0) return;
            WormholeData iconWh = null;
            for (int i = 0; i < star.Wormholes.Count; i++)
                if (star.Wormholes[i] != null && star.Wormholes[i].Phase == WormholePhase.Open)
                { iconWh = star.Wormholes[i]; break; }
            if (iconWh == null) return;

            string iconPath = !string.IsNullOrEmpty(iconWh.Graphics?.IconPath)
                ? iconWh.Graphics.IconPath
                : GalaxyConstants.WORMHOLE_ICON_PATH;
            var sprite = GraphicsManager.Instance?.TryGetSprite(iconPath);
            if (sprite == null) return;

            var whGO = new GameObject("Wormhole", typeof(RectTransform));
            whGO.transform.SetParent(parent, false);
            var wrt = whGO.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(1f, 1f);
            wrt.pivot = new Vector2(0f, 1f);
            wrt.anchoredPosition = new Vector2(2f, 2f);
            wrt.sizeDelta = new Vector2(14f, 14f);
            var img = whGO.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
        }

        private static Color StarNameColor(StarData star, GalaxyGenerationContext ctx) => GalaxyMapPresenter.StarNameColor(star, ctx);

        private static Color ParseColor(string csv) => GalaxyMapPresenter.ParseColor(csv);

        private void UpdateStarPositions()
        {
            foreach (var (rt, grid) in _starRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid);
            foreach (var (rt, grid) in _sectorLabelRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid);
            foreach (var (rt, grid) in _debugLabelRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid);
            foreach (var (rt, grid) in _expansionLabelRTs)
                if (rt != null) rt.anchoredPosition = GridToMap(grid) + new Vector2(4f, 3f);
            RebuildVoronoi();
        }

        private void AdjustScrollbars()
        {
            if (_vpRT == null || _hSbGO == null || _vSbGO == null) return;

            float svW = panelSize.x - 8f;
            float svH = panelSize.y - titleHeight - 12f;
            var   ms  = MapSize;

            bool needH = ms.x > svW;
            bool needV = ms.y > svH;
            if (needH) needV |= (ms.y > svH - SB);
            if (needV) needH |= (ms.x > svW - SB);

            _hSbGO.SetActive(needH);
            _vSbGO.SetActive(needV);
            _vpRT.offsetMin = new Vector2(0f, needH ? SB : 0f);
            _vpRT.offsetMax = new Vector2(needV ? -SB : 0f, 0f);
            _scrollRect.horizontal = needH;
            _scrollRect.vertical   = needV;
        }
    }
}
