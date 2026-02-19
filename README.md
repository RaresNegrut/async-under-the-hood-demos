# Async Under the Hood — Live Demos (one mini-app per core concept)

These demos correspond to the core concepts in the deck:
- Why async exists (threads are expensive; async frees threads during waits)
- Pre-`await` TPL (`ContinueWith`) vs `async/await`
- `Task` as a promise + `TaskCompletionSource` bridge
- `CancellationToken` propagation
- ThreadPool starvation from blocking (`.Wait()` / `.Result`)
- `SynchronizationContext` and the classic deadlock
- `ConfigureAwait(false)` as the library-side escape hatch
- `ExecutionContext` flow + `AsyncLocal<T>` (and why `ThreadLocal<T>` breaks)
- The awaitable pattern (custom awaiter)
- Compiler state machine (reflection view)
- `ValueTask<T>` fast path (allocation story)

## Prerequisites
- Install the .NET SDK (8.0+ recommended). Run: `dotnet --version`

## Quick start
From this folder:

```bash
dotnet build AsyncUnderTheHood.Demos.sln
dotnet run --project 01_WhyAsync
dotnet run --project 06_SynchronizationContext_Deadlock
```

### Run everything (nice for rehearsals)
- Bash/macOS/Linux: `bash run-all.sh`
- PowerShell: `pwsh ./run-all.ps1`

## Notes for live presentation
- Every project prints a short, “stage-friendly” narrative: what it’s demonstrating + the key observation.
- Deadlock demos run on a background thread and auto-timeout so you never freeze your talk.
