using System.Diagnostics;
using System.Runtime.InteropServices;
using Walker.Core;

internal static class Performance
{
    internal static void Measure()
    {
        Console.WriteLine($"BENCHMARK {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; .NET {Environment.Version}");
        Console.WriteLine("Search only: excludes native window enumeration, icon loading and PowerToys rendering.");
        foreach (var count in new[] { 200, 1000 })
        {
            var windows = Enumerable.Range(1, count).Select(index => new WindowInfo(index, index,
                $"Terminal session {index}: draft notes for an example project with a long window title", "Terminal",
                "terminal", "terminal.exe", false, true, index)).ToArray();
            var options = new SearchOptions(MatchTitlesInGlobalSearch: true);
            var warmup = Stopwatch.StartNew();
            while (warmup.Elapsed < TimeSpan.FromSeconds(1))
                WindowSearch.Search(windows, "terminal draft", false, options);
            var samples = new double[1000];
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < samples.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                WindowSearch.Search(windows, "terminal draft", false, options);
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / samples.Length;
            Array.Sort(samples);
            Console.WriteLine($"QUERY_BENCHMARK windows={count} samples={samples.Length} p50_ms={samples[500]:F4} p95_ms={samples[950]:F4} allocated_bytes_per_query={allocated}");
        }
    }
}
