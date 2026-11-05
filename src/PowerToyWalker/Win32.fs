/// Raw Win32 and COM declarations, and the constants that go with them.
module internal PowerToyWalker.Win32

// These keep their names from the Windows SDK, so that they can be looked up there.
// fsharplint:disable LiteralNames PublicValuesNames SynchronousFunctionNames

open System
open System.Runtime.InteropServices
open System.Text
open Microsoft.Win32.SafeHandles

[<Literal>]
let GWL_EXSTYLE = -20

[<Literal>]
let WS_EX_TOOLWINDOW = 0x80L

[<Literal>]
let WS_EX_APPWINDOW = 0x40000L

[<Literal>]
let GA_ROOTOWNER = 3u

/// DWMWA_CLOAKED
[<Literal>]
let DWMWA_CLOAKED = 14u

/// The window was hidden by its own app.
[<Literal>]
let DWM_CLOAKED_APP = 1

[<Literal>]
let SW_RESTORE = 9

[<Literal>]
let WM_CLOSE = 0x0010u

[<Literal>]
let WM_QUIT = 0x0012u

[<Literal>]
let WM_GETICON = 0x007Fu

[<Literal>]
let ICON_SMALL = 0

[<Literal>]
let ICON_BIG = 1

[<Literal>]
let ICON_SMALL2 = 2

[<Literal>]
let GCLP_HICON = -14

[<Literal>]
let GCLP_HICONSM = -34

let IDI_APPLICATION = 32512n

/// SMTO_ABORTIFHUNG ||| SMTO_ERRORONEXIT: give up if the window is hung or destroyed.
[<Literal>]
let SMTO_ABORT_IF_GONE = 0x0022u

[<Literal>]
let PROCESS_QUERY_LIMITED_INFORMATION = 0x1000u

[<Literal>]
let EVENT_SYSTEM_FOREGROUND = 3u

[<Literal>]
let WINEVENT_SKIPOWNPROCESS = 2u

type EnumWindowCallback = delegate of window: nativeint * parameter: nativeint -> bool

type WinEventCallback =
    delegate of
        hook: nativeint *
        eventId: uint32 *
        window: nativeint *
        objectId: int *
        childId: int *
        eventThread: uint32 *
        eventTime: uint32 ->
            unit

[<Struct; StructLayout(LayoutKind.Sequential)>]
type Message =
    val mutable Window: nativeint
    val mutable Id: uint32
    val mutable WParam: unativeint
    val mutable LParam: nativeint
    val mutable Time: uint32
    val mutable X: int
    val mutable Y: int
    val mutable Private: uint32

[<ComImport; Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"); InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>]
type IVirtualDesktopManager =
    [<PreserveSig>]
    abstract IsWindowOnCurrentVirtualDesktop:
        window: nativeint * [<Out; MarshalAs(UnmanagedType.Bool)>] current: byref<bool> -> int

    [<PreserveSig>]
    abstract GetWindowDesktopId: window: nativeint * [<Out>] desktop: byref<Guid> -> int

    [<PreserveSig>]
    abstract MoveWindowToDesktop: window: nativeint * [<In>] desktop: inref<Guid> -> int

/// CLSID_VirtualDesktopManager
let VirtualDesktopManagerClass = Guid "AA509086-5CA9-4C25-8F95-589D3C07B48A"

[<DllImport("user32.dll")>]
extern bool EnumWindows(EnumWindowCallback callback, nativeint parameter)

[<DllImport("user32.dll")>]
extern bool IsWindow(nativeint window)

[<DllImport("user32.dll")>]
extern bool IsWindowVisible(nativeint window)

[<DllImport("user32.dll")>]
extern uint32 GetWindowThreadProcessId(nativeint window, [<Out>] uint32& processId)

[<DllImport("user32.dll")>]
extern nativeint GetAncestor(nativeint window, uint32 flag)

[<DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")>]
extern nativeint GetWindowLongPtr(nativeint window, int index)

[<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
extern int GetWindowTextLength(nativeint window)

[<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
extern int GetWindowText(nativeint window, StringBuilder text, int length)

[<DllImport("user32.dll", CharSet = CharSet.Unicode)>]
extern int GetClassName(nativeint window, StringBuilder name, int length)

[<DllImport("user32.dll")>]
extern bool IsIconic(nativeint window)

[<DllImport("user32.dll")>]
extern bool ShowWindowAsync(nativeint window, int command)

[<DllImport("user32.dll")>]
extern bool SetForegroundWindow(nativeint window)

[<DllImport("user32.dll")>]
extern bool PostMessage(nativeint window, uint32 message, unativeint wparam, nativeint lparam)

[<DllImport("dwmapi.dll")>]
extern int DwmGetWindowAttribute(nativeint window, uint32 attribute, [<Out>] int& value, int size)

[<DllImport("user32.dll")>]
extern nativeint GetForegroundWindow()

[<DllImport("kernel32.dll")>]
extern uint32 GetCurrentThreadId()

[<DllImport("kernel32.dll")>]
extern SafeProcessHandle OpenProcess(uint32 access, bool inheritHandle, uint32 processId)

[<DllImport("kernel32.dll")>]
extern bool GetProcessTimes(
    SafeProcessHandle handle,
    [<Out>] int64& created,
    [<Out>] int64& exited,
    [<Out>] int64& kernel,
    [<Out>] int64& user
)

[<DllImport("kernel32.dll", CharSet = CharSet.Unicode)>]
extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint32 flags, StringBuilder name, int& length)

[<DllImport("user32.dll")>]
extern nativeint SetWinEventHook(
    uint32 first,
    uint32 last,
    nativeint moduleHandle,
    WinEventCallback callback,
    uint32 processId,
    uint32 threadId,
    uint32 flags
)

[<DllImport("user32.dll")>]
extern bool UnhookWinEvent(nativeint hook)

[<DllImport("user32.dll")>]
extern int GetMessage([<Out>] Message& message, nativeint window, uint32 first, uint32 last)

[<DllImport("user32.dll")>]
extern bool PeekMessage([<Out>] Message& message, nativeint window, uint32 first, uint32 last, uint32 remove)

[<DllImport("user32.dll")>]
extern bool PostThreadMessage(uint32 threadId, uint32 message, unativeint wparam, nativeint lparam)

[<DllImport("user32.dll")>]
extern nativeint SendMessageTimeout(
    nativeint window,
    uint32 message,
    unativeint wparam,
    nativeint lparam,
    uint32 flags,
    uint32 timeout,
    [<Out>] nativeint& result
)

[<DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")>]
extern nativeint GetClassLongPtr(nativeint window, int index)

[<DllImport("user32.dll")>]
extern nativeint CopyIcon(nativeint icon)

[<DllImport("user32.dll")>]
extern bool DestroyIcon(nativeint icon)

[<DllImport("user32.dll", EntryPoint = "LoadIconW")>]
extern nativeint LoadIcon(nativeint instance, nativeint resource)

[<DllImport("user32.dll")>]
extern uint32 GetDpiForWindow(nativeint window)

[<DllImport("shell32.dll", CharSet = CharSet.Unicode)>]
extern uint32 ExtractIconEx(string file, int index, [<Out>] nativeint& large, nativeint small, uint32 count)
