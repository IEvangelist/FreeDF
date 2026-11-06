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
| **LoadValues** | **64**      | **False**        |     **43.13 μs** |     **0.635 μs** |   **0.282 μs** |  **10.5743** |   **2.1492** |        **-** |   **178.86 KB** |
| **LoadValues** | **64**      | **True**         |     **56.86 μs** |     **2.800 μs** |   **1.465 μs** |  **12.3698** |   **2.8212** |        **-** |   **208.86 KB** |
| **LoadValues** | **4096**    | **False**        | **11,070.50 μs** | **1,115.824 μs** | **583.598 μs** | **750.0000** | **729.1667** | **270.8333** | **11371.17 KB** |
| **LoadValues** | **4096**    | **True**         | **14,069.39 μs** |   **894.324 μs** | **467.749 μs** | **895.8333** | **645.8333** | **250.0000** | **13291.17 KB** |
