```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1  
WarmupCount=3  

```
| Method      | FilterCount | IndirectParameters | Mean     | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------------ |------------ |------------------- |---------:|----------:|----------:|-------:|-------:|----------:|
| **FreshMember** | **1**           | **False**              | **7.548 μs** | **0.2389 μs** | **0.1249 μs** | **1.1389** | **0.0154** |  **19.24 KB** |
| **FreshMember** | **1**           | **True**               | **7.510 μs** | **0.3130 μs** | **0.1637 μs** | **1.1137** | **0.0148** |  **18.87 KB** |
| **FreshMember** | **2**           | **False**              | **8.279 μs** | **0.8002 μs** | **0.4185 μs** | **1.2350** | **0.0163** |  **21.04 KB** |
| **FreshMember** | **2**           | **True**               | **9.211 μs** | **1.2502 μs** | **0.5551 μs** | **1.1965** | **0.0176** |  **20.44 KB** |
