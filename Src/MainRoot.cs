using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Audio;
using KemoCard.Frame.Display;
using KemoCard.Frame.Locale;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Notification;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Ui.Toast;
using KemoCard.Mod.Run;

namespace MainRoot;

public partial class MainRoot : Control
{
    private const string KeywordTipLayerPath = "res://Src/mod/global/Ui/Tip/KeywordTipLayer.tscn";

    public override void _Ready()
    {
        var appLog = new GodotAppLog();
        AppLog.Configure(appLog);

        BootstrapServices();
        InitSoundManager();
        InitUIManager();
        RedDotService.Configure();
        RedDotService.SetupUpdateNode(GetTree());
        EnsureKeywordTipLayer();
        _ = GlobalModController.OpenMenuAsync();

        EventDispatcher.Configure(new GodotEventDispatcherLogger(appLog));
    }

    private void InitSoundManager()
    {
        var manager = new SoundManager();
        AddChild(manager);
        Sound.Configure(manager);

        var settings = AppRoot.Services.GlobalController.Snapshot.Settings;
        AudioSettingsLoader.Apply(manager, settings);
        var displayState = DisplaySettingsParser.Parse(settings);
        DisplaySettingsApplier.Apply(GetWindow(), displayState);
        LocaleSettingsApplier.Apply(displayState.LanguageCode);
    }

    private static void BootstrapServices()
    {
        var context = new ModStartupContext
        {
            SaveDirectory = ProjectSettings.GlobalizePath("user://saves"),
            ContentModRootDirectory = ProjectSettings.GlobalizePath("user://content_mods"),
            BundledContentModsDirectory = ProjectSettings.GlobalizePath("res://Config/mods"),
            // 调试构建：忽略 mod.json 版本号，每次启动都把随包内容重新暂存到用户目录。
            // 否则开发期新增的角色/卡牌/翻译会因为版本号没变而永远不出现在游戏里。
            ForceContentModRefresh = OS.IsDebugBuild(),
        };

        ModFactory.Bootstrap(context);
    }

    private void InitUIManager()
    {
        // 功能 Mod 的界面由各自声明、在 FeatureModCatalog 统一登记；此处只负责迭代装配，
        // 不再点名具体 Mod（新增功能 Mod 只需改 FeatureModCatalog 一处）。
        var services = AppRoot.Services;
        var features = services.Features;

        var registry = new UIRuntimeRegistry();
        foreach (var feature in features)
        {
            foreach (var registration in feature.Declare())
            {
                registry.Register(registration.ToRuntimeEntry());
            }
        }

        var uiManager = new UIManager();
        AddChild(uiManager);

        uiManager.Init(new UIManagerInitOpt
        {
            Registry = registry,
            StageRoot = this,
            FacadeProvider = services,
            KnownOwnerModIds = [.. features.Select(feature => feature.ModId)],
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

        ToastService.Configure(UIManager.Instance?.GetLayer(EUILayer.Notice));
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
        Sound.Configure(NullSoundService.Instance);
        GlobalEvents.Bus.OffAll();
        AppRoot.Shutdown();
        base._ExitTree();
    }

    private void QuitGame()
    {
        GetTree().Quit();
    }
}