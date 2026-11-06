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
| **FreshMember** | **1**           | **False**              | **7.922 μs** | **0.3735 μs** | **0.1954 μs** | **1.1352** | **0.0158** |  **19.24 KB** |
| **FreshMember** | **1**           | **True**               | **7.787 μs** | **0.2646 μs** | **0.1384 μs** | **1.1108** | **0.0156** |  **18.87 KB** |
| **FreshMember** | **2**           | **False**              | **8.823 μs** | **0.8421 μs** | **0.3739 μs** | **1.2440** | **0.0166** |  **21.04 KB** |
| **FreshMember** | **2**           | **True**               | **9.200 μs** | **0.4009 μs** | **0.1430 μs** | **1.1941** | **0.0213** |  **20.44 KB** |
