using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Walker.Core;

namespace PowerToyWalker;

internal static class NativeWindows
{
    private static readonly BoundedCache<(int Pid, long Created), (string Name, string App, string Path)> processCache = new(512);
    internal delegate bool EnumWindowCallback(nint window, nint parameter);
    internal delegate void WinEventCallback(nint hook, uint eventId, nint window,
        int objectId, int childId, uint eventThread, uint eventTime);

    internal static WindowInfo[] Capture(Func<nint, long> recency)
    {
        var windows = new List<WindowInfo>();
        var processes = new Dictionary<int, (string Name, string App, string Path)>();
        object? desktopObject = null;
        IVirtualDesktopManager? desktops = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new("AA509086-5CA9-4C25-8F95-589D3C07B48A"));
            if (type is not null)
            {
                desktopObject = Activator.CreateInstance(type);
                desktops = desktopObject as IVirtualDesktopManager;
            }
        }
        catch (COMException) { }

        try
        {
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window))
                    return true;
                GetWindowThreadProcessId(window, out var processId);
                if (processId == 0 || processId == Environment.ProcessId)
                    return true;
                var style = GetWindowLongPtr(window, -20).ToInt64();
                if ((style & 0x80) != 0 && (style & 0x40000) == 0) // tool/app windows
                    return true;
                var owner = GetAncestor(window, 3);
                if (owner != window && IsWindowVisible(owner) && (style & 0x40000) == 0)
                    return true;

                var title = new StringBuilder(Math.Min(GetWindowTextLength(window), 4095) + 1);
                GetWindowText(window, title, title.Capacity);
                if (string.IsNullOrWhiteSpace(title.ToString()))
                    return true;
                var className = new StringBuilder(256);
                GetClassName(window, className, className.Capacity);
                if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                    return true;

                var onCurrentDesktop = true;
                try
                {
                    if (desktops is not null && desktops.IsWindowOnCurrentVirtualDesktop(window, out var current) >= 0)
                        onCurrentDesktop = current;
                }
                catch (COMException) { }
                DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int));
                // DWM_CLOAKED_APP is hidden by the app; DWM_CLOAKED_SHELL can
                // mean a normal window on another virtual desktop.
                if ((cloaked & 1) != 0 || (cloaked != 0 && onCurrentDesktop))
                    return true;

                var pid = unchecked((int)processId);
                if (!processes.TryGetValue(pid, out var process))
                {
                    process = ProcessInfo(pid);
                    processes.Add(pid, process);
                }
                windows.Add(new(window.ToInt64(), pid, title.ToString(), process.App,
                    process.Name, process.Path, IsIconic(window), onCurrentDesktop, recency(window)));
                return true;
            }, 0);
        }
        finally
        {
            if (desktopObject is not null && Marshal.IsComObject(desktopObject))
                Marshal.ReleaseComObject(desktopObject);
        }
        return windows.ToArray();
    }

    private static (string Name, string App, string Path) ProcessInfo(int pid)
    {
        var name = $"Process {pid}";
        try
        {
            using var process = OpenProcess(0x1000, false, (uint)pid); // limited query information
            if (process.IsInvalid || !GetProcessTimes(process, out var created, out _, out _, out _))
                return (name, name, "");
            // Creation time prevents stale metadata when Windows reuses a PID.
            return processCache.GetOrAdd((pid, created), () =>
            {
                var pathBuffer = new StringBuilder(32768);
                var length = pathBuffer.Capacity;
                var path = QueryFullProcessImageName(process, 0, pathBuffer, ref length) ? pathBuffer.ToString() : "";
                if (!string.IsNullOrEmpty(path))
                    name = System.IO.Path.GetFileNameWithoutExtension(path);
                var description = string.IsNullOrEmpty(path) ? null : FileVersionInfo.GetVersionInfo(path).FileDescription;
                return (name, string.IsNullOrWhiteSpace(description) ? name : description, path);
            });
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.IO.IOException or NotSupportedException
            or UnauthorizedAccessException)
        {
            return (name, name, "");
        }
    }

    private static bool StillExists(WindowInfo window)
    {
        if (!IsWindow((nint)window.Handle))
            return false;
        GetWindowThreadProcessId((nint)window.Handle, out var pid);
        return pid == window.ProcessId;
    }

    internal static bool Activate(WindowInfo window)
    {
        if (!StillExists(window))
            return false;
        var handle = (nint)window.Handle;
        if (IsIconic(handle))
            ShowWindowAsync(handle, 9); // SW_RESTORE
        return SetForegroundWindow(handle);
    }

    internal static bool Close(WindowInfo window) => StillExists(window)
        && PostMessage((nint)window.Handle, 0x0010, 0, 0); // WM_CLOSE, preserving save prompts

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint window, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint window, in Guid desktop);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flag);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int length);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
}
