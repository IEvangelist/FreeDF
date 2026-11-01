# Rasterizer / codec CPU performance (desktop measurements)

Integration onto current main preserves the pool's band origin and full-page
clip coordinates while adopting lazy pooled layers and the new scanner.
The historical golden file predates main's CMYK, ICC, font and annotation
changes. An independent runner using retained pre-merge main assemblies
produces 239 page results on the identical complete fixture/sample set;
all 239 match the merged raster exactly. The golden is updated from that
control (including 21 newly covered pages), not from a tolerated raster change.
The editor's 15 fixed pixel hashes are unchanged.

The later codec integration preserves every one of those 239 entries and adds
78 JPEG fixture pages, bringing the golden corpus to 317. Their decoded image
samples first pass the committed independent `.bin`/native-precision references;
the new raster hashes then establish exact sequential/threaded regressions for
those previously absent pages. No existing hash is replaced or tolerated.
Hierarchical and arithmetic-lossless references are spec-derived, not evidence
of external-decoder certification. The integrated CCITT/filter/JPEG/raster
focus passes 364 tests; the subsequent full Release suite passes 2,540 tests,
including the 317-page sequential/threaded golden checks. This does not
establish a new browser speedup.

Measured with `bench/FreeDF.Bench` (`--raster-bench 240 <iterations> <baseline.tsv>`; `FREEDF_SEQ=1` forces the single-threaded paths that the browser uses),
.NET 10 Release, one benchmark at a time under the shared validation lock. Every row's RGBA SHA-256 matched the pre-change baseline (`same`), i.e. output is bit-identical.
Times are best-of-4 wall ms for rendering **all pages** of the file at 240 dpi (warm, hashing excluded).

| File (240 dpi) | Before | After, sequential (browser-like) | After, threaded (desktop) |
| --- | ---: | ---: | ---: |
| 03-typography-embedded | 321 | 153 | 80 |
| 06-patterns-shadings | 262 | 166 | 150 |
| 09-images | 157 | 92 | 74 |
| text.pdf (1 page) | 354 | 306 | 124 |
| vector.pdf (3 pages) | 1120 | 753 | 236 |
| image.pdf | 58 | ~38 | 31 |
| graphics/transparency | 75 | 51 | 49 |
| graphics/mesh | 33 | 29 | 27 |
| features | 13.4 | 8.1 | 5.9 |

What changed (all exact, same float operations in the same per-pixel order):

* Page group layer is lazily initialised per touched row span (no 66 MB clear), the white page fill is deferred until first use, and large layer buffers use an exact-sized pool with a 64 MiB retention cap instead of power-of-two-sized `ArrayPool` buffers.
* `EdgeScanner`: keyed edge sort instead of a delegate sort, edge order carried between sub-scanlines (near-sorted insertion), vertical-edge rows evaluated once, SIMD interior coverage accumulation and zeroing.
* Opaque/Normal solid painting writes opaque runs with vector stores (page and isolated layers), `Put`/`Comp` dispatch removed from the hot loop, `EndGroup` fast path.
* RGBA conversion is vectorised (Vector128, WASM-SIMD friendly); PNG predictor decoding works in place per filter type.
* Desktop only: solid fills/strokes are deferred and replayed per 8-128 row band in submission order, bands run in parallel (identical output, disjoint rows).

Residual bottlenecks (honest): the sub-scanline scan converter and float layer traffic still bound the sequential path. The desktop table above is not evidence that the browser meets the requested 5x targets.

Current-main integration exposed a deterministic AOT allocation regression:
the large 240 dpi first render allocated 217,060,608 bytes against the unchanged
200,000,000-byte gate (pre-raster control: 195,179,120). Eviction and size-class
experiments did not fix it and were removed. Browser tracing also showed that
the finalizer trimmer ran after every collection on a non-generational host;
it now runs only where generation 2 exists, preserving the cap and 5-second
idle trimming everywhere.

An isolated Normal RGB group covering a pristine white page now reuses that
page's RGB buffer rather than allocating a second full-page buffer. Its
compositing keeps the original white-backdrop float operations, initializes
untouched rows and transparent holes to white, and returns the shared buffer
only once. A cold large-group allocation regression and explicit-white
backdrop comparisons (partial/zero group alpha, Multiply and knockout) cover
the change; the sequential/threaded golden and band checks remain exact.

Fresh AOT on the integration with main `a7b1b0ed` passes the allocation and
pixel gates. Native tests pass (2,243), as do 33 Node invariants and the full,
forms, protection, merge, pool, page-image and boot browser smokes.

| Large 240 dpi primary render | Pre-raster control p50 / p95 | Integrated p50 / p95 |
| --- | ---: | ---: |
| Allocated bytes | 195,179,120 / 195,179,120 | 150,197,848 / 150,197,848 |
| Raster ms | 572.8 / 572.9 | 589.6 / 648.3 |
| PNG ms | 58.3 / 59.9 | 71.8 / 72.8 |
| Engine total ms | 630.7 / 632.8 | 657.0 / 721.1 |

These are three-repeat browser measurements, not the desktop best-of table.
The control predates main's UI modernization, so this is not an isolated
renderer-speed claim. Allocation improves; the measured render is slower and
does not meet the 150 ms target. The full profile still fails unchanged UI
timing gates: cached zoom medians are 181-183 ms, and Save event-frame medians
are 52.9-61.1 ms against the 50 ms ceiling. Those failures block main delivery;
they are not waived by the passing allocation/pixel checks.

An isolated UI control then kept the same published AOT engine, host and pool
and substituted only the archived `6100d68a` index and editor CSS. This control
passes all 535 metric groups, 15 pixel hashes and ten CI-policy controls with
the unchanged reference. Allocation remains 150,197,848 bytes. The comparison
isolates the UI regression rather than attributing it to the renderer:

| Document | Previous UI cached zoom p50 / p95 | Modern UI cached zoom p50 / p95 | Previous UI Save frame p50 / p95 | Modern UI Save frame p50 / p95 |
| --- | ---: | ---: | ---: | ---: |
| Showcase | 99.9 / 100.8 | 181.8 / 182.7 | 14.1 / 14.9 | 55.8 / 57.6 |
| Typography | 99.7 / 101.5 | 182.2 / 199.5 | 14.6 / 14.9 | 54.4 / 63.1 |
| Images | 99.4 / 100.9 | 182.8 / 183.1 | 14.4 / 14.9 | 52.9 / 53.6 |
| Report | 99.4 / 100.7 | 182.7 / 199.6 | 13.9 / 14.5 | 56.9 / 64.2 |
| Large 12-page | 99.4 / 100.2 | 181.9 / 197.4 | 14.5 / 14.7 | 61.1 / 65.0 |

All timings are milliseconds. The previous UI still misses the requested
uniform 12 ms Save target; a passing 50 ms regression gate is not that target.
