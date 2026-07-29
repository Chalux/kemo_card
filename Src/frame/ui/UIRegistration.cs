using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI;

/// <summary>
/// 声明式 UI 注册项，替代在 ModController 中手动逐条 Register 的模式。
/// </summary>
public sealed record UIRegistration
{
    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required EUIType Type { get; init; }
    public UIOpenOpt? BaseOpenOpt { get; init; }
    public UIOpenOpt? OpenOpt { get; init; }
    public UIRouteMeta? Parent { get; init; }

    public static UIRegistration Page(string id, string dir)
        => new() { Id = id, Dir = dir, Type = EUIType.Pge };

    public static UIRegistration Dialog(string id, string dir)
        => new() { Id = id, Dir = dir, Type = EUIType.Dlg };

    public static UIRegistration Window(string id, string dir)
        => new() { Id = id, Dir = dir, Type = EUIType.Win };

    public static UIRegistration Popup(string id, string dir)
        => new() { Id = id, Dir = dir, Type = EUIType.Pop };

    public UIRegistration WithParent(string parentId, UIOpenOpt? parentOpenOpt = null,
        object? parentOpenPayload = null)
        => this with
        {
            Parent = new UIRouteMeta
            {
                ParentId = parentId,
                ParentOpenOpt = parentOpenOpt,
                ParentOpenPayload = parentOpenPayload,
            }
        };

    internal UIRuntimeEntry ToRuntimeEntry() => new()
    {
        Id = Id,
        Dir = Dir,
        Type = Type,
        BaseOpenOpt = BaseOpenOpt,
        OpenOpt = OpenOpt,
        RouteMeta = Parent,
    };
}