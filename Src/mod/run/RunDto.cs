using System.Text.Json.Serialization;
using KemoCard.Mod.Run.Adventure;

namespace KemoCard.Mod.Run;

public sealed record PlayerRunStateDto
{
    public int? ActiveCharacterIndex { get; init; }
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
    public int Ring { get; init; }
    public string BattleId { get; init; } = "";
    public bool Won { get; init; }
    public int TurnsUsed { get; init; }
    public int DamageDealt { get; init; }
    public int DamageTaken { get; init; }
}

public sealed record RunDto
{
    /// <summary>
    /// 单机三人队的 Run 存档版本。读档时拒绝版本不匹配的存档。
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    public string RunId { get; init; } = "";
    public string StoryId { get; init; } = "";
    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public int CurrentRing { get; init; } = 1;
    public int MaxRing { get; init; } = RunConstants.DefaultMaxRings;
    public ERunPhase Phase { get; init; } = ERunPhase.Init;
    public int RunSeed { get; init; }

    public List<CharacterPoolEntryDto> CharacterPool { get; init; } = [];
    public List<string> CardCollection { get; init; } = [];
    public int SharedGold { get; init; }
    public List<string> Relics { get; init; } = [];
    [JsonPropertyName("potions")] // v5 存档沿用原字段名。
    public List<string?> Consumables { get; init; } = [];
    public Adventure.RunMapState? Map { get; init; }
    public List<string> ReservedEventIds { get; init; } = [];

    public List<PlayerRunStateDto> PlayerStates { get; init; } = [];
    public List<BattleRecordDto> BattleHistory { get; init; } = [];

    internal bool HasRestorableStructure()
    {
        if (CharacterPool is null || PlayerStates is null || PlayerStates.Count < RunConstants.SlotCount)
            return false;

        // 新 Run 在选完故事、完成队伍编辑前角色池本来就是空的（初始自动存档即此状态），
        // 不能因此拒绝。但已进入需要队伍的流程（Reward 及之后）或槽位已引用角色时，
        // 空池意味着没有角色可上阵、Boss 永远无法开战——属于无法恢复的坏档。
        if (CharacterPool.Count == 0 &&
            (Phase is not (ERunPhase.Init or ERunPhase.Event) ||
             PlayerStates.Any(state => state?.ActiveCharacterIndex is not null)))
            return false;

        if (CharacterPool.Any(character =>
            character is null || string.IsNullOrWhiteSpace(character.InstanceId) ||
            string.IsNullOrWhiteSpace(character.DefinitionId) || character.DefinitionCardIds is null ||
            character.Decks is null || character.Decks.Any(deck => deck?.CardIds is null)))
            return false;

        if (PlayerStates.Any(state => state is null || state.Modifiers is null || state.EventFlags is null ||
            state.Modifiers.Any(modifier => modifier is null)) || BattleHistory.Any(record => record is null))
            return false;

        return Map is null || IsRestorableMap(Map);
    }

    /// <summary>
    /// 地图结构校验：必须是完整的固定 2×3 网格（节点唯一且 Kind 与步骤匹配），
    /// 明文事件节点必须携带奖励；NodeOpen 时游标必须指向一个未完成的可打开节点。
    /// 这些状态只由服务层写入；拒绝不完整快照，避免读档后按钮缺失 / 节点无法离开 / Boss 无法开启。
    /// </summary>
    private static bool IsRestorableMap(RunMapState map)
    {
        if (map.Nodes is null || map.Nodes.Count != RunMapState.BranchCount * RunMapState.StepsPerBranch ||
            map.SelectedBranch is < -1 or >= RunMapState.BranchCount ||
            map.Position is < 0 or >= RunMapState.StepsPerBranch)
            return false;

        var cells = new HashSet<(int Branch, int Step)>();
        foreach (var node in map.Nodes)
        {
            if (node is null || node.Branch is < 0 or >= RunMapState.BranchCount ||
                node.Step is < 0 or >= RunMapState.StepsPerBranch ||
                !cells.Add((node.Branch, node.Step)) ||
                !MatchesStepKind(node))
                return false;

            if (node.Offers is null || node.Offers.Any(offer => offer is null))
                return false;

            if (node.Kind == ERunNodeKind.VisibleEvent && node.VisibleReward is null)
                return false;

            if (node.VisibleReward is { } reward && (reward.CardIds is null || reward.RelicIds is null))
                return false;
        }

        if (!map.NodeOpen)
            return true;

        var current = map.Nodes.FirstOrDefault(node => node.Branch == map.SelectedBranch && node.Step == map.Position);
        return current is not null && (current.Kind == ERunNodeKind.Shop || !current.Completed);
    }

    /// <summary>生成器的固定拓扑：0 = 事件，1 = 商店，2 = 明文事件（旧档允许仍是普通事件，读档时升级）。</summary>
    private static bool MatchesStepKind(RunMapNode node) => node.Step switch
    {
        1 => node.Kind == ERunNodeKind.Shop,
        2 => node.Kind is ERunNodeKind.VisibleEvent or ERunNodeKind.Event,
        _ => node.Kind == ERunNodeKind.Event,
    };

    /// <summary>
    /// 用默认集合替换显式 null，并把 <see cref="PlayerStates"/> 补齐到 <see cref="RunConstants.SlotCount"/>。
    /// </summary>
    /// <remarks>
    /// 手写 / 被外部工具改写过的存档可能带显式 <c>null</c>（如 <c>"playerStates": null</c>），
    /// 反序列化会把它填成 null。本方法在<see cref="RunSaveService"/> 的 try 之外执行，
    /// 因此必须自己容错，否则坏档不会走"归档"路径而是直接把 NRE 抛给上层。
    /// PlayerStates 也必须补齐：RunController / 队伍校验 / 出战流程都按 SlotCount 下标直接访问它。
    /// </remarks>
    public RunDto Normalize()
    {
        var playerStates = (PlayerStates ?? [])
            .Select(state => state ?? new PlayerRunStateDto())
            .ToList();
        while (playerStates.Count < RunConstants.SlotCount)
            playerStates.Add(new PlayerRunStateDto());

        return this with
        {
            CardCollection = CardCollection ?? [],
            BattleHistory = BattleHistory ?? [],
            Relics = Relics ?? [],
            Consumables = Consumables ?? [],
            ReservedEventIds = ReservedEventIds ?? [],
            PlayerStates = playerStates,
        };
    }
}