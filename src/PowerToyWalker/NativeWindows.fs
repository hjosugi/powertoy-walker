/// Reads and acts on real windows. This is the imperative edge: each function
/// is a small question or command to Win32, and `capture` composes the questions.
module internal PowerToyWalker.NativeWindows

open System
open System.ComponentModel
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text
open Microsoft.Win32.SafeHandles
open Walker.Core

type private ProcessMetadata =
    {
        Name: string
        App: string
        ExecutablePath: string
    }

type private CapturedWindow =
    {
        Native: nativeint
        Pid: int
        Text: string
        IsOnCurrentDesktop: bool
    }

let private processCache = BoundedCache<int * int64, ProcessMetadata>(512)

let private shellClasses =
    set [ "Progman"; "WorkerW"; "Shell_TrayWnd"; "Shell_SecondaryTrayWnd" ]

/// The process that owns a window, or 0 for a handle that names no window.
let processIdOf (window: nativeint) =
    let mutable pid = 0u
    Win32.GetWindowThreadProcessId(window, &pid) |> ignore
    int pid

/// Whether a handle still names a window of the given process. Windows reuses the
/// handles of closed windows, so the handle alone is not enough.
let isWindowOf pid (window: nativeint) =
    Win32.IsWindow window && processIdOf window = pid

/// Whether a handle names a window of a process other than PowerToys Run.
let isOtherProcess (window: nativeint) =
    let pid = processIdOf window
    pid <> 0 && pid <> Environment.ProcessId

let private topLevelWindows () =
    let found = ResizeArray<nativeint>()

    Win32.EnumWindows(
        Win32.EnumWindowCallback(fun window _ ->
            found.Add window
            true),
        0n
    )
    |> ignore

    found.ToArray()

/// Whether a window is the kind that Alt+Tab would list.
let private isSwitchable (window: nativeint) =
    let style = Win32.GetWindowLongPtr(window, Win32.GWL_EXSTYLE).ToInt64()
    let isAppWindow = (style &&& Win32.WS_EX_APPWINDOW) <> 0L
    let isToolWindow = (style &&& Win32.WS_EX_TOOLWINDOW) <> 0L
    let owner = Win32.GetAncestor(window, Win32.GA_ROOTOWNER)
    let isOwned = owner <> window && Win32.IsWindowVisible owner
    isAppWindow || not (isToolWindow || isOwned)

let private windowTitle (window: nativeint) =
    let text = StringBuilder(min (Win32.GetWindowTextLength window) 4095 + 1)
    Win32.GetWindowText(window, text, text.Capacity) |> ignore
    text.ToString()

let private className (window: nativeint) =
    let name = StringBuilder 256
    Win32.GetClassName(window, name, name.Capacity) |> ignore
    name.ToString()

let private isOnCurrentDesktop (desktops: Win32.IVirtualDesktopManager option) (window: nativeint) =
    match desktops with
    | None -> true
    | Some manager ->
        try
            let mutable current = false

            if manager.IsWindowOnCurrentVirtualDesktop(window, &current) >= 0 then
                current
            else
                true
        with :? COMException ->
            true

/// DWM_CLOAKED_APP is hidden by the app; DWM_CLOAKED_SHELL can
/// mean a normal window on another virtual desktop.
let private isCloaked onCurrentDesktop (window: nativeint) =
    let mutable cloaked = 0

    Win32.DwmGetWindowAttribute(window, Win32.DWMWA_CLOAKED, &cloaked, sizeof<int>)
    |> ignore

    (cloaked &&& Win32.DWM_CLOAKED_APP) <> 0 || (cloaked <> 0 && onCurrentDesktop)

let private tryCapture desktops (window: nativeint) =
    if
        Win32.IsWindowVisible window
        && isOtherProcess window
        && isSwitchable window
        && not (shellClasses.Contains(className window))
    then
        let title = windowTitle window
        let onCurrentDesktop = isOnCurrentDesktop desktops window

        if String.IsNullOrWhiteSpace title || isCloaked onCurrentDesktop window then
            None
        else
            Some
                {
                    Native = window
                    Pid = processIdOf window
                    Text = title
                    IsOnCurrentDesktop = onCurrentDesktop
                }
    else
        None

let private isExpectedProcessError (error: exn) =
    match error with
    | :? ArgumentException
    | :? InvalidOperationException
    | :? Win32Exception
    | :? IOException
    | :? NotSupportedException
    | :? UnauthorizedAccessException -> true
    | _ -> false

/// None when the file can't be read, such as an elevated process's executable.
/// The process still has its executable's name to show and to search.
let private fileDescription (path: string) =
    try
        FileVersionInfo.GetVersionInfo(path).FileDescription
        |> Option.ofObj
        |> Option.filter (fun description -> not (String.IsNullOrWhiteSpace description))
    with error when isExpectedProcessError error ->
        None

let private readMetadata unknown (handle: SafeProcessHandle) =
    let buffer = StringBuilder 32768
    let mutable length = buffer.Capacity

    let path =
        if Win32.QueryFullProcessImageName(handle, 0u, buffer, &length) then
            buffer.ToString()
        else
            ""

    if String.IsNullOrEmpty path then
        unknown
    else
        let name = Path.GetFileNameWithoutExtension path |> nonNull

        {
            Name = name
            App = fileDescription path |> Option.defaultValue name
            ExecutablePath = path
        }

let private tryCreationTime (handle: SafeProcessHandle) =
    let mutable created = 0L
    let mutable exited = 0L
    let mutable kernel = 0L
    let mutable user = 0L

    if Win32.GetProcessTimes(handle, &created, &exited, &kernel, &user) then
        Some created
    else
        None

let private processMetadata pid =
    let placeholder = $"Process {pid}"

    let unknown =
        {
            Name = placeholder
            App = placeholder
            ExecutablePath = ""
        }

    try
        use handle =
            Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, uint32 pid)

        if handle.IsInvalid then
            unknown
        else
            tryCreationTime handle
            // Creation time prevents stale metadata when Windows reuses a PID.
            |> Option.map (fun created ->
                processCache.GetOrAdd((pid, created), (fun () -> readMetadata unknown handle)))
            |> Option.defaultValue unknown
    with error when isExpectedProcessError error ->
        unknown

let private withVirtualDesktops (action: Win32.IVirtualDesktopManager option -> 'Result) =
    let instance =
        try
            Type.GetTypeFromCLSID Win32.VirtualDesktopManagerClass
            |> Option.ofObj
            |> Option.bind (Activator.CreateInstance >> Option.ofObj)
        with :? COMException ->
            None

    try
        instance
        |> Option.bind (function
            | :? Win32.IVirtualDesktopManager as manager -> Some manager
            | _ -> None)
        |> action
    finally
        instance
        |> Option.filter Marshal.IsComObject
        |> Option.iter (Marshal.ReleaseComObject >> ignore)

/// The switchable windows in Z order from the top. LastActivated is not known
/// here; WindowTracking.captured fills it in.
let capture () : WindowInfo[] =
    withVirtualDesktops (fun desktops ->
        let windows = topLevelWindows () |> Array.choose (tryCapture desktops)

        let metadata =
            windows
            |> Array.map _.Pid
            |> Array.distinct
            |> Array.map (fun pid -> pid, processMetadata pid)
            |> Map.ofArray

        windows
        |> Array.map (fun window ->
            let owner = metadata[window.Pid]

            {
                Handle = int64 window.Native
                ProcessId = window.Pid
                Title = window.Text
                AppName = owner.App
                ProcessName = owner.Name
                ExecutablePath = owner.ExecutablePath
                Minimized = Win32.IsIconic window.Native
                OnCurrentDesktop = window.IsOnCurrentDesktop
                LastActivated = 0L
            }))

let private stillExists (window: WindowInfo) =
    isWindowOf window.ProcessId (nativeint window.Handle)

let activate (window: WindowInfo) =
    stillExists window
    && (let handle = nativeint window.Handle

        if Win32.IsIconic handle then
            Win32.ShowWindowAsync(handle, Win32.SW_RESTORE) |> ignore

        Win32.SetForegroundWindow handle)

/// Posts WM_CLOSE, which preserves the app's save prompts.
let close (window: WindowInfo) =
    stillExists window
    && Win32.PostMessage(nativeint window.Handle, Win32.WM_CLOSE, 0un, 0n)
