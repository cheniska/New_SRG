using System;

namespace SRG.Utils
{
    /// <summary>
    /// Игровой лог для сообщений симуляции (бой, экономика, наука и т.д.).
    /// Симуляция пишет сюда и ничего не знает о том, кто это отображает:
    /// консоль (<c>SRG.UI.Screens.GameConsoleController</c>) подписывается на <see cref="OnEntry"/>.
    /// Если подписчиков нет (тесты, headless-прогон) — сообщение просто отбрасывается.
    /// </summary>
    public static class GameLog
    {
        public static event Action<string> OnEntry;

        public static void Add(string message) => OnEntry?.Invoke(message);
    }
}
