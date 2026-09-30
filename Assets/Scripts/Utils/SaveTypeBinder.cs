using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SRG.Utils
{
    /// <summary>
    /// Резолвер типов для <c>TypeNameHandling.Auto</c> в сейвах и упакованных дронах.
    ///
    /// 1. <b>Совместимость.</b> Сейвы до выделения сборок писали <c>"SRG.X.Y, Assembly-CSharp"</c>,
    ///    а сейвы до введения namespace'ов (июль 2026) — просто <c>"Y, Assembly-CSharp"</c>.
    ///    Имя сборки нормализуется в текущую (SRG.Simulation), короткое имя ищется среди SRG-типов.
    /// 2. <b>Безопасность.</b> <c>$type</c> в JSON задаёт, какой тип создать при загрузке, — без
    ///    ограничений сейв, пришедший от другого игрока, мог бы инстанцировать произвольный тип.
    ///    Разрешены только SRG-типы сборки симуляции, примитивы, строки, Unity-векторы/цвета и
    ///    стандартные коллекции из них.
    /// </summary>
    public sealed class SaveTypeBinder : DefaultSerializationBinder
    {
        public static readonly SaveTypeBinder Instance = new();

        private static readonly Assembly SimulationAssembly = typeof(SaveTypeBinder).Assembly;
        private static readonly string SimulationAssemblyName = SimulationAssembly.GetName().Name;

        /// <summary>Имена сборок, в которых раньше жили типы, сохраняемые в сейв.</summary>
        private static readonly string[] LegacyAssemblyNames = { "Assembly-CSharp" };

        private static readonly HashSet<Type> AllowedGenericDefinitions = new()
        {
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(Queue<>), typeof(Stack<>),
            typeof(KeyValuePair<,>), typeof(Nullable<>), typeof(SortedDictionary<,>), typeof(LinkedList<>),
        };

        private static readonly HashSet<Type> AllowedLeafTypes = new()
        {
            typeof(string), typeof(decimal), typeof(DateTime), typeof(TimeSpan), typeof(Guid),
            typeof(UnityEngine.Vector2), typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector2Int),
            typeof(UnityEngine.Vector3Int), typeof(UnityEngine.Color), typeof(UnityEngine.Color32),
        };

        private static Dictionary<string, Type> _bySimpleName;
        private static readonly object SimpleNameLock = new();

        public override Type BindToType(string assemblyName, string typeName)
        {
            Type type = TryResolve(NormalizeAssembly(assemblyName), NormalizeTypeName(typeName))
                        ?? ResolveLegacySimpleName(typeName);

            if (type == null)
                throw new JsonSerializationException($"Сейв: тип '{typeName}' ('{assemblyName}') не найден.");
            if (!IsAllowed(type))
                throw new JsonSerializationException($"Сейв: тип '{type.FullName}' не разрешён для загрузки.");
            return type;
        }

        /// <summary>true, если тип разрешено создавать из <c>$type</c>. Открыт для тестов.</summary>
        public static bool IsAllowed(Type t)
        {
            if (t == null) return false;
            if (t.IsArray) return IsAllowed(t.GetElementType());
            if (t.IsGenericType)
            {
                if (t.Assembly != SimulationAssembly && !AllowedGenericDefinitions.Contains(t.GetGenericTypeDefinition()))
                    return false;
                foreach (var arg in t.GetGenericArguments())
                    if (!IsAllowed(arg)) return false;
                return t.Assembly != SimulationAssembly || IsSrgType(t);
            }
            if (t.IsPrimitive || AllowedLeafTypes.Contains(t)) return true;
            return t.Assembly == SimulationAssembly && IsSrgType(t);
        }

        private static bool IsSrgType(Type t)
            => t.Namespace != null && (t.Namespace == "SRG" || t.Namespace.StartsWith("SRG.", StringComparison.Ordinal));

        private Type TryResolve(string assemblyName, string typeName)
        {
            try { return base.BindToType(assemblyName, typeName); }
            catch (JsonSerializationException) { return null; }
        }

        private static string NormalizeAssembly(string assemblyName)
        {
            if (assemblyName == null) return null;
            foreach (var legacy in LegacyAssemblyNames)
                if (assemblyName == legacy) return SimulationAssemblyName;
            return assemblyName;
        }

        /// <summary>Generic-аргументы несут свои имена сборок: "List`1[[SRG.X, Assembly-CSharp]]".</summary>
        private static string NormalizeTypeName(string typeName)
        {
            if (typeName == null) return null;
            foreach (var legacy in LegacyAssemblyNames)
                typeName = typeName
                    .Replace(", " + legacy + "]", ", " + SimulationAssemblyName + "]")
                    .Replace(", " + legacy + ",", ", " + SimulationAssemblyName + ",");
            return typeName;
        }

        /// <summary>Сейвы до namespace'ов: "Outer+Inner" без префикса SRG.* — ищем по короткому имени.</summary>
        private static Type ResolveLegacySimpleName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            lock (SimpleNameLock)
            {
                if (_bySimpleName == null)
                {
                    var map = new Dictionary<string, Type>();
                    foreach (var t in SimulationAssembly.GetTypes())
                    {
                        if (!IsSrgType(t)) continue;
                        // FullName без namespace: "SRG.X.Outer+Inner" → "Outer+Inner" —
                        // совпадает с тем, как тип писался в сейв до namespace'ов.
                        map[t.FullName.Substring(t.Namespace.Length + 1)] = t;
                    }
                    _bySimpleName = map;
                }
                return _bySimpleName.TryGetValue(typeName, out var found) ? found : null;
            }
        }
    }
}
