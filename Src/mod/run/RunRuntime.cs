using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Run;

/// <summary>
/// Run 会话门面：持有当前 RunController，供选故事 / Run 主界面读取与操作。
/// seed &lt; 0 视为随机；&gt;= 0 视为手写 seed（精确成为 RunSeed）。
/// </summary>
public static class RunRuntime
{
    private static RunController? _current;

    public static RunController? Current => _current;

    public static RunController CreateNew(string storyId, int seed, IReadOnlyList<CharacterDto> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        ArgumentNullException.ThrowIfNull(candidates);

        _current?.Dispose();
        var controller = new RunController(new RunMod());
        var hostRng = seed >= 0
            ? new HostRng(seed, "story_select")
            : new HostRng(Random.Shared.Next(1, int.MaxValue), "story_select");
        controller.CreateRun(storyId, hostRng, candidates, isMultiplayer: false);
        _current = controller;
        return controller;
    }

    public static void Abandon()
    {
        _current?.Dispose();
        _current = null;
    }
}