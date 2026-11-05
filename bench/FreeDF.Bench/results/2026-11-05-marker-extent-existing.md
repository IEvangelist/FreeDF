```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method      | FilterCount | IndirectParameters | CompressionFilter | Mean     | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------------ |------------ |------------------- |------------------ |---------:|----------:|----------:|-------:|-------:|----------:|
| **FreshMember** | **1**           | **False**              | **FlateDecode**       | **7.121 μs** | **0.5149 μs** | **0.2286 μs** | **1.1376** | **0.0135** |  **19.24 KB** |
| **FreshMember** | **1**           | **False**              | **LZWDecode**         | **6.323 μs** | **0.3721 μs** | **0.1652 μs** | **2.1380** | **0.1119** |  **36.12 KB** |
| **FreshMember** | **1**           | **True**               | **FlateDecode**       | **6.707 μs** | **0.3168 μs** | **0.1657 μs** | **1.1168** | **0.0127** |  **18.87 KB** |
| **FreshMember** | **1**           | **True**               | **LZWDecode**         | **6.358 μs** | **0.4722 μs** | **0.2469 μs** | **2.1097** | **0.0719** |  **35.74 KB** |
| **FreshMember** | **2**           | **False**              | **FlateDecode**       | **7.379 μs** | **0.3361 μs** | **0.1758 μs** | **1.2469** | **0.0280** |  **21.04 KB** |
| **FreshMember** | **2**           | **False**              | **LZWDecode**         | **6.936 μs** | **0.6149 μs** | **0.3216 μs** | **2.2443** | **0.1044** |   **37.9 KB** |
| **FreshMember** | **2**           | **True**               | **FlateDecode**       | **7.053 μs** | **0.1285 μs** | **0.0571 μs** | **1.2011** | **0.0138** |  **20.44 KB** |
| **FreshMember** | **2**           | **True**               | **LZWDecode**         | **6.602 μs** | **0.2907 μs** | **0.1291 μs** | **2.2090** | **0.1016** |   **37.3 KB** |
