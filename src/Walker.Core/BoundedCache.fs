namespace Walker.Core

open System

type internal LruEntry<'Value> = { Value: 'Value; LastUse: int64 }

type internal LruState<'Key, 'Value when 'Key: comparison> =
    {
        Tick: int64
        Entries: Map<'Key, LruEntry<'Value>>
    }

module internal LruState =
    let empty<'Key, 'Value when 'Key: comparison> : LruState<'Key, 'Value> =
        { Tick = 0L; Entries = Map.empty }

    /// Finds a value and marks it most recently used.
    let tryUse key state =
        match state.Entries |> Map.tryFind key with
        | None -> state, None
        | Some entry ->
            let tick = state.Tick + 1L

            {
                Tick = tick
                Entries = state.Entries |> Map.add key { entry with LastUse = tick }
            },
            Some entry.Value

    /// Stores a loaded value and returns the value callers should see: when two loads
    /// race, the first stored wins. A full cache evicts its least recently used entry.
    let add capacity key value state =
        match state.Entries |> Map.tryFind key with
        | Some existing -> state, existing.Value
        | None ->
            let room =
                if state.Entries.Count < capacity then
                    state.Entries
                else
                    let oldest = state.Entries |> Seq.minBy _.Value.LastUse
                    state.Entries |> Map.remove oldest.Key

            let tick = state.Tick + 1L

            {
                Tick = tick
                Entries = room |> Map.add key { Value = value; LastUse = tick }
            },
            value

/// A small LRU cache. The loader runs outside any atomic section.
[<Sealed>]
type BoundedCache<'Key, 'Value when 'Key: comparison>(capacity: int) =
    do ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1)
    let state = Atom<LruState<'Key, 'Value>>(LruState.empty)

    member _.GetOrAdd(key: 'Key, load: unit -> 'Value) : 'Value =
        state.Modify(LruState.tryUse key)
        |> Option.defaultWith (fun () ->
            let value = load ()
            state.Modify(LruState.add capacity key value))
