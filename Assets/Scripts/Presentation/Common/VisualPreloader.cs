using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy;
using SRG.Galaxy.Generation;
using SRG.Presentation.World;

namespace SRG.Presentation.Common
{
    /// <summary>
    /// Прогрев кэша графики при старте/загрузке игры, чтобы первый показ ракеты, взрыва,
    /// корпуса или звезды не давал хитч посреди хода. Вызывается слоем приложения (GalaxyManager).
    /// </summary>
    public static class VisualPreloader
    {
        /// <summary>Прогревает кэш GraphicsManager всеми спрайт-листами, чей первый показ иначе
        /// даёт хитч на Resources.Load + парсинге .png.json + Sprite.Create. Без этого:
        ///   • первый запуск ракеты данного типа = синхронная загрузка спрайтшита (десятки Sprite.Create);
        ///   • первый Sprite-снаряд оружия с Visual.Mode="Sprite" = Resources.Load на главном потоке;
        ///   • первый взрыв данного типа = загрузка листа + парсинг меты;
        ///   • первый вход в звезду = ~700 мс на первую загрузку StarVisuals.
        /// Единый sink — GraphicsManager.PreloadSheets; single-sprites и StarVisuals заходят
        /// через свои специфичные вызовы (GetSprite / GetStarVisuals) — их кэш общий с sheet-кэшем.</summary>
        public static void PreloadAll(GraphicsManager gm, GalaxyGenerationContext context, GameSettingsConfig settings)
        {
            if (gm == null) return;

            var sheets = new HashSet<string>();

            // 1) Взрывы: settings + константы + ExplosionPath каждого типа астероида.
            var asteroidTypes = context?.Config?.Asteroids?.Types;
            if (asteroidTypes != null)
                foreach (var t in asteroidTypes.Values)
                    if (!string.IsNullOrEmpty(t?.ExplosionPath)) sheets.Add(t.ExplosionPath);
            if (settings != null && !string.IsNullOrEmpty(settings.ExplosionSpritePath))
                sheets.Add(settings.ExplosionSpritePath);
            if (!string.IsNullOrEmpty(GalaxyConstants.EXPLOSION_SPRITE_PATH))
                sheets.Add(GalaxyConstants.EXPLOSION_SPRITE_PATH);

            var equip = context?.ItemsConfig;

            // 2) Ракеты (спрайт-листы) и спрайт-снаряды оружия (single sprites).
            if (equip != null)
                foreach (var (_, _, cfg) in equip.EnumerateAllItems())
                {
                    if (cfg == null) continue;
                    if (cfg.Missile != null && !string.IsNullOrEmpty(cfg.Missile.GraphicPath))
                        sheets.Add(cfg.Missile.GraphicPath);
                    if (cfg.Visual?.IsSprite == true && !string.IsNullOrEmpty(cfg.Visual.SpritePath))
                        gm.GetSprite(cfg.Visual.SpritePath);
                }

            // 3) Корпуса кораблей: все BodyGraphicPath × все расы галактики. ShipGraphicsResolver
            //    сам дедуплицирует запросы, HashSet тоже, поэтому двойной цикл дёшев
            //    (реально загрузим ~30–50 уникальных hull-листов). Убирает хитч 30–200 мс
            //    на первом появлении каждого синтет-подтипа.
            if (equip != null && context != null)
            {
                var races = context.AvailableRaces;
                foreach (var (_, _, cfg) in equip.EnumerateAllItems())
                {
                    if (cfg == null || string.IsNullOrEmpty(cfg.BodyGraphicPath)) continue;
                    if (races != null)
                        foreach (var raceKey in races.Keys)
                        {
                            string path = ShipGraphicsResolver.ResolveFromBase(
                                cfg.BodyGraphicPath, null, raceKey);
                            if (!string.IsNullOrEmpty(path)) sheets.Add(path);
                        }
                    // Default-вариант (без расы) — тоже прогреваем: используется для owner-агностик спавнов.
                    string defPath = ShipGraphicsResolver.ResolveFromBase(
                        cfg.BodyGraphicPath, null, null);
                    if (!string.IsNullOrEmpty(defPath)) sheets.Add(defPath);
                }
            }

            // 4) Астероиды: все варианты всех типов (Rocky/00..14, Metallic/Blue00..Blue13 и т.п.).
            if (asteroidTypes != null)
                foreach (var t in asteroidTypes.Values)
                {
                    if (t == null || string.IsNullOrEmpty(t.GraphicPath)) continue;
                    foreach (var sheet in SRG.Galaxy.Simulation.AsteroidSystem.EnumerateVariantSheetPaths(t.GraphicPath))
                        sheets.Add(sheet);
                }

            // 5) Гиперпереход-эффекты: begin / mid / end (см. HyperjumpPortalVisualController).
            sheets.Add(HyperjumpPortalVisualController.PathBegin);
            sheets.Add(HyperjumpPortalVisualController.PathMid);
            sheets.Add(HyperjumpPortalVisualController.PathEnd);

            // 6) Контейнеры: Container_1..Container_N (ContainerFactory.ContainerVariants).
            //    Первый выброшенный/пиратский контейнер иначе даёт хитч ~50-100мс среди хода
            //    (в perf-логе видели [GfxLoad] Container_1/2/3 на ходах 7/14/46).
            for (int ci = 1; ci <= SRG.Ships.ContainerFactory.ContainerVariants; ci++)
                sheets.Add($"{SRG.Ships.ContainerFactory.ContainersBasePath}/Container_{ci}");

            gm.PreloadSheets(sheets);

            // 7) Звёзды прогреваются отдельно: только стартовая — синхронно (PreloadStarVisuals),
            //    остальные уникальные (Color, GraphVar) — фоновой корутиной (Resources.LoadAsync),
            //    чтобы не блокировать старт на ~20 сек (26 листов × 700-800 мс каждый).
        }

        /// <summary>Прогрев стартовой звезды синхронно + запуск фоновой корутины на остальные
        /// уникальные (Color, GraphVar). Стартовая звезда обязана быть в кэше к моменту RenderSystem;
        /// остальные лениво догружаются, но большинство времени игрок в одной системе, так что
        /// хитч на первом входе в незнакомую систему — редкий случай, а сэкономленные ~20 сек
        /// стартовой заморозки видит каждый.</summary>
        public static void PreloadStars(GraphicsManager gm, GalaxyGenerationContext context, GalaxyData galaxy,
            StarData startingStar, MonoBehaviour coroutineHost)
        {
            if (gm == null || galaxy?.StarsMap == null) return;

            // 1) Стартовая — синхронно (следом идёт SetCurrentStar → RenderSystem).
            if (startingStar != null)
                gm.GetStarVisuals(startingStar.Color, startingStar.GraphVar);

            // 2) Остальные уникальные (Color, GraphVar) — в фон.
            var toLoad = new List<(string color, int var)>();
            var seen = new HashSet<(string, int)>();
            if (startingStar != null) seen.Add((startingStar.Color ?? "", startingStar.GraphVar));
            foreach (var s in galaxy.StarsMap.Values)
            {
                if (s == null) continue;
                var key = (s.Color ?? "", s.GraphVar);
                if (seen.Add(key)) toLoad.Add(key);
            }
            if (toLoad.Count > 0 && coroutineHost != null)
                coroutineHost.StartCoroutine(BackgroundPreloadStarSheets(gm, context, toLoad));
        }

        private static System.Collections.IEnumerator BackgroundPreloadStarSheets(
            GraphicsManager gm, GalaxyGenerationContext context, List<(string color, int var)> variants)
        {
            if (gm == null) yield break;
            var colors = context?.Config?.Stars?.Colors;
            if (colors == null) yield break;

            foreach (var (color, variant) in variants)
            {
                if (!colors.TryGetValue(color, out var colorData) || colorData == null) continue;
                string path = $"{colorData.Path}/Var{variant}";
                yield return gm.PreloadSheetAsync(path);
            }
        }
    }
}
