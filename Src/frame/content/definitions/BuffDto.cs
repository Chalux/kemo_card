using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class BuffEffectHooksDto
{
	[JsonPropertyName("onApply")]
	public List<EffectRefDto> OnApply { get; init; } = [];

	[JsonPropertyName("onTurnStart")]
	public List<EffectRefDto> OnTurnStart { get; init; } = [];

	[JsonPropertyName("onTurnEnd")]
	public List<EffectRefDto> OnTurnEnd { get; init; } = [];

	[JsonPropertyName("onStackChanged")]
	public List<EffectRefDto> OnStackChanged { get; init; } = [];

	[JsonPropertyName("onRemove")]
	public List<EffectRefDto> OnRemove { get; init; } = [];
}

public sealed class BuffDto
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("displayNameId")]
	public string DisplayNameId { get; init; } = "";

	[JsonPropertyName("descId")]
	public string DescId { get; init; } = "";

	[JsonPropertyName("iconPath")]
	public string IconPath { get; init; } = "";

	[JsonPropertyName("maxStacks")]
	public int MaxStacks { get; init; } = 1;

	[JsonPropertyName("stackRule")]
	public EBuffStackRule StackRule { get; init; }

	[JsonPropertyName("durationType")]
	public EBuffDurationType DurationType { get; init; }

	[JsonPropertyName("duration")]
	public int Duration { get; init; }

	[JsonPropertyName("dispellable")]
	public bool Dispellable { get; init; }

	[JsonPropertyName("exclusiveGroup")]
	public string? ExclusiveGroup { get; init; }

	[JsonPropertyName("hidden")]
	public bool Hidden { get; init; }

	[JsonPropertyName("hooks")]
	public BuffEffectHooksDto Hooks { get; init; } = new();

	[JsonPropertyName("scriptPath")]
	public string? ScriptPath { get; init; }

	[JsonPropertyName("tags")]
	public List<string> Tags { get; init; } = [];
}
