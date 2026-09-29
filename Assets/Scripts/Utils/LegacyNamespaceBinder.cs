using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SRG.Utils
{
    /// <summary>
    /// Резолвит типы из сейвов, записанных до введения namespace'ов (июль 2026):
    /// TypeNameHandling.Auto сохранял "DirectiveAttack, Assembly-CSharp", а тип теперь
    /// называется "SRG.NpcAI.DirectiveAttack". Если стандартный поиск не находит тип,
    /// ищем по короткому имени среди типов SRG.* этой сборки.
    /// </summary>
    public class LegacyNamespaceBinder : DefaultSerializationBinder
    {
        private static Dictionary<string, Type> _bySimpleName;

        public override Type BindToType(string assemblyName, string typeName)
        {
            try
            {
                return base.BindToType(assemblyName, typeName);
            }
            catch (JsonSerializationException)
            {
                if (_bySimpleName == null)
                {
                    _bySimpleName = new Dictionary<string, Type>();
                    foreach (var t in typeof(LegacyNamespaceBinder).Assembly.GetTypes())
                    {
                        if (t.Namespace == null || !t.Namespace.StartsWith("SRG")) continue;
                        // FullName без namespace: "SRG.X.Outer+Inner" → "Outer+Inner" —
                        // совпадает с тем, как тип писался в сейв до namespace'ов.
                        string key = t.FullName.Substring(t.Namespace.Length + 1);
                        _bySimpleName[key] = t;
                    }
                }
                if (_bySimpleName.TryGetValue(typeName, out var found))
                    return found;
                throw;
            }
        }
    }
}
