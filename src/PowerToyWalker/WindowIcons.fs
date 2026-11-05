namespace PowerToyWalker

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Threading
open System.Windows
open System.Windows.Interop
open System.Windows.Media
open System.Windows.Media.Imaging
open System.Windows.Threading
open Walker.Core

[<Struct>]
type internal IconKey =
    {
        Window: int64
        Pid: int
        Executable: string
    }

/// Reads window icons through Win32. Runs only on the icon cache's background workers.
module internal NativeIcons =
    /// An icon handle, and whether it is ours to destroy.
    type private NativeIcon =
        | Owned of nativeint
        | Shared of nativeint

    let private nonZero (handle: nativeint) =
        if handle = 0n then None else Some handle

    let private copied = Option.bind (Win32.CopyIcon >> nonZero)

    let private queryWindowIcon (window: nativeint) (budget: Stopwatch) kind =
        let remaining = 50 - int budget.ElapsedMilliseconds

        if remaining <= 0 then
            None
        else
            let mutable icon = 0n

            let sent =
                Win32.SendMessageTimeout(
                    window,
                    Win32.WM_GETICON,
                    unativeint (kind: int),
                    nativeint (Win32.GetDpiForWindow window),
                    Win32.SMTO_ABORT_IF_GONE,
                    uint32 remaining,
                    &icon
                )

            if sent = 0n then None else nonZero icon

    let private executableIcon (path: string) =
        if String.IsNullOrEmpty path then
            None
        else
            let mutable icon = 0n
            Win32.ExtractIconEx(path, 0, &icon, 0n, 1u) |> ignore
            nonZero icon

    let private acquire (key: IconKey) =
        let window = nativeint key.Window
        let query = queryWindowIcon window (Stopwatch.StartNew())

        let classIcon index =
            nonZero (Win32.GetClassLongPtr(window, index))

        // Prefer a real large app icon; ICON_SMALL2 can return a generated
        // generic icon even when an executable has a better icon available.
        query Win32.ICON_BIG
        |> Option.orElseWith (fun () -> query Win32.ICON_SMALL)
        |> Option.orElseWith (fun () -> classIcon Win32.GCLP_HICON)
        |> Option.orElseWith (fun () -> classIcon Win32.GCLP_HICONSM)
        |> copied
        |> Option.orElseWith (fun () -> executableIcon key.Executable)
        |> Option.orElseWith (fun () -> query Win32.ICON_SMALL2 |> copied)
        |> Option.map Owned
        |> Option.defaultWith (fun () -> Shared(Win32.LoadIcon(0n, Win32.IDI_APPLICATION)))

    let private toImage icon : ImageSource option =
        let handle =
            match icon with
            | Owned handle
            | Shared handle -> handle

        try
            try
                let source =
                    Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())

                source.Freeze()
                Some source
            with
            | :? ArgumentException
            | :? Win32Exception -> None
        finally
            match icon with
            | Owned handle -> Win32.DestroyIcon handle |> ignore
            | Shared _ -> ()

    /// The icon of a window that still exists and still belongs to the same process.
    let read (key: IconKey) =
        let window = nativeint key.Window

        if Win32.IsWindow window && NativeWindows.processIdOf window = key.Pid then
            acquire key |> toImage
        else
            None

/// The live drawings PowerToys Run renders. Each one's image can be swapped
/// in place, so a finished icon appears without re-adding the search results.
module private IconDrawing =
    let create (source: ImageSource) =
        let group = DrawingGroup()
        group.Children.Add(ImageDrawing(source, Rect(0.0, 0.0, 64.0, 64.0)))
        DrawingImage group

    let setImage (source: ImageSource) (drawing: DrawingImage) =
        ((drawing.Drawing :?> DrawingGroup).Children[0] :?> ImageDrawing).ImageSource <- source

    let tryTarget (reference: WeakReference<DrawingImage>) =
        match reference.TryGetTarget() with
        | true, drawing -> Some drawing
        | _ -> None

[<Sealed>]
type internal WindowIcons() =
    let fallback: ImageSource =
        let image = BitmapImage()
        image.BeginInit()
        image.CacheOption <- BitmapCacheOption.OnLoad
        let folder = Path.GetDirectoryName(typeof<IconKey>.Assembly.Location) |> nonNull
        image.UriSource <- Uri(Path.Combine(folder, "Images", "walker.png"))
        image.EndInit()
        image.Freeze()
        image

    let cache =
        new AsyncCache<IconKey, ImageSource>(NativeIcons.read >> Option.defaultValue fallback, fallback)

    // These small live drawings belong to the rendering dispatcher. Actual
    // bitmap sources are frozen by the background workers before publishing.
    let drawings = Dictionary<IconKey, WeakReference<DrawingImage>>()
    let changed = ConcurrentQueue<IconKey * ImageSource>()
    let mutable updatePending = 0
    let mutable disposed = 0

    let isDisposed () = Volatile.Read(&disposed) <> 0

    let uiDispatcher () =
        Application.Current |> Option.ofObj |> Option.map _.Dispatcher

    let tryDrawing key =
        match drawings.TryGetValue key with
        | true, reference -> IconDrawing.tryTarget reference
        | _ -> None

    let makeRoomForDrawing () =
        if drawings.Count = 256 then
            drawings
            |> Seq.filter (fun pair -> (IconDrawing.tryTarget pair.Value).IsNone)
            |> Seq.map _.Key
            |> Seq.toArray
            |> Array.iter (drawings.Remove >> ignore)

            if drawings.Count = 256 then
                drawings.Remove(Seq.head drawings.Keys) |> ignore

    let rec applyChanges () =
        match changed.TryDequeue() with
        | true, (key, source) ->
            tryDrawing key |> Option.iter (IconDrawing.setImage source)
            applyChanges ()
        | _ -> ()

    let iconReady (key, source) =
        match uiDispatcher () with
        | Some dispatcher when not (isDisposed () || dispatcher.HasShutdownStarted) ->
            changed.Enqueue((key, source))

            if Interlocked.CompareExchange(&updatePending, 1, 0) = 0 then
                dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    Action(fun () ->
                        Interlocked.Exchange(&updatePending, 0) |> ignore

                        if not (isDisposed ()) then
                            applyChanges ())
                )
                |> ignore
        | _ -> ()

    let subscription = cache.Updated |> Observable.subscribe iconReady

    /// Called while rendering a result: only a cache lookup and queue insertion.
    /// Native messages, executable reads and image decoding happen in the workers.
    member _.Get(window: WindowInfo) : ImageSource =
        let key =
            {
                Window = window.Handle
                Pid = window.ProcessId
                Executable = window.ExecutablePath
            }

        let source = cache.Get key

        match uiDispatcher () with
        | Some dispatcher when not (isDisposed ()) && dispatcher.CheckAccess() ->
            match tryDrawing key with
            | Some existing ->
                IconDrawing.setImage source existing
                existing
            | None ->
                makeRoomForDrawing ()
                let drawing = IconDrawing.create source
                drawings[key] <- WeakReference<DrawingImage> drawing
                drawing
        | _ -> source

    interface IDisposable with
        member _.Dispose() =
            Interlocked.Exchange(&disposed, 1) |> ignore
            subscription.Dispose()
            (cache :> IDisposable).Dispose()
