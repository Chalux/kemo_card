using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Mod.Global.Save;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Global;

public enum EGlobalModEvt
{
    [EventPayload(typeof(GlobalSaveChangedPayload))]
    GlobalSaveChanged,
}

public readonly struct GlobalSaveChangedPayload
{
    public GlobalSaveDto Snapshot { get; init; }
}

[EventTable(typeof(EGlobalModEvt), typeof(GlobalMod))]
public static partial class GlobalModEventTable
{
}

/// <summary>
/// 全局模块组合根：全局存档 + 通用界面（菜单、图鉴等）。
/// On*/Notify* 包装方法由 Source Generator 依据 <see cref="GlobalModEventTable"/> 生成。
/// </summary>
public sealed partial class GlobalMod : BaseMod
{
    public GlobalMod() : base("global")
    {
    }

    public GlobalSaveDto Current { get; internal set; } = GlobalSaveDto.CreateDefault();

    public static void RegisterUi(UIManager uiManager)
    {
        ArgumentNullException.ThrowIfNull(uiManager);
    }
}
