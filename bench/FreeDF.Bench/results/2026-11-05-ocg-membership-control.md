```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method    | Policy | Mean     | Error   | StdDev  | Gen0    | Gen1   | Allocated |
|---------- |------- |---------:|--------:|--------:|--------:|-------:|----------:|
| **Interpret** | **AllOff** | **108.9 μs** | **2.41 μs** | **1.26 μs** | **12.8092** | **0.2208** | **216.19 KB** |
| **Interpret** | **AllOn**  | **110.7 μs** | **1.41 μs** | **0.74 μs** | **12.6779** | **0.2224** | **216.19 KB** |
| **Interpret** | **AnyOff** | **112.6 μs** | **2.84 μs** | **1.48 μs** | **12.7641** | **0.2201** | **216.19 KB** |
| **Interpret** | **AnyOn**  | **118.6 μs** | **6.04 μs** | **3.16 μs** | **12.6008** | **0.2520** | **216.19 KB** |
