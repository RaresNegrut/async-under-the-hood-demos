using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 03: Task = promise (not a thread) + TaskCompletionSource bridge ===");
Console.WriteLine();

Console.WriteLine("Part 1: Task.Delay vs Task.Run");
Console.WriteLine($"  Current thread: {Environment.CurrentManagedThreadId}");

var delay = Task.Delay(200); // timer-based
Console.WriteLine($"  Task.Delay created. Status={delay.Status} (no thread is 'running' this task).");

var run = Task.Run(() =>
{
    Console.WriteLine($"  Task.Run executing on thread: {Environment.CurrentManagedThreadId}");
});
await Task.WhenAll(delay, run);
Console.WriteLine($"  After await. Current thread: {Environment.CurrentManagedThreadId}");
Console.WriteLine();

Console.WriteLine("Part 2: TaskCompletionSource (TCS) turns callback-style completion into awaitable Task<T>");
var sensor = new FakeSensor();
int value = await WaitForNextReadingAsync(sensor, TimeSpan.FromMilliseconds(250));
Console.WriteLine($"  Awaited sensor reading: {value}");

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- Task is a handle/promise for a result, not a 'thread'.");
Console.WriteLine("- TCS is the producer side: you decide when the promise completes.");
Console.WriteLine("- In real life: OS callbacks / events complete TCS that backs async I/O APIs.");

static Task<int> WaitForNextReadingAsync(FakeSensor sensor, TimeSpan timeout)
{
    // RunContinuationsAsynchronously avoids running consumer continuations inline on the thread that calls SetResult.
    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

    void Handler(object? s, int reading)
    {
        sensor.Reading -= Handler;
        tcs.TrySetResult(reading);
    }

    sensor.Reading += Handler;
    sensor.StartOneShot(reading: 42, after: timeout);

    return tcs.Task;
}

sealed class FakeSensor
{
    public event EventHandler<int>? Reading;

    public void StartOneShot(int reading, TimeSpan after)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(after).ConfigureAwait(false);
            Reading?.Invoke(this, reading);
        });
    }
}
