using System;
using System.Collections.Generic;
using UnityEngine;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI.Spawning;
using SRG.Ships;

namespace SRG.NpcAI
{
    /// <summary>
    /// Fluent-builder для поиска кораблей в звезде. Единая точка для будущих индексов
    /// (материализованные списки в <see cref="GalaxyShipCounters"/>) и spatial hash.
    /// До июля 2026 в NpcBrain/NpcTargeting/ActionEscort жили 8 узкоспециализированных
    /// Find*-методов, каждый с собственным <c>foreach star.Ships</c>.
    ///
    /// Использование:
    /// <code>
    ///   var target = ShipQuery.In(star)
    ///       .HostileTo(ship)
    ///       .Within(ship.Position, radius)
    ///       .OrderByDescending(scoreFn)
    ///       .FirstOrDefault();
    /// </code>
    ///
    /// Оптимизация: если задан HostileTo(ship) — итерация идёт по владельцам через
    /// <c>Counters.OwnersAtStar</c>, а не по всему <c>star.Ships</c>. Это ускоряет
    /// сцены с 40+ кораблей, где враждебных обычно 1–2 владельца.
    /// </summary>
    public struct ShipQuery
    {
        private StarData _star;
        private ShipData _self;              // исключается из результата
        private ShipData _hostileTo;         // фильтр AreHostile(self, s)
        private ShipData _friendlyTo;        // фильтр !AreHostile(self, s)
        private Vector2 _center;
        private float _maxDistSq;            // 0 = без radius-фильтра; иначе sqrDist(pos, center) < этот
        private bool _hasCenter;
        private bool _aliveOnly;             // default true (см. In())
        private bool _excludeLanded;         // корабли пристыкованные к носителю (LandedOnShipUid) — обычно скрыты
        private CombatClass? _combatClass;
        private Func<ShipData, bool> _predicate;

        public static ShipQuery In(StarData star)
            => new ShipQuery { _star = star, _aliveOnly = true, _excludeLanded = true };

        /// <summary>Исключить конкретный корабль (обычно себя).</summary>
        public ShipQuery Excluding(ShipData self) { _self = self; return this; }

        /// <summary>Только враждебные к self (AreHostile==true).</summary>
        public ShipQuery HostileTo(ShipData self) { _hostileTo = self; if (_self == null) _self = self; return this; }

        /// <summary>Только не-враждебные (в т.ч. союзники и нейтралы).</summary>
        public ShipQuery FriendlyTo(ShipData self) { _friendlyTo = self; if (_self == null) _self = self; return this; }

        /// <summary>Радиус-фильтр по квадрату дистанции (без sqrt).</summary>
        public ShipQuery Within(Vector2 center, float radius)
        {
            _center = center;
            _maxDistSq = radius * radius;
            _hasCenter = true;
            return this;
        }

        /// <summary>Только кораблям указанного боевого класса.</summary>
        public ShipQuery OfClass(CombatClass cls) { _combatClass = cls; return this; }

        /// <summary>Разрешить мёртвых (обычно false).</summary>
        public ShipQuery IncludeDead() { _aliveOnly = false; return this; }

        /// <summary>Разрешить пристыкованных к носителю (обычно false).</summary>
        public ShipQuery IncludeLanded() { _excludeLanded = false; return this; }

        /// <summary>Дополнительный пользовательский фильтр.</summary>
        public ShipQuery Where(Func<ShipData, bool> predicate) { _predicate = predicate; return this; }

        // ── Терминалы ────────────────────────────────────────────────────────────

        public ShipData FirstOrDefault()
        {
            foreach (var s in Enumerate()) return s;
            return null;
        }

        public ShipData Nearest(Vector2 from)
        {
            ShipData best = null;
            float bestSq = float.MaxValue;
            foreach (var s in Enumerate())
            {
                float d = (s.Position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            return best;
        }

        public ShipData Best(Func<ShipData, float> score)
        {
            ShipData best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var s in Enumerate())
            {
                float sc = score(s);
                if (sc > bestScore) { bestScore = sc; best = s; }
            }
            return best;
        }

        public int Count()
        {
            int n = 0;
            foreach (var _ in Enumerate()) n++;
            return n;
        }

        public List<ShipData> ToList()
        {
            var list = new List<ShipData>();
            foreach (var s in Enumerate()) list.Add(s);
            return list;
        }

        // ── Основной итератор ────────────────────────────────────────────────────

        public IEnumerable<ShipData> Enumerate()
        {
            if (_star == null) yield break;
            var rel = OwnerRaceRelationsManager.Instance;

            // Fast path: HostileTo(x) + материализованные списки → перебираем только владельцев,
            // враждебных x. Экономит O(star.Ships) когда враждебен 1–2 owner.
            if (_hostileTo != null && rel != null)
            {
                var owners = SpawnSystem.Counters?.GetOwnersAtStar(_star.Uid);
                if (owners != null && owners.Count > 0)
                {
                    for (int oi = 0; oi < owners.Count; oi++)
                    {
                        string ownerId = owners[oi];
                        if (ownerId == _hostileTo.Owner) continue; // свои не враги
                        // owner-level check дешевле per-ship AreHostile; отсекает большинство пар одним lookup'ом.
                        if (!rel.AreHostile(_hostileTo.Owner, ownerId, _hostileTo.Race, null)) continue;
                        var list = SpawnSystem.Counters.GetShipsAtStarByOwner(_star.Uid, ownerId);
                        if (list == null) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var s = list[i];
                            if (!PassesCommon(s)) continue;
                            // Персональная дельта могла сделать конкретный корабль неврагом:
                            // перепроверяем per-ship.
                            if (!rel.AreHostile(_hostileTo, s)) continue;
                            yield return s;
                        }
                    }
                    yield break;
                }
                // Fallback (индексы не готовы) — общий путь.
            }

            var ships = _star.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (!PassesCommon(s)) continue;
                if (_hostileTo != null && (rel == null || !rel.AreHostile(_hostileTo, s))) continue;
                if (_friendlyTo != null && rel != null && rel.AreHostile(_friendlyTo, s)) continue;
                yield return s;
            }
        }

        private bool PassesCommon(ShipData s)
        {
            if (s == null) return false;
            if (_self != null && s == _self) return false;
            if (_aliveOnly && s.CurrentHull <= 0) return false;
            if (_excludeLanded && !string.IsNullOrEmpty(s.LandedOnShipUid)) return false;
            if (_hasCenter && (s.Position - _center).sqrMagnitude > _maxDistSq) return false;
            if (_combatClass.HasValue && NpcBrain.ResolveCombatClass(s.ShipTypeId) != _combatClass.Value) return false;
            if (_predicate != null && !_predicate(s)) return false;
            return true;
        }
    }
}
