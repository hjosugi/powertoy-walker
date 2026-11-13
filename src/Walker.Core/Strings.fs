namespace Walker.Core

open System
open System.Text.Json

type SettingText = { Label: string; Description: string }

/// Everything the plugin shows, in one language. A language that leaves out a string
/// does not compile.
type Strings =
    {
        PluginDescription: string
        Minimized: string
        OtherDesktop: string
        CloseWindow: string
        ActivationFailed: string
        SettingsPanel: string
        PreferWindowsInGlobalSearch: SettingText
        MatchTitlesInGlobalSearch: SettingText
        CurrentWindowLast: SettingText
        IncludeMinimized: SettingText
        AllDesktops: SettingText
        FuzzyMatching: SettingText
    }

module Strings =
    let english =
        {
            PluginDescription = "Switch to open windows by app name or title, with app icons and recent windows first"
            Minimized = "Minimized"
            OtherDesktop = "Other desktop"
            CloseWindow = "Close window"
            ActivationFailed =
                "The window could not be activated. It may have closed or Windows may have blocked the focus change."
            SettingsPanel =
                "Configure global results, the action keyword, score adjustment and options in PowerToys Run settings."
            PreferWindowsInGlobalSearch =
                {
                    Label = "Prefer windows in global search"
                    Description =
                        "Adds a score boost to matching windows. Other plugins' score adjustments and usage history may still rank higher."
                }
            MatchTitlesInGlobalSearch =
                {
                    Label = "Match window titles in global search"
                    Description =
                        "The action keyword always searches titles. Turn this on to search titles without a keyword too."
                }
            CurrentWindowLast =
                {
                    Label = "List the window you came from last"
                    Description = "With an empty keyword query, Enter switches to the previous window like Alt+Tab."
                }
            IncludeMinimized =
                {
                    Label = "Include minimized windows"
                    Description = "Selecting a minimized window restores it."
                }
            AllDesktops =
                {
                    Label = "Include windows on other desktops"
                    Description = "Turn off to search only the current virtual desktop."
                }
            FuzzyMatching =
                {
                    Label = "Enable fuzzy matching"
                    Description =
                        "Also match abbreviations: the initials of words, such as 'vsc' for Visual Studio Code, or letters in order within a word, such as 'frfx' for Firefox."
                }
        }

    let japanese =
        {
            PluginDescription = "開いているウィンドウにアプリ名やタイトルで切り替えます。アイコン付きで、最近使ったウィンドウから並べます"
            Minimized = "最小化"
            OtherDesktop = "別のデスクトップ"
            CloseWindow = "ウィンドウを閉じる"
            ActivationFailed = "ウィンドウに切り替えられませんでした。ウィンドウが閉じられたか、Windows がフォーカスの切り替えを許可しなかった可能性があります。"
            SettingsPanel = "グローバル結果、アクションキーワード、スコア調整、オプションは PowerToys Run の設定で変更できます。"
            PreferWindowsInGlobalSearch =
                {
                    Label = "グローバル検索でウィンドウを優先する"
                    Description = "一致したウィンドウのスコアを上げます。他のプラグインのスコア調整や使用履歴によって、それでも上に並ぶ候補があります。"
                }
            MatchTitlesInGlobalSearch =
                {
                    Label = "グローバル検索でウィンドウのタイトルも照合する"
                    Description = "アクションキーワードを使うと、常にタイトルも検索します。オンにすると、キーワード無しでもタイトルを検索します。"
                }
            CurrentWindowLast =
                {
                    Label = "元のウィンドウを最後に並べる"
                    Description = "アクションキーワードだけを入力して Enter を押すと、Alt+Tab のように直前のウィンドウに切り替わります。"
                }
            IncludeMinimized =
                {
                    Label = "最小化したウィンドウを含める"
                    Description = "最小化したウィンドウを選ぶと、元の大きさに戻します。"
                }
            AllDesktops =
                {
                    Label = "他のデスクトップのウィンドウを含める"
                    Description = "オフにすると、現在の仮想デスクトップだけを検索します。"
                }
            FuzzyMatching =
                {
                    Label = "あいまい一致を使う"
                    Description =
                        "省略形にも一致させます。単語の頭文字 (Visual Studio Code なら 'vsc') や、1つの単語の中で順に並ぶ文字 (Firefox なら 'frfx') です。"
                }
        }

    /// The languages with a translation, by ISO 639-1 code. English comes first, as the fallback.
    let all = [ "en", english; "ja", japanese ]

    /// The strings for a language tag such as "ja-JP", or English when there is no translation.
    let forLanguage (tag: string) =
        let language = tag.Split('-', 2)[0]

        all
        |> List.tryFind (fun (code, _) -> String.Equals(code, language, StringComparison.OrdinalIgnoreCase))
        |> Option.map snd
        |> Option.defaultValue english

    /// The language picked in PowerToys settings comes first. When it is empty, PowerToys
    /// follows the Windows display language, and so does the plugin.
    let choose (powerToysLanguage: string) (displayLanguage: string) =
        forLanguage (
            if String.IsNullOrWhiteSpace powerToysLanguage then
                displayLanguage
            else
                powerToysLanguage
        )

    /// Reads the language tag from the text of PowerToys' language.json, such as
    /// {"language":"ja-JP"}. Anything else means no language was picked.
    let languageOfSettings (json: string) =
        try
            use document = JsonDocument.Parse json
            let root = document.RootElement

            match root.ValueKind with
            | JsonValueKind.Object ->
                match root.TryGetProperty "language" with
                | true, language when language.ValueKind = JsonValueKind.String -> language.GetString() |> nonNull
                | _ -> ""
            | _ -> ""
        with :? JsonException ->
            ""
