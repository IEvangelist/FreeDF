# Rasterizer / codec CPU performance (desktop measurements)

Integration onto current main preserves the pool's band origin and full-page
clip coordinates while adopting lazy pooled layers and the new scanner.
The historical golden file predates main's CMYK, ICC, font and annotation
changes. An independent runner using retained pre-merge main assemblies
produces 239 page results on the identical complete fixture/sample set;
all 239 match the merged raster exactly. The golden is updated from that
control (including 21 newly covered pages), not from a tolerated raster change.
The editor's 15 fixed pixel hashes are unchanged.

Measured with `bench/AdoBye.Bench` (`--raster-bench 240 <iterations> <baseline.tsv>`; `ADOBYE_SEQ=1` forces the single-threaded paths that the browser uses),
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
| Raster ms | 572.8 / 572.9 | 589.6 / 642.4 |
| PNG ms | 58.3 / 59.7 | 71.8 / 72.7 |
| Engine total ms | 630.7 / 632.6 | 657.0 / 714.7 |

These are three-repeat browser measurements, not the desktop best-of table.
The control predates main's UI modernization, so this is not an isolated
renderer-speed claim. Allocation improves; the measured render is slower and
does not meet the 150 ms target. The full profile still fails unchanged UI
timing gates: cached zoom medians are 181-183 ms, and Save event-frame medians
are 52.9-61.1 ms against the 50 ms ceiling. Those failures block main delivery;
they are not waived by the passing allocation/pixel checks.
