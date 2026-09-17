using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI;

/// <summary>
/// 声明式 UI 注册项。
/// </summary>
/// <remarks>
/// <see cref="OwnerModId"/> 是<b>必填</b>：每个界面必须归属唯一功能 Mod，用于启动期校验、
/// 按功能批量关闭/销毁，以及界面取数时的门面解析（见 ui-mod-binding 规格 §5）。
/// </remarks>
public sealed record UIRegistration
{
    /// <summary>归属功能 Mod 的 id（与 <c>FeatureModCatalog</c> 中登记的 modId 一致）。</summary>
    public required string OwnerModId { get; init; }

    public required string Id { get; init; }
    public required string Dir { get; init; }
    public required EUIType Type { get; init; }
    public UIOpenOpt? BaseOpenOpt { get; init; }
    public UIOpenOpt? OpenOpt { get; init; }
    public UIRouteMeta? Parent { get; init; }

    public static UIRegistration Page(string ownerModId, string id, string dir)
        => new()
        {
            OwnerModId = ownerModId,
            Id = id,
            Dir = dir,
            Type = EUIType.Pge,
            BaseOpenOpt = DefaultUIOpenOpt.ForType(EUIType.Pge),
        };

    public static UIRegistration Dialog(string ownerModId, string id, string dir)
        => new()
        {
            OwnerModId = ownerModId,
            Id = id,
            Dir = dir,
            Type = EUIType.Dlg,
            BaseOpenOpt = DefaultUIOpenOpt.ForType(EUIType.Dlg),
        };

    public static UIRegistration Window(string ownerModId, string id, string dir)
        => new()
        {
            OwnerModId = ownerModId,
            Id = id,
            Dir = dir,
            Type = EUIType.Win,
            BaseOpenOpt = DefaultUIOpenOpt.ForType(EUIType.Win),
        };

    public static UIRegistration Popup(string ownerModId, string id, string dir)
        => new()
        {
            OwnerModId = ownerModId,
            Id = id,
            Dir = dir,
            Type = EUIType.Pop,
            BaseOpenOpt = DefaultUIOpenOpt.ForType(EUIType.Pop),
        };

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
        OwnerModId = OwnerModId,
        Id = Id,
        Dir = Dir,
        Type = Type,
        BaseOpenOpt = BaseOpenOpt,
        OpenOpt = OpenOpt,
        RouteMeta = Parent,
    };
}