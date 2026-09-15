using System.Diagnostics;
using System.Text.Json;
using KemoCard.Mod.Run;

namespace KemoCard.Mod.Run.Save;

/// <summary>
/// 单槽 Run 存档读写：目录内按 RunId 命名、临时文件 + <c>File.Replace</c> 原子写、保留一份 <c>.bak</c>。
/// </summary>
public sealed class RunSaveService
{
    /// <summary>
    /// 损坏存档归档文件名中的标记。归档文件仍是 <c>.json</c>（便于人工查看），
    /// 但必须被排除在「可读存档」扫描之外，否则归档后会被当成新的主存档反复读取。
    /// </summary>
    private const string CorruptMarker = ".corrupt.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directoryPath;
    private readonly object _ioGate = new();
    private readonly Action<string>? _logWarning;

    public RunSaveService(string directoryPath, Action<string>? logWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = directoryPath;
        _logWarning = logWarning;
        Directory.CreateDirectory(directoryPath);
    }

    public bool Exists
    {
        get
        {
            lock (_ioGate)
            {
                return Directory.GetFiles(_directoryPath, "*.json").Any(IsPrimarySaveFile);
            }
        }
    }

    public RunDto LoadOrDefault()
    {
        lock (_ioGate)
        {
            var primaryFiles = Directory.GetFiles(_directoryPath, "*.json")
                .Where(IsPrimarySaveFile)
                .ToList();

            foreach (var file in primaryFiles)
            {
                if (TryRead(file, out var dto, isPrimary: true))
                    return dto;
            }

            var backupFiles = Directory.GetFiles(_directoryPath, "*.bak.json")
                .Where(IsBackupSaveFile);
            foreach (var file in backupFiles)
            {
                if (TryRead(file, out var dto, isPrimary: false))
                    return dto;
            }

            return new RunDto();
        }
    }

    /// <summary>
    /// 原子写入。
    /// </summary>
    /// <returns>
    /// <c>true</c> 表示已落盘；<c>false</c> 表示 <see cref="RunDto.RunId"/> 为空或写盘失败（磁盘满 / 文件被占用等）。
    /// 这里刻意不抛异常：调用点包含 Godot 信号回调与自动存档，IO 失败不应击穿主循环。
    /// </returns>
    public bool Save(RunDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrEmpty(dto.RunId))
            return false;

        try
        {
            var json = JsonSerializer.Serialize(dto, JsonOptions);
            lock (_ioGate)
            {
                var filePath = Path.Combine(_directoryPath, $"{dto.RunId}.json");
                var backupPath = Path.Combine(_directoryPath, $"{dto.RunId}.bak.json");
                var tempPath = Path.Combine(_directoryPath, $"{dto.RunId}.tmp.json");

                File.WriteAllText(tempPath, json);
                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogWarning($"Run save '{dto.RunId}' failed to write: {ex.Message}");
            return false;
        }
    }

    public void Delete()
    {
        lock (_ioGate)
        {
            foreach (var file in Directory.GetFiles(_directoryPath, "*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    // 删档失败必须留痕：否则「放弃 Run」后旧档仍在，「继续游戏」会把它读回来。
                    LogWarning($"Failed to delete run save '{file}': {ex.Message}");
                }
            }
        }
    }

    private bool TryRead(string path, out RunDto dto, bool isPrimary)
    {
        dto = new RunDto();
        if (!File.Exists(path))
            return false;

        RunDto? loaded;
        try
        {
            var json = File.ReadAllText(path);
            loaded = JsonSerializer.Deserialize<RunDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            HandleUnreadablePrimary(path, isPrimary, $"JSON error: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            // IO 错误（文件被占用等）不代表内容损坏：不得归档，更不得删除。
            LogWarning($"Run save IO error at '{path}': {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogWarning($"Run save access error at '{path}': {ex.Message}");
            return false;
        }

        if (loaded is null)
        {
            LogWarning($"Run save at '{path}' deserialized to null.");
            return false;
        }

        if (loaded.SchemaVersion > RunDto.CurrentSchemaVersion)
        {
            HandleUnreadablePrimary(
                path,
                isPrimary,
                $"schema version {loaded.SchemaVersion} is newer than supported {RunDto.CurrentSchemaVersion}");
            return false;
        }

        dto = loaded.Normalize();
        return true;
    }

    /// <summary>
    /// 主存档不可用时<b>归档</b>而不是删除：移到带时间戳的 <c>.corrupt</c> 文件。
    /// 此前实现直接 <c>File.Delete</c>，用户数据不可逆丢失且整条路径零日志。
    /// 归档（移动而非拷贝）同时让后续扫描不再反复处理同一个坏档。
    /// </summary>
    private void HandleUnreadablePrimary(string path, bool isPrimary, string reason)
    {
        LogWarning($"Run save at '{path}' is unusable ({reason}).");
        if (!isPrimary)
            return;

        try
        {
            var name = Path.GetFileName(path);
            var stem = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? name[..^".json".Length]
                : name;
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var archivePath = Path.Combine(_directoryPath, $"{stem}{CorruptMarker}{stamp}.json");
            File.Move(path, archivePath, overwrite: false);
            LogWarning($"Archived unusable run save to '{archivePath}'.");
        }
        catch (Exception ex)
        {
            // 归档失败就保留原文件，宁可下次再报一次，也不要静默丢数据。
            LogWarning($"Failed to archive unusable run save '{path}': {ex.Message}");
        }
    }

    private static bool IsPrimarySaveFile(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".bak.json", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".tmp.json", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(path).Contains(CorruptMarker, StringComparison.OrdinalIgnoreCase);

    private static bool IsBackupSaveFile(string path) =>
        path.EndsWith(".bak.json", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(path).Contains(CorruptMarker, StringComparison.OrdinalIgnoreCase);

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