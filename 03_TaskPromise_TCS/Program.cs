using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("Task == promise; Task != Thread; TaskCompletionSource Proof");
Console.WriteLine();

Console.WriteLine("Part 1: Tasks using no Threads. Tasks that use Threads");
Console.WriteLine($"  Current thread: {Environment.CurrentManagedThreadId}");

var delay = Task.Delay(200); // timer-based
Console.WriteLine($"  Task.Delay created. Status={delay.Status} (no thread is 'running' this task).");

Console.WriteLine("Still no threads involved; TCS lets dev arbitrarily complete the task");
var tcs = new TaskCompletionSource<int>();
tcs.SetResult(42);  // Now tcs.Task is complete, no thread is 'running' it, but it has a result ready.
await tcs.Task;     // Returns 42

var run = Task.Run(() =>
{
    Console.WriteLine($"  Task.Run executing on thread: {Environment.CurrentManagedThreadId}");
});
await Task.WhenAll(delay, run);
Console.WriteLine($"  After await. Current thread: {Environment.CurrentManagedThreadId}");
Console.WriteLine();

Console.WriteLine("Part 2: More about TaskCompletionSource");

var mres = new ManualResetEventSlim(false);
Task FaultyStartNewDelayed(int millisecondsDelay, Action action)
{
    var t = new Task(action);

    // Start a timer that will trigger it
    var timer = new Timer(
        _ => { t.Start(); mres.Set(); }, null, millisecondsDelay, Timeout.Infinite);
    t.ContinueWith(_ => timer.Dispose());
    return t;
}

var faultyTask = FaultyStartNewDelayed(500, () => Console.WriteLine("How may this be faulty? "));
try
{
    mres.Wait();
    faultyTask.Start();
}
catch (Exception ex)
{
    Console.WriteLine($"Uh oh, that's how: {ex.GetType().Name} - {ex.Message}");
} // Consumer is not supposed to control completion of Task
// Code like the above is avoidable by opting into using TaskCompletionSource

mres.Reset();
Task StartNewDelayed(int millisecondsDelay, Action action)
{
    var tcs = new TaskCompletionSource<object>();

    var timer = new Timer(
        _ => { tcs.SetResult(null!); mres.Set(); }, null, millisecondsDelay, Timeout.Infinite);

    return tcs.Task.ContinueWith(_ =>
    {
        timer.Dispose();
        action();
    });
}

var task = StartNewDelayed(500, () => Console.WriteLine("This is better!"));
mres.Wait();
try
{
    task.Start();
}
catch (Exception ex)
{
    Console.WriteLine($"Still fails, but for a different reason: {ex.Message}");
}// Consumer does not control the completion
