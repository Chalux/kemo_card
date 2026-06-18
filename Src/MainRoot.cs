using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Scripting;
using KemoCard.Frame.Ui;
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
        var uiManager = GetNodeOrNull<UiManager>("UiManager");
        var dlgHost = GetNodeOrNull<Control>("DlgCanvas/DlgHost");
        var popupStack = GetNodeOrNull<Control>("PopupCanvas/PopupStack");
        if (uiManager is null || dlgHost is null || popupStack is null)
        {
            GD.PushError("MainRoot: missing UiManager, DlgCanvas/DlgHost, or PopupCanvas/PopupStack.");
            CallDeferred(MethodName.QuitGame);
            return;
        }

        uiManager.Configure(dlgHost, popupStack);

        EventDispatcher.Configure(new GodotEventDispatcherLogger());

        _startup = ModFactory.Bootstrap(new()
        {
            UiManager = uiManager,
            SaveDirectory = ProjectSettings.GlobalizePath("user://saves"),
            ContentModRootDirectory = ProjectSettings.GlobalizePath("user://mods"),
            BundledContentModsDirectory = ProjectSettings.GlobalizePath("res://Config/mods"),
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
