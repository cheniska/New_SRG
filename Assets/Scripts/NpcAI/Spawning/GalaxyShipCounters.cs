using System.Collections.Generic;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.NpcAI.Spawning
{
    /// <summary>
    /// Инкрементальные счётчики живых кораблей и систем в галактике. Используются всеми политиками
    /// SpawnSystem для O(1) проверки cap/target.
    ///
    /// Семантика:
    ///   • Все словари обновляются ИНКРЕМЕНТАЛЬНО: при спавне → Inc*, при смерти → Dec*,
    ///     при смене Race/Owner у локации → пересчёт затронутых сторон/рас (см. <see cref="RecalculateFromScratch"/>).
    ///   • Старт игры / загрузка сохранения → один пересчёт O(N) через RecalculateFromScratch.
    ///
    /// Никаких хардкодированных сторон/типов — всё работает через строковые ключи. Модовые
    /// типы/стороны автоматически появляются в словарях при первом Inc.
    /// </summary>
    public class GalaxyShipCounters
    {
        // ──────────────────────────────────────────
        // Корабли
        // ──────────────────────────────────────────

        /// <summary>shipTypeId → число живых кораблей этого типа в галактике.</summary>
        public readonly Dictionary<string, int> ShipsByType = new();

        /// <summary>ownerId → число живых кораблей этой стороны.</summary>
        public readonly Dictionary<string, int> ShipsBySide = new();

        /// <summary>raceKey → число живых кораблей этой расы.</summary>
        public readonly Dictionary<string, int> ShipsByRace = new();

        /// <summary>(starUid, shipTypeId) → число живых кораблей данного типа в звезде.</summary>
        public readonly Dictionary<(string starUid, string shipTypeId), int> ShipsByStarAndType = new();

        /// <summary>(starUid, sideId) → число живых кораблей этой стороны в звезде.
        /// Используется в приветствиях планет и новостях (условия ShipsInCurStar/ToStar).</summary>
        public readonly Dictionary<(string starUid, string sideId), int> ShipsByStarAndSide = new();

        /// <summary>(planetUid, shipTypeId) → число живых кораблей данного типа, у которых HomePlanetUid == planet.</summary>
        public readonly Dictionary<(string planetUid, string shipTypeId), int> ShipsByPlanetAndType = new();

        /// <summary>(sectorUid, shipTypeId) → число живых кораблей данного типа в секторе.</summary>
        public readonly Dictionary<(string sectorUid, string shipTypeId), int> ShipsBySectorAndType = new();

        // ──────────────────────────────────────────
        // Материализованные списки — для ShipQuery / горячих сканеров NpcBrain / ActionEscort.
        // Ключ = (starUid, ownerId). Обновляется теми же хуками OnShipSpawned/OnShipDied и
        // OnShipMigrated (при миграции между звёздами). Не сериализуется — восстанавливается
        // в RecalculateFromScratch.
        // ──────────────────────────────────────────

        /// <summary>(starUid, ownerId) → живые корабли данной стороны в звезде.
        /// Используется ShipQuery.HostileTo / FriendlyTo для быстрого перебора кандидатов.</summary>
        public readonly Dictionary<(string starUid, string ownerId), List<ShipData>> ShipsAtStarByOwnerList = new();

        /// <summary>starUid → список ownerId, у которых есть живые корабли в звезде.
        /// Для итерации по владельцам без пробега всех Ships.</summary>
        public readonly Dictionary<string, List<string>> OwnersAtStar = new();

        // ──────────────────────────────────────────
        // Системы (звёзды)
        // ──────────────────────────────────────────

        /// <summary>Общее число обитаемых (Owner ≠ None) систем в галактике. Знаменатель доминации сторон.</summary>
        public int InhabitedSystemsCount;

        /// <summary>Число систем коалиции (Owner=Coalition). Знаменатель для target пиратов/рейнджеров.
        /// "Normals" в терминологии SR-HD.</summary>
        public int NormalSystemsCount;

        /// <summary>ownerId → число систем этого владельца.</summary>
        public readonly Dictionary<string, int> SystemsBySide = new();

        /// <summary>(ownerId, raceKey) → число систем (Owner=side AND Race=race). Для per-race доминации.</summary>
        public readonly Dictionary<(string ownerId, string raceKey), int> SystemsBySideAndRace = new();

        /// <summary>raceKey → число систем расы (через любого Owner). Используется доминатор-политикой.</summary>
        public readonly Dictionary<string, int> SystemsByRace = new();

        // ──────────────────────────────────────────
        // Crime
        // ──────────────────────────────────────────

        /// <summary>Средний CrimeRating по всем NPC галактики. Множитель target пиратов.</summary>
        public float AverageCrimeRating;

        // ──────────────────────────────────────────
        // API инкремента (вызывается из хуков)
        // ──────────────────────────────────────────

        public void OnShipSpawned(ShipData ship)
        {
            if (ship == null || ship.IsPlayer || ship.IsItem) return;
            AddShip(ship, +1);
        }

        public void OnShipDied(ShipData ship)
        {
            if (ship == null || ship.IsPlayer || ship.IsItem) return;
            AddShip(ship, -1);
        }

        private void AddShip(ShipData ship, int delta)
        {
            string type = ship.ShipTypeId ?? "Unknown";
            string side = ship.Owner ?? "None";
            string race = ship.Race ?? "None";
            Inc(ShipsByType, type, delta);
            Inc(ShipsBySide, side, delta);
            Inc(ShipsByRace, race, delta);
            if (!string.IsNullOrEmpty(ship.CurrentStarUid))
            {
                Inc(ShipsByStarAndType, (ship.CurrentStarUid, type), delta);
                Inc(ShipsByStarAndSide, (ship.CurrentStarUid, side), delta);
                if (delta > 0) AddToStarOwnerList(ship, ship.CurrentStarUid, side);
                else           RemoveFromStarOwnerList(ship, ship.CurrentStarUid, side);
            }
            if (!string.IsNullOrEmpty(ship.HomePlanetUid)) Inc(ShipsByPlanetAndType, (ship.HomePlanetUid, type), delta);
            // sector inferred при пересчёте; для inc нужен SectorUid у корабля либо lookup
        }

        /// <summary>Обновление материализованных списков при миграции корабля между звёздами
        /// (гиперпрыжок, teleport-консоль, player warp). Вызывать ДО присваивания нового CurrentStarUid.
        /// oldStarUid/newStarUid могут быть null — тогда сторона просто удаляется/добавляется.</summary>
        public void OnShipMigrated(ShipData ship, string oldStarUid, string newStarUid)
        {
            if (ship == null || ship.IsPlayer || ship.IsItem) return;
            if (oldStarUid == newStarUid) return;
            string type = ship.ShipTypeId ?? "Unknown";
            string side = ship.Owner ?? "None";
            if (!string.IsNullOrEmpty(oldStarUid))
            {
                Inc(ShipsByStarAndType, (oldStarUid, type), -1);
                Inc(ShipsByStarAndSide, (oldStarUid, side), -1);
                RemoveFromStarOwnerList(ship, oldStarUid, side);
            }
            if (!string.IsNullOrEmpty(newStarUid))
            {
                Inc(ShipsByStarAndType, (newStarUid, type), +1);
                Inc(ShipsByStarAndSide, (newStarUid, side), +1);
                AddToStarOwnerList(ship, newStarUid, side);
            }
        }

        private void AddToStarOwnerList(ShipData ship, string starUid, string side)
        {
            var key = (starUid, side);
            if (!ShipsAtStarByOwnerList.TryGetValue(key, out var list))
                ShipsAtStarByOwnerList[key] = list = new List<ShipData>(4);
            list.Add(ship);
            if (!OwnersAtStar.TryGetValue(starUid, out var owners))
                OwnersAtStar[starUid] = owners = new List<string>(4);
            if (!owners.Contains(side)) owners.Add(side);
        }

        private void RemoveFromStarOwnerList(ShipData ship, string starUid, string side)
        {
            var key = (starUid, side);
            if (!ShipsAtStarByOwnerList.TryGetValue(key, out var list)) return;
            list.Remove(ship);
            if (list.Count == 0)
            {
                ShipsAtStarByOwnerList.Remove(key);
                if (OwnersAtStar.TryGetValue(starUid, out var owners))
                {
                    owners.Remove(side);
                    if (owners.Count == 0) OwnersAtStar.Remove(starUid);
                }
            }
        }

        // ──────────────────────────────────────────
        // Полный пересчёт O(N) — старт игры / load
        // ──────────────────────────────────────────

        public void RecalculateFromScratch(GalaxyData galaxy)
        {
            ShipsByType.Clear(); ShipsBySide.Clear(); ShipsByRace.Clear();
            ShipsByStarAndType.Clear(); ShipsByStarAndSide.Clear();
            ShipsByPlanetAndType.Clear(); ShipsBySectorAndType.Clear();
            ShipsAtStarByOwnerList.Clear(); OwnersAtStar.Clear();
            SystemsBySide.Clear(); SystemsBySideAndRace.Clear(); SystemsByRace.Clear();
            InhabitedSystemsCount = 0; NormalSystemsCount = 0; AverageCrimeRating = 0f;

            if (galaxy == null) return;

            int crimeSamples = 0;
            float crimeSum = 0f;

            foreach (var sector in galaxy.Sectors)
            {
                if (sector == null) continue;
                string sectorUid = sector.Uid;

                foreach (var star in sector.Stars)
                {
                    if (star == null) continue;

                    // Системные счётчики
                    string starOwner = star.Owner ?? "None";
                    string starRace  = star.Race  ?? "None";
                    bool inhabited = starOwner != "None" && starOwner != GalaxyConstants.OWNER_UNRESOLVED_KEY;
                    if (inhabited)
                    {
                        InhabitedSystemsCount++;
                        if (starOwner == "Coalition") NormalSystemsCount++;
                        Inc(SystemsBySide, starOwner, +1);
                        Inc(SystemsByRace, starRace, +1);
                        Inc(SystemsBySideAndRace, (starOwner, starRace), +1);
                    }

                    // Корабли
                    foreach (var ship in star.Ships)
                    {
                        if (ship == null || ship.IsPlayer || ship.IsItem) continue;
                        if (ship.CurrentHull <= 0) continue;

                        string type = ship.ShipTypeId ?? "Unknown";
                        string side = ship.Owner ?? "None";
                        string race = ship.Race ?? "None";

                        Inc(ShipsByType, type, +1);
                        Inc(ShipsBySide, side, +1);
                        Inc(ShipsByRace, race, +1);
                        Inc(ShipsByStarAndType, (star.Uid, type), +1);
                        Inc(ShipsByStarAndSide, (star.Uid, side), +1);
                        AddToStarOwnerList(ship, star.Uid, side);
                        if (!string.IsNullOrEmpty(ship.HomePlanetUid))
                            Inc(ShipsByPlanetAndType, (ship.HomePlanetUid, type), +1);
                        Inc(ShipsBySectorAndType, (sectorUid, type), +1);

                        if (ship.Personality != null)
                        {
                            crimeSum += ship.Personality.CrimeRating;
                            crimeSamples++;
                        }
                    }
                }
            }

            AverageCrimeRating = crimeSamples > 0 ? crimeSum / crimeSamples : 0f;
        }

        /// <summary>Пересчёт только Crime (среднее по всем кораблям). Дешевле полного RecalculateFromScratch.</summary>
        public void RecalculateCrime(GalaxyData galaxy)
        {
            if (galaxy == null) { AverageCrimeRating = 0f; return; }
            int samples = 0;
            float sum = 0f;
            foreach (var sector in galaxy.Sectors)
                foreach (var star in sector.Stars)
                    foreach (var ship in star.Ships)
                    {
                        if (ship == null || ship.IsPlayer || ship.IsItem || ship.CurrentHull <= 0) continue;
                        if (ship.Personality == null) continue;
                        sum += ship.Personality.CrimeRating;
                        samples++;
                    }
            AverageCrimeRating = samples > 0 ? sum / samples : 0f;
        }

        // ──────────────────────────────────────────
        // Геттеры
        // ──────────────────────────────────────────

        public int GetShips(string shipTypeId)
            => ShipsByType.TryGetValue(shipTypeId ?? "", out var v) ? v : 0;

        public int GetShipsAtPlanet(string planetUid, string shipTypeId)
            => ShipsByPlanetAndType.TryGetValue((planetUid ?? "", shipTypeId ?? ""), out var v) ? v : 0;

        public int GetShipsAtStar(string starUid, string shipTypeId)
            => ShipsByStarAndType.TryGetValue((starUid ?? "", shipTypeId ?? ""), out var v) ? v : 0;

        /// <summary>Число живых кораблей стороны <paramref name="sideId"/> (=Owner) в звезде.
        /// Используется в приветствиях планет (ShipsInCurStar/ShipsInToStar).</summary>
        public int GetShipsAtStarBySide(string starUid, string sideId)
            => ShipsByStarAndSide.TryGetValue((starUid ?? "", sideId ?? ""), out var v) ? v : 0;

        public int GetShipsAtSector(string sectorUid, string shipTypeId)
            => ShipsBySectorAndType.TryGetValue((sectorUid ?? "", shipTypeId ?? ""), out var v) ? v : 0;

        public int GetSystemsOfSide(string ownerId)
            => SystemsBySide.TryGetValue(ownerId ?? "", out var v) ? v : 0;

        /// <summary>Живые корабли данной стороны в звезде. null → внешне трактуется как пусто.</summary>
        public List<ShipData> GetShipsAtStarByOwner(string starUid, string ownerId)
            => ShipsAtStarByOwnerList.TryGetValue((starUid ?? "", ownerId ?? ""), out var list) ? list : null;

        /// <summary>Список сторон с живыми кораблями в звезде. Для перебора «врагов кого-то» без
        /// пробега всех star.Ships.</summary>
        public List<string> GetOwnersAtStar(string starUid)
            => OwnersAtStar.TryGetValue(starUid ?? "", out var owners) ? owners : null;

        public int GetSystemsOfRace(string ownerId, string raceKey)
            => SystemsBySideAndRace.TryGetValue((ownerId ?? "", raceKey ?? ""), out var v) ? v : 0;

        // ──────────────────────────────────────────
        // Internal
        // ──────────────────────────────────────────

        private static void Inc<TKey>(Dictionary<TKey, int> dict, TKey key, int delta)
        {
            if (key == null) return;
            dict.TryGetValue(key, out var current);
            int updated = current + delta;
            if (updated <= 0) dict.Remove(key);
            else dict[key] = updated;
        }
    }
}
