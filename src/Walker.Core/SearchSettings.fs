namespace Walker.Core

/// A user-facing switch: how to read and change one field of SearchOptions.
/// Its default comes from SearchOptions.defaults, so defaults live in one place.
type SearchSetting =
    {
        Key: string
        Text: Strings -> SettingText
        Get: SearchOptions -> bool
        Set: bool -> SearchOptions -> SearchOptions
    }

module SearchSettings =
    let all =
        [
            {
                Key = "PreferWindowsInGlobalSearch"
                Text = _.PreferWindowsInGlobalSearch
                Get = _.PreferWindowsInGlobalSearch
                Set =
                    fun value options ->
                        { options with
                            PreferWindowsInGlobalSearch = value
                        }
            }
            {
                Key = "MatchTitlesInGlobalSearch"
                Text = _.MatchTitlesInGlobalSearch
                Get = _.MatchTitlesInGlobalSearch
                Set =
                    fun value options ->
                        { options with
                            MatchTitlesInGlobalSearch = value
                        }
            }
            {
                Key = "CurrentWindowLast"
                Text = _.CurrentWindowLast
                Get = _.CurrentWindowLast
                Set =
                    fun value options ->
                        { options with
                            CurrentWindowLast = value
                        }
            }
            {
                Key = "IncludeMinimized"
                Text = _.IncludeMinimized
                Get = _.IncludeMinimized
                Set =
                    fun value options ->
                        { options with
                            IncludeMinimized = value
                        }
            }
            {
                Key = "AllDesktops"
                Text = _.AllDesktops
                Get = _.AllDesktops
                Set = fun value options -> { options with AllDesktops = value }
            }
            {
                Key = "FuzzyMatching"
                Text = _.FuzzyMatching
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
