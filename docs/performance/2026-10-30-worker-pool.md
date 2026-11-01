# Worker pool and tiled rendering, 2026-10-30

Non-AOT Release publish (`Publish-EditorPages.ps1`), headless Chromium via `FreeDF.EditorSmoke --pool`, `navigator.hardwareConcurrency` overridden to 4
(3 render replicas). Fixture: 20-report.pdf + 6 x 09-images.pdf page (12 pages). Times are `pdfHost.image()` wall time in ms. Every scenario's decoded RGBA SHA-256
(240/239 dpi pages, 137 dpi pages, 12 thumbnails, 12 print pages at 150 dpi) equals the single-worker hashes; the run fails otherwise.

| Case | 1 worker | Pool | Speedup |
|---|---:|---:|---:|
| Cold 239 dpi page 0 (incl. replica boot) | 3920 | 1361 | 2.9x |
| Warm 239 dpi page (3 bands) | 406 | 220 | 1.8x |
| 4 uncached pages at 137 dpi (sequential) | 854 | 504 | 1.7x |
| 12 thumbnails at 41 dpi (one Promise.all) | 1857 | 751 | 2.5x |
| Print: 12 pages at 150 dpi (one Promise.all) | 7186 | 2645 | 2.7x |
| Largest frame gap during the run | 79 | 87 | n/a |

CPU throttling 4x (CDP `Emulation.setCPUThrottlingRate`, which throttles the page main thread, not workers): single 3749 / 436 / 853 / 1952 / 7736 ms,
pool 1699 / 296 / 678 / 790 / 2965 ms (cold / warm / navigation / thumbnails / print); frame gap 324 vs 338 ms. The main-thread canvas PNG encode grows
to 50-217 ms under throttling, which is why the warm speedup drops to 1.5x there.

Fallbacks (all hash-identical to single-worker): boot failure (pool disabled after 3 failures), replica crash (disabled after 4), 2 cores and
deviceMemory 2 (no pool created). 2 bands per replica was slower than 1 (warm 238 ms) because each band re-interprets the page content.

Residual limits: 3x was not reached for warm or navigation renders; the per-band fixed cost (content interpretation) and the PNG encode/data-URL conversion
dominate. AOT/SIMD was not measured (the wasm-tools workload is not installed in this environment). The benchmark exercises `host.image()` directly,
not the full `printDoc` UI.
