```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
Intel Core i7-14700K 3.40GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-TSMBOA : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=8  IterationTime=500ms  LaunchCount=1
WarmupCount=3

```
| Method           | MemberCount | Encrypted | Mean           | Error          | StdDev         | Gen0     | Gen1    | Allocated |
|----------------- |------------ |---------- |---------------:|---------------:|---------------:|---------:|--------:|----------:|
| **LoadOnly**         | **1**           | **False**     |   **8,829.631 ns** |    **186.6097 ns** |     **97.6005 ns** |   **0.9888** |  **0.0173** |   **17152 B** |
| FreshFirstMember | 1           | False     |   9,384.538 ns |    332.4926 ns |    173.9000 ns |   1.0609 |  0.0183 |   18368 B |
| FreshAllMembers  | 1           | False     |   9,500.182 ns |    795.3750 ns |    415.9965 ns |   1.0486 |  0.0181 |   18368 B |
| CachedMember     | 1           | False     |       1.973 ns |      0.0516 ns |      0.0270 ns |        - |       - |         - |
| **LoadOnly**         | **1**           | **True**      |  **34,952.196 ns** |  **1,142.3222 ns** |    **507.1981 ns** |   **2.6013** |  **0.0685** |   **45289 B** |
| FreshFirstMember | 1           | True      |  36,168.353 ns |  2,346.0051 ns |  1,041.6408 ns |   2.7083 |  0.0694 |   46753 B |
| FreshAllMembers  | 1           | True      |  35,763.541 ns |  1,742.3261 ns |    911.2702 ns |   2.6874 |  0.0689 |   46753 B |
| CachedMember     | 1           | True      |       1.737 ns |      0.0989 ns |      0.0439 ns |        - |       - |         - |
| **LoadOnly**         | **1000**        | **False**     |  **82,458.266 ns** |  **1,868.1173 ns** |    **829.4557 ns** |  **11.3487** |  **2.7961** |  **196104 B** |
| FreshFirstMember | 1000        | False     |  79,825.030 ns |  2,785.7929 ns |  1,457.0236 ns |  11.3350 |  2.8338 |  197320 B |
| FreshAllMembers  | 1000        | False     | 562,455.222 ns | 16,078.1775 ns |  8,409.1977 ns | 112.9386 | 47.1491 | 1954024 B |
| CachedMember     | 1000        | False     |       1.853 ns |      0.0804 ns |      0.0420 ns |        - |       - |         - |
| **LoadOnly**         | **1000**        | **True**      | **106,733.230 ns** |  **6,073.0915 ns** |  **3,176.3443 ns** |  **13.0814** |  **3.1146** |  **229275 B** |
| FreshFirstMember | 1000        | True      | 113,802.298 ns |  6,675.8800 ns |  3,491.6143 ns |  13.2299 |  3.1934 |  230739 B |
| FreshAllMembers  | 1000        | True      | 634,396.995 ns | 57,458.6094 ns | 30,051.9637 ns | 116.5865 | 52.8846 | 2019394 B |
| CachedMember     | 1000        | True      |       1.707 ns |      0.0845 ns |      0.0442 ns |        - |       - |         - |
