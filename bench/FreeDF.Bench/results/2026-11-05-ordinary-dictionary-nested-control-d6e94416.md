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
| **LoadValues** | **64**      | **False**        |     **41.93 μs** |   **1.438 μs** |   **0.752 μs** |  **10.5680** |   **2.1466** |        **-** |   **178.86 KB** |
| **LoadValues** | **64**      | **True**         |     **52.18 μs** |   **2.490 μs** |   **1.302 μs** |  **12.3570** |   **2.8595** |        **-** |   **208.86 KB** |
| **LoadValues** | **4096**    | **False**        | **10,755.72 μs** | **654.979 μs** | **342.567 μs** | **750.0000** | **729.1667** | **270.8333** | **11371.17 KB** |
| **LoadValues** | **4096**    | **True**         | **15,422.98 μs** | **342.441 μs** | **152.046 μs** | **968.7500** | **718.7500** | **250.0000** | **13291.15 KB** |
