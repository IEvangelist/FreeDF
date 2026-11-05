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
| **FreshMember** | **1**           | **False**              | **FlateDecode**       | **8.855 μs** | **0.9809 μs** | **0.5131 μs** | **1.1282** | **0.0161** |  **19.24 KB** |
| **FreshMember** | **1**           | **False**              | **LZWDecode**         | **9.050 μs** | **1.4313 μs** | **0.7486 μs** | **2.1378** | **0.1008** |  **36.12 KB** |
| **FreshMember** | **1**           | **True**               | **FlateDecode**       | **8.876 μs** | **0.8788 μs** | **0.4596 μs** | **1.1158** | **0.0162** |  **18.87 KB** |
| **FreshMember** | **1**           | **True**               | **LZWDecode**         | **8.706 μs** | **0.6366 μs** | **0.2826 μs** | **2.1101** | **0.0787** |  **35.74 KB** |
| **FreshMember** | **2**           | **False**              | **FlateDecode**       | **9.479 μs** | **0.8723 μs** | **0.4562 μs** | **1.2482** | **0.0178** |  **21.04 KB** |
| **FreshMember** | **2**           | **False**              | **LZWDecode**         | **9.823 μs** | **1.6772 μs** | **0.8772 μs** | **2.2349** | **0.0972** |   **37.9 KB** |
| **FreshMember** | **2**           | **True**               | **FlateDecode**       | **8.397 μs** | **0.5327 μs** | **0.2786 μs** | **1.2129** | **0.0168** |  **20.44 KB** |
| **FreshMember** | **2**           | **True**               | **LZWDecode**         | **8.582 μs** | **0.5161 μs** | **0.2699 μs** | **2.2007** | **0.0943** |   **37.3 KB** |
