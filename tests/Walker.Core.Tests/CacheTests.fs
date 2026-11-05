module internal CacheTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Walker.Core

type private ManualClock() =
    inherit TimeProvider()
    let mutable ticks = DateTimeOffset.UnixEpoch.Ticks

    override _.GetUtcNow() =
        DateTimeOffset(Interlocked.Read(&ticks), TimeSpan.Zero)

    member _.Advance(duration: TimeSpan) =
        Interlocked.Add(&ticks, duration.Ticks) |> ignore

let private eventually (condition: unit -> bool) =
    task {
        use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)

        while not (condition ()) do
            do! Task.Delay(5, deadline.Token)
    }

let private boundedCacheTests (check: string -> bool -> unit) =
    let reads = ref 0
    let metadata = BoundedCache<int * int64, string>(2)

    let load () =
        reads.Value <- reads.Value + 1
        $"metadata-{reads.Value}"

    check
        "unchanged process metadata is read only once"
        (metadata.GetOrAdd((7, 1L), load) = metadata.GetOrAdd((7, 1L), load)
         && reads.Value = 1)

    check "a reused process ID gets fresh metadata" (metadata.GetOrAdd((7, 2L), load) = "metadata-2")
    metadata.GetOrAdd((7, 1L), load) |> ignore // mark this as most recently used
    metadata.GetOrAdd((8, 3L), load) |> ignore
    check "metadata LRU retains recently used entries" (metadata.GetOrAdd((7, 1L), load) = "metadata-1")
    check "metadata cache evicts the oldest entry" (metadata.GetOrAdd((7, 2L), load) = "metadata-4")

let private blockedLoaderTests (check: string -> bool -> unit) =
    task {
        use release = new ManualResetEventSlim()

        let started =
            TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

        let calls = ref 0
        let ready = ref 0

        use cache =
            new AsyncCache<int, string>(
                (fun key ->
                    Interlocked.Increment(calls) |> ignore
                    started.TrySetResult() |> ignore

                    if not (release.Wait(TimeSpan.FromSeconds 5.0)) then
                        raise (TimeoutException "Test loader was not released")

                    $"icon-{key}"),
                "placeholder",
                capacity = 2,
                workers = 2
            )

        cache.Updated.Add(fun _ -> Interlocked.Increment(ready) |> ignore)
        // Keep the native-loader equivalent blocked while a separate caller
        // requests an icon, as a WPF rendering thread would.
        let firstRead = Task.Run<string>(Func<string>(fun () -> cache.Get 1))
        do! started.Task.WaitAsync(TimeSpan.FromSeconds 5.0)

        try
            let! completed = Task.WhenAny(firstRead :> Task, Task.Delay 500)

            let! value =
                if obj.ReferenceEquals(completed, firstRead) then
                    firstRead
                else
                    Task.FromResult ""

            check "an icon read returns while its slow loader is blocked" (value = "placeholder")

            for _ in 1..100 do
                cache.Get 1 |> ignore

            cache.Get 2 |> ignore

            for key in 3..99 do
                cache.Get key |> ignore
        finally
            release.Set()

        do! eventually (fun () -> Volatile.Read(&ready.contents) = 2)
        check "same-key requests are deduplicated and background work is bounded" (calls.Value = 2)
        check "finished icons replace placeholders" (cache.Get 1 = "icon-1" && cache.Get 2 = "icon-2")
        cache.Get 3 |> ignore
        do! eventually (fun () -> Volatile.Read(&ready.contents) = 3)

        check
            "a full icon cache accepts new entries without clearing all cached icons"
            (cache.Get 2 = "icon-2" && calls.Value = 3)
    }

let private expiryTests (check: string -> bool -> unit) =
    task {
        let clock = ManualClock()
        let revisions = ref 0

        use cache =
            new AsyncCache<int, string>(
                (fun _ -> $"v{Interlocked.Increment(revisions)}"),
                "placeholder",
                workers = 1,
                lifetime = TimeSpan.FromMinutes 1.0,
                clock = clock
            )

        cache.Get 1 |> ignore
        do! eventually (fun () -> cache.Get 1 = "v1")
        clock.Advance(TimeSpan.FromMinutes 2.0)
        check "an expired icon remains visible while being refreshed" (cache.Get 1 = "v1")
        do! eventually (fun () -> cache.Get 1 = "v2")
        check "expired icons are refreshed in the background" (revisions.Value = 2)
        (cache :> IDisposable).Dispose()
        check "disposed icon caches return a safe placeholder" (cache.Get 1 = "placeholder")
    }

let private failureTests (check: string -> bool -> unit) =
    task {
        let attempts = ref 0
        let clock = ManualClock()

        use cache =
            new AsyncCache<int, string>(
                (fun _ ->
                    if Interlocked.Increment(attempts) = 1 then
                        raise (IOException "Window disappeared while its icon was loading")

                    "recovered"),
                "placeholder",
                workers = 1,
                clock = clock
            )

        cache.Get 1 |> ignore
        cache.Get 2 |> ignore
        // A successful second request proves the one worker has already
        // published the first request's failure and retry time.
        do! eventually (fun () -> cache.Get 2 = "recovered")
        check "a failed icon loader keeps its fallback" (cache.Get 1 = "placeholder")
        clock.Advance(TimeSpan.FromSeconds 6.0)
        do! eventually (fun () -> cache.Get 1 = "recovered")
        check "an icon failure does not terminate the background worker" (attempts.Value = 3)
    }

let private concurrencyTests (check: string -> bool -> unit) =
    task {
        let inParallel action =
            Task.WhenAll(Array.init 8 (fun _ -> Task.Run<unit>(Func<unit>(action))))

        let counter = Atom(ref 0)

        let! _ =
            inParallel (fun () ->
                for _ in 1..50_000 do
                    counter.Update(fun count -> ref (count.Value + 1)))

        check "an atom applies every concurrent change exactly once" (counter.Value.Value = 400_000)

        let metadata = BoundedCache<int, int>(16)

        let! _ =
            inParallel (fun () ->
                for key in 1..50_000 do
                    let wanted = key % 64

                    if metadata.GetOrAdd(wanted, (fun () -> wanted * 2)) <> wanted * 2 then
                        raise (InvalidOperationException "A concurrent reader saw another key's value"))

        check "concurrent cache readers always see their own key's value" true

        let finished = ref 0

        use icons =
            new AsyncCache<int, string>((fun key -> $"icon-{key}"), "placeholder", capacity = 8, workers = 2)

        icons.Updated.Add(fun _ -> Interlocked.Increment(finished) |> ignore)

        let! _ =
            inParallel (fun () ->
                for key in 1..50_000 do
                    let value = icons.Get(key % 32)

                    if value <> "placeholder" && value <> $"icon-{key % 32}" then
                        raise (InvalidOperationException "A concurrent read saw another key's icon"))

        do! eventually (fun () -> Volatile.Read(&finished.contents) > 0)
        check "concurrent icon reads stay correct while workers load" true
    }

let run (check: string -> bool -> unit) =
    task {
        boundedCacheTests check
        do! blockedLoaderTests check
        do! expiryTests check
        do! failureTests check
        do! concurrencyTests check
    }
