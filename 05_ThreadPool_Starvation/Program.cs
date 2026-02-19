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
var bad = new Task[blockers];
for (int i = 0; i < blockers; i++)
{
    bad[i] = Task.Run(() => Task.Delay(BlockMs).Wait()); // blocks a worker thread
}

await Task.Delay(100); // let the pool get busy
Snapshot("After starting blockers:");

var latencyBad = await MeasureSchedulingLatencyAsync();
Console.WriteLine($"  Scheduling latency for Task.Run() while blocked: {latencyBad} ms");

await Task.WhenAll(bad);
Snapshot("After blockers finish:");

Console.WriteLine();
Console.WriteLine($"Scenario B (GOOD): start {blockers} timers with Task.Delay({BlockMs}) (no blocking threads)");
var latencyBefore = await MeasureSchedulingLatencyAsync();

var good = new Task[blockers];
for (int i = 0; i < blockers; i++)
    good[i] = Task.Delay(BlockMs);

await Task.Delay(100);
var latencyGood = await MeasureSchedulingLatencyAsync();

await Task.WhenAll(good);

Console.WriteLine($"  Scheduling latency before: {latencyBefore} ms");
Console.WriteLine($"  Scheduling latency during: {latencyGood} ms");

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- Blocking a ThreadPool thread on async work consumes a worker for no reason.");
Console.WriteLine("- Under enough blocking, *everything* queued to the pool gets delayed.");
