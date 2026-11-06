```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method | Filter      | Rows | Mean       | Error      | StdDev    | Gen0    | Gen1    | Gen2    | Allocated |
|------- |------------ |----- |-----------:|-----------:|----------:|--------:|--------:|--------:|----------:|
| **Load**   | **FlateDecode** | **64**   |   **7.553 μs** |  **0.6503 μs** | **0.3401 μs** |  **0.6783** |       **-** |       **-** |  **11.62 KB** |
| **Load**   | **FlateDecode** | **4096** | **152.822 μs** |  **9.0696 μs** | **4.0269 μs** | **58.6854** | **58.6854** | **58.6854** | **345.03 KB** |
| **Load**   | **LZWDecode**   | **64**   |   **5.656 μs** |  **0.4553 μs** | **0.2022 μs** |  **0.7461** |  **0.0110** |       **-** |  **12.59 KB** |
| **Load**   | **LZWDecode**   | **4096** | **242.087 μs** | **14.9046 μs** | **7.7954 μs** | **58.6538** | **58.6538** | **58.6538** | **340.87 KB** |
