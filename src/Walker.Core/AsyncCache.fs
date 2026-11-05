namespace Walker.Core

open System
open System.Threading.Channels
open System.Threading.Tasks

type internal CacheSlot<'Value> =
    {
        Value: 'Value
        LastUse: int64
        Expires: DateTimeOffset
        Pending: bool
    }

type internal CacheState<'Key, 'Value when 'Key: comparison> =
    {
        Capacity: int
        Tick: int64
        Slots: Map<'Key, CacheSlot<'Value>>
        Disposed: bool
    }

module internal CacheState =
    let empty capacity =
        {
            Capacity = capacity
            Tick = 0L
            Slots = Map.empty
            Disposed = false
        }

    /// The slots with room for a new key, or None if every slot is still loading:
    /// then a read must not create more work or grow the queue.
    let private tryMakeRoom state =
        if state.Slots.Count < state.Capacity then
            Some state.Slots
        else
            let idle =
                state.Slots |> Seq.filter (fun slot -> not slot.Value.Pending) |> Seq.toList

            match idle with
            | [] -> None
            | _ ->
                let oldest = idle |> List.minBy _.Value.LastUse
                Some(state.Slots |> Map.remove oldest.Key)

    let private tryFindOrAdmit fallback key state =
        match state.Slots |> Map.tryFind key with
        | Some slot -> Some(state.Slots, slot)
        | None ->
            tryMakeRoom state
            |> Option.map (fun slots ->
                slots,
                {
                    Value = fallback
                    LastUse = 0L
                    Expires = DateTimeOffset.MinValue
                    Pending = false
                })

    /// What a read returns, and whether the reader must queue a background load.
    let read now fallback key state =
        match
            (if state.Disposed then
                 None
             else
                 tryFindOrAdmit fallback key state)
        with
        | None -> state, (fallback, false)
        | Some(slots, slot) ->
            let tick = state.Tick + 1L
            let mustLoad = not slot.Pending && now >= slot.Expires

            let used =
                { slot with
                    LastUse = tick
                    Pending = slot.Pending || mustLoad
                }

            { state with
                Tick = tick
                Slots = slots |> Map.add key used
            },
            (slot.Value, mustLoad)

    let private settle key change state =
        { state with
            Slots = state.Slots |> Map.change key (Option.map change)
        }

    /// The queue was full. The slot stays expired, so a later read asks again.
    let notQueued key =
        settle key (fun slot -> { slot with Pending = false })

    let failed key retryAt =
        settle key (fun slot ->
            { slot with
                Pending = false
                Expires = retryAt
            })

    /// Publishes a finished load. Returns whether it counts: a disposed cache drops it.
    let loaded key value expires state =
        if state.Disposed then
            state, false
        else
            state
            |> settle key (fun slot ->
                { slot with
                    Value = value
                    Pending = false
                    Expires = expires
                }),
            true

    let dispose state =
        { state with
            Disposed = true
            Slots = Map.empty
        }

/// Nonblocking reads with bounded background work and stale-while-refresh caching.
[<Sealed>]
type AsyncCache<'Key, 'Value when 'Key: comparison>
    (load: 'Key -> 'Value, fallback: 'Value, ?capacity: int, ?workers: int, ?lifetime: TimeSpan, ?clock: TimeProvider) =
    let capacity = defaultArg capacity 256
    let workers = defaultArg workers 2
    let lifetime = defaultArg lifetime (TimeSpan.FromMinutes 1.0)
    let clock = defaultArg clock TimeProvider.System

    do
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1)
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1)

    let state = Atom<CacheState<'Key, 'Value>>(CacheState.empty capacity)
    let requests = Channel.CreateBounded<'Key>(capacity)
    let updated = Event<'Key * 'Value>()

    // A closed window or inaccessible executable must not take down the host.
    let tryLoad key =
        try
            Ok(load key)
        with error when not (error :? OutOfMemoryException) ->
            Error error

    let loadInBackground key =
        if not state.Value.Disposed then
            match tryLoad key with
            | Ok value ->
                if state.Modify(CacheState.loaded key value (clock.GetUtcNow() + lifetime)) then
                    updated.Trigger((key, value))
            | Error _ -> state.Update(CacheState.failed key (clock.GetUtcNow() + TimeSpan.FromSeconds 5.0))

    let worker () =
        task {
            let mutable running = true

            while running do
                let! available = requests.Reader.WaitToReadAsync()
                running <- available

                match requests.Reader.TryRead() with
                | true, key -> loadInBackground key
                | _ -> ()
        }

    do
        for _ in 1..workers do
            Task.Run(Func<Task>(fun () -> worker () :> Task)) |> ignore

    /// Raised on a worker after a load replaces a key's value.
    member _.Updated = updated.Publish

    member _.Get(key: 'Key) : 'Value =
        let value, mustLoad = state.Modify(CacheState.read (clock.GetUtcNow()) fallback key)

        if mustLoad && not (requests.Writer.TryWrite key) then
            state.Update(CacheState.notQueued key)

        value

    interface IDisposable with
        member _.Dispose() =
            state.Update CacheState.dispose
            requests.Writer.TryComplete() |> ignore
