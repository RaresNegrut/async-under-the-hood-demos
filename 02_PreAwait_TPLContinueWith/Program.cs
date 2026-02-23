Console.WriteLine("=== Pre-await TPL (ContinueWith) vs async/await ===");
Console.WriteLine("Same mechanics, very different ergonomics (nesting, AggregateException, Unwrap).");
Console.WriteLine();

static Task Delay(int dueTimeMs)
{
    if (dueTimeMs < -1)
        throw new ArgumentOutOfRangeException("dueTimeMs", "Invalid due time");

    var tcs = new TaskCompletionSource<object>();
    var timer = new Timer(delegate (object? self)
    {
        if(self is not null)
        {
            ((Timer)self).Dispose();
        }
        
        tcs.TrySetResult(null!);
    });
    
    timer.Change(dueTimeMs, -1);
    return tcs.Task;
}


static Task DoWorkTPL(bool throwInStep2)
{
    Console.WriteLine("[TPL] Step 1");
    return Delay(2000).ContinueWith(t1 =>
    {
        Console.WriteLine("[TPL] Step 2");
        if (throwInStep2) throw new InvalidOperationException("Boom in TPL step 2");

        // Returning Task from ContinueWith produces Task<Task> -> needs Unwrap.
        return Delay(2000).ContinueWith(_ => Console.WriteLine("[TPL] Step 3"));
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
#region Success Path for TPL and await
Console.WriteLine("Case A: success (TPL then await)");

var mres = new ManualResetEventSlim(false);
#pragma warning disable CS4014 // We block on mres.Wait() at the end, with the delegate signaling completion
DoWorkTPL(throwInStep2: false).ContinueWith(async _ =>
{
    await DoWorkAwait(throwInStep2: false);
    #endregion

    Console.WriteLine();
    Console.WriteLine();

    #region Error Path for TPL and await
    Console.WriteLine("Case B: error path (compare exception shape)");

    try
    {
        DoWorkTPL(throwInStep2: true).Wait();
    }
    catch (AggregateException ae)
    {
        Console.WriteLine($"TPL + .Wait() threw: {ae.GetType().Name}");
        Console.WriteLine($"  Message  : {ae.Message}");
        Console.WriteLine($"  Inner[0] : {ae.InnerExceptions[0].GetType().Name}: {ae.InnerExceptions[0].Message}");
        Console.WriteLine("  (.Wait() and .Result wrap exceptions in AggregateException — you must unwrap manually)");
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
    #endregion
    mres.Set();
});
#pragma warning restore CS4014

mres.Wait();