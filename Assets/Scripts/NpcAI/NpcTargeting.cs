using UnityEngine;
using SRG.Config;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Actions;

namespace SRG.NpcAI
{
    /// <summary>
    /// Общие функции поиска враждебных целей в звезде. Единая точка вместо локальных копий
    /// в NpcBrain / ActionDefend / ActionPartnerAttend / DirectiveAttackSystem.
    /// Враждебность — по OwnerRaceRelationsManager.AreHostile (фракция + персональная дельта).
    /// </summary>
    public static class NpcTargeting
    {
        /// <summary>Ближайший к fromPos враждебный self корабль. maxDistSq ограничивает радиус поиска
        /// (квадрат дистанции); float.MaxValue = вся система.</summary>
        public static ShipData FindNearestHostile(StarData star, ShipData self, Vector2 fromPos,
            float maxDistSq = float.MaxValue)
        {
            if (star == null || self == null) return null;
            // Radius=∞ → без spatial-hash пути (ShipQuery.HostileTo идёт через ownership-индекс).
            // Radius конечный → комбинируем ShipQuery + фильтр по расстоянию (индекс уже сузил
            // выборку по владельцу).
            if (float.IsPositiveInfinity(maxDistSq))
                return ShipQuery.In(star).HostileTo(self).Nearest(fromPos);

            float radius = Mathf.Sqrt(maxDistSq);
            return ShipQuery.In(star).HostileTo(self).Within(fromPos, radius).Nearest(fromPos);
        }

        public static string FindNearestHostileUid(StarData star, ShipData self, Vector2 fromPos,
            float maxDistSq = float.MaxValue)
            => FindNearestHostile(star, self, fromPos, maxDistSq)?.Uid;

        /// <summary>Первый попавшийся живой враждебный корабль (без сортировки по дистанции) —
        /// для проверок вида «есть ли вообще враг в системе».</summary>
        public static string FindAnyHostileUid(StarData star, ShipData self)
        {
            if (star == null || self == null) return null;
            return ShipQuery.In(star).HostileTo(self).FirstOrDefault()?.Uid;
        }

        // ── Поиск укрытий (используется fear-веткой AssessFear / ActionSeekShelter) ──

        /// <summary>Есть ли у корабля хоть одно рабочее оружие (по слотам корпуса).
        /// «Безоружен» → hard-trigger fear.</summary>
        public static bool HasWorkingWeapon(ShipData ship)
        {
            if (ship?.Equipment == null || ship.AllItems == null) return false;
            foreach (var slotKey in ship.GetSortedWeaponSlots())
            {
                string uid = ship.Equipment.GetItemUid(slotKey);
                if (uid != null && ship.AllItems.TryGetValue(uid, out var w) && w.IsWorking)
                    return true;
            }
            return false;
        }

        /// <summary>Ближайшая станция в звезде, не враждебная self (по OwnerRaceRelations).
        /// null если станций нет / все враждебны.</summary>
        public static ShipData FindFriendlyStationInStar(StarData star, ShipData self)
        {
            if (star?.Ships == null || self == null) return null;
            var rel = OwnerRaceRelationsManager.Instance;
            ShipData best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < star.Ships.Count; i++)
            {
                var s = star.Ships[i];
                if (s == null || !s.IsStation || s.CurrentHull <= 0) continue;
                if (rel != null && rel.AreHostile(self, s)) continue;
                float d = (s.Position - self.Position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            return best;
        }

        /// <summary>Ближайшая планета в звезде, на которую self может сесть: не враждебная,
        /// заселённая, LandingBlocked с чужим владельцем отсекается.</summary>
        public static PlanetData FindFriendlyPlanetInStar(StarData star, ShipData self)
        {
            if (star?.Planets == null || self == null) return null;
            var rel = OwnerRaceRelationsManager.Instance;
            PlanetData best = null;
            float bestSq = float.MaxValue;
            foreach (var p in star.Planets)
            {
                if (p == null) continue;
                if (string.IsNullOrEmpty(p.Race) || p.Race == GalaxyConstants.RACE_NONE_KEY) continue;
                if (p.Settlement == null || p.Settlement.Population <= 0) continue;
                if (rel != null && rel.AreHostile(self, p)) continue;
                if (p.Settlement.LandingBlocked
                    && OccupationService.GetControllingOwner(p) != self.Owner) continue;
                Vector2 pos = SRG.Utils.OrbitMath.GetPlanetPositionSimple(p);
                float d = (pos - self.Position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = p; }
            }
            return best;
        }

        /// <summary>Ближайшая соседняя система, где владелец не враждебен self (для «беги из звезды»).
        /// Использует прямую дистанцию по координатам звёзд — без Hyperjump-выборки маршрута.</summary>
        public static StarData FindNearestFriendlyStar(StarData currentStar, ShipData self, GalaxyData galaxy)
        {
            if (currentStar == null || self == null || galaxy?.StarsMap == null) return null;
            var rel = OwnerRaceRelationsManager.Instance;
            StarData best = null;
            float bestSq = float.MaxValue;
            foreach (var st in galaxy.StarsMap.Values)
            {
                if (st == null || st == currentStar) continue;
                string ctrl = st.CurrentSystemController;
                if (string.IsNullOrEmpty(ctrl)) continue;
                if (rel != null)
                {
                    // Дешевле проверка на уровне owner-owner: если контроллёр звезды враждебен owner'у self, пропускаем.
                    if (rel.AreHostile(self.Owner, ctrl)) continue;
                }
                float d = (st.Position - currentStar.Position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = st; }
            }
            return best;
        }
    }
}
