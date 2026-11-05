```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method | Pattern  | Strict | Mean      | Error     | StdDev   | Gen0    | Allocated |
|------- |--------- |------- |----------:|----------:|---------:|--------:|----------:|
| **Decode** | **Literal**  | **False**  | **246.27 μs** |  **9.726 μs** | **4.318 μs** | **11.0887** | **192.05 KB** |
| **Decode** | **Literal**  | **True**   | **288.06 μs** | **16.771 μs** | **8.772 μs** | **11.2613** | **192.05 KB** |
| **Decode** | **Repeated** | **False**  |  **10.43 μs** |  **0.399 μs** | **0.177 μs** | **11.3617** | **192.05 KB** |
| **Decode** | **Repeated** | **True**   |  **10.28 μs** |  **0.297 μs** | **0.132 μs** | **11.3489** | **192.05 KB** |
