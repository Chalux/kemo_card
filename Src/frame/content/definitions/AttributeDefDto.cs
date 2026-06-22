using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class AttributeDefDto
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("displayNameId")]
	public string DisplayNameId { get; init; } = "";

	[JsonPropertyName("defaultBase")]
	public float DefaultBase { get; init; }

	[JsonPropertyName("allowNegative")]
	public bool AllowNegative { get; init; }

	[JsonPropertyName("minValue")]
	public float? MinValue { get; init; }

	[JsonPropertyName("maxValue")]
	public float? MaxValue { get; init; }

	[JsonPropertyName("isMeta")]
	public bool IsMeta { get; init; }

	[JsonPropertyName("tags")]
	public List<string> Tags { get; init; } = [];
}
