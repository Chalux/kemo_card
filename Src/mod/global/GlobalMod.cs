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
    /// <summary>
    /// 本功能 Mod 的 id。静态的界面声明需要它，故提为常量并传给 <see cref="BaseMod.ModId"/>，避免两处漂移。
    /// </summary>
    public const string FeatureId = "global";

    public GlobalMod() : base(FeatureId)
    {
    }

    public GlobalSaveDto Current { get; internal set; } = GlobalSaveDto.CreateDefault();

    /// <summary>
    /// 声明式注册当前模块所有 UI。归属 id 用于启动期校验与按功能批量关闭/销毁。
    /// </summary>
    public static IEnumerable<UIRegistration> GetUIRegistrations()
    {
        yield return UIRegistration.Window(FeatureId, GlobalUiIds.Menu, "Src/mod/global/Ui");

        yield return UIRegistration.Dialog(FeatureId, GlobalUiIds.Codex, "Src/mod/global/Ui");
        yield return UIRegistration.Dialog(FeatureId, GlobalUiIds.CardDetails, "Src/mod/global/Ui");
        yield return UIRegistration.Dialog(FeatureId, GlobalUiIds.CharacterDetails, "Src/mod/global/Ui");
        yield return UIRegistration.Dialog(FeatureId, GlobalUiIds.Setting, "Src/mod/global/Ui");
        yield return UIRegistration.Dialog(FeatureId, GlobalUiIds.Alert, "Src/mod/global/Ui");

        // 词典：内容在打开时从词条表 + 内容注册表现算，因此保留缓存没有意义也没有害处（默认缓存）。
        yield return UIRegistration.Dialog(FeatureId, GlobalUiIds.Glossary, "Src/mod/global/Ui");
    }
}