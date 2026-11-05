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
| **Load**   | **FlateDecode** | **64**   |   **6.313 μs** |  **0.1659 μs** |  **0.0868 μs** |  **0.6839** |  **0.0127** |       **-** |  **11.62 KB** |
| **Load**   | **FlateDecode** | **4096** | **138.068 μs** |  **4.2051 μs** |  **1.8671 μs** | **58.6283** | **58.6283** | **58.6283** | **345.03 KB** |
| **Load**   | **LZWDecode**   | **64**   |   **5.223 μs** |  **0.7208 μs** |  **0.3770 μs** |  **0.7452** |  **0.0099** |       **-** |  **12.59 KB** |
| **Load**   | **LZWDecode**   | **4096** | **247.459 μs** | **31.6958 μs** | **16.5775 μs** | **58.5938** | **58.5938** | **58.5938** | **340.87 KB** |
