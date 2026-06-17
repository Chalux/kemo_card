using System.Diagnostics;
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
	private readonly string _directoryPath;
	private readonly object _ioGate = new();
	private readonly Action<string>? _logWarning;

	public GlobalSaveService(string directoryPath, Action<string>? logWarning = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
		_directoryPath = directoryPath;
		Directory.CreateDirectory(directoryPath);
		_filePath = Path.Combine(directoryPath, "global_save.json");
		_backupPath = Path.Combine(directoryPath, "global_save.bak.json");
		_tempPath = Path.Combine(directoryPath, "global_save.tmp.json");
		_logWarning = logWarning;
	}

	public bool Exists
	{
		get
		{
			lock (_ioGate)
			{
				return File.Exists(_filePath);
			}
		}
	}

	public GlobalSaveDto LoadOrDefault()
	{
		lock (_ioGate)
		{
			if (TryRead(_filePath, out var primary, isPrimary: true))
			{
				return primary;
			}

			if (TryRead(_backupPath, out var backup, isPrimary: false))
			{
				if (File.Exists(_filePath))
				{
					LogWarning($"Global save primary file unreadable; loaded backup from '{_backupPath}'.");
				}

				return backup;
			}

			return GlobalSaveDto.CreateDefault();
		}
	}

	public void Save(GlobalSaveDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);
		var json = JsonSerializer.Serialize(dto, JsonOptions);

		lock (_ioGate)
		{
			WriteAtomic(json);
		}
	}

	private bool TryRead(string path, out GlobalSaveDto dto, bool isPrimary)
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
				LogWarning($"Global save at '{path}' deserialized to null.");
				return false;
			}

			dto = loaded;
			return true;
		}
		catch (JsonException ex)
		{
			LogWarning($"Global save JSON error at '{path}': {ex.Message}");
			if (isPrimary)
			{
				ArchiveCorruptFile(path);
			}

			return false;
		}
		catch (IOException ex)
		{
			LogWarning($"Global save IO error at '{path}': {ex.Message}");
			return false;
		}
	}

	private void ArchiveCorruptFile(string path)
	{
		try
		{
			var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
			var archivePath = Path.Combine(_directoryPath, $"global_save.corrupt.{stamp}.json");
			File.Copy(path, archivePath, overwrite: false);
		}
		catch (Exception ex)
		{
			LogWarning($"Failed to archive corrupt save '{path}': {ex.Message}");
		}
	}

	private void WriteAtomic(string json)
	{
		File.WriteAllText(_tempPath, json);
		if (File.Exists(_filePath))
		{
			File.Replace(_tempPath, _filePath, _backupPath, ignoreMetadataErrors: true);
			return;
		}

		File.Move(_tempPath, _filePath);
	}

	private void LogWarning(string message)
	{
		if (_logWarning is not null)
		{
			_logWarning(message);
			return;
		}

		Trace.TraceWarning(message);
	}
}
