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
    /// </summary>
    public static class GameRng
    {
        private static readonly Stream Shared = new Stream(Environment.TickCount);

        /// <summary>Общий поток симуляции — для API, которые принимают <see cref="Stream"/>.</summary>
        public static Stream SharedStream => Shared;

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
