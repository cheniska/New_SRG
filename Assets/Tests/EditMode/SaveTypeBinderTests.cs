using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using SRG.Galaxy;
using SRG.NpcAI;
using SRG.Utils;

namespace SRG.Tests
{
    public class SaveTypeBinderTests
    {
        private static readonly string CoreLib = typeof(List<>).Assembly.GetName().Name;

        [Test]
        public void ResolvesTypesSavedFromAssemblyCSharp()
        {
            // Сейвы до выделения asmdef-сборок.
            Assert.AreEqual(typeof(GalaxyData),
                SaveTypeBinder.Instance.BindToType("Assembly-CSharp", "SRG.Galaxy.GalaxyData"));
        }

        [Test]
        public void ResolvesTypesFromCurrentAssembly()
        {
            string asm = typeof(GalaxyData).Assembly.GetName().Name;
            Assert.AreEqual(typeof(DirectiveAttackSystem),
                SaveTypeBinder.Instance.BindToType(asm, "SRG.NpcAI.DirectiveAttackSystem"));
        }

        [Test]
        public void ResolvesLegacyNamesWithoutNamespace()
        {
            // Сейвы до введения namespace'ов (июль 2026).
            Assert.AreEqual(typeof(DirectiveAttackSystem),
                SaveTypeBinder.Instance.BindToType("Assembly-CSharp", "DirectiveAttackSystem"));
        }

        [Test]
        public void ResolvesGenericCollectionsOfLegacyTypes()
        {
            var t = SaveTypeBinder.Instance.BindToType(CoreLib,
                "System.Collections.Generic.List`1[[SRG.Galaxy.StarData, Assembly-CSharp]]");
            Assert.AreEqual(typeof(List<StarData>), t);
        }

        [Test]
        public void RejectsNonGameTypes()
        {
            string asm = typeof(System.IO.FileInfo).Assembly.GetName().Name;
            Assert.Throws<JsonSerializationException>(() =>
                SaveTypeBinder.Instance.BindToType(asm, "System.IO.FileInfo"));
        }

        [Test]
        public void RejectsCollectionsOfNonGameTypes()
        {
            Assert.IsFalse(SaveTypeBinder.IsAllowed(typeof(List<System.IO.FileInfo>)));
            Assert.IsFalse(SaveTypeBinder.IsAllowed(typeof(Action)));
            Assert.IsFalse(SaveTypeBinder.IsAllowed(typeof(Type)));
        }

        [Test]
        public void AllowsPrimitivesAndGameCollections()
        {
            Assert.IsTrue(SaveTypeBinder.IsAllowed(typeof(int)));
            Assert.IsTrue(SaveTypeBinder.IsAllowed(typeof(string)));
            Assert.IsTrue(SaveTypeBinder.IsAllowed(typeof(Dictionary<string, List<StarData>>)));
            Assert.IsTrue(SaveTypeBinder.IsAllowed(typeof(UnityEngine.Vector2[])));
        }

        [Test]
        public void UnknownType_Throws()
        {
            Assert.Throws<JsonSerializationException>(() =>
                SaveTypeBinder.Instance.BindToType("Assembly-CSharp", "SRG.Nope.DoesNotExist"));
        }

        [Test]
        public void PolymorphicRoundTrip_PreservesConcreteType()
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = SaveTypeBinder.Instance,
            };
            var holder = new Holder { Items = new List<Directive> { new DirectiveAttackSystem("Kling", "star-1", 5) } };
            string json = JsonConvert.SerializeObject(holder, settings);
            StringAssert.Contains("SRG.NpcAI.DirectiveAttackSystem", json);

            var back = JsonConvert.DeserializeObject<Holder>(json, settings);
            Assert.IsInstanceOf<DirectiveAttackSystem>(back.Items[0]);
            Assert.AreEqual("star-1", ((DirectiveAttackSystem)back.Items[0]).TargetStarUid);
        }

        [Test]
        public void MaliciousTypeInSave_IsRejected()
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = SaveTypeBinder.Instance,
            };
            string asm = typeof(System.IO.FileInfo).Assembly.GetName().Name;
            string json = "{\"Payload\":{\"$type\":\"System.IO.FileInfo, " + asm + "\",\"fileName\":\"x\"}}";
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<ObjectHolder>(json, settings));
        }

        private class Holder { public List<Directive> Items; }
        private class ObjectHolder { public object Payload; }
    }
}
