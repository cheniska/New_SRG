using System.Collections.Generic;

namespace SRG.UI.Logic
{
    /// <summary>Итог действия игрока в UI: удалось ли оно и что сообщить в лог.</summary>
    public sealed class UiActionResult
    {
        public bool Success { get; }
        /// <summary>Сообщения для игрового лога (в порядке появления).</summary>
        public IReadOnlyList<string> Messages { get; }

        private UiActionResult(bool success, IReadOnlyList<string> messages)
        {
            Success = success;
            Messages = messages;
        }

        public static UiActionResult Ok(params string[] messages) => new(true, messages);
        public static UiActionResult Fail(params string[] messages) => new(false, messages);
        /// <summary>Ничего не произошло и сообщать нечего (нет корабля, предмет исчез и т.п.).</summary>
        public static UiActionResult None() => new(false, System.Array.Empty<string>());
    }

    /// <summary>Параметры диалога выбора количества (купить/продать N единиц).</summary>
    public sealed class QuantityPrompt
    {
        public string Title { get; }
        public int Max { get; }
        public int UnitPrice { get; }
        public string ConfirmLabel { get; }

        public QuantityPrompt(string title, int max, int unitPrice, string confirmLabel)
        {
            Title = title; Max = max; UnitPrice = unitPrice; ConfirmLabel = confirmLabel;
        }

        /// <summary>Подпись счётчика: «N / max   (сумма кр.)».</summary>
        public string FormatAmount(int amount) => $"{amount} / {Max}   ({amount * UnitPrice} кр.)";
    }
}
