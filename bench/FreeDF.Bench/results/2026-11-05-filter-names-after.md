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
| **FreshAllMembers** | **1**           | **False**     |  **10.16 μs** |  **1.001 μs** |  **0.524 μs** |   **1.0680** |  **0.0205** |      **18 KB** |
| **FreshAllMembers** | **1**           | **True**      |  **36.32 μs** |  **0.686 μs** |  **0.305 μs** |   **2.6946** |  **0.0749** |   **45.73 KB** |
| **FreshAllMembers** | **1000**        | **False**     | **587.87 μs** | **35.670 μs** | **18.656 μs** | **114.3868** | **48.3491** | **1909.07 KB** |
| **FreshAllMembers** | **1000**        | **True**      | **623.94 μs** | **21.386 μs** | **11.185 μs** | **117.6471** | **50.2451** | **1972.93 KB** |
