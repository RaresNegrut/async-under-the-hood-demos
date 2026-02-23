using System.Diagnostics;

Console.WriteLine("CancellationToken (cooperative; must be observed + propagated)");
Console.WriteLine();

using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromMilliseconds(350));

try
{
    await IoLikeWorkAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("I/O-like work canceled (Task.Delay observed the token).");
}

Console.WriteLine();
Console.WriteLine("Now a CPU-bound loop: cancellation only happens if *you* check.");
using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

try
{
    CpuBoundWork(cts2.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("CPU-bound work canceled (ThrowIfCancellationRequested checkpoint hit).");
}

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- Cancellation is cooperative: pass the token everywhere and check it at boundaries.");

static async Task IoLikeWorkAsync(CancellationToken ct)
{
    Console.WriteLine("Starting I/O-like work (await Task.Delay with token)...");
    await Task.Delay(5_000, ct);
    Console.WriteLine("You should never see this line.");
}

static void CpuBoundWork(CancellationToken ct)
{
    Console.WriteLine("Starting CPU-bound loop (with periodic cancellation checks)...");
    var sw = Stopwatch.StartNew();

    long sum = 0;
    for (int i = 0; i < int.MaxValue; i++)
    {
        sum += i;

        if ((i % 2_000_000) == 0)
            ct.ThrowIfCancellationRequested();
    }

    sw.Stop();
    Console.WriteLine($"Finished: sum={sum}, elapsed={sw.ElapsedMilliseconds}ms");
}
