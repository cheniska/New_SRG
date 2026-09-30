using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SRG.Simulation;

namespace SRG.Tests
{
    /// <summary>
    /// Расчёт хода в фоновом потоке (как в GalaxyManager): тот же результат, что и на главном,
    /// и ни одного обращения к API, доступному только с главного потока, мимо <see cref="MainThread"/>.
    /// </summary>
    [Category("Slow")]
    public class BackgroundTurnTests
    {
        private const int Seed = 20260930;
        private const int Days = 10;

        [Test]
        public void TurnOnWorkerThread_MatchesMainThread()
        {
            string expected;
            using (var w = TestWorld.Generate(Seed)) { w.RunDays(Days); expected = w.StateHash(); }

            string actual;
            using (var w = TestWorld.Generate(Seed))
            {
                var dispatcher = new TestDispatcher();
                MainThread.Dispatcher = dispatcher;
                SRG.Utils.RuntimePaths.Warmup();
                SetShimMainThread(Thread.CurrentThread.ManagedThreadId);
                try
                {
                    for (int i = 0; i < Days; i++) StepInBackground(w, dispatcher);
                }
                finally
                {
                    SetShimMainThread(-1);
                    MainThread.Dispatcher = null;
                }
                actual = w.StateHash();
            }
            Assert.AreEqual(expected, actual, "Ход в фоновом потоке дал другой мир, чем на главном");
        }

        /// <summary>Тот же порядок, что у GalaxyManager: расчёт в фоне (главный обслуживает Send),
        /// затем отложенные уведомления и события хода на главном потоке.</summary>
        private static void StepInBackground(TestWorld w, TestDispatcher dispatcher)
        {
            var session = w.Session;
            var task = Task.Run(() => session.SimulateDay());
            while (!task.IsCompleted)
            {
                dispatcher.PumpSends();
                Thread.Sleep(0);
            }
            dispatcher.PumpSends();
            dispatcher.DrainPosts();
            var data = task.GetAwaiter().GetResult();
            Assert.IsFalse(dispatcher.PostedFromMainThread, "Post с главного потока должен выполняться сразу");
            GameWorld.RaiseTurnCalculate(data);
            GameWorld.RaiseTurnComplete(data);
            session.FinalizeHyperjumpPhases();
        }

        // В headless-сборке заглушка UnityEngine проверяет поток так же, как Unity.
        private static void SetShimMainThread(int id)
        {
#if !UNITY_2017_1_OR_NEWER
            UnityEngine.ShimThreading.MainThreadId = id;
#endif
        }

        private sealed class TestDispatcher : IMainThreadDispatcher
        {
            private readonly int _main = Thread.CurrentThread.ManagedThreadId;
            private readonly object _lock = new();
            private readonly Queue<Action> _sends = new();
            private readonly Queue<Action> _posts = new();
            public bool PostedFromMainThread { get; private set; }

            public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _main;

            public void Post(Action action)
            {
                if (IsMainThread) { PostedFromMainThread = true; action(); return; }
                lock (_lock) _posts.Enqueue(action);
            }

            public T Send<T>(Func<T> func)
            {
                if (IsMainThread) return func();
                T result = default;
                using var done = new ManualResetEventSlim(false);
                lock (_lock) _sends.Enqueue(() => { try { result = func(); } finally { done.Set(); } });
                done.Wait();
                return result;
            }

            public void PumpSends() => Drain(_sends);
            public void DrainPosts() => Drain(_posts);

            private void Drain(Queue<Action> queue)
            {
                while (true)
                {
                    Action next;
                    lock (_lock) { if (queue.Count == 0) return; next = queue.Dequeue(); }
                    next();
                }
            }
        }
    }
}
