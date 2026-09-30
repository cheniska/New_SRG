using System;
using UnityEngine;

namespace SRG.Simulation
{
    /// <summary>
    /// Детерминированный генератор случайных чисел симуляции (xoshiro128**).
    ///
    /// Вся игровая логика (генерация галактики, бой, экономика, ИИ, спавн, Lua) берёт случайность
    /// только отсюда. Визуал продолжает пользоваться <see cref="UnityEngine.Random"/> — поэтому
    /// анимации, частицы и фон больше не сдвигают последовательность симуляции, а один и тот же
    /// сид + одни и те же действия игрока дают один и тот же результат.
    ///
    /// Состояние (<see cref="GetState"/>/<see cref="SetState"/>) сохраняется в сейв, чтобы после
    /// загрузки игра продолжалась так же, как продолжилась бы без сохранения.
    ///
    /// Семантика методов совпадает с <see cref="UnityEngine.Random"/>: <see cref="Range(int,int)"/>
    /// не включает max, <see cref="Range(float,float)"/> и <see cref="Value"/> включают обе границы.
    ///
    /// <para><b>Потоки звёзд.</b> Статические методы берут случайность из <i>текущего</i> потока:
    /// по умолчанию — общего, а внутри <see cref="Use"/> — из заданного. Расчёт звезды за день
    /// идёт в своём потоке (<see cref="Derive"/> от ключа дня галактики и Uid звезды): звёзды не
    /// делят последовательность, их результат не зависит от порядка обхода — это условие для
    /// параллельного расчёта звёзд. Текущий поток — свой у каждого потока выполнения.</para>
    /// </summary>
    public static class GameRng
    {
        private static readonly Stream Shared = new Stream(Environment.TickCount);

        [ThreadStatic] private static Stream _current;

        private static Stream Current => _current ?? Shared;

        /// <summary>Текущий поток симуляции (общий или заданный <see cref="Use"/>) — для API,
        /// которые принимают <see cref="Stream"/>.</summary>
        public static Stream SharedStream => Current;

        /// <summary>Сделать <paramref name="stream"/> текущим до Dispose возвращённой области.</summary>
        public static Scope Use(Stream stream)
        {
            var prev = _current;
            _current = stream;
            return new Scope(prev);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly Stream _prev;
            internal Scope(Stream prev) => _prev = prev;
            public void Dispose() => _current = _prev;
        }

        /// <summary>128-битный ключ из текущего потока (например, ключ дня галактики), из которого
        /// затем выводятся независимые потоки (<see cref="Derive"/>).</summary>
        public static uint[] NextKey()
        {
            var c = Current;
            return new[] { c.NextUInt(), c.NextUInt(), c.NextUInt(), c.NextUInt() };
        }

        /// <summary>Поток, однозначно определяемый ключом и строкой (Uid звезды и т.п.). Разные
        /// строки при одном ключе дают независимые последовательности; общий поток не расходуется.
        /// Состояние 128-битное: совпасть потоки могут лишь при совпадении ключа, а ключ новый
        /// каждый день.</summary>
        public static Stream Derive(uint[] key, string salt) => new Stream(key, SRG.Utils.StableHash.Of(salt ?? ""));

        /// <summary>Локальный поток, однозначно определяемый ключом (для детерминированных
        /// «случайных» свойств справочных данных; общий поток не расходует).</summary>
        public static Stream StreamFor(string key) => new Stream(SRG.Utils.StableHash.Of(key ?? ""));

        /// <summary>Пересеять поток (новая игра, тесты).</summary>
        public static void InitState(int seed) => Shared.InitState(seed);

        /// <summary>Снимок состояния для сохранения.</summary>
        public static uint[] GetState() => Shared.GetState();

        /// <summary>Восстановить состояние из сейва. Некорректный снимок игнорируется.</summary>
        public static void SetState(uint[] state) => Shared.SetState(state);

        /// <summary>[0..1] включительно.</summary>
        public static float Value => Shared.Value;

        /// <summary>[min..max) для целых; при min == max возвращает min.</summary>
        public static int Range(int minInclusive, int maxExclusive) => Shared.Range(minInclusive, maxExclusive);

        /// <summary>[min..max] для float.</summary>
        public static float Range(float minInclusive, float maxInclusive) => Shared.Range(minInclusive, maxInclusive);

        /// <summary>Случайная точка внутри единичного круга.</summary>
        public static Vector2 InsideUnitCircle => Shared.InsideUnitCircle;

        /// <summary>Уникальный идентификатор в формате GUID ("xxxxxxxx-xxxx-…"), но из потока
        /// симуляции: при том же сиде объекты получают те же UID, а значит одинаковы и
        /// производные от UID сиды и порядок обхода словарей.</summary>
        public static string NewUid() => Shared.NewUid();

        /// <summary>Независимый <see cref="System.Random"/>, засеянный из потока симуляции
        /// (для API, которые принимают System.Random).</summary>
        public static System.Random CreateSystemRandom() => new System.Random(Shared.NextInt());

        /// <summary>
        /// Отдельный поток со своим состоянием — для кода, которому нужна локальная
        /// воспроизводимая последовательность, не влияющая на общий поток.
        /// </summary>
        public sealed class Stream
        {
            private uint _s0, _s1, _s2, _s3;

            public Stream(int seed) => InitState(seed);

            /// <summary>Состояние = ключ XOR развёртка соли (SplitMix32) — см. <see cref="GameRng.Derive"/>.</summary>
            public Stream(uint[] key, int salt)
            {
                uint x = unchecked((uint)salt);
                _s0 = key[0] ^ SplitMix(ref x); _s1 = key[1] ^ SplitMix(ref x);
                _s2 = key[2] ^ SplitMix(ref x); _s3 = key[3] ^ SplitMix(ref x);
                if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 0x6A09E667u; // нулевое состояние вырождено
                // Прогрев: соседние соли дают близкие состояния — несколько шагов их разводят.
                for (int i = 0; i < 8; i++) NextUInt();
            }

            public void InitState(int seed)
            {
                // SplitMix32 разворачивает 32-битный сид в 128 бит состояния без нулевых слов.
                uint x = unchecked((uint)seed);
                _s0 = SplitMix(ref x); _s1 = SplitMix(ref x); _s2 = SplitMix(ref x); _s3 = SplitMix(ref x);
            }

            public uint[] GetState() => new[] { _s0, _s1, _s2, _s3 };

            public void SetState(uint[] state)
            {
                if (state == null || state.Length != 4) return;
                if ((state[0] | state[1] | state[2] | state[3]) == 0) return; // нулевое состояние вырождено
                _s0 = state[0]; _s1 = state[1]; _s2 = state[2]; _s3 = state[3];
            }

            public uint NextUInt()
            {
                unchecked
                {
                    uint result = RotL(_s1 * 5, 7) * 9;
                    uint t = _s1 << 9;
                    _s2 ^= _s0;
                    _s3 ^= _s1;
                    _s1 ^= _s2;
                    _s0 ^= _s3;
                    _s2 ^= t;
                    _s3 = RotL(_s3, 11);
                    return result;
                }
            }

            public int NextInt() => unchecked((int)NextUInt());

            public string NewUid()
            {
                var bytes = new byte[16];
                for (int i = 0; i < 16; i += 4)
                {
                    uint v = NextUInt();
                    bytes[i] = (byte)v; bytes[i + 1] = (byte)(v >> 8); bytes[i + 2] = (byte)(v >> 16); bytes[i + 3] = (byte)(v >> 24);
                }
                // Биты версии/варианта как у GUID v4 — формат неотличим от Guid.NewGuid().
                bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
                bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
                return new Guid(bytes).ToString();
            }

            public float Value => (NextUInt() >> 8) * (1f / 16777215f);

            public int Range(int minInclusive, int maxExclusive)
            {
                if (minInclusive == maxExclusive) return minInclusive;
                if (minInclusive < maxExclusive)
                {
                    ulong span = (ulong)((long)maxExclusive - minInclusive);
                    return (int)(minInclusive + (long)((NextUInt() * span) >> 32));
                }
                // Как у UnityEngine.Random: при min > max результат в (max..min].
                ulong back = (ulong)((long)minInclusive - maxExclusive);
                return (int)(minInclusive - (long)((NextUInt() * back) >> 32));
            }

            public float Range(float minInclusive, float maxInclusive)
                => minInclusive + (maxInclusive - minInclusive) * Value;

            public Vector2 InsideUnitCircle
            {
                get
                {
                    // Отбор из квадрата: в среднем ~1.27 итерации, результат детерминирован.
                    while (true)
                    {
                        float x = Value * 2f - 1f;
                        float y = Value * 2f - 1f;
                        if (x * x + y * y <= 1f) return new Vector2(x, y);
                    }
                }
            }

            private static uint RotL(uint x, int k) => (x << k) | (x >> (32 - k));

            private static uint SplitMix(ref uint x)
            {
                unchecked
                {
                    uint z = x += 0x9E3779B9u;
                    z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
                    z = (z ^ (z >> 13)) * 0xC2B2AE35u;
                    z ^= z >> 16;
                    return z == 0 ? 0x6A09E667u : z;
                }
            }
        }
    }
}
