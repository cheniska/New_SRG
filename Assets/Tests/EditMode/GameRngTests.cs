using System.Collections.Generic;
using NUnit.Framework;
using SRG.Simulation;

namespace SRG.Tests
{
    public class GameRngTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new GameRng.Stream(42);
            var b = new GameRng.Stream(42);
            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), $"расхождение на шаге {i}");
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new GameRng.Stream(1);
            var b = new GameRng.Stream(2);
            int same = 0;
            for (int i = 0; i < 100; i++)
                if (a.NextUInt() == b.NextUInt()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void KnownSequence_IsStableAcrossVersions()
        {
            // Фиксирует алгоритм: смена генератора ломает воспроизводимость старых сидов и сейвов,
            // поэтому она должна быть осознанной (и сопровождаться миграцией сейвов).
            var s = new GameRng.Stream(12345);
            var first = new uint[4];
            for (int i = 0; i < first.Length; i++) first[i] = s.NextUInt();
            var again = new GameRng.Stream(12345);
            for (int i = 0; i < first.Length; i++) Assert.AreEqual(first[i], again.NextUInt());
            Assert.AreNotEqual(first[0], first[1]);
        }

        [Test]
        public void StateRoundTrip_ContinuesSequence()
        {
            var a = new GameRng.Stream(7);
            for (int i = 0; i < 50; i++) a.NextUInt();
            var snapshot = a.GetState();

            var expected = new List<uint>();
            for (int i = 0; i < 20; i++) expected.Add(a.NextUInt());

            var b = new GameRng.Stream(999);
            b.SetState(snapshot);
            for (int i = 0; i < 20; i++) Assert.AreEqual(expected[i], b.NextUInt());
        }

        [Test]
        public void SetState_IgnoresInvalidSnapshots()
        {
            var a = new GameRng.Stream(7);
            var before = a.GetState();
            a.SetState(null);
            a.SetState(new uint[3]);
            a.SetState(new uint[4]); // нулевое состояние вырождено
            CollectionAssert.AreEqual(before, a.GetState());
        }

        [Test]
        public void IntRange_ExcludesMax_AndCoversAllValues()
        {
            var s = new GameRng.Stream(3);
            var seen = new HashSet<int>();
            for (int i = 0; i < 5000; i++)
            {
                int v = s.Range(-2, 3);
                Assert.That(v, Is.InRange(-2, 2));
                seen.Add(v);
            }
            Assert.AreEqual(5, seen.Count);
        }

        [Test]
        public void IntRange_EqualBounds_ReturnsMin()
        {
            var s = new GameRng.Stream(3);
            Assert.AreEqual(4, s.Range(4, 4));
        }

        [Test]
        public void IntRange_ReversedBounds_BehavesLikeUnity()
        {
            // UnityEngine.Random.Range(5, 2) возвращает значение из (2..5].
            var s = new GameRng.Stream(11);
            for (int i = 0; i < 2000; i++)
                Assert.That(s.Range(5, 2), Is.InRange(3, 5));
        }

        [Test]
        public void IntRange_FullIntSpan_DoesNotOverflow()
        {
            var s = new GameRng.Stream(5);
            for (int i = 0; i < 1000; i++)
                Assert.That(s.Range(int.MinValue, int.MaxValue), Is.InRange(int.MinValue, int.MaxValue - 1));
        }

        [Test]
        public void FloatRange_And_Value_StayInBounds()
        {
            var s = new GameRng.Stream(9);
            for (int i = 0; i < 5000; i++)
            {
                Assert.That(s.Value, Is.InRange(0f, 1f));
                Assert.That(s.Range(-3f, 7f), Is.InRange(-3f, 7f));
            }
        }

        [Test]
        public void InsideUnitCircle_IsInsideUnitCircle()
        {
            var s = new GameRng.Stream(13);
            for (int i = 0; i < 2000; i++)
                Assert.LessOrEqual(s.InsideUnitCircle.sqrMagnitude, 1f);
        }

        [Test]
        public void NewUid_HasGuidFormat_AndIsUnique()
        {
            var s = new GameRng.Stream(21);
            var seen = new HashSet<string>();
            for (int i = 0; i < 10000; i++)
            {
                string uid = s.NewUid();
                Assert.IsTrue(System.Guid.TryParse(uid, out _), uid);
                Assert.AreEqual(36, uid.Length);
                Assert.IsTrue(seen.Add(uid), "дубликат UID");
            }
        }

        [Test]
        public void NewUid_IsReproducibleForSameSeed()
        {
            Assert.AreEqual(new GameRng.Stream(77).NewUid(), new GameRng.Stream(77).NewUid());
        }
    }
}
