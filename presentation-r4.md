# Beyond the Basics of async/await

---

## 1. Opening — "We all do this, but why?"

You've written `async Task DoSomethingAsync()` hundreds of times. You know the rule: if it's async, you await it. Your IDE screams at you if you forget. Your code reviews demand it. And so you do it — almost mechanically.

But do you know what actually happens when you press F5?

Here's a method you've written a thousand times:

```csharp
async Task DoWorkAsync()
{
    Console.WriteLine("Before");
    await Task.Delay(1000);
    Console.WriteLine("After");
}
```

Three lines. Simple. But behind those three lines, the compiler is doing *a lot*. It's rewriting your method into something you wouldn't recognise. And the runtime is coordinating a whole cast of infrastructure you've never had to think about — until today.

The rest of this talk is going to answer the questions you've probably never asked:

- What *is* a `Task`, really?
- Who actually *runs* it?
- What does `await` compile to?
- Why does `ConfigureAwait(false)` exist, and should you care?

Let's peel the layers back, one at a time.

> **Key takeaway:** You use async/await every day. It's time to understand what's actually happening underneath.

---

## 2. Why async code exists at all

Let's start from the problem.

Threads are expensive. Each thread in .NET costs roughly 1 MB of stack space, plus the OS overhead of creating, scheduling, and context-switching it. Creating a thread isn't free either — the OS has to allocate that stack, set up kernel data structures, and register the thread with the scheduler. And destroying it has its own cost.

Now think about a server handling 10,000 concurrent requests. If each request occupies a thread for the duration of its work — including the time it spends *waiting* for a database query or an HTTP response — that's 10,000 threads. That's roughly 10 GB of stack space alone, before you've even done anything useful. And the OS thread scheduler is spending all its time context-switching between thousands of threads, most of which are doing absolutely nothing.

That's exactly what synchronous, blocking code does:

```csharp
void DoWorkSync()
{
    Console.WriteLine("Before");
    Thread.Sleep(1000);  // Thread is blocked — doing nothing, but held hostage
    Console.WriteLine("After");
}
```

That `Thread.Sleep` is a crime scene. The thread is alive, consuming memory, registered with the OS scheduler — and it's accomplishing absolutely nothing. It's just *waiting*. But because it's blocked, nobody else can use it. It's a wasted resource.

Now consider what we'd *like* to happen instead: "Hey runtime, I need to wait for 1000 milliseconds. I don't need this thread during that time. Take it back, give it to someone else, and come find me when the timer fires." That's exactly the mental model of async:

```csharp
async Task DoWorkAsync()
{
    Console.WriteLine("Before");
    await Task.Delay(1000);  // Thread is released — free to do other work
    Console.WriteLine("After");
}
```

With `Task.Delay`, no thread is held. A timer is set at the OS level. The thread that was running this method is released and can go do other useful work. When the timer fires, the runtime arranges for a thread to pick up where we left off and print "After".

The difference is dramatic. That server handling 10,000 concurrent requests? With async I/O, it might only need 20-50 threads — the ones actually *computing* at any given instant. The rest of the requests are just waiting, and waiting doesn't require a thread.

This is also why async is not the same as parallelism. Parallelism means doing multiple things at the same time to go faster. Async means not holding resources hostage while doing nothing. You might use both together, but they solve fundamentally different problems. A single-threaded application can still benefit enormously from async — it just means that one thread can juggle many operations instead of being pinned to one at a time.

The phrase to remember: async is about **yielding control while waiting**, so that resources (threads) can be used elsewhere.

> **Key takeaway:** Async is about **not wasting threads while waiting**, not about parallelism. A blocked thread is a wasted thread.

---

## 3. Async before `await` — the TPL way

So if the goal is "don't block the thread while waiting," how did we achieve that before `async`/`await` came along in C# 5.0?

The answer was the Task Parallel Library (TPL), specifically the `ContinueWith` method. The idea was: start an operation, and *attach a callback* that runs when it finishes. The thread that started the operation is free to leave; the callback will be invoked later, on whatever thread is available.

Let's rewrite our simple "Before / Delay / After" example using this approach:

```csharp
Task DoWorkWithTPL()
{
    Console.WriteLine("Before");

    return Task.Delay(1000).ContinueWith(_ =>
    {
        Console.WriteLine("After");
    });
}
```

That's not terrible for one step. But real code has sequential steps. And each step needs the result of the previous one. So you end up nesting:

```csharp
Task DoWorkWithTPL()
{
    Console.WriteLine("Before");

    return Task.Delay(1000).ContinueWith(_ =>
    {
        Console.WriteLine("After step 1");

        Task.Delay(500).ContinueWith(_ =>
        {
            Console.WriteLine("After step 2");
        });
    });
}
```

Two callbacks deep. And this is the *trivial* case — two delays and some print statements. Imagine ten sequential asynchronous operations. You'd have ten levels of nesting. This is the exact same problem JavaScript developers called "callback hell," and for the same reason.

But it gets worse. What about error handling?

```csharp
Task DoWorkWithTPLAndErrors()
{
    Console.WriteLine("Before");

    return Task.Delay(1000).ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            // t.Exception is an AggregateException — unwrap it yourself
            Console.WriteLine(t.Exception.InnerException.Message);
            return Task.CompletedTask;
        }

        Console.WriteLine("After step 1");
        return Task.Delay(500);
    }).Unwrap().ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            Console.WriteLine(t.Exception.InnerException.Message);
            return;
        }
        Console.WriteLine("After step 2");
    });
}
```

With `ContinueWith`, there's no `try`/`catch`. Exceptions are wrapped in `AggregateException` on the Task's `.Exception` property. You have to check `IsFaulted` manually in every callback. Miss a check, and the exception vanishes silently.

And there's `Unwrap()` — because `ContinueWith` on a `Task` that returns another `Task` gives you a `Task<Task>`, and you have to flatten it yourself.

The pain points stack up quickly:

- **Nesting**: each sequential step pushes you deeper. The code structure doesn't reflect the logical flow.
- **Error handling**: manual, error-prone, and easy to forget.
- **Composing sequential steps**: requires `Unwrap`, careful return types, and mental gymnastics.
- **Context**: if you needed your callback to run in a specific place (like the UI thread), you had to pass extra arguments to `ContinueWith` manually. We'll see why that matters later.
- **Readability**: the code reads nothing like the sequential logic it represents. Someone reading it has to mentally unwind the nesting to understand the flow.

Now look at the same logic with `async`/`await`:

```csharp
async Task DoWorkClean()
{
    Console.WriteLine("Before");

    await Task.Delay(1000);
    Console.WriteLine("After step 1");

    await Task.Delay(500);
    Console.WriteLine("After step 2");
}
```

Flat. Sequential. `try`/`catch` works normally. The compiler handles all the callback plumbing for you. This is the same underlying mechanism — continuations on Tasks — but the developer experience is night and day.

> **Key takeaway:** `await` is syntactic sugar that solves the callback-hell problem the TPL introduced. Same mechanics underneath, dramatically better developer experience.

---

## 4. What is a `Task`?

We keep saying "Task" — it's time to pin down what that actually means.

Here's the most important misconception to kill: **a `Task` is not a thread**. A `Task` does not represent a thread. It does not inherently own a thread, use a thread, or require a thread.

A `Task` is a **promise** — a data structure that represents an operation that will complete at some point in the future. It's a handle you can hold onto. You can ask it: "Are you done yet?" You can say: "Call me back when you're done." You can await it. But the Task itself is just a small object sitting on the heap, tracking whether its associated operation has completed, faulted, or been cancelled.

Let's make this concrete. Consider:

```csharp
// This does NOT create a thread. It sets a timer.
Task delayTask = Task.Delay(1000);
// No thread is "running" this delay. A system timer is ticking somewhere in the OS.
// The Task is just a handle that will transition to "completed" when the timer fires.
```

No thread was created. No thread is executing. A system timer was registered, and when it fires, it will mark this Task object as completed. During the 1000ms wait, zero threads are involved.

Now compare:

```csharp
// This DOES use a thread — it queues the lambda to run on a worker thread.
Task runTask = Task.Run(() =>
{
    Console.WriteLine("I'm on a worker thread");
});
```

`Task.Run` takes your delegate and queues it to be executed by a worker thread (we'll talk about where those threads come from in the next section). But notice: the Task is still just the *promise object*. It's not the thread. It's the *notification mechanism* — the thing that lets you know when the work the thread did is finished.

So the same type — `Task` — is used as a promise for both "a timer will fire" and "a thread will run this code." The completion mechanism is completely different, but the promise API is the same. This is a powerful abstraction.

To really drive this home, here's `TaskCompletionSource<T>` — a way to create a Task and complete it manually, proving that no thread ever needs to be involved:

```csharp
var tcs = new TaskCompletionSource<int>();

// At this point, tcs.Task exists but is not yet completed.
// No thread is running anything.

// Some time later — maybe in response to an event callback,
// or a hardware interrupt, or a message from another system:
tcs.SetResult(42);

// Now tcs.Task is completed with the value 42.
// No thread ever "ran" this Task. We just created a promise and fulfilled it.
Task<int> promise = tcs.Task;
```

`TaskCompletionSource` is how most truly asynchronous APIs work under the hood. At the bottom of the stack, there's almost always a `TaskCompletionSource` being completed by an OS callback — network data arrived, a file read finished, a timer fired. No dedicated thread sat around waiting.

### TaskCompletionSource in depth

`TaskCompletionSource<T>` (and the non-generic `TaskCompletionSource` added in .NET 5) is the bridge between event-driven, callback-based code and the async/await world. It gives you manual control over a Task's lifecycle: you create it, hold it open, and complete it whenever you're ready.

There are three ways to complete a `TaskCompletionSource`:

```csharp
var tcs = new TaskCompletionSource<string>();

// Success — transitions tcs.Task to RanToCompletion with a result:
tcs.SetResult("done");

// Fault — transitions tcs.Task to Faulted with an exception:
tcs.SetException(new InvalidOperationException("something went wrong"));

// Cancellation — transitions tcs.Task to Canceled:
tcs.SetCanceled();
```

Each of these can only be called once — calling any `Set*` method on an already-completed `TaskCompletionSource` throws `InvalidOperationException`. If you're in a situation where completion might race (e.g., a timeout and a real result arriving at the same time), use the `TrySet*` variants instead:

```csharp
// Returns false instead of throwing if already completed:
bool succeeded = tcs.TrySetResult("done");
bool faulted = tcs.TrySetException(new TimeoutException());
bool cancelled = tcs.TrySetCanceled();
```

A practical example — wrapping a callback-based API into async/await:

```csharp
Task<string> ReadLineAsync(StreamReader reader)
{
    var tcs = new TaskCompletionSource<string>();

    // Some legacy API that uses callbacks:
    reader.BeginReadLine(line =>
    {
        if (line != null)
            tcs.SetResult(line);
        else
            tcs.SetException(new EndOfStreamException());
    });

    return tcs.Task;  // Caller can await this
}
```

This pattern appears everywhere: wrapping EAP (Event-based Asynchronous Pattern) APIs, bridging hardware interrupts, implementing custom awaitables, and building testing infrastructure where you want to control when an operation "completes."

One important detail: by default, `TaskCompletionSource` runs continuations synchronously on the thread that calls `SetResult`. This can be dangerous if a lot of work chains off that completion — it all runs on the completing thread. To avoid this, pass `TaskCreationOptions.RunContinuationsAsynchronously` to the constructor:

```csharp
var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
// Now SetResult() will queue continuations to the ThreadPool instead of running them inline.
```

One more thing: `Task<T>` is just `Task` plus a result value. `Task` is a promise that something will *finish*. `Task<T>` is a promise that something will finish *and produce a value of type T*. Same concept, slightly more useful.

> **Key takeaway:** A Task is a data structure — a promise — not a unit of execution. TaskCompletionSource is the primitive that lets you create and manually complete Tasks, bridging callback-based APIs into the async/await world.

---

## 5. CancellationToken & CancellationTokenSource — cooperative cancellation

Now that we know Tasks are promises, a natural question arises: what if we want to *cancel* an in-flight operation? Maybe the user navigated away, a timeout expired, or the server is shutting down.

.NET's answer is **cooperative cancellation** via `CancellationToken` and `CancellationTokenSource`. The key word is *cooperative* — you cannot forcibly abort an async operation from the outside. Instead, you pass a token into the method, and the method must actively check it and decide to stop.

### The two halves

The cancellation system is split into two types with distinct roles:

- **`CancellationTokenSource`** (CTS) — the *controller*. It creates the token and owns the `Cancel()` method. The code that *initiates* cancellation holds this.
- **`CancellationToken`** (CT) — the *read-only handle*. The code that *does the work* receives this. It can observe whether cancellation has been requested, but it cannot trigger it.

This separation of concerns is deliberate: the worker code can check for cancellation but can't accidentally trigger it, and the controlling code can signal cancellation without needing access to the operation's internals.

```csharp
// The controller: creates the source and the token
var cts = new CancellationTokenSource();

// Pass the token to the worker
Task work = DoLongWorkAsync(cts.Token);

// Some time later — user clicks cancel, or a timeout fires:
cts.Cancel();  // Signals all observers of cts.Token

// The worker method
async Task DoLongWorkAsync(CancellationToken ct)
{
    // BCL methods accept tokens natively:
    await Task.Delay(10_000, ct);  // Throws OperationCanceledException if cancelled

    // For CPU-bound loops, check manually:
    for (int i = 0; i < 1_000_000; i++)
    {
        ct.ThrowIfCancellationRequested();  // Throws if Cancel() was called
        // ... do work ...
    }

    // Always pass the token downstream:
    await httpClient.GetAsync(url, ct);
}
```

### Timed cancellation

`CancellationTokenSource` supports automatic cancellation after a timeout:

```csharp
// Cancel automatically after 5 seconds
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

// Or set it after creation:
var cts2 = new CancellationTokenSource();
cts2.CancelAfter(TimeSpan.FromSeconds(10));

try
{
    await DoWorkAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation timed out or was cancelled");
}
```

### Linked cancellation

You can combine multiple cancellation sources with `CreateLinkedTokenSource`. The linked source cancels when *any* of its parents cancel — useful for combining a user-initiated cancel with a timeout:

```csharp
// User can cancel manually, OR the operation times out after 30s
var userCts = new CancellationTokenSource();
var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

using var linked = CancellationTokenSource.CreateLinkedTokenSource(
    userCts.Token, timeoutCts.Token);

await DoWorkAsync(linked.Token);
// Cancels if the user calls userCts.Cancel() OR if 30 seconds pass
```

### Exception handling

When a CancellationToken is observed, methods throw `OperationCanceledException` (or its subclass `TaskCanceledException`). Catch it at the top level of your call chain:

```csharp
try
{
    await SomeComplexOperationAsync(ct);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // Expected cancellation — clean up gracefully
    Console.WriteLine("Operation was cancelled");
}
```

The `when` guard ensures you only catch cancellation from *your* token, not from some unrelated internal cancellation.

### Disposal

`CancellationTokenSource` implements `IDisposable`. If you use `CancelAfter` or the timeout constructor, it registers with an internal timer. Always dispose when done to release those resources. In long-lived scenarios (like ASP.NET request handling), the framework typically handles this for you.

### Common mistakes

The most common mistake is accepting a `CancellationToken` parameter but never passing it downstream. If your method calls `await httpClient.GetAsync(url)` without the token, cancellation won't propagate — the HTTP request will continue even after the caller cancelled. Always thread the token through your entire call chain.

> **Key takeaway:** Cancellation in .NET is cooperative: `CancellationTokenSource` controls cancellation, `CancellationToken` observes it. Pass the token to every async method in the chain, use `ThrowIfCancellationRequested()` in CPU loops, and catch `OperationCanceledException` at the top level.

---

## 6. What runs my Tasks? — The ThreadPool

So if a `Task` is just a promise, and `Task.Delay` doesn't use threads, something still has to run the code you pass to `Task.Run`. Where do those threads come from?

The answer is the .NET **ThreadPool** — a pool of pre-created worker threads, managed by the runtime, that exist for the lifetime of your application. When you call `Task.Run(...)`, the delegate you pass is placed into a queue. One of the ThreadPool's worker threads dequeues it and executes it.

```csharp
Task.Run(() =>
{
    Console.WriteLine($"Running on thread {Environment.CurrentManagedThreadId}");
});

Task.Run(() =>
{
    Console.WriteLine($"Also on thread {Environment.CurrentManagedThreadId}");
});
// These might run on the same thread, or different ones — the pool decides.
```

Why a pool instead of creating a new thread each time? Because thread creation is expensive. Creating a thread involves allocating ~1 MB of stack space, making OS system calls, and registering with the kernel scheduler. Destroying it reverses all that. If your server handles 1,000 requests per second and each one needs a thread for 5ms of CPU work, creating and destroying 1,000 threads per second would be ruinous. The ThreadPool solves this by reusing a fixed-ish set of threads.

The ThreadPool is also smart about sizing. It starts with a small number of threads (typically one per CPU core). If work items are queuing up faster than threads can process them, the pool slowly injects new threads — roughly one every 500ms. This conservative growth is intentional: it avoids oversaturating the CPU with context-switching overhead. But it means that if you suddenly block many ThreadPool threads (say, by calling `.Result` or `.Wait()` on Tasks), you can *starve* the pool. The pool sees that threads are "busy" (even though they're just blocked-waiting), and it adds new threads very slowly, causing cascading stalls. This is one of the biggest practical dangers of mixing blocking code with async code.

Now — and this is important — `Task.Delay` does *not* occupy a ThreadPool thread during the delay. It registers a system timer. No thread is consumed during the wait. But when that timer fires, someone needs to run the code that comes *after* the `await`. That "code after the await" is called a **continuation** — and by default, it gets queued to a ThreadPool thread to execute. (There are other places it can go, which we'll explore in the next section.)

```csharp
async Task ShowThreads()
{
    Console.WriteLine($"Before await: thread {Environment.CurrentManagedThreadId}");
    await Task.Delay(1000);
    // The continuation might run on a DIFFERENT thread than the one above
    Console.WriteLine($"After await: thread {Environment.CurrentManagedThreadId}");
}
```

Run this in a console app and you'll almost certainly see two different thread IDs. The thread that ran "Before" was released during the delay. When the timer fired, a (possibly different) ThreadPool thread picked up the continuation and ran "After."

This is the fundamental pattern: the code before an `await` runs on one thread. The code after the `await` — the continuation — may run on an entirely different thread. The ThreadPool is the default place where continuations are dispatched. But it's not the only option.

> **Key takeaway:** The ThreadPool is the default engine that runs work and continuations. It reuses threads to avoid creation overhead, but it can be starved if threads are blocked. `Task.Delay` uses a timer, not a thread — only the continuation afterwards needs a thread.

---

## 7. SynchronizationContext & TaskScheduler — the *real* schedulers

We just said that after an `await`, the continuation runs on a ThreadPool thread. But that's not always true. If you've written WPF or WinForms code, you know you can update UI controls after an `await` without getting a cross-thread exception. That means the continuation ran on the *UI thread*, not on some random ThreadPool thread.

How? The answer is **SynchronizationContext**.

### SynchronizationContext

`SynchronizationContext` is an abstraction that answers one question: **where should a continuation run?**

It has a `Post` method that takes a delegate and schedules it to run in the "right place." What "right place" means depends on the implementation:

- In **WinForms**, the `WindowsFormsSynchronizationContext` posts the delegate to the UI thread's Win32 message loop. The next time the message pump runs, it picks up your continuation and executes it on the UI thread.
- In **WPF**, the `DispatcherSynchronizationContext` posts to the WPF Dispatcher, which also runs on the UI thread.
- In **ASP.NET (classic, pre-Core)**, there was a request-scoped `SynchronizationContext` that ensured only one thread at a time could execute within a given request.
- In **ASP.NET Core** and **console apps**, there is **no** `SynchronizationContext`. The static property `SynchronizationContext.Current` returns `null`.

```csharp
// In a console app:
Console.WriteLine(SynchronizationContext.Current);
// Output: (nothing — it's null)

// In a WPF app's button click handler:
// SynchronizationContext.Current would be a DispatcherSynchronizationContext
// Its Post() method sends the delegate to the UI thread's Dispatcher queue
```

The key insight: when you `await` something, the async infrastructure checks `SynchronizationContext.Current` *before* suspending. If one exists, it's captured, and when the awaited operation completes, the continuation is posted to that context via its `Post` method. This is why, in a WPF app, you can write:

```csharp
async Task ButtonClickHandler()
{
    // Running on UI thread
    Console.WriteLine("Starting work...");

    await Task.Delay(2000);

    // STILL on UI thread — the SynchronizationContext brought us back
    Console.WriteLine("Done! Updating UI safely.");
}
```

Without a SynchronizationContext — in a console app, for instance — the continuation just runs on whatever ThreadPool thread is available.

### TaskScheduler

Below `SynchronizationContext` there's another abstraction: `TaskScheduler`. This is the lower-level scheduler used internally by the TPL's `Task.Run` and `ContinueWith` methods. The default, `TaskScheduler.Default`, sends work to the ThreadPool.

You typically don't interact with `TaskScheduler` directly in async/await code. It matters more when you use the TPL directly (as we saw in Section 3). But it's worth knowing it exists: when the async infrastructure can't find a `SynchronizationContext`, it falls back to using `TaskScheduler.Current` (which is usually `TaskScheduler.Default`, i.e., the ThreadPool).

### The priority chain

When a continuation needs to be scheduled after an `await`, the infrastructure follows this chain:

1. Check `SynchronizationContext.Current`. If non-null, capture it and use it to post the continuation.
2. If null, fall back to `TaskScheduler.Default` — which targets the ThreadPool.

### The classic deadlock

Understanding this scheduling chain also explains the most infamous async bug: the deadlock that happens when you call `.Result` or `.Wait()` on a Task from a thread that has a `SynchronizationContext`.

Here's the scenario, step by step:

1. You're on the UI thread. `SynchronizationContext.Current` is set.
2. You call `SomeAsyncMethod().Result` — this *blocks* the UI thread, waiting for the Task to complete.
3. Inside `SomeAsyncMethod`, the code hits an `await`. The infrastructure captures the `SynchronizationContext` (the UI context).
4. The awaited operation completes. The infrastructure tries to post the continuation to the UI thread via the captured context.
5. But the UI thread is blocked in step 2, waiting for the very Task that needs the UI thread to finish.
6. **Deadlock.** The UI thread is waiting for the Task. The Task is waiting for the UI thread.

```csharp
// DON'T DO THIS in UI code:
void ButtonClick()
{
    // This blocks the UI thread…
    var result = SomeAsyncMethod().Result;
    // …but SomeAsyncMethod's continuation needs the UI thread to run.
    // Deadlock.
}

async Task SomeAsyncMethod()
{
    await Task.Delay(1000);
    // This continuation will try to post to the UI thread's SynchronizationContext.
    // But that thread is blocked above. Dead.
    Console.WriteLine("Done");
}
```

This deadlock *does not happen* in console apps or ASP.NET Core, because there's no `SynchronizationContext` — continuations just run on ThreadPool threads, so there's no single thread everyone is fighting over. But in UI apps and classic ASP.NET, it's a constant trap.

> **Key takeaway:** SynchronizationContext determines *where* continuations run after an `await`. It's why UI code "just works" after an await — and it's why deadlocks happen when you block on async code from a context-bound thread.

---

## 8. `ConfigureAwait(false)` — what it does and when to use it

We've just seen that `SynchronizationContext` capture causes deadlocks when you block on async code from a context-bound thread. The natural question is: can we opt out? That's exactly what `ConfigureAwait(false)` does.

`ConfigureAwait(false)` tells the infrastructure: **"I don't need to come back to the original SynchronizationContext. Run my continuation wherever is most convenient."** In practice, that means the continuation always runs on a ThreadPool thread, regardless of what context was active before the `await`.

Mechanically, `Task.ConfigureAwait(false)` doesn't return a `Task`. It returns a `ConfiguredTaskAwaitable` — a different struct whose awaiter's `OnCompleted` implementation *deliberately ignores* the current `SynchronizationContext` and schedules the continuation directly to the ThreadPool.

```csharp
async Task WithContextCapture()
{
    Console.WriteLine($"Before: thread {Environment.CurrentManagedThreadId}");
    await Task.Delay(1000);
    // Continuation runs on the captured SynchronizationContext:
    // - In a UI app: UI thread
    // - In a console app: ThreadPool thread
    Console.WriteLine($"After: thread {Environment.CurrentManagedThreadId}");
}

async Task WithoutContextCapture()
{
    Console.WriteLine($"Before: thread {Environment.CurrentManagedThreadId}");
    await Task.Delay(1000).ConfigureAwait(false);
    // Continuation ALWAYS runs on a ThreadPool thread,
    // even if a SynchronizationContext was present.
    Console.WriteLine($"After: thread {Environment.CurrentManagedThreadId}");
}
```

**Use `ConfigureAwait(false)` in library code.** If you're writing a NuGet package, a shared utility, or any code that doesn't know whether it'll be called from a UI app, a console app, or ASP.NET — use `ConfigureAwait(false)` on every `await`. It prevents the deadlock we just described and avoids the overhead of posting to a `SynchronizationContext` you don't need.

**Don't use it in UI code** where you need to stay on the UI thread to update controls. And in **ASP.NET Core**, there is no `SynchronizationContext`, so it's largely a no-op — but it's still good practice in library code, because your library might be consumed in a WPF app someday.

You need it on *every* `await` in library code, not just the first one. Defensive coding means making the intent explicit on each await.

### Solving the deadlock with ConfigureAwait(false)

Let's revisit the classic deadlock and see how `ConfigureAwait(false)` fixes it:

```csharp
// Library code using ConfigureAwait(false)
async Task SomeLibraryMethodAsync()
{
    await Task.Delay(1000).ConfigureAwait(false);
    // This continuation runs on the ThreadPool, NOT the UI thread.
    Console.WriteLine("Done");
}

// UI code that (incorrectly) blocks on async
void ButtonClick()
{
    // This blocks the UI thread, but it's OK —
    // the library's continuation doesn't need the UI thread.
    var result = SomeLibraryMethodAsync().Result;  // No deadlock!
}
```

Without `ConfigureAwait(false)`, the continuation would try to post to the UI thread, which is blocked — deadlock. With it, the continuation runs on a ThreadPool thread, completes the Task, and `.Result` unblocks. The caller should still use `await` instead of `.Result`, but `ConfigureAwait(false)` makes your library resilient to that mistake.

> **Key takeaway:** SynchronizationContext determines where continuations run. `ConfigureAwait(false)` opts out of that capture, directing the continuation to the ThreadPool. Use it in library code to prevent deadlocks and improve performance. Don't use it in UI code where you need to stay on the UI thread.

---

## 9. ExecutionContext & AsyncLocal — how state flows across awaits

We now know that after an `await`, your code might resume on a completely different thread. Before the await you were on thread 5; after it, you're on thread 11. The thread changed, but your code keeps running as if nothing happened.

But here's a subtle question: what about *ambient state*? Things like the current culture, the security principal, your own per-request correlation IDs — data that's logically associated with "this flow of execution" rather than with a specific thread. If the thread changes, how does that data survive?

The answer is **ExecutionContext**.

`ExecutionContext` is an opaque container maintained by the runtime. It holds all the ambient data associated with the current flow of execution. When the async infrastructure suspends your code at an `await`, it captures the current `ExecutionContext`. When the continuation resumes — possibly on a different thread — the captured `ExecutionContext` is restored before your code runs.

### AsyncLocal\<T\>

If you need to carry ambient data through an async call chain, `AsyncLocal<T>` is the tool for the job. Think of it as a "logical thread-local" — a value that's associated with the current *async flow*, not with any particular physical thread.

```csharp
static AsyncLocal<string> _currentUser = new AsyncLocal<string>();

async Task DemoAsyncLocal()
{
    _currentUser.Value = "Alice";
    Console.WriteLine($"Before await: {_currentUser.Value}");  // Alice

    await Task.Delay(500);

    // Even though we might be on a completely different thread now,
    // the ExecutionContext was captured and restored, so:
    Console.WriteLine($"After await: {_currentUser.Value}");   // Alice
}
```

The value persists across the `await` because `ExecutionContext` was captured (carrying the `AsyncLocal` data) and restored on the other side. It doesn't matter that the thread changed — the *logical context* was preserved.

### Copy-on-write isolation

Here's where it gets interesting. When you fork execution — say, by starting a `Task.Run` — the child flow gets a *copy* of the parent's `ExecutionContext`. Modifications in the child don't affect the parent:

```csharp
static AsyncLocal<string> _currentUser = new AsyncLocal<string>();

async Task DemoIsolation()
{
    _currentUser.Value = "Alice";
    Console.WriteLine($"Parent before: {_currentUser.Value}");  // Alice

    await Task.Run(() =>
    {
        // Child inherits the value:
        Console.WriteLine($"Child sees: {_currentUser.Value}");  // Alice

        // Child modifies it — only affects the child's copy:
        _currentUser.Value = "Bob";
        Console.WriteLine($"Child changed to: {_currentUser.Value}");  // Bob
    });

    // Parent is unaffected:
    Console.WriteLine($"Parent after: {_currentUser.Value}");   // Alice
}
```

This is copy-on-write semantics. The child flow got its own snapshot of the `ExecutionContext`. When it wrote "Bob," it only modified its own copy. The parent's context was never touched.

This is the right behaviour for things like per-request correlation IDs: you want every task spawned during a request to inherit the ID, but you don't want a child task accidentally overwriting the parent's state.

### Why not ThreadLocal\<T\>?

You might be tempted to use `ThreadLocal<T>` for per-request state. Don't. `ThreadLocal<T>` is tied to the physical thread, not the logical async flow. After an `await`, you might resume on a different thread — and your `ThreadLocal` value will be gone (or worse, you'll see someone *else's* value, left over from whatever that thread was doing before).

```csharp
static ThreadLocal<string> _badIdea = new ThreadLocal<string>();

async Task DemoBrokenThreadLocal()
{
    _badIdea.Value = "Alice";
    Console.WriteLine($"Before: {_badIdea.Value}");  // Alice

    await Task.Delay(500);

    // DANGER: We might be on a different thread now.
    // _badIdea.Value could be null, or "Bob", or anything —
    // it belongs to whatever thread we're running on now.
    Console.WriteLine($"After: {_badIdea.Value}");   // ??? Unpredictable!
}
```

`AsyncLocal<T>` always works correctly across `await` boundaries. `ThreadLocal<T>` is a trap in async code.

> **Key takeaway:** ExecutionContext flows automatically across awaits, preserving ambient state even when threads change. Use `AsyncLocal<T>` for per-async-flow state; never `ThreadLocal<T>` in async code.

---

## 10. What is an Awaitable? What is an Awaiter?

So far, every time we've used `await`, we've awaited a `Task`. You might think `await` and `Task` are inseparable. They're not.

Here's a fact that surprises people: the `await` keyword doesn't know about `Task`. It doesn't require `Task`. It doesn't require any specific type at all. Instead, `await` is **pattern-based**. It relies on the **awaitable pattern** — a set of methods and properties that any type can implement.

### The pattern

An object is *awaitable* if it has a `GetAwaiter()` method. That method can be an instance method or an extension method — the compiler doesn't care. It just needs to return an **awaiter**.

An *awaiter* is any object (struct or class) that provides three things:

1. **`IsCompleted`** — a `bool` property. Has the operation already finished? This enables a performance optimisation: if the answer is `true`, the code after the `await` can run immediately without any suspension, callback registration, or thread switching.
2. **`OnCompleted(Action continuation)`** — a method that registers a callback. "When you're done, call this." The awaiter stores the callback and invokes it when the operation completes. (There's also `UnsafeOnCompleted`, which works the same way but lets the caller manage `ExecutionContext` flow separately — used for performance in the builder infrastructure we'll see in the next section.)
3. **`GetResult()`** — a method that returns the result of the operation (or `void` if there is no result). If the operation faulted, this is where the exception is rethrown.

That's it. Any type that has `GetAwaiter()` returning something with these three members can be awaited. `Task` happens to implement this pattern, but it's not special.

### A custom awaitable

Let's prove it by building our own. This is deliberately minimal — just enough to show the pattern works:

```csharp
struct MyDelayAwaitable
{
    private readonly int _milliseconds;

    public MyDelayAwaitable(int milliseconds) => _milliseconds = milliseconds;

    // This is the method the compiler looks for.
    // It returns our custom awaiter.
    public MyDelayAwaiter GetAwaiter() => new MyDelayAwaiter(_milliseconds);
}

struct MyDelayAwaiter : System.Runtime.CompilerServices.INotifyCompletion
{
    private readonly Task _innerTask;

    public MyDelayAwaiter(int milliseconds) => _innerTask = Task.Delay(milliseconds);

    // Has the inner operation already completed?
    public bool IsCompleted => _innerTask.IsCompleted;

    // Register the continuation callback.
    public void OnCompleted(Action continuation) =>
        _innerTask.GetAwaiter().OnCompleted(continuation);

    // Return the result (or rethrow the exception).
    public void GetResult() => _innerTask.GetAwaiter().GetResult();
}
```

And now we can `await` it:

```csharp
async Task UseCustomAwaitable()
{
    Console.WriteLine("Before");
    await new MyDelayAwaitable(1000);  // Compiles and works!
    Console.WriteLine("After");
}
```

The compiler sees `await expr`, looks for `expr.GetAwaiter()`, and generates code against the `IsCompleted`, `OnCompleted`, and `GetResult` members of the returned awaiter. It doesn't care that `MyDelayAwaitable` isn't `Task`. It just needs the pattern.

### Why this matters

This extensibility is how several important types in .NET work. `Task` and `Task<T>` implement the pattern, obviously. But so does `ValueTask` and `ValueTask<T>` — a more allocation-efficient alternative for operations that often complete synchronously. And as we saw in Section 8, `ConfigureAwait(false)` works by returning a *different* wrapper type whose awaiter implements the pattern in a slightly different way — specifically, by skipping the SynchronizationContext capture. Same pattern, different behavior.

You can even make `await` work on completely custom types — timers, channel reads, game frame ticks, whatever makes sense for your domain. The pattern is the contract; the compiler doesn't care about the specific type.

> **Key takeaway:** `await` is pattern-based, not type-based. You can await anything that has a `GetAwaiter()` returning an object with `IsCompleted`, `OnCompleted`, and `GetResult`. `Task` is just the most common implementation.

---

## 11. What does `await` really do? — The State Machine deep dive

This is the heart of the talk. Everything we've covered — Tasks, the ThreadPool, SynchronizationContext, ExecutionContext, awaiters — comes together in what the compiler does when it sees the `async` keyword.

The punchline: the compiler **rewrites your entire method** into a state machine. Your clean, readable, sequential code becomes a struct with a `MoveNext()` method containing a switch statement. Each `await` is a suspension point — a place where the method can pause and later resume.

Let's see it for real.

### The original method

```csharp
async Task ExampleAsync()
{
    Console.WriteLine("Step 1");
    await Task.Delay(1000);
    Console.WriteLine("Step 2");
    await Task.Delay(500);
    Console.WriteLine("Step 3");
}
```

Three `Console.WriteLine` calls. Two `await` points. This method has three segments of code — the parts between the suspension points:
- Segment A: `Console.WriteLine("Step 1")` and starting `Task.Delay(1000)`
- Segment B: `Console.WriteLine("Step 2")` and starting `Task.Delay(500)`
- Segment C: `Console.WriteLine("Step 3")`

The state machine's job is to execute one segment at a time, suspend if the awaited operation isn't finished yet, and resume at the right segment when it is.

### What the compiler does

The compiler:
1. Creates a **struct** that implements `IAsyncStateMachine`.
2. Moves all your method's local variables into **fields** on that struct (so they survive across suspension points — they can't live on the stack, because the stack frame is gone when the method returns).
3. Adds a **state field** (`<>1__state`) that tracks which segment to execute next.
4. Adds an **`AsyncTaskMethodBuilder`** field — the infrastructure piece that creates the caller-visible `Task`, sets its result or exception, and coordinates with `SynchronizationContext` and `ExecutionContext`.
5. Adds **awaiter fields** — one per `await` expression — to hold the awaiter while suspended.
6. Puts all your code into a **`MoveNext()`** method, rewritten as a switch on the state field.
7. Replaces your original method with a **thin stub** that creates the state machine and starts it.

### The Generated State Machine

Here's a cleaned-up but structurally accurate version of what the Roslyn compiler produces. This is *real generated code*, simplified for readability but preserving the actual structure. Read it carefully — the comments explain every piece.

```csharp
// The compiler generates a struct implementing IAsyncStateMachine.
// Your method's local variables become fields on this struct.
struct ExampleAsyncStateMachine : IAsyncStateMachine
{
    // ─── State tracking ───────────────────────────────────────────
    // Current state of the machine:
    //  -1 = not yet started, or currently running a segment
    //   0 = suspended, waiting for the first Task.Delay(1000) to complete
    //   1 = suspended, waiting for the second Task.Delay(500) to complete
    //  -2 = completed (finished successfully or faulted)
    public int <>1__state;

    // ─── The builder ──────────────────────────────────────────────
    // AsyncTaskMethodBuilder serves several roles:
    //   - It creates the Task object that the caller receives
    //   - It calls SetResult() or SetException() on that Task when the method ends
    //   - It handles SynchronizationContext and ExecutionContext capture
    //   - It calls AwaitUnsafeOnCompleted to register continuations
    public AsyncTaskMethodBuilder <>t__builder;

    // ─── Awaiter storage ──────────────────────────────────────────
    // One field per await expression. These store the awaiter while the
    // state machine is suspended, so that when MoveNext is called again,
    // it can retrieve the awaiter and call GetResult() on it.
    private TaskAwaiter <>u__1;  // awaiter for: await Task.Delay(1000)
    private TaskAwaiter <>u__2;  // awaiter for: await Task.Delay(500)

    // ─── MoveNext: the core of the state machine ─────────────────
    // ALL of your original method's logic lives here, rewritten as a
    // switch/goto structure that can be entered and exited at each await.
    public void MoveNext()
    {
        int num = <>1__state;
        try
        {
            TaskAwaiter awaiter;

            switch (num)
            {
                default:  // state == -1: first entry, start from the top
                    // ── Your original code ──
                    Console.WriteLine("Step 1");

                    // ── await Task.Delay(1000) ──
                    // Step 1: get the awaiter from the Task
                    awaiter = Task.Delay(1000).GetAwaiter();

                    // Step 2: check IsCompleted (the "fast path")
                    // If the Task completed synchronously (e.g., cached result,
                    // Task.Delay(0)), skip suspending entirely — huge perf win.
                    if (!awaiter.IsCompleted)
                    {
                        // Not done yet — we need to suspend.
                        <>1__state = 0;    // ← Record that we're paused at await #1
                        <>u__1 = awaiter;  // ← Stash the awaiter for later

                        // Step 3: register MoveNext as the continuation.
                        // The builder handles:
                        //   - Capturing SynchronizationContext.Current (Section 7)
                        //   - Capturing ExecutionContext (Section 9)
                        //   - Wrapping MoveNext in a callback
                        //   - Passing that callback to awaiter.UnsafeOnCompleted()
                        <>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);

                        return;  // ← EXIT MoveNext. The method is now "paused."
                                 //   The thread is released. The caller gets back
                                 //   an incomplete Task.
                    }
                    // If we get here, the Task was already complete — skip suspend.
                    goto AfterFirstAwait;

                case 0:  // ── Resuming after first await ──
                    // MoveNext was called again because Task.Delay(1000) completed.
                    awaiter = <>u__1;      // ← Retrieve the stashed awaiter
                    <>u__1 = default;      // ← Clear the field (avoid holding references)
                    <>1__state = -1;       // ← Back to "running" state
                    goto AfterFirstAwait;

                case 1:  // ── Resuming after second await ──
                    awaiter = <>u__2;
                    <>u__2 = default;
                    <>1__state = -1;
                    goto AfterSecondAwait;
            }

        AfterFirstAwait:
            // Step 4: get the result (or rethrow the exception).
            // If Task.Delay(1000) faulted, GetResult() throws here,
            // and the catch block below handles it.
            awaiter.GetResult();

            // ── Your original code ──
            Console.WriteLine("Step 2");

            // ── await Task.Delay(500) ──
            awaiter = Task.Delay(500).GetAwaiter();

            if (!awaiter.IsCompleted)  // ← Same fast-path check
            {
                <>1__state = 1;
                <>u__2 = awaiter;
                <>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
                return;  // ← "Paused" at the second await point.
            }

        AfterSecondAwait:
            awaiter.GetResult();

            // ── Your original code ──
            Console.WriteLine("Step 3");
        }
        catch (Exception ex)
        {
            // If ANY exception occurs in your code or in GetResult(),
            // it's caught here and set on the outer Task.
            <>1__state = -2;                   // ← Terminal state
            <>t__builder.SetException(ex);     // ← Faults the Task the caller holds
            return;
        }

        // Happy path: method completed normally.
        <>1__state = -2;                       // ← Terminal state
        <>t__builder.SetResult();              // ← Completes the Task the caller holds
    }

    // Required by the interface. Used by the builder for boxing scenarios.
    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        <>t__builder.SetStateMachine(stateMachine);
    }
}
```

### The stub method

Your original `ExampleAsync` method is replaced with this thin stub:

```csharp
Task ExampleAsync()
{
    var stateMachine = new ExampleAsyncStateMachine();
    stateMachine.<>1__state = -1;                              // Initial state
    stateMachine.<>t__builder = AsyncTaskMethodBuilder.Create();
    stateMachine.<>t__builder.Start(ref stateMachine);         // ← Calls MoveNext() immediately
    return stateMachine.<>t__builder.Task;                     // ← Returns the promise Task
}
```

Notice: `Start` calls `MoveNext()` *synchronously* on the calling thread. The first segment of your code (up to the first `await` that isn't already complete) runs on the calling thread, not on a ThreadPool thread. This is an important detail — `async` methods begin executing synchronously. The method only "becomes asynchronous" at the point where it actually suspends.

### Step-by-step walkthrough

Let's trace execution from start to finish.

**Call 1 — `ExampleAsync()` is invoked by the caller:**

The stub creates the state machine with state = -1, creates the builder, and calls `Start`, which calls `MoveNext()`.

Inside `MoveNext()`: state is -1, so we hit the `default` branch.
- Executes `Console.WriteLine("Step 1")` — prints **"Step 1"**.
- Calls `Task.Delay(1000)` — creates a timer; returns a Task.
- Calls `.GetAwaiter()` on that Task — gets a `TaskAwaiter`.
- Checks `awaiter.IsCompleted` → **false** (the timer hasn't fired yet — 1000ms is an eternity in CPU time).
- Sets `<>1__state = 0` and stashes the awaiter in `<>u__1`.
- Calls `<>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)`:
  - The builder captures `SynchronizationContext.Current` and `ExecutionContext`.
  - It registers a callback with the awaiter: "when this Task completes, call `MoveNext()` again."
- **Returns** from `MoveNext()`. Control flows back through `Start`, back to the stub, and the stub returns `<>t__builder.Task` to the caller. That Task is **not yet completed** — it's a live promise.

**The calling thread is now free.** It can return to the ThreadPool, or in a UI app, go back to pumping messages. No thread is blocked. A system timer is ticking.

**~1000ms pass. The timer fires.**

The OS timer callback runs. The `TaskAwaiter` invokes the registered continuation. The builder's machinery kicks in:
- It restores the captured `ExecutionContext`.
- If there was a captured `SynchronizationContext`, it calls `Post` to schedule `MoveNext()` on that context (e.g., the UI thread). If not, it queues `MoveNext()` to the ThreadPool.

**Call 2 — `MoveNext()` runs again (on a ThreadPool thread or the captured context):**

State is **0**, so we hit `case 0`.
- Retrieves the stashed awaiter from `<>u__1`, clears the field, sets state back to -1 (running).
- Jumps to `AfterFirstAwait`.
- Calls `awaiter.GetResult()` — the Task completed successfully, so this just returns. (If the Task had faulted, `GetResult()` would throw, and the catch block would set the exception on the outer Task.)
- Executes `Console.WriteLine("Step 2")` — prints **"Step 2"**.
- Calls `Task.Delay(500).GetAwaiter()`.
- Checks `IsCompleted` → **false**.
- Sets state = **1**, stashes the awaiter in `<>u__2`.
- Calls `AwaitUnsafeOnCompleted` again — registers `MoveNext` as the continuation.
- **Returns**. Thread is free again.

**~500ms pass. The second timer fires.**

Same process: continuation is dispatched to the appropriate context or ThreadPool.

**Call 3 — `MoveNext()` runs a final time:**

State is **1**, so we hit `case 1`.
- Retrieves the awaiter, clears the field, resets state to -1.
- Jumps to `AfterSecondAwait`.
- `awaiter.GetResult()` — success, returns.
- Executes `Console.WriteLine("Step 3")` — prints **"Step 3"**.
- Falls out of the try block into the success epilogue.
- Sets state to **-2** (completed/terminal).
- Calls `<>t__builder.SetResult()` — the outer Task that the caller has been holding is now **completed**.

That's the entire lifecycle. Three calls to `MoveNext()`. Three segments of code. Two suspension points. Zero threads blocked during the waits.

### AsyncTaskMethodBuilder — the unsung hero

The builder deserves a closer look. `AsyncTaskMethodBuilder` (and its generic sibling `AsyncTaskMethodBuilder<T>`) is responsible for:

- **Creating the outer Task** that the caller awaits. The builder lazily creates this Task and returns it from the stub method.
- **Managing context capture**: it captures `SynchronizationContext.Current` and `ExecutionContext` when suspending, and restores/posts to them when resuming.
- **Setting the result or exception**: when `MoveNext()` falls through the try block, the builder calls `SetResult()`. When an exception is caught, it calls `SetException()`. This is what transitions the outer Task to its final state.
- **Optimising allocation**: the state machine starts as a struct on the stack. If it needs to suspend (i.e., the first `await` is truly asynchronous), the builder **boxes** the struct onto the heap so it can survive across callbacks. If the method completes synchronously (all awaits hit the fast path), no heap allocation happens at all — not even for the Task, because the builder returns a cached completed Task. This is a significant performance optimisation.

### The struct-to-heap boxing story

This is worth dwelling on, because it's one of the cleverest optimisations in the async machinery.

The state machine is declared as a **struct**, not a class. This means it starts on the stack — no heap allocation. If the async method runs to completion without ever truly suspending (because every `await` hit the `IsCompleted` fast path), the struct lives and dies on the stack. Zero allocations.

But if the method *does* need to suspend — because some `await` is truly asynchronous — the struct needs to survive beyond the current stack frame. The builder detects this on the first suspension and boxes the struct onto the heap. From that point on, subsequent `MoveNext()` calls operate on the heap-allocated copy.

In the best case (synchronous completion), an `async` method allocates nothing. In the typical case (one or more true suspensions), it allocates one object: the boxed state machine. Compare that to the `ContinueWith` approach from Section 3, which would allocate closures, delegates, and Task objects at each step. The compiler-generated approach is far more efficient.

### The fast path — `IsCompleted`

Those `if (!awaiter.IsCompleted)` checks are not just defensive code — they're a critical performance optimisation.

If the awaited Task has already completed by the time you check (which is more common than you'd think — cached HTTP responses, buffered stream reads, `Task.Delay(0)`, results already computed), the state machine doesn't bother suspending. It skips the state assignment, the awaiter stash, the context capture, the continuation registration — all of it. It just falls through to the goto label and continues execution synchronously. No thread switch, no callback overhead, no heap allocation.

This means that an `async` method where every `await` completes synchronously runs at nearly the same speed as the equivalent non-async code. The overhead is just the state field checks and a few struct field assignments.

> **Key takeaway:** `async`/`await` is a compiler rewrite into a state machine. Your method is chopped into segments at each `await` point, and `MoveNext()` hopscotches through them. The builder handles context capture, the outer Task, and allocation optimisation. No magic — just a struct, a switch, and some callbacks.

---

## 12. `ValueTask<T>` — reducing allocations on hot paths

We've seen that `Task<T>` is a class — a heap-allocated object. Every time an `async Task<T>` method truly suspends, at minimum one allocation happens (the boxed state machine). And every time a method completes asynchronously, a `Task<T>` object is created to carry the result.

For most code, this is fine. But on *hot paths* — methods called thousands of times per second, like socket reads, cache lookups, or stream operations — those allocations add up. The garbage collector has to clean them all up, and GC pressure can measurably affect throughput.

Enter `ValueTask<T>`.

### What it is

`ValueTask<T>` is a **struct** that can represent either:
- A **bare `T` result** (synchronous completion — zero allocations), or
- A **wrapped `Task<T>`** (asynchronous completion — same allocation as before)

```csharp
// Task<T>: always allocates a Task object, even if the result is known immediately
async Task<int> GetValueAlways()
{
    return 42;  // Allocates a Task<int> to carry the result
}

// ValueTask<T>: struct — no allocation if the result is ready
async ValueTask<int> GetValueFast()
{
    if (_cache.TryGetValue(key, out var cached))
        return cached;  // No suspension, no Task, zero allocations

    return await FetchFromDatabaseAsync();  // Suspends — allocates one Task
}
```

The win is on the synchronous-completion path. If the result is already available — a cache hit, a buffered read, an already-computed value — `ValueTask<T>` returns it without touching the heap. This is the same `IsCompleted` fast path the state machine uses, but taken one step further: not only does the state machine avoid suspending, the *return type itself* avoids allocation.

### Where it's used

`ValueTask<T>` is used throughout .NET's performance-critical internals:

- `Stream.ReadAsync` returns `ValueTask<int>`
- `Socket.ReceiveAsync` returns `ValueTask<int>`
- `PipeReader.ReadAsync` returns `ValueTask<ReadResult>`
- `HttpClient` uses it internally for connection pooling
- `IAsyncEnumerator<T>.MoveNextAsync()` returns `ValueTask<bool>`

These are all methods that complete synchronously more often than not (data is already buffered, the socket has data ready, the pipe has bytes available), so the allocation savings are significant at scale.

### Usage rules — stricter than Task

`ValueTask<T>` has important restrictions that `Task<T>` does not:

1. **Await it exactly once.** Don't store it in a variable and await it later in multiple places. Don't pass it to `Task.WhenAll`. Once you've consumed the result, it's done.
2. **Don't await it concurrently.** Two threads awaiting the same `ValueTask<T>` is undefined behavior.
3. **Don't call `.Result` or `.GetAwaiter().GetResult()` unless `IsCompleted` is true.** On a non-completed `ValueTask`, this can corrupt internal state.
4. **Don't await it more than once.** After you've awaited and consumed the result, the underlying `IValueTaskSource` may have already been recycled for another operation.

```csharp
// ✅ CORRECT — await it directly
int result = await GetValueAsync();

// ❌ WRONG — storing and awaiting twice
var vt = GetValueAsync();
int a = await vt;
int b = await vt;  // BROKEN — may have been recycled

// ❌ WRONG — concurrent awaits
var vt = GetValueAsync();
var t1 = ConsumeAsync(vt);
var t2 = ConsumeAsync(vt);  // Undefined behavior
await Task.WhenAll(t1, t2);
```

If you need `Task<T>` behavior (caching, multiple awaits, passing to `WhenAll`), call `.AsTask()` to convert — but that allocates a `Task`, defeating the purpose.

### IValueTaskSource — the zero-allocation endgame

For the ultimate in allocation avoidance, .NET provides `IValueTaskSource<T>`. Instead of falling back to a `Task<T>` on suspension, you can implement a *poolable* completion source that gets reused across operations:

```csharp
// Conceptually: a pool of reusable IValueTaskSource instances
// Each async operation borrows one, completes it, and returns it to the pool
// The next operation reuses the same object — zero allocations per operation
```

This is what `Socket`, `PipeReader`, and other high-throughput .NET internals use. It's complex to implement correctly, but it achieves near-zero allocation even on the asynchronous path.

### When to use ValueTask vs Task

**Use `Task<T>`** by default. It's simpler, has no usage restrictions, and is perfectly fine for the vast majority of code.

**Use `ValueTask<T>`** when:
- The method is on a proven hot path (profiling shows allocation pressure)
- The method frequently completes synchronously (cache hits, buffered data)
- You're writing a library or framework where callers will call this thousands of times per second

**Don't** switch to `ValueTask<T>` speculatively. Profile first. The allocation savings only matter where GC pressure is measurable.

> **Key takeaway:** `ValueTask<T>` is a struct that avoids allocating a Task when the result is already available. Use it on hot paths where synchronous completion is common. It has stricter usage rules than Task: await once, don't cache, no concurrent awaits. Profile before switching — it's a performance tool, not a default choice.

---

## 13. Closing — Recap & mental model

Let's put the whole picture together.

When you write `async Task MyMethod()`, the compiler doesn't compile your method as written. It generates a state machine — a struct with a `MoveNext()` method that contains all of your code, rewritten as a switch statement. Each `await` is a potential suspension point.

When execution reaches an `await`, the state machine asks the awaiter: "Are you done?" If yes — the fast path — it keeps running without pausing. If not, it records its current state, registers `MoveNext()` as a callback on the awaiter, and *returns* — freeing the thread.

When the awaited operation completes (a timer fires, I/O finishes, a promise is fulfilled), the registered callback kicks in. The `AsyncTaskMethodBuilder` restores the captured `ExecutionContext`, and either posts `MoveNext()` to the captured `SynchronizationContext` (in a UI app, that's the UI thread) or queues it to the ThreadPool (in console apps and ASP.NET Core). Your code resumes in `MoveNext()` at the right state, as if nothing happened.

The `Task` returned to the caller is just a promise — a handle on the outcome of the whole operation. When `MoveNext()` falls through to the end, the builder calls `SetResult()` on that Task, and the caller's own `await` can now resume.

Every piece has a job:

> - **Why async exists (Section 2):** to free threads during waits, not for parallelism.
> - **Before `await` (Section 3):** callback hell with `ContinueWith` — same mechanics, terrible ergonomics.
> - **`Task` & `TaskCompletionSource` (Section 4):** a promise (data structure), not a thread or a unit of execution. TCS bridges callbacks to async/await.
> - **`CancellationToken` (Section 5):** cooperative cancellation — pass the token everywhere, check it at every boundary.
> - **ThreadPool (Section 6):** the default engine that runs work items and continuations; can be starved by blocking.
> - **SynchronizationContext (Section 7):** decides *where* continuations run — UI thread, ThreadPool, etc. Source of deadlocks when blocked.
> - **`ConfigureAwait(false)` (Section 8):** opt out of SynchronizationContext capture — essential in library code, skip in UI code.
> - **ExecutionContext & AsyncLocal (Section 9):** how ambient state (correlation IDs, culture, etc.) flows across await-induced thread hops.
> - **Awaitable pattern (Section 10):** `await` works on anything with `GetAwaiter()` — it's pattern-based, not hardwired to `Task`.
> - **State machine (Section 11):** the compiler rewrites your method into a struct with `MoveNext()`, a state field, and a builder.
> - **`ValueTask` (Section 12):** struct-based promise that avoids allocations on the synchronous-completion fast path.

You started this talk knowing that `async`/`await` is something you put on methods because you're told you must. Now you know *what* it does, *why* it exists, and *how* the compiler and runtime conspire to make it work. The next time you see a deadlock, a ThreadPool starvation issue, or a mysterious context bug, you'll have the mental model to diagnose it.

Now go open SharpLab, paste an async method, and look at the generated code. You'll never look at `await` the same way again.
