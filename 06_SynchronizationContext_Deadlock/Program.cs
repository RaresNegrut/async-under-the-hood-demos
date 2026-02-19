using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 06: SynchronizationContext + the classic deadlock ===");
Console.WriteLine("Goal: show *why* .Result/.Wait() can deadlock on a context-bound thread (UI-style).");
Console.WriteLine();

// We run both scenarios on a dedicated "UI thread" to make it concrete.
RunOnUiThread(ui =>
{
    Console.WriteLine("[Case A] BAD: blocking with .Result on the UI thread (no message pumping) ...");
    var started = DateTimeOffset.UtcNow;

    // This call blocks the UI thread. Continuation tries to post back to the UI context.
    // But the UI thread is blocked, so nothing pumps the queue => deadlock.
    try
    {
        _ = SomeAsync().Result;
        Console.WriteLine("Unexpected: completed.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected exception: {ex}");
    }

    // We'll never get here; the host detects the hang and reports it.
});

Console.WriteLine();
RunOnUiThread(ui =>
{
    Console.WriteLine("[Case B] GOOD: don't block; keep the UI context pumping while the Task is incomplete ...");

    // Start async work. It will capture this SynchronizationContext.
    var task = SomeAsync();

    // When it completes (on ThreadPool), tell the UI context to stop pumping.
    task.ContinueWith(_ => ui.Post(_ => ui.Complete(), null), TaskScheduler.Default);

    // Pump the UI queue. This is the “message loop”.
    ui.RunOnCurrentThread();

    Console.WriteLine("Completed without deadlock.");
});

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- In UI frameworks, continuations are posted back to the UI SynchronizationContext.");
Console.WriteLine("- If you block the UI thread with .Result/.Wait(), the posted continuation can't run => deadlock.");

static async Task<int> SomeAsync()
{
    Console.WriteLine($"  SomeAsync: before await (thread {Environment.CurrentManagedThreadId})");
    await Task.Delay(200); // captures SynchronizationContext.Current
    Console.WriteLine($"  SomeAsync: after  await (thread {Environment.CurrentManagedThreadId})");
    return 123;
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

    if (!done.Wait(TimeSpan.FromMilliseconds(600)))
        Console.WriteLine("  -> Detected hang (deadlock) as expected. Moving on safely.");
    else if (error is not null)
        Console.WriteLine($"  -> UI thread threw: {error}");
}

sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback d, object? state)> _queue = new();
    private int _completed;

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (Volatile.Read(ref _completed) == 1) return;
        _queue.Add((d, state));
    }

    public void RunOnCurrentThread()
    {
        foreach (var (d, state) in _queue.GetConsumingEnumerable())
        {
            d(state);
            if (Volatile.Read(ref _completed) == 1)
                break;
        }
    }

    public void Complete()
    {
        Interlocked.Exchange(ref _completed, 1);
        _queue.CompleteAdding();
    }
}
