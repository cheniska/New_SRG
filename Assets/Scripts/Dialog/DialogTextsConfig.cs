using System.Collections.Generic;
using Newtonsoft.Json;

namespace SRG.Dialog
{
    /// <summary>
    /// Секция <c>"Dialog"</c> файла Assets/Config/TextsConfig.json — единственное место,
    /// где живут *фразы* диалоговой системы. DialogsConfig.json остаётся структурой:
    /// деревья, переходы, условия, действия, правила матчинга приветствий.
    /// <br/><br/>
    /// Связывание двух файлов делает <see cref="DialogTexts.Link"/> на загрузке:
    /// <see cref="Pools"/> заливаются в <see cref="DialogsConfig.StringPools"/>,
    /// <see cref="Greetings"/> — в <c>GreetingRule.Texts</c> по Id правила.
    /// Рантайм после линковки работает как раньше и про этот класс не знает.
    /// </summary>
    public class DialogTextsConfig
    {
        /// <summary>Значение тега по умолчанию, когда резолвер вернул пусто.
        /// Заменяет исторические инлайновые фоллбэки <c>{tag|текст}</c> в DialogsConfig.json.
        /// Ключ — имя тега (<c>cargo_amount</c>), допускается <c>*</c> как маска
        /// (<c>nav_star_*_dist</c> покрывает все восемь слотов навигации).
        /// Пустая строка — валидное значение («подставить ничего»), это не то же самое,
        /// что отсутствие ключа (тогда в тексте останется видимый <c>{tag}</c>).</summary>
        [JsonProperty("TagDefaults")] public Dictionary<string, string> TagDefaults { get; set; } = new();

        /// <summary>Имя говорящего по умолчанию для тега <c>{speaker_name}</c>, если у цели диалога
        /// нет собственного имени. Ключ — id дерева диалога (<c>Station_MC_gov</c> → «Дежурный медик»).
        /// Раньше это жило в инлайновых фоллбэках поля <c>Speaker</c>.</summary>
        [JsonProperty("Speakers")] public Dictionary<string, string> Speakers { get; set; } = new();

        /// <summary>Фраза по умолчанию, когда пул отсутствует. Нужна для шаблонных ключей вида
        /// <c>{pool:Gossip.&lt;Race&gt;Any}</c>, где не для каждой расы заведён свой пул.
        /// <br/>
        /// Ключ — либо сам шаблон (<c>Attack.&lt;ShipType&gt;Fear</c>), либо семейство, т.е. часть
        /// до первой точки (<c>Attack</c> покрывает всё семейство сразу). Шаблон приоритетнее:
        /// у Fear/HaveBusiness/Suspect внутри одного семейства смысл фраз разный.</summary>
        [JsonProperty("PoolDefaults")] public Dictionary<string, string> PoolDefaults { get; set; } = new();

        /// <summary>Пулы вариативных строк — то, что раньше было
        /// <c>DialogsConfig.StringPools</c>. Формат идентичен: плоский ключ → список альтернатив.</summary>
        [JsonProperty("Pools")] public Dictionary<string, List<string>> Pools { get; set; } = new();

        /// <summary>Тексты правил приветствий (планетных и корабельных), ключ — <c>GreetingRule.Id</c>.
        /// Само правило (условия матчинга) остаётся в DialogsConfig.json.</summary>
        [JsonProperty("Greetings")] public Dictionary<string, List<string>> Greetings { get; set; } = new();

        /// <summary>Фразы, которые нужны самому коду, а не тегу в тексте узла: подписи кнопок UI,
        /// пометки в списках выбора, заголовок окна без говорящего. Раньше это были строковые
        /// литералы в C# и «дефолты» полей структурного конфига — то есть фразы в двух местах,
        /// куда переводчик не заглядывает.
        /// <br/>
        /// Читается через <see cref="DialogTexts.Phrase"/>. Отсутствующий ключ возвращает пустую
        /// строку и пишет warning: пропажу фразы лучше увидеть в логе, чем получить литерал
        /// на чужом языке.</summary>
        [JsonProperty("Phrases")] public Dictionary<string, string> Phrases { get; set; } = new();

        /// <summary>Нейтральные значения <c>&lt;Плейсхолдеров&gt;</c> приветствий («капитан»,
        /// «эта планета», «неизвестной расы»): чем подставиться, когда в конкретном контексте
        /// данных нет. Ключ — имя плейсхолдера без угловых скобок.
        /// <br/>
        /// Таблица обязана покрывать все плейсхолдеры, встречающиеся в текстах приветствий:
        /// непокрытый уедет игроку сырым <c>&lt;Tag&gt;</c>, а приветствие показывается при каждом
        /// открытии диалога. См. <see cref="PlanetGreetings.PlanetGreetingRenderer"/>.</summary>
        [JsonProperty("GreetingDefaults")] public Dictionary<string, string> GreetingDefaults { get; set; } = new();
    }
}
