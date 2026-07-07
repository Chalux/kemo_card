using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
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
/// </summary>
public sealed partial class GlobalMod : BaseMod
{
    public GlobalMod() : base("global")
    {
    }

    public GlobalSaveDto Current { get; internal set; } = GlobalSaveDto.CreateDefault();

    /// <summary>
    /// 声明式注册当前模块所有 UI。
    /// </summary>
    public static IEnumerable<UIRegistration> GetUIRegistrations()
    {
        yield return UIRegistration.Window(GlobalUiIds.Menu, "Src/mod/global/Ui");

        yield return UIRegistration.Dialog(GlobalUiIds.Codex, "Src/mod/global/Ui");
    }

    /// <summary>
    /// 将此模块的 UI 注册到运行时注册表。
    /// </summary>
    public static void RegisterUi(UIRuntimeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var reg in GetUIRegistrations())
        {
            registry.Register(reg.ToRuntimeEntry());
        }
    }
}
