using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

internal static class Program
{
    private static async Task Main()
    {
        Console.WriteLine("=== DEMO 10: Compiler state machine (reflection view) ===");
        Console.WriteLine("Goal: show that async methods compile into a state machine with MoveNext().");
        Console.WriteLine();

        Console.WriteLine("Part 1: async methods start synchronously");
        var t = ExampleAsync(); // call without awaiting yet
        Console.WriteLine($"Returned Task immediately. Status={t.Status}");
        await t;

        Console.WriteLine();
        Console.WriteLine("Part 2: reflect the state machine type and its fields");
        var method = typeof(Program).GetMethod(nameof(ExampleAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        var attr = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        Console.WriteLine($"AsyncStateMachineAttribute.StateMachineType: {attr?.StateMachineType.FullName}");

        if (attr?.StateMachineType is not null)
        {
            var smType = attr.StateMachineType;

            Console.WriteLine($"Is value type (struct): {smType.IsValueType}");
            if (!smType.IsValueType)
                Console.WriteLine("  (Note: Debug builds emit a class for easier debugging; Release builds emit a struct)");
            var moveNext = smType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Console.WriteLine($"Has MoveNext(): {moveNext is not null}");

            Console.WriteLine("Fields:");
            foreach (var f in smType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                   .OrderBy(f => f.Name))
            {
                Console.WriteLine($"  {f.FieldType.Name,-30} {f.Name}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Part 3: hand-rolled state machine (what MoveNext() actually does)");
        Console.WriteLine("  This mirrors the compiler-generated code for ExampleAsync, with state transitions logged.");
        Console.WriteLine();

        var sim = new HandRolledStateMachine();
        sim.Start();
        await sim.Task;

        Console.WriteLine();
        Console.WriteLine("Key observation:");
        Console.WriteLine("- The method body is rewritten into MoveNext() + a state field + awaiter fields + a builder.");
        Console.WriteLine("- MoveNext() is called once per segment. State -1=running, 0/1/...=suspended, -2=done.");
        Console.WriteLine("- Between calls to MoveNext(): NO thread is blocked. A timer fires and re-invokes MoveNext().");
    }

    private static async Task ExampleAsync()
    {
        Console.WriteLine($"  Step 1 (thread {Environment.CurrentManagedThreadId})");
        await Task.Delay(150);
        Console.WriteLine($"  Step 2 (thread {Environment.CurrentManagedThreadId})");
        await Task.Delay(100);
        Console.WriteLine($"  Step 3 (thread {Environment.CurrentManagedThreadId})");
    }

    /// <summary>
    /// Hand-written equivalent of what the compiler generates for ExampleAsync.
    /// Same structure as the real MoveNext(), with logging at every state transition.
    /// </summary>
    private sealed class HandRolledStateMachine
    {
        private int _state = -1;
        private readonly TaskCompletionSource _tcs = new();
        private TaskAwaiter _awaiter;

        public Task Task => _tcs.Task;

        public void Start()
        {
            Console.WriteLine($"  [Stub] State machine created. <>1__state = {_state}");
            Console.WriteLine($"  [Stub] builder.Start() calls MoveNext() synchronously...");
            MoveNext();
            Console.WriteLine($"  [Stub] Returning builder.Task to caller (Status={Task.Status})");
        }

        private void MoveNext()
        {
            Console.WriteLine($"  ┌─ MoveNext() entered. <>1__state = {_state}, thread {Environment.CurrentManagedThreadId}");
            try
            {
                switch (_state)
                {
                    case 0: goto AfterFirstAwait;
                    case 1: goto AfterSecondAwait;
                }

                // ── Segment A: user code before first await ──
                Console.WriteLine($"  │  Segment A: executing user code...");
                Console.WriteLine($"  │    → Step 1 (thread {Environment.CurrentManagedThreadId})");

                _awaiter = System.Threading.Tasks.Task.Delay(200).GetAwaiter();
                Console.WriteLine($"  │  Checking awaiter.IsCompleted → {_awaiter.IsCompleted}");
                if (!_awaiter.IsCompleted)
                {
                    _state = 0;
                    Console.WriteLine($"  │  Not complete → <>1__state ← {_state}. Registering MoveNext as callback.");
                    Console.WriteLine($"  └─ RETURN. Thread released — no thread blocked during the wait.");
                    _awaiter.OnCompleted(MoveNext);
                    return;
                }

            AfterFirstAwait:
                // ── Segment B: code between first and second await ──
                Console.WriteLine($"  │  Segment B: resuming after first await.");
                _awaiter.GetResult();   // would rethrow if the awaited task faulted
                _state = -1;            // back to "running"
                Console.WriteLine($"  │    → Step 2 (thread {Environment.CurrentManagedThreadId})");

                _awaiter = System.Threading.Tasks.Task.Delay(150).GetAwaiter();
                Console.WriteLine($"  │  Checking awaiter.IsCompleted → {_awaiter.IsCompleted}");
                if (!_awaiter.IsCompleted)
                {
                    _state = 1;
                    Console.WriteLine($"  │  Not complete → <>1__state ← {_state}. Registering MoveNext as callback.");
                    Console.WriteLine($"  └─ RETURN. Thread released — no thread blocked during the wait.");
                    _awaiter.OnCompleted(MoveNext);
                    return;
                }

            AfterSecondAwait:
                // ── Segment C: code after last await ──
                Console.WriteLine($"  │  Segment C: resuming after second await.");
                _awaiter.GetResult();
                _state = -1;
                Console.WriteLine($"  │    → Step 3 (thread {Environment.CurrentManagedThreadId})");
            }
            catch (Exception ex)
            {
                _state = -2;
                Console.WriteLine($"  │  Exception! <>1__state ← -2 (terminal). builder.SetException().");
                Console.WriteLine($"  └─ MoveNext() done (faulted).");
                _tcs.SetException(ex);
                return;
            }

            _state = -2;
            Console.WriteLine($"  │  All segments complete. <>1__state ← -2 (terminal).");
            Console.WriteLine($"  │  builder.SetResult() → outer Task is now RanToCompletion.");
            Console.WriteLine($"  └─ MoveNext() done. 3 calls total, 3 segments, 0 threads blocked during waits.");
            _tcs.SetResult();
        }
    }
}
