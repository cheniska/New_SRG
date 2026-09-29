using System.Collections.Generic;
using UnityEngine;
using SRG.Config;
using SRG.Galaxy;

namespace SRG.Equipment
{
    /// <summary>Результат выполнения UseCode-скрипта. Возвращается из
    /// <see cref="IArtefactActionScript.TryUse"/>; вызывающий (ActivateItem) применяет
    /// пост-эффекты: consume/wear/лог. Message при пустой строке в консоль не пишется.</summary>
    public readonly struct ActionResult
    {
        public readonly bool Success;
        public readonly bool Consume;
        public readonly int  WearApplied;
        public readonly string Message;

        private ActionResult(bool success, bool consume, int wear, string message)
        {
            Success = success; Consume = consume; WearApplied = wear; Message = message;
        }

        public static ActionResult Ok(string msg = null, bool consume = false, int wear = 0)
            => new ActionResult(true, consume, wear, msg);

        public static ActionResult Fail(string msg = null)
            => new ActionResult(false, false, 0, msg);
    }

    public interface IArtefactActionScript
    {
        ActionResult TryUse(ShipData ship, ItemInstance item, ItemsConfig equipConfig);
    }

    /// <summary>Реестр UseCode-скриптов. UseCode задаётся в конфиге (<see cref="ItemConfig.UseCode"/>);
    /// EquipmentSystem.ActivateItem вызывает соответствующий обработчик до легаси-switch.
    /// Если UseCode не задан или обработчик не найден — управление возвращается к дефолтной ветке.</summary>
    public static class ArtefactActionRegistry
    {
        private static readonly Dictionary<string, IArtefactActionScript> _scripts = new();

        public static void Register(string id, IArtefactActionScript script)
        {
            if (string.IsNullOrEmpty(id) || script == null) return;
            _scripts[id] = script;
        }

        public static IArtefactActionScript Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _scripts.TryGetValue(id, out var s) ? s : null;
        }
    }
}
