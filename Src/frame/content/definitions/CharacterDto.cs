using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class CharacterDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    // 2026-09-21：角色不再有 descId。角色详细界面改为展示该角色的专属卡牌列表，
    // 避免"角色简介"与卡组/被动重复表达同一件事。

    [JsonPropertyName("element")]
    public EElement Element { get; init; } = EElement.None;

    [JsonPropertyName("role")]
    public ERole Role { get; init; } = ERole.None;

    [JsonPropertyName("skillRefs")]
    public List<SkillRefDto> SkillRefs { get; init; } = [];

    /// <summary>主动技蓄力链（战斗规格 §5.1），与扁平 <see cref="SkillRefs"/> 分离，战斗主动按钮只读本字段。</summary>
    [JsonPropertyName("activeSkillChain")]
    public List<ActiveSkillChainEntryDto> ActiveSkillChain { get; init; } = [];

    /// <summary>
    /// 潜能门闩被动：解锁后开战挂对应 buff（见 <see cref="PassiveRefDto"/>）。
    /// 2026-09-21 起这是角色唯一的"常驻增益"通道——旧的 <c>buffRefs</c>（常驻天赋）已移除，
    /// 因为它与被动重复却走另一条挂载路径。
    /// </summary>
    [JsonPropertyName("passives")]
    public List<PassiveRefDto> Passives { get; init; } = [];

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