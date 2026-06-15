using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Ui;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Save;

namespace KemoCard.Frame.Mvc;

public sealed class ModStartupContext
{
    public required UiManager UiManager { get; init; }

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
        var contentModPipeline = BootstrapContentMods(context, global.Mod);

        return new ModStartupResult
        {
            GlobalMod = global.Mod,
            GlobalController = global.Controller,
            GlobalSaveService = global.SaveService,
            ContentModPipeline = contentModPipeline,
        };
    }

    private static (GlobalMod Mod, GlobalModController Controller, GlobalSaveService SaveService) BootstrapGlobalMod(
        ModStartupContext context)
    {
        var mod = new GlobalMod();
        var saveService = new GlobalSaveService(context.SaveDirectory);
        var controller = new GlobalModController(mod, saveService, context.UiManager);

        GlobalMod.RegisterUi(context.UiManager);
        controller.LoadFromDisk();

        return (mod, controller, saveService);
    }

    private static ContentModPipeline BootstrapContentMods(ModStartupContext context, GlobalMod globalMod)
    {
        ContentModBootstrap.EnsureDefaultModsCopied(
            context.ContentModRootDirectory,
            context.BundledContentModsDirectory);

        var registry = new GameDefinitionRegistry();
        var pipeline = new ContentModPipeline(
            context.ContentModRootDirectory,
            registry,
            new GodotContentModLogger(),
            new NullContentModUserNotifier(),
            new GodotContentModTranslationLoader());

        var enabledModIds = globalMod.Current.EnabledModIds ?? GlobalSaveDto.CreateDefault().EnabledModIds!;
        pipeline.Rebuild(enabledModIds);

        return pipeline;
    }
}
