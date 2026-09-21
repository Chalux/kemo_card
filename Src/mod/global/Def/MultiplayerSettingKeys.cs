namespace KemoCard.Mod.Global.Def;

/// <summary>全局联机设置键（存 GlobalSave 的 Settings 字典，字符串值）。</summary>
public static class MultiplayerSettingKeys
{
    /// <summary>潜能消费模式：自由消费（free）/ 表决消费（vote）。</summary>
    public const string PotentialConsumeMode = "multiplayer.potential.consume_mode";
    public const string PotentialConsumeModeFree = "free";
    public const string PotentialConsumeModeVote = "vote";
    public const string PotentialConsumeModeDefault = PotentialConsumeModeFree;

    /// <summary>表决模式下每环每槽位可提议次数（默认 2）。</summary>
    public const string PotentialProposalsPerRing = "multiplayer.potential.proposals_per_ring";
    public const int DefaultProposalsPerRing = 2;

    /// <summary>提议次数无限制勾选框（"1"/"0"）。</summary>
    public const string PotentialProposalsUnlimited = "multiplayer.potential.proposals_unlimited";
}
