using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== DEMO 08: ExecutionContext flow + AsyncLocal<T> (and why ThreadLocal breaks) ===");
Console.WriteLine();

static readonly AsyncLocal<string?> _asyncLocal = new();
static readonly ThreadLocal<string?> _threadLocal = new();

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
Console.WriteLine("Copy-on-write demo: child flow inherits, but changes don't propagate back.");
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
Console.WriteLine("- AsyncLocal stores ambient data on that logical async flow.");
Console.WriteLine("- ThreadLocal is a trap in async code because threads can change.");
