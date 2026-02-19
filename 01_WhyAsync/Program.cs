using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 01: Why async exists ===");
Console.WriteLine("Goal: show that async frees threads during waits (not parallelism).");
Console.WriteLine();

const int N = 400;
const int WaitMs = 250;

static void PrintThreadPoolSnapshot(string label)
{
    ThreadPool.GetAvailableThreads(out var workerAvail, out var ioAvail);
    ThreadPool.GetMaxThreads(out var workerMax, out var ioMax);

    Console.WriteLine($"{label}");
    Console.WriteLine($"  ThreadPool.ThreadCount      : {ThreadPool.ThreadCount}");
    Console.WriteLine($"  Worker available / max      : {workerAvail} / {workerMax}");
    Console.WriteLine($"  IO completion avail / max   : {ioAvail} / {ioMax}");
}

PrintThreadPoolSnapshot("Before:");

Console.WriteLine();
Console.WriteLine($"Scenario A (blocking): {N} tasks doing Thread.Sleep({WaitMs}) on the ThreadPool");
var sw = Stopwatch.StartNew();
var blocking = new Task[N];
for (int i = 0; i < N; i++)
{
    blocking[i] = Task.Run(() => Thread.Sleep(WaitMs));
}
await Task.WhenAll(blocking);
sw.Stop();
Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds} ms");
PrintThreadPoolSnapshot("After blocking:");

Console.WriteLine();
await Task.Delay(1000); // Give the ThreadPool some time to recover from the blocking scenario
Console.WriteLine($"Scenario B (async waiting): {N} tasks doing await Task.Delay({WaitMs}) (no threads while waiting)");
sw.Restart();
var asyncWaits = new Task[N];
for (int i = 0; i < N; i++)
{
    asyncWaits[i] = Task.Delay(WaitMs);
}
await Task.WhenAll(asyncWaits);
sw.Stop();
Console.WriteLine($"  Elapsed: {sw.ElapsedMilliseconds} ms");
PrintThreadPoolSnapshot("After async waiting:");

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- Thread.Sleep ties up worker threads while doing nothing.");
Console.WriteLine("- Task.Delay uses timers; threads are returned to the pool while waiting.");
