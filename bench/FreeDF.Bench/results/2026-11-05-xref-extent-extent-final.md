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
| **Load**   | **FlateDecode** | **64**   |   **7.674 μs** |  **0.7852 μs** | **0.4107 μs** |  **0.6789** |       **-** |       **-** |  **11.62 KB** |
| **Load**   | **FlateDecode** | **4096** | **157.083 μs** | **11.5712 μs** | **5.1377 μs** | **58.6929** | **58.6929** | **58.6929** | **345.03 KB** |
| **Load**   | **LZWDecode**   | **64**   |   **5.987 μs** |  **0.6212 μs** | **0.3249 μs** |  **0.7447** |  **0.0115** |       **-** |  **12.59 KB** |
| **Load**   | **LZWDecode**   | **4096** | **250.124 μs** | **14.9056 μs** | **7.7959 μs** | **58.7121** | **58.7121** | **58.7121** | **340.87 KB** |
