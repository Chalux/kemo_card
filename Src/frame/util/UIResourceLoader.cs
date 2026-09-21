using Godot;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Util;

public static class UIResourceLoader
{
	/// <summary>
	/// 加载界面场景。<b>必须在主线程同步加载</b>（2026-09-19 修复）。
	/// </summary>
	/// <remarks>
	/// <para>此前用的是 <c>ResourceLoader.LoadThreadedRequest</c> + 轮询 + <c>await Task.Delay</c>：
	/// 后台线程加载无法创建 C# 脚本，含 <c>script = ExtResource(...)</c> 的场景会在脚本赋值那一步
	/// 报 <c>Parse Error: Failed.</c> —— 结果是主菜单等所有经 UI 管理器打开的界面都加载失败。</para>
	/// <para>签名与取消令牌保持 <see cref="Task"/> 形态，调用方（UI 状态机）无需改动；
	/// 若要恢复异步加载，必须先把界面脚本改成非 C# 资源或改用 Godot 支持的方式。</para>
	/// </remarks>
	public static Task<PackedScene?> LoadSceneAsync(string path, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(path))
		{
			AppLog.Error("资源路径不能为空.", "UIResourceLoader");
			return Task.FromResult<PackedScene?>(null);
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (!ResourceLoader.Exists(path))
		{
			AppLog.Error($"资源不存在 '{path}'.", "UIResourceLoader");
			return Task.FromResult<PackedScene?>(null);
		}

		var scene = ResourceLoader.Load<PackedScene>(path);
		if (scene == null)
		{
			AppLog.Error($"加载资源失败 '{path}'.", "UIResourceLoader");
			return Task.FromResult<PackedScene?>(null);
		}

		return Task.FromResult<PackedScene?>(scene);
	}

	public static async Task<PackedScene?[]> LoadScenesAsync(
		IEnumerable<string> paths,
		CancellationToken cancellationToken = default)
	{
		string[] pathsArray = [.. paths];
		Task<PackedScene?>[] tasks = [.. pathsArray.Select(path => LoadSceneAsync(path, cancellationToken))];
		return await Task.WhenAll(tasks);
	}
}
