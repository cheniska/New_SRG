using System.Collections.Generic;
using Newtonsoft.Json;
using SRG.Config;
using SRG.Economy;
using SRG.Galaxy;

namespace SRG.NpcAI.Spawning
{
    /// <summary>
    /// Конфиг top-up системы спавна кораблей. Загружается из секции "Spawn" в GalaxyConfig.json.
    /// Все числа — динамические, нет хардкода фракций/типов кораблей.
    ///
    /// Формула спавна (везде):
    ///   shortage    = max(0, (target - count) / target)
    ///   probability = base_chance × (1 + shortage)         // на цели = base, пусто = 2×base
    ///   spawn_if    = shortage > 0 AND roll < probability AND star.SpawnsToday &lt; GameSettingsConfig.MaxShipsSpawnedPerStarPerDay
    ///
    /// Лимит спавнов на систему в день вынесен в <see cref="Config.GameSettingsConfig.MaxShipsSpawnedPerStarPerDay"/>
    /// (был GlobalCooldownDays в этом же конфиге).
    /// </summary>
    public class SpawnConfig
    {
        /// <summary>Параметры доминации сторон/рас.</summary>
        [JsonProperty("Domination")] public DominationConfig Domination { get; set; } = new();

        /// <summary>Стартовая раздача при генерации галактики.</summary>
        [JsonProperty("InitialDistribution")] public InitialDistributionConfig InitialDistribution { get; set; } = new();

        /// <summary>Политики спавна per-shipTypeId. Динамический словарь — модовые типы добавляют свои записи.
        /// Для неизвестных типов берётся <see cref="DefaultPolicy"/>.</summary>
        [JsonProperty("Policies")] public Dictionary<string, ShipSpawnPolicyConfig> Policies { get; set; } = new();

        /// <summary>Фолбэк для типов, которых нет в Policies. Без логики — просто чтобы не падать.</summary>
        [JsonProperty("DefaultPolicy")] public ShipSpawnPolicyConfig DefaultPolicy { get; set; } = new()
        {
            Model = "TopUpPerPlanet",
            BaseChance = 0f,
            BaseTarget = 0
        };

        /// <summary>Таблицы подтипов — одна на расу с флагом <see cref="RaceConfig.UseShipTypeTable"/>.
        /// Ключ внешнего словаря: raceKey (например "RaceDominators1").
        /// Ключ внутреннего: имя ступени доминации ("0-25"/"25-50"/"50-75"/"75-100").
        /// Значение: словарь shipTypeId → вес (произвольные числа; нормализуются при выборе).</summary>
        [JsonProperty("SubtypeTables")] public Dictionary<string, Dictionary<string, Dictionary<string, int>>> SubtypeTables { get; set; } = new();

        /// <summary>Возвращает политику по ShipTypeId с фолбэком на DefaultPolicy.</summary>
        public ShipSpawnPolicyConfig GetPolicy(string shipTypeId)
        {
            if (Policies != null && shipTypeId != null && Policies.TryGetValue(shipTypeId, out var p) && p != null) return p;
            return DefaultPolicy;
        }
    }

    /// <summary>
    /// Конфиг политики спавна для одного ShipTypeId.
    /// Поля используются по-разному в зависимости от <see cref="Model"/>.
    /// </summary>
    public class ShipSpawnPolicyConfig
    {
        /// <summary>Тип политики (определяет, какой класс будет её исполнять):
        /// "TopUpPerPlanet" — гражданские/воины (per-planet roll);
        /// "TopUpPerGalaxy" — рейнджеры/пираты (один глобальный roll);
        /// "TopUpPerSector" — линкор (per-sector roll);
        /// "TopUpPerStar"   — доминаторы (per-star roll на клинг-звёздах).</summary>
        [JsonProperty("Model")] public string Model { get; set; } = "TopUpPerPlanet";

        /// <summary>Базовый шанс ролла в день (0..1). Умножается на (1 + shortage).</summary>
        [JsonProperty("BaseChance")] public float BaseChance { get; set; } = 0.02f;

        /// <summary>Базовый target (если не зависит от расы/радиуса/модификаторов).</summary>
        [JsonProperty("BaseTarget")] public int BaseTarget { get; set; } = 0;

        /// <summary>Target per-race override. Ключ = raceKey, значение = target.
        /// Применяется если у локации (планеты/звезды) есть Race.</summary>
        [JsonProperty("TargetByRace")] public Dictionary<string, int> TargetByRace { get; set; }

        /// <summary>Множитель target × (NormalSystemsCount × Multiplier).
        /// Используется в TopUpPerGalaxy для рейнджеров (×1.5) и пиратов (×0.3).</summary>
        [JsonProperty("TargetFromNormalsMultiplier")] public float TargetFromNormalsMultiplier { get; set; } = 0f;

        /// <summary>Глобальный потолок target (для TopUpPerGalaxy). 0 = без ограничения.</summary>
        [JsonProperty("TargetGlobalCap")] public int TargetGlobalCap { get; set; } = 0;

        /// <summary>Бонус к target по размеру системы.
        /// Ключ = бакет ("Small"/"Medium"/"Large"), значение = +N. Применяется в TopUpPerPlanet для воинов.</summary>
        [JsonProperty("RadiusBonus")] public Dictionary<string, int> RadiusBonus { get; set; }

        /// <summary>Бонус к target по правительству планеты. Ключ = Government, значение = -1..+1.
        /// Применяется только к Warrior.</summary>
        [JsonProperty("GovernmentBonus")] public Dictionary<string, int> GovernmentBonus { get; set; }

        /// <summary>Применять ли <see cref="RaceConfig.FleetSizeModifier"/> к target (только Warrior).</summary>
        [JsonProperty("UseFleetSizeModifier")] public bool UseFleetSizeModifier { get; set; } = false;

        /// <summary>Бонус к target по типу экономики планеты. Ключ = EconomyType ("Industrial" и т.п.), значение = +N.
        /// Применяется к Transport.</summary>
        [JsonProperty("EconomyBonus")] public Dictionary<string, int> EconomyBonus { get; set; }

        /// <summary>Crime-модификатор target (для пиратов). Если null — не применяется.</summary>
        [JsonProperty("CrimeMultiplier")] public CrimeMultiplierConfig CrimeMultiplier { get; set; }

        /// <summary>Допустимые Owner для целевой локации (Coalition, Pirates, ...). Пусто/null = любой.</summary>
        [JsonProperty("AllowedOwners")] public List<string> AllowedOwners { get; set; }

        /// <summary>Допустимые расы для целевой локации. Пусто/null = любая.</summary>
        [JsonProperty("AllowedRaces")] public List<string> AllowedRaces { get; set; }

        /// <summary>Куда спавнить (для TopUpPerGalaxy/TopUpPerSector). Значения:
        /// "OwnPlanet"        — у обитаемой планеты целевой расы (домашняя);
        /// "CoalitionPlanet"  — у случайной обитаемой коалиц. планеты;
        /// "SectorCapital"    — у крупнейшей планеты сектора;
        /// "ClingStar"        — в клинг-звезде.</summary>
        [JsonProperty("SpawnLocation")] public string SpawnLocation { get; set; }

        /// <summary>Стартовый капитал корабля. Применяется как
        ///   ship.Money = round(StartingMoney × GalaxyData.InflationFactor)
        /// при старте — масштабируется вместе с ценами в TradeSystem. 0 = без денег
        /// (актуально для доминаторов).</summary>
        [JsonProperty("StartingMoney")] public int StartingMoney { get; set; } = 0;
    }

    public class CrimeMultiplierConfig
    {
        [JsonProperty("Divisor")] public float Divisor { get; set; } = 50f;
        [JsonProperty("Min")]     public float Min     { get; set; } = 0.5f;
        [JsonProperty("Max")]     public float Max     { get; set; } = 2.0f;
    }

    public class DominationConfig
    {
        /// <summary>Порог доминации стороны: ≥75% галактики (по числу систем).</summary>
        [JsonProperty("SideThreshold")] public float SideThreshold { get; set; } = 0.75f;

        /// <summary>Коэффициент для расового порога (только если у Owner SeparateRaceDomination=true).
        /// Раса доминирует при доле ≥ SideThreshold × RaceCoefficient = 0.75 × 0.75 = 0.5625.</summary>
        [JsonProperty("RaceCoefficient")] public float RaceCoefficient { get; set; } = 0.75f;

        /// <summary>Границы ступеней для выбора строки таблицы подтипов (0..1).
        /// Имена ступеней должны совпадать с ключами SubtypeTables (по умолчанию "0-25", "25-50", "50-75", "75-100").</summary>
        [JsonProperty("Tiers")] public List<DominationTier> Tiers { get; set; } = new()
        {
            new() { Name = "0-25",   Min = 0.00f, Max = 0.25f },
            new() { Name = "25-50",  Min = 0.25f, Max = 0.50f },
            new() { Name = "50-75",  Min = 0.50f, Max = 0.75f },
            new() { Name = "75-100", Min = 0.75f, Max = 1.01f }
        };
    }

    public class DominationTier
    {
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("Min")]  public float Min   { get; set; }
        [JsonProperty("Max")]  public float Max   { get; set; }
    }

    /// <summary>Стартовая раздача при генерации галактики (жёсткие числа, не зависят от target).</summary>
    public class InitialDistributionConfig
    {
        /// <summary>Сколько кораблей каждого типа спавнить на одну обитаемую планету.</summary>
        [JsonProperty("PerHabitablePlanet")] public Dictionary<string, int> PerHabitablePlanet { get; set; } = new();

        /// <summary>Сколько кораблей каждого типа спавнить в каждую звезду с обитаемой планетой.</summary>
        [JsonProperty("PerSystem")] public Dictionary<string, int> PerSystem { get; set; } = new();

        /// <summary>Сколько кораблей каждого типа спавнить в каждый обитаемый сектор.</summary>
        [JsonProperty("PerSector")] public Dictionary<string, int> PerSector { get; set; } = new();
    }
}
