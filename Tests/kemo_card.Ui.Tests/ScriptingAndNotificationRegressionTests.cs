using KemoCard.Frame.Content;
using KemoCard.Frame.Notification;
using KemoCard.Frame.Scripting;
using KemoCard.Frame.UI;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// P1 级缺陷回归：脚本运行时并发/释放、脚本路径前缀判定、导航栈、红点注册表生命周期。
/// </summary>
[TestFixture]
public sealed class ScriptingAndNotificationRegressionTests
{
    #region ModScriptLoader：路径前缀必须带目录分隔符

    [Test]
    public void FileExists_rejects_sibling_directory_sharing_scripts_prefix()
    {
        var modDir = CreateModWithScript("effects/demo.js", "export function execute(ctx) { return {}; }");
        // 同级目录名以 "scripts" 为前缀：旧实现 StartsWith(scriptsRoot) 会把它误判为合法脚本路径。
        var sibling = Path.Combine(modDir, "scripts-evil");
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "x.js"), "evil");

        var loader = new ModScriptLoader(BuildCatalog(modDir));

        Assert.That(loader.FileExists("base.game/scripts-evil/x.js"), Is.False);
    }

    [Test]
    public void FileExists_accepts_script_under_scripts_root()
    {
        var modDir = CreateModWithScript("effects/demo.js", "export function execute(ctx) { return {}; }");

        var loader = new ModScriptLoader(BuildCatalog(modDir));

        Assert.That(loader.FileExists("base.game/effects/demo.js"), Is.True);
    }

    #endregion

    #region ModScriptRuntime：释放幂等

    [Test]
    public void Dispose_is_idempotent_and_invoke_after_dispose_returns_failure()
    {
        var modDir = CreateModWithScript("effects/demo.js", "export function execute(ctx) { return {}; }");
        var catalog = BuildCatalog(modDir);
        var registry = new GameDefinitionRegistry();
        var runtime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());

        runtime.Dispose();

        Assert.DoesNotThrow(() => runtime.Dispose(), "Dispose 必须幂等（热重载/退出路径会重复调用）");

        var result = runtime.Invoke(
            "base.game",
            "effects/demo.js",
            "execute",
            NewContext(registry));

        Assert.That(result.Success, Is.False);
        Assert.DoesNotThrow(() => runtime.Recreate(), "释放后再次 Recreate 不得抛异常");
    }

    [Test]
    public void Repeated_invoke_reuses_cached_entry_and_survives_Recreate()
    {
        var modDir = CreateModWithScript(
            "effects/demo.js",
            "export function execute(ctx) { return { proposedEffects: [] }; }");
        var catalog = BuildCatalog(modDir);
        var registry = new GameDefinitionRegistry();
        using var runtime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());

        var first = runtime.Invoke("base.game", "effects/demo.js", "execute", NewContext(registry));
        var second = runtime.Invoke("base.game", "effects/demo.js", "execute", NewContext(registry));
        Assert.That(first.Success, Is.True, first.Error);
        Assert.That(second.Success, Is.True, second.Error);

        runtime.Recreate();

        var afterRecreate = runtime.Invoke("base.game", "effects/demo.js", "execute", NewContext(registry));
        Assert.That(afterRecreate.Success, Is.True, "Recreate 后入口缓存必须随环境一起失效并重新加载：" + afterRecreate.Error);
    }

    /// <summary>
    /// PuerTS 的 V8 isolate 与创建线程绑定，跨线程进入会以 V8 内部的
    /// <c>RangeError: Maximum call stack size exceeded</c> 之类的形式失败。
    /// 互斥锁不能修复这一点，只能把「并发进入」变成「串行但仍在错误线程上执行」，
    /// 所以必须显式拒绝并给出可诊断的错误，而不是让调用方看到一个莫名的 JS 异常。
    /// </summary>
    [Test]
    public void Invoke_from_another_thread_is_rejected_with_a_diagnosable_error()
    {
        var modDir = CreateModWithScript(
            "effects/demo.js",
            "export function execute(ctx) { return { proposedEffects: [] }; }");
        var catalog = BuildCatalog(modDir);
        var registry = new GameDefinitionRegistry();
        using var runtime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());

        ModScriptInvokeResult? crossThread = null;
        var thread = new System.Threading.Thread(() =>
            crossThread = runtime.Invoke("base.game", "effects/demo.js", "execute", NewContext(registry)));
        thread.Start();
        thread.Join();

        Assert.That(crossThread, Is.Not.Null);
        Assert.That(crossThread!.Success, Is.False);
        Assert.That(crossThread.Error, Does.Contain("线程"));
    }

    private static ScriptCallContext NewContext(GameDefinitionRegistry registry) =>
        new() { RunSeed = 0, StreamKey = "test", Registry = registry };

    #endregion

    #region UIStack：导航栈语义

    [Test]
    public void Push_and_Back_return_previous_entry_and_drop_top()
    {
        var stack = new UIStack();
        stack.Push("Menu");
        stack.Push("RunMain");

        Assert.That(stack.Count, Is.EqualTo(2));
        Assert.That(stack.Top, Is.EqualTo("RunMain"));
        Assert.That(stack.Back(), Is.EqualTo("Menu"));
        Assert.That(stack.Count, Is.EqualTo(1));
        Assert.That(stack.Back(), Is.Null, "栈内不足两层时不得继续回退");
    }

    [Test]
    public void Remove_is_idempotent_and_does_not_touch_other_entries()
    {
        var stack = new UIStack();
        stack.Push("Menu");
        stack.Push("RunMain");

        Assert.That(stack.Remove("RunMain"), Is.True);
        Assert.That(stack.Remove("RunMain"), Is.False, "重复移除必须是无操作");
        Assert.That(stack.Contains("Menu"), Is.True, "移除不得误删其它界面");
        Assert.That(stack.Count, Is.EqualTo(1));
    }

    #endregion

    #region RedDotService：反订阅与重注册

    [SetUp]
    public void ResetRedDots() => RedDotService.Configure();

    [Test]
    public void Configure_runs_registered_unsubscribers()
    {
        var unsubscribed = 0;
        (Action subscribe, Action unsubscribe) trigger = (() => { }, () => unsubscribed++);
        RedDotService.RegisterNode("A", () => true, trigger);

        RedDotService.Configure();

        Assert.That(unsubscribed, Is.EqualTo(1), "Configure 必须先执行反订阅，否则静态事件订阅跨重启泄漏");
    }

    [Test]
    public void ReRegister_preserves_parent_and_children_links()
    {
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.True);

        // 重注册子节点（例如运行时按条件重建触发器）
        RedDotService.RegisterNode("Menu/Codex", () => true);

        Assert.That(RedDotService.IsActive("Menu/Codex"), Is.True);
        Assert.That(RedDotService.IsActive("Menu"), Is.True, "重注册后向上聚合不得断裂");
        Assert.That(
            RedDotService.InternalGetNode("Menu/Codex")!.Parent,
            Is.SameAs(RedDotService.InternalGetNode("Menu")));
        Assert.That(
            RedDotService.InternalGetNode("Menu")!.Children,
            Has.Count.EqualTo(1),
            "父节点不得同时持有新旧两个子节点");
    }

    [Test]
    public void ReRegister_aggregate_node_keeps_inherited_children_active()
    {
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.True);

        // 纯聚合节点（无 checkFunc）重注册：必须按继承来的子节点首评
        RedDotService.RegisterNode("Menu");

        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }

    [Test]
    public void ReRegister_does_not_leave_stale_node_in_old_parent()
    {
        RedDotService.RegisterNode("A", () => false);
        RedDotService.RegisterNode("P", () => false);
        RedDotService.RegisterParent("A", "P");

        var oldChild = RedDotService.InternalGetNode("A")!;
        RedDotService.RegisterNode("A", () => false);

        Assert.That(
            RedDotService.InternalGetNode("P")!.Children,
            Has.None.SameAs(oldChild),
            "旧节点必须从父节点 Children 中摘除");
    }

    #endregion

    private static string CreateModWithScript(string relativePath, string source)
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_p1_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base-game", "base.game");
        ContentModTestHelper.AddScript(modDir, relativePath, source);
        return modDir;
    }

    private static ModScriptCatalog BuildCatalog(string modDir)
    {
        var catalog = new ModScriptCatalog();
        catalog.Rebuild(
        [
            new DiscoveredModEntry(
                modDir,
                new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }),
        ]);
        return catalog;
    }
}