# Browser editor and GitHub Pages

The Pages build runs the **same editor UI and C# PDF engine** as
`tools/AdoBye.EditorWeb`, entirely in browser WebAssembly. There is no PDF API
server. Opening a PDF reads a local `File`; parsing, rendering, edits and exports
stay in the tab's memory. Downloads use local blobs. There is no telemetry,
external font request, PDF upload, service worker, or browser persistence of PDF
contents. Closing/reloading the tab discards its document; save a copy first.
The host still makes ordinary GET requests for its static app/runtime assets.
Replacing item lists or removing/importing pages prunes uploaded image bytes
that no live page item references; shared asset IDs remain available while any
live item references them. Removed pages' item lists are discarded.

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
