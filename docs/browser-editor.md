# Browser editor and GitHub Pages

The Pages build runs the **same editor UI and C# PDF engine** as
`tools/AdoBye.EditorWeb`, entirely in browser WebAssembly. There is no PDF API
server. Opening a PDF reads a local `File`; parsing, rendering, edits and exports
stay in a dedicated local browser worker. Downloads use local blobs. There is no telemetry,
external font request, PDF upload, or service worker. The browser now retains
the previous editable session locally in IndexedDB and asks before resuming it;
download important documents rather than relying on browser storage.
The host still makes ordinary GET requests for its static app/runtime assets.
Replacing item lists or removing/importing pages prunes uploaded image bytes
that no live page item, retained UI undo snapshot, or editor clipboard references; shared asset IDs
remain available while any live item references them. Item/history updates are
atomic and ordered. Unclaimed uploads expire after ten minutes (swept on the next
editor command), or can be explicitly cancelled with `POST /api/asset-cancel?id=...`.
Successful same-ID upload retries renew the unclaimed lease. Neither expiry nor
cancellation removes live-item, undo-history, or clipboard references.
The UI keeps pending upload files until an item save succeeds, allowing failed
saves to retry with the same asset ID even after expiry; abandoned pending files
are cancelled when their references are released.
Clearing navigation history releases its references; removed pages' item lists
are discarded. Copy/paste retains image bytes across navigation and source-page
removal; replacing the clipboard releases its previous reference. Successfully
opening or creating a document clears the clipboard and pending upload files.

## Publishing

After merging, enable **Settings > Pages > Build and deployment > Source:
GitHub Actions**. If the `github-pages` environment has branch protection rules,
allow the repository's default branch (`main`). No secret or personal token is
needed. Bootstrap the site by running **Publish browser PDF editor** manually,
or let the next push to `main` do it.

The configured project metadata formerly said `master`, but the canonical
`origin` now reports `main` and has no remote `master` ref. The workflow follows
the actual default: pushes to `main` and manual dispatch publish; pull requests
to `main` build/test only. Manual dispatch from a non-default branch cannot
deploy. Concurrent runs cancel superseded work. Deployment uses the official
Pages artifact/deployment actions and only the deploy job gets `pages: write`
and `id-token: write`; all jobs have `contents: read`.

The expected address after a successful deployment is
**https://ievangelist.github.io/csharp-pdf/**. Adding the workflow does not itself
mean the site is live. The implementation session does not push or deploy.

The app's `<base href="/csharp-pdf/">` is inserted by
`tools/Publish-EditorPages.ps1`. Styles, bootstrap scripts and `_framework`
assets resolve beneath it. The editor has one document route; there is no
history-based router requiring a Pages 404 rewrite. No special MIME overrides,
cross-origin isolation headers, threads, COOP/COEP, or server configuration are
required on Pages. `.nojekyll` preserves the `_framework` folder.

### Startup failures

The worker's .NET resource loader retries failed network requests and HTTP 5xx
responses up to four total attempts, waiting 250, 750 and 1750 ms between attempts.
Each fetch has a 15-second deadline. Retries revalidate the same static URL instead
of reusing a cached failure; successful first attempts retain normal browser caching.
The loader preserves the runtime-provided resource integrity hash. Runtime-level
download retries are disabled so they cannot multiply this budget. The initial
`dotnet.js` module import also has four attempts, using a fresh query only after
a failed import because browsers cache failed module imports by URL.
Unchanged downloads have no additional delay. No PDF bytes enter this loader.
An HTTP 4xx response exposed to the loader fails immediately. Chromium can instead
reject an error response or corrupt body as a generic fetch/integrity `TypeError`;
these indistinguishable failures use the same four-attempt bound, without ever
disabling integrity verification.

If startup fails, the inactive editor is replaced by an accessible error with a
keyboard-operable **Reload editor** button. A 90-second worker startup deadline
also covers a module graph or runtime initialization that never settles.
Reload does not clear the previous IndexedDB recovery copy. The HTTP editor does
not use the WASM loader and keeps its existing behavior.

Pages serves the observed runtime assets with `Cache-Control: max-age=600`,
ETags and `Vary: Accept-Encoding`; this repository cannot configure custom Pages
response headers. Compression is disabled in this publish, and there is no
service worker. The runtime manifest in `dotnet.js` names fingerprinted assemblies
and native/runtime modules. A cached manifest crossing a deployment can therefore
refer to an asset that no longer exists; Reload is the recovery action, not an
integrity bypass or a fallback to an unrelated asset.

The observed live cold-boot failures were HTTP 503s for varying existing WASM
assets, not missing filenames or integrity mismatches. The same URLs returned
HTTP 200 on direct checks; default Chromium later succeeded too. This supports
transient delivery failure, but does **not** establish HTTP/2 stream exhaustion.
The SDK already limits downloads to 16 concurrent requests; its former retries
used the same cache policy with only a short final delay. Asset bundling, trimming,
and concurrency are unchanged rather than speculatively changing the PDF runtime.

`AdoBye.EditorSmoke --boot <published-wwwroot> <evidence-directory>` measures
three fresh-context cold starts and injects transient HTTP 503/network/module
failures, permanent failures, missing resources and corrupted integrity payloads.
It also simulates a worker that never becomes ready. It checks recovery,
terminal errors, bounded attempts, keyboard Reload, mobile
layout and context-wide static-GET-only traffic (including the worker).
`--boot-baseline` measures just the three unmodified cold starts. Both use the
profile harness's Chromium viewport/device scale, CDP-disabled cache, navigation
clock and two animation frames before recording readiness. Timings are local,
not claims about public-site network latency.
In the boot-hardening comparison, two three-context runs per version, ordered
hardened/baseline/baseline/hardened under one validation lock, measured medians
of **888 ms before / 914 ms after** (six observations each; +26 ms, 2.9%).
Individual values ranged 843-1027 ms before and 881-1044 ms after. This is a
small sampled difference within substantial run-to-run variation, not proof of
zero overhead or of public-network performance.

## Editor operation inventory

| Existing editor action | Browser host |
|---|---|
| New document, paper sizes/orientation; open picker; PDF drop | Local; dropping onto an open document imports its pages, as in EditorWeb |
| Rendered pages/thumbnails; navigation; fit width/page; zoom | C# software renderer in WASM; no PDF.js or remote rendering |
| Select/move/resize/delete/recolour existing editable graphics | Same content-rewriter operations, including object transforms and reset |
| Select existing editable text; add text; typography/colour/alignment/spacing | Selection/focus/cancel does not replace PDF content. Actual text changes reuse supported original embedded glyphs and text transforms; the PDF renderer paints edited originals rather than approximate browser fonts |
| Rectangle, ellipse, line, PNG/JPEG insertion; fill/stroke/opacity | Same builder and image decoders |
| Copy/paste, duplicate, arrange, bring to front, undo, geometry lock | Same UI operations and existing undo scope (object edits, not a document-wide history) |
| Redaction | Same content-removing redactor, not a painted-cover substitute |
| Forms: text, button and choice fields | Same field-value edits; existing UI represents choice values as text |
| Page rotate/delete/reorder/duplicate; blank insert; merge/import | Same page editor |
| Title/document metadata; search/case matching/highlights | Same local session |
| Save / Save as / Download; PDF page-range extraction | Downloads a copy; cannot overwrite the original filesystem path |
| Reload / previous edit recovery | Explicit Resume previous edit or Discard and clear; unencrypted editable state, undo and clipboard remain local in IndexedDB |
| PNG, all-pages PNG ZIP, text, compressed PDF exports | Local downloads |
| Print, fullscreen, keyboard shortcuts, panels | Browser APIs and existing editor UI; printing uses locally rendered page images |
| Password-protected export | Not available in WASM: .NET browser lacks the required synchronous PDF cryptography. The browser menu explicitly offers unprotected compression instead; the server keeps protected export |
| Password/certificate-protected input | The existing open UI has no password/certificate prompt. Unsupported browser cryptography reports an explicit server-editor limitation. Successfully opened/imported encrypted input disables recovery; decrypted bytes and passwords are never cached for recovery |
| Installed fonts and system font fallback | Browser cannot enumerate host fonts. New text offers the five standard PDF families; original embedded fonts can still be reused. Non-embedded fonts render using bundled, licensed Bitstream Vera Sans regular/bold substitutes, not exact serif/monospace/italic/Symbol/Dingbats/CJK fidelity. For exact appearance and broader character coverage, use PDFs with embedded fonts or the server editor |

The existing PDF engine's format/coverage limitations still apply (see
`spec-matrix.md`). Complex or large documents and high-DPI multi-page exports
can take substantial worker time or exhaust browser memory. This host deliberately uses
single-threaded WASM in a dedicated module worker for Pages compatibility, and disables trimming to preserve
the shared editor's reflection-based JSON payloads. Initial download is larger
than a trimmed/AOT-specialized app; no `wasm-tools` workload is required.

The shared session preserves unique indirect Form-child editing. Shared,
repeated, hidden, nested or ambiguous children remain withheld. Graphic-group
edits cannot be combined with Form-child edits, and page mutations require
saving/reopening or resetting pending Form-child edits; the HTTP adapter retains
409 conflict responses and the browser presents the same conflict message.

Bundled font resources reuse `samples/_gen/fonts/Vera.ttf` and `VeraBd.ttf`.
Their redistribution notice is shipped as `bitstream-vera-license.txt`. They
are PDF rendering substitutes, not a change to the editor's visual design.

## Original text selection and editing

Selecting original text opens its content and original font/effective size in
Properties without creating an overlay, changing dirty state, or rewriting text
operators. Focusing, unchanged input, Escape, deselection and a save/reopen retain
the original font, text matrix, glyph positioning and rendered pixels. Plain
selection of an existing overlay or graphic also does not enqueue a no-op edit.

The first actual content change creates a replacement. Supported original
embedded glyphs retain their font resource; horizontal scale, shear, baseline,
character spacing and word spacing are carried through the original rendering
matrix. Edited original text is painted in the base PDF raster; its HTML element
is a transparent selection/drag target, not a substitute-font visual preview.
The Properties notice explicitly explains fallback for unavailable glyphs.
Changing font family/style deliberately selects the chosen replacement font.
Existing vertical/rotated-text eligibility limits remain. Changed text is
re-encoded: original per-pair TJ kerning is not a general shaping engine.

`AdoBye.EditorSmoke --text-selection <published-wwwroot>` uses the embedded-font
typography sample and a sheared/scaled variant. It checks untouched state,
unchanged input/cancel, exact save/reopen PNGs and every text run's geometry,
then verifies supported actual edits retain embedded font, baseline and matrix.

## Local recovery

The browser retains one editable session in the origin's `adobye-editor`
IndexedDB database. Startup shows **Resume previous edit** with **Resume** and
**Discard and clear**, without opening or rendering the retained document.
Blank startup cannot overwrite that record. Resume restores the unflattened
source PDF, pending overlay/original-text and graphic/Form-child edits, page
operations, field values, metadata, filename/dirty state, current page's undo
stack, clipboard and their image bytes. Original Form references are remapped
across serialization, including original-text references in UI undo snapshots.
Invalid backend records are validated in an isolated session before replacing
the live editor. A failed resume keeps the retained copy and shows an error.

Snapshots coalesce after 1.5 seconds of inactivity and flush pending item/object
saves first. PDF serialization runs in the PDF worker, not a pointer handler;
view-only updates reuse the last PDF/model payload. Writes/deletes are
sequenced IndexedDB transactions requesting strict durability. **Recovery saved**
appears only after transaction completion; **Recovery pending** is not a
durability guarantee. Quota, blocked storage, corruption and initialization
errors are visible. A failed write can be retried without losing the open edit.
Click the recovery indicator to clear the retained copy and pause recovery for
the current document; opening/creating another document enables it again.
Discard at startup permanently deletes the retained record.

No PDF bytes, image blobs or edit data go into localStorage; it retains only
lightweight preferences such as geometry locking. No open/export password is
persisted. Recovery of encrypted input is deliberately unsupported: a session
opened from or importing encrypted input cannot emit a recovery snapshot, even
if it subsequently saves unencrypted. Browser cryptography that cannot open the
input reports the limitation without replacing the current document.

Recovery is a local working copy, not a secure export or backup. It contains
the original unredacted input so edits/undo remain possible. Clear it after
sensitive work. Browser eviction, private-browsing policies, device failure and
closing before **Recovery saved** can lose changes; keep downloaded copies.

`AdoBye.EditorSmoke --recovery <absolute-published-wwwroot>` checks real embedded-
font text edits and exact resumed raster pixels, image delete/undo/clipboard
paste across reload, prompt gating, durable writes/deletes, encrypted-input
limitations, corrupt-record retention and quota failure/retry. Pointer movement
alone is asserted to cause no PDF snapshot. Shared-session tests also cover
fields/metadata, page operations, direct graphics and unique Form-child edits,
repeated recovery, missing references and atomic failure.

## Measured editor performance

### Input responsiveness during fresh PDF work

The PDF runtime now loads directly through the supported `.NET` JavaScript
runtime/`JSExport` API in `worker.js`; no Blazor runtime or PDF engine runs on the
UI thread. Commands remain FIFO and failures reject the caller without poisoning
the next retry. Page/asset/print images are asynchronous. Superseded image targets
and stale page-selection responses cannot overwrite current UI state.
The existing browser `Parallel.For` guards and `WasmEnableThreads=false` remain:
there are no shared-memory threads, service workers, or COOP/COEP requirements.

The same four fixtures, settings and three fresh Chromium contexts were measured
again at the selection checkpoint and with worker execution. The profiler now
adds an uncached 137-dpi render, sends a trusted CDP pointer event while it is in
flight, and measures delivery of independent 16-ms worker heartbeat messages.
It enforces a 200-ms fresh-render input/heartbeat goal for the worker host.
Medians in milliseconds:

| Sample | Pointer dispatch acknowledgement, before → worker | Maximum heartbeat delivery delay per run, before → worker | Fresh-work phase wall time, before → worker |
|---|---:|---:|---:|
| Embedded typography | 536 → 2 | 562 → 0.7 | 618 → 954 |
| Vector graphics | 402 → 2 | 429 → 0.2 | 486 → 914 |
| Images | 784 → 2 | 807 → 0.2 | 867 → 1297 |
| Multipage report | 1140 → 2 | 1169 → 0.3 | 1225 → 1526 |

Across all measured steady-state phases/runs, worst heartbeat delay decreased
from 1221 ms to 42 ms. Image text-edit main-thread blocking
`sum(max(longTaskMs-50, 0))` decreased from a 746-ms median to zero while total
edit wall time was about 1224 → 1306 ms. Zoom wall times were 374/292/661/608 ms
before and 439/325/739/689 ms afterward:
this change removes UI blocking, not the underlying raster computation.
After moving work off-thread, lazy thumbnail jobs can still be queued when the
first page becomes ready; their time now lands in the following fresh-work phase
(2-5 total commands, rather than one). Those phase wall times are therefore not
isolated decoder CPU measurements. FIFO thumbnail/raster work can still delay a
later PDF result even though input, panels and pointer handling remain responsive.

First-page PNG hashes matched exactly for all four fixtures in all three runs.
Cold-ready times were 973/966/901 ms before and 865/1084/980 ms after in these
uncompressed local runs; no cold-download optimization is claimed.
Main-thread CDP task time during fresh-work phases dropped from roughly
428-1233 ms to 10-20 ms, but those metrics now exclude the worker's CPU.
Observed main-context JS heap was 4.9-23.5 MiB before and 2.9-20.2 MiB after,
not the managed/linear WASM heap. There is still one PDF runtime/managed heap,
plus message/base64 and raster copies crossing the worker boundary. Large files,
high-resolution exports and transient decoder memory remain bounded only by
browser limits, not by the small encoded-PNG cache.

### Recovery-inclusive replay

The same four fixtures/settings were replayed in three cache-disabled Chromium
contexts after recovery integration. The profiler now measures the additional
`local-recovery` phase through durable completion and enforces the same
less-than-200-ms heartbeat goal during it. Medians in milliseconds:

| Sample | Fresh-render pointer acknowledgement | Fresh-render wall time | Text-edit wall time | Save/download wall time | Recovery worker command |
|---|---:|---:|---:|---:|---:|
| Embedded typography | 1.8 | 726 | 726 | 193 | 47 |
| Vector graphics | 2.7 | 801 | 676 | 193 | 18 |
| Images | 1.4 | 1152 | 1208 | 211 | 56 |
| Multipage report | 1.5 | 1396 | 873 | 195 | 14 |

All twelve fresh-render phases and twelve recovery phases met the latency goal.
Worst steady-state heartbeat delay was 45.1 ms; worst during recovery was
42.8 ms. No steady-state long tasks were recorded, including during recovery.
The recovery phase's 1460-1525-ms document medians mostly include the 1.5-second
debounce, not serialization CPU. Exactly two local commands (state plus
snapshot) occur in each recovery phase, with no raster requests.
First-page PNG hashes again match the pre-worker baseline exactly in all twelve
cases. Cold-ready times were 802/731/733 ms in this local replay; these are not
a cold-download speedup claim. Managed/linear heap allocation and worker CPU are
still not measured by main-thread CDP metrics. Recovery adds a persistent
PDF/model/image working copy and transient JSON/base64/Blob copies, not a memory
reduction. Underlying raster CPU, FIFO result latency and large-file memory
limits remain unresolved.

The integrated Release build passed, with 1579/1579 full solution tests and
85/85 selected recovery/session/Form HTTP/annotation-structure cases, no
failures/skips. Full static-site Chromium smoke also passes original text
fidelity, worker race fencing, recovery/failure paths, mobile layout and no
outbound PDF traffic.

### Earlier redundant-work reduction

The Chromium runner also supports `--profile <published-wwwroot-or-url> <output.json> <absolute-pdf-paths...>`.
It runs three fresh, cache-disabled browser contexts at 1440x1000 and device scale 1,
opening the same four samples in order and exercising zoom, navigation, overlay
creation, selection/drag, undo, existing-text edits and save/download. JSON records
per-command wall time/count, long tasks, CDP CPU/task time and JavaScript heap
size, plus exact first-page PNG hashes. PDF bytes stay in the browser; profiling
the public site does not upload documents or call an editor server.

```powershell
$pdfs = '03-typography-embedded.pdf', '05-vector-graphics.pdf', '09-images.pdf', '20-report.pdf' |
    ForEach-Object { (Resolve-Path (Join-Path samples $_)).Path }
$site = (Resolve-Path tools\AdoBye.EditorWasm\bin\pages\wwwroot).Path
dotnet run --no-build -c Release --project tools\AdoBye.EditorSmoke -- --profile https://ievangelist.github.io/csharp-pdf/ "$env:TEMP\editor-live.json" @pdfs
dotnet run --no-build -c Release --project tools\AdoBye.EditorSmoke -- --profile $site "$env:TEMP\editor-local.json" @pdfs
```

The live `7fb432d` baseline and integrated local Release baseline both showed
synchronous raster work dominating steady-state latency. The local before/after
medians below use identical inputs/settings, with three observations per cell
(milliseconds; action times include automation and the existing 300 ms save
debounce, not just handler CPU time).

| Sample | Open | Zoom | Next page | Selection/drag | Undo | Text edit | Save/download |
|---|---|---|---|---|---|---|---|
| Embedded typography | 1120 -> 1012 | 455 -> 409 | 333 -> 296 | 778 -> 763 | 546 -> 512 | 828 -> 775 | 564 -> 212 |
| Vector graphics | 819 -> 779 | 304 -> 301 | 733 -> 632 | 724 -> 709 | 511 -> 476 | 723 -> 678 | 840 -> 207 |
| Images | 1446 -> 1012 | 696 -> 684 | N/A (one page) | 1307 -> 961 | 1043 -> 726 | 1676 -> 1242 | 1104 -> 241 |
| Multipage report | 895 -> 759 | 656 -> 628 | 195 -> 166 | 728 -> 711 | 510 -> 492 | 936 -> 907 | 850 -> 211 |

The fixes remove unnecessary graphics/image decoding from metadata-only
`Analyze`, reuse exact PNG bytes in an 8 MiB per-session cache invalidated on
content changes, render only visible thumbnails after the current page is ready
and in separate event-loop tasks, and stop rebuilding every thumbnail/page when
marking a download saved. Inserted-image URLs are reused while referenced and
released with UI item/history/clipboard references or document replacement.
Deferred thumbnails reserve their rotated page geometry and remain hidden until
their real image loads; their initially absent `src` is deliberate, not a
broken-image placeholder. Render DPI and final pixels are unchanged.

All four first-page PNG hashes match exactly before/after in all three runs.
Save/download performs three local commands instead of 10-15, with no page or
thumbnail rerenders. Median save CPU task time drops from 468/760/1025/770 ms
to 38/39/90/29 ms (text/vector/image/report), and median save blocking above the
50 ms long-task threshold drops from 306/425/841/621 ms to 0/0/13/0 ms.
At that checkpoint image text-edit blocking fell from 1213 to 768 ms; the
unacceptable residual motivated the subsequent worker slice above.

Cold startup was measured separately: live 3960/1428/1203 ms (11,663,034
transferred bytes each), local before 1306/1042/1089 ms and after
1100/997/1049 ms. Live compression/CDN and the uncompressed local static server
are not comparable download benchmarks (local baseline transferred 30,604,390
bytes). This slice does not claim a startup/bundle-size improvement.
JavaScript heap readings vary with GC (roughly 5-20 MiB in these runs) and do
not include the managed WASM heap; no managed-allocation improvement is claimed.
At that checkpoint fresh rasterization/image decode and zoom still ran
synchronously on the main thread. The worker slice above subsequently moved
that work off the UI thread; reducing total raster CPU and bounding transient
raster/decoder memory remain follow-ups.

Validation for this slice passes 142/142 focused session/object/structure/action
cases and 1549/1549 full Release cases, with zero failures/skips. The incremental
solution build has zero warnings/errors; earlier compilation retains the known
CS8604, CS0649, CS0675, xUnit2029 and xUnit2013 warnings. Integrated Chromium
coverage includes a zero-rerender assertion after the pending edit flush and
thumbnail completion, clipboard/undo, invalid-input recovery and collapsed mobile
thumbnail visibility. The static-site publish validates 218 local WASM artifacts.

## Local merge and header layout

The header order is brand, File menu, then the open document name and
saved/unsaved status. Long names truncate with the full filename in a tooltip;
mobile uses a status dot and collapsed controls. Local recovery keeps its
separate durable-checkpoint status and explicit Resume/Discard prompt. The
one-time GitHub Sponsors link is a plain external anchor with
`noopener noreferrer`; it does not preconnect or send document data.
The checkout frequency query is asserted, but the available tier list and actual
one-time checkout still require manual verification on GitHub.

**File > Merge PDFs** accepts multiple local PDFs (or a drop into its dialog).
Review/reorder/remove files, select one-based page ranges and choose an insertion
point or append. The backend imports the selected files as one atomic batch,
registers imported AcroForm fields (renaming collisions), nests outlines under
file bookmarks and preserves page labels. Existing page overlays stay with
their pages; a failure leaves the document unchanged. This does not merge every
catalog-level structure (for example, arbitrary tags/layers/attachment trees).
Document JavaScript and signatures are not carried into the merged catalog.
Page insertion is not an overlay undo operation; retain/download the original
when the merge must be reversible.

Enter/Space activate the focused row or Add PDFs button; reordering/removing a
row retains focus, Tab stays inside the dialog and Escape cancels. Backend
rejections keep the ordered files, ranges and insertion choice open with an
inline error for correction and retry.

Encrypted inputs unsupported by the browser are flagged by filename before
merging. Passwords are never requested or persisted by this dialog. If a
supported encrypted import occurs in the server host, recovery is disabled
rather than caching a decrypted copy. Merge stays local in both hosts.

The integrated Release build succeeds and full suite passes 1696/1696. A local
static WASM publish into a fresh output directory validates 207 artifacts (without the optional `wasm-tools`
optimization workload). Full Chromium smoke passes text fidelity, recovery,
clipboard/assets and desktop/mobile header assertions; the separate `--merge`
scenario covers keyboard controls/focus/Escape, inline backend rejection,
direct host requests with a later invalid-range or encrypted frame (unchanged
state, recovery history/assets and working overlay undo), order/ranges/insertion, saved page
count/text, form registration, outlines/labels, render and reopen. A batched
desktop/mobile screenshot check accompanies these assertions. This does not
complete the outstanding latency/benchmark pass.

## Build and smoke coverage

Use the SDK declared in root `global.json`:

```powershell
.\tools\Publish-EditorPages.ps1
dotnet test tests\AdoBye.Tests -c Release --filter "FullyQualifiedName~Editor|FullyQualifiedName~ExistingObjectTransform"
dotnet build tools\AdoBye.EditorSmoke -c Release
.\tools\AdoBye.EditorSmoke\bin\Release\net10.0\playwright.ps1 install chromium
$site = (Resolve-Path tools\AdoBye.EditorWasm\bin\pages\wwwroot).Path
dotnet run --no-build -c Release --project tools\AdoBye.EditorSmoke -- $site
dotnet run --no-build -c Release --project tools\AdoBye.EditorSmoke -- --merge $site
```

The publish script checks required static assets and WASM magic bytes. Override
`-Output` to publish elsewhere and `-BasePath` for another repository path or `/`.
Always pass an absolute publish directory to the smoke runner (`dotnet run`
starts in its project directory).

The Chromium runner hosts **only static files** under `/csharp-pdf/`, with no
editor API and no custom response headers. It exercises the real UI for
document creation, text editing, preview (including a nonblank-glyph pixel
assertion), save/download, file-picker reopen, rotation and search. It also
checks exports/image decoding, failed-input recovery and mobile controls, and
rejects API requests, HTTP methods other than GET, root-relative asset requests
and external requests. Shared-session tests check the saved PDF with the parser
and text extractor. Set `EDITOR_SMOKE_ARTIFACTS` to a directory to capture
desktop/mobile screenshots.

For active-page image reliability, run the focused Chromium regression against
the published static site and one or more local sample PDFs:

```powershell
$sample = (Resolve-Path samples\26-repacked-object-streams.pdf).Path
$artifacts = Join-Path $env:TEMP 'pdf-page-image'
dotnet run -c Release --project tools\AdoBye.EditorSmoke -- --page-image $site $sample $artifacts
```

It samples the displayed page image on every animation frame while zooming,
toggling both sidebars and navigating pages; it also checks decoded pixels and
injects transient/permanent decode failures to verify cache-bypass retry,
last-good-image retention and visible error reporting. Failure screenshots and
the frame trace are written to the artifact directory. The command can be
repeated with other sample paths; keep PDFs local to the browser.

The original server host remains available:

```powershell
dotnet run -c Release --project tools\AdoBye.EditorWeb -- file.pdf
```

It uses the same `EditorSession` under its HTTP adapter, retains installed
fonts, startup-file saving and protected export, and does not load WebAssembly.
