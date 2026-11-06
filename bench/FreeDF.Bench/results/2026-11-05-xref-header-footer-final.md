```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method | Filter      | Rows | Mean       | Error     | StdDev    | Gen0    | Gen1    | Gen2    | Allocated |
|------- |------------ |----- |-----------:|----------:|----------:|--------:|--------:|--------:|----------:|
| **Load**   | **FlateDecode** | **64**   |   **6.309 μs** | **0.1389 μs** | **0.0726 μs** |  **0.6812** |  **0.0129** |       **-** |  **11.62 KB** |
| **Load**   | **FlateDecode** | **4096** | **133.873 μs** | **1.1003 μs** | **0.5755 μs** | **58.7121** | **58.7121** | **58.7121** | **345.03 KB** |
| **Load**   | **LZWDecode**   | **64**   |   **4.827 μs** | **0.0817 μs** | **0.0427 μs** |  **0.7378** |  **0.0101** |       **-** |  **12.59 KB** |
| **Load**   | **LZWDecode**   | **4096** | **217.366 μs** | **4.6933 μs** | **2.0839 μs** | **58.6735** | **58.6735** | **58.6735** | **340.87 KB** |
