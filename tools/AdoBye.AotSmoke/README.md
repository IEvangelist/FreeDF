# NativeAOT compatibility smoke test

The four production libraries target .NET 10 and enable `IsAotCompatible`, including trimming and dynamic-code analysis. This is backed by an actual native publish/run, not only a project flag.

From the repository root on Windows x64, with the .NET 10 SDK and the supported Visual Studio C++ desktop build tools / Windows SDK:

```powershell
dotnet publish tools\AdoBye.AotSmoke\AdoBye.AotSmoke.csproj -c Release -r win-x64 -o artifacts\aot-smoke
.\artifacts\aot-smoke\AdoBye.AotSmoke.exe --iterations 5
```

See Microsoft's [NativeAOT deployment prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) for other platforms. Publish for the target platform with its supported native toolchain; this smoke test has been executed on **Windows x64**, not every supported .NET platform.

The consumer deliberately roots all four production assemblies so native compilation and trimming diagnostics cover more than just the executed call graph. Trimming and AOT warnings are errors; none are suppressed. `RuntimeFeature.IsDynamicCodeSupported` must be false, so running an ordinary managed build cannot accidentally count as a successful NativeAOT test.

Runtime assertions exercise:

- In-memory parsing and page count.
- Embedded TrueType/CJK font decoding and predefined CMap resources (`AB` followed by U+3042).
- Visible raster glyph output and PNG encoding.
- Metadata editing, save/reload, identical decoded text, and identical raster pixels.
- Broken-xref repair, including the native byte-pattern dependency.

The fixture is linked from the existing test corpus and copied beside the published executable. No PDF data is downloaded. A failed assertion terminates with an explicit error. The smoke executable is a validation tool, not a product API.

`--iterations N` repeats that same whole workload after one warmup and prints elapsed time and managed allocated bytes. This is diagnostic output, not a replacement for the BenchmarkDotNet comparisons. It excludes fixture disk I/O, but includes rendering/editing/repair and validation work. Signing, every codec, every PDF feature, and every native platform are not exhaustively covered by the runtime assertions; the normal test suite remains required.

## GC is a host choice

No GC implementation is added to the libraries, and the PdfPig benchmark baseline remains stock .NET. The executable reports whether `GC.GetConfigurationVariables()` contains the `SatoriGC` key; `--require-satori` fails unless that activation marker is present.

[PublishWithSatoriGC 10.0.14-satori.40433706833.1](https://www.nuget.org/packages/PublishWithSatoriGC/10.0.14-satori.40433706833.1) documents experimental, self-contained .NET 10 CoreCLR/NativeAOT support, including win-x64. Framework-dependent and Mono applications are unsupported. Its optional SDK line is:

```xml
<Sdk Name="PublishWithSatoriGC" Version="10.0.14-satori.40433706833.1" />
```

Use it only in an isolated, explicitly opted-in application; `-p:UseSatoriGC=false` selects stock publishing with that SDK. Do not copy external collector binaries into the libraries or change machine-wide runtime settings. The upstream [Satori roadmap](https://github.com/VSadov/Satori/blob/main/src/coreclr/gc/satori/README.md) still lists performance tuning and diagnostic work, so neither reliability equivalence nor a 3x speedup is promised. Any GC comparison must keep workload, native/managed publish mode, runtime, and environment matched and verify activation.

## Executed validation: 2026-10-29

Stock NativeAOT publishing succeeded with .NET SDK 10.0.403 / runtime 10.0.14 on Windows 11 25H2 x64, using the installed supported native toolchain. All four assemblies were rooted. There were **no IL trimming or dynamic-code warnings**, with no suppressions added. The native executable passed all assertions and reported `dynamic-code=False, SatoriGC=False`. Existing ordinary compiler warnings in the content project were present before these changes.

After renaming the libraries, the native publish was repeated against all four rooted `PortableDocument.*` assemblies, with the renamed embedded CMap/glyph resources and `PortableDocument.Render.PortableDocument` entry class. The newly published native executable again passed five iterations of every assertion (`dynamic-code=False, SatoriGC=False`), including CJK text, raster output, edit/save/reload, and xref repair. The full managed suite passed 312 tests, all six comparative-corpus preflights passed, and the solution and remaining demo/editor/sample consumers built successfully. No new trimming or dynamic-code diagnostics appeared.

An optional Satori experiment used a separate, session-local project and output folder, the identical linked `Program.cs`, the same rooted production assemblies and fixture, `PublishAot=true`, and the pinned SDK line above. This SDK was not added to any repository project. The SDK emitted its explicit experimental warning. Its native executable passed the same assertions, including xref repair, and reported `dynamic-code=False, SatoriGC=True`; `--require-satori` was enforced.

To reproduce the opt-in shape without changing the stock app, create a sibling experimental project containing the stock smoke project/program, retain its relative project/fixture references and root declarations, and add only the pinned SDK line. Publish that app to a different directory with the same `-c Release -r win-x64` settings. Run each published executable with `--iterations 500`, adding `--require-satori` only for the Satori executable.

Three alternating stock/Satori trials each performed one warmup followed by 500 identical parse/text/render/PNG/edit/save/repair workloads. Disk I/O was outside timing:

| Trial | Stock elapsed ms | Satori elapsed ms | Stock allocated bytes | Satori allocated bytes |
|---|---:|---:|---:|---:|
| 1 | 1,653.750 | 1,669.349 | 1,602,091,632 | 1,589,165,968 |
| 2 | 1,604.013 | 1,684.670 | 1,602,067,336 | 1,589,164,000 |
| 3 | 1,656.181 | 1,702.880 | 1,602,092,384 | 1,589,234,840 |

Mean elapsed times were 1,637.981 ms stock and 1,685.633 ms Satori: Satori took about **2.9% longer** in this small experiment, not 3x less time. Allocation counters are recorded as returned by each collector and are not assumed to be perfectly equivalent accounting. This short diagnostic is not BenchmarkDotNet, a statistically strong collector ranking, or an exhaustive reliability test. It gives no reason to replace the stock collector by default; the main PdfPig results remain separate stock managed-.NET measurements.
