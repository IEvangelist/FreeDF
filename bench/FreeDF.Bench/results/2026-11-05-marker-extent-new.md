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
| **FreshMember** | **False**              | **FlateDecode**       | **6.738 μs** | **0.2116 μs** | **0.1107 μs** | **1.1509** | **0.0131** |  **19.51 KB** |
| **FreshMember** | **False**              | **LZWDecode**         | **6.020 μs** | **0.2340 μs** | **0.1039 μs** | **2.1495** | **0.0470** |  **36.38 KB** |
| **FreshMember** | **True**               | **FlateDecode**       | **6.518 μs** | **0.1506 μs** | **0.0669 μs** | **1.1343** | **0.0252** |  **19.14 KB** |
| **FreshMember** | **True**               | **LZWDecode**         | **6.704 μs** | **1.0241 μs** | **0.5356 μs** | **2.1356** | **0.0944** |  **36.02 KB** |
