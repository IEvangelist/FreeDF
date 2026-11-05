```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1  
WarmupCount=3  

```
| Method          | MemberCount | Encrypted | Mean      | Error      | StdDev    | Gen0     | Gen1    | Allocated  |
|---------------- |------------ |---------- |----------:|-----------:|----------:|---------:|--------:|-----------:|
| **FreshAllMembers** | **1**           | **False**     |  **11.06 μs** |   **0.748 μs** |  **0.391 μs** |   **1.0561** |  **0.0211** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **44.46 μs** |   **5.425 μs** |  **2.838 μs** |   **2.6751** |  **0.0811** |   **45.72 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **808.64 μs** | **108.710 μs** | **48.268 μs** | **107.9545** | **45.4545** | **1908.91 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **756.14 μs** |  **72.149 μs** | **32.035 μs** | **116.0714** | **53.5714** | **1972.75 KB** |
