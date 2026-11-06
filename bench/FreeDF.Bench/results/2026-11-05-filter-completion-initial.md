```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1  
WarmupCount=3  

```
| Method          | MemberCount | Encrypted | Mean        | Error      | StdDev    | Gen0     | Gen1    | Allocated  |
|---------------- |------------ |---------- |------------:|-----------:|----------:|---------:|--------:|-----------:|
| **FreshAllMembers** | **1**           | **False**     |    **20.69 μs** |   **0.251 μs** |  **0.111 μs** |   **1.0534** |       **-** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |    **64.13 μs** |   **0.769 μs** |  **0.342 μs** |   **2.7098** |       **-** |   **45.72 KB** |
| **FreshAllMembers** | **1000**        | **False**     |   **869.73 μs** | **102.183 μs** | **53.444 μs** | **112.5000** | **43.7500** | **1908.91 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **1,305.34 μs** |  **15.995 μs** |  **8.366 μs** | **115.8088** | **53.3088** | **1972.75 KB** |
