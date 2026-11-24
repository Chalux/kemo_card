using System.Text.Json;
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public static class ContentDefinitionJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new FlagsEnumJsonConverter<ERace>());
        options.Converters.Add(new FlagsEnumJsonConverter<EElement>());
        options.Converters.Add(new RoleJsonConverter());
        options.Converters.Add(new ItemKindJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ItemKindJsonConverter : JsonConverter<EItemKind>
    {
        public override EItemKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) && Enum.IsDefined((EItemKind)value))
                return (EItemKind)value;
            if (reader.TokenType == JsonTokenType.String)
            {
                var name = reader.GetString();
                if (string.Equals(name, "Potion", StringComparison.OrdinalIgnoreCase)) return EItemKind.Consumable;
                if (Enum.TryParse<EItemKind>(name, true, out var kind) && Enum.IsDefined(kind)) return kind;
            }
            throw new JsonException("Unknown item kind.");
        }

        public override void Write(Utf8JsonWriter writer, EItemKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class RoleJsonConverter : JsonConverter<ERole>
    {
        public override ERole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Role must be a string.");

            return reader.GetString()!.ToUpperInvariant() switch
            {
                "NONE" => ERole.None,
                "ATTACK" or "WARRIOR" or "WIZARD" or "SWORDMAN" or "MAGE"
                    or "CONTROLLER" or "CARDPLAYER" or "ELEMENTIST" => ERole.Attack,
                "DEFENSE" or "GUARD" or "SHIELD" => ERole.Defense,
                "SUPPORT" or "HEALER" or "ALCHEMIST" => ERole.Support,
                _ => throw new JsonException($"Unknown role '{reader.GetString()}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, ERole value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
