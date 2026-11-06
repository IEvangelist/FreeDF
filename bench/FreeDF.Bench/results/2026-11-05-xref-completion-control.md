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
| **Load**   | **FlateDecode** | **64**   |   **6.915 μs** | **0.2045 μs** | **0.0908 μs** |  **0.6873** |       **-** |       **-** |  **11.62 KB** |
| **Load**   | **FlateDecode** | **4096** | **146.022 μs** | **2.4932 μs** | **1.3040 μs** | **58.5938** | **58.5938** | **58.5938** | **345.03 KB** |
| **Load**   | **LZWDecode**   | **64**   |   **4.979 μs** | **0.1144 μs** | **0.0598 μs** |  **0.7423** |  **0.0099** |       **-** |  **12.59 KB** |
| **Load**   | **LZWDecode**   | **4096** | **220.822 μs** | **6.5083 μs** | **3.4040 μs** | **58.5664** | **58.5664** | **58.5664** | **340.87 KB** |
