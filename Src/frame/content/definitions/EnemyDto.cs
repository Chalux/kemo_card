using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class EnemyDto
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("displayNameId")]
	public string DisplayNameId { get; init; } = "";

	[JsonPropertyName("descId")]
	public string DescId { get; init; } = "";

	[JsonPropertyName("maxHp")]
	public int MaxHp { get; init; }

	[JsonPropertyName("element")]
	public EElement Element { get; init; }

	[JsonPropertyName("role")]
	public ERole Role { get; init; }

	[JsonPropertyName("skillRefs")]
	public List<SkillRefDto> SkillRefs { get; init; } = [];

	[JsonPropertyName("buffRefs")]
	public List<BuffRefDto> BuffRefs { get; init; } = [];

	[JsonPropertyName("artPath")]
	public string ArtPath { get; init; } = "";

	[JsonPropertyName("scriptPath")]
	public string? ScriptPath { get; init; }

	[JsonPropertyName("tags")]
	public List<string> Tags { get; init; } = [];
}
