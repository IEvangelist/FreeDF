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
| **FreshMember** | **1**           | **False**              | **7.655 μs** | **0.2891 μs** | **0.1512 μs** | **1.1339** | **0.0153** |  **19.24 KB** |
| **FreshMember** | **1**           | **True**               | **7.634 μs** | **0.3003 μs** | **0.1571 μs** | **1.1186** | **0.0155** |  **18.87 KB** |
| **FreshMember** | **2**           | **False**              | **8.618 μs** | **1.0173 μs** | **0.5321 μs** | **1.2375** | **0.0165** |  **21.04 KB** |
| **FreshMember** | **2**           | **True**               | **8.419 μs** | **0.5908 μs** | **0.2623 μs** | **1.2105** | **0.0159** |  **20.44 KB** |
