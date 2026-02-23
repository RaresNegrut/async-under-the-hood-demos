Console.WriteLine("ExecutionContext flow + AsyncLocal<T> (Why ThreadLocal breaks) ===");
Console.WriteLine();

AsyncLocal<string?> _asyncLocal = new();
ThreadLocal<string?> _threadLocal = new();

_asyncLocal.Value = "Alice";
_threadLocal.Value = "Alice";

Console.WriteLine($"Start thread: {Environment.CurrentManagedThreadId}");
Console.WriteLine($"  AsyncLocal : {_asyncLocal.Value}");
Console.WriteLine($"  ThreadLocal: {_threadLocal.Value}");

Console.WriteLine();
Console.WriteLine("Force a thread hop with await Task.Run(...)");
await Task.Run(() => { /* hop */ });

Console.WriteLine($"After hop thread: {Environment.CurrentManagedThreadId}");
Console.WriteLine($"  AsyncLocal : {_asyncLocal.Value}   (flows with ExecutionContext)");
Console.WriteLine($"  ThreadLocal: {_threadLocal.Value ?? "<null>"} (tied to physical thread)");

Console.WriteLine();
Console.ReadKey();
Console.WriteLine("=============================");
Console.WriteLine("Copy-on-write demo: child flow inherits, but changes don't propagate \"outward\"");

_asyncLocal.Value = "Parent(Alice)";
Console.WriteLine($"  Parent before Task.Run: {_asyncLocal.Value}");

await Task.Run(() =>
{
    Console.WriteLine($"  Child sees: {_asyncLocal.Value}");
    _asyncLocal.Value = "Child(Bob)";
    Console.WriteLine($"  Child changed to: {_asyncLocal.Value}");
});

Console.WriteLine($"  Parent after Task.Run : {_asyncLocal.Value}");

Console.WriteLine();
Console.WriteLine("Key observation:");
Console.WriteLine("- ExecutionContext is captured at await and restored on resume.");
