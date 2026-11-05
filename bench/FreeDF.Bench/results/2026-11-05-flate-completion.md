```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method      | FilterCount | IndirectParameters | CompressionFilter | Mean      | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------------ |------------ |------------------- |------------------ |----------:|----------:|----------:|-------:|-------:|----------:|
| **FreshMember** | **1**           | **False**              | **FlateDecode**       |  **7.459 μs** | **0.1837 μs** | **0.0961 μs** | **1.1342** | **0.0147** |  **19.24 KB** |
| **FreshMember** | **1**           | **False**              | **LZWDecode**         |  **7.434 μs** | **0.6033 μs** | **0.2679 μs** | **2.1362** | **0.1103** |  **36.12 KB** |
| **FreshMember** | **1**           | **True**               | **FlateDecode**       |  **7.597 μs** | **0.1889 μs** | **0.0839 μs** | **1.1171** | **0.0145** |  **18.87 KB** |
| **FreshMember** | **1**           | **True**               | **LZWDecode**         |  **7.104 μs** | **0.2409 μs** | **0.1069 μs** | **2.1096** | **0.0817** |  **35.74 KB** |
| **FreshMember** | **2**           | **False**              | **FlateDecode**       |  **8.502 μs** | **0.7798 μs** | **0.4078 μs** | **1.2462** | **0.0158** |  **21.04 KB** |
| **FreshMember** | **2**           | **False**              | **LZWDecode**         |  **9.215 μs** | **1.8919 μs** | **0.9895 μs** | **2.2346** | **0.1016** |   **37.9 KB** |
| **FreshMember** | **2**           | **True**               | **FlateDecode**       | **10.043 μs** | **2.3797 μs** | **1.2446 μs** | **1.2019** | **0.0200** |  **20.44 KB** |
| **FreshMember** | **2**           | **True**               | **LZWDecode**         |  **8.759 μs** | **1.5753 μs** | **0.8239 μs** | **2.2046** | **0.0896** |   **37.3 KB** |
