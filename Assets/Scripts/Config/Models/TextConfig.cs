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
    public class TextConfig
    {
        /// <summary>Общие для всех галактик поля.</summary>
        [JsonProperty("RaceNames")] public Dictionary<string, string> RaceNames { get; set; }

        /// <summary>
        /// Сообщения для экрана смерти. Ключ — название PlayerDeathCause
        /// (Weapon, Missile, Asteroid, Unknown) либо "Default" как фолбэк.
        /// Допустимые плейсхолдеры: {killer}, {star}.
        /// </summary>
        [JsonProperty("DeathMessages")] public Dictionary<string, string> DeathMessages { get; set; }

        /// <summary>Локализуемые тексты новостей галактики (категории + шаблоны).
        /// Читаются через <see cref="SRG.Galaxy.Politics.NewsTexts"/>.</summary>
        [JsonProperty("News")] public NewsTextsConfig News { get; set; }

        /// <summary>Фразы диалоговой системы: пулы строк, тексты приветствий, дефолты тегов,
        /// имена говорящих. DialogsConfig.json держит только структуру и условия.
        /// Связывается с ней на загрузке через <see cref="SRG.Dialog.DialogTexts.Link"/>.</summary>
        [JsonProperty("Dialog")] public SRG.Dialog.DialogTextsConfig Dialog { get; set; } = new();

        /// <summary>Per-galaxy пулы имён (ключ = ключ галактики в GalaxyConfig.Galaxies).
        /// Генератор берёт пулы отсюда по активной галактике.</summary>
        [JsonProperty("Galaxies")] public Dictionary<string, TextConfigGalaxyData> Galaxies { get; set; }

        /// <summary>Legacy-fallback: если галактика не найдена в <see cref="Galaxies"/>,
        /// генератор использует эти поля. Оставлены для обратной совместимости.</summary>
        [JsonProperty("SectorNames")] public Dictionary<string, string> SectorNames { get; set; }
        [JsonProperty("StarsNames")] public Dictionary<string, RaceNamesData> StarsNames { get; set; }
        [JsonProperty("PlanetsNames")] public Dictionary<string, RaceNamesData> PlanetsNames { get; set; }
    }

    /// <summary>Тексты и шаблоны новостей для лент/панелей.
    /// Категории (Categories) — ключ→отображаемое имя. Шаблоны (Templates) — ключ→формат-строка
    /// с именованными плейсхолдерами вида <c>{starName}</c>. Fallback при отсутствии ключа —
    /// сам ключ (заметно в UI, но не падает).</summary>
    public class NewsTextsConfig
    {
        [JsonProperty("Categories")] public Dictionary<string, string> Categories { get; set; } = new();
        [JsonProperty("Templates")]  public Dictionary<string, string> Templates  { get; set; } = new();
    }

    /// <summary>Per-galaxy контейнер имён — то, что раньше жило в корне TextConfig.
    /// Формат подсекций идентичен legacy-полям TextConfig.</summary>
    public class TextConfigGalaxyData
    {
        [JsonProperty("SectorNames")] public Dictionary<string, string> SectorNames { get; set; }
        [JsonProperty("StarsNames")] public Dictionary<string, RaceNamesData> StarsNames { get; set; }
        [JsonProperty("PlanetsNames")] public Dictionary<string, RaceNamesData> PlanetsNames { get; set; }
    }

    public class RaceNamesData
    {
        [JsonProperty("Fixed")] public Dictionary<string, string> Fixed { get; set; } = new();
        [JsonProperty("Pool")] public List<string> Pool { get; set; } = new();
    }
}
