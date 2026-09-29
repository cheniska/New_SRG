using System;
using System.Collections.Generic;
using SRG.Core;
using SRG.Galaxy;
using SRG.Ships.Services;

namespace SRG.Ships.Disguise
{
    /// <summary>
    /// Резольвер маскировки — единственная точка правды на вопрос
    /// «как observer видит замаскированный корабль?».
    ///
    /// Используется:
    ///  • <see cref="Galaxy.Politics.OwnerRaceRelationsManager"/> — при вычислении враждебности/отношения;
    ///  • <see cref="Combat.WeaponSystem"/> — при регистрации попадания (TryDetect);
    ///  • UI/приветствия — при выборе диалоговой ветки.
    /// </summary>
    public static class DisguiseService
    {
        /// <summary>ShipTypeId, у которых маска не работает — они видят игрока «в лицо»
        /// независимо от маскировки. Настраивается через <see cref="ImmuneShipTypes"/>.</summary>
        private static readonly HashSet<string> _immuneShipTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Ranger", "Pirate", "Transport", "Liner", "Diplomat"
        };

        /// <summary>Возвращает список ShipTypeId, которых маскировка не обманывает.</summary>
        public static IReadOnlyCollection<string> ImmuneShipTypes => _immuneShipTypes;

        /// <summary>Добавить ShipTypeId в список необманываемых.</summary>
        public static void AddImmuneShipType(string shipTypeId)
        {
            if (!string.IsNullOrEmpty(shipTypeId)) _immuneShipTypes.Add(shipTypeId);
        }

        /// <summary>Заменить список необманываемых полностью.</summary>
        public static void SetImmuneShipTypes(IEnumerable<string> shipTypeIds)
        {
            _immuneShipTypes.Clear();
            if (shipTypeIds == null) return;
            foreach (var s in shipTypeIds)
                if (!string.IsNullOrEmpty(s)) _immuneShipTypes.Add(s);
        }

        /// <summary>Активна ли маскировка на корабле.</summary>
        public static bool IsDisguised(ShipData ship) => ship?.Disguise != null;

        /// <summary>Видит ли <paramref name="observer"/> <paramref name="ship"/> как замаскированного
        /// (т.е. эффективный Owner/Race подменяются)? False, если:
        ///  • маска не надета,
        ///  • observer — ShipTypeId из <see cref="ImmuneShipTypes"/>,
        ///  • observer — партнёр/наёмник Player'а (PartnerLeaderUid),
        ///  • раса observer'а уже в <see cref="DisguiseState.DetectedByRaces"/>.</summary>
        public static bool ObserverSeesDisguise(ShipData ship, ShipData observer)
        {
            if (ship?.Disguise == null || observer == null) return false;
            if (ReferenceEquals(ship, observer)) return false;
            if (observer.Race != null && ship.Disguise.DetectedByRaces.Contains(observer.Race)) return false;
            if (!string.IsNullOrEmpty(observer.PartnerLeaderUid) && observer.PartnerLeaderUid == ship.Uid) return false;
            if (!string.IsNullOrEmpty(observer.ShipTypeId) && _immuneShipTypes.Contains(observer.ShipTypeId)) return false;
            return true;
        }

        /// <summary>Эффективный Owner корабля с точки зрения observer'а. Возвращает оригинал,
        /// если маскировка не «действует» на этого наблюдателя.</summary>
        public static string GetEffectiveOwner(ShipData ship, ShipData observer)
        {
            if (!ObserverSeesDisguise(ship, observer)) return ship?.Owner;
            var d = ship.Disguise;
            return string.IsNullOrEmpty(d.TargetOwner) ? ship.Owner : d.TargetOwner;
        }

        /// <summary>Эффективная Race корабля с точки зрения observer'а.</summary>
        public static string GetEffectiveRace(ShipData ship, ShipData observer)
        {
            if (!ObserverSeesDisguise(ship, observer)) return ship?.Race;
            var d = ship.Disguise;
            return string.IsNullOrEmpty(d.TargetRace) ? ship.Race : d.TargetRace;
        }

        /// <summary>Пометить, что раса <paramref name="detectedRace"/> раскрыла маску.
        /// Ничего не делает, если маска не надета. Возвращает true, если пометка
        /// произведена именно этим вызовом (не была установлена ранее).</summary>
        public static bool TryDetect(ShipData ship, string detectedRace)
        {
            if (ship?.Disguise == null || string.IsNullOrEmpty(detectedRace)) return false;
            if (ship.Disguise.DetectedByRaces.Contains(detectedRace)) return false;
            // Раскрытие только той расы, под которую маскируемся: остальные не «знают друг друга».
            // Если камуфляж без TargetRace (только Owner) — раскрытие ловится, но касается только
            // конкретной расы наблюдателя.
            var target = ship.Disguise.TargetRace;
            if (!string.IsNullOrEmpty(target) && !string.Equals(target, detectedRace, StringComparison.Ordinal))
                return false;
            ship.Disguise.DetectedByRaces.Add(detectedRace);
            return true;
        }

        /// <summary>Активировать маскировку на корабле. Если в звезде уже присутствуют корабли
        /// расы-цели — DetectedBy для этой расы взводится немедленно (нельзя переоблачиться
        /// на глазах у противника). Возвращает true при успехе.</summary>
        public static bool Activate(ShipData ship, DisguiseState state)
        {
            if (ship == null || state == null) return false;
            if (ship.Disguise != null) Deactivate(ship);

            state.SavedVisualPath = ship.CustomBodyGraphicPath;
            state.DetectedByRaces ??= new HashSet<string>();
            ship.Disguise = state;

            if (!string.IsNullOrEmpty(state.VisualPath))
                ShipVisualService.SetOverride(ship, state.VisualPath);

            // «На глазах» — если в текущей звезде есть корабль расы-цели, маска мгновенно
            // раскрывается для этой расы. Учитываются только живые корабли (CurrentHull > 0).
            if (!string.IsNullOrEmpty(state.TargetRace))
            {
                var star = ship.CurrentStar;
                if (star == null && !string.IsNullOrEmpty(ship.CurrentStarUid))
                    GalaxyManager.Instance?.GeneratedGalaxy?.StarsMap.TryGetValue(ship.CurrentStarUid, out star);
                if (star != null)
                {
                    var ships = star.Ships;
                    for (int i = 0; i < ships.Count; i++)
                    {
                        var s = ships[i];
                        if (s == null || s == ship) continue;
                        if (s.CurrentHull <= 0) continue;
                        if (string.IsNullOrEmpty(s.Race) || s.Race != state.TargetRace) continue;
                        state.DetectedByRaces.Add(s.Race);
                        break;
                    }
                }
            }

            return true;
        }

        /// <summary>Снять маскировку и вернуть исходную графику. DetectedBy сбрасывается
        /// (живёт в самом объекте DisguiseState, который здесь удаляется).</summary>
        public static void Deactivate(ShipData ship)
        {
            if (ship?.Disguise == null) return;
            var saved = ship.Disguise.SavedVisualPath;
            ship.Disguise = null;
            ShipVisualService.SetOverride(ship, string.IsNullOrEmpty(saved) ? null : saved);
        }
    }
}
