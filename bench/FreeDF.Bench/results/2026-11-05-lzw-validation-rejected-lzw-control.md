```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method | Pattern  | Strict | Mean      | Error     | StdDev   | Gen0    | Allocated |
|------- |--------- |------- |----------:|----------:|---------:|--------:|----------:|
| **Decode** | **Literal**  | **False**  | **290.94 μs** | **17.484 μs** | **9.145 μs** | **10.9223** | **192.05 KB** |
| **Decode** | **Literal**  | **True**   | **304.85 μs** |  **8.674 μs** | **4.537 μs** | **11.0677** | **192.05 KB** |
| **Decode** | **Repeated** | **False**  |  **12.34 μs** |  **2.435 μs** | **1.081 μs** | **11.3616** | **192.05 KB** |
| **Decode** | **Repeated** | **True**   |  **11.96 μs** |  **0.404 μs** | **0.144 μs** | **11.3615** | **192.05 KB** |
