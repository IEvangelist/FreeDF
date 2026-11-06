```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method      | Streams | IndirectLength | Mean        | Error      | StdDev     | Gen0     | Gen1     | Gen2     | Allocated  |
|------------ |-------- |--------------- |------------:|-----------:|-----------:|---------:|---------:|---------:|-----------:|
| **LoadStreams** | **64**      | **False**          |    **20.48 μs** |   **0.205 μs** |   **0.091 μs** |   **4.4555** |   **0.4496** |        **-** |   **75.36 KB** |
| **LoadStreams** | **64**      | **True**           |    **22.74 μs** |   **0.346 μs** |   **0.181 μs** |   **4.5356** |   **0.4536** |        **-** |   **77.02 KB** |
| **LoadStreams** | **4096**    | **False**          | **3,059.76 μs** | **422.172 μs** | **220.804 μs** | **362.5000** | **237.5000** | **175.0000** | **4747.11 KB** |
| **LoadStreams** | **4096**    | **True**           | **2,946.36 μs** |  **81.916 μs** |  **36.371 μs** | **362.5000** | **237.5000** | **175.0000** | **4843.27 KB** |
