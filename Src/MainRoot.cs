using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod;
using KemoCard.Mod.Global;

namespace MainRoot;

public partial class MainRoot : Control
{
    private const string KeywordTipLayerPath = "res://Src/mod/global/Ui/Tip/KeywordTipLayer.tscn";

    public override void _Ready()
    {
        AppLog.Configure(new GodotAppLog());

        BootstrapServices();
        InitUIManager();
        EnsureKeywordTipLayer();
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

    private void EnsureKeywordTipLayer()
    {
        if (!ResourceLoader.Exists(KeywordTipLayerPath))
        {
            AppLog.Warning($"未找到词条提示层场景 {KeywordTipLayerPath}", "MainRoot");
            return;
        }

        var packed = ResourceLoader.Load<PackedScene>(KeywordTipLayerPath);
        var layer = packed.Instantiate();
        AddChild(layer);
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
