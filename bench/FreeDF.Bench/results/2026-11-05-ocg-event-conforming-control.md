```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method    | Event  | Mean     | Error   | StdDev  | Gen0   | Allocated |
|---------- |------- |---------:|--------:|--------:|-------:|----------:|
| **IsVisible** | **Export** | **132.7 ns** | **2.24 ns** | **1.17 ns** | **0.0082** |     **144 B** |
| **IsVisible** | **Print**  | **140.2 ns** | **1.45 ns** | **0.64 ns** | **0.0083** |     **144 B** |
| **IsVisible** | **View**   | **126.3 ns** | **3.14 ns** | **1.64 ns** | **0.0068** |     **120 B** |
