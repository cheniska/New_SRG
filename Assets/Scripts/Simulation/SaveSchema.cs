using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Serialization;

namespace SRG.Simulation
{
    /// <summary>
    /// Описание формата файла сохранения: какие типы и поля попадают в JSON.
    ///
    /// Модели мира сериализуются напрямую, без отдельных DTO, поэтому формат сейва меняется всякий
    /// раз, когда меняется сериализуемая часть модели (переименовали свойство, сменили тип, сняли
    /// [JsonIgnore]). Чтобы такие изменения не проходили незаметно, схема фиксируется в репозитории
    /// (Assets/Tests/EditMode/SaveSchema.txt) и сверяется тестом SaveSchemaTests: любое расхождение —
    /// повод решить, нужна ли миграция старых сейвов (<see cref="SaveSerializer.CurrentVersion"/>).
    /// </summary>
    public static class SaveSchema
    {
        /// <summary>Схема, начиная с <see cref="SaveData"/>: строки «Тип.Поле: ТипПоля», отсортированы.</summary>
        public static string Describe()
        {
            var resolver = SaveSerializer.Settings.ContractResolver ?? new DefaultContractResolver();
            var lines = new SortedSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<Type>();
            var queue = new Queue<Type>();
            queue.Enqueue(typeof(SaveData));

            var assemblyTypes = typeof(SaveData).Assembly.GetTypes();

            while (queue.Count > 0)
            {
                var type = Unwrap(queue.Dequeue());
                if (type == null || !visited.Add(type)) continue;
                if (IsLeaf(type)) continue;

                // Полиморфные поля (Directive, NpcAction, NpcOrder…) пишут конкретный тип в $type —
                // в схему входят все конкретные наследники.
                if (type.IsAbstract || type.IsInterface || HasSubclasses(type, assemblyTypes))
                    foreach (var sub in assemblyTypes.Where(t => t != type && !t.IsAbstract && type.IsAssignableFrom(t)
                                                              && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)))
                    {
                        lines.Add($"{Name(type)} <- {Name(sub)}");
                        queue.Enqueue(sub);
                    }

                var contract = resolver.ResolveContract(type);
                switch (contract)
                {
                    case JsonObjectContract obj:
                        foreach (var p in obj.Properties)
                        {
                            if (p.Ignored || !p.Readable) continue;
                            lines.Add($"{Name(type)}.{p.PropertyName}: {Name(p.PropertyType)}");
                            queue.Enqueue(p.PropertyType);
                        }
                        break;
                    case JsonDictionaryContract dict:
                        queue.Enqueue(dict.DictionaryKeyType);
                        queue.Enqueue(dict.DictionaryValueType);
                        break;
                    case JsonArrayContract arr:
                        queue.Enqueue(arr.CollectionItemType);
                        break;
                }
            }
            return string.Join("\n", lines) + "\n";
        }

        private static bool HasSubclasses(Type t, Type[] all)
            => t.IsClass && t != typeof(object) && t.Namespace != null && t.Namespace.StartsWith("SRG", StringComparison.Ordinal)
               && all.Any(x => x != t && x.IsSubclassOf(t));

        private static Type Unwrap(Type t)
        {
            if (t == null) return null;
            if (t.IsArray) return t.GetElementType();
            var nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null) return nullable;
            if (t.IsGenericType && t.Namespace == "System.Collections.Generic")
            {
                // Коллекции разворачиваем в их элементы (ключ/значение для словарей).
                foreach (var arg in t.GetGenericArguments()) if (!IsLeaf(arg)) return arg;
                return null;
            }
            return t;
        }

        private static bool IsLeaf(Type t)
            => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(object)
               || t == typeof(DateTime) || t == typeof(Guid) || t == typeof(TimeSpan)
               || t.Namespace == "UnityEngine"
               || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ValueTuple<,>));

        private static string Name(Type t)
        {
            if (t.IsArray) return Name(t.GetElementType()) + "[]";
            var nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null) return Name(nullable) + "?";
            string n = t.IsNested ? Name(t.DeclaringType) + "+" + t.Name : t.Name;
            if (!t.IsGenericType) return n;
            n = n.Substring(0, n.IndexOf('`') is var i && i >= 0 ? i : n.Length);
            return n + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">";
        }
    }
}
