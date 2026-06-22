using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class BattleDto
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("displayNameId")]
	public string DisplayNameId { get; init; } = "";

	[JsonPropertyName("descId")]
	public string DescId { get; init; } = "";

	[JsonPropertyName("waves")]
	public List<BattleWaveDto> Waves { get; init; } = [];

	[JsonPropertyName("scriptPath")]
	public string? ScriptPath { get; init; }

	[JsonPropertyName("rewards")]
	public BattleRewardDto Rewards { get; init; } = new();

	[JsonPropertyName("backgroundPath")]
	public string? BackgroundPath { get; init; }

	[JsonPropertyName("musicId")]
	public string? MusicId { get; init; }

	[JsonPropertyName("tags")]
	public List<string> Tags { get; init; } = [];

	[JsonPropertyName("combatRuleIds")]
	public List<string> CombatRuleIds { get; init; } = [];
}
