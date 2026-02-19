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
        Console.WriteLine("Key observation:");
        Console.WriteLine("- The method body is rewritten into MoveNext() + a state field + awaiter fields + a builder.");
    }

    private static async Task ExampleAsync()
    {
        Console.WriteLine($"  Step 1 (thread {Environment.CurrentManagedThreadId})");
        await Task.Delay(150);
        Console.WriteLine($"  Step 2 (thread {Environment.CurrentManagedThreadId})");
        await Task.Delay(100);
        Console.WriteLine($"  Step 3 (thread {Environment.CurrentManagedThreadId})");
    }
}
