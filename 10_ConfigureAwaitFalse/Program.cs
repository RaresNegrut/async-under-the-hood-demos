using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 07: ConfigureAwait(false) as the library-side escape hatch ===");
Console.WriteLine("Goal: show that opting out of SynchronizationContext capture prevents the classic deadlock.");
Console.WriteLine();

// Run on a dedicated "UI thread" with a SynchronizationContext installed,
// but DO NOT pump it. We'll block with .Result and see what happens.
RunOnUiThread(ui =>
{
    Console.WriteLine("[Case] Blocking with .Result on a context thread...");
    Console.WriteLine("  - The awaited operation uses ConfigureAwait(false), so its continuation doesn't need the UI context.");
    Console.WriteLine("  - The Task can complete from the ThreadPool, unblocking .Result.");

    var result = SomeLibraryMethodAsync().Result;
    Console.WriteLine($"Completed. Result={result}");
});

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- ConfigureAwait(false) tells the awaiter: do NOT post the continuation back to the captured context.");
Console.WriteLine("- That makes library code resilient when a caller accidentally blocks on async (still not recommended).");

static async Task<int> SomeLibraryMethodAsync()
{
    Console.WriteLine($"  Library: before await (thread {Environment.CurrentManagedThreadId})");
    await Task.Delay(200).ConfigureAwait(false); // opt out of SynchronizationContext capture
    Console.WriteLine($"  Library: after  await (thread {Environment.CurrentManagedThreadId})");
    return 42;
}

static void RunOnUiThread(Action<SingleThreadSynchronizationContext> body)
{
    var done = new ManualResetEventSlim(false);
    Exception? error = null;

    var thread = new Thread(() =>
    {
        var ui = new SingleThreadSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);

        try { body(ui); }
        catch (Exception ex) { error = ex; }
        finally { done.Set(); }
    })
    { IsBackground = true, Name = "Fake-UI-Thread" };

    thread.Start();

    if (!done.Wait(TimeSpan.FromSeconds(2)))
        Console.WriteLine("Unexpected: hung (should not).");
    else if (error is not null)
        Console.WriteLine($"UI thread threw: {error}");
}

sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback d, object? state)> _queue = new();
    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
}
