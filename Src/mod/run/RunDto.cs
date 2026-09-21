namespace KemoCard.Mod.Run;

public sealed record PlayerControllerDto
{
    public string PlayerId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsOwner { get; init; }
}

public sealed record PlayerRunStateDto
{
    public int? ActiveCharacterIndex { get; init; }
    public int Gold { get; init; }
    public List<RunModifierDto> Modifiers { get; init; } = [];
    public Dictionary<string, object> EventFlags { get; init; } = new(StringComparer.Ordinal);

    /// <summary>潜能直充余额：仅本槽位可消费、无需表决、可返还；消费时先扣这里再扣团队池。</summary>
    public int PotentialDirectCredit { get; init; }

    /// <summary>本槽位的潜能消费流水（解锁的被动），逐笔可返还。</summary>
    public List<PotentialSpendEntryDto> PotentialSpent { get; init; } = [];
}

/// <summary>一笔潜能消费：解锁某个角色被动；返还即重新锁定该被动。</summary>
public sealed record PotentialSpendEntryDto
{
    public string EntryId { get; init; } = "";

    /// <summary>消费额度来源：pool = 团队池，credit = 本槽位直充。</summary>
    public string Source { get; init; } = "pool";

    public int Amount { get; init; }
    public string CharacterInstanceId { get; init; } = "";
    public string BuffId { get; init; } = "";
}

public sealed record CharacterPoolEntryDto
{
    public string DefinitionId { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public List<string> DefinitionCardIds { get; init; } = [];
    public List<DeckSnapshotDto> Decks { get; init; } = [];
    public int CurrentDeckIndex { get; init; }
}

public sealed record DeckSnapshotDto
{
    public List<string> CardIds { get; init; } = [];
}

public sealed record RunModifierDto
{
    public string ModifierId { get; init; } = "";
    public float Value { get; init; }
    public string Source { get; init; } = "";
}

public sealed record BattleRecordDto
{
    public string BattleId { get; init; } = "";
    public bool Won { get; init; }
    public int TurnsUsed { get; init; }
    public int DamageDealt { get; init; }
    public int DamageTaken { get; init; }
}

public sealed record RunDto
{
    /// <summary>
    /// 当前 Run 存档 schema 版本。读档时高于此版本的存档会被拒绝并归档：
    /// 若照默认值反序列化，会得到一个「看似合法但错」的 Run（字段静默变成默认值）。
    /// v2：新增团体潜能池与每槽位潜能账本。
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public string RunId { get; init; } = "";
    public string StoryId { get; init; } = "";
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public int CurrentRing { get; init; } = 1;
    public int MaxRing { get; init; } = RunConstants.DefaultMaxRings;
    public ERunPhase Phase { get; init; } = ERunPhase.Init;
    public int RunSeed { get; init; }

    public bool IsMultiplayer { get; init; }
    public List<PlayerControllerDto> PlayerControllers { get; init; } = [];
    public Dictionary<int, string> SlotOwnership { get; init; } = new();

    public List<CharacterPoolEntryDto> CharacterPool { get; init; } = [];
    public List<string> CardCollection { get; init; } = [];
    public int SharedGold { get; init; }

    /// <summary>团队潜能池（v2）：全队共享的可消费额度，重复获得角色 +20 入池。</summary>
    public int TeamPotentialPool { get; init; }

    public List<PlayerRunStateDto> PlayerStates { get; init; } = [];
    public List<BattleRecordDto> BattleHistory { get; init; } = [];

    /// <summary>
    /// 把已知的旧版本存档提升到当前 schema。v1 → v2 补潜能默认值（池 0、槽位账本空）。
    /// 后续新增版本时在此按版本追加迁移。
    /// </summary>
    /// <remarks>
    /// 手写 / 被外部工具改写过的存档可能带显式 <c>null</c>（如 <c>"playerStates": null</c>），
    /// 反序列化会把它填成 null。本方法在<see cref="RunSaveService"/> 的 try 之外执行，
    /// 因此必须自己容错，否则坏档不会走"归档"路径而是直接把 NRE 抛给上层。
    /// </remarks>
    public RunDto Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        CardCollection = CardCollection ?? [],
        BattleHistory = BattleHistory ?? [],
        PlayerStates = [.. (PlayerStates ?? []).Select(state => state is null
            ? new PlayerRunStateDto()
            : state with { PotentialSpent = state.PotentialSpent ?? [] })],
    };
}