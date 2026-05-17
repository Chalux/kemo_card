using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content;

public sealed class ContentModManifestDto
{
	[JsonPropertyName("modId")]
	public string ModId { get; init; } = "";

	[JsonPropertyName("displayName")]
	public string DisplayName { get; init; } = "";

	[JsonPropertyName("version")]
	public string Version { get; init; } = "1.0.0";

	[JsonPropertyName("loadOrder")]
	public int LoadOrder { get; init; }

	[JsonPropertyName("dependencies")]
	public ContentModDependenciesDto Dependencies { get; init; } = new();

	[JsonPropertyName("contentRoot")]
	public string ContentRoot { get; init; } = "content";
}

public sealed class ContentModDependenciesDto
{
	[JsonPropertyName("required")]
	public List<string> Required { get; init; } = new();

	[JsonPropertyName("optional")]
	public List<string> Optional { get; init; } = new();
}
