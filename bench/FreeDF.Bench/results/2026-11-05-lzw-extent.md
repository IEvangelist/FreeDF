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
| **FreshMember** | **1**           | **False**              | **FlateDecode**       | **7.468 μs** | **0.0467 μs** | **0.0166 μs** | **1.1400** | **0.0152** |  **19.24 KB** |
| **FreshMember** | **1**           | **False**              | **LZWDecode**         | **7.269 μs** | **0.5299 μs** | **0.2353 μs** | **2.1366** | **0.1132** |  **36.12 KB** |
| **FreshMember** | **1**           | **True**               | **FlateDecode**       | **7.708 μs** | **0.8267 μs** | **0.3671 μs** | **1.1161** | **0.0145** |  **18.87 KB** |
| **FreshMember** | **1**           | **True**               | **LZWDecode**         | **8.134 μs** | **0.8273 μs** | **0.4327 μs** | **2.1195** | **0.0668** |  **35.74 KB** |
| **FreshMember** | **2**           | **False**              | **FlateDecode**       | **9.166 μs** | **1.4573 μs** | **0.7622 μs** | **1.2486** | **0.0176** |  **21.04 KB** |
| **FreshMember** | **2**           | **False**              | **LZWDecode**         | **8.964 μs** | **0.6890 μs** | **0.3059 μs** | **2.2328** | **0.1031** |   **37.9 KB** |
| **FreshMember** | **2**           | **True**               | **FlateDecode**       | **9.946 μs** | **1.6605 μs** | **0.8685 μs** | **1.2023** | **0.0188** |  **20.44 KB** |
| **FreshMember** | **2**           | **True**               | **LZWDecode**         | **8.900 μs** | **1.4475 μs** | **0.7570 μs** | **2.2028** | **0.0931** |   **37.3 KB** |
