using System.Text.Json;

namespace KemoCard.Mod.Global.Save;

/// <summary>
/// 全局存档读写：独立文件、原子写、保留一份备份。
/// </summary>
public sealed class GlobalSaveService
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private readonly string _filePath;
	private readonly string _backupPath;
	private readonly string _tempPath;

	public GlobalSaveService(string directoryPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
		Directory.CreateDirectory(directoryPath);
		_filePath = Path.Combine(directoryPath, "global_save.json");
		_backupPath = Path.Combine(directoryPath, "global_save.bak.json");
		_tempPath = Path.Combine(directoryPath, "global_save.tmp.json");
	}

	public bool Exists => File.Exists(_filePath);

	public GlobalSaveDto LoadOrDefault()
	{
		if (TryRead(_filePath, out var primary))
		{
			return primary;
		}

		if (TryRead(_backupPath, out var backup))
		{
			return backup;
		}

		return GlobalSaveDto.CreateDefault();
	}

	public void Save(GlobalSaveDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);
		var json = JsonSerializer.Serialize(dto, JsonOptions);
		WriteAtomic(json);
	}

	private bool TryRead(string path, out GlobalSaveDto dto)
	{
		dto = GlobalSaveDto.CreateDefault();
		if (!File.Exists(path))
		{
			return false;
		}

		try
		{
			var json = File.ReadAllText(path);
			var loaded = JsonSerializer.Deserialize<GlobalSaveDto>(json, JsonOptions);
			if (loaded is null)
			{
				return false;
			}

			dto = loaded;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private void WriteAtomic(string json)
	{
		File.WriteAllText(_tempPath, json);
		if (File.Exists(_filePath))
		{
			File.Copy(_filePath, _backupPath, overwrite: true);
		}

		if (File.Exists(_filePath))
		{
			File.Delete(_filePath);
		}

		File.Move(_tempPath, _filePath);
	}
}
