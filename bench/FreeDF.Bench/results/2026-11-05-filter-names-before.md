```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1  
WarmupCount=3  

```
| Method          | MemberCount | Encrypted | Mean       | Error      | StdDev     | Gen0     | Gen1    | Allocated  |
|---------------- |------------ |---------- |-----------:|-----------:|-----------:|---------:|--------:|-----------:|
| **FreshAllMembers** | **1**           | **False**     |   **9.640 μs** |  **0.1852 μs** |  **0.0822 μs** |   **1.0641** |  **0.0201** |   **17.94 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **37.180 μs** |  **0.3751 μs** |  **0.1665 μs** |   **2.6690** |  **0.0741** |   **45.67 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **580.829 μs** | **24.9691 μs** | **13.0593 μs** | **114.3868** | **49.5283** | **1908.39 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **633.013 μs** | **16.1169 μs** |  **8.4294 μs** | **115.3846** | **48.0769** | **1972.74 KB** |
