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
| **FreshMember** | **1**           | **False**              | **8.387 μs** | **0.6178 μs** | **0.2743 μs** | **1.1281** | **0.0157** |  **19.24 KB** |
| **FreshMember** | **1**           | **True**               | **8.195 μs** | **0.4520 μs** | **0.2364 μs** | **1.1129** | **0.0164** |  **18.87 KB** |
| **FreshMember** | **2**           | **False**              | **8.929 μs** | **0.8004 μs** | **0.4186 μs** | **1.2434** | **0.0166** |  **21.04 KB** |
| **FreshMember** | **2**           | **True**               | **8.149 μs** | **0.3191 μs** | **0.1417 μs** | **1.2037** | **0.0180** |  **20.44 KB** |
