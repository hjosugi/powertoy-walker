namespace Walker.Core;

public sealed record WindowInfo(long Handle, int ProcessId, string Title, string AppName,
    string ProcessName, string ExecutablePath, bool Minimized, bool OnCurrentDesktop,
    long LastActivated);

public sealed record SearchOptions(
    bool PreferWindowsInGlobalSearch = true,
    bool MatchTitlesInGlobalSearch = false,
    bool CurrentWindowLast = true,
    bool IncludeMinimized = true,
    bool AllDesktops = true,
    bool FuzzyMatching = true);

public sealed record RankedWindow(WindowInfo Window, int Score);

public static class WindowSearch
{
    public const string PluginName = "powertoy-walker";
    public const int GlobalPreferenceBoost = 10_000;

    public static string ResultTitle(WindowInfo window) =>
        string.IsNullOrWhiteSpace(window.Title) ? window.AppName : window.Title;

    public static string ResultSubtitle(WindowInfo window) => $"{window.AppName} ({PluginName})"
        + (window.Minimized ? " · Minimized" : "")
        + (!window.OnCurrentDesktop ? " · Other desktop" : "");

    public static IReadOnlyList<RankedWindow> Search(IEnumerable<WindowInfo> windows,
        string text, bool triggered, SearchOptions options, long previousWindow = 0)
    {
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!triggered && terms.Length == 0)
            return [];

        var candidates = windows
            .Where(w => options.IncludeMinimized || !w.Minimized)
            .Where(w => options.AllDesktops || w.OnCurrentDesktop)
            .OrderBy(w => options.CurrentWindowLast && w.Handle == previousWindow)
            .ThenByDescending(w => w.LastActivated)
            .ThenBy(w => w.Handle)
            .ToArray();
        var results = new List<RankedWindow>();
        for (var rank = 0; rank < candidates.Length; rank++)
        {
            var window = candidates[rank];
            var quality = 0;
            foreach (var term in terms)
            {
                var match = Math.Max(Match(term, window.AppName, options.FuzzyMatching),
                    Match(term, window.ProcessName, options.FuzzyMatching));
                if (triggered || options.MatchTitlesInGlobalSearch)
                    match = Math.Max(match, Match(term, window.Title, options.FuzzyMatching) * 9 / 10);
                if (match == 0)
                {
                    quality = -1;
                    break;
                }
                quality += match;
            }
            if (quality < 0)
                continue;
            // Match quality dominates recency. The recency term breaks ties;
            // triggered empty searches retain MRU order even with many windows.
            var score = terms.Length == 0 ? 1_000_000 - rank
                : (quality / terms.Length) * (candidates.Length + 1) + candidates.Length - rank;
            if (!triggered && options.PreferWindowsInGlobalSearch)
                score += GlobalPreferenceBoost;
            results.Add(new(window, score));
        }
        return results.OrderByDescending(r => r.Score).ToArray();
    }

    private static int Match(string term, string text, bool fuzzy)
    {
        if (string.Equals(term, text, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (text.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            return 90;
        if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            return 75;
        if (!fuzzy)
            return 0;
        var index = 0;
        foreach (var character in text)
            if (char.ToUpperInvariant(character) == char.ToUpperInvariant(term[index]) && ++index == term.Length)
                return 40;
        return 0;
    }
}
