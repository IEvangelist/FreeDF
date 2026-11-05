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
| **FreshMember** | **1**           | **False**              | **FlateDecode**       | **6.775 μs** | **0.2060 μs** | **0.1078 μs** | **1.1361** | **0.0140** |  **19.24 KB** |
| **FreshMember** | **1**           | **False**              | **LZWDecode**         | **7.175 μs** | **1.9791 μs** | **0.8787 μs** | **2.1393** | **0.1088** |  **36.12 KB** |
| **FreshMember** | **1**           | **True**               | **FlateDecode**       | **6.944 μs** | **0.1117 μs** | **0.0584 μs** | **1.1101** | **0.0139** |  **18.87 KB** |
| **FreshMember** | **1**           | **True**               | **LZWDecode**         | **6.324 μs** | **0.5474 μs** | **0.1952 μs** | **2.1107** | **0.0736** |  **35.74 KB** |
| **FreshMember** | **2**           | **False**              | **FlateDecode**       | **7.622 μs** | **0.2318 μs** | **0.1212 μs** | **1.2444** | **0.0148** |  **21.04 KB** |
| **FreshMember** | **2**           | **False**              | **LZWDecode**         | **7.595 μs** | **0.2828 μs** | **0.1256 μs** | **2.2348** | **0.1057** |   **37.9 KB** |
| **FreshMember** | **2**           | **True**               | **FlateDecode**       | **8.204 μs** | **1.0427 μs** | **0.5453 μs** | **1.2094** | **0.0149** |  **20.44 KB** |
| **FreshMember** | **2**           | **True**               | **LZWDecode**         | **7.346 μs** | **0.5345 μs** | **0.2796 μs** | **2.2092** | **0.0878** |   **37.3 KB** |
