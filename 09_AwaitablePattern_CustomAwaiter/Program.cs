using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 09: The awaitable pattern (custom awaiter) ===");
Console.WriteLine("Goal: await is pattern-based (GetAwaiter + IsCompleted + OnCompleted + GetResult).");
Console.WriteLine();

Console.WriteLine("Before custom awaitable...");
await new MyDelayAwaitable(250);
Console.WriteLine("After custom awaitable!");

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- The compiler doesn't require Task.");
Console.WriteLine("- It just requires the awaitable pattern.");

readonly struct MyDelayAwaitable
{
    private readonly int _ms;
    public MyDelayAwaitable(int ms) => _ms = ms;
    public MyDelayAwaiter GetAwaiter() => new(_ms);
}

readonly struct MyDelayAwaiter : INotifyCompletion
{
    private readonly Task _inner;
    public MyDelayAwaiter(int ms) => _inner = Task.Delay(ms);

    public bool IsCompleted => _inner.IsCompleted;
    public void OnCompleted(Action continuation) => _inner.GetAwaiter().OnCompleted(continuation);
    public void GetResult() => _inner.GetAwaiter().GetResult();
}
