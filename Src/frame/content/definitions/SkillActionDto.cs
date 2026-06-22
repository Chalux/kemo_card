using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class SkillActionDto
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("kind")]
	public ESkillActionKind Kind { get; init; }

	[JsonPropertyName("params")]
	public Dictionary<string, object>? Params { get; init; }

	[JsonPropertyName("scriptPath")]
	public string? ScriptPath { get; init; }

	[JsonPropertyName("scriptEntry")]
	public string? ScriptEntry { get; init; }

	[JsonPropertyName("actionRefs")]
	public List<SkillActionRefDto> ActionRefs { get; init; } = [];
}
