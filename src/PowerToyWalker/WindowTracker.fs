namespace PowerToyWalker

open System
open System.Threading
open Walker.Core

/// Feeds real window events into the pure WindowTracking state: a WinEvent hook
/// for foreground changes, and a background capture every 500 ms.
[<Sealed>]
type internal WindowTracker() =
    let state = Atom WindowTracking.initial
    let mutable refreshing = 0
    let mutable disposed = 0

    [<VolatileField>]
    let mutable eventThreadId = 0u

    let isDisposed () = Volatile.Read(&disposed) <> 0

    // PowerToys Run's own launcher is never a window to come back to.
    let recordForeground (window: nativeint) =
        if NativeWindows.isOtherProcess window then
            state.Update(WindowTracking.activated (int64 window))

    let callback =
        Win32.WinEventCallback(fun _ _ window _ _ _ _ -> recordForeground window)

    let watchForeground () =
        eventThreadId <- Win32.GetCurrentThreadId()
        let mutable message = Win32.Message()
        Win32.PeekMessage(&message, 0n, 0u, 0u, 0u) |> ignore // create the thread's message queue

        if not (isDisposed ()) then
            let hook =
                Win32.SetWinEventHook(
                    Win32.EVENT_SYSTEM_FOREGROUND,
                    Win32.EVENT_SYSTEM_FOREGROUND,
                    0n,
                    callback,
                    0u,
                    0u,
                    Win32.WINEVENT_SKIPOWNPROCESS
                )

            try
                while not (isDisposed ()) && Win32.GetMessage(&message, 0n, 0u, 0u) > 0 do
                    ()
            finally
                if hook <> 0n then
                    Win32.UnhookWinEvent hook |> ignore

                // Windows calls this delegate for as long as the hook lives.
                GC.KeepAlive callback

    let recordIfChanged () =
        let window = Win32.GetForegroundWindow()

        if state.Value.Previous <> Some(int64 window) then
            recordForeground window

    let refresh () =
        if not (isDisposed ()) && Interlocked.Exchange(&refreshing, 1) = 0 then
            try
                try
                    recordIfChanged ()
                    let windows = NativeWindows.capture ()
                    state.Update(WindowTracking.captured windows)
                // This runs on a timer thread, where an escaping exception would end the
                // whole PowerToys Run process. Keep the last immutable snapshot instead.
                with error when not (error :? OutOfMemoryException) ->
                    ()
            finally
                Volatile.Write(&refreshing, 0)

    do recordForeground (Win32.GetForegroundWindow())

    let eventThread =
        Thread(ThreadStart watchForeground, IsBackground = true, Name = "powertoy-walker foreground")

    do eventThread.Start()

    let timer = new Timer(TimerCallback(fun _ -> refresh ()), null, 0, 500)

    member _.Windows = state.Value.Windows
    member _.PreviousWindow = state.Value.Previous

    interface IDisposable with
        member _.Dispose() =
            if Interlocked.Exchange(&disposed, 1) = 0 then
                timer.Dispose()

                if eventThreadId <> 0u then
                    Win32.PostThreadMessage(eventThreadId, Win32.WM_QUIT, 0un, 0n) |> ignore

                eventThread.Join 1000 |> ignore
