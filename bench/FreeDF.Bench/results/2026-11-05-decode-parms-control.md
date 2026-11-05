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
| **FreshAllMembers** | **1**           | **False**     |  **12.71 μs** |  **1.999 μs** |  **1.046 μs** |   **1.0635** |       **-** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **40.42 μs** |  **1.544 μs** |  **0.686 μs** |   **2.6510** |  **0.0803** |   **45.72 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **654.46 μs** | **12.730 μs** |  **5.652 μs** | **112.7717** | **47.5543** | **1908.91 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **735.32 μs** | **91.467 μs** | **40.612 μs** | **116.0714** | **53.5714** | **1972.75 KB** |
