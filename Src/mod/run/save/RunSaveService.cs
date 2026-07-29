using System.Text.Json;
using KemoCard.Mod.Run;

namespace KemoCard.Mod.Run.Save;

public sealed class RunSaveService
{
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
                return Directory.GetFiles(_directoryPath, "*.json")
                    .Any(f => !f.EndsWith(".bak.json", StringComparison.OrdinalIgnoreCase)
                           && !f.EndsWith(".tmp.json", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public RunDto LoadOrDefault()
    {
        lock (_ioGate)
        {
            var primaryFiles = Directory.GetFiles(_directoryPath, "*.json")
                .Where(f => !f.EndsWith(".bak.json", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".tmp.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in primaryFiles)
            {
                if (TryRead(file, out var dto, isPrimary: true))
                    return dto;
            }

            var backupFiles = Directory.GetFiles(_directoryPath, "*.bak.json");
            foreach (var file in backupFiles)
            {
                if (TryRead(file, out var dto, isPrimary: false))
                    return dto;
            }

            return new RunDto();
        }
    }

    public void Save(RunDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrEmpty(dto.RunId))
            return;

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
    }

    public void Delete()
    {
        lock (_ioGate)
        {
            foreach (var file in Directory.GetFiles(_directoryPath, "*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private bool TryRead(string path, out RunDto dto, bool isPrimary)
    {
        dto = new RunDto();
        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<RunDto>(json, JsonOptions);
            if (loaded == null)
                return false;
            dto = loaded;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LogWarning($"Run save at '{path}' failed to load: {ex.Message}");
            if (isPrimary)
            {
                try { File.Delete(path); } catch { }
            }
            return false;
        }
    }

    private void LogWarning(string message)
    {
        _logWarning?.Invoke(message);
    }
}