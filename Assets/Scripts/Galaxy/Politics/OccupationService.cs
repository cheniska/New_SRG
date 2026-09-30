using System;
using UnityEngine;
using SRG.Config;
using SRG.Simulation;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Управление оккупацией планет. Оккупация — состояние планеты (<see cref="PlanetData.OccupiedByOwner"/>).
    /// Система «оккупирована» = все её населённые планеты управляются одной стороной; отдельного флага
    /// на StarData нет, только кэш <see cref="StarData.CurrentSystemController"/> для событий/UI.
    ///
    /// Скриптовые точки входа: <see cref="OccupyPlanet"/>, <see cref="LiberatePlanet"/>,
    /// <see cref="BlockLanding"/>, <see cref="AllowLanding"/>. Все они уведомляют подписчиков через
    /// статические события — на них будет вешаться будущий Faction-AI и NewsFeed.
    ///
    /// Авто-правило захвата — в <see cref="OccupationAutoRule"/>, вызывается раз в ход.
    /// </summary>
    public static class OccupationService
    {
        /// <summary>Одна планета сменила контроллёра (окку/освобождение). prevController — кто был до
        /// (эффективный: Occupied либо родной Owner). Ставит null, если планета не была оккупирована.</summary>
        public static event Action<PlanetData, string> OnPlanetControlChanged;

        /// <summary>Система перешла в состояние «полностью контролируется одной стороной» или вышла из
        /// него. newController=null — стало смешанной; newController!=null — стало единой.
        /// prevController — предыдущее значение <see cref="StarData.CurrentSystemController"/>.</summary>
        public static event Action<StarData, string, string> OnSystemControlChanged;

        /// <summary>Сторона (Owner) полностью потеряла все свои системы во всей галактике. Триггерится
        /// последним изменением контроля в её пользу. Слушается GalaxyNewsService — публикует
        /// «глобальное поражение стороны».</summary>
        public static event Action<string> OnFactionDefeated;

        /// <summary>Эффективный владелец планеты сейчас. Если оккупирована — оккупант, иначе родной Owner.</summary>
        public static string GetControllingOwner(PlanetData planet)
        {
            if (planet == null) return null;
            return string.IsNullOrEmpty(planet.Settlement.OccupiedByOwner) ? planet.Owner : planet.Settlement.OccupiedByOwner;
        }

        /// <summary>Занять планету стороной occupierOwnerId. Не проверяет отношений/близости кораблей —
        /// это скриптовый вход. Автоблокировку посадки не ставит: вызывать <see cref="BlockLanding"/>
        /// отдельно (или использовать авто-правило, которое учитывает <see cref="OwnerConfig.OccupationMode"/>).
        /// Если планета уже под этим оккупантом — no-op.</summary>
        public static void OccupyPlanet(PlanetData planet, StarData star, string occupierOwnerId, int currentTurn)
        {
            if (planet == null || string.IsNullOrEmpty(occupierOwnerId)) return;
            if (planet.Settlement.OccupiedByOwner == occupierOwnerId) return;
            string prev = GetControllingOwner(planet);
            planet.Settlement.OccupiedByOwner = occupierOwnerId;
            planet.Settlement.OccupationStartTurn = currentTurn;
            OnPlanetControlChanged?.Invoke(planet, prev);
            RecomputeSystemControl(star ?? FindStar(planet));
        }

        /// <summary>Снять оккупацию (планета возвращается к Owner). Также снимает LandingBlocked —
        /// освобождение убирает все блокировки. Если планета не оккупирована — no-op.</summary>
        public static void LiberatePlanet(PlanetData planet, StarData star, int currentTurn)
        {
            if (planet == null || string.IsNullOrEmpty(planet.Settlement.OccupiedByOwner)) return;
            string prev = planet.Settlement.OccupiedByOwner;
            planet.Settlement.OccupiedByOwner = null;
            planet.Settlement.OccupationStartTurn = 0;
            planet.Settlement.LandingBlocked = false;
            OnPlanetControlChanged?.Invoke(planet, prev);
            RecomputeSystemControl(star ?? FindStar(planet));
        }

        /// <summary>Запретить посадку не-членам контроллёра. Действует независимо от оккупации —
        /// родная сторона тоже может закрыть свою планету (карантин/эмбарго).</summary>
        public static void BlockLanding(PlanetData planet)
        {
            if (planet == null || planet.Settlement.LandingBlocked) return;
            planet.Settlement.LandingBlocked = true;
        }

        /// <summary>Снять блокировку посадки.</summary>
        public static void AllowLanding(PlanetData planet)
        {
            if (planet == null || !planet.Settlement.LandingBlocked) return;
            planet.Settlement.LandingBlocked = false;
        }

        /// <summary>Пересобирает <see cref="StarData.CurrentSystemController"/> и оповещает подписчиков
        /// при изменении. Учитываются только населённые планеты (Population &gt; 0, Race != None).
        /// Вызывается автоматически из Occupy/Liberate.</summary>
        public static void RecomputeSystemControl(StarData star)
        {
            if (star?.Planets == null) return;
            string uniform = null;
            bool started = false;
            bool mixed = false;
            for (int i = 0; i < star.Planets.Count; i++)
            {
                var p = star.Planets[i];
                if (p.Settlement.Population <= 0) continue;
                if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                string c = GetControllingOwner(p);
                if (!started) { uniform = c; started = true; }
                else if (uniform != c) { mixed = true; break; }
            }
            string newCtrl = (!started || mixed) ? null : uniform;
            if (newCtrl != star.CurrentSystemController)
            {
                string prev = star.CurrentSystemController;
                star.CurrentSystemController = newCtrl;
                OnSystemControlChanged?.Invoke(star, newCtrl, prev);

                // Проверка «сторона потеряла все свои системы во всей галактике».
                // Триггерится только при переходе к другому владельцу (prev был реальным).
                if (!string.IsNullOrEmpty(prev) && prev != newCtrl && !FactionHasAnySystem(prev))
                    OnFactionDefeated?.Invoke(prev);
            }
        }

        /// <summary>Есть ли хотя бы одна звезда во всей галактике под контролем указанной стороны.
        /// Использует <see cref="StarData.CurrentSystemController"/> — кэш, обновляемый при каждом
        /// изменении состава планет.</summary>
        private static bool FactionHasAnySystem(string ownerId)
        {
            var galaxy = GameWorld.GeneratedGalaxy;
            if (galaxy?.StarsMap == null || string.IsNullOrEmpty(ownerId)) return true;
            foreach (var s in galaxy.StarsMap.Values)
                if (s.CurrentSystemController == ownerId) return true;
            return false;
        }

        /// <summary>Режим оккупации стороны из OwnerConfig ("Full"|"Partial"). Дефолт — Partial.</summary>
        public static string GetOccupationMode(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return "Partial";
            var owners = GameWorld.Context?.AvailableOwners;
            if (owners != null && owners.TryGetValue(ownerId, out var oc) && !string.IsNullOrEmpty(oc.OccupationMode))
                return oc.OccupationMode;
            return "Partial";
        }

        private static StarData FindStar(PlanetData planet)
        {
            var galaxy = GameWorld.GeneratedGalaxy;
            if (galaxy?.StarsMap == null) return null;
            foreach (var star in galaxy.StarsMap.Values)
                for (int i = 0; i < star.Planets.Count; i++)
                    if (star.Planets[i] == planet) return star;
            return null;
        }
    }
}
