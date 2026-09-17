using Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Scripting;
using KemoCard.Frame.UI;
using KemoCard.Mod.Run.Save;

namespace KemoCard.Mod.Run;

/// <summary>
/// Run 会话门面：持有当前 RunController，供选故事 / Run 主界面读取与操作。
/// seed &lt; 0 视为随机；&gt;= 0 视为手写 seed（精确成为 RunSeed）。
/// 存档为单槽：user://saves/run/，覆盖式写入。
/// </summary>
public static class RunRuntime
{
    private static RunController? _current;
    private static RunSaveService? _saveService;

    public static RunController? Current => _current;

    /// <summary>
    /// 单槽存档门面（懒加载；目录 user://saves/run）。
    /// </summary>
    public static RunSaveService SaveService
    {
        get
        {
            if (_saveService != null)
            {
                return _saveService;
            }

            var dir = ProjectSettings.GlobalizePath("user://saves/run");
            // 必须接日志：损坏存档归档、写盘失败、删档失败都只走 logWarning，
            // 不接就等于整条异常路径静默无输出。
            _saveService = new RunSaveService(dir, message => AppLog.Warning(message, "RunSave"));
            return _saveService;
        }
    }

    /// <summary>
    /// 是否存在可继续的存档。
    /// </summary>
    public static bool HasSave => SaveService.Exists;

    public static RunController CreateNew(string storyId, int seed, IReadOnlyList<CharacterDto> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        ArgumentNullException.ThrowIfNull(candidates);

        SaveService.Delete();
        CloseRunUi();
        _current?.Dispose();
        var controller = CreateController();
        controller.EnableAutoSave(SaveService);
        var hostRng = seed >= 0
            ? new HostRng(seed, "story_select")
            : new HostRng(Random.Shared.Next(1, int.MaxValue), "story_select");
        controller.CreateRun(storyId, hostRng, candidates, isMultiplayer: false);
        _current = controller;
        return controller;
    }

    /// <summary>
    /// 关闭并销毁本功能的全部界面（决策 1：<c>RunMain</c> 生命周期与 Run 会话一致，不留缓存）。
    /// </summary>
    /// <remarks>
    /// 解绑订阅由各节点离场时的 <c>BindingScope</c> 负责；这里只负责"界面随会话一起消失"。
    /// </remarks>
    private static void CloseRunUi() =>
        UIManager.Instance?.CloseByOwner(RunMod.FeatureId, destroy: true);

    /// <summary>
    /// 组合根装配：Run 只从菜单进入，此时 <c>MainRoot</c> 已执行 <c>ModFactory.Bootstrap</c>，
    /// 因此可安全取到真实的脚本宿主（缺省会退化为 Null 实现，导致内容脚本效果静默失效）。
    /// </summary>
    private static RunController CreateController() =>
        new(new RunMod(), AppRoot.Services.ContentEffectScriptHost);

    /// <summary>
    /// 手动 / 退出前保存当前会话。
    /// </summary>
    public static void SaveCurrent()
    {
        _current?.Save(SaveService);
    }

    /// <summary>
    /// 从最近存档恢复会话；失败（无档）返回 false。
    /// </summary>
    public static bool TryLoadLatest()
    {
        var loaded = SaveService.LoadOrDefault();
        if (string.IsNullOrEmpty(loaded.RunId))
        {
            return false;
        }

        _current?.Dispose();
        var controller = CreateController();
        controller.EnableAutoSave(SaveService);
        controller.LoadRun(loaded);
        _current = controller;
        return true;
    }

    /// <summary>
    /// 放弃当前 Run：销毁会话并删除存档（「继续游戏」不再指向已放弃的档）。
    /// </summary>
    public static void Abandon()
    {
        CloseRunUi();
        _current?.Dispose();
        _current = null;
        SaveService.Delete();
    }
}