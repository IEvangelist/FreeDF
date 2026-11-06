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
| **LoadValues** | **64**      | **False**            |     **8.354 μs** |   **0.1118 μs** |  **0.0497 μs** |   **1.5257** |   **0.0532** |        **-** |   **25.86 KB** |
| **LoadValues** | **64**      | **True**             |    **16.475 μs** |   **0.3977 μs** |  **0.2080 μs** |   **3.7568** |   **0.3415** |        **-** |   **63.86 KB** |
| **LoadValues** | **4096**    | **False**            |   **652.307 μs** |  **28.4294 μs** | **14.8691 μs** | **186.2500** | **186.2500** | **186.2500** | **1579.11 KB** |
| **LoadValues** | **4096**    | **True**             | **2,504.824 μs** | **114.5658 μs** | **59.9201 μs** | **364.5833** | **239.5833** | **177.0833** | **4011.11 KB** |
