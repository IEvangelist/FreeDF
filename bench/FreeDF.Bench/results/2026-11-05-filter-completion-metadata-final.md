```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1  
WarmupCount=3  

```
| Method      | FilterCount | IndirectParameters | Mean     | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|------------ |------------ |------------------- |---------:|----------:|----------:|-------:|-------:|----------:|
| **FreshMember** | **1**           | **False**              | **8.584 μs** | **0.6571 μs** | **0.3437 μs** | **1.1268** | **0.0182** |  **19.24 KB** |
| **FreshMember** | **1**           | **True**               | **8.598 μs** | **0.4625 μs** | **0.2419 μs** | **1.1182** | **0.0172** |  **18.87 KB** |
| **FreshMember** | **2**           | **False**              | **8.375 μs** | **0.2672 μs** | **0.1187 μs** | **1.2355** | **0.0165** |  **21.04 KB** |
| **FreshMember** | **2**           | **True**               | **8.347 μs** | **0.1999 μs** | **0.1046 μs** | **1.1976** | **0.0171** |  **20.44 KB** |
