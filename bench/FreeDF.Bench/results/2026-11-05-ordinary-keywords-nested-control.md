```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method     | Objects | StreamValues | Mean         | Error      | StdDev     | Gen0     | Gen1     | Gen2     | Allocated   |
|----------- |-------- |------------- |-------------:|-----------:|-----------:|---------:|---------:|---------:|------------:|
| **LoadValues** | **64**      | **False**        |     **44.27 μs** |   **2.413 μs** |   **1.262 μs** |  **10.5743** |   **2.1492** |        **-** |   **178.86 KB** |
| **LoadValues** | **64**      | **True**         |     **51.95 μs** |   **1.733 μs** |   **0.907 μs** |  **12.2863** |   **2.6709** |        **-** |   **208.86 KB** |
| **LoadValues** | **4096**    | **False**        | **11,072.97 μs** | **844.614 μs** | **441.750 μs** | **750.0000** | **729.1667** | **270.8333** | **11371.17 KB** |
| **LoadValues** | **4096**    | **True**         | **14,066.03 μs** | **583.082 μs** | **304.963 μs** | **895.8333** | **645.8333** | **250.0000** | **13291.15 KB** |
