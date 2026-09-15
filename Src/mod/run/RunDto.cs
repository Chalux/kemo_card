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
    /// </summary>
    public const int CurrentSchemaVersion = 1;

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

    public List<PlayerRunStateDto> PlayerStates { get; init; } = [];
    public List<BattleRecordDto> BattleHistory { get; init; } = [];

    /// <summary>
    /// 把已知的旧版本存档提升到当前 schema。v1 之前没有需要迁移的字段，
    /// 后续新增版本时在此按版本补默认值 / 迁移语义。
    /// </summary>
    public RunDto Normalize() => this with { SchemaVersion = CurrentSchemaVersion };
}