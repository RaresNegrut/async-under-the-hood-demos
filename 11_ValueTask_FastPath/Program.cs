using System;
using System.Collections.Generic;
using System.Threading.Tasks;

internal static class Program
{
    private static readonly Dictionary<int, int> Cache = new();

    private static void Main()
    {
        Console.WriteLine("=== DEMO 11: ValueTask<T> fast path (allocation story) ===");
        Console.WriteLine("Goal: show why ValueTask<T> can remove allocations when results are already available.");
        Console.WriteLine();

        Cache[1] = 123;

        const int Iter = 200_000;

        // Warm-up
        _ = GetCachedAsTask(1).GetAwaiter().GetResult();
        _ = GetCachedAsValueTask(1).GetAwaiter().GetResult();

        long bytesTask = MeasureAllocations(() =>
        {
            int checksum = 0;
            for (int i = 0; i < Iter; i++)
                checksum += GetCachedAsTask(1).GetAwaiter().GetResult();
            Consume(checksum);
        });

        long bytesValueTask = MeasureAllocations(() =>
        {
            int checksum = 0;
            for (int i = 0; i < Iter; i++)
                checksum += GetCachedAsValueTask(1).GetAwaiter().GetResult();
            Consume(checksum);
        });

        Console.WriteLine($"Hot path (cache hit) over {Iter:N0} calls:");
        Console.WriteLine($"  Task<int>      allocated: {bytesTask:N0} bytes  (~{(double)bytesTask / Iter:F2} bytes/call)");
        Console.WriteLine($"  ValueTask<int> allocated: {bytesValueTask:N0} bytes  (~{(double)bytesValueTask / Iter:F2} bytes/call)");

        Console.WriteLine();
        Console.WriteLine("Cold path (cache miss) — ValueTask still needs a Task when it truly suspends:");
        Cache.Remove(2);
        var vt = GetCachedAsValueTask(2);
        var r = vt.AsTask().GetAwaiter().GetResult();
        Console.WriteLine($"  Miss result: {r} (ValueTask wrapped a Task on the slow path)");

        Console.WriteLine();
        Console.WriteLine("Key observation:");
        Console.WriteLine("- ValueTask<T> is about the synchronous-completion fast path (e.g., caches).");
        Console.WriteLine("- On real async suspension, it still allocates (by falling back to a Task).");
        Console.WriteLine("- Correctness rule: treat arbitrary ValueTask as 'await once' (don’t cache/await twice).");
    }

    private static Task<int> GetCachedAsTask(int key)
        => Cache.TryGetValue(key, out var v)
            ? Task.FromResult(v)                 // allocates a Task<T>
            : SlowFetchAsync(key);

    private static ValueTask<int> GetCachedAsValueTask(int key)
        => Cache.TryGetValue(key, out var v)
            ? new ValueTask<int>(v)             // no allocation
            : new ValueTask<int>(SlowFetchAsync(key)); // falls back to Task on slow path

    private static async Task<int> SlowFetchAsync(int key)
    {
        // Simulate I/O
        await Task.Delay(30).ConfigureAwait(false);
        Cache[key] = key * 10;
        return Cache[key];
    }

    private static long MeasureAllocations(Action body)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        body();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Consume(int value)
    {
        // Prevent JIT from optimizing away the loop.
        if (value == int.MinValue) Console.WriteLine(value);
    }
}
