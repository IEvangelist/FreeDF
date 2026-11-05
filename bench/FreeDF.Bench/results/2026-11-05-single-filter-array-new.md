```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method      | IndirectParameters | CompressionFilter | Mean     | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------------ |------------------- |------------------ |---------:|----------:|----------:|-------:|-------:|----------:|
| **FreshMember** | **False**              | **FlateDecode**       | **7.684 μs** | **1.3861 μs** | **0.7249 μs** | **1.1565** | **0.0158** |  **19.51 KB** |
| **FreshMember** | **False**              | **LZWDecode**         | **5.980 μs** | **0.0735 μs** | **0.0326 μs** | **2.1483** | **0.0470** |  **36.38 KB** |
| **FreshMember** | **True**               | **FlateDecode**       | **6.603 μs** | **0.1540 μs** | **0.0684 μs** | **1.1315** | **0.0254** |  **19.14 KB** |
| **FreshMember** | **True**               | **LZWDecode**         | **6.174 μs** | **0.1798 μs** | **0.0798 μs** | **2.1314** | **0.0848** |  **36.02 KB** |
