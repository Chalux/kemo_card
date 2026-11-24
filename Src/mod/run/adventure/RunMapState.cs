namespace KemoCard.Mod.Run.Adventure;

public enum ERunNodeKind { Event, Shop, VisibleEvent }

public enum EVisibleEventKind { Gold, RolePack, AllRolesPack, RelicChoice }

public sealed record RunVisibleReward
{
    public EVisibleEventKind Kind { get; init; }
    public KemoCard.Frame.Content.Definitions.ERole Role { get; init; }
    public bool Prepared { get; set; }
    public List<string> CardIds { get; set; } = [];
    public List<string> RelicIds { get; set; } = [];
    public RunVisibleReward Copy() => this with { CardIds = [.. CardIds], RelicIds = [.. RelicIds] };
}

public sealed record RunShopOffer
{
    public string ItemId { get; init; } = "";
    public int Price { get; init; }
    public bool Sold { get; set; }
}

public sealed record RunMapNode
{
    public int Branch { get; init; }
    public int Step { get; init; }
    public ERunNodeKind Kind { get; init; }
    public string? EventId { get; init; }
    public bool Completed { get; set; }
    public RunVisibleReward? VisibleReward { get; init; }
    public List<RunShopOffer> Offers { get; init; } = [];
    public RunMapNode Copy() => this with { Offers = Offers.Select(offer => offer with { }).ToList(), VisibleReward = VisibleReward?.Copy() };
}

public sealed record RunMapState
{
    /// <summary>地图固定 2 条分支 × 每分支 3 步；生成与读档校验共用同一口径。</summary>
    public const int BranchCount = 2;
    public const int StepsPerBranch = 3;

    public int Ring { get; init; }
    public int SelectedBranch { get; set; } = -1;
    // 保留存档字段名：当前打开的节点编号，不再表示必须依次推进的路线进度。
    public int Position { get; set; }
    public bool NodeOpen { get; set; }
    public List<RunMapNode> Nodes { get; init; } = [];
    public RunMapState Copy() => this with { Nodes = Nodes.Select(node => node.Copy()).ToList() };
    [System.Text.Json.Serialization.JsonIgnore]
    public RunMapNode? CurrentNode => Nodes.FirstOrDefault(node => node.Branch == SelectedBranch && node.Step == Position);
}