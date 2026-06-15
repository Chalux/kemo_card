using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Ui;
using KemoCard.Mod.Global;

namespace MainRoot;

public partial class MainRoot : Control
{
    public GlobalMod? GlobalMod { get; private set; }

    public ContentModPipeline? ContentModPipeline { get; private set; }

    public GameDefinitionRegistry? GameDefinitions => ContentModPipeline?.Registry;

    public override void _Ready()
    {
        var uiManager = GetNodeOrNull<UiManager>("UiManager");
        var dlgHost = GetNodeOrNull<Control>("DlgCanvas/DlgHost");
        var popupStack = GetNodeOrNull<Control>("PopupCanvas/PopupStack");
        if (uiManager is null || dlgHost is null || popupStack is null)
        {
            GD.PushError("MainRoot: missing UiManager, DlgCanvas/DlgHost, or PopupCanvas/PopupStack.");
            return;
        }

        uiManager.Configure(dlgHost, popupStack);

        EventDispatcher.Configure(new GodotEventDispatcherLogger());

        var modResult = ModFactory.Bootstrap(new()
        {
            UiManager = uiManager,
            SaveDirectory = ProjectSettings.GlobalizePath("user://saves"),
            ContentModRootDirectory = ProjectSettings.GlobalizePath("user://mods"),
            BundledContentModsDirectory = ProjectSettings.GlobalizePath("res://Config/mods"),
        });

        GlobalMod = modResult.GlobalMod;
        ContentModPipeline = modResult.ContentModPipeline;
    }
}
