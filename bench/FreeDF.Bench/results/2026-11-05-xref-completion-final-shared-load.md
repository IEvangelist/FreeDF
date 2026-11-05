```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method | Filter      | Rows | Mean       | Error      | StdDev     | Gen0    | Gen1    | Gen2    | Allocated |
|------- |------------ |----- |-----------:|-----------:|-----------:|--------:|--------:|--------:|----------:|
| **Load**   | **FlateDecode** | **64**   |   **7.408 μs** |  **0.3637 μs** |  **0.1615 μs** |  **0.6797** |       **-** |       **-** |  **11.62 KB** |
| **Load**   | **FlateDecode** | **4096** | **154.272 μs** |  **7.4735 μs** |  **3.9088 μs** | **58.5938** | **58.5938** | **58.5938** | **345.03 KB** |
| **Load**   | **LZWDecode**   | **64**   |   **5.941 μs** |  **0.4583 μs** |  **0.2397 μs** |  **0.7383** |  **0.0117** |       **-** |  **12.59 KB** |
| **Load**   | **LZWDecode**   | **4096** | **263.260 μs** | **35.7832 μs** | **18.7153 μs** | **58.7687** | **58.7687** | **58.7687** | **340.87 KB** |
