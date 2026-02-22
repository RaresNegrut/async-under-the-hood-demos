using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 05: ThreadPool starvation (blocking on async) ===");
Console.WriteLine("Goal: show why .Result/.Wait() under load can crush throughput.");
Console.WriteLine();

static void Snapshot(string label)
{
    ThreadPool.GetAvailableThreads(out var wAvail, out var ioAvail);
    ThreadPool.GetMaxThreads(out var wMax, out var ioMax);
    Console.WriteLine($"{label}");
    Console.WriteLine($"  ThreadPool.ThreadCount : {ThreadPool.ThreadCount}");
    Console.WriteLine($"  Worker avail/max       : {wAvail}/{wMax}");
    Console.WriteLine($"  IO avail/max           : {ioAvail}/{ioMax}");
}

static async Task<long> MeasureSchedulingLatencyAsync()
{
    var sw = Stopwatch.StartNew();
    await Task.Run(() => { /* needs a worker thread */ });
    sw.Stop();
    return sw.ElapsedMilliseconds;
}

Snapshot("Initial:");

int blockers = Math.Max(50, Environment.ProcessorCount * 40);
const int BlockMs = 1500;

Console.WriteLine();
Console.WriteLine($"Scenario A (BAD): start {blockers} ThreadPool work items that do Task.Delay({BlockMs}).Wait()");
var swA = Stopwatch.StartNew();
var bad = new Task[blockers];
for (int i = 0; i < blockers; i++)
{
    bad[i] = Task.Run(() => Task.Delay(BlockMs).Wait()); // blocks a worker thread
}

await Task.Delay(800); // let the pool saturate
Snapshot("While blockers running:");

var latencyBad = await MeasureSchedulingLatencyAsync();
Console.WriteLine($"  Scheduling latency for a trivial Task.Run(): {latencyBad} ms");

await Task.WhenAll(bad);
swA.Stop();
Console.WriteLine($"  Total wall-clock time for {blockers} blocked waits: {swA.ElapsedMilliseconds} ms");
Snapshot("After blockers finish:");

Console.WriteLine();
Console.WriteLine($"Scenario B (GOOD): start {blockers} async waits with Task.Delay({BlockMs}) (no blocked threads)");
var swB = Stopwatch.StartNew();

var good = new Task[blockers];
for (int i = 0; i < blockers; i++)
    good[i] = Task.Delay(BlockMs);

await Task.Delay(800); // same wait for fairness
var latencyGood = await MeasureSchedulingLatencyAsync();

await Task.WhenAll(good);
swB.Stop();

Console.WriteLine($"  Scheduling latency for a trivial Task.Run(): {latencyGood} ms");
Console.WriteLine($"  Total wall-clock time for {blockers} async waits: {swB.ElapsedMilliseconds} ms");

Console.WriteLine();
Console.WriteLine($"  ┌─────────────────────────────────────────────────────────┐");
Console.WriteLine($"  │  Blocked (.Wait):  {swA.ElapsedMilliseconds,6} ms   Latency: {latencyBad,4} ms  │");
Console.WriteLine($"  │  Async   (await):  {swB.ElapsedMilliseconds,6} ms   Latency: {latencyGood,4} ms  │");
Console.WriteLine($"  └─────────────────────────────────────────────────────────┘");

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- Blocking ThreadPool threads with .Wait()/.Result starves the pool.");
Console.WriteLine("- The pool injects new threads slowly (~1 per 500ms), so recovery is glacial.");
Console.WriteLine("- Compare wall-clock times: blocking serializes work; async lets everything overlap.");
