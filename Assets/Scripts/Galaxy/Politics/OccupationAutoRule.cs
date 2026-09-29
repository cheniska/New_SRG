using UnityEngine;
using SRG.Config;
using SRG.NpcAI;
using SRG.Ships;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Автоматическое правило захвата системы:
    /// - Full-режим оккупанта: система переходит под контроль, если в ней нет ни одного корабля кроме этой стороны.
    /// - Partial-режим оккупанта: достаточно, чтобы не было чужих боевых кораблей (гражданские других сторон допускаются).
    ///
    /// Захват = проход по всем населённым планетам системы:
    /// - если родная сторона планеты == кандидат → LiberatePlanet (возврат оккупированной)
    /// - иначе если оккупант враждебен кандидату → OccupyPlanet (+ BlockLanding при Full-режиме)
    ///
    /// Задержки нет — правило срабатывает мгновенно на том же ходу.
    /// Игрок в подсчёте кораблей игнорируется.
    ///
    /// Вызывается раз в ход из DirectiveManager.OnTurnCalculate (за компанию с директивами),
    /// чтобы не плодить дополнительные MonoBehaviour в сцене.
    /// </summary>
    public static class OccupationAutoRule
    {
        public static void Tick(GalaxyData galaxy)
        {
            if (galaxy?.StarsMap == null) return;
            int turn = galaxy.CurrentTurn;
            foreach (var star in galaxy.StarsMap.Values)
                ProcessStar(star, turn);
        }

        private static void ProcessStar(StarData star, int currentTurn)
        {
            // Одно сканирование Ships: определяем «единственный владелец всех кораблей» (для Full)
            // и «единственный владелец боевых кораблей» (для Partial).
            string soleOwner = null;
            bool multipleOwners = false;
            string soleCombatOwner = null;
            bool multipleCombatOwners = false;
            bool hasCombat = false;

            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s.CurrentHull <= 0) continue;
                if (s.IsPlayer) continue;
                string owner = s.Owner;
                if (string.IsNullOrEmpty(owner)
                    || owner == GalaxyConstants.OWNER_NONE_KEY
                    || owner == GalaxyConstants.OWNER_MIXED_KEY) continue;

                if (soleOwner == null) soleOwner = owner;
                else if (soleOwner != owner) multipleOwners = true;

                bool combat = NpcBrain.ResolveCombatClass(s.ShipTypeId) != CombatClass.Civilian;
                if (combat)
                {
                    hasCombat = true;
                    if (soleCombatOwner == null) soleCombatOwner = owner;
                    else if (soleCombatOwner != owner) multipleCombatOwners = true;
                }
            }

            // Кандидат в оккупанты
            string candidate = null;
            if (soleOwner != null && !multipleOwners)
            {
                // Все живые NPC-корабли — одной стороны. Подходит для любого режима.
                candidate = soleOwner;
            }
            else if (hasCombat && !multipleCombatOwners)
            {
                // Смешанная система, но боевые — одной стороны. Только Partial-режим сработает.
                string mode = OccupationService.GetOccupationMode(soleCombatOwner);
                if (string.Equals(mode, "Partial", System.StringComparison.OrdinalIgnoreCase))
                    candidate = soleCombatOwner;
            }

            if (candidate == null) return;
            // Пираты не воюют за территорию (у них нет ГШ — см. HighCommandRegistry.BootstrapFromGalaxy):
            // система, где остались одни пираты, не переходит под их контроль.
            if (candidate == "Pirates") return;

            var rel = OwnerRaceRelationsManager.Instance;
            if (rel == null) return;
            string candidateMode = OccupationService.GetOccupationMode(candidate);
            bool isFull = string.Equals(candidateMode, "Full", System.StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < star.Planets.Count; i++)
            {
                var p = star.Planets[i];
                if (p.Settlement.Population <= 0) continue;
                if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) continue;

                string controller = OccupationService.GetControllingOwner(p);
                if (controller == candidate) continue;
                // Захватываем только вражескую территорию — не воюем со своими и союзными.
                if (!rel.AreHostile(candidate, controller)) continue;

                if (candidate == p.Owner)
                {
                    // Родная сторона вернулась — освобождение (снимает и LandingBlocked).
                    OccupationService.LiberatePlanet(p, star, currentTurn);
                }
                else
                {
                    OccupationService.OccupyPlanet(p, star, candidate, currentTurn);
                    if (isFull) OccupationService.BlockLanding(p);
                }
            }
        }
    }
}
