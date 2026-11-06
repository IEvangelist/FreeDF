```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method     | Objects | DictionaryValues | Mean         | Error       | StdDev     | Gen0     | Gen1     | Gen2     | Allocated  |
|----------- |-------- |----------------- |-------------:|------------:|-----------:|---------:|---------:|---------:|-----------:|
| **LoadValues** | **64**      | **False**            |     **8.306 μs** |   **0.5265 μs** |  **0.2754 μs** |   **1.5207** |   **0.0507** |        **-** |   **25.86 KB** |
| **LoadValues** | **64**      | **True**             |    **16.650 μs** |   **0.2711 μs** |  **0.1204 μs** |   **3.7749** |   **0.3611** |        **-** |   **63.86 KB** |
| **LoadValues** | **4096**    | **False**            |   **623.126 μs** |  **17.7894 μs** |  **9.3042 μs** | **186.2500** | **186.2500** | **186.2500** | **1579.11 KB** |
| **LoadValues** | **4096**    | **True**             | **2,499.328 μs** | **150.7849 μs** | **78.8634 μs** | **364.5833** | **239.5833** | **177.0833** | **4011.11 KB** |
