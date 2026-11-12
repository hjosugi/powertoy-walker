module Program

open System
open System.IO
open System.Text.Json
open Microsoft.FSharp.Reflection
open Walker.Core

let private firefox =
    {
        Handle = 1L
        ProcessId = 10
        Title = "Inbox - Gmail"
        AppName = "Firefox"
        ProcessName = "firefox"
        ExecutablePath = "firefox.exe"
        Minimized = false
        OnCurrentDesktop = true
        LastActivated = 30L
    }

let private windows =
    [
        firefox
        {
            Handle = 2L
            ProcessId = 20
            Title = "Draft"
            AppName = "Text Editor"
            ProcessName = "editor"
            ExecutablePath = "editor.exe"
            Minimized = true
            OnCurrentDesktop = true
            LastActivated = 20L
        }
        {
            Handle = 3L
            ProcessId = 30
            Title = "Terminal one"
            AppName = "Terminal"
            ProcessName = "terminal"
            ExecutablePath = "terminal.exe"
            Minimized = false
            OnCurrentDesktop = false
            LastActivated = 10L
        }
        {
            Handle = 4L
            ProcessId = 30
            Title = "Terminal two"
            AppName = "Terminal"
            ProcessName = "terminal"
            ExecutablePath = "terminal.exe"
            Minimized = false
            OnCurrentDesktop = true
            LastActivated = 40L
        }
    ]

let private defaults = SearchOptions.defaults

let private handlesWith options previous scope text =
    WindowSearch.search options scope previous text windows
    |> List.map _.Window.Handle

let private handles = handlesWith defaults None

let private searchTests check =
    check "global empty query stays quiet" (handles GlobalQuery "  " |> List.isEmpty)
    check "keyword empty query is MRU" (handles KeywordQuery "" = [ 4L; 1L; 2L; 3L ])
    check "previous window goes last" (handlesWith defaults (Some 4L) KeywordQuery "" = [ 1L; 2L; 3L; 4L ])

    check
        "previous-window demotion is optional"
        (handlesWith
            { defaults with
                CurrentWindowLast = false
            }
            (Some 4L)
            KeywordQuery
            ""
         |> List.head
            =
            4L)

    check "global query matches app and process names" (handles GlobalQuery "EDITOR" = [ 2L ])
    check "global query ignores titles by default" (handles GlobalQuery "gmail" |> List.isEmpty)
    check "keyword query matches titles" (handles KeywordQuery "gmail" = [ 1L ])

    check
        "global title matching can be enabled"
        (handlesWith
            { defaults with
                MatchTitlesInGlobalSearch = true
            }
            None
            GlobalQuery
            "gmail"
            =
            [ 1L ])

    check "tokens can match app and title in any order" (handles KeywordQuery "draft text" = [ 2L ])
    check "fuzzy matching supports abbreviations" (handles GlobalQuery "frfx" = [ 1L ])

    check
        "fuzzy matching can be disabled"
        (handlesWith { defaults with FuzzyMatching = false } None GlobalQuery "frfx"
         |> List.isEmpty)

    check "unrelated queries never get a boosted result" (handles GlobalQuery "zzzz" |> List.isEmpty)

    let desktop =
        [
            { firefox with
                Handle = 11L
                Title = "Main.fs - powertoy-walker - Visual Studio Code"
                AppName = "Visual Studio Code"
                ProcessName = "Code"
            }
            { firefox with
                Handle = 12L
                Title = "nvim ~/ghq/github.com/hjosugi/powertoy-walker"
                AppName = "WezTerm"
                ProcessName = "wezterm-gui"
            }
            { firefox with
                Handle = 13L
                Title = "GitHub - hjosugi/powertoy-walker - Google Chrome"
                AppName = "Google Chrome"
                ProcessName = "chrome"
            }
            { firefox with
                Handle = 14L
                Title = "memo.txt - Notepad"
                AppName = "Notepad"
                ProcessName = "Notepad"
            }
            { firefox with
                Handle = 15L
                Title = "Spotify Premium"
                AppName = "Spotify"
                ProcessName = "Spotify"
            }
        ]

    let onDesktop text =
        WindowSearch.search defaults KeywordQuery None text desktop
        |> List.map _.Window.Handle
        |> List.sort

    // Letters in order across a whole title made "note" and "spot" match every window here.
    check "abbreviations do not match letters scattered across a title" (onDesktop "note" = [ 14L ])
    check "a short query lists only the windows it names" (onDesktop "spot" = [ 15L ])
    check "initials of consecutive words match" (onDesktop "vsc" = [ 11L ])
    check "initials include capitals within a word" (onDesktop "wt" = [ 12L ])
    check "initials can match inside a title" (onDesktop "pw" = [ 11L; 12L; 13L ])

    check
        "a match at a word start ranks above one inside a word"
        ((WindowSearch.search
            defaults
            KeywordQuery
            None
            "mail"
            [
                { firefox with
                    Handle = 21L
                    Title = "Inbox - Gmail"
                    LastActivated = 100L
                }
                { firefox with
                    Handle = 22L
                    Title = "Inbox - Mail"
                    LastActivated = 1L
                }
            ]
          |> List.map _.Window.Handle)
            =
            [ 22L; 21L ])

    check
        "minimized windows can be excluded"
        (handlesWith
            { defaults with
                IncludeMinimized = false
            }
            None
            KeywordQuery
            ""
            =
            [ 4L; 1L; 3L ])

    check
        "other desktops can be excluded"
        (handlesWith { defaults with AllDesktops = false } None GlobalQuery "terminal" = [ 4L ])

    check "equal matches retain MRU order" (handles KeywordQuery "terminal" = [ 4L; 3L ])

let private scoringTests check =
    let manyWindows =
        [ 1..200 ]
        |> List.map (fun index ->
            { firefox with
                Handle = int64 index
                LastActivated = int64 index
            })

    let manyScores =
        WindowSearch.search defaults KeywordQuery None "fire" manyWindows
        |> List.map _.Score

    check
        "200 equal matches have distinct recency scores"
        (manyScores
         |> List.pairwise
         |> List.forall (fun (first, second) -> first > second))

    check
        "match quality dominates recency"
        ((WindowSearch.search
            defaults
            GlobalQuery
            None
            "fire"
            [
                { firefox with
                    Handle = 10L
                    AppName = "Fire"
                    ProcessName = "fire"
                    LastActivated = 1L
                }
                { firefox with
                    Handle = 11L
                    LastActivated = 100L
                }
            ]
          |> List.head)
            .Window.Handle
            =
            10L)

    let single options scope =
        WindowSearch.search options scope None "fire" windows |> List.exactlyOne

    let withoutPreference =
        { defaults with
            PreferWindowsInGlobalSearch = false
        }

    let preferred = single defaults GlobalQuery
    let normal = single withoutPreference GlobalQuery

    check
        "global preference raises scores without changing matches"
        (preferred.Window = normal.Window
         && preferred.Score - normal.Score = WindowSearch.GlobalPreferenceBoost)

    check
        "global preference never changes keyword scores"
        ((single defaults KeywordQuery).Score = (single withoutPreference KeywordQuery).Score)

let private labelTests check =
    check "window title has no plugin label" (WindowSearch.resultTitle firefox = "Inbox - Gmail")

    check
        "plugin label follows the app name on the subtitle"
        (WindowSearch.resultSubtitle Strings.english firefox = "Firefox (powertoy-walker)")

    check
        "window status follows the app and plugin label"
        (WindowSearch.resultSubtitle Strings.english windows[2] = "Terminal (powertoy-walker) · Other desktop")

    check
        "window status is in the chosen language"
        (WindowSearch.resultSubtitle Strings.japanese windows[1] = "Text Editor (powertoy-walker) · 最小化")

    check
        "the plugin label is not part of the searchable window text"
        (handles KeywordQuery "powertoy-walker" |> List.isEmpty)

    check
        "empty window title falls back to the app name"
        (WindowSearch.resultTitle { firefox with Title = "" } = "Firefox")

/// Every string in a catalog, settings included.
let private textsOf (strings: Strings) =
    FSharpValue.GetRecordFields strings
    |> Array.toList
    |> List.collect (function
        | :? string as text -> [ text ]
        | :? SettingText as setting -> [ setting.Label; setting.Description ]
        | other -> invalidOp $"Unexpected field value {other}")

let private languageTests check =
    let english = textsOf Strings.english

    check
        "every language has every string, and nothing is left in English"
        (Strings.all
         |> List.forall (fun (code, strings) ->
             let texts = textsOf strings

             texts |> List.forall (String.IsNullOrWhiteSpace >> not)
             && (code = "en" || List.forall2 (<>) texts english)))

    check
        "each setting shows its own text"
        (Strings.all
         |> List.forall (fun (_, strings) ->
             SearchSettings.all
             |> List.map (fun setting -> (setting.Text strings).Label)
             |> List.distinct
             |> List.length
                 =
                 SearchSettings.all.Length))

    check "a Japanese display language shows Japanese" (Strings.choose "" "ja-JP" = Strings.japanese)
    check "language tags match without regard to case or region" (Strings.forLanguage "JA" = Strings.japanese)
    check "the PowerToys language comes before the display language" (Strings.choose "en-US" "ja-JP" = Strings.english)
    check "a Japanese PowerToys language shows Japanese" (Strings.choose "ja-JP" "en-US" = Strings.japanese)

    check
        "languages without a translation show English"
        (Strings.choose "" "fr-FR" = Strings.english
         && Strings.choose "" "" = Strings.english)

    check "the language is read from language.json" (Strings.languageOfSettings """{"language":"ja-JP"}""" = "ja-JP")

    check
        "an unreadable language.json means no language was picked"
        ([ ""; "{"; "[]"; """{"language":null}"""; """{"language":7}""" ]
         |> List.forall (Strings.languageOfSettings >> (=) ""))

    // The end-to-end test reads what to expect on screen from this file.
    use expected =
        Path.Combine(AppContext.BaseDirectory, "../../../../../tests/e2e/expected-text.json")
        |> File.ReadAllText
        |> JsonDocument.Parse

    check
        "the end-to-end test expects the plugin's own text"
        (expected.RootElement.EnumerateObject()
         |> Seq.forall (fun language ->
             let strings = Strings.forLanguage language.Name

             let text (name: string) =
                 language.Value.GetProperty(name).GetString()

             text "description" = strings.PluginDescription
             && text "fuzzyMatchingLabel" = strings.FuzzyMatching.Label
             && text "minimized" = strings.Minimized
             && text "otherDesktop" = strings.OtherDesktop
             && text "closeWindow" = strings.CloseWindow))

let private trackingTests check =
    let handlesOf state =
        state.Windows |> Array.map (fun window -> window.Handle, window.LastActivated)

    let captured = [| firefox; windows[1]; windows[2] |]

    let tracked =
        WindowTracking.initial
        |> WindowTracking.activated 2L
        |> WindowTracking.activated 3L
        |> WindowTracking.activated 2L
        |> WindowTracking.captured captured

    check "the latest foreground window is the previous window" (tracked.Previous = Some 2L)

    check "activated windows rank by last use and others by Z order" (handlesOf tracked = [| 1L, -1L; 2L, 3L; 3L, 2L |])

    let afterClose = tracked |> WindowTracking.captured [| firefox |]
    check "closed windows are forgotten" (not (afterClose.Recency.ContainsKey 2L))
    check "a closed previous window stays until another activates" (afterClose.Previous = Some 2L)

let private settingsTests check =
    check
        "settings with no stored value keep their defaults"
        (SearchSettings.toOptions Map.empty = SearchOptions.defaults)

    check
        "every setting changes only its own field"
        (SearchSettings.all
         |> List.forall (fun setting ->
             let flipped = not (SearchSettings.defaultValue setting)
             let options = SearchSettings.toOptions (Map [ setting.Key, flipped ])

             setting.Get options = flipped
             && SearchSettings.all
                |> List.filter (fun other -> other.Key <> setting.Key)
                |> List.forall (fun other -> other.Get options = SearchSettings.defaultValue other)))

let private manifestTests check =
    let path =
        Path.Combine(AppContext.BaseDirectory, "../../../../../src/PowerToyWalker/plugin.json")

    use manifest = JsonDocument.Parse(File.ReadAllText path)
    let root = manifest.RootElement

    check
        "manifest exposes global and prefix modes"
        (root.GetProperty("IsGlobal").GetBoolean()
         && root.GetProperty("ActionKeyword").GetString() = "w:")

    // The host matches an action keyword as a prefix of the text, then skips all global
    // plugins. A keyword of only letters would take over ordinary typing, such as "word".
    check
        "the action keyword cannot take over ordinary typing"
        (root.GetProperty("ActionKeyword").GetString()
         |> nonNull
         |> Seq.exists (Char.IsLetterOrDigit >> not))

    // PowerToys Run rejects any other language value; it means "a .NET assembly".
    check
        "manifest declares the only managed language the host accepts"
        (root.GetProperty("Language").GetString() = "csharp")
    // Without this the host loads the plugin where FSharp.Core cannot be resolved.
    check "manifest loads dependencies from the plugin folder" (root.GetProperty("DynamicLoading").GetBoolean())

[<EntryPoint>]
let main args =
    if args |> Array.contains "--benchmark-only" then
        Performance.measure ()
    else
        let passed = ref 0

        let check name condition =
            if not condition then
                raise (InvalidOperationException("FAIL: " + name))

            printfn "ok: %s" name
            passed.Value <- passed.Value + 1

        searchTests check
        scoringTests check
        labelTests check
        languageTests check
        trackingTests check
        settingsTests check
        manifestTests check
        (CacheTests.run check).GetAwaiter().GetResult()
        printfn "ALL PASSED (%d checks)" passed.Value

        if args |> Array.contains "--benchmark" then
            Performance.measure ()

    0
