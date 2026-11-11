namespace Walker.Core

/// A user-facing switch: how to read and change one field of SearchOptions.
/// Its default comes from SearchOptions.defaults, so defaults live in one place.
type SearchSetting =
    {
        Key: string
        Label: string
        Description: string
        Get: SearchOptions -> bool
        Set: bool -> SearchOptions -> SearchOptions
    }

module SearchSettings =
    let all =
        [
            {
                Key = "PreferWindowsInGlobalSearch"
                Label = "Prefer windows in global search"
                Description =
                    "Adds a score boost to matching windows. Other plugins' score adjustments and usage history may still rank higher."
                Get = _.PreferWindowsInGlobalSearch
                Set =
                    fun value options ->
                        { options with
                            PreferWindowsInGlobalSearch = value
                        }
            }
            {
                Key = "MatchTitlesInGlobalSearch"
                Label = "Match window titles in global search"
                Description =
                    "The action keyword always searches titles. Turn this on to search titles without a keyword too."
                Get = _.MatchTitlesInGlobalSearch
                Set =
                    fun value options ->
                        { options with
                            MatchTitlesInGlobalSearch = value
                        }
            }
            {
                Key = "CurrentWindowLast"
                Label = "List the window you came from last"
                Description = "With an empty keyword query, Enter switches to the previous window like Alt+Tab."
                Get = _.CurrentWindowLast
                Set =
                    fun value options ->
                        { options with
                            CurrentWindowLast = value
                        }
            }
            {
                Key = "IncludeMinimized"
                Label = "Include minimized windows"
                Description = "Selecting a minimized window restores it."
                Get = _.IncludeMinimized
                Set =
                    fun value options ->
                        { options with
                            IncludeMinimized = value
                        }
            }
            {
                Key = "AllDesktops"
                Label = "Include windows on other desktops"
                Description = "Turn off to search only the current virtual desktop."
                Get = _.AllDesktops
                Set = fun value options -> { options with AllDesktops = value }
            }
            {
                Key = "FuzzyMatching"
                Label = "Enable fuzzy matching"
                Description =
                    "Also match abbreviations: the initials of words, such as 'vsc' for Visual Studio Code, or letters in order within a word, such as 'frfx' for Firefox."
                Get = _.FuzzyMatching
                Set = fun value options -> { options with FuzzyMatching = value }
            }
        ]

    let defaultValue setting = setting.Get SearchOptions.defaults

    /// Builds options from stored values. A setting with no stored value keeps its default.
    let toOptions (stored: Map<string, bool>) =
        all
        |> List.fold
            (fun options setting ->
                match stored.TryFind setting.Key with
                | Some value -> setting.Set value options
                | None -> options)
            SearchOptions.defaults
