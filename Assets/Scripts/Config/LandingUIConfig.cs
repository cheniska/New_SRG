using System.Collections.Generic;
using Newtonsoft.Json;
using SRG.Galaxy;

namespace SRG.Config
{
    /// <summary>
    /// Конфигурация интерфейса посадки: набор кнопок навигации, начальный экран, подпись
    /// кнопки «связи» — задаются отдельно для каждого <see cref="LandingSiteKind"/>. Для
    /// специальных корпусов (например, научная база «SB») можно доложить дополнительные
    /// кнопки через <see cref="HullOverrides"/>. Файл — <c>Assets/Config/LandingUIConfig.json</c>.
    /// </summary>
    public class LandingUIConfig
    {
        [JsonProperty("Profiles")]      public Dictionary<string, LandingProfileConfig> Profiles { get; set; } = new();
        [JsonProperty("HullOverrides")] public Dictionary<string, HullOverrideConfig>   HullOverrides { get; set; } = new();

        /// <summary>Профиль по типу цели. null, если для этого Kind нет описания
        /// (вызывающий берёт fallback).</summary>
        public LandingProfileConfig ResolveProfile(LandingSiteKind kind)
        {
            if (Profiles == null) return null;
            return Profiles.TryGetValue(kind.ToString(), out var p) ? p : null;
        }

        /// <summary>Дополнения по HullType (только для <see cref="ShipData"/>): экстра-кнопки,
        /// которые дозакладываются к базовому профилю. null, если override не задан.</summary>
        public HullOverrideConfig ResolveHullOverride(string hullType)
        {
            if (string.IsNullOrEmpty(hullType) || HullOverrides == null) return null;
            return HullOverrides.TryGetValue(hullType, out var o) ? o : null;
        }
    }

    /// <summary>Профиль конкретного типа посадочной цели (планета/станция/носитель).</summary>
    public class LandingProfileConfig
    {
        /// <summary>Screen-id, который открывается сразу после посадки (обычно "Overview" для
        /// планет/станций и "Hangar" для линкоров). Если не задан — берётся первый из
        /// <see cref="NavButtons"/>.</summary>
        [JsonProperty("InitialScreen")]     public string InitialScreen { get; set; }

        /// <summary>Подпись отдельной кнопки диалога (открывает связь с администрацией/капитаном).
        /// Если пусто — используется <see cref="PlanetUIConfig.DialogButtonLabel"/>.</summary>
        [JsonProperty("DialogButtonLabel")] public string DialogButtonLabel { get; set; }

        /// <summary>Кнопки навигации сверху вниз в порядке отображения. Пустой список = только
        /// кнопка диалога, без вкладок.</summary>
        [JsonProperty("NavButtons")]        public List<PlanetNavButtonConfig> NavButtons { get; set; } = new();

        /// <summary>Переопределение заголовков экранов для этого типа посадки. Ключ — screen-id
        /// (например, "Overview"), значение — что показывать в шапке. Если нет — берётся
        /// <see cref="PlanetUIConfig.Screens"/>[id].Title. Пример: у станций Overview обычно
        /// называется «Командование», а у планет — «Планета».</summary>
        [JsonProperty("ScreenTitles")]      public Dictionary<string, string> ScreenTitles { get; set; } = new();
    }

    /// <summary>Дополнения по коду корпуса (см. <c>HullType</c> установленного корпуса ItemInstance).</summary>
    public class HullOverrideConfig
    {
        /// <summary>Дополнительные кнопки — дописываются в конец списка навигации базового профиля.
        /// Обработчик клика ищется по <see cref="PlanetNavButtonConfig.Id"/> в контроллере UI
        /// (спец-Id вроде "SbImprovement" открывают отдельные модальные диалоги, не screen'ы).</summary>
        [JsonProperty("ExtraNavButtons")] public List<PlanetNavButtonConfig> ExtraNavButtons { get; set; } = new();
    }
}
