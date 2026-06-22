using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class SkillActionRefDto
{
	[JsonPropertyName("actionId")]
	public string ActionId { get; init; } = "";

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }
}
