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
| **LoadValues** | **64**      | **False**            |     **8.359 μs** |   **0.1479 μs** |  **0.0773 μs** |   **1.5314** |   **0.0510** |        **-** |   **25.86 KB** |
| **LoadValues** | **64**      | **True**             |    **16.686 μs** |   **0.2244 μs** |  **0.1174 μs** |   **3.7778** |   **0.3645** |        **-** |   **63.86 KB** |
| **LoadValues** | **4096**    | **False**            |   **634.120 μs** |  **27.1478 μs** | **14.1988 μs** | **186.2981** | **186.2981** | **186.2981** | **1579.11 KB** |
| **LoadValues** | **4096**    | **True**             | **2,563.380 μs** | **179.6789 μs** | **93.9756 μs** | **370.1923** | **245.1923** | **182.6923** | **4011.11 KB** |
