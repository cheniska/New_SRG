using UnityEngine;
using System.Collections.Generic;
using SRG.Config;
using SRG.Core;

namespace SRG.Galaxy.Politics
{
    /// <summary>
    /// Категории отношений (шкала 1..100, разбита на 5 равных диапазонов по 20).
    /// Hostile: 1..20, Bad: 21..40, Normal: 41..60, Good: 61..80, Best: 81..100.
    /// </summary>
    public enum RelationLevel
    {
        Hostile = 0,
        Bad = 1,
        Normal = 2,
        Good = 3,
        Best = 4
    }

    public class OwnerRaceRelationsManager : MonoBehaviour
    {
        public static OwnerRaceRelationsManager Instance { get; private set; }

        // Уровень считается «враждой» — атакуется и используется AI как hostile.
        public const int HOSTILE_MAX = 20;     // ≤ 20 — Hostile
        public const int BAD_MAX = 40;         // 21..40 — Bad
        public const int NORMAL_MAX = 60;     // 41..60 — Neutral
        public const int GOOD_MAX = 80;        // 61..80 — Good
        // 81..100 — Excellent
        public const int MIN_VALUE = 1;
        public const int MAX_VALUE = 100;

        private readonly Dictionary<string, int> _ownerInternalRelation = new();
        private readonly Dictionary<string, int> _ownerRelations = new();
        private readonly Dictionary<string, int> _raceRelations = new();

        // Персональные дельты между конкретными сущностями (ship/planet/star UID).
        // Sparse: запись появляется только когда между парой что-то реально произошло.
        // Применяется поверх фракционной базы: effective = clamp(base + delta, 1..100).
        // Бэкенд хранения — GalaxyData.PersonalRelations (чтобы сохранение/загрузка работали
        // через стандартный GalaxySaveManager). Если galaxy не доступна — используется fallback.
        private readonly Dictionary<string, int> _personalRelationsFallback = new();

        // Кэш AreHostile только для фракционной части — ключ (ownerA, ownerB, raceA, raceB).
        // Пары с персональной дельтой считаются напрямую (их мало и дельты часто меняются),
        // поэтому кэш не растёт по UID-парам и не нуждается в инвалидации при изменении дельт.
        private readonly Dictionary<(string, string, string, string), bool> _hostileCache = new();

        private bool _initialized = false;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void Initialize(GalaxyConfig cfg)
        {
            _ownerInternalRelation.Clear();
            _ownerRelations.Clear();
            _raceRelations.Clear();
            _hostileCache.Clear();
            _personalRelationsFallback.Clear();

            if (cfg == null)
            {
                Debug.LogWarning("[OwnerRaceRelationsManager] GalaxyConfig is null — инициализация пропущена.");
                return;
            }

            int ownerCount = 0;
            if (cfg.Ships?.Owners != null)
                foreach (var (ownerId, ownerCfg) in cfg.Ships.Owners)
                {
                    _ownerInternalRelation[ownerId] = ownerCfg.DefaultInternalRelation;
                    ownerCount++;
                }

            int ownerRelCount = 0;
            if (cfg.OwnerRelations != null)
                foreach (var (key, value) in cfg.OwnerRelations)
                {
                    _ownerRelations[NormalizeKey(key)] = value;
                    ownerRelCount++;
                }

            int raceRelCount = 0;
            if (cfg.RaceRelations != null)
                foreach (var (key, value) in cfg.RaceRelations)
                {
                    _raceRelations[NormalizeKey(key)] = value;
                    raceRelCount++;
                }

            _initialized = true;

            Debug.Log($"[OwnerRaceRelationsManager] Инициализировано: " +
                      $"{ownerCount} владельцев, {ownerRelCount} пар отношений владельцев, " +
                      $"{raceRelCount} пар отношений рас");
        }

        // ---------- Конвертация значения в категорию ----------

        public static RelationLevel ToLevel(int value)
        {
            if (value <= HOSTILE_MAX) return RelationLevel.Hostile;
            if (value <= BAD_MAX) return RelationLevel.Bad;
            if (value <= NORMAL_MAX) return RelationLevel.Normal;
            if (value <= GOOD_MAX) return RelationLevel.Good;
            return RelationLevel.Best;
        }

        /// <summary>Середина диапазона категории — используется при SetLevel.</summary>
        public static int LevelToMidValue(RelationLevel lvl) => lvl switch
        {
            RelationLevel.Hostile   => 10,
            RelationLevel.Bad       => 30,
            RelationLevel.Normal   => 50,
            RelationLevel.Good      => 70,
            RelationLevel.Best => 90,
            _ => 50
        };

        /// <summary>Минимальное значение, попадающее в категорию (для RaiseToLevel).</summary>
        public static int LevelMinValue(RelationLevel lvl) => lvl switch
        {
            RelationLevel.Hostile   => MIN_VALUE,
            RelationLevel.Bad       => HOSTILE_MAX + 1,
            RelationLevel.Normal   => BAD_MAX + 1,
            RelationLevel.Good      => NORMAL_MAX + 1,
            RelationLevel.Best => GOOD_MAX + 1,
            _ => 1
        };

        /// <summary>Максимальное значение, попадающее в категорию (для LowerToLevel).</summary>
        public static int LevelMaxValue(RelationLevel lvl) => lvl switch
        {
            RelationLevel.Hostile   => HOSTILE_MAX,
            RelationLevel.Bad       => BAD_MAX,
            RelationLevel.Normal   => NORMAL_MAX,
            RelationLevel.Good      => GOOD_MAX,
            RelationLevel.Best => MAX_VALUE,
            _ => 100
        };

        // ---------- Базовое (фракционное) отношение ----------

        /// <summary>
        /// Базовое фракционное отношение по Owner+Race (без учёта персональной дельты по UID).
        /// </summary>
        public int GetFactionRelation(string ownerA, string ownerB, string raceA = null, string raceB = null)
        {
            if (!_initialized) return 50;

            bool ownerAValid = !string.IsNullOrEmpty(ownerA)
                && ownerA != GalaxyConstants.OWNER_NONE_KEY
                && ownerA != GalaxyConstants.OWNER_MIXED_KEY;
            bool ownerBValid = !string.IsNullOrEmpty(ownerB)
                && ownerB != GalaxyConstants.OWNER_NONE_KEY
                && ownerB != GalaxyConstants.OWNER_MIXED_KEY;

            bool raceAValid = !string.IsNullOrEmpty(raceA)
                && raceA != GalaxyConstants.RACE_NONE_KEY
                && raceA != GalaxyConstants.RACE_MIXED_KEY;
            bool raceBValid = !string.IsNullOrEmpty(raceB)
                && raceB != GalaxyConstants.RACE_NONE_KEY
                && raceB != GalaxyConstants.RACE_MIXED_KEY;

            if (ownerAValid && ownerBValid)
            {
                string oKey = NormalizeKey(ownerA, ownerB);
                if (_ownerRelations.TryGetValue(oKey, out int ownerRel))
                    return ownerRel;

                if (ownerA == ownerB)
                {
                    // Одна раса внутри одного владельца — всегда союзники; internal relation
                    // задаёт отношения между разными расами владельца (Dominators = 0 → войны
                    // между RaceDominators1/2/3, но не внутри одной расы).
                    if (raceAValid && raceBValid)
                    {
                        if (raceA == raceB) return 100;
                        string rKey = NormalizeKey(raceA, raceB);
                        if (_raceRelations.TryGetValue(rKey, out int raceRel))
                            return raceRel;
                    }
                    if (_ownerInternalRelation.TryGetValue(ownerA, out int internalRel))
                        return internalRel;
                    return 100;
                }
            }

            if (raceAValid && raceBValid)
            {
                string rKey = NormalizeKey(raceA, raceB);
                if (_raceRelations.TryGetValue(rKey, out int raceRel))
                    return raceRel;
                if (raceA == raceB) return 100;
            }

            // Дефолт: незнакомые фракции — нейтральны (50), а не 0.
            return 50;
        }

        // Старая сигнатура для обратной совместимости (только фракция).
        public int GetRelation(string ownerA, string ownerB, string raceA = null, string raceB = null)
            => GetFactionRelation(ownerA, ownerB, raceA, raceB);

        // ---------- Эффективное отношение (фракция + персональная дельта) ----------

        /// <summary>
        /// Эффективное отношение с учётом персональной дельты для конкретной пары UID.
        /// Если uidA/uidB пусты — то же что GetFactionRelation.
        /// </summary>
        public int GetRelation(string uidA, string ownerA, string raceA,
                               string uidB, string ownerB, string raceB)
        {
            int baseRel = GetFactionRelation(ownerA, ownerB, raceA, raceB);
            int delta = GetPersonalDelta(uidA, uidB);
            return Mathf.Clamp(baseRel + delta, MIN_VALUE, MAX_VALUE);
        }

        public int GetRelation(ShipData a, ShipData b)
        {
            if (a == null || b == null) return 50;
            // Маскировка: если a/b — замаскированный корабль, то для второй стороны его Owner/Race
            // подменяются (в пределах ObserverSeesDisguise). См. SRG.Ships.Disguise.DisguiseService.
            string ownerA = SRG.Ships.Disguise.DisguiseService.GetEffectiveOwner(a, b);
            string raceA  = SRG.Ships.Disguise.DisguiseService.GetEffectiveRace(a, b);
            string ownerB = SRG.Ships.Disguise.DisguiseService.GetEffectiveOwner(b, a);
            string raceB  = SRG.Ships.Disguise.DisguiseService.GetEffectiveRace(b, a);
            return GetRelation(a.Uid, ownerA, raceA, b.Uid, ownerB, raceB);
        }

        public int GetRelation(ShipData ship, PlanetData planet)
        {
            if (ship == null || planet == null) return 50;
            // Оккупация: если планета оккупирована — отношение считается к оккупанту, не к родному Owner.
            // Родная сторона планеты остаётся Race (гражданское население не меняется), но политический
            // контроль над территорией — у оккупанта.
            string effectiveOwner = OccupationService.GetControllingOwner(planet);
            return GetRelation(ship.Uid, ship.Owner, ship.Race, planet.Uid, effectiveOwner, planet.Race);
        }

        public int GetRelation(PlanetData a, PlanetData b)
        {
            if (a == null || b == null) return 50;
            return GetRelation(a.Uid, a.Owner, a.Race, b.Uid, b.Owner, b.Race);
        }

        public RelationLevel GetLevel(ShipData a, ShipData b)        => ToLevel(GetRelation(a, b));
        public RelationLevel GetLevel(ShipData a, PlanetData b)      => ToLevel(GetRelation(a, b));
        public RelationLevel GetLevel(PlanetData a, PlanetData b)    => ToLevel(GetRelation(a, b));

        // ---------- AreHostile ----------

        /// <summary>Враждебны = категория Hostile (≤ 20).</summary>
        public bool AreHostile(string ownerA, string ownerB, string raceA = null, string raceB = null)
            => AreHostile(null, ownerA, raceA, null, ownerB, raceB);

        public bool AreHostile(string uidA, string ownerA, string raceA,
                               string uidB, string ownerB, string raceB)
        {
            // Персональная дельта — редкость: пары с ней считаем без кэша.
            // store.Count == 0 (типичный случай) — вообще без аллокации ключа.
            var store = GetPersonalStore();
            if (store.Count > 0 && !string.IsNullOrEmpty(uidA) && !string.IsNullOrEmpty(uidB) && uidA != uidB
                && store.TryGetValue(NormalizeKey(uidA, uidB), out int delta) && delta != 0)
                return GetRelation(uidA, ownerA, raceA, uidB, ownerB, raceB) <= HOSTILE_MAX;

            var key = (ownerA ?? "", ownerB ?? "", raceA ?? "", raceB ?? "");
            if (!_hostileCache.TryGetValue(key, out bool result))
            {
                result = GetFactionRelation(ownerA, ownerB, raceA, raceB) <= HOSTILE_MAX;
                _hostileCache[key] = result;
            }
            return result;
        }

        public bool AreHostile(ShipData a, ShipData b)
        {
            if (a == null || b == null) return false;
            // См. пояснение выше в GetRelation(ShipData, ShipData).
            string ownerA = SRG.Ships.Disguise.DisguiseService.GetEffectiveOwner(a, b);
            string raceA  = SRG.Ships.Disguise.DisguiseService.GetEffectiveRace(a, b);
            string ownerB = SRG.Ships.Disguise.DisguiseService.GetEffectiveOwner(b, a);
            string raceB  = SRG.Ships.Disguise.DisguiseService.GetEffectiveRace(b, a);
            return AreHostile(a.Uid, ownerA, raceA, b.Uid, ownerB, raceB);
        }

        public bool AreHostile(ShipData ship, PlanetData planet)
        {
            if (ship == null || planet == null) return false;
            string effectiveOwner = OccupationService.GetControllingOwner(planet);
            return AreHostile(ship.Uid, ship.Owner, ship.Race, planet.Uid, effectiveOwner, planet.Race);
        }

        public bool AreHostile(PlanetData a, PlanetData b)
            => a != null && b != null && AreHostile(a.Uid, a.Owner, a.Race, b.Uid, b.Owner, b.Race);

        // ---------- Управление фракционным отношением ----------

        public void SetOwnerRelation(string ownerA, string ownerB, int value)
        {
            _ownerRelations[NormalizeKey(ownerA, ownerB)] = Mathf.Clamp(value, MIN_VALUE, MAX_VALUE);
            _hostileCache.Clear();
        }

        /// <summary>
        /// Скорректировать отношение между владельцами на delta (с клампом 1..100).
        /// Если пара не зафиксирована — берётся текущее по GetFactionRelation как базовое.
        /// </summary>
        public void AdjustOwnerRelation(string ownerA, string ownerB, int delta)
        {
            if (string.IsNullOrEmpty(ownerA) || string.IsNullOrEmpty(ownerB)) return;
            if (ownerA == ownerB) return;
            if (ownerA == GalaxyConstants.OWNER_NONE_KEY || ownerB == GalaxyConstants.OWNER_NONE_KEY) return;
            if (ownerA == GalaxyConstants.OWNER_MIXED_KEY || ownerB == GalaxyConstants.OWNER_MIXED_KEY) return;

            string key = NormalizeKey(ownerA, ownerB);
            if (!_ownerRelations.TryGetValue(key, out int current))
                current = GetFactionRelation(ownerA, ownerB);
            _ownerRelations[key] = Mathf.Clamp(current + delta, MIN_VALUE, MAX_VALUE);
            _hostileCache.Clear();
        }

        public void SetRaceRelation(string raceA, string raceB, int value)
        {
            _raceRelations[NormalizeKey(raceA, raceB)] = Mathf.Clamp(value, MIN_VALUE, MAX_VALUE);
            _hostileCache.Clear();
        }

        // ---------- Управление персональной дельтой ----------

        private Dictionary<string, int> GetPersonalStore()
        {
            var galaxy = GalaxyManager.Instance?.GeneratedGalaxy;
            return galaxy != null ? galaxy.PersonalRelations : _personalRelationsFallback;
        }

        public int GetPersonalDelta(string uidA, string uidB)
        {
            if (string.IsNullOrEmpty(uidA) || string.IsNullOrEmpty(uidB) || uidA == uidB) return 0;
            var store = GetPersonalStore();
            return store.TryGetValue(NormalizeKey(uidA, uidB), out int d) ? d : 0;
        }

        public void SetPersonalDelta(string uidA, string uidB, int delta)
        {
            if (string.IsNullOrEmpty(uidA) || string.IsNullOrEmpty(uidB) || uidA == uidB) return;
            int clamped = Mathf.Clamp(delta, -99, 99);
            var store = GetPersonalStore();
            string key = NormalizeKey(uidA, uidB);
            if (clamped == 0) store.Remove(key);
            else store[key] = clamped;
            // Кэш враждебности чисто фракционный — персональные дельты его не затрагивают.
        }

        public void AdjustPersonalDelta(string uidA, string uidB, int change)
        {
            if (string.IsNullOrEmpty(uidA) || string.IsNullOrEmpty(uidB) || uidA == uidB) return;
            SetPersonalDelta(uidA, uidB, GetPersonalDelta(uidA, uidB) + change);
        }

        // ---------- Save/Load (legacy API — фракционные карты вне GalaxyData) ----------

        public (Dictionary<string, int> ownerRels, Dictionary<string, int> raceRels) GetSaveData()
            => (new Dictionary<string, int>(_ownerRelations), new Dictionary<string, int>(_raceRelations));

        public void LoadSaveData(Dictionary<string, int> ownerRels, Dictionary<string, int> raceRels)
        {
            if (ownerRels != null)
                foreach (var (key, value) in ownerRels)
                    _ownerRelations[NormalizeKey(key)] = Mathf.Clamp(value, MIN_VALUE, MAX_VALUE);
            if (raceRels != null)
                foreach (var (key, value) in raceRels)
                    _raceRelations[NormalizeKey(key)] = Mathf.Clamp(value, MIN_VALUE, MAX_VALUE);
            _hostileCache.Clear();
        }

        /// <summary>Сброс кэша враждебности (вызывать после ручных правок фракционных карт извне;
        /// для персональных дельт не требуется — они не кэшируются).</summary>
        public void InvalidateCache() => _hostileCache.Clear();

        private static string NormalizeKey(string a, string b)
            => string.CompareOrdinal(a, b) <= 0 ? $"{a}-{b}" : $"{b}-{a}";
        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            int dashIdx = key.IndexOf('-');
            if (dashIdx < 0) return key;
            string a = key.Substring(0, dashIdx);
            string b = key.Substring(dashIdx + 1);
            return NormalizeKey(a, b);
        }
    }
}
