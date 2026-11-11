namespace Walker.Core

open System

type WindowInfo =
    {
        Handle: int64
        ProcessId: int
        Title: string
        AppName: string
        ProcessName: string
        ExecutablePath: string
        Minimized: bool
        OnCurrentDesktop: bool
        LastActivated: int64
    }

type SearchOptions =
    {
        PreferWindowsInGlobalSearch: bool
        MatchTitlesInGlobalSearch: bool
        CurrentWindowLast: bool
        IncludeMinimized: bool
        AllDesktops: bool
        FuzzyMatching: bool
    }

module SearchOptions =
    let defaults =
        {
            PreferWindowsInGlobalSearch = true
            MatchTitlesInGlobalSearch = false
            CurrentWindowLast = true
            IncludeMinimized = true
            AllDesktops = true
            FuzzyMatching = true
        }

/// How a query reached the plugin: mixed into PowerToys Run's global results,
/// or addressed to this plugin through its action keyword.
type SearchScope =
    | GlobalQuery
    | KeywordQuery

type RankedWindow = { Window: WindowInfo; Score: int }

module WindowSearch =
    [<Literal>]
    let PluginName = "powertoy-walker"

    [<Literal>]
    let GlobalPreferenceBoost = 10_000

    let resultTitle (window: WindowInfo) =
        if String.IsNullOrWhiteSpace window.Title then
            window.AppName
        else
            window.Title

    let resultSubtitle (window: WindowInfo) =
        [
            $"{window.AppName} ({PluginName})"
            if window.Minimized then
                "Minimized"
            if not window.OnCurrentDesktop then
                "Other desktop"
        ]
        |> String.concat " · "

    let private sameLetter (left: char) (right: char) =
        Char.ToUpperInvariant left = Char.ToUpperInvariant right

    /// Words start at the beginning of the text, after a character that is neither a letter
    /// nor a digit, and at a capital that follows a lower-case letter (the T of WezTerm).
    let private isWordStart (text: string) index =
        Char.IsLetterOrDigit text[index]
        && (index = 0
            || not (Char.IsLetterOrDigit text[index - 1])
            || Char.IsLower text[index - 1] && Char.IsUpper text[index])

    [<TailCall>]
    let rec private nextWordStart (text: string) index =
        if index >= text.Length then -1
        elif isWordStart text index then index
        else nextWordStart text (index + 1)

    [<TailCall>]
    let rec private occursAtWordStart (term: string) (text: string) from =
        match text.IndexOf(term, from, StringComparison.OrdinalIgnoreCase) with
        | -1 -> false
        | index -> isWordStart text index || occursAtWordStart term text (index + 1)

    /// The rest of the term are the first letters of the next words, one word per letter.
    [<TailCall>]
    let rec private matchesInitials (term: string) (text: string) termAt textAt =
        termAt = term.Length
        || (match nextWordStart text textAt with
            | -1 -> false
            | start ->
                sameLetter text[start] term[termAt]
                && matchesInitials term text (termAt + 1) (start + 1))

    /// The rest of the term appears in order before this run of letters and digits ends.
    [<TailCall>]
    let rec private matchesWithinWord (term: string) (text: string) termAt textAt =
        termAt = term.Length
        || textAt < text.Length
           && Char.IsLetterOrDigit text[textAt]
           && matchesWithinWord
               term
               text
               (if sameLetter text[textAt] term[termAt] then
                    termAt + 1
                else
                    termAt)
               (textAt + 1)

    /// Abbreviations start at a word: initials of consecutive words ("vsc" for Visual Studio
    /// Code), or letters in order within one word ("frfx" for Firefox). Letters scattered
    /// across a long title would match almost any short query, so they do not count.
    [<TailCall>]
    let rec private abbreviationQuality (term: string) (text: string) from best =
        match nextWordStart text from with
        | -1 -> best
        | start ->
            let quality =
                if not (sameLetter text[start] term[0]) then 0
                elif matchesInitials term text 1 (start + 1) then 60
                elif matchesWithinWord term text 1 (start + 1) then 40
                else 0

            if quality = 60 then
                quality
            else
                abbreviationQuality term text (start + 1) (max best quality)

    let private matchQuality fuzzy (term: string) (text: string) =
        if String.Equals(term, text, StringComparison.OrdinalIgnoreCase) then
            100
        elif text.StartsWith(term, StringComparison.OrdinalIgnoreCase) then
            90
        elif occursAtWordStart term text 0 then
            80
        elif text.Contains(term, StringComparison.OrdinalIgnoreCase) then
            70
        elif fuzzy then
            abbreviationQuality term text 0 0
        else
            0

    let private termQuality options scope (window: WindowInfo) term =
        let quality = matchQuality options.FuzzyMatching term
        let nameQuality = max (quality window.AppName) (quality window.ProcessName)

        if scope = KeywordQuery || options.MatchTitlesInGlobalSearch then
            max nameQuality (quality window.Title * 9 / 10)
        else
            nameQuality

    [<TailCall>]
    let rec private sumQuality options scope (terms: string[]) window index sum =
        if index = terms.Length then
            Some(sum / terms.Length)
        else
            match termQuality options scope window terms[index] with
            | 0 -> None
            | quality -> sumQuality options scope terms window (index + 1) (sum + quality)

    /// The average quality over all terms, or None as soon as one term misses.
    let private averageQuality options scope (terms: string[]) window =
        sumQuality options scope terms window 0 0

    /// Most recent first, with the window the user came from optionally moved last.
    /// Spelled out because this runs on every keystroke, and comparing tuples boxes.
    let private mruOrder options previous (left: WindowInfo) (right: WindowInfo) =
        let isDemoted (window: WindowInfo) =
            options.CurrentWindowLast && Option.contains window.Handle previous

        match compare (isDemoted left) (isDemoted right) with
        | 0 ->
            match compare right.LastActivated left.LastActivated with
            | 0 -> compare left.Handle right.Handle
            | order -> order
        | order -> order

    let private isListed options (window: WindowInfo) =
        (options.IncludeMinimized || not window.Minimized)
        && (options.AllDesktops || window.OnCurrentDesktop)

    let search options scope (previous: int64 option) (text: string) (windows: seq<WindowInfo>) =
        let terms = text.Split(Array.empty<char>, StringSplitOptions.RemoveEmptyEntries)

        if scope = GlobalQuery && terms.Length = 0 then
            []
        else
            let candidates =
                windows
                |> Seq.filter (isListed options)
                |> Seq.sortWith (mruOrder options previous)
                |> Seq.toArray

            let count = candidates.Length

            let boost =
                if scope = GlobalQuery && options.PreferWindowsInGlobalSearch then
                    GlobalPreferenceBoost
                else
                    0

            // Match quality dominates recency. The recency term breaks ties;
            // keyword searches with no terms retain MRU order even with many windows.
            let score rank window =
                if terms.Length = 0 then
                    Some(1_000_000 - rank)
                else
                    averageQuality options scope terms window
                    |> Option.map (fun quality -> quality * (count + 1) + count - rank)

            let tryRank rank window =
                score rank window
                |> Option.map (fun points ->
                    {
                        Window = window
                        Score = points + boost
                    })

            candidates
            |> Seq.mapi tryRank
            |> Seq.choose id
            |> Seq.sortByDescending _.Score
            |> Seq.toList
