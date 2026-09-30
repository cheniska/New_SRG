using System.Collections.Generic;
using SRG.Combat;

namespace SRG.Equipment
{
    /// <summary>
    /// Реестр допустимых значений для полей <see cref="EmbedConfig"/> — bonus keys
    /// (по категориям), WeaponFlags, WeaponEffectTypes. Расширяется в рантайме через
    /// <c>Register*</c>-методы: моды/скрипты могут ввести новую пушечную флажку, новый
    /// тип эффекта или ParamKey оборудования, и валидатор перестанет ругаться.
    ///
    /// Дефолтный набор заполняется <see cref="RegisterDefaults"/> при первом обращении:
    ///   • bonus keys — из §6.2 диздока (см. docs/modules/micromodules_design.md);
    ///   • WeaponFlags — из <see cref="EmbedWeaponFlags"/>;
    ///   • WeaponEffectTypes — из значений enum <see cref="CombatEffectType"/>.
    /// </summary>
    public static class EmbedRegistry
    {
        private static bool _defaultsRegistered;

        // Категория → допустимые ключи Params. Ключ Bonuses "<Category>.<Key>" валиден,
        // если категория в реестре И Key в её множестве. Спец-префикс — см. IsKnownBonus.
        private static readonly Dictionary<string, HashSet<string>> BonusKeys = new();

        // Ключи Bonuses, где нужно префиксное совпадение (например "Hull.Vulnerability.*").
        private static readonly Dictionary<string, HashSet<string>> BonusKeyPrefixes = new();

        private static readonly HashSet<string> WeaponFlags = new();
        private static readonly HashSet<string> WeaponEffectTypes = new();
        private static readonly HashSet<string> TriggerEvents = new();
        private static readonly HashSet<string> TriggerOps = new();

        // ── API регистрации ────────────────────────────────────────────────

        /// <summary>Разрешить bonus key <c>category.paramKey</c> (например, RegisterBonus("Engine", "Speed")).</summary>
        public static void RegisterBonus(string category, string paramKey)
        {
            EnsureDefaults();
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(paramKey)) return;
            if (!BonusKeys.TryGetValue(category, out var set))
                BonusKeys[category] = set = new HashSet<string>();
            set.Add(paramKey);
        }

        /// <summary>Разрешить префикс bonus key: <c>category.prefix*</c> валидирует любые ключи,
        /// начинающиеся с <c>prefix</c> (например, "Vulnerability.").</summary>
        public static void RegisterBonusPrefix(string category, string prefix)
        {
            EnsureDefaults();
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(prefix)) return;
            if (!BonusKeyPrefixes.TryGetValue(category, out var set))
                BonusKeyPrefixes[category] = set = new HashSet<string>();
            set.Add(prefix);
        }

        public static void RegisterWeaponFlag(string flag)
        {
            EnsureDefaults();
            if (!string.IsNullOrEmpty(flag)) WeaponFlags.Add(flag);
        }

        public static void RegisterWeaponEffectType(string type)
        {
            EnsureDefaults();
            if (!string.IsNullOrEmpty(type)) WeaponEffectTypes.Add(type);
        }

        /// <summary>Зарегистрировать имя события триггера (см. <see cref="TriggerEvent"/>).</summary>
        public static void RegisterTriggerEvent(string name)
        {
            EnsureDefaults();
            if (!string.IsNullOrEmpty(name)) TriggerEvents.Add(name);
        }

        /// <summary>Зарегистрировать имя операции триггера (см. TriggerBus.OpRegistry).</summary>
        public static void RegisterTriggerOp(string name)
        {
            EnsureDefaults();
            if (!string.IsNullOrEmpty(name)) TriggerOps.Add(name);
        }

        // ── API запроса ────────────────────────────────────────────────────

        /// <summary>true, если ключ Bonuses (<c>category.paramKey</c>) допустим по реестру.</summary>
        public static bool IsKnownBonus(string category, string paramKey)
        {
            EnsureDefaults();
            if (BonusKeys.TryGetValue(category, out var set) && set.Contains(paramKey)) return true;
            if (BonusKeyPrefixes.TryGetValue(category, out var prefixes))
                foreach (var p in prefixes)
                    if (paramKey.StartsWith(p, System.StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool IsKnownWeaponFlag(string flag)
        {
            EnsureDefaults();
            return !string.IsNullOrEmpty(flag) && WeaponFlags.Contains(flag);
        }

        public static bool IsKnownWeaponEffectType(string type)
        {
            EnsureDefaults();
            return !string.IsNullOrEmpty(type) && WeaponEffectTypes.Contains(type);
        }

        public static bool IsKnownTriggerEvent(string name)
        {
            EnsureDefaults();
            return !string.IsNullOrEmpty(name) && TriggerEvents.Contains(name);
        }

        public static bool IsKnownTriggerOp(string name)
        {
            EnsureDefaults();
            return !string.IsNullOrEmpty(name) && TriggerOps.Contains(name);
        }

        // ── Defaults ───────────────────────────────────────────────────────

        private static void EnsureDefaults()
        {
            if (_defaultsRegistered) return;
            _defaultsRegistered = true;
            RegisterDefaults();
        }

        private static void RegisterDefaults()
        {
            // Bonus keys — из §6.2 диздока.
            AddBonuses(EquipmentCategory.Engine,       "Speed", "JumpRange", "SpeedMult");
            AddBonuses(EquipmentCategory.FuelTank,     "Capacity");
            AddBonuses(EquipmentCategory.Radar,        "Range", "GalaxyMapScope");
            AddBonuses(EquipmentCategory.Scanner,      "Power");
            AddBonuses(EquipmentCategory.Droid,        "Efficiency", "HealPerTurn", "Damage", "WearMult");
            AddBonuses(EquipmentCategory.CargoGrabber, "Power", "Range", "MassBonus", "SpeedBonus");
            AddBonuses(EquipmentCategory.Shield,       "BlockPercent", "ReflectChance");
            AddBonuses(EquipmentCategory.Forsage,      "SpeedMul");
            AddBonuses(EquipmentCategory.Hull,         "Armor", "HP",
                "MassMult",           // антигравитатор: доля -0.25 → -25% массы
                "MissileDamageMult",  // ракетанг: -0.10..-0.40 → блокировка ракетного урона
                "SunDamageMult");     // отморозки: -0.50 → тепловой урон от звезды ×0.5 (TODO: чтение)
            // Артефакты — self-scope: их SlotCode кладёт бонусы в собственные Params через
            // ключ "Artefacts.*". Читаются кодом артефакта (CollectSpeedMult, ApplyTurnEffects, ...).
            AddBonuses(EquipmentCategory.Artefacts,    "SpeedMult", "HealPerTurn", "TurnsDelta", "GalaxyMapScope",
                                                        "SpeedRecoveryMult"); // Криоколония: доп. декремент Slow за ход.
            // TODO Криоколония/солнце: как только появится система урона от звезды, читать
            // Hull.SunDamageMult (снижение теплового урона от звезды) — уже добавлен в Hull-ключи ниже.
            AddBonuses(EquipmentCategory.Weapons,
                "MinDmg", "MaxDmg", "Range",
                "ArmorPenetration", "ShieldPenetration",
                "EquipmentHitChance", "EquipmentDamage",
                "MaxAmmo",
                "EnergyDamageMult"); // пропорционар: +60% энергоурона

            // Префикс уязвимостей корпуса: Hull.Vulnerability.<DamageType>
            AddPrefix(EquipmentCategory.Hull, "Vulnerability.");

            // WeaponFlags — из EmbedWeaponFlags.
            AddFlags(EmbedWeaponFlags.NoDamageDelta,
                     EmbedWeaponFlags.IgnoreArmorAndShield,
                     EmbedWeaponFlags.NonLethal,
                     EmbedWeaponFlags.AmmoFree);

            // WeaponEffectTypes — все значения enum.
            foreach (var v in System.Enum.GetNames(typeof(CombatEffectType)))
                WeaponEffectTypes.Add(v);

            // Trigger events — из TriggerEvent.
            TriggerEvents.Add(TriggerEvent.Fire);
            TriggerEvents.Add(TriggerEvent.Hit);
            TriggerEvents.Add(TriggerEvent.TakeDamage);
            TriggerEvents.Add(TriggerEvent.Kill);
            TriggerEvents.Add(TriggerEvent.MissileFire);
            TriggerEvents.Add(TriggerEvent.Wear);
            TriggerEvents.Add(TriggerEvent.TurnStart);
            TriggerEvents.Add(TriggerEvent.TurnEnd);
            TriggerEvents.Add(TriggerEvent.Move);

            // Trigger ops — встроенные (регистрирует TriggerBus в статическом ctor).
            // Дефолты здесь пусты; названия добавляются TriggerBus.RegisterBuiltinOps.
        }

        private static void AddBonuses(string category, params string[] keys)
        {
            if (!BonusKeys.TryGetValue(category, out var set))
                BonusKeys[category] = set = new HashSet<string>();
            foreach (var k in keys) set.Add(k);
        }

        // Псевдо-категория (не EquipmentCategory) для группировки корабельных бонусов —
        // например "Hyperjump.*", "Radar.*" в будущем. Валидатор EmbedConfigValidator
        // проверяет их через <see cref="HasCategory"/>.
        private static void AddCategory(string category, params string[] keys) => AddBonuses(category, keys);

        /// <summary>true, если категория известна реестру. Валидатор считает её допустимой
        /// в ключах Bonuses "&lt;Category&gt;.&lt;ParamKey&gt;" даже если её нет в
        /// <see cref="EquipmentCategory.DisplayOrder"/> (псевдо-категории вроде Hyperjump, Radar).</summary>
        public static bool HasCategory(string category)
        {
            EnsureDefaults();
            return !string.IsNullOrEmpty(category) &&
                   (BonusKeys.ContainsKey(category) || BonusKeyPrefixes.ContainsKey(category));
        }

        private static void AddPrefix(string category, string prefix)
        {
            if (!BonusKeyPrefixes.TryGetValue(category, out var set))
                BonusKeyPrefixes[category] = set = new HashSet<string>();
            set.Add(prefix);
        }

        private static void AddFlags(params string[] flags)
        {
            foreach (var f in flags) WeaponFlags.Add(f);
        }
    }
}
