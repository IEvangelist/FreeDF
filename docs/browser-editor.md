# Browser editor and GitHub Pages

The Pages build runs the **same editor UI and C# PDF engine** as
`tools/AdoBye.EditorWeb`, entirely in browser WebAssembly. There is no PDF API
server. Opening a PDF reads a local `File`; parsing, rendering, edits and exports
stay in the tab's memory. Downloads use local blobs. There is no telemetry,
external font request, PDF upload, service worker, or browser persistence of PDF
contents. Closing/reloading the tab discards its document; save a copy first.
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

## Editor operation inventory

| Existing editor action | Browser host |
|---|---|
| New document, paper sizes/orientation; open picker; PDF drop | Local; dropping onto an open document imports its pages, as in EditorWeb |
| Rendered pages/thumbnails; navigation; fit width/page; zoom | C# software renderer in WASM; no PDF.js or remote rendering |
| Select/move/resize/delete/recolour existing editable graphics | Same content-rewriter operations, including object transforms and reset |
| Click existing editable text; add text; typography/colour/alignment/spacing | Same overlays and original embedded-font reuse |
| Rectangle, ellipse, line, PNG/JPEG insertion; fill/stroke/opacity | Same builder and image decoders |
| Copy/paste, duplicate, arrange, bring to front, undo, geometry lock | Same UI operations and existing undo scope (object edits, not a document-wide history) |
| Redaction | Same content-removing redactor, not a painted-cover substitute |
| Forms: text, button and choice fields | Same field-value edits; existing UI represents choice values as text |
| Page rotate/delete/reorder/duplicate; blank insert; merge/import | Same page editor |
| Title/document metadata; search/case matching/highlights | Same local session |
| Save / Save as / Download; PDF page-range extraction | Downloads a copy; cannot overwrite the original filesystem path |
| PNG, all-pages PNG ZIP, text, compressed PDF exports | Local downloads |
| Print, fullscreen, keyboard shortcuts, panels | Browser APIs and existing editor UI; printing uses locally rendered page images |
| Password-protected export | Not available in WASM: .NET browser lacks the required synchronous PDF cryptography. The browser menu explicitly offers unprotected compression instead; the server keeps protected export |
| Password/certificate-protected input | Not added: the existing open UI has no password/certificate prompt. Encrypted files may fail with a visible error; decrypt locally first or use the library/server tools |
| Installed fonts and system font fallback | Browser cannot enumerate host fonts. New text offers the five standard PDF families; original embedded fonts can still be reused. Non-embedded fonts render using bundled, licensed Bitstream Vera Sans regular/bold substitutes, not exact serif/monospace/italic/Symbol/Dingbats/CJK fidelity. For exact appearance and broader character coverage, use PDFs with embedded fonts or the server editor |

The existing PDF engine's format/coverage limitations still apply (see
`spec-matrix.md`). Complex or large documents and high-DPI multi-page exports
can block the tab or exhaust browser memory. This host deliberately uses
single-threaded WASM for Pages compatibility, and disables trimming to preserve
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

## Measured editor performance

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
Image text-edit blocking falls from 1213 to 768 ms; it is still too long.

Cold startup was measured separately: live 3960/1428/1203 ms (11,663,034
transferred bytes each), local before 1306/1042/1089 ms and after
1100/997/1049 ms. Live compression/CDN and the uncompressed local static server
are not comparable download benchmarks (local baseline transferred 30,604,390
bytes). This slice does not claim a startup/bundle-size improvement.
JavaScript heap readings vary with GC (roughly 5-20 MiB in these runs) and do
not include the managed WASM heap; no managed-allocation improvement is claimed.
Fresh rasterization/image decode and zoom still execute synchronously on the
main thread, and the 8 MiB encoded cache does not bound transient raster/decoder
memory. Moving that work off-thread or further reducing raster CPU remains a
follow-up, not a solved responsiveness claim.

Validation for this slice passes 142/142 focused session/object/structure/action
cases and 1549/1549 full Release cases, with zero failures/skips. The incremental
solution build has zero warnings/errors; earlier compilation retains the known
CS8604, CS0649, CS0675, xUnit2029 and xUnit2013 warnings. Integrated Chromium
coverage includes a zero-rerender assertion after the pending edit flush and
thumbnail completion, clipboard/undo, invalid-input recovery and collapsed mobile
thumbnail visibility. The static-site publish validates 218 local WASM artifacts.

## Build and smoke coverage

Use the SDK declared in root `global.json`:

```powershell
.\tools\Publish-EditorPages.ps1
dotnet test tests\AdoBye.Tests -c Release --filter "FullyQualifiedName~Editor|FullyQualifiedName~ExistingObjectTransform"
dotnet build tools\AdoBye.EditorSmoke -c Release
.\tools\AdoBye.EditorSmoke\bin\Release\net10.0\playwright.ps1 install chromium
$site = (Resolve-Path tools\AdoBye.EditorWasm\bin\pages\wwwroot).Path
dotnet run --no-build -c Release --project tools\AdoBye.EditorSmoke -- $site
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

The original server host remains available:

```powershell
dotnet run -c Release --project tools\AdoBye.EditorWeb -- file.pdf
```

It uses the same `EditorSession` under its HTTP adapter, retains installed
fonts, startup-file saving and protected export, and does not load WebAssembly.
