```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1  
WarmupCount=3  

```
| Method          | MemberCount | Encrypted | Mean      | Error     | StdDev    | Gen0     | Gen1    | Allocated  |
|---------------- |------------ |---------- |----------:|----------:|----------:|---------:|--------:|-----------:|
| **FreshAllMembers** | **1**           | **False**     |  **10.42 μs** |  **0.313 μs** |  **0.163 μs** |   **1.0651** |  **0.0213** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **44.21 μs** |  **4.360 μs** |  **2.281 μs** |   **2.6361** |       **-** |   **45.72 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **769.10 μs** | **87.458 μs** | **45.742 μs** | **112.7717** | **47.5543** | **1908.91 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **722.53 μs** | **44.620 μs** | **19.811 μs** | **115.8537** | **53.3537** | **1972.75 KB** |
