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
| **LoadValues** | **64**      | **False**        |     **42.68 μs** |   **1.144 μs** |   **0.599 μs** |  **10.5769** |   **2.2727** |        **-** |   **179.38 KB** |
| **LoadValues** | **64**      | **True**         |     **53.04 μs** |   **1.080 μs** |   **0.479 μs** |  **12.4373** |   **2.9264** |        **-** |   **209.88 KB** |
| **LoadValues** | **4096**    | **False**        |  **9,353.73 μs** | **585.942 μs** | **306.459 μs** | **687.5000** | **645.8333** | **291.6667** | **11403.21 KB** |
| **LoadValues** | **4096**    | **True**         | **11,860.24 μs** | **483.904 μs** | **253.091 μs** | **833.3333** | **604.1667** | **250.0000** | **13355.18 KB** |
