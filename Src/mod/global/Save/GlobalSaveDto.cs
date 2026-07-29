namespace KemoCard.Mod.Global.Save;

/// <summary>
/// 跨 Run 全局存档 DTO（与单次 Run 存档分离）。
/// </summary>
public sealed record GlobalSaveDto(
    int SchemaVersion,
    Dictionary<string, bool> Achievements,
    Dictionary<string, bool> Unlocks,
    Dictionary<string, bool> CodexEntries,
    Dictionary<string, string> Settings,
    IReadOnlyList<string>? EnabledModIds = null,
    string? ContentVersionHash = null)
{
    public const int CurrentSchemaVersion = 2;

    public static GlobalSaveDto CreateDefault()
    {
        return new GlobalSaveDto(
            SchemaVersion: CurrentSchemaVersion,
            Achievements: new Dictionary<string, bool>(StringComparer.Ordinal),
            Unlocks: new Dictionary<string, bool>(StringComparer.Ordinal),
            CodexEntries: new Dictionary<string, bool>(StringComparer.Ordinal),
            Settings: new Dictionary<string, string>(StringComparer.Ordinal),
            EnabledModIds: new[] { "base.game" });
    }

    public GlobalSaveDto Normalize()
    {
        var enabled = EnabledModIds is { Count: > 0 }
            ? EnabledModIds
            : CreateDefault().EnabledModIds!;
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            EnabledModIds = enabled.ToList(),
        };
    }
}