# Rasterizer / codec CPU performance (desktop measurements)

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

* Page group layer is lazily initialised per touched row span (no 66 MB clear), the white page fill is deferred until first use, and large layer buffers are pooled (`ArrayPool` never retained >1M-element arrays, so every large render re-allocated and zero-filled them).
* `EdgeScanner`: keyed edge sort instead of a delegate sort, edge order carried between sub-scanlines (near-sorted insertion), vertical-edge rows evaluated once, SIMD interior coverage accumulation and zeroing.
* Opaque/Normal solid painting writes opaque runs with vector stores (page and isolated layers), `Put`/`Comp` dispatch removed from the hot loop, `EndGroup` fast path.
* RGBA conversion is vectorised (Vector128, WASM-SIMD friendly); PNG predictor decoding works in place per filter type.
* Desktop only: solid fills/strokes are deferred and replayed per 8-128 row band in submission order, bands run in parallel (identical output, disjoint rows).

Residual bottlenecks (honest): the sub-scanline scan converter (about 100 ns per sub-scanline for glyph-sized shapes) and 12-16 bytes per pixel of float layer traffic bound the sequential path; bit-identical output rules out analytic coverage, glyph bitmap caches and 8-bit layers. Browser (single-thread WASM) gains are therefore ~1.2-1.5x here; the larger gains need either relaxing bit-identity or fewer pixels per render.
