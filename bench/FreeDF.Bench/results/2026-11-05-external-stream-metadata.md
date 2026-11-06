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
| **FreshMember** | **1**           | **False**              | **8.079 μs** | **0.6054 μs** | **0.2688 μs** | **1.1355** | **0.0156** |  **19.24 KB** |
| **FreshMember** | **1**           | **True**               | **8.392 μs** | **0.8072 μs** | **0.4222 μs** | **1.1050** | **0.0151** |  **18.87 KB** |
| **FreshMember** | **2**           | **False**              | **8.993 μs** | **0.9975 μs** | **0.5217 μs** | **1.2403** | **0.0180** |  **21.04 KB** |
| **FreshMember** | **2**           | **True**               | **8.536 μs** | **0.9885 μs** | **0.5170 μs** | **1.2120** | **0.0168** |  **20.44 KB** |
