```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method | DecodedBytes | Mean              | Error           | StdDev          | Gen0     | Gen1     | Gen2     | Allocated |
|------- |------------- |------------------:|----------------:|----------------:|---------:|---------:|---------:|----------:|
| **Encode** | **0**            |          **3.002 ns** |       **0.5245 ns** |       **0.2743 ns** |   **0.0018** |        **-** |        **-** |      **32 B** |
| **Encode** | **256**          |      **9,401.110 ns** |     **422.5161 ns** |     **220.9841 ns** |   **0.0740** |        **-** |        **-** |    **1408 B** |
| **Encode** | **1048576**      | **18,574,103.125 ns** | **239,006.0760 ns** | **125,004.7992 ns** | **968.7500** | **968.7500** | **968.7500** | **5243754 B** |
