```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method    | Event  | Mean     | Error    | StdDev  | Gen0   | Allocated |
|---------- |------- |---------:|---------:|--------:|-------:|----------:|
| **IsVisible** | **Export** | **126.0 ns** |  **7.21 ns** | **3.77 ns** | **0.0082** |     **144 B** |
| **IsVisible** | **Print**  | **147.1 ns** | **13.32 ns** | **6.97 ns** | **0.0083** |     **144 B** |
| **IsVisible** | **View**   | **148.2 ns** | **14.87 ns** | **7.78 ns** | **0.0067** |     **120 B** |
