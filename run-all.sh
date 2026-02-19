#!/usr/bin/env bash
set -euo pipefail
projects=(
  01_WhyAsync
  02_PreAwait_TPLContinueWith
  03_TaskPromise_TCS
  04_CancellationToken
  05_ThreadPool_Starvation
  06_SynchronizationContext_Deadlock
  07_ConfigureAwaitFalse
  08_ExecutionContext_AsyncLocal
  09_AwaitablePattern_CustomAwaiter
  10_StateMachine_Reflection
  11_ValueTask_FastPath
)
for p in "${projects[@]}"; do
  echo
  echo "============================================================"
  echo "RUNNING $p"
  echo "============================================================"
  dotnet run --project "$p" -c Release
done
