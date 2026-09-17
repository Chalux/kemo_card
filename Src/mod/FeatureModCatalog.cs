using KemoCard.Frame.UI;
using KemoCard.Mod.Global;
using KemoCard.Mod.Run;

namespace KemoCard.Mod;

/// <summary>
/// 一个功能 Mod 的界面声明。
/// </summary>
/// <remarks>
/// 只声明「怎么产出注册项」，<b>不需要 Mod 实例</b>：<c>GlobalMod</c> 是 bootstrap 期的长生命周期对象，
/// 而 <c>RunMod</c> 是每次 Run 现造的会话级对象；界面注册却只在启动期发生一次。
/// 若要求实例才能声明，就会产生「注册用一个实例、玩法用另一个实例」的双实例语义
/// （见 ui-mod-binding 规格 §5.1）。
/// </remarks>
/// <param name="ModId">功能 Mod 的 id，必须与 <c>BaseMod.ModId</c> 一致。</param>
/// <param name="Declare">产出该功能全部界面注册项。</param>
public sealed record FeatureUiDeclaration(string ModId, Func<IEnumerable<UIRegistration>> Declare);

/// <summary>
/// 全项目<b>唯一</b>列出「拥有界面的功能 Mod」的地方。
/// </summary>
/// <remarks>
/// 新增功能 Mod 只需在此加一行（原实现要在 <c>MainRoot</c> 与 <c>ModFactory</c> 各改一处）。
/// 仍是显式手工装配——符合 AGENT.md 对组合根的取向，且不做反射扫描（AOT/裁剪不友好）。
/// </remarks>
public static class FeatureModCatalog
{
    /// <summary>按装配顺序返回全部声明界面的功能 Mod。</summary>
    public static IReadOnlyList<FeatureUiDeclaration> Features { get; } =
    [
        new(GlobalMod.FeatureId, GlobalMod.GetUIRegistrations),
        new(RunMod.FeatureId, RunMod.GetUIRegistrations),
    ];
}