using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Scripting;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Save;

namespace MainRoot;

public partial class MainRoot : Control
{
    private ModStartupResult? _startup;

    public GlobalMod? GlobalMod => _startup?.GlobalMod;

    public GlobalModController? GlobalController => _startup?.GlobalController;

    public GlobalSaveService? GlobalSaveService => _startup?.GlobalSaveService;

    public ContentModPipeline? ContentModPipeline => _startup?.ContentModPipeline;

    public GameDefinitionRegistry? GameDefinitions => ContentModPipeline?.Registry;

    public ModScriptRuntime? ScriptRuntime => _startup?.ScriptRuntime;

    public PuertsContentEffectScriptHost? ContentEffectScriptHost => _startup?.ContentEffectScriptHost;

    public StoryScriptInvoker? StoryScriptInvoker => _startup?.StoryScriptInvoker;

    public EventScriptInvoker? EventScriptInvoker => _startup?.EventScriptInvoker;

    public BattleScriptInvoker? BattleScriptInvoker => _startup?.BattleScriptInvoker;

    public EnemyAiScriptInvoker? EnemyAiScriptInvoker => _startup?.EnemyAiScriptInvoker;

    public override void _Ready()
    {
        InitUIManager();

        EventDispatcher.Configure(new GodotEventDispatcherLogger());

        _startup = ModFactory.Bootstrap(new()
        {
            SaveDirectory = ProjectSettings.GlobalizePath("user://saves"),
            ContentModRootDirectory = ProjectSettings.GlobalizePath("user://mods"),
            BundledContentModsDirectory = ProjectSettings.GlobalizePath("res://Config/mods"),
        });
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
        GlobalController?.Dispose();
        GlobalMod?.Dispose();
        ScriptRuntime?.Dispose();
        _startup = null;
        base._ExitTree();
    }

    private void QuitGame()
    {
        GetTree().Quit();
    }
}
