using System.Runtime.CompilerServices;

Console.WriteLine("09.The awaitable pattern (custom awaiter)");
Console.WriteLine("Goal: await is pattern-based (GetAwaiter + IsCompleted + OnCompleted + GetResult).");
Console.WriteLine();

Console.WriteLine("Before custom awaitable...");
await new MyDelayAwaitable(250);
Console.WriteLine("After custom awaitable!");

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
