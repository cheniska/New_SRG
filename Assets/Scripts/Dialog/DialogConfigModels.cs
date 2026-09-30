using System.Collections.Generic;
using Newtonsoft.Json;
using SRG.Config;
using SRG.Dialog.PlanetGreetings;

namespace SRG.Dialog
{
    // ──────────────────────────────────────────────────────────────────────────────
    // Конфиг системы диалогов
    //
    // Подключается через GalaxyConfig.Dialogs (раздел "Dialogs" в galaxyConfig.json).
    //
    // Пример:
    // {
    //   "Dialogs": {
    //     "Dialogs": {
    //       "planet_governor_default": {
    //         "Title": "Аудиенция у губернатора",
    //         "Speaker": "Губернатор",
    //         "StartNode": "intro",
    //         "Nodes": {
    //           "intro": {
    //             "Text": "Приветствую, капитан. Чем обязан вашему визиту?",
    //             "Replies": [
    //               { "Text": "Мне нужна помощь с ремонтом.",   "NextNode": "repair" },
    //               { "Text": "Хочу пожертвовать 1000 кр.",
    //                 "Actions": [ { "Method": "TakeMoney", "Args": ["1000"] },
    //                              { "Method": "Log",       "Args": ["Губернатор поблагодарил вас."] } ],
    //                 "NextNode": "thanks" },
    //               { "Text": "Прощайте.", "NextNode": null }
    //             ]
    //           },
    //           "repair": {
    //             "Text": "Бесплатный ремонт — это, конечно, заманчиво...",
    //             "OnEnter": [ { "Method": "Log", "Args": ["[Диалог] Запрос на ремонт."] } ],
    //             "Replies": [
    //               { "Text": "Понял, спасибо.", "NextNode": "intro" }
    //             ]
    //           },
    //           "thanks": {
    //             "Text": "Благодарю за щедрость!",
    //             "Replies": [ { "Text": "Прощайте.", "NextNode": null } ]
    //           }
    //         }
    //       }
    //     }
    //   }
    // }
    //
    // NextNode == null/"" — закрывает диалог.
    // Actions выполняются ДО перехода к NextNode. Метод "EndDialog" закрывает диалог
    // независимо от NextNode. Доступные методы регистрируются через
    // DialogService.RegisterAction(name, handler). Базовый набор — см. DialogActions.
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Единый корень файла Assets/Config/DialogsConfig.json.
    /// Собирает всё, связанное с диалогами: деревья, UI, приветствия планет и кораблей,
    /// пулы вариативных строк для узлов. GalaxyConfig.Dialogs имеет этот тип.
    /// </summary>
    public class DialogsConfig
    {
        [JsonProperty("Dialogs")]         public Dictionary<string, DialogTree> Dialogs { get; set; } = new();
        [JsonProperty("DialogUI")]        public DialogUIConfig                 DialogUI { get; set; } = new();
        [JsonProperty("PlanetGreetings")] public PlanetGreetingsConfig          PlanetGreetings { get; set; } = new();
        [JsonProperty("ShipGreetings")]   public ShipGreetingsConfig            ShipGreetings { get; set; } = new();

        /// <summary>
        /// Пулы вариативных строк. Ключ — плоский путь (Attack.PirateOk, Dominator.CommandBlazer, …),
        /// значение — список альтернатив. Используется тегом <c>{pool:Key}</c> в узлах диалога;
        /// поддерживается подстановка <c>&lt;ShipType&gt;</c> прямо в ключе:
        ///   <c>{pool:Attack.&lt;ShipType&gt;Ok}</c>.
        /// </summary>
        [JsonProperty("StringPools")] public Dictionary<string, List<string>> StringPools { get; set; } = new();

        /// <summary>Числовые пороги матчинга (ранги/рейтинг/статус игрока, квантование stock/price/ship-count)
        /// вынесены из C# сюда, чтобы можно было балансить без пересборки. См. <see cref="DialogTuning"/>.</summary>
        [JsonProperty("Tuning")] public DialogTuning Tuning { get; set; } = new();
    }

    /// <summary>Числовые пороги системы приветствий и матчинга. Значения по умолчанию совпадают
    /// с историческим хардкодом; безопасно оставить пустой блок в конфиге.</summary>
    public class DialogTuning
    {
        /// <summary>Актуальный тюнинг из GalaxyConfig.Dialogs. Может быть null до Awake/загрузки конфига.</summary>
        public static DialogTuning Current =>
            SRG.Core.GalaxyManager.Instance?.Context?.Config?.Dialogs?.Tuning;


        /// <summary>Owner-id, при котором игрок всегда считается «Pirate». По умолчанию "Pirates".</summary>
        [JsonProperty("PirateOwner")]        public string PirateOwner        { get; set; } = "Pirates";
        /// <summary>Минимальный CrimeRating для статуса «Pirate».</summary>
        [JsonProperty("PirateCrimeMin")]     public float  PirateCrimeMin     { get; set; } = 50f;
        /// <summary>Минимальный TradeProfit для статуса «Trader».</summary>
        [JsonProperty("TraderProfitMin")]    public int    TraderProfitMin    { get; set; } = 5000;
        /// <summary>Профит должен превышать (killsDom+killsPirate) × это значение, чтобы стать «Trader».</summary>
        [JsonProperty("TraderProfitPerKill")] public int   TraderProfitPerKill { get; set; } = 500;
        /// <summary>Минимальное число убийств для статуса «Warrior».</summary>
        [JsonProperty("WarriorKillsMin")]    public int    WarriorKillsMin    { get; set; } = 5;

        /// <summary>Лестница ранга игрока (Rank, MinKills). Сортировка от больших порогов к меньшим не требуется —
        /// селектор ищет первую подходящую при обходе.</summary>
        [JsonProperty("RankLadder")]         public List<RankStep>   RankLadder   { get; set; } = new();
        /// <summary>Лестница рейтинга игрока (Level, MinScore).</summary>
        [JsonProperty("RatingLadder")]       public List<RatingStep> RatingLadder { get; set; } = new();

        /// <summary>Пороги квантования запаса на планете (Zero/Mini/Small/Average/Big/Huge).
        /// Каждый порог — минимум для следующей ступени. По умолчанию: 50/200/500/1000.</summary>
        [JsonProperty("StockQuants")]        public StockQuantConfig StockQuants { get; set; } = new();
        /// <summary>Пороги квантования цены (Mini/Small/Average/Big/Huge). Значения — верхние границы
        /// диапазонов (доли от BasePrice). По умолчанию: 0.6/0.9/1.3/2.0.</summary>
        [JsonProperty("PriceQuants")]        public PriceQuantConfig PriceQuants { get; set; } = new();
        /// <summary>Порог, начиная с которого число кораблей квантуется как "Many".</summary>
        [JsonProperty("ShipCountMany")]      public int              ShipCountMany { get; set; } = 10;

        /// <summary>Пороги квантования населения планеты (Mini/Small/Average/Big/Huge).</summary>
        [JsonProperty("PopulationQuants")]   public StockQuantConfig PopulationQuants { get; set; }
            = new() { Mini = 1_000_000, Small = 100_000_000, Average = 1_000_000_000, Big = 2_000_000_000 };
        /// <summary>Пороги квантования денег игрока (Mini/Small/Average/Big/Huge).</summary>
        [JsonProperty("MoneyQuants")]        public StockQuantConfig MoneyQuants { get; set; }
            = new() { Mini = 1_000, Small = 10_000, Average = 100_000, Big = 1_000_000 };

        /// <summary>Фолбэк для partner-fee, если конфиг партнёров не загружен.</summary>
        [JsonProperty("PartnerMinFeeFallback")] public int PartnerMinFeeFallback { get; set; } = 1000;

        /// <summary>Базовый минимум выкупа за перемирие. Итоговая сумма = base × (1 + MaxHull/500).</summary>
        [JsonProperty("TruceMinFeeBase")]    public int    TruceMinFeeBase    { get; set; } = 500;
        /// <summary>Максимальная дистанция, на которой корабль воспринимает угрозу / принимает выкуп.
        /// Дальше — реплика LongDistance / отказ от Truce/Money/Goods.</summary>
        [JsonProperty("TruceLongDistance")]  public float  TruceLongDistance  { get; set; } = 8f;

        /// <summary>Макс. мировая дистанция для угрозы ограбления/вымогательства (CargoRobberyService,
        /// ExtortionService). Отделено от TruceLongDistance: перемирие требует контакта в упор,
        /// а грабитель может «взять на прицел» с боевой дистанции. По умолчанию 400 —
        /// средняя дальность пушки, оптимальная зона угрозы.</summary>
        [JsonProperty("RobberyLongDistance")] public float RobberyLongDistance { get; set; } = 400f;

        /// <summary>Максимальная доля денег корабля, которую он согласится отдать вымогателю (0..1).</summary>
        [JsonProperty("ExtortionMaxPayRatio")]     public float ExtortionMaxPayRatio     { get; set; } = 0.5f;
        /// <summary>Прирост CrimeRating за 100 кр., выжатых из корабля вымогательством.</summary>
        [JsonProperty("ExtortionCrimePerHundred")] public float ExtortionCrimePerHundred { get; set; } = 0.5f;
        /// <summary>Доля от каждого стека, которую цель сбрасывает в результате ограбления (0..1).</summary>
        [JsonProperty("CargoRobFraction")]         public float CargoRobFraction         { get; set; } = 0.5f;
        /// <summary>Доля денег жертвы, отдаваемая игроку за спасение (0..1).</summary>
        [JsonProperty("ProtectRewardMoneyRatio")]  public float ProtectRewardMoneyRatio  { get; set; } = 0.2f;
        /// <summary>Доля стека груза, сбрасываемая жертвой за спасение (0..1).</summary>
        [JsonProperty("ProtectRewardGoodsRatio")]  public float ProtectRewardGoodsRatio  { get; set; } = 0.25f;
        /// <summary>Порог «страха» для совместной атаки: если сила цели / (наша+союзника) > этого значения —
        /// союзник отказывается по причине Fear. Значение 1.5 означает «мы не сунемся против врага
        /// в полтора раза сильнее нас».</summary>
        [JsonProperty("JointAttackFearRatio")]     public float JointAttackFearRatio     { get; set; } = 1.5f;
        /// <summary>Резервная цена за единицу товара при ship-to-ship торговле, если у стека
        /// не задан <see cref="ItemStack.BasePrice"/>.</summary>
        [JsonProperty("ShipTradeFallbackPrice")]   public int   ShipTradeFallbackPrice   { get; set; } = 100;

        /// <summary>Минимальный суммарный вес стеков, при котором убегающий корабль сбрасывает груз (DropGoodsInFear).</summary>
        [JsonProperty("FearDropWeightMin")]     public int   FearDropWeightMin     { get; set; } = 20;
        /// <summary>Сколько ходов между двумя авто-сбросами груза одного корабля в состоянии страха.</summary>
        [JsonProperty("FearDropCooldownTurns")] public int   FearDropCooldownTurns { get; set; } = 3;
        /// <summary>Доля крупнейшего стека, которую убегающий сбрасывает разово (0..1).</summary>
        [JsonProperty("FearDropFraction")]      public float FearDropFraction      { get; set; } = 0.25f;

        // Текст для current_planet, когда корабль в космосе, переехал в
        // TextsConfig.Dialog.Phrases["current_planet_in_space"]: это фраза, а Tuning — числовые
        // пороги. Фразам в структурном конфиге не место (docs/modules/dialog_system.md §1).

        /// <summary>Маппинг «раса → короткое имя расового класса» для подстановки
        /// <c>&lt;DominatorClass&gt;</c> в ключах StringPools (напр., <c>{pool:Dominator.Hi&lt;DominatorClass&gt;}</c>).
        /// Если пусто — используется встроенный fallback (RaceDominators1..3 → Blazer/Keller/Terron — внутренние id серий «Пирон»/«Логос»/«Демиург»).
        /// Добавь сюда 4-ю расу синтетов и её класс — новые пулы
        /// <c>Dominator.Hi&lt;NewClass&gt;</c> подхватятся автоматически.</summary>
        [JsonProperty("DominatorClassMap")] public Dictionary<string, string> DominatorClassMap { get; set; }
    }

    public class RankStep
    {
        [JsonProperty("Rank")]     public string Rank     { get; set; }
        [JsonProperty("MinKills")] public int    MinKills { get; set; }
    }

    public class RatingStep
    {
        [JsonProperty("Level")]    public string Level    { get; set; }
        [JsonProperty("MinScore")] public float  MinScore { get; set; }
    }

    public class StockQuantConfig
    {
        [JsonProperty("Mini")]    public int Mini    { get; set; } = 50;
        [JsonProperty("Small")]   public int Small   { get; set; } = 200;
        [JsonProperty("Average")] public int Average { get; set; } = 500;
        [JsonProperty("Big")]     public int Big     { get; set; } = 1000;
    }

    public class PriceQuantConfig
    {
        [JsonProperty("Mini")]    public float Mini    { get; set; } = 0.6f;
        [JsonProperty("Small")]   public float Small   { get; set; } = 0.9f;
        [JsonProperty("Average")] public float Average { get; set; } = 1.3f;
        [JsonProperty("Big")]     public float Big     { get; set; } = 2.0f;
    }

    public class DialogTree
    {
        [JsonProperty("Title")]     public string Title     { get; set; }
        [JsonProperty("Speaker")]   public string Speaker   { get; set; }
        [JsonProperty("StartNode")] public string StartNode { get; set; }
        [JsonProperty("Nodes")]     public Dictionary<string, DialogNode> Nodes { get; set; } = new();

        /// <summary>Псевдоним: если задан, при резолве dialogId эта запись прозрачно
        /// переадресуется на указанный id. Позволяет использовать один общий диалог под
        /// несколькими кластерными именами без копирования:
        /// <code>"Ship_Coalition_default": { "AliasOf": "Ship_default" }</code>
        /// Остальные поля (Title/Speaker/Nodes) игнорируются, если <c>AliasOf</c> задан.</summary>
        [JsonProperty("AliasOf")]   public string AliasOf   { get; set; }
    }

    public class DialogNode
    {
        [JsonProperty("Text")]    public string Text    { get; set; }
        [JsonProperty("Speaker")] public string Speaker { get; set; }
        [JsonProperty("OnEnter")] public List<DialogAction> OnEnter { get; set; }
        [JsonProperty("Replies")] public List<DialogReply> Replies { get; set; } = new();

        /// <summary>Варианты текста узла по условию: берётся первый подошедший, иначе
        /// <see cref="Text"/>. Нужны узлам-исходам, где реплика NPC зависит от кода причины
        /// (<c>extort_reason == "Accepted"</c> и т.п.).
        /// <br/>
        /// Без этого приходилось раскладывать реплики NPC по условным <see cref="DialogReply"/> —
        /// и игрок «нажимал» слова собеседника вместо своих. Реплика NPC принадлежит тексту узла,
        /// ответы — игроку; см. docs/modules/dialog_system.md §5.</summary>
        [JsonProperty("TextVariants")] public List<DialogTextVariant> TextVariants { get; set; }
    }

    /// <summary>Условный вариант текста узла. <see cref="Condition"/> — тот же синтаксис, что у
    /// реплик (тег-DSL либо <c>lua:</c>); пустое условие означает «подходит всегда».</summary>
    public class DialogTextVariant
    {
        [JsonProperty("Condition")] public string Condition { get; set; }
        [JsonProperty("Text")]      public string Text      { get; set; }
    }

    public class DialogReply
    {
        [JsonProperty("Text")]      public string Text      { get; set; }
        [JsonProperty("NextNode")]  public string NextNode  { get; set; }
        [JsonProperty("Condition")] public string Condition { get; set; }
        /// <summary>Действия, выполняемые ПЕРЕД <see cref="Actions"/> (симметрично с <see cref="DialogNode.OnEnter"/>).
        /// В отличие от Actions, значение семантически «эффекты входа в переход».
        /// Порядок: reply.OnEnter → reply.Actions → node.OnEnter (следующего узла).</summary>
        [JsonProperty("OnEnter")]   public List<DialogAction> OnEnter { get; set; }
        [JsonProperty("Actions")]   public List<DialogAction> Actions { get; set; }
    }

    public class DialogAction
    {
        /// <summary>Имя действия из реестра <see cref="DialogService.RegisterAction"/>. Legacy-путь.</summary>
        [JsonProperty("Method")] public string Method { get; set; }
        /// <summary>Аргументы для <see cref="Method"/>-действия. В Lua-режиме доступны как таблица <c>args</c>.</summary>
        [JsonProperty("Args")]   public List<string> Args { get; set; } = new();
        /// <summary>Lua-выражение или блок statement'ов. Если задан — исполняется через <see cref="SRG.Scripting.LuaHost"/>
        /// в CoreScript с локалами <c>ctx</c> (<see cref="DialogContext"/>), <c>player</c>, <c>target</c>, <c>planet</c>,
        /// <c>args</c>. <see cref="Method"/> при этом игнорируется.</summary>
        [JsonProperty("Script")] public string Script { get; set; }
    }

    public class DialogUIConfig
    {
        // Космос: маленькая панель в правом верхнем углу экрана
        [JsonProperty("SpaceWidth")]          public float SpaceWidth          { get; set; } = 360f;
        [JsonProperty("SpaceHeight")]         public float SpaceHeight         { get; set; } = 380f;
        [JsonProperty("SpaceMargin")]         public float SpaceMargin         { get; set; } = 12f;
        [JsonProperty("SpaceRepliesHeight")]  public float SpaceRepliesHeight  { get; set; } = 160f;

        // Планета: широкая панель в левой части формы планеты
        [JsonProperty("PlanetWidth")]         public float PlanetWidth         { get; set; } = 460f;
        [JsonProperty("PlanetHeight")]        public float PlanetHeight        { get; set; } = 560f;
        [JsonProperty("PlanetMargin")]        public float PlanetMargin        { get; set; } = 24f;
        [JsonProperty("PlanetRepliesHeight")] public float PlanetRepliesHeight { get; set; } = 240f;

        // Подпись кнопки закрытия переехала в TextsConfig.Dialog.Phrases["ui_close"]
        // (и значок крестика — в "ui_close_icon"): это тоже фразы.
        // Шрифт: имя ресурса (Resources.GetBuiltinResource<Font>). По умолчанию — LegacyRuntime.ttf.
        [JsonProperty("FontResource")]        public string FontResource       { get; set; } = "LegacyRuntime.ttf";
        // Canvas sorting order (выше PlanetUI=50 и ShipScan=55).
        [JsonProperty("SortingOrder")]        public int    SortingOrder       { get; set; } = 70;

        // Кегль
        [JsonProperty("TitleFontSize")]       public int    TitleFontSize      { get; set; } = 14;
        [JsonProperty("BodyFontSize")]        public int    BodyFontSize       { get; set; } = 14;
        [JsonProperty("ReplyFontSize")]       public int    ReplyFontSize      { get; set; } = 13;
        [JsonProperty("CloseIconFontSize")]   public int    CloseIconFontSize  { get; set; } = 18;
        [JsonProperty("TitleHeight")]         public float  TitleHeight        { get; set; } = 32f;

        // Палитра. Формат "#RRGGBB" или "#RRGGBBAA". Пусто/нераспарсенное → дефолт.
        [JsonProperty("ColorBackground")]     public string ColorBackground    { get; set; } = "#0A121F/F5";
        [JsonProperty("ColorTitleBg")]        public string ColorTitleBg       { get; set; } = "#0F1929/FF";
        [JsonProperty("ColorPanel")]          public string ColorPanel         { get; set; } = "#0C1724/FF";
        [JsonProperty("ColorAccent")]         public string ColorAccent        { get; set; } = "#8CD1FF/FF";
        [JsonProperty("ColorText")]           public string ColorText          { get; set; } = "#E0EBFA/FF";
        [JsonProperty("ColorReplyBtn")]       public string ColorReplyBtn      { get; set; } = "#213859/FF";
        [JsonProperty("ColorReplyHover")]     public string ColorReplyHover    { get; set; } = "#2E578C/FF";
        [JsonProperty("ColorCloseBg")]        public string ColorCloseBg       { get; set; } = "#731A1A/EB";
        [JsonProperty("ColorScrollbar")]      public string ColorScrollbar     { get; set; } = "#141929/F2";
        [JsonProperty("ColorScrollHandle")]   public string ColorScrollHandle  { get; set; } = "#4C8CCC/F2";
    }
}
