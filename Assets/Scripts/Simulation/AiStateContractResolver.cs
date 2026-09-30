using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SRG.NpcAI;

namespace SRG.Simulation
{
    /// <summary>
    /// Контракт сериализации для состояния ИИ (<see cref="NpcBrain"/>, <see cref="NpcAction"/>,
    /// <see cref="NpcOrder"/> и их вложенных типов).
    ///
    /// Эти классы не проектировались как DTO: состояние живёт в приватных (часто readonly) полях,
    /// конструкторы принимают параметры и выполняют логику. Поэтому для них:
    ///   • сериализуются все поля экземпляра по всей иерархии (включая private/readonly и
    ///     backing-поля автосвойств) — снимок ровно того, что определяет поведение;
    ///   • объект при загрузке создаётся без вызова конструктора, поля восстанавливаются как были;
    ///   • поля с <see cref="NonSerializedAttribute"/> пропускаются — так помечаются кэши ссылок на
    ///     объекты мира (цель по UID ищется заново при первом обращении).
    ///
    /// Правило для нового кода ИИ: ссылки на корабли/звёзды/планеты — только через UID;
    /// прямую ссылку можно держать как кэш с [NonSerialized] (проверяется тестом AiStateSerializationTests).
    /// </summary>
    public sealed class AiStateContractResolver : DefaultContractResolver
    {
        public static bool IsAiStateType(Type t)
        {
            for (var cur = t; cur != null; cur = cur.DeclaringType)
            {
                if (typeof(NpcBrain).IsAssignableFrom(cur)) return true;
                if (typeof(NpcAction).IsAssignableFrom(cur)) return true;
                if (typeof(NpcOrder).IsAssignableFrom(cur)) return true;
            }
            return false;
        }

        /// <summary>Поля экземпляра по всей иерархии типа (от базового к производному).</summary>
        public static List<FieldInfo> StateFields(Type t)
        {
            var chain = new List<Type>();
            for (var cur = t; cur != null && cur != typeof(object) && cur != typeof(ValueType); cur = cur.BaseType)
                chain.Insert(0, cur);
            var result = new List<FieldInfo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var type in chain)
                foreach (var f in type.GetFields(flags))
                {
                    if (f.IsNotSerialized) continue;
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                    result.Add(f);
                }
            return result;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            if (!IsAiStateType(objectType)) return contract;

            contract.DefaultCreator = () => FormatterServices.GetUninitializedObject(objectType);
            contract.DefaultCreatorNonPublic = false;
            contract.CreatorParameters.Clear();
            contract.OverrideCreator = null;
            return contract;
        }

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            if (!IsAiStateType(type)) return base.CreateProperties(type, memberSerialization);

            var props = new List<JsonProperty>();
            var names = new HashSet<string>();
            foreach (var f in StateFields(type))
            {
                var p = base.CreateProperty(f, MemberSerialization.Fields);
                // Одинаковые приватные имена в базовом и производном классе — различаем по типу.
                string name = f.Name;
                if (!names.Add(name)) { name = f.DeclaringType.Name + "." + f.Name; names.Add(name); }
                p.PropertyName = name;
                p.UnderlyingName = f.Name;
                p.ValueProvider = new ReflectionValueProvider(f);
                p.Readable = true;
                p.Writable = true;
                p.Ignored = false;
                p.DeclaringType = f.DeclaringType;
                props.Add(p);
            }
            return props;
        }
    }
}
