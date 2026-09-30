using System;
using Newtonsoft.Json;
using UnityEngine;
using SRG.Simulation;

namespace SRG.Galaxy
{
    /// <summary>
    /// Фаза жизненного цикла червоточины. В отличие от гиперпрыжка, здесь фазы описывают ТОЛЬКО
    /// визуал сама червоточина как объект — вход в неё (перенос корабля) идёт через отдельную
    /// логику <see cref="Ships.Movement.HyperjumpController"/>.
    /// </summary>
    public enum WormholePhase
    {
        Opening = 0,   // играется клип open один раз (≈1 ход симуляции).
        Open    = 1,   // клип cycle идёт по кругу N ходов (см. GameSettings.Wormhole_OpenTurns).
        Closing = 2,   // играется клип close один раз (≈1 ход). После — червоточина удаляется.
    }

    /// <summary>
    /// Активная червоточина в системе. Существует ~N ходов, соединяет CurrentStar с TargetStar.
    /// Позиция фиксирована на весь срок жизни. Вход в червоточину — как гиперпрыжок с точкой
    /// входа в WormholePosition; топливо/дальность не проверяются, точка выхода в целевой
    /// системе — случайная.
    /// </summary>
    [Serializable]
    public class WormholeData
    {
        public string Uid { get; set; } = GameRng.NewUid();

        /// <summary>Позиция червоточины в системе-источнике (мировые координаты).</summary>
        public Vector2 Position { get; set; }

        /// <summary>UID системы, куда червоточина ведёт.</summary>
        public string TargetStarUid { get; set; }

        /// <summary>Идентификатор целевой галактики. null/пусто → та же галактика, что источник
        /// (обычный случай). Заполняется скриптом для межгалактических проходов. Runtime сейчас
        /// одногалактический — при попытке входа в такую червоточину логируется предупреждение,
        /// поле сохранено для будущей мультигалактической поддержки.</summary>
        public string TargetGalaxyId { get; set; }

        /// <summary>Фиксированная точка выхода в целевой системе. null → каждый вход выбирает
        /// случайную точку в 30..80% радиуса целевой системы (используется по умолчанию для
        /// естественных червоточин). Указывается скриптом для «сюжетных» проходов.</summary>
        public Vector2? FixedTargetPosition { get; set; }

        /// <summary>Переопределение путей к спрайтам (opening/cycle/closing/icon). null/пусто →
        /// используется дефолт из <see cref="Config.GameSettingsConfig"/>. Позволяет скрипту создать
        /// уникальную по внешнему виду червоточину (например, для сюжетного «портала предтеч»).</summary>
        public WormholeGraphics Graphics { get; set; }

        /// <summary>Переопределение длительности фазы Open. -1 → берём GameSettings.Wormhole_OpenTurns.
        /// Значение применяется при переходе Opening→Open, поэтому изменение после создания
        /// не действует (используйте <see cref="OpenTurnsRemaining"/> для точечной коррекции).</summary>
        public int LifetimeOverride { get; set; } = -1;

        /// <summary>Текущая фаза.</summary>
        public WormholePhase Phase { get; set; } = WormholePhase.Opening;

        /// <summary>Сколько ходов уже прошло в текущей фазе. Инкрементится в WormholeSystem.</summary>
        public int DaysInPhase { get; set; }

        /// <summary>Сколько ходов ещё должно длиться фаза Open. Инициализируется при переходе в Open
        /// (значением из <see cref="LifetimeOverride"/> либо из GameSettings). Скрипт может задать
        /// новое значение напрямую — WormholeSystem каждый ход декрементит его.</summary>
        public int OpenTurnsRemaining { get; set; }

        /// <summary>Ход, на котором червоточина была создана. Для аналитики/UI.</summary>
        public int CreatedTurn { get; set; }

        /// <summary>UID парной червоточины в целевой системе. Каждая червоточина создаётся сразу
        /// с парой: одна в исходной системе, вторая в целевой. Пара живёт синхронно (одинаковые
        /// фазы Opening/Open/Closing) и закрывается одновременно. Позволяет показать выход как
        /// такую же червоточину (визуал/клик) в целевой системе — без «гипертоннеля» на прибытии.
        /// null → одиночная (совместимость со скриптами/сейвами без пары).</summary>
        public string PairedWormholeUid { get; set; }

        /// <summary>true — червоточину не следует использовать: корабль уже внутри неё пролетает
        /// (одноразовый вход). Не используется в текущей версии — оставлено на будущее.</summary>
        [JsonIgnore] public bool IsExpired => Phase == WormholePhase.Closing && DaysInPhase >= 1;
    }

    /// <summary>Опциональный набор путей к спрайтам конкретной червоточины. Любое поле может
    /// быть null/пустым — тогда используется дефолт из <see cref="Config.GameSettingsConfig"/>.
    /// Все пути — относительно Resources.</summary>
    [Serializable]
    public class WormholeGraphics
    {
        public string OpeningPath { get; set; }
        public string CyclePath   { get; set; }
        public string ClosingPath { get; set; }
        public string IconPath    { get; set; }

        /// <summary>Короткая фабрика: <c>WormholeGraphics.Of("...open", "...cycle", "...close")</c>.
        /// Любой аргумент может быть null — тогда для этой фазы возьмётся дефолт из настроек.</summary>
        public static WormholeGraphics Of(string opening, string cycle = null, string closing = null, string icon = null)
            => new WormholeGraphics { OpeningPath = opening, CyclePath = cycle, ClosingPath = closing, IconPath = icon };
    }
}
