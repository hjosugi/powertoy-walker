module internal Performance

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Walker.Core

let private window index =
    {
        Handle = int64 index
        ProcessId = index
        Title = $"Terminal session {index}: draft notes for an example project with a long window title"
        AppName = "Terminal"
        ProcessName = "terminal"
        ExecutablePath = "terminal.exe"
        Minimized = false
        OnCurrentDesktop = true
        LastActivated = int64 index
    }

let private measureWindows count =
    let windows = [| 1..count |] |> Array.map window

    let options =
        { SearchOptions.defaults with
            MatchTitlesInGlobalSearch = true
        }

    let search () =
        WindowSearch.search options GlobalQuery None "terminal draft" windows |> ignore

    let warmup = Stopwatch.StartNew()

    while warmup.Elapsed < TimeSpan.FromSeconds 1.0 do
        search ()

    let before = GC.GetAllocatedBytesForCurrentThread()

    let samples =
        Array.init 1000 (fun _ ->
            let start = Stopwatch.GetTimestamp()
            search ()
            Stopwatch.GetElapsedTime(start).TotalMilliseconds)

    let allocated =
        (GC.GetAllocatedBytesForCurrentThread() - before) / int64 samples.Length

    let sorted = Array.sort samples

    printfn
        "QUERY_BENCHMARK windows=%d samples=%d p50_ms=%.4f p95_ms=%.4f allocated_bytes_per_query=%d"
        count
        samples.Length
        sorted[500]
        sorted[950]
        allocated

let measure () =
    printfn
        "BENCHMARK %s; %O; .NET %O"
        RuntimeInformation.OSDescription
        RuntimeInformation.ProcessArchitecture
        Environment.Version

    printfn "Search only: excludes native window enumeration, icon loading and PowerToys rendering."
    [ 200; 1000 ] |> List.iter measureWindows
