using System.Text.Json;
using Walker.Core;

if (args.Contains("--benchmark-only"))
{
    Performance.Measure();
    return;
}

var windows = new[]
{
    new WindowInfo(1, 10, "Inbox - Gmail", "Firefox", "firefox", "firefox.exe", false, true, 30),
    new WindowInfo(2, 20, "Draft", "Text Editor", "editor", "editor.exe", true, true, 20),
    new WindowInfo(3, 30, "Terminal one", "Terminal", "terminal", "terminal.exe", false, false, 10),
    new WindowInfo(4, 30, "Terminal two", "Terminal", "terminal", "terminal.exe", false, true, 40),
};
var defaults = new SearchOptions();
var passed = 0;
void Check(string name, bool condition)
{
    if (!condition)
        throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("ok: " + name);
    passed++;
}
long[] Handles(string text, bool triggered, SearchOptions? options = null, long previous = 0) =>
    WindowSearch.Search(windows, text, triggered, options ?? defaults, previous).Select(r => r.Window.Handle).ToArray();

Check("global empty query stays quiet", Handles("  ", false).Length == 0);
Check("keyword empty query is MRU", Handles("", true).SequenceEqual([4L, 1L, 2L, 3L]));
Check("previous window goes last", Handles("", true, previous: 4).SequenceEqual([1L, 2L, 3L, 4L]));
Check("previous-window demotion is optional", Handles("", true, defaults with { CurrentWindowLast = false }, 4).First() == 4);
Check("global query matches app and process names", Handles("EDITOR", false).SequenceEqual([2L]));
Check("global query ignores titles by default", Handles("gmail", false).Length == 0);
Check("keyword query matches titles", Handles("gmail", true).SequenceEqual([1L]));
Check("global title matching can be enabled", Handles("gmail", false, defaults with { MatchTitlesInGlobalSearch = true }).SequenceEqual([1L]));
Check("tokens can match app and title in any order", Handles("draft text", true).SequenceEqual([2L]));
Check("fuzzy matching supports abbreviations", Handles("frfx", false).SequenceEqual([1L]));
Check("fuzzy matching can be disabled", Handles("frfx", false, defaults with { FuzzyMatching = false }).Length == 0);
Check("unrelated queries never get a boosted result", Handles("zzzz", false).Length == 0);
Check("minimized windows can be excluded", Handles("", true, defaults with { IncludeMinimized = false }).SequenceEqual([4L, 1L, 3L]));
Check("other desktops can be excluded", Handles("terminal", false, defaults with { AllDesktops = false }).SequenceEqual([4L]));
Check("equal matches retain MRU order", Handles("terminal", true).SequenceEqual([4L, 3L]));
var manyWindows = Enumerable.Range(1, 200)
    .Select(i => windows[0] with { Handle = i, LastActivated = i }).ToArray();
var manyScores = WindowSearch.Search(manyWindows, "fire", true, defaults).Select(r => r.Score).ToArray();
Check("200 equal matches have distinct recency scores", manyScores.Zip(manyScores.Skip(1)).All(pair => pair.First > pair.Second));
Check("match quality dominates recency", WindowSearch.Search([
    windows[0] with { Handle = 10, AppName = "Fire", ProcessName = "fire", LastActivated = 1 },
    windows[0] with { Handle = 11, LastActivated = 100 },
], "fire", false, defaults).First().Window.Handle == 10);
var preferred = WindowSearch.Search(windows, "fire", false, defaults).Single();
var normal = WindowSearch.Search(windows, "fire", false, defaults with { PreferWindowsInGlobalSearch = false }).Single();
Check("global preference raises scores without changing matches", preferred.Window == normal.Window && preferred.Score - normal.Score == WindowSearch.GlobalPreferenceBoost);
Check("global preference never changes keyword scores", WindowSearch.Search(windows, "fire", true, defaults).Single().Score == WindowSearch.Search(windows, "fire", true, defaults with { PreferWindowsInGlobalSearch = false }).Single().Score);
Check("window title has no plugin label", WindowSearch.ResultTitle(windows[0]) == "Inbox - Gmail");
Check("plugin label follows the app name on the subtitle", WindowSearch.ResultSubtitle(windows[0]) == "Firefox (powertoy-walker)");
Check("window status follows the app and plugin label", WindowSearch.ResultSubtitle(windows[2]) == "Terminal (powertoy-walker) · Other desktop");
Check("the plugin label is not part of the searchable window text", Handles("powertoy-walker", true).Length == 0);
Check("empty window title falls back to the app name", WindowSearch.ResultTitle(windows[0] with { Title = "" }) == "Firefox");
var manifestPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/PowerToyWalker/plugin.json");
using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
Check("manifest exposes global and prefix modes", manifest.RootElement.GetProperty("IsGlobal").GetBoolean() && manifest.RootElement.GetProperty("ActionKeyword").GetString() == "w");
await CacheTests.Run(Check);
Console.WriteLine($"ALL PASSED ({passed} checks)");
if (args.Contains("--benchmark"))
    Performance.Measure();
