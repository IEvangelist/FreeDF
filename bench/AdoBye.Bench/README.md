# Parser and text-extraction benchmarks

BenchmarkDotNet 0.15.8 compares AdoBye with PdfPig 0.1.16 using the same PDF byte arrays. PdfPig's official package is `PdfPig`, verified against the [v0.1.16 release](https://github.com/UglyToad/PdfPig/releases/tag/v0.1.16). It is an Apache-2.0 dependency of this benchmark executable only, not a production library dependency.

## Reproduce

From the repository root, with the .NET 10 SDK:

```powershell
dotnet build bench\AdoBye.Bench -c Release
dotnet run -c Release --no-build --project bench\AdoBye.Bench -- --preflight
dotnet run -c Release --no-build --project bench\AdoBye.Bench -- --filter "*PdfOpenBench*" "*TextExtractionBench*" "*OpenDocumentTextBench*" --join
```

The comparison classes use BenchmarkDotNet's default job (adaptive warmup, pilot, and measurement iterations), not the legacy renderer's short job. A smoke run is useful for correctness but not performance conclusions:

```powershell
dotnet run -c Release --no-build --project bench\AdoBye.Bench -- --filter "*PdfOpenBench*" "*TextExtractionBench*" "*OpenDocumentTextBench*" --job Dry --join
```

## Corpus and correctness

Candidates are `bench\fixtures\text.pdf`, `vector.pdf`, `image.pdf`, and `samples\03-typography-embedded.pdf`, `06-patterns-shadings.pdf`, `09-images.pdf`. They cover text, embedded typography, vector graphics, patterns/shadings, and images. Setup reads bytes outside timing.

Before any comparison, preflight opens every candidate with both engines twice. It requires equal page counts, stable text across independent opens, and equal per-page decoded text after removing whitespace. Only whitespace is normalized to account for text-layout differences; text is not reordered, case-folded, or replaced. Mismatches and exceptions fail preflight visibly: no candidate is silently skipped to improve results. `PASS` lines include byte length, SHA-256, page count, and normalized character count so a run's exact corpus can be checked. Blank/image-only pages are valid equal inputs but should not be interpreted as text-heavy workloads.

Both extraction paths return materialized arrays of per-page strings in content order: AdoBye concatenates `PdfRenderer.ExtractText` spans and PdfPig concatenates `Page.Letters` values. Preflight normalization is not timed.

## Measurement boundaries

| Class | Timed work |
|---|---|
| `PdfOpenBench` | Open bytes, obtain page count, dispose where required; not full content decoding |
| `TextExtractionBench` | Fresh open plus all-page text extraction and materialization, with disposal |
| `OpenDocumentTextBench` | First all-page text extraction/materialization on an already-open document |

Both engines receive the same existing `byte[]`. PdfPig 0.1.16 supports `Open(byte[])` directly, so no harness `MemoryStream` adapter or disk I/O is introduced. Any internal copying/adaptation is part of the engine's cost. PdfPig disposal is timed for fresh-open cases; AdoBye documents have no disposal API.

The already-open case opens a batch of 32 fresh documents in `IterationSetup` and disposes them in `IterationCleanup`, outside timing. BenchmarkDotNet uses one invocation and an unroll factor of one per iteration, with `OperationsPerInvoke=32` so times and allocations are reported per document. Each document is extracted exactly once, preventing PdfPig's decoded-page cache from turning later invocations into cache lookups while AdoBye repeats interpretation. This measures first extraction after open, not warm cached access. Batching increases measurement time, but blank pages can still produce minimum-iteration-time warnings. Setup-induced GC/cache effects and keeping both engines' unopened document batches alive can affect results; use fresh-open extraction for end-to-end decoding cost. Do not subtract separate benchmark means to estimate extraction cost.

`MemoryDiagnoser` reports managed allocations, not total process/native memory. Ratios are PdfPig divided by the AdoBye baseline for the same document and operation. Results are specific to corpus, runtime, CPU, build, and cache state, not general speed guarantees. BenchmarkDotNet writes mean/error, allocations, ratios, exact commands, OS, CPU, runtime, JIT, and GC information under `BenchmarkDotNet.Artifacts`; preserve those reports when sharing measurements.

Rendering remains in the separate legacy `RenderBench`. PdfPig has no comparable raster-rendering implementation in this harness, so these results make no rendering-parity claim.

AdoBye's text-only interpreter retains decoded text, spans, glyph bytes, geometry, advances, and form traversal, but does not paint paths, rasterize glyphs, decode image pixels, or build editor graphics-object lists. `Analyze` and rendering retain those operations. Typeface fallback still depends on installed fonts; both engines' font caches and JIT are warmed before the timed measurements, and no cold-process startup claims are made.

## Profiling

The explicit workload runs repeated fresh opens/extractions without BenchmarkDotNet's harness:

```powershell
dotnet run -c Release --no-build --project bench\AdoBye.Bench -- --profile-text text.pdf 1000
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1:5 --output text.nettrace -- dotnet bench\AdoBye.Bench\bin\Release\net10.0\AdoBye.Bench.dll --profile-text text.pdf 1000
dotnet-trace report text.nettrace topN -n 20
```

`dotnet-trace` is an optional diagnostic tool, not a production or benchmark project dependency. Runtime GC allocation-tick events in the trace give sampled allocation types and stacks; `MemoryDiagnoser` supplies the per-operation allocation measurements. Profiling perturbs execution and includes startup, GC, and background threads, so sampled CPU percentages are not benchmark timings. Keep trace collection separate from timing runs.

The measured fixes preserve the existing public text span and glyph metadata:

- Resolve preferred TrueType cmap offsets once per font, preserving priority, first-duplicate behavior, and fallback when a preferred map has no glyph. Remove per-lookup LINQ closures.
- Cache simple-font fallback widths and reuse the glyph decoding pass when collecting Unicode.
- Use stack-backed spans for fixed glyph bounds instead of tuple arrays and interface enumerators.
- Separate text collection from graphics painting and editor-object collection. Full analysis and rendering remain enabled by default.
- Parse numeric tokens directly from UTF-8 byte spans, avoiding token strings, sign substrings, LINQ digit checks, and decimal reconstruction arrays. Preserve existing tolerant syntax, including repeated signs, fractional suffix truncation, overflow fallback, and rejected integer suffixes such as `5-`.

The initial 1,000-document `text.pdf` trace showed `ShowText` and `TrueTypeFont.LookupAny` as application CPU hotspots. Sampled allocations attributed approximately 4.33 GB to cmap predicate delegates, 3.24 GB to tuple arrays, and 1.37 GB to predicate closure objects. After the text-path changes, those types disappeared from the top allocation types; retained glyph metadata (`TextGlyph`, byte arrays, and glyph arrays) dominates instead. These are sampled aggregate diagnostic amounts, not exact per-operation allocations.

The vector workload then exposed numeric token parsing: in a separate 500-document trace before the byte-span lexer change, `PdfLexer.TryNumber` accounted for 17.43% exclusive sampled CPU and `Enumerable.ToArray` for 5.95%. Sampled string, `TakeWhile` iterator, char-array, and char-enumerator allocations were approximately 3.10 GB, 1.10 GB, 0.57 GB, and 0.52 GB respectively. This motivated the focused numeric parser change rather than omitting vector input. Whole-process profiles include GC/finalizer background activity and profiler overhead.

After that change, a matched 500-document trace put numeric parsing at 7.33% exclusive sampled CPU; the fractional `TakeWhile`/char-array/enumerator chain disappeared from the top allocation types. Sampled strings fell to about 0.27 GB. Remaining application hotspots include stream inflation, numeric token objects, path construction, and graphics-state cloning. Further work should target those with new profiles and correctness tests rather than weakening the corpus or dropping required text metadata.

## Full-corpus measured baseline: 2026-10-29 (90a225b)

These are machine-specific measurements, not general guarantees. Environment: Windows 11 25H2 build 26200.9711; Intel Core i9-10900 at 2.80 GHz (10 physical / 20 logical cores); .NET SDK 10.0.403; .NET 10.0.14 x64 RyuJIT x86-64-v3; concurrent workstation GC; TieredPGO enabled. Both engines ran in the same BenchmarkDotNet executable with stock .NET GC. No Satori GC or NativeAOT substitution was used.

Exact measurement arguments (the output directory was session-local):

```powershell
dotnet run -c Release --no-build --project bench\AdoBye.Bench -- --filter "*PdfOpenBench*" "*TextExtractionBench*" "*OpenDocumentTextBench*" --join --warmupCount 6 --iterationCount 15 --iterationTime 1000 --launchCount 1
```

The final run completed all 36 cases in 12m 58s. All six corpus documents passed the two-engine, repeated-open per-page text preflight; none were skipped. The typography sample's differing raw extraction lengths are whitespace/layout differences: after removing whitespace, both engines produce the same 2,308 characters in the same per-page order. Normalized counts for text, vector, image, typography, patterns, and image samples are 27,148 / 0 / 0 / 2,308 / 751 / 594.

The [full BenchmarkDotNet report](results/2026-10-29.md) and [CSV](results/2026-10-29.csv) retain means, errors, standard deviations, allocations, GC counts, and ratio distributions for all 36 cases. The table below pairs them into 18 comparisons. Times are **microseconds per document**, managed allocation is **KB (1,024 bytes)**, and the ratio is **PdfPig mean / AdoBye mean** (larger favors AdoBye). This quotient of reported means can differ slightly from BenchmarkDotNet's mean of the ratio distribution.

| Operation | Document | AdoBye mean (us) | PdfPig mean (us) | AdoBye KB | PdfPig KB | Mean ratio |
|---|---|---:|---:|---:|---:|---:|
| Open | 03-typography-embedded.pdf | 14.383 | 47.154 | 35.36 | 79.61 | 3.28x |
| Open | 06-patterns-shadings.pdf | 29.291 | 71.154 | 59.10 | 103.89 | 2.43x |
| Open | 09-images.pdf | 12.047 | 43.920 | 26.88 | 63.51 | 3.65x |
| Open | image.pdf | 7.656 | 30.540 | 18.69 | 52.26 | 3.99x |
| Open | text.pdf | 20.967 | 54.076 | 43.69 | 78.47 | 2.58x |
| Open | vector.pdf | 13.835 | 42.528 | 30.69 | 63.69 | 3.07x |
| Open + extract | 03-typography-embedded.pdf | 3,582.752 | 12,367.550 | 7,406.24 | 10,903.17 | 3.45x |
| Open + extract | 06-patterns-shadings.pdf | 869.958 | 2,667.212 | 1,980.98 | 3,559.84 | 3.07x |
| Open + extract | 09-images.pdf | 1,330.180 | 6,223.108 | 2,928.14 | 4,984.61 | 4.68x |
| Open + extract | image.pdf | 92.832 | 350.528 | 557.36 | 466.97 | 3.78x |
| Open + extract | text.pdf | 4,311.902 | 12,028.781 | 6,368.08 | 13,952.00 | 2.79x |
| Open + extract | vector.pdf | 6,413.557 | 7,467.955 | 7,169.46 | 5,673.50 | 1.16x |
| Already open, first extract | 03-typography-embedded.pdf | 4,267.859 | 14,798.886 | 7,370.90 | 10,823.69 | 3.47x |
| Already open, first extract | 06-patterns-shadings.pdf | 1,500.233 | 4,530.929 | 1,922.28 | 3,455.96 | 3.02x |
| Already open, first extract | 09-images.pdf | 2,637.558 | 5,729.605 | 2,901.58 | 4,914.32 | 2.17x |
| Already open, first extract | image.pdf | 195.008 | 269.924 | 538.47 | 414.71 | 1.38x |
| Already open, first extract | text.pdf | 4,200.978 | 11,412.827 | 6,324.40 | 13,873.50 | 2.72x |
| Already open, first extract | vector.pdf | 9,359.342 | 7,433.557 | 7,138.92 | 5,609.94 | 0.79x |

**The across-the-board goal is not met:** 10/18 mean ratios reach the newer 3x target and 15/18 reach 2x. Below 3x are open-only patterns/text; fresh-open extraction text/vector; and already-open extraction image sample, image fixture, text, and vector. Vector fresh-open extraction reaches only 1.16x (6.414 ms versus a 3x target of 2.489 ms); already-open vector extraction remains slower than PdfPig. The already-open image fixture also misses 2x. Near-threshold results, especially the 3.02x already-open pattern case, are not robust guarantees.

The final byte-span lexer pass alone reduced vector fresh-open extraction from 9.629 to 6.414 ms and allocations from 16,991.19 to 7,169.46 KB in matched jobs; [pre-lexer CSV](results/2026-10-29-before-lexer.csv) preserves that control. AdoBye still allocates more than PdfPig on the image fixture and vector extraction. These residual costs and target misses are intentionally reported.

BenchmarkDotNet warned about short iterations for already-open typography/pattern/image cases despite batching, and multimodal already-open distributions. Fresh-open and open-only measurements met the 100 ms minimum-iteration recommendation. Treat the already-open measurements as noisier diagnostics, not a basis for narrow ranking claims. Setup/lifetime/cache differences make subtraction or comparisons between benchmark classes invalid.

Validation: Release benchmark build succeeded; all 311 existing and added tests passed. Added regression tests cover numeric token tolerance/overflow, cmap priority/duplicate/fallback behavior and zero repeated-lookup allocations, repeatable glyph geometry/bytes/advances, and text-only versus full-analysis parity on font, feature, and graphics fixtures.

### Targeted path-only follow-up

A later, narrowly scoped pass skips path construction/painting/clipping operators only when graphics drawing is disabled and no graphics-object collection is requested. It still parses the operands and retains text-relevant graphics state, CTM changes, colors, forms, and `q`/`Q` restoration. Full analysis and rendering are unchanged. A new parity test covers every path/paint/clip opcode followed by transformed text and restored text state.

The [targeted CSV](results/2026-10-29-text-path.csv) contains all 12 new measurements. These supersede the corresponding six extraction comparisons above; the full-corpus table remains a labeled baseline, not a claim that unmeasured cases were rerun. Environment and timing settings are unchanged. Exact timing arguments:

```powershell
dotnet run -c Release --no-build --project bench\AdoBye.Bench -- --filter "*Text*vector.pdf*" "*Text*06-patterns*" "*Text*text.pdf*" --join --warmupCount 6 --iterationCount 15 --iterationTime 1000 --launchCount 1
```

Times are microseconds per document; allocations below retain BenchmarkDotNet's reported MB precision (1 MB = 1,048,576 bytes). Ratios are quotients of reported means.

| Operation | Document | AdoBye mean (us) | PdfPig mean (us) | AdoBye MB | PdfPig MB | Mean ratio |
|---|---|---:|---:|---:|---:|---:|
| Open + extract | 06-patterns-shadings.pdf | 851.9 | 2,631.4 | 1.90 | 3.48 | 3.09x |
| Open + extract | text.pdf | 4,404.1 | 11,876.1 | 6.22 | 13.62 | 2.70x |
| Open + extract | vector.pdf | 5,700.9 | 7,637.3 | 5.72 | 5.54 | 1.34x |
| Already open, first extract | 06-patterns-shadings.pdf | 2,214.1 | 4,745.1 | 1.85 | 3.38 | 2.14x |
| Already open, first extract | text.pdf | 4,420.0 | 12,036.0 | 6.18 | 13.55 | 2.72x |
| Already open, first extract | vector.pdf | 7,636.4 | 7,796.7 | 5.69 | 5.48 | 1.02x |

Vector fresh-open extraction improves by about 11% and already-open vector extraction by about 18% against the preceding matched baseline, with lower managed allocations. It **still misses both 2x and 3x**. Text fresh-open means are close to the preceding run within its error range; already-open patterns vary substantially and now measure 2.14x, not the baseline's 3.02x. The already-open AdoBye pattern case still has a minimum-iteration-time warning. No across-the-board speedup is claimed. All six preflight documents still pass, and the full suite now passes 312 tests.
