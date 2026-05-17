using KemoCard.Frame.Ui;
using KemoCard.Mod.Global.Save;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Global;

/// <summary>
/// 全局模块组合根：全局存档 + 通用界面（菜单、图鉴等）。
/// </summary>
public sealed class GlobalMod
{
	private GlobalMod(GlobalModModel model, GlobalModController controller, GlobalSaveService saveService)
	{
		Model = model;
		Controller = controller;
		SaveService = saveService;
	}

	public GlobalModModel Model { get; }

	public GlobalModController Controller { get; }

	public GlobalSaveService SaveService { get; }

	public static GlobalMod Create(IUiManager uiManager, string saveDirectory)
	{
		ArgumentNullException.ThrowIfNull(uiManager);
		ArgumentException.ThrowIfNullOrWhiteSpace(saveDirectory);

		var model = new GlobalModModel();
		var saveService = new GlobalSaveService(saveDirectory);
		var controller = new GlobalModController(model, saveService, uiManager);
		return new GlobalMod(model, controller, saveService);
	}

	public void RegisterUi(UiManager uiManager)
	{
		ArgumentNullException.ThrowIfNull(uiManager);

		uiManager.RegisterDlg<MenuDlg, MenuDlgPayload>(
			GlobalUiIds.Menu,
			static _ => new MenuDlg());

		uiManager.RegisterDlg<CodexDlg, CodexDlgPayload>(
			GlobalUiIds.Codex,
			static _ => new CodexDlg());
	}

	public void Bootstrap()
	{
		Controller.LoadFromDisk();
	}
}
