using NUnit.Framework;
using SRG.Utils;

namespace SRG.Tests
{
    public class StableHashTests
    {
        [Test]
        public void MatchesFnv1aReferenceVectors()
        {
            // Эталонные значения FNV-1a 32 для ASCII.
            Assert.AreEqual(unchecked((int)0x811C9DC5u), StableHash.Of(""));
            Assert.AreEqual(unchecked((int)0xE40C292Cu), StableHash.Of("a"));
            Assert.AreEqual(unchecked((int)0xBF9CF968u), StableHash.Of("foobar"));
        }

        [Test]
        public void Null_ReturnsZero() => Assert.AreEqual(0, StableHash.Of(null));

        [Test]
        public void IsCaseSensitive() => Assert.AreNotEqual(StableHash.Of("Uid"), StableHash.Of("uid"));
    }
}
