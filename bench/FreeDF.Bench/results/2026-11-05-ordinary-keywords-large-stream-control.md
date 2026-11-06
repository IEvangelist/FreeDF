```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TOSIUQ : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=12  IterationTime=1s  LaunchCount=1
WarmupCount=3

```
| Method     | Objects | StreamValues | Mean     | Error    | StdDev   | Gen0     | Gen1     | Gen2     | Allocated |
|----------- |-------- |------------- |---------:|---------:|---------:|---------:|---------:|---------:|----------:|
| LoadValues | 4096    | True         | 14.53 ms | 0.717 ms | 0.518 ms | 912.5000 | 675.0000 | 250.0000 |  12.98 MB |
