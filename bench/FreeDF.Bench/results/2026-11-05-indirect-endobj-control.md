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
| **LoadValues** | **64**      | **False**            |     **7.913 μs** |   **0.1974 μs** |  **0.1033 μs** |   **1.5342** |   **0.0484** |        **-** |   **25.86 KB** |
| **LoadValues** | **64**      | **True**             |    **15.869 μs** |   **0.2132 μs** |  **0.0947 μs** |   **3.7898** |   **0.3728** |        **-** |   **63.86 KB** |
| **LoadValues** | **4096**    | **False**            |   **597.317 μs** |  **12.1756 μs** |  **5.4060 μs** | **186.3208** | **186.3208** | **186.3208** | **1579.11 KB** |
| **LoadValues** | **4096**    | **True**             | **2,450.040 μs** | **133.5840 μs** | **69.8670 μs** | **370.1923** | **245.1923** | **182.6923** | **4011.11 KB** |
