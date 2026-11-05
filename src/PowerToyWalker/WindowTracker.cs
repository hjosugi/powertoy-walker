using System.Collections.Concurrent;
using Walker.Core;

namespace PowerToyWalker;

internal sealed class WindowTracker : IDisposable
{
    private readonly ConcurrentDictionary<nint, long> recency = new();
    private WindowInfo[] windows = [];
    private long sequence;
    private long previousWindow;
    private int refreshing;
    private int disposed;
    private volatile uint eventThreadId;
    private readonly Thread eventThread;
    private readonly Timer timer;
    private readonly NativeWindows.WinEventCallback callback;

    internal IReadOnlyList<WindowInfo> Windows => Volatile.Read(ref windows);
    internal long PreviousWindow => Interlocked.Read(ref previousWindow);

    internal WindowTracker()
    {
        callback = (_, _, window, _, _, _, _) => RecordForeground(window);
        RecordForeground(NativeWindows.GetForegroundWindow());
        eventThread = new Thread(WatchForeground) { IsBackground = true, Name = "powertoy-walker foreground" };
        eventThread.Start();
        timer = new Timer(_ => Refresh(), null, 0, 500);
    }

    private void RecordForeground(nint window)
    {
        if (window == 0 || !NativeWindows.IsWindow(window))
            return;
        NativeWindows.GetWindowThreadProcessId(window, out var pid);
        if (pid == Environment.ProcessId)
            return;
        recency[window] = Interlocked.Increment(ref sequence);
        Interlocked.Exchange(ref previousWindow, window.ToInt64());
    }

    private void WatchForeground()
    {
        eventThreadId = NativeWindows.GetCurrentThreadId();
        NativeWindows.PeekMessage(out _, 0, 0, 0, 0); // create the thread's message queue
        if (Volatile.Read(ref disposed) != 0)
            return;
        var hook = NativeWindows.SetWinEventHook(3, 3, 0, callback, 0, 0, 2); // foreground, skip own process
        try
        {
            while (Volatile.Read(ref disposed) == 0 && NativeWindows.GetMessage(out _, 0, 0, 0) > 0) { }
        }
        finally
        {
            if (hook != 0)
                NativeWindows.UnhookWinEvent(hook);
        }
    }

    private void Refresh()
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref refreshing, 1) != 0)
            return;
        try
        {
            RecordIfChanged();
            var zOrder = 0L;
            var next = NativeWindows.Capture(handle => recency.GetValueOrDefault(handle, --zOrder));
            Volatile.Write(ref windows, next);
            var visible = next.Select(window => (nint)window.Handle).ToHashSet();
            foreach (var key in recency.Keys)
                if (!visible.Contains(key))
                    recency.TryRemove(key, out _);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A transient desktop/process change must not terminate the host.
            // Keep the last immutable snapshot until the next refresh.
        }
        finally { Volatile.Write(ref refreshing, 0); }
    }

    private void RecordIfChanged()
    {
        var window = NativeWindows.GetForegroundWindow();
        if (window.ToInt64() != PreviousWindow)
            RecordForeground(window);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        timer.Dispose();
        if (eventThreadId != 0)
            NativeWindows.PostThreadMessage(eventThreadId, 0x0012, 0, 0); // WM_QUIT
        eventThread.Join(1000);
    }
}
