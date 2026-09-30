using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SRG.Combat;
using SRG.Dialog;
using SRG.Dialog.PlanetGreetings;
using SRG.Economy;
using SRG.Equipment;
using SRG.Galaxy;
using SRG.Galaxy.Politics;
using SRG.NpcAI;
using SRG.NpcAI.Spawning;
using SRG.Science;
using SRG.Ships;
using SRG.Ships.Services;
using SRG.Simulation;

namespace SRG.Config
{
    public class GalaxyConfigData
    {
        /// <summary>Отображаемое название галактики.</summary>
        [JsonProperty("Name")] public string Name { get; set; }

        /// <summary>Расы, присутствующие в этой галактике (ключи из GalaxyConfig.Races).</summary>
        [JsonProperty("Races")] public List<string> Races { get; set; }

        /// <summary>Размер галактики в единицах внутренней сетки [x, y].</summary>
        [JsonProperty("GalaxyGridSize")] public int[] GalaxyGridSize { get; set; }

        [JsonProperty("StarsCount")] public int StarsCount { get; set; }
        [JsonProperty("ConstCount")] public int SectorCount { get; set; }
        [JsonProperty("MinStarsPerConstellation")] public int MinStarsPerSector { get; set; }
        [JsonProperty("MaxStarsPerConstellation")] public int MaxStarsPerSector { get; set; }
        [JsonProperty("MinPlanetsPerStar")] public int MinPlanetsPerStar { get; set; }
        [JsonProperty("MaxPlanetsPerStar")] public int MaxPlanetsPerStar { get; set; }

        [JsonProperty("StartingStarName")] public string StartingStarName { get; set; }

        [JsonProperty("MinStarDistanceParsecs")] public float MinStarDistanceParsecs { get; set; } = 6f;

        [JsonProperty("MapPixelsPerParsec")] public float MapPixelsPerParsec { get; set; } = 4f;
        [JsonProperty("VoronoiBorderColor")] public string VoronoiBorderColor { get; set; } = "90,100,130";
        [JsonProperty("GTU")] public GtuConfig GTU { get; set; }
        /// <summary>Целевой охват одной расы в режиме Expansion: round(StarsCount × Rnd(BaseFractionMin..Max) × 2 / RaceCount) ± Jitter.
        /// Множитель ×2 учитывает, что системы могут быть мультирасовыми.</summary>
        [JsonProperty("TargetSystemsPerRace")] public TargetSystemsPerRaceConfig TargetSystemsPerRace { get; set; }

        /// <summary>
        /// Роли рас в галактике: ключ — произвольное имя роли ("Major", "Hostile", "Invader", …),
        /// значение — список рас и правила расселения. Позволяет балансировать не конкретные расы,
        /// а группы («все основные расы получают равную долю систем»). Если задано — список рас
        /// галактики = объединение рас всех ролей (поле <see cref="Races"/> можно не заполнять).
        /// </summary>
        [JsonProperty("RaceRoles")] public Dictionary<string, GalaxyRaceRoleConfig> RaceRoles { get; set; }

        /// <summary>Все расы галактики: <see cref="Races"/> ∪ расы из <see cref="RaceRoles"/>. null — список не задан.</summary>
        public HashSet<string> GetAllRaces()
        {
            HashSet<string> set = null;
            if (Races != null && Races.Count > 0) set = new HashSet<string>(Races);
            if (RaceRoles != null)
                foreach (var role in RaceRoles.Values)
                    if (role?.Races != null)
                        foreach (var r in role.Races) (set ??= new HashSet<string>()).Add(r);
            return set;
        }

        /// <summary>Роль, к которой относится раса (первая найденная), или null.</summary>
        public string FindRaceRole(string raceKey, out GalaxyRaceRoleConfig role)
        {
            role = null;
            if (RaceRoles == null || string.IsNullOrEmpty(raceKey)) return null;
            foreach (var kv in RaceRoles)
                if (kv.Value?.Races != null && kv.Value.Races.Contains(raceKey)) { role = kv.Value; return kv.Key; }
            return null;
        }
    }

    /// <summary>Правила расселения для группы рас (роли) в галактике.</summary>
    public class GalaxyRaceRoleConfig
    {
        /// <summary>Ключи рас (из GalaxyConfig.Races), входящих в роль.</summary>
        [JsonProperty("Races")] public List<string> Races { get; set; } = new();

        /// <summary>Участвует ли роль в расселении (Expansion). Непланетарные расы не расселяются в любом случае.</summary>
        [JsonProperty("Colonize")] public bool Colonize { get; set; } = true;

        /// <summary>
        /// Доля систем галактики, которую роль должна суммарно занять (0..1). Целевое число систем на расу:
        /// round(StarsCount × SystemsShare × Overlap / RaceCount) ± Jitter — одинаковое для всех рас роли.
        /// 0 — использовать старую формулу <see cref="GalaxyConfigData.TargetSystemsPerRace"/>.
        /// </summary>
        [JsonProperty("SystemsShare")] public float SystemsShare { get; set; } = 0f;

        /// <summary>Средняя «мультирасовость» заселённой системы (сколько рас роли в среднем делят одну систему).</summary>
        [JsonProperty("Overlap")] public float Overlap { get; set; } = 1.3f;

        [JsonProperty("Jitter")] public int Jitter { get; set; } = 1;

        /// <summary>
        /// Гарантия ниши: если расе не хватает пригодных планет до цели, генератор превращает ближайшую
        /// свободную планету подходящего размера с температурой в пределах Terraformable в «родной» мир расы
        /// (параметры → Optimal/середина Acceptable). Выравнивает узкоспециализированные расы.
        /// </summary>
        [JsonProperty("GuaranteeNiche")] public bool GuaranteeNiche { get; set; } = true;

        /// <summary>
        /// Режим покрытия: [min, max] — доля систем галактики, которую роль заселяет суммарно (разыгрывается
        /// на каждую генерацию). Если задан — вместо SystemsShare/Overlap: расы роли расселяются по очереди,
        /// пока доля заселённых ролью систем не достигнет цели или расам некуда расти.
        /// </summary>
        [JsonProperty("Coverage")] public float[] Coverage { get; set; }

        /// <summary>
        /// Режим покрытия: [min, max] — разница между самой крупной и самой мелкой расой роли, в долях от числа
        /// звёзд галактики. Разыгрывается на каждую генерацию; верхняя граница — жёсткий потолок разницы.
        /// </summary>
        [JsonProperty("RaceSpread")] public float[] RaceSpread { get; set; } = { 0.05f, 0.10f };

        /// <summary>
        /// Необязательные веса рас роли (режим покрытия): задают, какие расы крупнее. Разница весов
        /// масштабируется в RaceSpread. Не задано — порядок рас случайный на каждую генерацию.
        /// </summary>
        [JsonProperty("Weights")] public Dictionary<string, float> Weights { get; set; }

        /// <summary>
        /// Штраф за уже заселённую систему при выборе цели расселения (в «уровнях спорности» планеты).
        /// Больше — меньше смешанных систем и выше покрытие; 0 — без предпочтения пустых систем.
        /// </summary>
        [JsonProperty("OccupiedPenalty")] public int OccupiedPenalty { get; set; } = 2;

        public bool UsesCoverage => Coverage != null && Coverage.Length >= 1 && Coverage[0] > 0f;
    }

    public class TargetSystemsPerRaceConfig
    {
        [JsonProperty("BaseFractionMin")] public float BaseFractionMin { get; set; } = 0.85f;
        [JsonProperty("BaseFractionMax")] public float BaseFractionMax { get; set; } = 0.9f;
        [JsonProperty("Multiplier")]      public float Multiplier      { get; set; } = 2.0f;
        [JsonProperty("Jitter")]          public int   Jitter          { get; set; } = 2;
    }

    public class SettingsConfigSection
    {
        [JsonProperty("SystemSizeMult")] public int SystemSizeMult { get; set; }
        [JsonProperty("InitialDate")] public string InitialDate { get; set; } = "01.01.0001";
        [JsonProperty("JumpFuelCostPerUnit")] public float JumpFuelCostPerUnit { get; set; } = 1.0f;
        /// <summary>Единиц дистанции пк, за которые корабль тратит 1 ход в фазе HyperArrive
        /// («болтание в гипертоннеле»). Итог: длительность = <c>ceil(distance / этот параметр)</c>,
        /// минус модификатор от Гипергенератора (<c>Hyperjump.TurnsDelta</c>). Минимум — 1 ход.</summary>
        [JsonProperty("HyperCrossPerTurnDistance")] public float HyperCrossPerTurnDistance { get; set; } = 30f;
        [JsonProperty("DefaultOwner")] public string DefaultOwner { get; set; }
        /// <summary>1 — режим расселения: расы начинают только с premadeConfig-планет и колонизируют галактику.</summary>
        [JsonProperty("Expansion")] public int Expansion { get; set; } = 0;
    }
}
