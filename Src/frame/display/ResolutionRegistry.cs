namespace KemoCard.Frame.Display;

public readonly record struct ResolutionEntry(string Id, int Width, int Height)
{
    public string DisplayLabel => $"{Width}x{Height}";
}

public static class ResolutionRegistry
{
    private static readonly List<ResolutionEntry> Entries = [];
    private static readonly object Gate = new();

    static ResolutionRegistry() => ResetToBuiltinsForTests();

    public static IReadOnlyList<ResolutionEntry> All
    {
        get { lock (Gate) return Entries.ToArray(); }
    }

    public static void Register(ResolutionEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        lock (Gate)
        {
            if (Entries.Any(e => e.Id == entry.Id))
                return;
            Entries.Add(entry);
        }
    }

    public static bool TryGet(string id, out ResolutionEntry entry)
    {
        lock (Gate)
        {
            foreach (var e in Entries)
            {
                if (e.Id == id)
                {
                    entry = e;
                    return true;
                }
            }
        }
        entry = default;
        return false;
    }

    public static void ResetToBuiltinsForTests()
    {
        lock (Gate)
        {
            Entries.Clear();
            Entries.Add(new ResolutionEntry("1280x720", 1280, 720));
            Entries.Add(new ResolutionEntry("1920x1080", 1920, 1080));
            Entries.Add(new ResolutionEntry("2560x1440", 2560, 1440));
            Entries.Add(new ResolutionEntry("3840x2160", 3840, 2160));
        }
    }
}