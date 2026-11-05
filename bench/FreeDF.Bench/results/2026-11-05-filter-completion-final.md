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
| **FreshAllMembers** | **1**           | **False**     |  **10.50 μs** |  **0.234 μs** |  **0.104 μs** |   **1.0511** |  **0.0210** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **45.11 μs** |  **4.733 μs** |  **2.476 μs** |   **2.6892** |       **-** |   **45.72 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **781.15 μs** | **65.700 μs** | **29.171 μs** | **112.5000** | **48.4375** | **1908.91 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **823.72 μs** | **79.041 μs** | **41.340 μs** | **116.7763** | **54.2763** | **1972.75 KB** |
