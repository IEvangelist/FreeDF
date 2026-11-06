```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method     | Objects | StreamValues | Mean         | Error        | StdDev     | Gen0     | Gen1     | Gen2     | Allocated   |
|----------- |-------- |------------- |-------------:|-------------:|-----------:|---------:|---------:|---------:|------------:|
| **LoadValues** | **64**      | **False**        |     **42.81 μs** |     **2.564 μs** |   **1.138 μs** |  **10.5541** |   **2.1438** |        **-** |   **178.86 KB** |
| **LoadValues** | **64**      | **True**         |     **52.06 μs** |     **1.373 μs** |   **0.610 μs** |  **12.3153** |   **2.8736** |        **-** |   **208.86 KB** |
| **LoadValues** | **4096**    | **False**        | **10,898.58 μs** | **1,110.366 μs** | **580.743 μs** | **750.0000** | **729.1667** | **270.8333** | **11371.17 KB** |
| **LoadValues** | **4096**    | **True**         | **14,092.70 μs** | **1,088.522 μs** | **569.318 μs** | **895.8333** | **645.8333** | **250.0000** | **13291.18 KB** |
