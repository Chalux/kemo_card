using Godot;

namespace KemoCard.Frame.Util;

public static class UIResourceLoader
{
	public static async Task<PackedScene?> LoadSceneAsync(string path, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(path))
		{
			GD.PushError("UIResourceLoader: 资源路径不能为空.");
			return null;
		}

		Error err = ResourceLoader.LoadThreadedRequest(path);
		if (err != Error.Ok && err != Error.AlreadyInUse)
		{
			GD.PushError($"UIResourceLoader: 请求加载资源失败 '{path}': {err}.");
			return null;
		}

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
			switch (status)
			{
				case ResourceLoader.ThreadLoadStatus.Loaded:
					return ResourceLoader.LoadThreadedGet(path) as PackedScene;
				case ResourceLoader.ThreadLoadStatus.InvalidResource:
				case ResourceLoader.ThreadLoadStatus.Failed:
					GD.PushError($"UIResourceLoader: 加载资源失败 '{path}': {status}.");
					return null;
				default:
					await Task.Delay(100, cancellationToken);
					break;
			}
		}
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
