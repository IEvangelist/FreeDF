# Parser and text-extraction benchmarks

BenchmarkDotNet 0.15.8 compares FreeDF with PdfPig 0.1.16 using the same PDF byte arrays. PdfPig's official package is `PdfPig`, verified against the [v0.1.16 release](https://github.com/UglyToad/PdfPig/releases/tag/v0.1.16). It is an Apache-2.0 dependency of this benchmark executable only, not a production library dependency.

Open/page-count comparisons additionally include PDFsharp 6.2.4 (MIT). This
version is intentionally pinned: PDFsharp 6.5.0 changed to AGPL-3.0-only and
requires license initialization. PDFsharp is a benchmark-only dependency.
It is not included in text comparisons because this harness has no equivalent
PDFsharp decoded-text operation. Results describe these versions, not the
latest version of every provider or complete spec-conformance rankings.

## Ordinary embedded stream framing: 2026-11-05

OrdinaryStreamBench times fresh PdfFile.Load plus traversal/checksum
of all raw bytes in 64/4,096 ordinary streams with direct or shared
indirect Length. Setup independently checks every stream's exact
bytes, row count, unrepaired state and checksum. Fixture generation,
correctness checks and source/input/MVID reporting are not timed.
This measures ordinary lazy stream loading, not decoding, malformed
repair, external-file loading or a provider-conformance ranking.

The exact archived e8dd5642 Core/control central properties and final
Core are linked by separate minimal BDN executables with matching
project flags and the same checked-in fixture source. All four input
lengths/SHA256 hashes match; engine MVIDs differ. Tests, corpus and
all correctness preflights finish before sequential control/final
timing. The first harness attempt executed zero benchmarks because
its project filename differed from the assembly name. Its failed
logs/NA output are retained separately in session artifacts; only the
corrected matching-name harnesses supply the reports below.

BDN 0.15.8 / .NET 10.0.14 / SDK 10.0.403 / Windows 11 / i7-14700K,
one launch, three warmups, eight 500 ms iterations. Error is the
99.9% confidence-interval half-width.

| Streams / Length | Control +/- error (us) | Final +/- error (us) | Managed bytes/op control -> final |
|---|---:|---:|---:|
| 64 / direct | 20.48 +/- 0.205 | 19.82 +/- 0.732 | 77,168 -> 77,168 |
| 64 / indirect | 22.74 +/- 0.346 | 21.14 +/- 0.773 | 78,872 -> 78,872 |
| 4,096 / direct | 3,059.76 +/- 422.172 | 2,739.69 +/- 66.945 | 4,861,037.60 -> 4,861,039.00 |
| 4,096 / indirect | 2,946.36 +/- 81.916 | 2,914.20 +/- 161.269 | 4,959,509.60 -> 4,959,509.60 |

All four final means are lower. Only the 64-stream indirect-Length
confidence intervals do not overlap; this limited single-run result
does not establish a universal or causal improvement or zero overhead.
Managed counters match in three cases; the +1.4-byte/op large direct
observation is retained without causal attribution or an exact
neutrality claim. Native/retained memory is not measured. These
results do not explain the earlier classic-xref allocation delta.

[Control report](results/2026-11-05-ordinary-stream-control.md),
[final report](results/2026-11-05-ordinary-stream-final.md), matching
CSV and [eight raw GC/source/input/MVID rows](results/2026-11-05-ordinary-stream-gc.csv)
retain the evidence. Reproduce with separate net10.0 BDN 0.15.8
executables linking OrdinaryStreamBench.cs against archived/final Core,
matching assembly/project names, Optimize/TieredPGO enabled and
ServerGarbageCollector disabled; run `--filter '*OrdinaryStreamBench*'
--warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1`
independently for each source.

## Ordinary indirect object closers: 2026-11-05

IndirectObjectBench times fresh PdfFile.Load followed by traversal of
all 64/4,096 integer or dictionary values and a checksum. It exercises
lazy non-stream loads, not merely catalog parsing. Setup separately
checks each value, row count, unrepaired state and checksum; fixture
creation, correctness checks and SHA256/MVID reporting are not timed.
Inputs are synthetic correctness/performance fixtures, not a full
spec-conformance corpus or provider comparison.

The control archives exact 2a61e790 Core and central project/package
properties. Final production adds only the unrepaired non-stream
endobj/bounds guard. Both minimal isolated BDN executables link the
same checked-in IndirectObjectBench source with matching project flags;
all four input lengths/hashes match and engine MVIDs differ. Tests,
corpus and correctness preflights finish before sequential timing.

BDN 0.15.8 / .NET 10.0.14 / SDK 10.0.403 / Windows 11 / i7-14700K,
one launch, three warmups, eight 500 ms iterations. Error is the
99.9% confidence-interval half-width.

| Values / kind | Control +/- error (us) | Final +/- error (us) | Managed bytes/op control -> final |
|---|---:|---:|---:|
| 64 / integer | 7.913 +/- 0.1974 | 8.116 +/- 0.4013 | 26,480 -> 26,480 |
| 64 / dictionary | 15.869 +/- 0.2132 | 16.120 +/- 1.1222 | 65,392 -> 65,392 |
| 4,096 / integer | 597.317 +/- 12.1756 | 606.692 +/- 12.8214 | 1,617,013.21 -> 1,617,013.21 |
| 4,096 / dictionary | 2,450.040 +/- 133.5840 | 2,430.302 +/- 201.0937 | 4,107,378.77 -> 4,107,375.00 |

Three means increase, one decreases; all confidence intervals overlap.
No proven timing improvement, regression or zero-overhead claim.
Managed allocation counters match in three cases; the approximately
-3.77-byte/op observation in the large-dictionary case is retained,
not asserted to be a causal reduction or exact neutrality. Native/
retained memory and malformed-object repair costs are not measured.
This workload does not explain the earlier classic allocation delta.

[Control report](results/2026-11-05-indirect-endobj-control.md),
[final report](results/2026-11-05-indirect-endobj-final.md), matching
CSV and [eight raw GC/source/input/MVID rows](results/2026-11-05-indirect-endobj-gc.csv)
retain the evidence. Reproduce with separate net10.0 BDN 0.15.8
executables linking IndirectObjectBench.cs against archived/final Core,
Optimize/TieredPGO enabled, ServerGarbageCollector disabled; run
`--filter '*IndirectObjectBench*' --warmupCount 3 --iterationCount 8
--iterationTime 500 --launchCount 1` independently for each source.

## Primary xref chain cycle guard: 2026-11-05

A new XrefHybridBench final run compares the primary-cycle guard with
the retained preceding hybrid-target final (faecce41 Core, unchanged
by the later editor-only main integration). The same isolated harness,
fixture source, input hashes/lengths and fresh PdfFile.Load boundary
are used. Preflight checks run outside timing; engine MVIDs differ.
Tests/corpus finish before the final timing run. The retained control
is reused, not rerun. This measures terminating-chain overhead, not
malformed-chain repair cost or complete linearized-file conformance.

BDN 0.15.8 / .NET 10.0.14 / SDK 10.0.403 / Windows 11 / i7-14700K,
one launch, three warmups, eight 500 ms iterations. Error is the
99.9% confidence-interval half-width.

| Rows / updates | Hybrid-target control +/- error (us) | Cycle final +/- error (us) | Managed bytes/load control -> final |
|---|---:|---:|---:|
| 64 / 1 | 4.690 +/- 0.0496 | 4.837 +/- 0.1436 | 23,272 -> 23,272 |
| 64 / 16 | 12.521 +/- 0.6365 | 13.374 +/- 1.1797 | 53,616 -> 53,552 |
| 4,096 / 1 | 261.820 +/- 7.3428 | 271.591 +/- 4.5186 | 882,662.12 -> 882,534.33 |
| 4,096 / 16 | 286.196 +/- 12.1715 | 283.922 +/- 11.6385 | 912,878.11 -> 912,814.32 |

Three means increase and one decreases; all confidence intervals
overlap. These measurements establish neither a timing improvement nor
a regression/zero-overhead result. Observed managed-byte decreases are
retained but are not attributed causally to moving the visited-set check.
No native/retained-memory guarantee or explanation of the historical
classic allocation discrepancy follows.

[Retained control](results/2026-11-05-xref-hybrid-final.md),
[cycle final](results/2026-11-05-xref-primary-cycle-final.md), matching
CSV and [eight raw GC/hash/MVID rows](results/2026-11-05-xref-primary-cycle-gc.csv)
preserve the observations. The isolated reproduction recipe below
applies with the corresponding control/final Core sources.

## Hybrid supplemental-stream validation: 2026-11-05

XrefHybridBench measures fresh PdfFile.Load with 64/4,096 rows and
one/sixteen incremental updates reusing a supplemental xref stream.
Setup checks unrepaired loading, row count and resolved target identity
outside timing, and emits input SHA256 plus engine MVID. Shared streams
are decoded once, not once per update. This synthetic open workload is
not a provider-conformance ranking or a complete hybrid grammar test.

The matched control archives production 44e5a160 with its original
central package/project properties; the final includes only the
supplemental target-kind/separate visited-set change. Both runs compile
the identical checked-in XrefHybridBench source through minimal isolated
BDN executables with matching project flags. Input lengths/hashes match;
engine MVIDs differ. Tests/corpus/preflights finished before timing;
control/final processes ran sequentially, not concurrently.

BDN 0.15.8 / .NET 10.0.14 / SDK 10.0.403 / Windows 11 / i7-14700K,
one launch, three warmups, eight 500 ms iterations. Error is the
99.9% confidence-interval half-width.

| Rows / updates | Control +/- error (us) | Final +/- error (us) | Managed bytes/load control -> final |
|---|---:|---:|---:|
| 64 / 1 | 4.779 +/- 0.1227 | 4.690 +/- 0.0496 | 23,272 -> 23,272 |
| 64 / 16 | 12.596 +/- 0.2779 | 12.521 +/- 0.6365 | 53,552 -> 53,616 |
| 4,096 / 1 | 263.891 +/- 3.5572 | 261.820 +/- 7.3428 | 882,534.19 -> 882,662.12 |
| 4,096 / 16 | 273.971 +/- 4.9803 | 286.196 +/- 12.1715 | 912,878.11 -> 912,878.11 |

Three means decrease; the large sixteen-update mean increases about
4.5%. All confidence intervals overlap: neither a proven timing
improvement nor a regression/zero-overhead conclusion follows. Preserve
the observed +64/~128 managed-byte differences rather than claiming
allocation neutrality or explaining them solely by the new lazy set.
These are managed counters, not native or retained-memory measurements.
This hybrid workload does not resolve the historical classic allocation
discrepancy.

[Control report](results/2026-11-05-xref-hybrid-control.md),
[final report](results/2026-11-05-xref-hybrid-final.md), matching CSV
and [eight raw GC/hash/MVID rows](results/2026-11-05-xref-hybrid-gc.csv)
retain favorable and unfavorable observations. To reproduce the isolated
comparison, archive the control Core plus Directory.Build.props and
Directory.Packages.props; use identical net10.0 BDN 0.15.8 executables
linking XrefHybridBench.cs and referencing either archived or final Core,
with Optimize/TieredPGO enabled and ServerGarbageCollector disabled.
Run `--filter '*XrefHybridBench*' --warmupCount 3 --iterationCount 8
--iterationTime 500 --launchCount 1` separately for each source revision.

## Cumulative xref-stream header/footer guards: 2026-11-05

Matched XrefFilterBench inputs compare the retained generation-bound
control (production b0315639) with later header range/integer syntax,
startxref boundary/integer syntax and intervening classic parser guards.
Classic-only paths are not exercised by these inputs, but their possible
code-layout effects are not isolated. Decoder and fixture code are
unchanged; setup checks correctness outside the timed fresh PdfFile.Load.
Input lengths, SHA256 and decoded hashes match; engine MVIDs differ.

BDN 0.15.8 / .NET 10.0.14 / SDK 10.0.403 / Windows 11 / i7-14700K,
one launch, three warmups, eight 500 ms iterations. The final timed run
is isolated from this slice's test/corpus workloads.

| Filter / rows | Generation control +/- 99.9% half-width (us) | Cumulative final (us) |
|---|---:|---:|
| Flate / 64 | 6.313 +/- 0.1659 | 6.309 +/- 0.1389 |
| Flate / 4,096 | 138.068 +/- 4.2051 | 133.873 +/- 1.1003 |
| LZW / 64 | 5.223 +/- 0.7208 | 4.827 +/- 0.0817 |
| LZW / 4,096 | 247.459 +/- 31.6958 | 217.366 +/- 4.6933 |

All means decrease, all confidence intervals overlap. No proven speedup,
regression, zero-overhead or single-guard attribution is claimed. Rounded
managed allocations remain 11.62/345.03 KB Flate and 12.59/340.87 KB LZW,
not zero or a native/retained-memory guarantee. Raw allocation/operation
counters are retained rather than asserted exactly equal. This different
workload does not explain the earlier classic fallback allocation delta.

[Retained control](results/2026-11-05-xref-guards-final.md),
[cumulative final](results/2026-11-05-xref-header-footer-final.md),
matching CSV and [eight raw GC/hash/MVID rows](results/2026-11-05-xref-header-footer-gc.csv)
preserve the observations.

## Classic allocation diagnostic: 2026-11-05

A bounded diagnostic investigated the approximately 64-byte/op difference
in the historical large-fallback BDN result below without repeating or
discarding that benchmark. Exact production sources were reconstructed
with `git archive` at 0fd028a97ce45feea74342d436090159fbb6037a
(generation control) and 10ca3f9ff699c8165c80ab3bbf340e020dfd0a02
(negative-field plus Size guards), including identical XrefClassicBench
source, central package versions and project properties. Both isolated
Release projects built successfully; no production files were edited.

The same fresh-load workload/preflight ran in separate processes with
DOTNET_TieredCompilation=1 and 0, after 1,024 warmup loads and a 500 ms
pause per case. Three 256-load samples use current-thread allocated-byte
counters immediately around each Load call; serialization, validation,
fixture setup and output are outside those counters. All 48 samples
(two revisions, two tiering modes, four cases, three samples) report
identical allocation values across revisions/modes, including identical
per-operation minima/maxima:

| Rows / path | Both source revisions (bytes/load) |
|---|---:|
| 64 / fixed | 14,336 |
| 4,096 / fixed | 736,040 |
| 64 / fallback compatibility | 16,560 |
| 4,096 / fallback compatibility | 932,648 |

This diagnostic does **not reproduce** the source-revision delta. Its
absolute numbers differ from BDN; it does not explain that discrepancy,
prove a BDN fault, or dismiss the retained 64-byte/op observation. No
allocation reduction, timing, native/retained-memory or universal
zero-overhead claim follows. Fallback fixtures still deliberately use
nonconforming compatibility syntax.

[All 48 raw samples](results/2026-11-05-classic-allocation-diagnostic.csv)
include commit IDs, engine MVIDs and source/fixture SHA256.
[Exact diagnostic source](results/2026-11-05-classic-allocation-Program.cs.txt)
and [project snapshot](results/2026-11-05-classic-allocation-Diagnostic.csproj.txt)
are inert text artifacts, not benchmark-project compilation inputs.
To reproduce, archive each revision's `src/PortableDocument.Core`,
`Directory.Build.props`, `Directory.Packages.props` and
`bench/FreeDF.Bench/XrefClassicBench.cs` into separate temporary roots.
Copy the snapshots to Program.cs/Diagnostic.csproj in a third temporary
directory, then build serially for each source root:

```powershell
dotnet build .\Diagnostic.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:EngineRoot=C:\path\to\archived-root
$env:DOTNET_TieredCompilation = "1" # Repeat in another process with "0".
dotnet .\bin\Release\net10.0\Diagnostic.dll
Remove-Item Env:DOTNET_TieredCompilation
```

## Cumulative classic negative-field and Size guards: 2026-11-05

The same `XrefClassicBench` inputs and fresh-load timed boundary compare
the retained generation-bound final (production 0fd028a9) with the later
negative-offset/free-number guard plus direct-positive-integer Size
schema guard. Decoder and fixture code are unchanged. Identical lengths/
SHA256 and distinct engine MVIDs are verified. Settings and environment
match the generation run below; final timing is isolated from this
slice's test/corpus workloads.

| Rows / path | Generation control +/- 99.9% half-width (us) | Cumulative final (us) |
|---|---:|---:|
| 64 / fixed | 2.702 +/- 0.0278 | 3.443 +/- 0.8154 |
| 4,096 / fixed | 199.027 +/- 3.6679 | 199.631 +/- 3.4871 |
| 64 / fallback compatibility | 4.216 +/- 0.0825 | 5.517 +/- 1.3823 |
| 4,096 / fallback compatibility | 318.211 +/- 5.3483 | 335.045 +/- 13.1715 |

All final means increase, all confidence intervals overlap. These results
do not establish speedup, regression, zero overhead or individual-guard
causality. Fallback compatibility input is not conforming table syntax.
The 4,096-row fallback managed allocation observation increases about
64 bytes/op (932,667.57 to 932,731.55, rounded 910.81 to 910.87 KB).
This difference is unresolved and retained, not asserted unchanged or
attributed without measurement. Other rounded managed sizes are unchanged;
none establishes native/retained memory usage.

[Retained control](results/2026-11-05-xref-classic-bound-final.md),
[cumulative final](results/2026-11-05-xref-classic-size-final.md),
matching CSV and [eight raw GC/input identity rows](results/2026-11-05-xref-classic-size-gc.csv)
preserve the full observations.

Both classic timing series predate the later xref/startxref keyword-
boundary and footer integer-syntax checks; no new timing or allocation
claim is made for those guards.

## Classic xref generation bound: 2026-11-05

`XrefClassicBench` times fresh `PdfFile.Load` for 64/4,096-row classic
tables. Setup verifies indexed loading without repair, row count,
target type and reference/cache identity outside the timed method.
`FixedEntry=true` uses fixed-width entries. The token-based
`FixedEntry=false` input deliberately exercises nonconforming compatibility
syntax, not valid-file conformance. Neither case establishes complete
generation/free-list lifecycle compliance.

The control is production fcbe2021 before the generation-bound edit;
the final changes only fixed/fallback generation checks. Identical input
lengths/SHA256 and distinct engine MVIDs are verified. BDN 0.15.8 on
.NET 10.0.14 / SDK 10.0.403 / Windows 11 / i7-14700K uses one launch,
three warmups and eight 500 ms measurement iterations:

| Rows / path | Control mean +/- 99.9% half-width (us) | Final (us) |
|---|---:|---:|
| 64 / fixed | 2.728 +/- 0.0759 | 2.702 +/- 0.0278 |
| 4,096 / fixed | 202.029 +/- 6.1551 | 199.027 +/- 3.6679 |
| 64 / fallback compatibility | 4.243 +/- 0.0955 | 4.216 +/- 0.0825 |
| 4,096 / fallback compatibility | 321.416 +/- 4.1745 | 318.211 +/- 5.3483 |

All intervals overlap: no verified speedup, regression or zero-overhead
claim. Managed allocations remain 13.94/718.81 KB fixed and
16.11/910.81 KB fallback, rounded by BDN; raw values are retained rather
than asserted exactly equal. These are nonzero managed allocations, not
native/retained-memory or whole-document rendering measurements.
An initial overlapping test/benchmark build hit a file lock before any
measurement; its serial retry succeeded. Both timed runs were isolated
from this slice's test/corpus workloads.

[Control report](results/2026-11-05-xref-classic-bound-control.md),
[final report](results/2026-11-05-xref-classic-bound-final.md), matching CSV
reports and [eight raw GC/input identity rows](results/2026-11-05-xref-classic-bound-gc.csv)
are retained.

These measurements predate the later negative-offset/free-number guard;
they are not timing evidence for that additional check.

```powershell
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --filter "*XrefClassicBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

## Reproduce

From the repository root, with the .NET 10 SDK:

```powershell
dotnet build bench\FreeDF.Bench -c Release
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --preflight
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --filter "*PdfOpenBench*" "*TextExtractionBench*" "*OpenDocumentTextBench*" --join
```

The comparison classes use BenchmarkDotNet's default job (adaptive warmup, pilot, and measurement iterations), not the legacy renderer's short job. A smoke run is useful for correctness but not performance conclusions:

```powershell
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --filter "*PdfOpenBench*" "*TextExtractionBench*" "*OpenDocumentTextBench*" --job Dry --join
```

## Corpus and correctness

Candidates are `bench\fixtures\text.pdf`, `vector.pdf`, `image.pdf`, and `samples\03-typography-embedded.pdf`, `06-patterns-shadings.pdf`, `09-images.pdf`. They cover text, embedded typography, vector graphics, patterns/shadings, and images. Setup reads bytes outside timing.

Before any comparison, preflight opens every candidate with all three engines twice. It requires equal page counts across all engines, stable text across independent FreeDF/PdfPig opens, and equal per-page decoded text for those two engines after removing whitespace. Only whitespace is normalized to account for text-layout differences; text is not reordered, case-folded, or replaced. Mismatches and exceptions fail preflight visibly: no candidate is silently skipped to improve results. `PASS` lines include byte length, SHA-256, page count, and normalized character count so a run's exact corpus can be checked. Blank/image-only pages are valid equal inputs but should not be interpreted as text-heavy workloads.

Both extraction paths return materialized arrays of per-page strings in content order: FreeDF concatenates `PortableDocument.Render.PortableDocument.ExtractText` spans and PdfPig concatenates `Page.Letters` values. Preflight normalization is not timed.

## Measurement boundaries

| Class | Timed work |
|---|---|
| `PdfOpenBench` | Open bytes, obtain page count, dispose where required; not full content decoding |
| `TextExtractionBench` | Fresh open plus all-page text extraction and materialization, with disposal |
| `OpenDocumentTextBench` | First all-page text extraction/materialization on an already-open document |

Both engines receive the same existing `byte[]`. PdfPig 0.1.16 supports `Open(byte[])` directly, so no harness `MemoryStream` adapter or disk I/O is introduced. Any internal copying/adaptation is part of the engine's cost. PdfPig disposal is timed for fresh-open cases; FreeDF documents have no disposal API.

PDFsharp opens the same bytes via a non-writable `MemoryStream`, in `Import`
mode. Creating and disposing that adapter and disposing the document are timed.
Open/page-count is equivalent at the caller boundary, not a requirement for
identical internal eager/lazy parsing or validation work. These are warm-process,
fresh-document measurements, not cold process or disk I/O benchmarks.

The already-open case opens a batch of 32 fresh documents in `IterationSetup` and disposes them in `IterationCleanup`, outside timing. BenchmarkDotNet uses one invocation and an unroll factor of one per iteration, with `OperationsPerInvoke=32` so times and allocations are reported per document. Each document is extracted exactly once, preventing PdfPig's decoded-page cache from turning later invocations into cache lookups while FreeDF repeats interpretation. This measures first extraction after open, not warm cached access. Batching increases measurement time, but blank pages can still produce minimum-iteration-time warnings. Setup-induced GC/cache effects and keeping both engines' unopened document batches alive can affect results; use fresh-open extraction for end-to-end decoding cost. Do not subtract separate benchmark means to estimate extraction cost.

`MemoryDiagnoser` reports managed allocations, not total process/native memory. Provider ratios are divided by the FreeDF baseline for the same document and operation. Results are specific to corpus, runtime, CPU, build, and cache state, not general speed guarantees. BenchmarkDotNet writes mean/error, allocations, ratios, exact commands, OS, CPU, runtime, JIT, and GC information under `BenchmarkDotNet.Artifacts`; preserve those reports when sharing measurements.

Rendering remains in the separate legacy `RenderBench`. PdfPig has no comparable raster-rendering implementation in this harness, so these results make no rendering-parity claim.

## Cumulative xref source/framing/schema guards: 2026-11-05

Same `XrefFilterBench` inputs and timed `PdfFile.Load` boundary as below.
The retained exact-extent final at `6bb7f047` is the control. Final adds
four changes together: external F source detection, strict xref framing,
integer Length schema and type 0/1 generation limits. Decoder code is
unchanged. This is a cumulative comparison, not a measurement of one
individual guard or a universal parser throughput guarantee.

Microsecond means with 99.9% confidence half-widths:

| Filter / rows | Exact-extent control | Cumulative final |
|---|---:|---:|
| Flate / 64 | 7.674 +/- 0.7852 | 6.313 +/- 0.1659 |
| Flate / 4,096 | 157.083 +/- 11.5712 | 138.068 +/- 4.2051 |
| LZW / 64 | 5.987 +/- 0.6212 | 5.223 +/- 0.7208 |
| LZW / 4,096 | 250.124 +/- 14.9056 | 247.459 +/- 31.6958 |

Flate means fall about 18%/12% with non-overlapping intervals; both LZW
intervals overlap. Direct XRef framing short-circuits the object-stream
Type resolver, but these results do not isolate that mechanism from other
guards, JIT layout or environmental variation. They do not prove zero
overhead for any one check. Rounded managed allocation sizes are unchanged;
raw counters, not equality/zero-allocation assumptions, are preserved.

`results/2026-11-05-xref-guards-final.{md,csv}` retains the final run.
Use `2026-11-05-xref-extent-extent-final.{md,csv}` for its existing control.
The new `2026-11-05-xref-guards-gc.csv` retains all eight raw allocation/
operation counts and input/decoded hashes with engine MVIDs. Matched
hashes agree and engine MVIDs differ. The reproduction command/job is the
same as the strict filter-completion section below.

## Exact xref row extents and rejected LZW experiment: 2026-11-05

The decoded-row guard requires exact `Index`/`Size` times `W` length
before publishing entries. It adds one check after the existing
truncation check. Reuse the **strict** control from `502e4804` below;
comparing against the earlier permissive decoder would conflate changes.
The four `XrefFilterBench` inputs, decoded hashes, setup checks and timed
boundaries are identical. Same command/job as the following section.

Microsecond means with 99.9% confidence half-widths:

| Filter / rows | Strict control | Exact-extent final |
|---|---:|---:|
| Flate / 64 | 7.553 +/- 0.6503 | 7.674 +/- 0.7852 |
| Flate / 4,096 | 152.822 +/- 9.0696 | 157.083 +/- 11.5712 |
| LZW / 64 | 5.656 +/- 0.4553 | 5.987 +/- 0.6212 |
| LZW / 4,096 | 242.087 +/- 14.9046 | 250.124 +/- 14.9056 |

All final means are higher and all intervals overlap. Managed allocation
sizes remain the same rounded KB values; raw counters are retained.
These runs establish neither a speedup nor zero overhead/proven
regression. Reports are
`results/2026-11-05-xref-extent-{strict-control,extent-final}.{md,csv}`.

Before that guard, a separate LZW candidate on `502e4804` checked the
initial nine-bit clear code once, initialized the remaining seven bits
outside the decode loop, and reused output length for the size check
and dictionary offset. It removed no EOD/padding/dictionary/size checks.
The candidate passed 1,096 focused tests but **was rejected and reverted**:
all eight matched timing intervals overlap controls, and repeated strict
decoding has a higher mean. Flate controls are unchanged source paths,
yet their means rose, underscoring environmental/code-layout uncertainty.

| Direct LZW case | Control | Rejected candidate |
|---|---:|---:|
| Literal / ordinary | 290.94 +/- 17.484 | 268.728 +/- 17.9742 |
| Literal / strict | 304.85 +/- 8.674 | 292.290 +/- 25.1611 |
| Repeated / ordinary | 12.34 +/- 2.435 | 11.649 +/- 0.5973 |
| Repeated / strict | 11.96 +/- 0.404 | 12.127 +/- 1.1240 |

`results/2026-11-05-lzw-validation-rejected-{lzw-control,lzw-rejected}.{md,csv}`
retain the four direct controls and all eight candidate direct/xref
results. The xref candidate compares with the retained strict xref
control, not an additional remeasurement. The
`2026-11-05-xref-extent-and-rejected-lzw-gc.csv` retains all 20 raw
allocation/operation counts and input/decoded hashes with engine MVIDs.
Matched hashes agree and engine MVIDs differ. No rejected decoder code
is shipped; no optimization claim is made.

## Strict cross-reference filter completion: 2026-11-05

`XrefFilterBench` measures fresh `PdfFile.Load(byte[])` on minimal parser
fixtures with 64 or 4,096 decoded xref rows. Flate and literal LZW encode
the same rows; LZW uses clear codes every 200 literals to keep nine-bit
codes. These are parser fixtures, not page/rendering conformance controls.
Setup generates bytes and checks unrepaired loading, exact entry count,
target identity and stable input/decoded hashes outside timing. The timed
method and bytes are unchanged between control and final.

Control production is `00aa7422ce8d1b7461ebe5e4c5a9eed41d78db66`,
measured before enabling strict xref decoding. Final adds strict codec
validation and local exception translation. Reproduce with:

```powershell
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --filter "*XrefFilterBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

Means and 99.9% confidence half-widths are microseconds:

| Filter / rows | Control | Isolated final | Managed bytes/op (control / final) |
|---|---:|---:|---:|
| Flate / 64 | 6.915 +/- 0.2045 | 7.553 +/- 0.6503 | 11,896 / 11,896 |
| Flate / 4,096 | 146.022 +/- 2.4932 | 152.822 +/- 9.0696 | 353,315.06 / 353,315.15 |
| LZW / 64 | 4.979 +/- 0.1144 | 5.656 +/- 0.4553 | 12,888 / 12,888 |
| LZW / 4,096 | 220.822 +/- 6.5083 | 242.087 +/- 14.9046 | 349,055.36 / 349,055.42 |

Final means are 4.7-13.6% higher. Three confidence intervals overlap;
the small LZW case shows a clear measured cost in this run. This is not
a speedup or a universal regression estimate. The strict input checks
are required; reducing their cost remains explicit performance work.
Managed allocations have the same rounded KB sizes but are not zero
and raw values are retained rather than asserted equal.

`results/2026-11-05-xref-completion-{control,final}.{md,csv}` preserve
the matched measurements. The first final run overlapped corpus/preflight
validation; its separate `final-shared-load` reports are retained as
confounded measurements, not mixed into the isolated comparison.
The `-gc.csv` contains all 12 raw allocation counters, operation counts,
input/decoded hashes and engine module IDs. All corresponding hashes
match and control/final engine IDs differ. No other provider is timed
here: this compares FreeDF validation policies, not conformance rankings.

## Temporary optional-content events: 2026-11-05

ISO 32000-1 8.11.4.5 p236 requires viewer Print/Export recommendations
over the current group state, without persisting the temporary change.
Manual View overrides cannot bypass those recommendations. In the absence
of a temporary recommendation, queries retain manual state or evaluate
View state using the supplied zoom/context.

Control production is `d3420550e362fead4630b69c38703da4f8a78920`,
measured before production edits. The existing `OptionalContentEventBench`
uses identical PDF bytes/hashes, timed method, parameters and selected-event
results in control and final. Only an untimed cross-event assertion changes:
a View-ON group without Print recommendations now stays ON for Print.
These are queries on an already-open in-memory state, not document
initialization, rendering, printing or export throughput.

| Event | Control mean +/- error (ns) | Final mean +/- error (ns) | Managed B/query, control -> final |
|---|---:|---:|---:|
| Export | 99.06 +/- 6.301 | 75.33 +/- 2.513 | 0 -> 0 |
| Print | 94.75 +/- 2.260 | 75.94 +/- 4.190 | 0 -> 0 |
| View | 93.91 +/- 2.675 | 101.95 +/- 13.667 | 0 -> 0 |

Print/Export means are lower with disjoint reported intervals: available
event recommendations no longer require deriving configured state first.
View's intervals overlap; neither an improvement nor a regression is
established by that comparison. These three inputs are not universal
visibility or rendering performance.

New `OptionalContentTemporaryEventBench` adds eight **final-only** cases:
Print/Export, manual override absent/present, temporary OFF recommendation
absent/present. Every fixture starts configured OFF and has a View-ON rule;
manual cases explicitly set ON. Setup requires correct temporary visibility,
restored View visibility, unrepaired parsing and no warnings. It logs input
length/hash and measured engine identity outside timing. Only `IsVisible`
is timed. The unmodified View fallback scans recommendations for both
events and is deliberately retained rather than hidden by cheaper cases.

| Event | Manual ON | Temporary OFF rule | Mean +/- error (ns) | Managed B/query |
|---|---|---|---:|---:|
| Export | No | No | 177.78 +/- 6.637 | 0 |
| Export | No | Yes | 84.71 +/- 4.853 | 0 |
| Export | Yes | No | 75.48 +/- 3.087 | 0 |
| Export | Yes | Yes | 84.40 +/- 6.861 | 0 |
| Print | No | No | 179.11 +/- 7.086 | 0 |
| Print | No | Yes | 79.57 +/- 1.657 | 0 |
| Print | Yes | No | 70.58 +/- 2.036 | 0 |
| Print | Yes | Yes | 77.51 +/- 2.819 | 0 |

Reports: [control](results/2026-11-05-ocg-temporary-control.md)
([CSV](results/2026-11-05-ocg-temporary-control.csv)),
[final](results/2026-11-05-ocg-temporary-final.md)
([CSV](results/2026-11-05-ocg-temporary-final.csv)),
[raw GC/input/output/engine CSV](results/2026-11-05-ocg-temporary-gc.csv).
All fourteen measured cases have zero raw allocated bytes, not a rounded
display of a small allocation. Four new correctness cases additionally
require exactly zero thread allocations over 4096 warmed fallback queries.
The eight new cases join standalone preflight; they have no pre-edit
performance comparator. No total/native/retained-memory, cold-start,
cross-provider ranking or complete lifecycle-parity claim follows.

Commands use three warmups/eight 500-ms iterations/one launch on the
same runtime/machine, without running the regression suite concurrently:

```powershell
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --filter "*OptionalContentEventBench*" "*OptionalContentTemporaryEventBench*" --join --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

## LZW output-indexed dictionary: 2026-11-05

`LzwBench.Decode` measures ordinary and strict decoding of two deterministic
65,536-byte outputs. Literal input uses seeded random byte codes with clear
codes before table exhaustion, crossing all code widths. Repeated input uses
growing next-entry special cases followed by literal completion. Setup
constructs both code streams and requires exact decoded-byte equality;
input length/SHA-256, decoded SHA-256 and measured engine module identity
are logged outside timing. PDF parsing, metadata construction, encoding and
verification are excluded; decoder output materialization and pooled-table
rent/cleared-return are timed. All four cases join standalone preflight.

Control production is `d18e21ecb2f2f1b3644812e33ee3822bb4966356`,
measured before decoder edits. The benchmark source, exact encoded/decoded
hashes and timed API boundary match final production. Instead of allocating
literal and dictionary-entry byte arrays, the decoder stores 4096
offset/length pairs into append-only decoded output. Previous entry plus
the current entry's first byte is already contiguous there. The next-entry
special case copies previous output and appends its first byte. Ordinary
full-table recovery keeps previously addressable entries without allocating
unaddressable ones; strict rejection remains unchanged.

| Pattern | Strict | Control mean +/- error (us) | Final mean +/- error (us) | Managed B/decode, control -> final |
|---|---|---:|---:|---:|
| Literal | No | 761.83 +/- 12.091 | 246.27 +/- 9.726 | 2,515,048 -> 196,656 |
| Literal | Yes | 776.33 +/- 26.474 | 288.06 +/- 16.771 | 2,506,856 -> 196,656 |
| Repeated | No | 27.40 +/- 4.892 | 10.43 +/- 0.399 | 387,088 -> 196,656 |
| Repeated | Yes | 26.69 +/- 5.637 | 10.28 +/- 0.297 | 378,896 -> 196,656 |

Mean decode times are about 61-68% lower on these workloads,
with disjoint reported control/final error intervals. Managed allocation
falls about 92% for Literal and 48-49% for Repeated. Exact bytes derive
from raw GC totals/operation counts, not rounded KB conversions.
The eight-thousand-integer table is pooled (32 KiB requested per concurrent
decode), returned cleared even on failure. Warm-process managed B/op does
not measure cold pool growth, retained pool storage, peak memory or native
allocations. Output growth/final materialization still allocate; this is
not zero-allocation decoding.

Reports: [control](results/2026-11-05-lzw-index-control.md)
([CSV](results/2026-11-05-lzw-index-control.csv)),
[final](results/2026-11-05-lzw-index-final.md)
([CSV](results/2026-11-05-lzw-index-final.csv)),
[raw GC/input/output/engine diagnostic CSV](results/2026-11-05-lzw-index-gc.csv).
Three warmups/eight 500-ms iterations/one launch on the same runtime/machine
do not establish universal throughput. Both timed inputs use EarlyChange 1;
separate correctness tests cover 0/1, long chains, repeated resets, full
tables, malformed recovery and concurrent failure/reuse, including the
existing independent libtiff fixture. No new provider timing/ranking or
complete LZW/PDF conformance claim follows from these measurements.

## Optional-content intent compatibility cost: 2026-11-05

The intent-participation correction is measured against preceding production
`4a8e9c569d671e44b30f61002df733abf168aa09`, captured before implementation.
It uses the unchanged four `OptionalContentMembershipBench` cases described
below: fresh interpreter/counting backend, already-open input, 128 sections,
16 groups, default View intents. Benchmark source, exact input hashes and
expected fill counts match. Intent matching occurs on the first group
lookup and is cached alongside its rendering contribution; public layer
state is unchanged. This measures compatibility-path cost, not excluded
Design groups, visibility-expression performance, PDF open or raster output.

| Policy | Control mean +/- error (us) | Intermediate nullable cache (us) | Final enum cache (us) |
|---|---:|---:|---:|
| AllOff | 84.29 +/- 3.721 | 88.24 +/- 0.642 | 85.04 +/- 2.506 |
| AllOn | 85.56 +/- 1.481 | 103.84 +/- 19.645 | 85.94 +/- 2.968 |
| AnyOff | 86.76 +/- 4.130 | 92.88 +/- 4.653 | 86.69 +/- 2.534 |
| AnyOn | 83.96 +/- 0.569 | 92.28 +/- 5.026 | 87.03 +/- 3.760 |

All twelve workloads allocate exactly **143,552 managed B/interpretation**,
derived from raw GC totals/operation counts rather than rounded KB display.
The intermediate run is retained rather than discarded for its higher
means/variance. Final means range from about 0.1% lower to 3.7% higher than
control, with overlapping control/final error intervals for every case.
This establishes neither a speedup nor zero CPU overhead. The runs use
three warmups/eight 500-ms iterations/one launch on the same runtime/machine
without exclusive-load guarantees; BDN outlier handling remains in reports.

Reports: [control](results/2026-11-05-ocg-intent-control.md)
([CSV](results/2026-11-05-ocg-intent-control.csv)),
[intermediate](results/2026-11-05-ocg-intent-intermediate.md)
([CSV](results/2026-11-05-ocg-intent-intermediate.csv)),
[final](results/2026-11-05-ocg-intent-final.md)
([CSV](results/2026-11-05-ocg-intent-final.csv)),
[raw GC/input/engine diagnostic CSV](results/2026-11-05-ocg-intent-gc.csv).
The intermediate representation was not committed as a separate production
revision; its measurements/child engine identity are retained, not an
independently reproducible source revision or isolated causal experiment.
No provider rankings or new event/Zoom measurements are inferred.

## Optional-content membership interpretation: 2026-11-05

`OptionalContentMembershipBench.Interpret` measures a fresh interpreter and
counting backend on an already-open one-page document. It interprets 128
marked-content sections, each using one OCMD with 16 groups (eight ON/eight
OFF), one rectangle and one of the four membership policies. PDF writing,
loading and fixture validation are outside timing; interpreter/backend
construction, content lexing, path processing, membership evaluation and
fill counting are inside. This is not a raster-render or isolated policy
microbenchmark.

Preflight requires an unrepaired one-page document, no interpreter warnings,
and exactly 128 fills for AnyOn/AnyOff or zero for AllOn/AllOff. All four
cases are included in standalone provider/parser preflight. Control production
is `74bb2a091167ccfabf6f24cc9ed729ffd40d08c8`, measured before production
edits; final uses the same benchmark source and all four exact input
lengths/SHA-256 hashes. The change replaces resolved-group/state lists
and LINQ with two boolean accumulators, still resolving/evaluating every
member rather than skipping later diagnostics once the result is known.

| Policy | Control mean +/- error (us) | Final mean +/- error (us) | Managed B/interpretation, control -> final |
|---|---:|---:|---:|
| AllOff | 108.9 +/- 2.41 | 87.36 +/- 2.085 | 221,376 -> 143,552 |
| AllOn | 110.7 +/- 1.41 | 91.95 +/- 10.452 | 221,376 -> 143,552 |
| AnyOff | 112.6 +/- 2.84 | 89.87 +/- 4.471 | 221,376 -> 143,552 |
| AnyOn | 118.6 +/- 6.04 | 99.48 +/- 9.754 | 221,376 -> 143,552 |

Reports: [control](results/2026-11-05-ocg-membership-control.md)
([CSV](results/2026-11-05-ocg-membership-control.csv)),
[final](results/2026-11-05-ocg-membership-final.md)
([CSV](results/2026-11-05-ocg-membership-final.csv)),
[raw GC/input/engine diagnostic CSV](results/2026-11-05-ocg-membership-gc.csv).
Exact managed bytes are derived from raw totals/operation counts, not rounded
KB display. Savings are 77,824 B/interpretation (608 B per marked section)
on this workload, about 35% less total managed allocation. Final means
are about 16-20% lower; all reported control/final error intervals here
are disjoint. The noisier final AllOn/AnyOn results are retained.

Runs use the same machine/runtime, three warmups/eight 500-ms iterations/
one launch without exclusive-load guarantees. Remaining interpreter/content/
path allocations are included: this is **not zero-allocation rendering**,
native-memory accounting, large-layer scaling or universal throughput.
Provider correctness preflight passes; no provider timings or unrelated
event/Zoom benchmarks are rerun.

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*OptionalContentMembershipBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

The [scoped record](../../docs/spec-backlog.md#2026-11-05-membership-policy-allocation-removal)
lists the compatibility/diagnostic controls and remaining parity limits.

## Conforming optional-content event controls: 2026-11-05

A primary Table 101 recheck (printed p229) caught a fixture issue:
BaseState, if present in the default configuration, must be ON. The first
event benchmarks and recommendation tests instead used BaseState OFF in
that default dictionary. The Table 103 example on printed p234 also uses
OFF there, but it does not override the normative Table 101 restriction.
The earlier event reports are retained as nonconforming-configuration
recovery measurements, **not valid default-configuration controls**.

Corrected fixtures use default BaseState ON plus `OFF [5 0 R]`.
Matching usage still enables the initially OFF group; a nonmatching event
must leave it OFF. Both conditions are now required by benchmark preflight,
in addition to page count, no repair and no warnings. The 38 corrected
behavior controls reproduce the same 27 failures/11 passes on preceding
source, then pass with the existing implementation. The full focused
selector passes 178 cases after correction.

| Query | Control mean +/- error (ns) | Final mean +/- error (ns) | Managed B/query, control -> final |
|---|---:|---:|---:|
| Export | 132.7 +/- 2.24 | 92.92 +/- 5.659 | 144 -> 0 |
| Print | 140.2 +/- 1.45 | 93.70 +/- 0.760 | 144 -> 0 |
| View | 126.3 +/- 3.14 | 91.25 +/- 0.769 | 120 -> 0 |

Reports: [control](results/2026-11-05-ocg-event-conforming-control.md)
([CSV](results/2026-11-05-ocg-event-conforming-control.csv)),
[final](results/2026-11-05-ocg-event-conforming-final.md)
([CSV](results/2026-11-05-ocg-event-conforming-final.csv)),
[raw GC/input/engine diagnostic CSV](results/2026-11-05-ocg-event-conforming-gc.csv).
The corrected inputs are View 703, Print 707 and Export 711 bytes, with
exact hashes matched **within this pair**, not to the earlier event fixtures.
`OptionalContentState.cs` control source is from
`b69a47fa360e2f42b01018c80b60c5f6bd6e4928`; a session-local compile
substitution is inherited by generated BenchmarkDotNet child builds. Normal
production is force-rebuilt afterward and checked by the focused suite.

Production source is unchanged by this fixture-only follow-up. The preceding
19,573-test full run and 435-PDF/870-result unchanged full JSON remain the
production integration evidence; neither is rerun here. Changed test/benchmark
projects build and provider/parser preflight passes. Unaffected Zoom inputs
and timings are not rerun or relabelled. Construction is outside timing;
these same-machine, three-warmup/eight-iteration/500-ms/one-launch results
remain one-group cached-query measurements, not universal rendering,
large-layer, Language/User or native-memory guarantees.

## Optional-content usage recommendations: 2026-11-05

`OptionalContentEventBench` adds View/Print/Export state-query cases.
Each uses one parsed group, one explicit application, matching event/category,
BaseState OFF and an ON recommendation. These historical event fixtures have
the default-configuration issue described in the corrected record above.
Both revisions return ON. Construction/loading is outside timing, the public query returns the
result, and setup rejects repair, wrong page count, wrong visibility or warnings.
All three cases are in standalone correctness preflight. Cross-event categories,
Zoom enabling and conflicting recommendations are tested for correctness,
not compared as equivalent old timings.

The control uses production sources from
`b69a47fa360e2f42b01018c80b60c5f6bd6e4928`, measured before production edits
with the same benchmark source. Existing Zoom 1/3 cases are also rerun.
All five byte lengths/SHA-256 hashes match exactly; the
[diagnostic CSV](results/2026-11-05-ocg-usage-gc.csv) preserves raw GC
totals/operation counts and measured child engine module IDs.

| Query | Control mean +/- error (ns) | Final mean +/- error (ns) | Managed B/query, control -> final |
|---|---:|---:|---:|
| Export | 126.0 +/- 7.21 | 88.66 +/- 1.474 | 144 -> 0 |
| Print | 147.1 +/- 13.32 | 85.76 +/- 3.015 | 144 -> 0 |
| View | 148.2 +/- 14.87 | 84.41 +/- 3.572 | 120 -> 0 |
| Zoom 1 | 98.43 +/- 2.961 | 90.71 +/- 1.843 | 0 -> 0 |
| Zoom 3 | 97.16 +/- 2.566 | 92.43 +/- 4.025 | 0 -> 0 |

Reports: [control event](results/2026-11-05-ocg-usage-control-event.md)
([CSV](results/2026-11-05-ocg-usage-control-event.csv)),
[final event](results/2026-11-05-ocg-usage-final-event.md)
([CSV](results/2026-11-05-ocg-usage-final-event.csv)),
[control Zoom](results/2026-11-05-ocg-usage-control-zoom.md)
([CSV](results/2026-11-05-ocg-usage-control-zoom.csv)),
[final Zoom](results/2026-11-05-ocg-usage-final-zoom.md)
([CSV](results/2026-11-05-ocg-usage-final-zoom.csv)).

State-key names now use literals instead of repeatedly concatenated strings;
event queries resolve the state value once. All five final means are lower,
but these one-group cached-query runs are not whole-render, large-layer,
Language/User or native-memory measurements. Zoom 3 reported intervals overlap.
Runs retain three warmups/eight 500-ms iterations/one launch on the same
machine/runtime; no exclusive machine load is established. Provider correctness
preflight passes, with no provider timing rerun or equivalent independent-reader
interactive usage claim.

Three new exact zero-byte/4,096-query event regressions complement the two
Zoom cases. These allocation-sensitive test classes now share a non-parallel
collection: increased warmup alone had not prevented a one-off 328-byte
focused-run anomaly. No allocation allowance or retry is added; final focused
and full runs satisfy the strict budget. The
[parity record](../../docs/spec-backlog.md#2026-11-05-optional-content-usage-recommendations)
states the primary basis, controls and remaining limits.

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*OptionalContentEventBench*" "*OptionalContentZoomBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

## Optional-content application scope: 2026-11-05

The Table 103 OCGs selection correction reuses both valid, explicitly
selected `OptionalContentZoomBench` workloads. Exact 687-byte inputs and
SHA-256 hashes match the preceding results; omitted/null selection is
tested for correctness, not timed against an old incorrect result.

| Source/run | Zoom 1 mean +/- error (ns) | Zoom 3 mean +/- error (ns) | Managed B/query |
|---|---:|---:|---:|
| [Preliminary fixed](results/2026-11-05-ocg-scope-preliminary.md) ([CSV](results/2026-11-05-ocg-scope-preliminary.csv)) | 107.7 +/- 7.00 | 105.4 +/- 8.17 | 0 |
| [Fresh preceding-source control](results/2026-11-05-ocg-scope-control.md) ([CSV](results/2026-11-05-ocg-scope-control.csv)) | 122.6 +/- 10.02 | 117.8 +/- 6.59 | 0 |
| [Final fixed](results/2026-11-05-ocg-scope-final.md) ([CSV](results/2026-11-05-ocg-scope-final.csv)) | 101.18 +/- 1.348 | 96.68 +/- 5.688 | 0 |

The control compiles `OptionalContentState.cs` from
`bf3e8bd7af4cb92b5b29159d466e3db1643e0910`, with the same benchmark source
and other production sources. A session-local MSBuild compile substitution
is inherited by BenchmarkDotNet's generated child builds through the
`CustomAfterMicrosoftCommonTargets` environment variable. A preliminary
attempt passing that property only to the outer build is **not** a valid
control and is excluded from this table. Normal production was force-rebuilt
after substitution and its assembly hash matched the full-suite-tested
fixed assembly before final checks.

The [diagnostic CSV](results/2026-11-05-ocg-scope-gc.csv) preserves raw GC
totals, operation counts, exact input hashes and measured child engine
module IDs (the preliminary run predates engine-ID logging). The final
means are lower than this fresh control, but earlier samples show substantial
run-to-run variation; there is no exclusive-load or universal speed claim.
All runs retain three warmups/eight 500-ms iterations/one launch on the
same machine/runtime. Managed query allocation remains zero; construction,
many-group scaling, Language/User and whole-render performance are not
measured. Provider correctness preflight passes; provider timing is not rerun.

The exact zero-byte/4,096-query regression assertion is unchanged. Its
warmup increases from 512 to 8,192 after small one-off allocations were
observed in focused runs on both preceding and fixed sources; their cause
is not established. Final focused/full runs and the separate benchmark
allocation results satisfy the budget. The
[scoped parity record](../../docs/spec-backlog.md#2026-11-05-optional-content-application-group-selection)
describes the behavior controls and limits.

## Optional-content zoom: 2026-11-05

`OptionalContentZoomBench.IsVisible` measures a repeated public visibility
query against one parsed OCG, one View application and one Zoom rule
`min=1, max=2`. State construction, PDF writing/loading and preflight are
outside timing; the method returns visibility. Zoom 1 (ON) and 3 (OFF) have
the same correct outcomes on both revisions. The corrected exact boundary
at Zoom 2 is tested separately, not timed as an equivalent old operation.
Both benchmark configurations are included in standalone preflight.

The original production sources are byte-identical to
`32da37a441aaeff867469c571cb9e0ed99e04739`; subsequent reports isolate
the boundary comparison, closure removal and final event-name boxing
removal. All exact fixture lengths/hashes match. The
[diagnostic CSV](results/2026-11-05-ocg-zoom-gc.csv) preserves raw GC
totals/operation counts and input hashes for every stage.

| Stage | Zoom 1 mean +/- error (ns) | Zoom 3 mean +/- error (ns) | Managed B/query |
|---|---:|---:|---:|
| [Original](results/2026-11-05-ocg-zoom-control.md) ([CSV](results/2026-11-05-ocg-zoom-control.csv)) | 118.5 +/- 2.93 | 120.0 +/- 4.46 | 208 |
| [Boundary only](results/2026-11-05-ocg-zoom-boundary.md) ([CSV](results/2026-11-05-ocg-zoom-boundary.csv)) | 117.1 +/- 2.83 | 123.6 +/- 9.69 | 208 |
| [Closure removal](results/2026-11-05-ocg-zoom-closures.md) ([CSV](results/2026-11-05-ocg-zoom-closures.csv)) | 102.6 +/- 1.72 | 107.3 +/- 4.34 | 24 |
| [Final](results/2026-11-05-ocg-zoom-final.md) ([CSV](results/2026-11-05-ocg-zoom-final.csv)) | 95.72 +/- 0.954 | 98.32 +/- 1.889 | 0 |

Final query means are about 19%/18% lower on this workload, and measured
managed allocations fall from 208 B/query to zero. Two regressions also
require zero thread-local bytes across 4,096 warmed queries with correct
results. The cached warning delegate moves one allocation to state creation;
this is not a zero-allocation document/state constructor or full-render claim.
Language/User rules, many-layer/application scaling, direct interpreter
callbacks and native/total-process memory are not measured here.

Runs use Windows 11 / Core i7-14700K / .NET 10.0.14, three warmups,
eight 500-ms iterations and one launch; no exclusive machine load was
established. The boundary-only timing is retained, including its noisier
OFF case. Original/final reported intervals do not overlap, but these
measurements do not imply universal throughput or provider rankings.
All provider/parser correctness preflight passes; provider timing is not rerun.

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*OptionalContentZoomBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-optional-content-upper-zoom-boundary)
records the primary Table 102 basis and analytic/raster controls, not
independently established interactive zoom behavior in reference readers.

## Object-stream parser workloads

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*ObjectStreamBench*"
```

`ObjectStreamBench` measures one and 1,000 dictionary members with both plaintext
and AES-128 object streams. Setup generates parser-only fixtures outside timing,
checks every member's compressed xref status, type and exact integer value,
rejects repaired loads, and verifies the full traversal checksum. These synthetic
fixtures are not full document/conformance controls. Encryption uses fixed
credentials/FileId but random cryptographic material; per-process hashes and byte
counts are printed for provenance, not claimed byte identity across launches.
The writer packs up to 100 objects per stream (including the root); 1,000
members exercise multiple containers, not a single 1,000-member index.
The standalone `--preflight` command also validates all four parser configurations.

| Method | Timed boundary |
|---|---|
| `LoadOnly` | Fresh `PdfFile.Load`; not compressed-member materialization |
| `FreshFirstMember` | Fresh load plus first compressed dictionary lookup, including shared index construction |
| `FreshAllMembers` | Fresh load, every member and value, checksum consumption |
| `CachedMember` | One lookup on the setup-loaded, fully primed file |

Results expose encryption, index construction, scaling and cache costs separately.
Cached timing is deliberately not comparable to fresh access; no cross-operation
baseline/ratio is assigned. Timed methods return the parsed object or checksum
to BenchmarkDotNet. Loading encrypted files includes encryption dictionary
resolution/authentication. These are new baselines, not evidence that the recent
parity fixes sped up or regressed parsing; attributing a change requires matched
before/after source builds with the same fixture bytes and timing settings.

## Parser/provider baseline: 2026-11-05

The [complete 34-case report](results/2026-11-05-parser-providers.md) and
[CSV](results/2026-11-05-parser-providers.csv) retain all measurements against
production revision `b7ab3c5b49f9b5f2c70219baf5f60c6a6493a107`.
The run took 6m 56s on Windows 11 / Core i7-14700K / .NET 10.0.14,
with three warmups and eight 500 ms measurement iterations, one launch:

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*ObjectStreamBench*" "*PdfOpenBench*" --join --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

All six input page counts agree across the three engines; FreeDF/PdfPig text
preflight also passes. On this corpus, fresh open/page-count FreeDF means are
4.06-15.43 us; PdfPig 17.13-42.40 us; PDFsharp 23.55-259.82 us.
These are not full parsing, text, rendering or validation rankings. Reported
provider work can differ because of eager/lazy internals and adapter costs.

The new plaintext one-member parser load allocates 180,872 managed bytes; full
1,000-member traversal allocates 2,926,083 bytes. These allocation baselines
identify optimization targets rather than justify a claim of exceptional
performance. First-member vs load-only means overlap in several configurations;
do not subtract them. Cached lookup reports approximately 1.9-2.3 ns with no
managed allocation, subject to normal sub-nanosecond harness/JIT limitations.
All parser configurations pass member-value/compressed-xref preflight.

Unlike the older run below, this machine has an i7-14700K, and the sample PDFs
have the hashes printed by the current preflight, not the historical sample
hashes. No exclusive OS-wide load guarantee was established; distributions
include visible noise and outlier removals. Do not infer cross-date speedups
or attribute a regression to a specific parity correction from these means.

### Matched Filter-name validation: 2026-11-05

The [before report](results/2026-11-05-filter-names-before.md)
([CSV](results/2026-11-05-filter-names-before.csv)) uses production revision
`7d8a1c437f592995a7e0966710b0f67a5bdfa748`; the
[after report](results/2026-11-05-filter-names-after.md)
([CSV](results/2026-11-05-filter-names-after.csv)) includes the accompanying
object-stream Filter-name correction. Both measure `FreshAllMembers` with
three warmups, eight 500 ms measurement iterations and one launch, on the
same machine/runtime as the parser/provider baseline. Provider and parser
preflight passed. No provider timing was rerun for this scoped change.

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*ObjectStreamBench.FreshAllMembers*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

Plaintext bytes are identical across the two source builds:

| Members | Bytes | SHA-256 |
|---|---:|---|
| 1 | 431 | `490DC1DCAB4152A0A9E396A558B51816A7695D34A24E38713627DC3E1B5B3A46` |
| 1,000 | 12,596 | `49105B35A7D7999B63F302385510034DB987F90BC151C9653CD6962873C2298A` |

| Plaintext members | Before mean (us) | After mean (us) | Before allocated (KiB) | After allocated (KiB) |
|---|---:|---:|---:|---:|
| 1 | 9.640 | 10.16 | 17.94 | 18.00 |
| 1,000 | 580.829 | 587.87 | 1908.39 | 1909.07 |

Timing intervals overlap, particularly the noisy single-member follow-up;
these measurements establish neither a speedup nor zero validation cost.
Managed allocation rises by about 64 bytes per index for the scoped resolver
delegate, less than 0.4% in both plaintext cases. The 1,000-member fixture
spans multiple containers. Warm managed allocation is not total/native or
retained memory.

Encrypted controls pass and all four measurements remain in the reports.
Their random cryptographic material differs across launches, so encrypted
before/after means are not attributed to this correction on matched bytes.
No shared-machine exclusivity or universal throughput guarantee is claimed.
The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-object-stream-filter-names)
records malformed controls separately from these valid Flate workloads.

## Object-stream parameter workloads

`ObjectStreamMetadataBench.FreshMember` measures fresh load and one compressed
dictionary lookup with nondefault TIFF predictor 2. Eight configurations cover
one Flate or LZW filter with dictionary parameters and an ASCIIHex/Flate or
ASCIIHex/LZW pipeline with aligned `[null, dictionary]` parameters, each
direct or indirect. The LZW fixture is a short literal-only nine-bit stream;
it does not benchmark compression, code-width transitions or table resets.
Fixture construction, encoding and preflight are outside timing. Two
independent opens must return the exact member type, preserve cached identity,
retain compressed xref status and avoid repair. All eight configurations are
also part of standalone `--preflight`.

These are complete synthetic parser inputs, not page-rendering or full
document-conformance fixtures. Fresh load, filter metadata resolution, pipeline
decoding, predictor application and member materialization are timed together.
They differ from `ObjectStreamBench`'s dictionaries and xref encoding; do not
subtract separate means or compare unlike operations as validation overhead.

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*ObjectStreamMetadataBench*" "*ObjectStreamBench.FreshAllMembers*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

### Strict marker extent: 2026-11-05

The [existing metadata cases](results/2026-11-05-marker-extent-existing.md)
([CSV](results/2026-11-05-marker-extent-existing.csv)) and
[single-array cases](results/2026-11-05-marker-extent-new.md)
([CSV](results/2026-11-05-marker-extent-new.csv)) measure the accompanying
strict ASCIIHex/ASCII85/RunLength marker-position checks against production
base `f02910ffa764524073b79060b0c9904d1efb905f`. The preceding
[single-filter follow-up](#single-filter-array-parameters-2026-11-05) supplies
the matched control; all twelve exact input hashes and measured managed
B/op remain identical. Raw diagnostic totals and input provenance are
retained in the [GC CSV](results/2026-11-05-marker-extent-gc.csv).

Four two-filter cases exercise the changed strict ASCIIHex path. Their
Flate direct/indirect means move from 7.622/8.204 to 7.379/7.053 us;
LZW direct/indirect move from 7.595/7.346 to 6.936/6.602 us. Unchanged
named-filter/single-array controls also move: across all twelve cases,
eight means fall and four rise, with every reported mean/error interval
overlapping. This is not evidence of an isolated speedup or zero overhead.
These are small fresh-load/member workloads, not large ASCII throughput
measurements, ASCII85/RunLength performance evidence or provider rankings.

The same three-warmup/eight-iteration/500-ms/one-launch command in the
single-filter follow-up below is used
on the same Windows 11 / Core i7-14700K / .NET 10.0.14 environment. No exclusive
machine load was established. Provider/parser preflight passes; no provider
timings are rerun. Allocations cover managed memory, not total/native memory.
The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-strict-ascii-and-runlength-input-extent)
separates strict rejection from unchanged ordinary recovery.

### Single-filter array parameters: 2026-11-05

`SingleFilterArrayMetadataBench` reuses the existing fixture and correctness
preflight with `Filter [/FlateDecode]` or `[/LZWDecode]` and a single nondefault
Predictor 2 dictionary. Four configurations cover direct/indirect parameter
dictionaries without duplicating the two-filter pipelines. All four are now
included in standalone preflight. The ordinary named-filter and two-filter
configurations retain their exact prior bytes and hashes.

The [new-operation report](results/2026-11-05-single-filter-array-new.md)
([CSV](results/2026-11-05-single-filter-array-new.csv)) measures the shared-reader
correction on production base `2fff5e9d76fae30372067518347ab1b7069097b1`.
The array/dictionary fixtures failed before the correction, so these are
successful-operation baselines, not speedups over failed decoding.

| Codec | Indirect parameters | Mean +/- error (us) | Managed B/op |
|---|---|---:|---:|
| Flate | false | 7.684 +/- 1.3861 | 19,976 |
| Flate | true | 6.603 +/- 0.1540 | 19,600 |
| LZW | false | 5.980 +/- 0.0735 | 37,256 |
| LZW | true | 6.174 +/- 0.1798 | 36,880 |

The [eight existing operations](results/2026-11-05-single-filter-array-existing.md)
([CSV](results/2026-11-05-single-filter-array-existing.csv)) were measured in
the same invocation. Compared with the retained
[preceding-source report](results/2026-11-05-flate-extent-final.md), five means
fell and three rose; every reported mean/error interval overlaps. Exact input
hashes and managed bytes per operation match for all eight. This does not
establish an isolated timing change, zero overhead or universal performance.
The new array inputs differ from named-filter inputs, so their separate means
must not be subtracted as a decoder-validation cost.

The [diagnostic CSV](results/2026-11-05-single-filter-array-gc.csv) preserves
input lengths/hashes and raw allocated-byte/operation totals for all twelve
cases. Byte-per-operation values use BenchmarkDotNet 0.15.8's .NET 10
round-to-even calculation, not reverse conversion from its rounded KB display.
The run uses Windows 11 / Core i7-14700K / .NET 10.0.14, three warmups,
eight 500-ms iterations and one launch; no exclusive machine load was established.
All provider/parser preflight passed, but no provider timing was rerun here.
Managed allocations exclude native/total-process memory.

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*ObjectStreamMetadataBench*" "*SingleFilterArrayMetadataBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-single-filter-array-parameters)
retains the independent reference disagreement and primary Table 5 basis;
these workloads do not establish full filter or document conformance.

### Matched strict LZW control: 2026-11-05

The [original control](results/2026-11-05-strict-lzw-control.md)
([CSV](results/2026-11-05-strict-lzw-control.csv)) uses production sources
byte-identical to `0a7fefebe5678d6a345a3af03d042763b2f9b345`, with the new
eight-case harness. The [strict candidate](results/2026-11-05-strict-lzw-final.md)
([CSV](results/2026-11-05-strict-lzw-final.csv)) validates LZW code streams,
uses strict predictors and initializes the literal table at the required first
clear code, avoiding a redundant initialization. Both use three warmups,
eight 500 ms measurement iterations and one launch, on the same Windows 11 /
i7-14700K / .NET 10.0.14 machine. All eight preflight input hashes match.
The four original Flate hashes below remain unchanged; new LZW hashes are:

| Filters | Indirect parameters | Bytes | SHA-256 |
|---|---|---:|---|
| 1 | No | 479 | `AB67D73FDDB7F0A40E179F4675A030538B072276E5A884CC1061E551C5A86065` |
| 1 | Yes | 480 | `B5E5C79C5227B7ADB9EDAD20CEF37995319BC77AA1D0536539B46DA899D56BD8` |
| 2 | No | 544 | `D0773263D94E6BB4EFD77F279D7F91FB44E90D7EBE8EDAE7FB088FC5EF0C0F18` |
| 2 | Yes | 545 | `CF473F0DB7728640E39B3C321ACE601509C08304780C1B3B8526A43B545BBA58` |

| LZW filters / indirect | Original mean (us) | Strict mean (us) | Original / strict allocated (KiB) |
|---|---:|---:|---:|
| 1 / No | 12.627 | 9.050 | 44.12 / 36.12 |
| 1 / Yes | 12.332 | 8.706 | 43.74 / 35.74 |
| 2 / No | 13.476 | 9.823 | 45.90 / 37.90 |
| 2 / Yes | 13.244 | 8.582 | 45.30 / 37.30 |

Each LZW operation allocates 8 KiB less, about 17-18% of these complete
fresh-load/member operations. Managed allocations in all four Flate controls
remain identical. Timing is less attributable: the unchanged one-filter/direct
Flate control changes from 8.148 to 8.855 us, while the other three Flate
controls fall from 14.279-15.095 to 8.397-9.479 us. The LZW means' decline
therefore cannot isolate validation or initialization cost from machine/run
state. Both complete reports retain errors, distributions and all eight cases.
No exclusive machine load, universal speedup or total/native-memory saving is
claimed. Earlier dated four-case metadata reports below are Flate-only.
Provider correctness preflight passes; provider timings were not rerun here.
The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-strict-lzw-code-streams)
keeps malformed-stream acceptance separate from these valid-path measurements.

### LZW extent follow-up: 2026-11-05

The [extent candidate](results/2026-11-05-lzw-extent.md)
([CSV](results/2026-11-05-lzw-extent.csv)) adds a final-byte consumed-input
comparison to strict LZW. Its matched control is the strict candidate above,
production revision `0fb56ab8905b9ffa26e702a583f6418d2a15e3b0`;
the benchmark harness, all eight fixture hashes and measurement settings
are unchanged. Provider/parser/parameter preflight passes.

| LZW filters / indirect | Previous mean (us) | Extent mean (us) | Both allocated (KiB) |
|---|---:|---:|---:|
| 1 / No | 9.050 | 7.269 | 36.12 |
| 1 / Yes | 8.706 | 8.134 | 35.74 |
| 2 / No | 9.823 | 8.964 | 37.90 |
| 2 / Yes | 8.582 | 8.900 | 37.30 |

All eight reported managed allocations remain identical, retaining the
preceding LZW 8 KiB reduction. Three LZW means fall and one rises; all four
reported timing intervals overlap. Unchanged Flate means also move in both
directions. No speedup, zero-cost or exclusive-load guarantee is inferred.
The [scoped extent record](../../docs/spec-backlog.md#2026-11-05-lzw-end-of-data-extent)
separates invalid excess-byte controls from these valid operations.

### Flate completion follow-up: 2026-11-05

The [completion candidate](results/2026-11-05-flate-completion.md)
([CSV](results/2026-11-05-flate-completion.csv)) adds a strict-only input
stream that throws on unexpected requested EOF while the inflater is unfinished.
It neither reads compressed data one byte at a time nor scans compressed bits
twice. The matched control is the extent candidate immediately above,
production revision `54abed8eee1b1a7dd2930b5cec6840b41658a6c6`.
The harness, all eight fixture hashes and measurement settings are unchanged;
all provider/parser/parameter preflight configurations pass.

| Flate filters / indirect | Previous mean (us) | Completion mean (us) | Both allocated (KiB) |
|---|---:|---:|---:|
| 1 / No | 7.468 | 7.459 | 19.24 |
| 1 / Yes | 7.708 | 7.597 | 18.87 |
| 2 / No | 9.166 | 8.502 | 21.04 |
| 2 / Yes | 9.946 | 10.043 | 20.44 |

All eight reported managed allocations are identical. Three Flate means fall
and one rises; all four reported intervals overlap. The unchanged LZW means
also vary. Results retain the prior LZW allocation reduction, not a universal
speedup or zero-cost assertion. The
[scoped completion record](../../docs/spec-backlog.md#2026-11-05-completed-flate-inflation)
distinguishes actual inflater completion from the remaining buffered
consumed-input boundary.

### Matched parameter-schema control: 2026-11-05

The [metadata control report](results/2026-11-05-filter-metadata-control.md)
([CSV](results/2026-11-05-filter-metadata-control.csv)) uses exact production
sources from `766fa9117b3e362cdd2d0a583626a69472709bab` and the new benchmark
harness. The [candidate report](results/2026-11-05-filter-metadata-after.md)
([CSV](results/2026-11-05-filter-metadata-after.csv)) uses the parameter-schema
correction. Both use the settings above on Windows 11 / i7-14700K / .NET 10.0.14.
All four metadata input hashes match across builds:

| Filters | Indirect parameters | Bytes | SHA-256 |
|---|---|---:|---|
| 1 | No | 483 | `6E31C2072F1916702C2EC7F4F6113C8D88EBB4CDDF85C9FBEADAB187C06F599C` |
| 1 | Yes | 484 | `83BC2FA0909AB319F936C8C9451E4E4BF9002D6E601370EEA15E03705065739A` |
| 2 | No | 550 | `8971C7124F942E668C75CA4D7645080E4F801619CECAF1FBD0182E401B8BB5E8` |
| 2 | Yes | 551 | `93160FE0E5D8AE7B8523E832467130B7AEA9AFEC2AD4F8863487420FBD8283F4` |

| Filters / indirect | Original mean (us) | Candidate mean (us) | Original / candidate allocated (KiB) |
|---|---:|---:|---:|
| 1 / No | 8.387 | 7.655 | 19.24 / 19.24 |
| 1 / Yes | 8.195 | 7.634 | 18.87 / 18.87 |
| 2 / No | 8.929 | 8.618 | 21.04 / 21.04 |
| 2 / Yes | 8.149 | 8.419 | 20.44 / 20.44 |

The [full-traversal control](results/2026-11-05-decode-parms-control.md)
([CSV](results/2026-11-05-decode-parms-control.csv)) and
[candidate](results/2026-11-05-decode-parms-after.md)
([CSV](results/2026-11-05-decode-parms-after.csv)) retain the common no-parameter
workloads too. Both plaintext hashes match the table in the Filter-name
follow-up above. Plaintext one-member means are 12.71 / 11.19 us and
1,000-member means 654.46 / 674.65 us; reported allocations are identical
at 18.00 / 1908.91 KiB respectively. The 1,000-member candidate mean is about
3.1% higher; its reported timing interval overlaps the original control.
All metadata-case intervals also overlap. This is not a speedup or a
zero-overhead guarantee.

The candidate was measured first. Its apparent slowdown against the earlier
Filter-name run triggered rebuilding and measuring the original production
source, rather than dismissing the result. That original source also became
slower than the earlier run, so the earlier means cannot attribute this
session's full timing shift to parameter validation. The repeated original
control, including its noisy single-member case, is retained in full.
Afterward the candidate production sources were restored byte-for-byte,
rebuilt and passed focused tests and all benchmark preflight configurations.

These remain one-launch shared-machine measurements, not controlled OS-wide
exclusivity or universal throughput bounds. Managed allocation excludes
native/retained memory. Encrypted traversal controls pass but their random
bytes differ across launches; no matched-byte encrypted attribution is made.
The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-object-stream-decodeparms-value-types)
records malformed controls and the remaining cardinality/codec limits.

### Matched decoder-completion control: 2026-11-05

The [original traversal report](results/2026-11-05-decoder-completion-control.md)
([CSV](results/2026-11-05-decoder-completion-control.csv)) uses exact production
`ea53866f478a65d7f8af9fd296538930df2f931a`. The
[candidate traversal report](results/2026-11-05-decoder-completion-after.md)
([CSV](results/2026-11-05-decoder-completion-after.csv)) adds the remaining-codec
check before object-stream index construction.
The [four candidate metadata cases](results/2026-11-05-decoder-completion-metadata.md)
([CSV](results/2026-11-05-decoder-completion-metadata.csv)) retain the nondefault
parameter pipeline workloads too. All use the same three warmups/eight 500 ms
iterations/one launch and machine/runtime as the parameter-schema follow-up.

Both plaintext input hashes match the Filter-name table above. Original /
candidate plaintext means are 11.06 / 10.42 us for one member and
808.64 / 769.10 us for 1,000 members; their timing intervals overlap.
Reported managed allocations remain 18.00 / 1908.91 KiB respectively, unchanged
across original/candidate. No speedup or zero-cost claim is supported.

The candidate was measured first. Its 1,000-member mean was notably above
the earlier parameter-schema run, so exact original production was rebuilt
and remeasured immediately. That control also slowed and has wide intervals;
the older result is not sufficient to attribute the shared-machine timing
shift to the added check. Both full reports are retained without suppressing
the noisy distribution. The candidate check was then restored, rebuilt and
passed focused tests plus all benchmark preflight configurations.

Encrypted controls pass but their random bytes differ across builds, so no
matched-byte timing attribution is made. These are warm managed allocation
reports, not native/retained memory measurements or OS-wide-exclusive runs.
The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-object-stream-decoder-completion)
explains that codec completion is not an implemented image-decoding pipeline
for object streams.

### External-payload containment follow-up: 2026-11-05

The [four metadata cases](results/2026-11-05-external-stream-metadata.md)
([CSV](results/2026-11-05-external-stream-metadata.csv)) measure the accompanying
object-stream F check against the immediately preceding
[completion-guard source](results/2026-11-05-decoder-completion-metadata.md),
revision `6e6e82cee31035e1980ee8163c3f5165d1cc5798`.
All four inputs have the same hashes as the parameter-workload table above.
They have no external F payload; this measures the additional absent-entry
path, not external I/O or external-decoder throughput. No external files are
opened. Settings remain three warmups/eight 500 ms iterations/one launch on
the same machine/runtime. All benchmark preflight configurations pass.

| Filters / indirect | Previous mean (us) | New mean (us) | Previous / new allocated (KiB) |
|---|---:|---:|---:|
| 1 / No | 7.548 | 8.079 | 19.24 / 19.24 |
| 1 / Yes | 7.510 | 8.392 | 18.87 / 18.87 |
| 2 / No | 8.279 | 8.993 | 21.04 / 21.04 |
| 2 / Yes | 9.211 | 8.536 | 20.44 / 20.44 |

All reported timing intervals overlap; allocations are unchanged.
The higher means in three cases and wide intervals are retained, not converted
into a speedup or zero-cost claim. These are one-launch shared-machine results,
not total/native/retained memory measurements or universal performance bounds.
The [scoped parity record](../../docs/spec-backlog.md#2026-11-05-object-stream-external-payload-containment)
distinguishes unsupported external-payload containment from actual external
stream support.

### Strict object-stream filter follow-up: 2026-11-05

The [original traversal control](results/2026-11-05-filter-completion-control.md)
([CSV](results/2026-11-05-filter-completion-control.csv)) uses exact permissive
production `5ca4a2bac6a7604ea171a727ed6ec08c3494a75d`.
The [initial strict candidate](results/2026-11-05-filter-completion-initial.md)
([CSV](results/2026-11-05-filter-completion-initial.csv)) uses the existing strict
decoder, including per-byte Adler reduction. The
[final strict candidate](results/2026-11-05-filter-completion-final.md)
([CSV](results/2026-11-05-filter-completion-final.csv)) reduces Adler sums per
bounded 5,552-byte block without changing accepted checksum/header values.
All use three warmups/eight 500 ms iterations/one launch on the same machine
and runtime as the preceding follow-ups. Both plaintext fixture hashes match
across original/initial/final.

| Plaintext members | Original mean (us) | Initial strict mean (us) | Final strict mean (us) | Allocated each (KiB) |
|---|---:|---:|---:|---:|
| 1 | 10.94 | 20.69 | 10.50 | 18.00 |
| 1,000 | 700.80 | 869.73 | 781.15 | 1908.91 |

The initial candidate was measured first; its cost triggered exact original
production measurement and inspection of the checksum loop. The bounded
accumulator removes two modulo operations per decoded byte; it does not skip
checksum validation or remove the strict decoding requirement. Seven worst-case
buffer-boundary tests and existing random/corrupt decoder controls cover it.
The full initial reports are retained, not replaced by the better final run.

Final original/candidate timing intervals overlap, but the 1,000-member final
mean remains about 11.5% higher than permissive production. This is a visible
cost/uncertainty, not evidence that stricter validation is free. The lower
single-member final mean is not a speedup guarantee. These shared-machine,
one-launch results cannot isolate every JIT/scheduling contribution.
Reported managed allocation is unchanged and excludes native/retained memory.
Encrypted configurations pass but differ in random bytes across launches;
no matched encrypted throughput attribution is made.

The nondefault parameter workloads are retained for both the
[initial strict decoder](results/2026-11-05-filter-completion-metadata-initial.md)
([CSV](results/2026-11-05-filter-completion-metadata-initial.csv)) and
[final strict decoder](results/2026-11-05-filter-completion-metadata-final.md)
([CSV](results/2026-11-05-filter-completion-metadata-final.csv)).
All deterministic hashes match the table above and all preflight configurations
pass. These include strict predictor application in addition to filter decoding.
No new provider ranking or full codec-parity claim is made; see the
[scoped parity record](../../docs/spec-backlog.md#2026-11-05-object-stream-strict-filter-decoding).

## Flate decoder workloads

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*FlateBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

`FlateBench` uses 256-byte and 1 MiB decoded payloads, each patterned or seeded
pseudorandom, in both ordinary and strict modes. Generation, zlib compression
and exact decoded-byte preflight are
outside timing; decoding and materializing the result are timed. Printed
encoded-byte SHA-256 values allow matched-build comparisons on the same
runtime. `--preflight` validates all eight cases. Ordinary mode calls `Flate`
directly; strict mode uses the existing internal filter dispatcher with one
Flate filter/default parameters, a reused filter list and an identity resolver.
The benchmark assembly has friend access, not an added public API or runtime
dependency. Compare each mode against the same source-build mode, rather than
subtracting unlike call-boundary means to estimate validation overhead.

Strict input reserves only the final source byte from bulk reads, preventing
read-ahead from hiding excess data. The inflater must actually complete at
exactly the end of the source; its native checksum validation replaces the
redundant managed Adler scan. This adds at most one final single-byte input
read, not per-byte throttling of the whole compressed input. Output scratch
reads and pooling below are unchanged.

The inflater borrows an 81,920-byte scratch range from `ArrayPool<byte>.Shared`,
retaining the previous read request size and output-limit checks. A `finally`
returns the whole rental with clearing enabled, including decoder errors and
limit exceptions, so decrypted stream bytes are not retained uncleared in the
shared pool. This removes repeated scratch-array allocation, not decoded-result
allocation. The current pool bucket is 128 KiB and can remain retained per
thread; first use can allocate that larger rental. Warm managed-allocation
reductions do not establish lower cold-process or total retained memory.
Existing permissive recovery, strict checksum checks and the 1 GiB inflated
size limit are unchanged.

The output stream starts with capacity equal to the encoded length capped at
64 KiB, rather than sizing itself from the first short native read. This avoids
unfavorable repeated growth on large incompressible inputs while bounding the
up-front allocation. It does not preallocate the inferred decoded size.

### Matched Flate extent and native checksum: 2026-11-05

The [control](results/2026-11-05-flate-extent-control.md)
([CSV](results/2026-11-05-flate-extent-control.csv)) uses the decoder
byte-identical to `9cc66b1fa142f4a4d5add3605d680df6ce7238db`, with the new
strict-mode harness and benchmark friend visibility. The
[initial candidate](results/2026-11-05-flate-extent-initial.md)
([CSV](results/2026-11-05-flate-extent-initial.csv)) reserves four trailer
bytes individually; the [final candidate](results/2026-11-05-flate-extent-final.md)
([CSV](results/2026-11-05-flate-extent-final.csv)) reserves only the last byte.
Both candidates use native checksum validation rather than a second managed
output scan. All use three warmups/eight 500 ms iterations/one launch on the
same Windows 11 / i7-14700K / .NET 10.0.14 machine.

| Strict decoded bytes / input | Control mean (us) | Four-byte mean (us) | One-byte mean (us) |
|---|---:|---:|---:|
| 256 / patterned | 2.234 | 2.446 | 2.090 |
| 256 / random | 2.494 | 2.531 | 2.030 |
| 1 MiB / patterned | 1411.499 | 1046.686 | 797.934 |
| 1 MiB / random | 775.246 | 658.671 | 340.777 |

The final strict large-input means fall about 43%/56%, but unchanged ordinary
large-input means also fall from 1055.154/455.629 to 796.553/336.079 us,
about 25%/26%. Machine/run state therefore accounts for some of the shift;
these are not universal or wholly isolated speedup measurements. Do not
subtract separate means to estimate validation overhead. Small Flate reported
allocations are identical (992/952 B); large Flate allocations vary by less
than 0.03% across the three reports, not a claimed total-memory reduction.

| Plaintext traversal | Control mean (us) | Four-byte mean (us) | One-byte mean (us) | Both endpoint allocated (B) |
|---|---:|---:|---:|---:|
| 1 member | 12.121 | 20.897 | 9.840 | 18432 |
| 1,000 members | 721.705 | 737.110 | 612.174 | 1954728 |

The initially slower small-path result is retained. The final single-member
interval overlaps the control; the 1,000-member mean is about 15% lower,
subject to the same shared-machine caveat. All eight Flate preflight hashes
(four unique inputs) and both plaintext hashes match exactly. Encrypted
controls are retained but cryptographic randomness prevents matched-byte
timing attribution across launches.

The eight candidate metadata cases are also retained, with all hashes matching
the preceding completed-inflater control report above. Final Flate means are
6.755-7.835 us, versus 7.459-10.043 us in that control; unchanged LZW controls
also vary. No provider timing was rerun; all provider/parser/metadata/Flate
preflight configurations pass. These are valid-path performance controls,
not spec-conformance rankings or zero-cost assertions. The
[scoped extent record](../../docs/spec-backlog.md#2026-11-05-exact-flate-input-extent)
records malformed-input behavior separately.

### Matched decoder follow-up: 2026-11-05

This earlier four-case decoder report measures ordinary mode only.

The [original control](results/2026-11-05-flate-before.md)
([CSV](results/2026-11-05-flate-before.csv)) retains the original inflater/output
allocation implementation from `bf48d4bd`. The
[final report](results/2026-11-05-flate-after.md)
([CSV](results/2026-11-05-flate-after.csv)) uses pooled scratch plus bounded
initial output capacity. Both use the same four encoded-byte hashes, runtime,
machine and three-warmup/eight-500-ms-iteration/one-launch settings above.

| Decoded bytes / input | Original mean (us) | Final mean (us) | Original allocated (KiB) | Final allocated (KiB) |
|---|---:|---:|---:|---:|
| 256 / patterned | 2.454 | 2.279 | 80.94 | 0.97 |
| 256 / random | 2.203 | 2.045 | 80.94 | 0.93 |
| 1 MiB / patterned | 889.409 | 781.331 | 3585.19 | 3511.19 |
| 1 MiB / random | 467.571 | 363.783 | 5193.24 | 3009.03 |

Small-input timing intervals overlap; the reliable result is approximately
98.8% less managed allocation, not a small-input speedup claim. Large-input
means are about 12% and 22% lower in this matched control, with the random-input
allocation about 42% lower. One shared-machine launch is not a universal
throughput guarantee. Original-vs-final reports expose confidence intervals.

The intermediate
[pooling-only control](results/2026-11-05-flate-pooling-only.md)
([CSV](results/2026-11-05-flate-pooling-only.csv)) is deliberately preserved:
it reduced allocation but made large random-data decoding slower. A repeated
original-code control confirmed the signal; the pooling-only change was not
retained on its own. Bounded output capacity addresses that measured growth
cliff without changing read sizes or recovery policy.

Nine boundary/concurrency/corruption tests pass on both original and final
code, including a canonical independently encoded empty-zlib fixture.
Final Release: 19,238 passed, zero failed/skipped. Final solution build:
zero warnings/errors. A fresh pinned 435-PDF/870-result full JSON corpus and
the sixteen retained encryption-chain native outcomes are wholly unchanged.
This behavior-preserving performance change does not add a spec-parity claim.

The [final sixteen parser cases](results/2026-11-05-objectstream-flate-after.md)
([CSV](results/2026-11-05-objectstream-flate-after.csv)) retain the same timing
settings as the original 34-case baseline. Both plaintext fixture SHA-256
hashes are identical before/after. One-member fresh-load allocation falls
from 180,872 to 17,152 bytes (about 90.5%); 1,000-member fresh full traversal
falls from 2,926,083 to 1,954,024 bytes (about 33.2%). These are warm-process
managed allocations, with the pool-retention caveat above. Encrypted controls
also pass and their full measurements are retained, but random encryption
material makes their bytes differ across builds; no matched-byte encrypted
speedup is attributed. Cached lookup still has no managed allocation.

## Flate encoder workloads

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*FlateEncodeBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

`FlateEncodeBench` measures complete zlib encoding for empty, 256-byte and 1 MiB
seeded-pseudorandom payloads. Setup generates inputs and independently
decompresses the output through the runtime's zlib reader; empty input must
produce the canonical complete eight-byte stream, not a zero-byte result.
Encoding and result materialization are timed. `--preflight` validates all
three configurations. SHA-256 values identify the encoded bytes.

The [2026-11-05 report](results/2026-11-05-flate-encode.md)
([CSV](results/2026-11-05-flate-encode.csv)) uses the same machine/runtime and
three-warmup/eight-500-ms-iteration/one-launch settings as the decoder follow-up.
Means are approximately 3.0 ns / 9.40 us / 18.57 ms for empty / 256-byte /
1 MiB inputs. Empty encoding allocates 32 managed bytes for its independent
eight-byte array; nanosecond timing is subject to harness/JIT limitations.
These are new encoding baselines, not performance comparisons with the former
invalid zero-byte encoding or with other compression levels/providers.
Nineteen tests additionally preserve exact nonempty compressed bytes and verify
empty annotation/sample authoring; see the
[scoped parity record](../../docs/spec-backlog.md#2026-11-05-complete-empty-flate-encoding).

## Serialized refresh: 2026-10-30

The [43-case report](results/2026-10-30.md) and [CSV](results/2026-10-30.csv)
retain the six-corpus parser/text comparison plus seven render/JPEG/parser
cases. Release preflight passed all six inputs; the measurement completed all
43 cases in 17m 05s. Environment remains Windows 11 / Core i9-10900 /
.NET SDK 10.0.403 / .NET 10.0.14. A shared validation lock excluded other
coordinated .NET/Node/browser workloads for this command, not unrelated OS
activity. Exact measurement arguments:

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*PdfOpenBench*" "*TextExtractionBench*" "*OpenDocumentTextBench*" "*RenderBench*" --join --warmupCount 6 --iterationCount 15 --iterationTime 1000 --launchCount 1
```

The renderer's job label still says `ShortRun`, but the report/CSV confirm
**six warmups and fifteen one-second measurement iterations**, not the
attribute's short defaults. There is no corresponding checked-in historical
renderer run, so 115.922 ms text / 260.196 ms vector / 23.405 ms image and
8.557 / 17.164 ms baseline/progressive JPEG are a new baseline, not a claimed
render speedup. Parser-only `RenderBench.Parse` is 18.816 us.

The fixed `bench\fixtures` inputs retain their original bytes:

| Fixture | Bytes | SHA-256 |
|---|---:|---|
| text.pdf | 13273 | D6E01E37007C1BE70253FE983B7AAC72958DC2C379AF774CF50E897692A68216 |
| vector.pdf | 153694 | 35133AE41A9FB1F432B7C287F35A7792A11CE72CADE9E9DDC35CE5C2B5975714 |
| image.pdf | 403807 | 7E30EA13A0B91E8B9BFD532107DDDE1D912013111B8CBE8E2A4C2717102343BE |

Samples 03 / 06 / 09 were regenerated with the C# authoring tool during
integration; they are **different bytes** from the 2026-10-29 corpus. Their
current same-input two-engine comparisons remain valid, but cross-date
differences cannot isolate library speed. Current SHA-256 values are
B78B0FF94FEEDEBE472671B79B8D6C671175C90149B89C91FB48EF5A6FE83738 /
559A8146672A1A1597BDE96BBC2211AE8CDE68040AB8436B3C9807391EC64682 /
66442101158EC936566FAE96914CC0A1401222EACB61E2410DF4C7A9AC663115.

Selected fixed-corpus comparisons (microseconds, means; historical text/vector
extraction uses the later text-path control rather than superseded baseline):

| Operation / fixture | Previous | Current | Interpretation |
|---|---:|---:|---|
| Open text | 20.967 | 18.780 | Lower mean, not attributable to the image-only change |
| Open vector | 13.835 | 12.959 | Small cross-run difference |
| Open image | 7.656 | 7.360 | Small cross-run difference |
| Open + extract text | 4404.1 | 4716.248 | About 7% higher; current 99.9% error is 243.315 us; monitor |
| Open + extract vector | 5700.9 | 5796.548 | Within current 320.512 us error; no win/regression claim |
| Open + extract image | 92.832 | 86.805 | Lower mean; blank-text workload, not text throughput |

The text fresh-extraction mean is a regression signal, not dismissed as
unrelated: PdfPig also moved from 11876.1 to 12446.381 us (about 5%), and
PortableDocument's current interval overlaps the prior mean's interval.
The image conversion fast path is not executed by text-only interpretation.
A matched control is needed before attributing this cross-date change.
Already-open cases still warn about short iterations (minimum 4.952 to
66.592 ms in affected cases) and multimodal distributions; do not rank their
small differences. The renderer has no comparison baseline, so joined ratio
columns show `?` for it, as expected.

The editor-specific matched before/after changes and remaining latency misses
are recorded [separately](../../docs/performance/2026-10-30.md).

FreeDF's text-only interpreter retains decoded text, spans, glyph bytes, geometry, advances, and form traversal, but does not paint paths, rasterize glyphs, decode image pixels, or build editor graphics-object lists. `Analyze` and rendering retain those operations. Typeface fallback still depends on installed fonts; both engines' font caches and JIT are warmed before the timed measurements, and no cold-process startup claims are made.

## Profiling

The explicit workload runs repeated fresh opens/extractions without BenchmarkDotNet's harness:

```powershell
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --profile-text text.pdf 1000
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1:5 --output text.nettrace -- dotnet bench\FreeDF.Bench\bin\Release\net10.0\FreeDF.Bench.dll --profile-text text.pdf 1000
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

The libraries and static entry class were subsequently renamed to `PortableDocument.*` / `PortableDocument.Render.PortableDocument`. Current benchmark method labels use `PortableDocument`; historical reports retain their original method labels, while the tables below use the FreeDF name. Reproduction commands use the renamed benchmark project. These naming changes are not new performance measurements.

These are machine-specific measurements, not general guarantees. Environment: Windows 11 25H2 build 26200.9711; Intel Core i9-10900 at 2.80 GHz (10 physical / 20 logical cores); .NET SDK 10.0.403; .NET 10.0.14 x64 RyuJIT x86-64-v3; concurrent workstation GC; TieredPGO enabled. Both engines ran in the same BenchmarkDotNet executable with stock .NET GC. No Satori GC or NativeAOT substitution was used.

Exact measurement arguments (the output directory was session-local):

```powershell
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --filter "*PdfOpenBench*" "*TextExtractionBench*" "*OpenDocumentTextBench*" --join --warmupCount 6 --iterationCount 15 --iterationTime 1000 --launchCount 1
```

The final run completed all 36 cases in 12m 58s. All six corpus documents passed the two-engine, repeated-open per-page text preflight; none were skipped. The typography sample's differing raw extraction lengths are whitespace/layout differences: after removing whitespace, both engines produce the same 2,308 characters in the same per-page order. Normalized counts for text, vector, image, typography, patterns, and image samples are 27,148 / 0 / 0 / 2,308 / 751 / 594.

The [full BenchmarkDotNet report](results/2026-10-29.md) and [CSV](results/2026-10-29.csv) retain means, errors, standard deviations, allocations, GC counts, and ratio distributions for all 36 cases. The table below pairs them into 18 comparisons. Times are **microseconds per document**, managed allocation is **KB (1,024 bytes)**, and the ratio is **PdfPig mean / FreeDF mean** (larger favors FreeDF). This quotient of reported means can differ slightly from BenchmarkDotNet's mean of the ratio distribution.

| Operation | Document | FreeDF mean (us) | PdfPig mean (us) | FreeDF KB | PdfPig KB | Mean ratio |
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

The final byte-span lexer pass alone reduced vector fresh-open extraction from 9.629 to 6.414 ms and allocations from 16,991.19 to 7,169.46 KB in matched jobs; [pre-lexer CSV](results/2026-10-29-before-lexer.csv) preserves that control. FreeDF still allocates more than PdfPig on the image fixture and vector extraction. These residual costs and target misses are intentionally reported.

BenchmarkDotNet warned about short iterations for already-open typography/pattern/image cases despite batching, and multimodal already-open distributions. Fresh-open and open-only measurements met the 100 ms minimum-iteration recommendation. Treat the already-open measurements as noisier diagnostics, not a basis for narrow ranking claims. Setup/lifetime/cache differences make subtraction or comparisons between benchmark classes invalid.

Validation: Release benchmark build succeeded; all 311 existing and added tests passed. Added regression tests cover numeric token tolerance/overflow, cmap priority/duplicate/fallback behavior and zero repeated-lookup allocations, repeatable glyph geometry/bytes/advances, and text-only versus full-analysis parity on font, feature, and graphics fixtures.

### Targeted path-only follow-up

A later, narrowly scoped pass skips path construction/painting/clipping operators only when graphics drawing is disabled and no graphics-object collection is requested. It still parses the operands and retains text-relevant graphics state, CTM changes, colors, forms, and `q`/`Q` restoration. Full analysis and rendering are unchanged. A new parity test covers every path/paint/clip opcode followed by transformed text and restored text state.

The [targeted CSV](results/2026-10-29-text-path.csv) contains all 12 new measurements. These supersede the corresponding six extraction comparisons above; the full-corpus table remains a labeled baseline, not a claim that unmeasured cases were rerun. Environment and timing settings are unchanged. Exact timing arguments:

```powershell
dotnet run -c Release --no-build --project bench\FreeDF.Bench -- --filter "*Text*vector.pdf*" "*Text*06-patterns*" "*Text*text.pdf*" --join --warmupCount 6 --iterationCount 15 --iterationTime 1000 --launchCount 1
```

Times are microseconds per document; allocations below retain BenchmarkDotNet's reported MB precision (1 MB = 1,048,576 bytes). Ratios are quotients of reported means.

| Operation | Document | FreeDF mean (us) | PdfPig mean (us) | FreeDF MB | PdfPig MB | Mean ratio |
|---|---|---:|---:|---:|---:|---:|
| Open + extract | 06-patterns-shadings.pdf | 851.9 | 2,631.4 | 1.90 | 3.48 | 3.09x |
| Open + extract | text.pdf | 4,404.1 | 11,876.1 | 6.22 | 13.62 | 2.70x |
| Open + extract | vector.pdf | 5,700.9 | 7,637.3 | 5.72 | 5.54 | 1.34x |
| Already open, first extract | 06-patterns-shadings.pdf | 2,214.1 | 4,745.1 | 1.85 | 3.38 | 2.14x |
| Already open, first extract | text.pdf | 4,420.0 | 12,036.0 | 6.18 | 13.55 | 2.72x |
| Already open, first extract | vector.pdf | 7,636.4 | 7,796.7 | 5.69 | 5.48 | 1.02x |

Vector fresh-open extraction improves by about 11% and already-open vector extraction by about 18% against the preceding matched baseline, with lower managed allocations. It **still misses both 2x and 3x**. Text fresh-open means are close to the preceding run within its error range; already-open patterns vary substantially and now measure 2.14x, not the baseline's 3.02x. The already-open FreeDF pattern case still has a minimum-iteration-time warning. No across-the-board speedup is claimed. All six preflight documents still pass, and the full suite now passes 312 tests.

### Bounded post-gate profile and rejected inflater experiment

A Release profile of 500 fresh `vector.pdf` opens/extractions after the path gate still attributes 14.87% exclusive CPU samples to `PdfLexer.TryNumber`, 6.45% to `ReadObject`, 6.34% to Flate decoding, and 5.92% to Latin1 string creation. Whole-process sampling also includes background/runtime work (23.83% in `Monitor.Enter_Slowpath`), which is not attributed to an application lock without stack evidence. Sampled allocation totals were approximately 1.73 GB byte arrays, 429 MB `PdfReal`, 274 MB strings, 260 MB `PdfKeyword`, 96 MB double arrays, 66 MB LINQ numeric iterators, and 53 MB graphics-state clones. These are aggregate sampled estimates for 500 repetitions, not per-document allocation measurements. Path geometry is no longer a leading allocator.

The final bounded experiment pooled only the 81,920-byte inflate scratch buffer, returned it in `finally`, cleared returned storage, and retained the original read chunk size and output ownership. Both new output-independence/raw-deflate-fallback regression tests and the full 314-test experimental suite passed. All six strict corpus preflights continued to pass. The [eight-case experimental CSV](results/2026-10-29-inflate-rejected.csv) is retained, but **the pooling change and experiment-only tests were reverted**: fresh vector CPU time did not improve, and the text change was not compelling against the concurrently faster PdfPig control. Lower allocations alone did not justify retaining this change as a vector-speed fix.

Same machine/runtime/stock GC and measurement settings as above; exact experimental filters were `"*Text*vector.pdf*" "*Text*text.pdf*"`. Times are microseconds, allocations MB (1,048,576 bytes), and ratios are PdfPig/FreeDF quotients of means:

| Operation | Document | FreeDF mean (us) | PdfPig mean (us) | FreeDF MB | PdfPig MB | Mean ratio |
|---|---|---:|---:|---:|---:|---:|
| Open + extract | text.pdf | 4,246 | 11,161 | 5.83 | 13.62 | 2.63x |
| Open + extract | vector.pdf | 5,861 | 7,933 | 5.48 | 5.54 | 1.35x |
| Already open, first extract | text.pdf | 4,262 | 11,107 | 5.79 | 13.55 | 2.61x |
| Already open, first extract | vector.pdf | 6,756 | 7,881 | 5.45 | 5.48 | 1.17x |

Compared with the retained path-gate control, fresh vector went from 5,700.9 to 5,861 us (about 2.8% longer), while already-open vector was noisy (experimental error +/-783.7 us). Text's roughly 3.6% shorter fresh mean accompanied a roughly 6% shorter PdfPig mean. Thus the retained implementation remains the preceding 12-case result/control, including vector **1.34x fresh / 1.02x already-open**, not 3x. Further eliminating token object allocations or decompression copies needs broader parser ownership/API changes and is deferred rather than attempted as an unbounded rewrite.
