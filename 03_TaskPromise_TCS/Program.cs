using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("Task == promise; Task != Thread; TaskCompletionSource Proof");
Console.WriteLine();

Console.WriteLine("Part 1: Tasks using no Threads. Tasks that use Threads");
Console.WriteLine($"  Current thread: {Environment.CurrentManagedThreadId}");

#region No thread involved
Console.WriteLine("No threads involved; TCS lets dev arbitrarily complete the task");
var tcs = new TaskCompletionSource<int>();
tcs.SetResult(42);  // Now tcs.Task is complete, no thread is 'running' it, but it has a result ready.
await tcs.Task;     // Returns 42


var delay = Task.Delay(200); // timer-based
Console.WriteLine($"  Task.Delay created. Status={delay.Status} (no thread is 'running' this task).");
#endregion

#region Threads involved
var run = Task.Run(() =>
{
    Console.WriteLine($"  Task.Run executing on thread: {Environment.CurrentManagedThreadId}");
});
await Task.WhenAll(delay, run);
Console.WriteLine($"  After await. Current thread: {Environment.CurrentManagedThreadId}");
#endregion
Console.WriteLine();