namespace Walker.Core

/// What the plugin knows about open windows, held as one immutable value.
type TrackedWindows =
    {
        Windows: WindowInfo[]
        /// Handle to the sequence number of its last activation.
        Recency: Map<int64, int64>
        Sequence: int64
        Previous: int64 option
    }

module WindowTracking =
    let initial =
        {
            Windows = [||]
            Recency = Map.empty
            Sequence = 0L
            Previous = None
        }

    /// A window came to the foreground.
    let activated handle state =
        let sequence = state.Sequence + 1L

        { state with
            Recency = state.Recency |> Map.add handle sequence
            Sequence = sequence
            Previous = Some handle
        }

    /// A fresh capture, in Z order from the top, replaces the snapshot. Windows never
    /// activated rank below all others, by Z order. Closed windows are forgotten.
    let captured (windows: WindowInfo[]) state =
        let ranked =
            windows
            |> Array.mapi (fun index window ->
                let neverActivated = int64 (-index - 1)

                { window with
                    LastActivated = state.Recency |> Map.tryFind window.Handle |> Option.defaultValue neverActivated
                })

        let isOpen = ranked |> Seq.map _.Handle |> Set.ofSeq

        { state with
            Windows = ranked
            Recency = state.Recency |> Map.filter (fun handle _ -> isOpen.Contains handle)
        }
