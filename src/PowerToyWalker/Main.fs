namespace PowerToyWalker

open System
open System.Threading
open System.Windows.Controls
open System.Windows.Input
open Microsoft.PowerToys.Settings.UI.Library
open Walker.Core
open Wox.Plugin

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

    let activate window =
        let activated = NativeWindows.activate window

        if not activated then
            context
            |> Option.iter
                _.API.ShowMsg(
                    WindowSearch.PluginName,
                    "The window could not be activated. It may have closed or Windows may have blocked the focus change."
                )

        activated

    let toResult (hit: RankedWindow) =
        Result(
            Title = WindowSearch.resultTitle hit.Window,
            SubTitle = WindowSearch.resultSubtitle hit.Window,
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
            Title = $"Close window ({WindowSearch.PluginName})",
            Glyph = "\uE711",
            FontFamily = "Segoe MDL2 Assets",
            AcceleratorKey = Key.W,
            AcceleratorModifiers = ModifierKeys.Control,
            Action = Func<ActionContext, bool>(fun _ -> NativeWindows.close window)
        )

    let toAdditionalOption (setting: SearchSetting) =
        PluginAdditionalOption(
            Key = setting.Key,
            DisplayLabel = setting.Label,
            Value = SearchSettings.defaultValue setting,
            DisplayDescription = setting.Description
        )

    /// PowerToys Run reflects on this to validate plugin.json.
    static member PluginID = "A8724BE7A2C2494FAD64D0B94CB3CF79"

    interface IPlugin with
        member _.Name = WindowSearch.PluginName

        member _.Description =
            "Switch to open windows by app name or title, with app icons and recent windows first"

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
            UserControl(
                Content =
                    TextBlock(
                        Text =
                            "Configure global results, the action keyword, score adjustment and options in PowerToys Run settings."
                    )
            )

    interface IDisposable with
        member _.Dispose() =
            if Interlocked.Exchange(&disposed, 1) = 0 then
                (icons :> IDisposable).Dispose()
                tracker |> Option.iter (fun current -> (current :> IDisposable).Dispose())
                tracker <- None
