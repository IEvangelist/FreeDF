# Parser and text-extraction benchmarks

BenchmarkDotNet 0.15.8 compares FreeDF with PdfPig 0.1.16 using the same PDF byte arrays. PdfPig's official package is `PdfPig`, verified against the [v0.1.16 release](https://github.com/UglyToad/PdfPig/releases/tag/v0.1.16). It is an Apache-2.0 dependency of this benchmark executable only, not a production library dependency.

Open/page-count comparisons additionally include PDFsharp 6.2.4 (MIT). This
version is intentionally pinned: PDFsharp 6.5.0 changed to AGPL-3.0-only and
requires license initialization. PDFsharp is a benchmark-only dependency.
It is not included in text comparisons because this harness has no equivalent
PDFsharp decoded-text operation. Results describe these versions, not the
latest version of every provider or complete spec-conformance rankings.

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

## Flate decoder workloads

```powershell
dotnet run -c Release --no-build --no-restore --project bench\FreeDF.Bench -- --filter "*FlateBench*" --warmupCount 3 --iterationCount 8 --iterationTime 500 --launchCount 1
```

`FlateBench` uses 256-byte and 1 MiB decoded payloads, each patterned or seeded
pseudorandom. Generation, zlib compression and exact decoded-byte preflight are
outside timing; decoding and materializing the result are timed. Printed
encoded-byte SHA-256 values allow matched-build comparisons on the same
runtime. `--preflight` validates all four cases.

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

### Matched decoder follow-up: 2026-11-05

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
