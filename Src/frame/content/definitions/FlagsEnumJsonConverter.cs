using System.Text.Json;
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class FlagsEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ParseOne(reader.GetString());
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            ulong combined = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException($"Flags enum array expects strings for {typeof(TEnum).Name}.");
                }

                combined |= Convert.ToUInt64(ParseOne(reader.GetString()));
            }

            return (TEnum)Enum.ToObject(typeof(TEnum), combined);
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {typeof(TEnum).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    private static TEnum ParseOne(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Enum.TryParse<TEnum>(name, ignoreCase: false, out var v))
        {
            throw new JsonException($"Unknown {typeof(TEnum).Name} value '{name}'.");
        }

        return v;
    }
}
