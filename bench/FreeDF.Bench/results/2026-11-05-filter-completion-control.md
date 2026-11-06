```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1  
WarmupCount=3  

```
| Method          | MemberCount | Encrypted | Mean      | Error      | StdDev     | Gen0     | Gen1    | Allocated  |
|---------------- |------------ |---------- |----------:|-----------:|-----------:|---------:|--------:|-----------:|
| **FreshAllMembers** | **1**           | **False**     |  **10.94 μs** |   **0.904 μs** |   **0.473 μs** |   **1.0547** |  **0.0211** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **40.20 μs** |   **1.661 μs** |   **0.737 μs** |   **2.6967** |  **0.0793** |   **45.72 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **700.80 μs** |  **49.904 μs** |  **26.101 μs** | **113.0319** | **47.8723** | **1908.91 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **834.62 μs** | **226.974 μs** | **118.712 μs** | **116.2791** | **53.7791** | **1972.75 KB** |
