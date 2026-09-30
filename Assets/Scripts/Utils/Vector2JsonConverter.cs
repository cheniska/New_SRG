using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SRG.Utils
{
    /// <summary>
    /// <see cref="Vector2"/> в JSON как <c>{"x":…,"y":…}</c>. Без конвертера Newtonsoft пишет и
    /// вычисляемые свойства (normalized/magnitude/sqrMagnitude), причём normalized — рекурсивно:
    /// каждый вектор в сейве занимал в несколько раз больше места. Старые сейвы с этими лишними
    /// полями читаются: берутся только x и y.
    /// </summary>
    public sealed class Vector2JsonConverter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return default;
            var o = JObject.Load(reader);
            return new Vector2(o.Value<float?>("x") ?? 0f, o.Value<float?>("y") ?? 0f);
        }
    }
}
