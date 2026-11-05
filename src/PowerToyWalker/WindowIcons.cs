using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Walker.Core;

namespace PowerToyWalker;

internal sealed class WindowIcons : IDisposable
{
    private readonly record struct IconKey(long Handle, int ProcessId, string ExecutablePath);
    private readonly AsyncCache<IconKey, ImageSource> cache;
    private readonly ImageSource fallback;
    // These small live drawings belong to the rendering dispatcher. Actual
    // bitmap sources are frozen by the background workers before publishing.
    private readonly Dictionary<IconKey, WeakReference<DrawingImage>> drawings = new();
    private readonly ConcurrentQueue<(IconKey Key, ImageSource Source)> changed = new();
    private int updatePending;
    private int disposed;

    internal WindowIcons()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(Main).Assembly.Location)!, "Images", "walker.png"));
        image.EndInit();
        image.Freeze();
        fallback = image;
        cache = new AsyncCache<IconKey, ImageSource>(ReadIcon, fallback);
        cache.Updated += IconReady;
    }

    // Called while rendering a result: only a cache lookup and queue insertion.
    // Native messages, executable reads and image decoding happen in the workers.
    internal ImageSource Get(WindowInfo window)
    {
        var key = new IconKey(window.Handle, window.ProcessId, window.ExecutablePath);
        var source = cache.Get(key);
        var dispatcher = Application.Current?.Dispatcher;
        if (Volatile.Read(ref disposed) != 0 || dispatcher is null || !dispatcher.CheckAccess())
            return source;
        if (drawings.TryGetValue(key, out var reference) && reference.TryGetTarget(out var existing))
        {
            ((ImageDrawing)((DrawingGroup)existing.Drawing).Children[0]).ImageSource = source;
            return existing;
        }
        if (drawings.Count == 256)
        {
            foreach (var dead in drawings.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray())
                drawings.Remove(dead);
            if (drawings.Count == 256)
                drawings.Remove(drawings.Keys.First());
        }
        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(source, new Rect(0, 0, 64, 64)));
        var drawing = new DrawingImage(group);
        drawings[key] = new(drawing);
        return drawing;
    }

    private void IconReady(IconKey key, ImageSource source)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (Volatile.Read(ref disposed) != 0 || dispatcher is null || dispatcher.HasShutdownStarted)
            return;
        changed.Enqueue((key, source));
        if (Interlocked.CompareExchange(ref updatePending, 1, 0) != 0)
            return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref updatePending, 0);
            if (Volatile.Read(ref disposed) != 0)
                return;
            while (changed.TryDequeue(out var updated))
                if (drawings.TryGetValue(updated.Key, out var reference) && reference.TryGetTarget(out var drawing))
                    ((ImageDrawing)((DrawingGroup)drawing.Drawing).Children[0]).ImageSource = updated.Source;
        }));
    }

    private ImageSource ReadIcon(IconKey window)
    {
        var handle = (nint)window.Handle;
        NativeWindows.GetWindowThreadProcessId(handle, out var pid);
        if (!NativeWindows.IsWindow(handle) || pid != window.ProcessId)
            return fallback;
        nint icon = 0;
        var budget = Stopwatch.StartNew();
        // Prefer a real large app icon; ICON_SMALL2 can return a generated
        // generic icon even when an executable has a better icon available.
        foreach (var kind in new[] { 1, 0 }) // ICON_BIG, ICON_SMALL
        {
            icon = QueryWindowIcon(handle, kind, budget);
            if (icon != 0)
                break;
        }
        if (icon == 0)
            icon = GetClassLongPtr(handle, -14); // GCLP_HICON
        if (icon == 0)
            icon = GetClassLongPtr(handle, -34); // GCLP_HICONSM
        var owned = false;
        if (icon != 0)
        {
            icon = CopyIcon(icon);
            owned = icon != 0;
        }
        if (icon == 0 && !string.IsNullOrEmpty(window.ExecutablePath))
        {
            ExtractIconEx(window.ExecutablePath, 0, out icon, 0, 1);
            owned = icon != 0;
        }
        if (icon == 0)
        {
            var borrowed = QueryWindowIcon(handle, 2, budget); // generated icon as a final window fallback
            icon = borrowed == 0 ? 0 : CopyIcon(borrowed);
            owned = icon != 0;
        }
        if (icon == 0)
            icon = LoadIcon(0, (nint)32512); // system application icon, shared
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception error) when (error is ArgumentException or System.ComponentModel.Win32Exception)
        {
            return fallback;
        }
        finally
        {
            if (owned)
                DestroyIcon(icon);
        }
    }

    private static nint QueryWindowIcon(nint window, int kind, Stopwatch budget)
    {
        var remaining = 50 - (int)budget.ElapsedMilliseconds;
        if (remaining <= 0)
            return 0;
        var result = SendMessageTimeout(window, 0x007F, (nuint)kind, (nint)GetDpiForWindow(window),
            0x0022, (uint)remaining, out var icon); // abort if hung or destroyed
        return result == 0 ? 0 : icon;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
        cache.Updated -= IconReady;
        cache.Dispose();
    }

    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint window, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIcon(nint instance, nint resource);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out nint large, nint small, uint count);
}
