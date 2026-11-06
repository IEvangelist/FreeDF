```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method      | Streams | IndirectLength | Mean        | Error      | StdDev    | Gen0     | Gen1     | Gen2     | Allocated  |
|------------ |-------- |--------------- |------------:|-----------:|----------:|---------:|---------:|---------:|-----------:|
| **LoadStreams** | **64**      | **False**          |    **19.82 μs** |   **0.732 μs** |  **0.325 μs** |   **4.4671** |   **0.4744** |        **-** |   **75.36 KB** |
| **LoadStreams** | **64**      | **True**           |    **21.14 μs** |   **0.773 μs** |  **0.343 μs** |   **4.5599** |   **0.4602** |        **-** |   **77.02 KB** |
| **LoadStreams** | **4096**    | **False**          | **2,739.69 μs** |  **66.945 μs** | **35.014 μs** | **364.5833** | **239.5833** | **177.0833** | **4747.11 KB** |
| **LoadStreams** | **4096**    | **True**           | **2,914.20 μs** | **161.269 μs** | **84.347 μs** | **362.5000** | **237.5000** | **175.0000** | **4843.27 KB** |
