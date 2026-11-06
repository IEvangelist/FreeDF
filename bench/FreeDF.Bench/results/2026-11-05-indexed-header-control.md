```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method     | Objects | DictionaryValues | Mean         | Error      | StdDev     | Gen0     | Gen1     | Gen2     | Allocated  |
|----------- |-------- |----------------- |-------------:|-----------:|-----------:|---------:|---------:|---------:|-----------:|
| **LoadValues** | **64**      | **False**            |     **7.858 μs** |  **0.3915 μs** |  **0.2047 μs** |   **1.5300** |   **0.0459** |        **-** |   **25.86 KB** |
| **LoadValues** | **64**      | **True**             |    **15.673 μs** |  **0.5124 μs** |  **0.2275 μs** |   **3.7842** |   **0.3662** |        **-** |   **63.86 KB** |
| **LoadValues** | **4096**    | **False**            |   **621.890 μs** |  **8.4489 μs** |  **4.4189 μs** | **186.2981** | **186.2981** | **186.2981** | **1579.11 KB** |
| **LoadValues** | **4096**    | **True**             | **2,303.305 μs** | **40.8396 μs** | **18.1330 μs** | **364.5833** | **239.5833** | **177.0833** | **4011.11 KB** |
