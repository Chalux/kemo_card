using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Ui;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Save;

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

		var saveDirectory = ProjectSettings.GlobalizePath("user://saves");
		GlobalMod = GlobalMod.Create(uiManager, saveDirectory);
		GlobalMod.RegisterUi(uiManager);
		GlobalMod.Bootstrap();

		var modRoot = ProjectSettings.GlobalizePath("user://mods");
		var bundledMods = ProjectSettings.GlobalizePath("res://Config/mods");
		ContentModBootstrap.EnsureDefaultModsCopied(modRoot, bundledMods);

		var registry = new GameDefinitionRegistry();
		ContentModPipeline = new ContentModPipeline(
			modRoot,
			registry,
			new GodotContentModLogger(),
			new NullContentModUserNotifier());

		var save = GlobalMod.SaveService.LoadOrDefault().Normalize();
		var enabledModIds = save.EnabledModIds ?? GlobalSaveDto.CreateDefault().EnabledModIds!;
		ContentModPipeline.Rebuild(enabledModIds);
	}
}
