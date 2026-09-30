using System;
using System.Collections.Generic;
using System.Threading;
using SRG.Simulation;

namespace SRG.Core
{
    /// <summary>
    /// <see cref="IMainThreadDispatcher"/> для Unity: запросы из потока расчёта хода к Unity API,
    /// UI и визуалу выполняются на главном потоке.
    ///
    ///   • Send — поток расчёта ждёт; главный обслуживает очередь каждый кадр (<see cref="PumpSends"/>).
    ///   • Post — копятся и выполняются после расчёта (<see cref="DrainPosts"/>) в порядке вызовов:
    ///     во время расчёта главный поток не должен читать мир, а уведомления (лог, новости,
    ///     визуал новых кораблей) читают его.
    /// Создаётся и обслуживается <see cref="GalaxyManager"/>.
    /// </summary>
    public sealed class MainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly int _mainThreadId;
        private readonly object _lock = new();
        private readonly Queue<Action> _sends = new();
        private readonly Queue<Action> _posts = new();

        /// <summary>Создавать на главном потоке.</summary>
        public MainThreadDispatcher() => _mainThreadId = Thread.CurrentThread.ManagedThreadId;

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public void Post(Action action)
        {
            if (action == null) return;
            if (IsMainThread) { action(); return; }
            lock (_lock) _posts.Enqueue(action);
        }

        public T Send<T>(Func<T> func)
        {
            if (IsMainThread) return func();

            T result = default;
            Exception error = null;
            using var done = new ManualResetEventSlim(false);
            lock (_lock)
                _sends.Enqueue(() =>
                {
                    try { result = func(); }
                    catch (Exception e) { error = e; }
                    finally { done.Set(); }
                });
            done.Wait();
            if (error != null)
                throw new InvalidOperationException("Ошибка при выполнении вызова на главном потоке", error);
            return result;
        }

        /// <summary>Выполнить ожидающие Send-запросы. Вызывается главным потоком каждый кадр,
        /// пока идёт расчёт хода.</summary>
        public void PumpSends()
        {
            while (true)
            {
                Action next;
                lock (_lock)
                {
                    if (_sends.Count == 0) return;
                    next = _sends.Dequeue();
                }
                next();
            }
        }

        /// <summary>Выполнить отложенные Post-уведомления. Вызывается главным потоком после
        /// окончания расчёта хода. Исключение одного уведомления не теряет остальные.</summary>
        public void DrainPosts()
        {
            while (true)
            {
                Action next;
                lock (_lock)
                {
                    if (_posts.Count == 0) return;
                    next = _posts.Dequeue();
                }
                try { next(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }
    }
}
