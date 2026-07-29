using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class CharacterDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("descId")]
    public string DescId { get; init; } = "";

    [JsonPropertyName("element")]
    public EElement Element { get; init; } = EElement.None;

    [JsonPropertyName("role")]
    public ERole Role { get; init; } = ERole.None;

    [JsonPropertyName("skillRefs")]
    public List<SkillRefDto> SkillRefs { get; init; } = [];

    /// <summary>主动技蓄力链（战斗规格 §5.1），与扁平 <see cref="SkillRefs"/> 分离，战斗主动按钮只读本字段。</summary>
    [JsonPropertyName("activeSkillChain")]
    public List<ActiveSkillChainEntryDto> ActiveSkillChain { get; init; } = [];

    [JsonPropertyName("buffRefs")]
    public List<BuffRefDto> BuffRefs { get; init; } = [];

    [JsonPropertyName("cards")]
    public List<string> Cards { get; init; } = [];

    [JsonPropertyName("artPath")]
    public string ArtPath { get; init; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    [JsonPropertyName("race")]
    public ERace Race { get; init; } = ERace.None;

    [JsonPropertyName("maxEnergy")]
    public int MaxEnergy { get; init; } = 0;

    [JsonPropertyName("initialEnergy")]
    public int InitialEnergy { get; init; } = 0;

    [JsonPropertyName("portraits")]
    public CharacterPortraitsDto? Portraits { get; init; }

    [JsonPropertyName("presentation")]
    public CharacterPresentationDto? Presentation { get; init; }
}
