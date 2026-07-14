namespace KemoCard.Frame.Locale;

public readonly record struct LocaleEntry(string Code, string DisplayNameKey);

public static class LocaleRegistry
{
    private static readonly List<LocaleEntry> Entries = [];
    private static readonly object Gate = new();

    static LocaleRegistry() => ResetToBuiltinsForTests();

    public static IReadOnlyList<LocaleEntry> All
    {
        get { lock (Gate) return Entries.ToArray(); }
    }

    public static void Register(LocaleEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Code);
        lock (Gate)
        {
            if (Entries.Any(e => e.Code == entry.Code))
                return;
            Entries.Add(entry);
        }
    }

    public static bool TryGet(string code, out LocaleEntry entry)
    {
        lock (Gate)
        {
            foreach (var e in Entries)
            {
                if (e.Code == code)
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
            Entries.Add(new LocaleEntry("zh_CN", "UI_LOCALE_ZH_CN"));
            Entries.Add(new LocaleEntry("en", "UI_LOCALE_EN"));
        }
    }
}
