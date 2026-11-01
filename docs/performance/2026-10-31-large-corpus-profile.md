# Large-corpus profile: 2026-10-31

Baseline: `origin/main` `2908aa7a`. Optimised: commits `72bf8968` and `22df9972` (Core parser). Corpus, harness and raw data are in this repository (`tools/profile`, `tools/FreeDF.Profile`, `docs/performance/data`); the PDFs are not.

**Goals not met.** The target of being about 5x faster than the public editor numbers is **not met** for rendering or for open on large files. Rendering is still roughly 5-10x *slower* than PDFium/MuPDF on ordinary pages (details below). This report contains one large, measured parser fix, several measured and ranked opportunities, and the areas I could not measure.

## Method

- Hardware/OS: Intel Core i9-10900 (10C/20T, 2.8 GHz base), 63.7 GB RAM, Windows 11 Enterprise 10.0.26200, power plan *High performance*. No CPU affinity pinning, no frequency locking; the machine is shared, so see the noise note.
- Software: .NET SDK 10.0.403 / runtime 10.0.14, Release, workstation concurrent GC (default), TieredPGO on. PdfPig 0.1.16, pypdfium2 5.14.0 (PDFium 156.0.8076.0), PyMuPDF 2.0 (MuPDF 1.28.5), pikepdf 10.17.0 (qpdf 12.4.2), Python 3.13.
- Each (engine, file) pair runs in its **own process** (clean peak working set and GC counters), holding the shared validation slot for that single command only; five iterations or a 30 s budget, whichever ends first; median reported, with the first (cold, JIT-included) iteration shown as `first`. Memory is the process **peak working set across all operations** for that file, so it is an upper bound on any single operation and includes runtime/interpreter baseline.
- Operations measured: open (bytes already in memory), text extraction of 8 evenly spaced pages **including a fresh open**, per-page render at 72 and 150 dpi and a 24 dpi thumbnail (8 sampled pages), full save (rewrite), and whole-document text extraction (`text_all`, 2 iterations) for five files. All engines receive the same bytes. Python engines are driven through their normal Python bindings, so some call overhead is included; pdfium renders include conversion to a NumPy array.
- Not run (see "Not measured"): correctness/pixel diffs between engines, merge/split/redact/flatten/validators timing, ETW/PerfView, browser WASM AOT vs non-AOT, encryption cost beyond open, JBIG2/CCITT/large-JPX synthetic images.
- **Noise.** Repeating identical operations across runs on this shared machine moved medians by up to about +/-30% (for example `save_full` for files my change does not touch ranges 0.6x-1.3x between the two runs). Differences under about 1.3x between runs are **not** evidence of change. Only the large ratios below are claimed.
- Equivalence of the parser changes: the xref table (`Dictionary<int,XrefEntry>`) was hashed for 235 PDFs (repo samples, test fixtures, bench fixtures, corpus) with the old and new build: all identical, plus both damaged 61 MB files (same hash, same 1501 pages). Full test suite 2218/2218 passes after both commits.

## Corpus

9 real-world files (2.2-36 MB, 15-849 pages; text-heavy government/legal, arXiv papers, one 568-page JPX scanned book, one NASA book with many images) and 13 synthetic files (0.8-65 MB, up to 2000 pages): text 300/2000 pages, CJK text, vector art (400 Beziers/page, partial transparency), 40 large 3000x2000 images, a 1500-page mix of all repo samples (classic xref, object-stream/xref-stream and plain variants), AES-256 and RC4-128 encrypted 2000-page text, a 500-revision incremental chain, and two damaged variants of the mix (garbage `startxref`; 4 KB truncated tail). Sizes, URLs, terms and SHA-256 are in `data/corpus-manifest.md|json`. I did not obtain 50-500 MB real files; the largest is 65 MB (synthetic), 36 MB real.

## Headline findings

1. **Damaged-file recovery was pathologically slow and is fixed.** A 61.6 MB, 1501-page file with a bad `startxref` took 15.9 s to open (truncated tail: 14.3 s), 50-70x slower than MuPDF (0.23 s) and 7-13x slower than pikepdf (1.2-1.6 s). A sampling trace showed 95% of the time inside the PCRE2 `N G obj` byte scan. Replaced with a vectorised `IndexOf("obj")` + bounded backward check, and recovery no longer copies every stream while looking for XRef/ObjStm. **Now 0.25 s open** (median over warm iterations; 0.98 s for a whole cold single-shot process including runtime start), same recovered xref, same page count. PdfPig/PDFium fail outright on the truncated variant.
2. **Object streams are now lazy**: xref-stream/object-stream 1500-page file open **316 -> 98-142 ms** (2.2-3.5x depending on run, in-process A/B 344 -> 98 ms). It still parses every page object up front.
3. **Open is dominated by GC, not parsing.** Warm open of the 61 MB mix allocates about 36 MB per open (~20 KB per page), and in a 20-open run the GC paused for about 0.9 s (about half the wall time): 73 gen0 / 43 gen1 / 13 gen2 collections. Experiments: `DOTNET_gcConcurrent=0` cut pauses to 344 ms; `DOTNET_GCgen0MaxBudget=0x10000000` cut median open 83 -> 46 ms and GC count 73 -> 6. These are host settings, not library changes; browser/WASM does not use them.
4. **Whole-document text extraction is already competitive**: 1.0-2.1x of MuPDF\'s time and faster than PDFium on two of five documents, and faster than PdfPig on four of five (see table). Sampled-page text with fresh open: geometric mean 2.0x faster than PdfPig, 0.6x of PDFium, 0.43x of MuPDF.
5. **Rendering is the largest gap.** Geometric mean of PDFium time / ours per page: 0.11 at 150 dpi, 0.14 at 72 dpi, 0.23 for 24 dpi thumbnails (so 4-9x slower than PDFium); MuPDF similar. A 24 dpi thumbnail of a JPX scan page costs 489 ms vs 40 ms (PDFium): thumbnail time equals full-page time (~440-510 ms at 24, 72 and 150 dpi), i.e. **resolution-independent, dominated by full-resolution JPX decode**. (Active work on rasterizer/codec performance is on `ievangelist-rasterizer-codec-perf`; I deliberately did not touch the rasterizer.)
6. **Memory is 3-10x higher than the other engines**: peak working set 959 MB for the 25 MB JPX scan (PDFium 144, MuPDF 296, PdfPig 100), 1.42 GB for the 36 MB NASA book (PDFium 441, MuPDF 419, pikepdf 320), 798 MB for the 8 MB vector file (PDFium 72). Every stream's bytes are copied at parse time (`ReadStream` `ToArray`) and render buffers/layers are large.
7. **Cold start**: first open costs 145-460 ms for real files vs 4-40 ms warm (JIT/tiering, first-use initialisation); PdfPig shows 460-1000 ms cold. PDFium/MuPDF are 1-5 ms cold.
8. **Save is competitive**: geometric mean 1.17x faster than PDFium, 1.6x faster than MuPDF and 1.7x faster than pikepdf (n=20 files; 0.7x-2.2x spread against PDFium).

## Where we win and lose

Geometric mean over the 20 non-damaged files of *other engine median / ours* (above 1 = we are faster); the range is min-max:

| operation | PdfPig | PDFium | MuPDF | pikepdf |
|---|---|---|---|---|
| open (warm) | 2.07 (1.09-5.69) | 0.08 (0.01-1.30) | 0.07 (0.01-0.90) | 1.83 (0.56-3.85) |
| text, 8 pages + open | 1.99 (1.47-2.92) | 0.60 (0.34-1.63) | 0.43 (0.19-1.48) | n/a |
| render 150 dpi / page | n/a | 0.11 (0.05-0.72) | 0.10 (0.03-0.84) | n/a |
| render 72 dpi / page | n/a | 0.14 (0.06-0.68) | 0.09 (0.03-1.02) | n/a |
| thumbnail 24 dpi / page | n/a | 0.23 (0.08-0.62) | 0.39 (0.13-1.70) | n/a |
| save (full rewrite) | n/a | 1.17 (0.68-2.24) | 1.61 (1.02-3.73) | 1.68 (0.98-22.6) |

This table is from the baseline run on `2908aa7a`. PDFium and MuPDF open lazily (about 0.3-5 ms for most files), so ours is an order of magnitude slower at open on every file. Per-file tables for every operation are in `data/2026-10-31-main-tables.md`.

Whole-document text extraction, median ms (fresh open + all pages, 2 iterations; includes cold JIT in the first iteration, shown as the median only because n=2 means median = the larger of two):

| file (pages) | FreeDF | PdfPig | PDFium | MuPDF |
|---|---:|---:|---:|---:|
| irs-p17 (142) | 1497 | 2322 | 956 | 710 |
| 911report (585) | 1177 | 1394 | 929 | 749 |
| cfr-title26 (753) | 1738 | 1702 | 1495 | 1050 |
| nasa-sp-4029 (344) | 1713 | 4248 | 2674 | 1454 |
| syn-text-2000p (2000) | 1721 | 2490 | 2141 | 1680 |

## Scaling

- Text-only synthetic, 300 -> 2000 pages (6.7x): open 4.8 -> 38.4 ms (8.0x), sampled-page text with open 25.5 -> 64.2 ms, render per page unchanged (24-43 ms): per-page render cost is independent of page count and open is close to linear.
- Incremental chain with 500 revisions: open 8.7 ms (6.8 ms after), fine.
- Recovery was linear in bytes but with a very large constant (about 4 MB/s before, about 250 MB/s now).
- No super-linear behaviour was found in the sizes tested (up to 2000 pages, 150k+ objects, 65 MB). I did not test beyond that.

## Hotspots (sampled traces; dotnet-trace SampleProfiler)

| scenario | where the time goes |
|---|---|
| open damaged 61 MB | 94.6% inclusive in `Pcre2ControlSearch.Find` (26.8% exclusive), `Pcre2Kernel.Step` 23% exclusive. Fixed. |
| warm open 61 MB mix | main thread: 82% of samples in the GC suspension/poll path; managed time is `PdfDocument.Walk` -> `GetObject` -> `PdfLexer.ReadDictBody/ReadArrayBody`; `List.AddWithResize`/`Array.Copy` about 18-31% inclusive, `Dictionary` resize 13%, `RuntimeHelpers.GetHashCode` 4% (reference `HashSet` in `Walk`). |
| 24 dpi thumbnail of JPX scan | `ImageDecoder.Decode` -> `JpxCodestream.DecodeTile/Synthesize` (full-resolution tier-1/DWT), `Jbig2Decoder.DecodeGeneric` minor; timing independent of dpi. (Sample percentages are diluted by idle thread-pool threads, so absolute percentages are not quoted.) |

I did not collect allocation-type profiles for the page-walk allocations (only per-phase totals: `PdfFile.Load` 1.1-2.2 MB, page tree walk 31-54 MB). The 20-50 KB/page figure is a total, not attributed.

## Implemented (small separate commits)

| commit | change | evidence |
|---|---|---|
| `72f8642d` | Profiling tooling (`tools/FreeDF.Profile`, `tools/profile/*`) | reproducible corpus run |
| `72bf8968` | Core: byte-scan recovery; skip stream copies during recovery; lazy object-stream members; fixed-width xref entries; no LINQ in xref-stream rows | damaged open 15.9 s -> 0.25 s (64x, median) / 0.98 s (cold single process); object-stream mix 316 -> 98-142 ms; nasa open 22 -> 17 ms; xref hash identical on 235 files; 2218/2218 tests |
| `22df9972` | Core: presize xref dictionary | allocation opening nasa-sp-4029, 20 opens: 468 -> 346 MB (deterministic) |
| `6686783b` | Manifest + downloader + raw data | `Get-Corpus.ps1` verified all nine hashes |

Behaviour notes: a malformed member inside an object stream now fails only when that member is requested (previously the whole stream failed on first access and triggered recovery). Valid files are unaffected. No rendering code was touched, so rendered output is unchanged by construction; I did not run a pixel-hash gate on corpus pages. The Core project still references `GregsEx`, which is now unused by any source file (removable, see opportunity 9).

## Ranked opportunities

Impact is relative to the measured gap, effort S/M/L, risk L/M/H.

| # | Opportunity | Evidence | Est. impact | Effort | Risk |
|---|---|---|---|---|---|
| 1 | Lazy page tree: stop parsing every page dictionary at `PdfDocument.Load` (`Pages` is an eager `List<PdfPage>`); resolve page N on demand and count pages from `/Count` | open 15-140 ms vs 0.3-5 ms (PDFium/MuPDF) on every file; page walk is ~94% of open and ~20-50 KB/page | open 5-20x on large docs; also lowers GC | M (public `Pages` type change) | M |
| 2 | JPX reduced-resolution decode (skip high resolution levels when output scale is low) and JPX/JBIG2 tile-streaming decode | thumbnail = full-page time (489 ms vs 40 ms PDFium); scan peak 959 MB | thumbnail/low-dpi on scanned books 5-10x; peak memory -50% | L | M (needs pixel-hash gates) |
| 3 | Rendering parity work: gate every raster change with this corpus (per-page ms at 24/72/150 dpi; PDFium within 3x target) | geomean 4-9x slower than PDFium | up to 5-9x per page; owned by `ievangelist-rasterizer-codec-perf` | L | M |
| 4 | Stop copying stream bytes at parse (`PdfStream.RawData` as a segment of `Data`/lazy) | `ReadStream` `ToArray()`; 1.42 GB peak on 36 MB file; LOH gen2 collections | memory -20-40%, GC pauses lower | M | M (API/ownership) |
| 5 | Allocation-lean lexer: avoid `string` per ref generation (`TryRef`), reuse `Dictionary`/`List` capacities, avoid per-leaf inherited-dictionary copy in `Walk` | `AddWithResize`/`Array.Copy` 18-31% inclusive, 36 MB per 1501-page open | open -30-50% allocations | S-M | L |
| 6 | Host GC settings documented/enabled for desktop/CLI: `GCgen0MaxBudget` (about 256 MB) and non-concurrent GC | median open 83 -> 46 ms, GC count 73 -> 6, pause 0.93 -> 0.22 s | up to ~1.8x warm open | S | L (memory trade-off) |
| 7 | Startup: ReadyToRun/pre-JIT for desktop tools; trim first-use initialisation | `first` open 145-460 ms vs 4-40 ms warm | cold open 3-10x | S-M | L |
| 8 | Regression gate: run `Invoke-CorpusProfile.ps1` (synthetic subset: damaged recovery, objstm, 2000 text pages, thumbnails) in CI with generous budgets (damaged open < 2 s, objstm open < 250 ms) | recovery regression went unnoticed (15 s) | prevents order-of-magnitude regressions | S | L |
| 9 | Remove unused `GregsEx` reference from Core (and the `Directory.Packages.props` entry) | no source file uses it after `72bf8968` | smaller payload/dependency surface (size not measured) | S | L |
| 10 | Developer loop: incremental `dotnet build` of Profile/tools takes 1.7-7 s; the full suite is 34-36 s. Keep the slot-lock helper (`tools/profile/Invoke-Locked.ps1`) and run corpus A/B with `base-bin` copies to avoid rebuilding over running benchmarks | build/test timings above; two benchmark runs were nearly corrupted by rebuilds over a running exe | saves minutes per iteration | S | L |
| 11 | Text path: remaining 1.0-2.1x gap to MuPDF on whole-document extraction (glyph metadata retention per `bench/FreeDF.Bench/README.md`) | table above | 1.0-2.1x | M | L |

## Not measured / not verified

- Browser/WASM editor (AOT vs non-AOT, Chromium): not re-measured; see `docs/performance/2026-10-30-aot.md` and `2026-10-30-batch2.md` for the existing numbers.
- Pixel and text correctness diffs between engines on the corpus; only page-count/xref equality for the parser change.
- Merge/split, redaction, form flatten, PDF/A and PDF/UA validators, encryption/decryption beyond open, thumbnail caches, search, incremental save timing at scale (harness ops exist for validate/save_incr but were not run at corpus scale).
- JBIG2/CCITT/large-JPX synthetic images and real files above 36 MB.
- Allocation-type profiles (only totals); LOH churn counts besides the GC totals shown; ETW/PerfView; CPU time per operation (recorded in the JSONL as process CPU, not summarised here).
- `ievangelist-rasterizer-codec-perf`, `ievangelist-worker-pool-tiled-rendering` and `ievangelist-startup-payload-caching` were not rebased or measured; the first measured baseline attempt accidentally used a stale local `master` (`d340421c`), so it was discarded, and every number here is from `origin/main` `2908aa7a`.
- Single machine, single OS, no power-plan or affinity control beyond the *High performance* plan; the noise band is about +/-30%.

## Reproduce

```powershell
python tools\profile\gen_synthetic.py $env:CORPUS\syn
.\tools\profile\Get-Corpus.ps1 -OutDir $env:CORPUS\real
dotnet build tools\FreeDF.Profile -c Release
.\tools\profile\Invoke-CorpusProfile.ps1 -Corpus $env:CORPUS -Out results.jsonl -Session <your-session>
python tools\profile\summarize.py results.jsonl
```
