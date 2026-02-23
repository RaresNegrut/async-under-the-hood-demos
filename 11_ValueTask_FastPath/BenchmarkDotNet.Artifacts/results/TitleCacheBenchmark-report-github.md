```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7840/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 5800H with Radeon Graphics 3.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 8.0.24 (8.0.24, 8.0.2426.7010), X64 RyuJIT x86-64-v3
  Job-YFEFPZ : .NET 8.0.24 (8.0.24, 8.0.2426.7010), X64 RyuJIT x86-64-v3

IterationCount=10  WarmupCount=3  

```
| Method             | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Task_CacheHit      | 30.09 ns | 1.840 ns | 1.217 ns |  1.00 |    0.05 | 0.0172 |     144 B |        1.00 |
| ValueTask_CacheHit | 25.22 ns | 1.241 ns | 0.821 ns |  0.84 |    0.04 | 0.0086 |      72 B |        0.50 |
