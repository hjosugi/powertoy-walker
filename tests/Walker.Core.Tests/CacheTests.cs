using Walker.Core;

internal static class CacheTests
{
    internal static async Task Run(Action<string, bool> check)
    {
        var reads = 0;
        var metadata = new BoundedCache<(int Pid, long Created), string>(2);
        string Load() => $"metadata-{++reads}";
        check("unchanged process metadata is read only once",
            metadata.GetOrAdd((7, 1), Load) == metadata.GetOrAdd((7, 1), Load) && reads == 1);
        check("a reused process ID gets fresh metadata", metadata.GetOrAdd((7, 2), Load) == "metadata-2");
        metadata.GetOrAdd((7, 1), Load); // mark this as most recently used
        metadata.GetOrAdd((8, 3), Load);
        check("metadata LRU retains recently used entries", metadata.GetOrAdd((7, 1), Load) == "metadata-1");
        check("metadata cache evicts the oldest entry", metadata.GetOrAdd((7, 2), Load) == "metadata-4");

        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var ready = 0;
        using (var cache = new AsyncCache<int, string>(key =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Test loader was not released");
            return $"icon-{key}";
        }, "placeholder", capacity: 2, workers: 2))
        {
            cache.Updated += (_, _) => Interlocked.Increment(ref ready);
            // Keep the native-loader equivalent blocked while a separate caller
            // requests an icon, as a WPF rendering thread would.
            var firstRead = Task.Run(() => cache.Get(1));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                var completed = await Task.WhenAny(firstRead, Task.Delay(500));
                check("an icon read returns while its slow loader is blocked",
                    completed == firstRead && await firstRead == "placeholder");
                for (var i = 0; i < 100; i++)
                    cache.Get(1);
                cache.Get(2);
                for (var i = 3; i < 100; i++)
                    cache.Get(i);
            }
            finally { release.Set(); }
            await Eventually(() => Volatile.Read(ref ready) == 2);
            check("same-key requests are deduplicated and background work is bounded", calls == 2);
            check("finished icons replace placeholders", cache.Get(1) == "icon-1" && cache.Get(2) == "icon-2");
            cache.Get(3);
            await Eventually(() => Volatile.Read(ref ready) == 3);
            check("a full icon cache accepts new entries without clearing all cached icons", cache.Get(2) == "icon-2" && calls == 3);
        }

        var clock = new ManualClock();
        var revisions = 0;
        using (var cache = new AsyncCache<int, string>(_ => $"v{Interlocked.Increment(ref revisions)}",
            "placeholder", workers: 1, lifetime: TimeSpan.FromMinutes(1), clock: clock))
        {
            cache.Get(1);
            await Eventually(() => cache.Get(1) == "v1");
            clock.Advance(TimeSpan.FromMinutes(2));
            check("an expired icon remains visible while being refreshed", cache.Get(1) == "v1");
            await Eventually(() => cache.Get(1) == "v2");
            check("expired icons are refreshed in the background", revisions == 2);
            cache.Dispose();
            check("disposed icon caches return a safe placeholder", cache.Get(1) == "placeholder");
        }

        var attempts = 0;
        var retryClock = new ManualClock();
        using (var cache = new AsyncCache<int, string>(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new IOException("Window disappeared while its icon was loading");
            return "recovered";
        }, "placeholder", workers: 1, clock: retryClock))
        {
            cache.Get(1);
            cache.Get(2);
            // A successful second request proves the one worker has already
            // published the first request's failure and retry time.
            await Eventually(() => cache.Get(2) == "recovered");
            check("a failed icon loader keeps its fallback", cache.Get(1) == "placeholder");
            retryClock.Advance(TimeSpan.FromSeconds(6));
            await Eventually(() => cache.Get(1) == "recovered");
            check("an icon failure does not terminate the background worker", attempts == 3);
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(5, deadline.Token);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UnixEpoch.Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }
}
