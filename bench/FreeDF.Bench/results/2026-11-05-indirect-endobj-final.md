```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method     | Objects | DictionaryValues | Mean         | Error       | StdDev      | Gen0     | Gen1     | Gen2     | Allocated  |
|----------- |-------- |----------------- |-------------:|------------:|------------:|---------:|---------:|---------:|-----------:|
| **LoadValues** | **64**      | **False**            |     **8.116 μs** |   **0.4013 μs** |   **0.1782 μs** |   **1.5336** |   **0.0474** |        **-** |   **25.86 KB** |
| **LoadValues** | **64**      | **True**             |    **16.120 μs** |   **1.1222 μs** |   **0.5869 μs** |   **3.7879** |   **0.3666** |        **-** |   **63.86 KB** |
| **LoadValues** | **4096**    | **False**            |   **606.692 μs** |  **12.8214 μs** |   **5.6928 μs** | **186.3208** | **186.3208** | **186.3208** | **1579.11 KB** |
| **LoadValues** | **4096**    | **True**             | **2,430.302 μs** | **201.0937 μs** | **105.1759 μs** | **364.5833** | **239.5833** | **177.0833** | **4011.11 KB** |
