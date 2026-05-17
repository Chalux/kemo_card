namespace KemoCard.Frame.Content;

public static class ContentModBootstrap
{
	public static void EnsureDefaultModsCopied(string modRootDirectory, string bundledModsSourceDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(modRootDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(bundledModsSourceDirectory);

		if (!Directory.Exists(bundledModsSourceDirectory))
		{
			return;
		}

		Directory.CreateDirectory(modRootDirectory);
		foreach (var sourceDir in Directory.EnumerateDirectories(bundledModsSourceDirectory))
		{
			var folderName = Path.GetFileName(sourceDir);
			var destDir = Path.Combine(modRootDirectory, folderName);
			if (Directory.Exists(destDir))
			{
				continue;
			}

			CopyDirectoryRecursive(sourceDir, destDir);
		}
	}

	private static void CopyDirectoryRecursive(string sourceDir, string destDir)
	{
		Directory.CreateDirectory(destDir);
		foreach (var file in Directory.EnumerateFiles(sourceDir))
		{
			var destFile = Path.Combine(destDir, Path.GetFileName(file));
			File.Copy(file, destFile);
		}

		foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
		{
			var destSub = Path.Combine(destDir, Path.GetFileName(subDir));
			CopyDirectoryRecursive(subDir, destSub);
		}
	}
}
