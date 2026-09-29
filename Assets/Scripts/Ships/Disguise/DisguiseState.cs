using System.Collections.Generic;
using Newtonsoft.Json;

namespace SRG.Ships.Disguise
{
    /// <summary>
    /// Состояние активного камуфляжа корабля. Прикрепляется к <see cref="SRG.Galaxy.ShipData"/>.
    /// Пусто (null) = маска не надета. Живёт до явной деактивации (снятие камуфляжа).
    /// </summary>
    /// <remarks>
    /// Ключевые инварианты:
    ///  * DetectedByRaces сбрасывается при деактивации, а не «навсегда» (см. описание задачи).
    ///  * Настройки (TargetRace/TargetOwner/VisualPath/DisplayName) — снимок из ScriptParams
    ///    item-конфига в момент активации. Изменение конфига после активации не «перемаскирует»
    ///    уже надетый камуфляж.
    /// </remarks>
    public class DisguiseState
    {
        /// <summary>Uid item-инстанса, вокруг которого построена активная маска.
        /// Нужен для повторной активации/деактивации одного и того же предмета.</summary>
        [JsonProperty] public string SourceItemUid { get; set; }

        /// <summary>Раса, под которую маскируемся (например, "RaceDominators1"). null = без замены расы.</summary>
        [JsonProperty] public string TargetRace { get; set; }

        /// <summary>Owner (сторона), под которого маскируемся (например, "Dominators"). null = без замены.</summary>
        [JsonProperty] public string TargetOwner { get; set; }

        /// <summary>Путь к спрайту корпуса, накладываемому на время маскировки.</summary>
        [JsonProperty] public string VisualPath { get; set; }

        /// <summary>Отображаемое имя (для UI/консоли), опционально.</summary>
        [JsonProperty] public string DisplayName { get; set; }

        /// <summary>Расы, которые уже раскрыли эту маску. Сравнение по <c>ShipData.Race</c>.
        /// Сбрасывается при деактивации.</summary>
        [JsonProperty] public HashSet<string> DetectedByRaces { get; set; } = new HashSet<string>();

        /// <summary>Оригинальный <see cref="SRG.Galaxy.ShipData.CustomBodyGraphicPath"/> до активации.
        /// Восстанавливается при деактивации. null означает «был без override».</summary>
        [JsonProperty] public string SavedVisualPath { get; set; }
    }
}
