using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Save;

namespace KemoCard.Mod;

public sealed class ModStartupContext
{
    public required string SaveDirectory { get; init; }

    public required string ContentModRootDirectory { get; init; }

    public required string BundledContentModsDirectory { get; init; }
}

public sealed class ModStartupResult
{
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
        var content = BootstrapContentMods(context, global.Mod);

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
        KeywordCatalog.Shared.WarningHandler = msg => GD.PushWarning(msg);
        KeywordCatalog.Shared.Clear();
        BuiltinKeywords.RegisterAll(KeywordCatalog.Shared);
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
            context.BundledContentModsDirectory);

        var registry = new GameDefinitionRegistry();
        var catalog = new ModScriptCatalog();
        var scriptRuntime = new ModScriptRuntime(catalog, registry, new NullModScriptLogger());
        var prewarmer = new ModScriptPrewarmer(scriptRuntime, registry);
        var pipeline = new ContentModPipeline(
            context.ContentModRootDirectory,
            registry,
            new GodotContentModLogger(),
            new NullContentModUserNotifier(),
            scriptRuntime,
            catalog,
            new GodotContentModTranslationLoader(),
            prewarmer);

        var enabledModIds = globalMod.Current.EnabledModIds ?? GlobalSaveDto.CreateDefault().EnabledModIds!;
        pipeline.Rebuild(enabledModIds);

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
