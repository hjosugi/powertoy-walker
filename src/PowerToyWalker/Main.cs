using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.PowerToys.Settings.UI.Library;
using Walker.Core;
using Wox.Plugin;

namespace PowerToyWalker;

public sealed class Main : IPlugin, ISettingProvider, IContextMenu, IDisposable
{
    public static string PluginID => "A8724BE7A2C2494FAD64D0B94CB3CF79";
    public string Name => WindowSearch.PluginName;
    public string Description => "Switch to open windows by app name or title, with app icons and recent windows first";
    private WindowTracker? tracker;
    private PluginInitContext? context;
    private SearchOptions options = new();
    private readonly WindowIcons icons = new();
    private int disposed;

    public void Init(PluginInitContext context)
    {
        this.context = context;
        tracker = new WindowTracker();
    }

    public List<Result> Query(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return BuildResults(query);
    }

    private List<Result> BuildResults(Query query)
    {
        var current = tracker;
        if (current is null)
            return [];
        var settings = Volatile.Read(ref options);
        return WindowSearch.Search(current.Windows, query.Search,
            !string.IsNullOrEmpty(query.ActionKeyword), settings, current.PreviousWindow)
            .Select(hit => new Result
            {
                Title = WindowSearch.ResultTitle(hit.Window),
                SubTitle = WindowSearch.ResultSubtitle(hit.Window),
                Score = hit.Score,
                DisableUsageBasedScoring = true,
                ContextData = hit.Window,
                // A nonempty IcoPath overrides Icon in PowerToys Run.
                Icon = () => icons.Get(hit.Window),
                Action = _ => Activate(hit.Window),
            }).ToList();
    }

    private bool Activate(WindowInfo window)
    {
        if (NativeWindows.Activate(window))
            return true;
        context?.API.ShowMsg(Name, "The window could not be activated. It may have closed or Windows may have blocked the focus change.");
        return false;
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not WindowInfo window)
            return [];
        return [new()
        {
            PluginName = Name,
            Title = $"Close window ({Name})",
            Glyph = "\uE711",
            FontFamily = "Segoe MDL2 Assets",
            AcceleratorKey = Key.W,
            AcceleratorModifiers = ModifierKeys.Control,
            Action = _ => NativeWindows.Close(window),
        }];
    }

    public IEnumerable<PluginAdditionalOption> AdditionalOptions =>
    [
        Option("PreferWindowsInGlobalSearch", "Prefer windows in global search", true,
            "Adds a score boost to matching windows. Other plugins' score adjustments and usage history may still rank higher."),
        Option("MatchTitlesInGlobalSearch", "Match window titles in global search", false,
            "The action keyword always searches titles. Turn this on to search titles without a keyword too."),
        Option("CurrentWindowLast", "List the window you came from last", true,
            "With an empty keyword query, Enter switches to the previous window like Alt+Tab."),
        Option("IncludeMinimized", "Include minimized windows", true, "Selecting a minimized window restores it."),
        Option("AllDesktops", "Include windows on other desktops", true, "Turn off to search only the current virtual desktop."),
        Option("FuzzyMatching", "Enable fuzzy matching", true, "Also match characters in order, such as 'frfx' for Firefox."),
    ];

    private static PluginAdditionalOption Option(string key, string label, bool value, string description) => new()
    {
        Key = key, DisplayLabel = label, Value = value, DisplayDescription = description,
    };

    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        var values = settings.AdditionalOptions?.ToDictionary(option => option.Key, option => option.Value)
            ?? new Dictionary<string, bool>();
        bool Read(string key, bool fallback) => values.GetValueOrDefault(key, fallback);
        Volatile.Write(ref options, new(
            Read("PreferWindowsInGlobalSearch", true), Read("MatchTitlesInGlobalSearch", false),
            Read("CurrentWindowLast", true), Read("IncludeMinimized", true),
            Read("AllDesktops", true), Read("FuzzyMatching", true)));
    }

    public Control CreateSettingPanel() => new UserControl
    {
        Content = new TextBlock
        {
            Text = "Configure global results, the action keyword, score adjustment and options in PowerToys Run settings.",
        },
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        icons.Dispose();
        tracker?.Dispose();
        tracker = null;
    }
}
