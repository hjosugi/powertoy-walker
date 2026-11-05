namespace Walker.Core;

/// <summary>A small LRU cache. The loader runs outside the cache lock.</summary>
public sealed class BoundedCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> entries = new();
    private readonly LinkedList<(TKey Key, TValue Value)> order = new();

    public TValue GetOrAdd(TKey key, Func<TValue> load)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existing))
            {
                order.Remove(existing);
                order.AddFirst(existing);
                return existing.Value.Value;
            }
        }
        var value = load();
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existing))
                return existing.Value.Value;
            if (entries.Count == capacity)
            {
                entries.Remove(order.Last!.Value.Key);
                order.RemoveLast();
            }
            entries.Add(key, order.AddFirst((key, value)));
        }
        return value;
    }
}
