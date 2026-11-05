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
| **FreshAllMembers** | **1**           | **False**     |  **11.19 μs** |  **0.187 μs** |  **0.067 μs** |   **1.0664** |       **-** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **40.22 μs** |  **1.192 μs** |  **0.529 μs** |   **2.6297** |       **-** |   **45.72 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **674.65 μs** | **12.282 μs** |  **6.424 μs** | **110.5769** | **43.2692** | **1908.91 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **748.65 μs** | **69.317 μs** | **36.254 μs** | **116.4773** | **53.9773** | **1972.75 KB** |
