using KemoCard.Fixed.Godot;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Scripting;
using KemoCard.Frame.UI;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Condition;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Save;
using KemoCard.Mod.Run;

namespace KemoCard.Mod;

public sealed class ModStartupContext
{
    public required string SaveDirectory { get; init; }

    public required string ContentModRootDirectory { get; init; }

    public required string BundledContentModsDirectory { get; init; }

    /// <summary>
    /// 调试构建置 true：忽略版本号，每次启动都把随包 Mod 重新拷贝到用户目录。
    /// 开发期新增内容不抬版本号也能在游戏里看到。
    /// </summary>
    public bool ForceContentModRefresh { get; init; }
}

public sealed class ModStartupResult : IUiFacadeProvider
{
    /// <summary>拥有界面的功能 Mod 声明，供启动期迭代注册（不再由 <c>MainRoot</c> 硬编码）。</summary>
    public IReadOnlyList<FeatureUiDeclaration> Features { get; init; } = FeatureModCatalog.Features;

    public required GlobalMod GlobalMod { get; init; }

    public required GlobalModController GlobalController { get; init; }

    public required GlobalSaveService GlobalSaveService { get; init; }

    public required ContentModPipeline ContentModPipeline { get; init; }

    public required ModScriptRuntime ScriptRuntime { get; init; }

    public required PuertsContentEffectScriptHost ContentEffectScriptHost { get; init; }

    public required StoryScriptInvoker StoryScriptInvoker { get; init; }

    public required EventScriptInvoker EventScriptInvoker { get; init; }

    public required BattleScriptInvoker BattleScriptInvoker { get; init; }

    public required EnemyAiScriptInvoker EnemyAiScriptInvoker { get; init; }

    /// <summary>
    /// 按归属解析界面门面（ui-mod-binding 规格 §5.4）。界面只允许取「自己功能」的门面，
    /// 取代此前直接访问 <c>AppRoot.Services</c> 的跨功能取数。
    /// </summary>
    /// <remarks>
    /// Run 的门面取的是 <c>RunRuntime.Current</c> 而<b>不是</b>启动期实例——因为 Run 是会话级对象，
    /// 每次 Run 都会换一个新的 <c>RunController</c>。
    /// </remarks>
    public object? ResolveUiFacade(string ownerModId) => ownerModId switch
    {
        GlobalMod.FeatureId => GlobalController,
        RunMod.FeatureId => RunRuntime.Current,
        _ => null,
    };
}

/// <summary>
/// 功能 Mod 组合工厂：按序注册 UI、启动各功能模块，并重建内容 Mod 管线。
/// </summary>
public sealed class ModFactory
{
    public static ModStartupResult Bootstrap(ModStartupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var global = BootstrapGlobalMod(context);
        RegisterBuiltinConditions();
        var content = BootstrapContentMods(context, global.Mod);

        // 内容 Mod 规格 §5：内容指纹与注册表版本联动，写进全局存档以便 Run 存档判断内容是否被改动。
        // 冷启动的 Rebuild 在 BootstrapContentMods 内完成；之后新增的 Rebuild 调用点（如未来的主菜单 Mod 设置）
        // 同样需要回写指纹。
        global.Controller.UpdateContentVersionHash(content.Pipeline.Registry.ContentVersionHash);

        var result = new ModStartupResult
        {
            GlobalMod = global.Mod,
            GlobalController = global.Controller,
            GlobalSaveService = global.SaveService,
            ContentModPipeline = content.Pipeline,
            ScriptRuntime = content.ScriptRuntime,
            ContentEffectScriptHost = content.ContentEffectScriptHost,
            StoryScriptInvoker = content.StoryScriptInvoker,
            EventScriptInvoker = content.EventScriptInvoker,
            BattleScriptInvoker = content.BattleScriptInvoker,
            EnemyAiScriptInvoker = content.EnemyAiScriptInvoker,
        };

        AppRoot.Initialize(result);
        RegisterBuiltinKeywords();
        return result;
    }

    private static void RegisterBuiltinKeywords()
    {
        KeywordCatalog.Shared.WarningHandler = msg => AppLog.Warning(msg, "Keyword");
        KeywordCatalog.Shared.Clear();
        BuiltinKeywords.RegisterAll(KeywordCatalog.Shared);
    }

    private static void RegisterBuiltinConditions()
    {
        ConditionDomains.Persistent.Clear();
        ConditionDomains.Combat.Clear();
        BuiltinPersistentConditions.RegisterAll(ConditionDomains.Persistent);
        BuiltinCombatConditions.RegisterAll(ConditionDomains.Combat);
    }

    private static (GlobalMod Mod, GlobalModController Controller, GlobalSaveService SaveService) BootstrapGlobalMod(
        ModStartupContext context)
    {
        var mod = new GlobalMod();
        var saveService = new GlobalSaveService(context.SaveDirectory);
        var controller = new GlobalModController(mod, saveService);

        controller.LoadFromDisk();

        return (mod, controller, saveService);
    }

    /// <summary>
    /// 内容加载报告有异常（跳过 / 冲突 / 校验剔除 / 脚本错误）时汇总一条告警日志；
    /// 明细由 <see cref="GodotContentModLogger"/> 逐条输出，这里只给一行可读摘要。
    /// </summary>
    private static void LogContentReportIssues(IAppLog appLog, ContentLoadReport report)
    {
        if (!report.HasIssues)
        {
            return;
        }

        appLog.Warning(
            $"Content load report has issues: skippedMods={report.SkippedMods.Count}, "
            + $"idConflicts={report.IdConflicts.Count}, "
            + $"removedDefinitions={report.RemovedValidationErrors.Count}, "
            + $"validationErrors={report.ValidationErrors.Count}, "
            + $"scriptLoadErrors={report.ScriptLoadErrors.Count}",
            "ContentMod");
    }

    private static (
        ContentModPipeline Pipeline,
        ModScriptRuntime ScriptRuntime,
        PuertsContentEffectScriptHost ContentEffectScriptHost,
        StoryScriptInvoker StoryScriptInvoker,
        EventScriptInvoker EventScriptInvoker,
        BattleScriptInvoker BattleScriptInvoker,
        EnemyAiScriptInvoker EnemyAiScriptInvoker) BootstrapContentMods(
        ModStartupContext context,
        GlobalMod globalMod)
    {
        ContentModBootstrap.EnsureDefaultModsCopied(
            context.ContentModRootDirectory,
            context.BundledContentModsDirectory,
            context.ForceContentModRefresh);

        var appLog = new StaticAppLogBridge();
        var registry = new GameDefinitionRegistry();
        var catalog = new ModScriptCatalog();
        var scriptRuntime = new ModScriptRuntime(catalog, registry, new AppLogModScriptLogger(appLog));
        var prewarmer = new ModScriptPrewarmer(scriptRuntime, registry);
        var pipeline = new ContentModPipeline(
            context.ContentModRootDirectory,
            registry,
            new GodotContentModLogger(appLog),
            new NullContentModUserNotifier(),
            scriptRuntime,
            catalog,
            new GodotContentModTranslationLoader(),
            prewarmer);

        var enabledModIds = globalMod.Current.EnabledModIds ?? GlobalSaveDto.CreateDefault().EnabledModIds!;

        // 内容 Mod 规格 §5：Run 进行中误调用 Rebuild 必须被拒绝。frame 层不依赖 Run 状态，
        // 因此由组合根（本处）把「当前没有进行中的 Run」接到管线守卫上；未接线时守卫默认放行。
        pipeline.CanRebuild = static () => RunRuntime.Current is null;

        pipeline.Rebuild(enabledModIds);

        // 报告默认只落日志（Notifier 首版为空实现），LatestReport 保留最近一次结果供组合根 / 后续主菜单 Mod UI 读取。
        // 注意：内容 Mod 规格 §7 的 Fatal 分级（Mod 根目录不可读、合并后无有效基础内容 ⇒ 阻止开新 Run、主菜单强提示）
        // 暂未实现，这里只告警不阻断；是否阻断属于产品决策，落地前不在这里补。
        LogContentReportIssues(appLog, pipeline.LatestReport);

        return (
            pipeline,
            scriptRuntime,
            new PuertsContentEffectScriptHost(scriptRuntime, registry),
            new StoryScriptInvoker(scriptRuntime, registry),
            new EventScriptInvoker(scriptRuntime, registry),
            new BattleScriptInvoker(scriptRuntime, registry),
            new EnemyAiScriptInvoker(scriptRuntime, registry));
    }
}