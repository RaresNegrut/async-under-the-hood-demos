using System;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 02: Pre-await TPL (ContinueWith) vs async/await ===");
Console.WriteLine("Goal: same mechanics, very different ergonomics (nesting, AggregateException, Unwrap).");
Console.WriteLine();

static Task DoWorkTPL(bool throwInStep2)
{
    Console.WriteLine("[TPL] Step 1");
    return Task.Delay(150).ContinueWith(t1 =>
    {
        Console.WriteLine("[TPL] Step 2");
        if (throwInStep2) throw new InvalidOperationException("Boom in TPL step 2");

        // Returning Task from ContinueWith produces Task<Task> -> needs Unwrap.
        return Task.Delay(150).ContinueWith(_ => Console.WriteLine("[TPL] Step 3"));
    }).Unwrap();
}

static async Task DoWorkAwait(bool throwInStep2)
{
    Console.WriteLine("[await] Step 1");
    await Task.Delay(150);
    Console.WriteLine("[await] Step 2");
    if (throwInStep2) throw new InvalidOperationException("Boom in await step 2");
    await Task.Delay(150);
    Console.WriteLine("[await] Step 3");
}

Console.WriteLine("Case A: success (TPL then await)");
await DoWorkTPL(throwInStep2: false);
await DoWorkAwait(throwInStep2: false);

Console.WriteLine();
Console.WriteLine("Case B: error path (compare exception shape)");

// Use .Wait() so the AggregateException is NOT unwrapped (this is what UI code does with .Result)
try
{
    DoWorkTPL(throwInStep2: true).Wait();
}
catch (AggregateException ae)
{
    Console.WriteLine($"TPL + .Wait() threw: {ae.GetType().Name}");
    Console.WriteLine($"  Message  : {ae.Message}");
    Console.WriteLine($"  Inner[0] : {ae.InnerExceptions[0].GetType().Name}: {ae.InnerExceptions[0].Message}");
    Console.WriteLine("  (.Wait()/.Result wraps exceptions in AggregateException — you must unwrap manually)");
}

// With await, the infrastructure unwraps AggregateException for you
try
{
    await DoWorkAwait(throwInStep2: true);
}
catch (Exception ex)
{
    Console.WriteLine($"await threw: {ex.GetType().Name}  (clean, unwrapped automatically)");
    Console.WriteLine($"  Message  : {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- ContinueWith pushes you into manual fault checks / nesting / Unwrap.");
Console.WriteLine("- async/await gives you normal control-flow + try/catch.");
