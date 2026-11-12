namespace PowerToyWalker

open System
open System.Globalization
open System.IO
open System.Threading
open System.Windows.Controls
open System.Windows.Input
open Microsoft.PowerToys.Settings.UI.Library
open Walker.Core
open Wox.Plugin

module private PowerToysLanguage =
    /// The language picked in PowerToys settings, as its LanguageHelper reads it, or "" for
    /// the Windows display language. PowerToys Run applies it to its main thread only, and
    /// plugins answer queries on other threads, so the plugin reads the setting itself.
    let load () =
        let path =
            Path.Combine(
                Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData,
                "Microsoft",
                "PowerToys",
                "language.json"
            )

        try
            if File.Exists path then
                Strings.languageOfSettings (File.ReadAllText path)
            else
                ""
        with
        | :? IOException
        | :? UnauthorizedAccessException -> ""

[<Sealed>]
type Main() =
    [<VolatileField>]
    let mutable tracker: WindowTracker option = None

    [<VolatileField>]
    let mutable context: PluginInitContext option = None

    [<VolatileField>]
    let mutable options = SearchOptions.defaults

    let mutable disposed = 0
    let icons = new WindowIcons()

    // PowerToys modules apply a new language when they restart, so it is read once.
    let strings =
        lazy (Strings.choose (PowerToysLanguage.load ()) CultureInfo.CurrentUICulture.Name)

    let activate window =
        let activated = NativeWindows.activate window

        if not activated then
            context
            |> Option.iter _.API.ShowMsg(WindowSearch.PluginName, strings.Value.ActivationFailed)

        activated

    let toResult (hit: RankedWindow) =
        Result(
            Title = WindowSearch.resultTitle hit.Window,
            SubTitle = WindowSearch.resultSubtitle strings.Value hit.Window,
            Score = hit.Score,
            DisableUsageBasedScoring = true,
            ContextData = box hit.Window,
            // A nonempty IcoPath overrides Icon in PowerToys Run.
            Icon = Result.IconDelegate(fun () -> icons.Get hit.Window),
            Action = Func<ActionContext, bool>(fun _ -> activate hit.Window)
        )

    let closeWindowItem window =
        ContextMenuResult(
            PluginName = WindowSearch.PluginName,
            Title = $"{strings.Value.CloseWindow} ({WindowSearch.PluginName})",
            Glyph = "\uE711",
            FontFamily = "Segoe MDL2 Assets",
            AcceleratorKey = Key.W,
            AcceleratorModifiers = ModifierKeys.Control,
            Action = Func<ActionContext, bool>(fun _ -> NativeWindows.close window)
        )

    let toAdditionalOption (setting: SearchSetting) =
        let text = setting.Text strings.Value

        PluginAdditionalOption(
            Key = setting.Key,
            DisplayLabel = text.Label,
            Value = SearchSettings.defaultValue setting,
            DisplayDescription = text.Description
        )

    /// PowerToys Run reflects on this to validate plugin.json.
    static member PluginID = "A8724BE7A2C2494FAD64D0B94CB3CF79"

    interface IPlugin with
        member _.Name = WindowSearch.PluginName

        member _.Description = strings.Value.PluginDescription

        member _.Init(initContext) =
            context <- Some initContext
            tracker <- Some(new WindowTracker())

        member _.Query(query) =
            ArgumentNullException.ThrowIfNull query

            match tracker with
            | None -> ResizeArray()
            | Some current ->
                let scope =
                    if String.IsNullOrEmpty query.ActionKeyword then
                        GlobalQuery
                    else
                        KeywordQuery

                current.Windows
                |> WindowSearch.search options scope current.PreviousWindow query.Search
                |> List.map toResult
                |> ResizeArray

    interface IContextMenu with
        member _.LoadContextMenus(selected) =
            match selected.ContextData with
            | :? WindowInfo as window -> ResizeArray [ closeWindowItem window ]
            | _ -> ResizeArray()

    interface ISettingProvider with
        member _.AdditionalOptions =
            SearchSettings.all |> List.map toAdditionalOption |> List.toSeq

        member _.UpdateSettings(settings) =
            options <-
                settings.AdditionalOptions
                |> Option.ofObj
                |> Option.map (Seq.map (fun option -> option.Key, option.Value) >> Map.ofSeq)
                |> Option.defaultValue Map.empty
                |> SearchSettings.toOptions

        member _.CreateSettingPanel() =
            UserControl(Content = TextBlock(Text = strings.Value.SettingsPanel))

    interface IDisposable with
        member _.Dispose() =
            if Interlocked.Exchange(&disposed, 1) = 0 then
                (icons :> IDisposable).Dispose()
                tracker |> Option.iter (fun current -> (current :> IDisposable).Dispose())
                tracker <- None
