using System.Threading.Channels;

namespace Walker.Core;

/// <summary>Nonblocking reads with bounded background work and stale-while-refresh caching.</summary>
public sealed class AsyncCache<TKey, TValue> : IDisposable where TKey : notnull
{
    private sealed class Entry(TKey key, TValue value)
    {
        internal readonly TKey Key = key;
        internal TValue Value = value;
        internal long LastUse;
        internal DateTimeOffset Expires;
        internal bool Pending;
    }

    private readonly object gate = new();
    private readonly Dictionary<TKey, Entry> entries = new();
    private readonly Channel<Entry> requests;
    private readonly Func<TKey, TValue> load;
    private readonly TValue fallback;
    private readonly int capacity;
    private readonly TimeSpan lifetime;
    private readonly TimeProvider clock;
    private long sequence;
    private bool disposed;
    public event Action<TKey, TValue>? Updated;

    public AsyncCache(Func<TKey, TValue> load, TValue fallback, int capacity = 256,
        int workers = 2, TimeSpan? lifetime = null, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        this.load = load;
        this.fallback = fallback;
        this.capacity = capacity;
        this.lifetime = lifetime ?? TimeSpan.FromMinutes(1);
        this.clock = clock ?? TimeProvider.System;
        requests = Channel.CreateBounded<Entry>(capacity);
        for (var i = 0; i < workers; i++)
            _ = Task.Run(ProcessRequests);
    }

    public TValue Get(TKey key)
    {
        lock (gate)
        {
            if (disposed)
                return fallback;
            if (!entries.TryGetValue(key, out var entry))
            {
                if (entries.Count == capacity)
                {
                    var oldest = entries.Values.Where(item => !item.Pending)
                        .MinBy(item => item.LastUse);
                    // If all slots are loading, don't create more tasks or grow the queue.
                    if (oldest is null)
                        return fallback;
                    entries.Remove(oldest.Key);
                }
                entry = new(key, fallback);
                entries.Add(key, entry);
            }
            entry.LastUse = ++sequence;
            if (!entry.Pending && clock.GetUtcNow() >= entry.Expires)
            {
                entry.Pending = true;
                if (!requests.Writer.TryWrite(entry))
                    entry.Pending = false;
            }
            return entry.Value;
        }
    }

    private async Task ProcessRequests()
    {
        await foreach (var entry in requests.Reader.ReadAllAsync())
        {
            lock (gate)
                if (disposed)
                    return;
            TValue value;
            try { value = load(entry.Key); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // A closed window or inaccessible executable must not take down the host.
                lock (gate)
                {
                    entry.Pending = false;
                    entry.Expires = clock.GetUtcNow() + TimeSpan.FromSeconds(5);
                }
                continue;
            }
            lock (gate)
            {
                if (disposed)
                    return;
                entry.Value = value;
                entry.Pending = false;
                entry.Expires = clock.GetUtcNow() + lifetime;
            }
            Updated?.Invoke(entry.Key, value);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            entries.Clear();
            requests.Writer.TryComplete();
        }
    }
}
