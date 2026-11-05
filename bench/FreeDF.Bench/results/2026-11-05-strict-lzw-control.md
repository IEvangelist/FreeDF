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
| **FreshMember** | **1**           | **False**              | **FlateDecode**       |  **8.148 μs** | **0.8791 μs** | **0.3903 μs** | **1.1393** | **0.0160** |  **19.24 KB** |
| **FreshMember** | **1**           | **False**              | **LZWDecode**         | **12.627 μs** | **0.5333 μs** | **0.2368 μs** | **2.6173** | **0.1378** |  **44.12 KB** |
| **FreshMember** | **1**           | **True**               | **FlateDecode**       | **14.279 μs** | **1.0126 μs** | **0.5296 μs** | **1.1164** | **0.0157** |  **18.87 KB** |
| **FreshMember** | **1**           | **True**               | **LZWDecode**         | **12.332 μs** | **0.8207 μs** | **0.3644 μs** | **2.5884** | **0.0539** |  **43.74 KB** |
| **FreshMember** | **2**           | **False**              | **FlateDecode**       | **15.095 μs** | **0.4247 μs** | **0.2221 μs** | **1.2430** | **0.0166** |  **21.04 KB** |
| **FreshMember** | **2**           | **False**              | **LZWDecode**         | **13.476 μs** | **0.5125 μs** | **0.2681 μs** | **2.7174** | **0.1874** |   **45.9 KB** |
| **FreshMember** | **2**           | **True**               | **FlateDecode**       | **14.993 μs** | **0.1772 μs** | **0.0927 μs** | **1.1997** | **0.0162** |  **20.44 KB** |
| **FreshMember** | **2**           | **True**               | **LZWDecode**         | **13.244 μs** | **0.8939 μs** | **0.4676 μs** | **2.6882** | **0.1734** |   **45.3 KB** |
