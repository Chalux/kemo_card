namespace KemoCard.Frame.UI;

/// <summary>
/// 按归属解析功能 Mod 暴露给<b>自家界面</b>的门面。
/// </summary>
/// <remarks>
/// <para>由组合根实现并注入 <see cref="UIManager"/>（见 ui-mod-binding 规格 §5.1 ②）。
/// 之所以做成"提供者"而不是让功能 Mod 实例直接暴露，是因为 Run 这类 Mod 是<b>会话级</b>对象
/// （每次 Run 现造），而界面注册只在启动期发生一次——只有动态解析才能让 Run 的界面每次拿到当前会话的门面。</para>
/// <para>界面侧统一经 <c>BaseWin.Facade&lt;TFacade&gt;()</c> 取用，不得直接访问 <c>AppRoot.Services</c>。</para>
/// </remarks>
public interface IUiFacadeProvider
{
    /// <summary>
    /// 解析指定功能 Mod 的门面。
    /// </summary>
    /// <param name="ownerModId">功能 Mod 的 id（来自 <c>UIVo.OwnerModId</c>）。</param>
    /// <returns>该功能的门面实例；无此功能或无门面时返回 <c>null</c>。</returns>
    object? ResolveUiFacade(string ownerModId);
}