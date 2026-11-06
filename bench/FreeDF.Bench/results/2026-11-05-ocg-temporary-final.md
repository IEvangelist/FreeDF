```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Type                               | Method    | Event  | Manual | Recommendation | Mean      | Error     | StdDev   | Allocated |
|----------------------------------- |---------- |------- |------- |--------------- |----------:|----------:|---------:|----------:|
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Export** | **False**  | **False**          | **177.78 ns** |  **6.637 ns** | **3.471 ns** |         **-** |
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Export** | **False**  | **True**           |  **84.71 ns** |  **4.853 ns** | **2.155 ns** |         **-** |
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Export** | **True**   | **False**          |  **75.48 ns** |  **3.087 ns** | **1.615 ns** |         **-** |
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Export** | **True**   | **True**           |  **84.40 ns** |  **6.861 ns** | **3.588 ns** |         **-** |
| **OptionalContentEventBench**          | **IsVisible** | **Export** | **?**      | **?**              |  **75.33 ns** |  **2.513 ns** | **1.314 ns** |         **-** |
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Print**  | **False**  | **False**          | **179.11 ns** |  **7.086 ns** | **3.706 ns** |         **-** |
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Print**  | **False**  | **True**           |  **79.57 ns** |  **1.657 ns** | **0.867 ns** |         **-** |
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Print**  | **True**   | **False**          |  **70.58 ns** |  **2.036 ns** | **1.065 ns** |         **-** |
| **OptionalContentTemporaryEventBench** | **IsVisible** | **Print**  | **True**   | **True**           |  **77.51 ns** |  **2.819 ns** | **1.474 ns** |         **-** |
| **OptionalContentEventBench**          | **IsVisible** | **Print**  | **?**      | **?**              |  **75.94 ns** |  **4.190 ns** | **2.191 ns** |         **-** |
| **OptionalContentEventBench**          | **IsVisible** | **View**   | **?**      | **?**              | **101.95 ns** | **13.667 ns** | **6.068 ns** |         **-** |
