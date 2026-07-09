using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod;
using KemoCard.Mod.Global;

namespace MainRoot;

public partial class MainRoot : Control
{
    public override void _Ready()
    {
        BootstrapServices();
        InitUIManager();
        _ = GlobalModController.OpenMenuAsync();

        EventDispatcher.Configure(new GodotEventDispatcherLogger());
    }

    private static void BootstrapServices()
    {
        var context = new ModStartupContext
        {
            SaveDirectory = ProjectSettings.GlobalizePath("user://saves"),
            ContentModRootDirectory = ProjectSettings.GlobalizePath("user://content_mods"),
            BundledContentModsDirectory = ProjectSettings.GlobalizePath("res://Config/mods"),
        };

        ModFactory.Bootstrap(context);
    }

    private void InitUIManager()
    {
        var registry = new UIRuntimeRegistry();
        GlobalMod.RegisterUi(registry);

        var uiManager = new UIManager();
        AddChild(uiManager);

        uiManager.Init(new UIManagerInitOpt
        {
            Registry = registry,
            StageRoot = this,
            Layers =
            [
                EUILayer.Win,
                EUILayer.Dlg,
                EUILayer.Loading,
                EUILayer.Pop,
            ],
            TopLayers =
            [
                EUILayer.Debug,
                EUILayer.Notice,
                EUILayer.Guide,
            ],
        });
    }

    public override void _ExitTree()
    {
        GlobalEvents.Bus.OffAll();
        AppRoot.Shutdown();
        base._ExitTree();
    }

    private void QuitGame()
    {
        GetTree().Quit();
    }
}
