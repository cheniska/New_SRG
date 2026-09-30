using System;

namespace SRG.Simulation
{
    /// <summary>
    /// Доставка вызовов на главный поток Unity. Реализуется слоем приложения
    /// (<c>SRG.Core.MainThreadDispatcher</c>); в тестах и headless-прогоне не подключается.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        bool IsMainThread { get; }
        /// <summary>Выполнить на главном потоке позже — после расчёта хода, в порядке постановки.</summary>
        void Post(Action action);
        /// <summary>Выполнить на главном потоке и дождаться результата (главный поток обслуживает
        /// такие запросы каждый кадр, пока идёт расчёт хода).</summary>
        T Send<T>(Func<T> func);
    }

    /// <summary>
    /// Точка, через которую код, работающий во время расчёта хода, обращается к тому, что живёт
    /// только на главном потоке: Unity API (Resources, Sprite, Application), UI и визуал.
    ///
    /// Ход считается в фоновом потоке (см. <c>GalaxyManager</c>), а Unity API и MonoBehaviour-ы
    /// доступны только с главного. Без диспетчера (тесты, headless) и на главном потоке
    /// вызовы выполняются сразу — поведение то же, что до выноса расчёта в поток.
    /// </summary>
    public static class MainThread
    {
        public static IMainThreadDispatcher Dispatcher { get; set; }

        /// <summary>true — текущий поток главный (или диспетчера нет: всё выполняется на месте).</summary>
        public static bool IsCurrent => Dispatcher?.IsMainThread ?? true;

        /// <summary>Уведомление без результата (лог, UI, создание визуала): на главном потоке —
        /// сразу, из расчёта хода — после его окончания, в порядке вызовов.</summary>
        public static void Post(Action action)
        {
            if (action == null) return;
            var d = Dispatcher;
            if (d == null || d.IsMainThread) action();
            else d.Post(action);
        }

        /// <summary>Вызов с результатом: из расчёта хода блокирует поток расчёта до выполнения
        /// на главном (обычно до следующего кадра). Дорого — кэшируйте результат.</summary>
        public static T Send<T>(Func<T> func)
        {
            var d = Dispatcher;
            if (d == null || d.IsMainThread) return func();
            return d.Send(func);
        }

        public static void Send(Action action)
            => Send<bool>(() => { action(); return true; });
    }
}
