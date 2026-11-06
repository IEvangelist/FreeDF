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
| **LoadValues** | **64**      | **False**        |     **44.02 μs** |   **2.138 μs** |   **1.118 μs** |  **10.6050** |   **2.2601** |        **-** |   **179.38 KB** |
| **LoadValues** | **64**      | **True**         |     **52.06 μs** |   **2.335 μs** |   **1.037 μs** |  **12.3773** |   **2.9664** |        **-** |   **209.88 KB** |
| **LoadValues** | **4096**    | **False**        |  **9,697.25 μs** | **576.247 μs** | **301.388 μs** | **703.1250** | **656.2500** | **296.8750** | **11403.21 KB** |
| **LoadValues** | **4096**    | **True**         | **12,154.84 μs** | **647.026 μs** | **338.407 μs** | **833.3333** | **604.1667** | **250.0000** | **13355.18 KB** |
