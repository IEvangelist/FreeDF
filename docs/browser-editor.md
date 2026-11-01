# FreeDF browser editor and GitHub Pages

FreeDF's Pages build runs the **same editor UI and C# PDF engine** as
`tools/FreeDF.EditorWeb`, entirely in browser WebAssembly. There is no PDF API
server. Opening a PDF reads a local `File`; parsing, rendering, edits and exports
stay in a dedicated local browser worker. Downloads use local blobs. There is no telemetry,
external font request or PDF upload. A same-origin service worker caches
verified application assets for offline reuse. The browser now retains
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

### Appearance

The header's **Appearance** menu has **Primary color** (actions) and
**Accent shade** (selection and focus) selectors. The default is PinkInk-inspired
pink with Medium accents. Primary choices include pink, cyan, magenta, blue,
violet, green, orange, red and teal. Accent options are Light, Medium and Dark
variations of the selected primary, tuned for contrast in each theme. Changing
the primary automatically recolors the accents without resetting the shade.
Light/dark/system theme and reduced-motion choices remain independent.
Selections stay in local browser storage and apply before first paint; old
saved legacy accent colors retain their previous primary/action color and adopt
the default Medium shade.
**Reset PinkInk colors** restores the defaults without resetting theme or motion.
These settings affect only the editor UI, never PDF colors or exported content.

**Appearance > Layout and density** independently configures menus/toolbars and
sidebars/controls (Compact, Default or Comfortable), plus interface text size
(Small, Default or Large). Choices apply immediately and persist in localStorage
before first paint. **Reset density** resets only these settings. Touch controls
retain their minimum target sizes, and PDF text and exports are unchanged.

The footer credits **Developed by David Pine**, linking to
**https://davidpine.dev/**. The GitHub source link appears only in the header,
including on mobile, rather than being duplicated in the footer.

The panel toggles live on the second toolbar row, not the primary header.
Drag either panel's visible inner edge to resize it, use the focused separator's
Left/Right keys (Home/End for limits), or double-click to restore its default
width. Panel widths and collapsed state persist on this device across reloads
and window resizing. Narrow screens use resizable overlay sheets.
Multi-page PDFs show a bottom progress strip reflecting the current page out
of the total; it follows all paging controls and disappears for single-page or
closed documents. The existing floating previous/next pager remains available.
Background redraws and layout changes preserve a focused page-number entry.
Enter submits it; leaving the field restores the actual current page number.

The welcome screen's primary **Open the showcase** button opens the tagged,
18-page benchmarks, brand and spec showcase (`28-benchmarks-brand-spec.pdf`).
It also leads **File > Open sample** and the published manifest. Share
**https://ievangelist.github.io/FreeDF/#sample=28** to open it directly.
**Open the full sample collection** remains a secondary welcome action:
sample 27 still contains only samples 01-26 (50 pages). Both PDFs and the sample
manifest are fetched only on demand from this site, never precached; size and
SHA256 are verified before opening. Nothing is uploaded.

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
**https://ievangelist.github.io/FreeDF/**. Adding the workflow does not itself
mean the site is live. The implementation session does not push or deploy.

The app's `<base href="/FreeDF/">` is inserted by
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
response headers. SDK compression is disabled by default (Pages has been observed
to apply gzip itself), and there is no
service worker. The runtime manifest in `dotnet.js` names fingerprinted assemblies
and native/runtime modules. A cached manifest crossing a deployment can therefore
refer to an asset that no longer exists; Reload is the recovery action, not an
integrity bypass or a fallback to an unrelated asset.

The observed live cold-boot failures were HTTP 503s for varying existing WASM
assets, not missing filenames or integrity mismatches. The same URLs returned
HTTP 200 on direct checks; default Chromium later succeeded too. This supports
transient delivery failure, but does **not** establish HTTP/2 stream exhaustion.
The SDK already limits downloads to 16 concurrent requests; its former retries
used the same cache policy with only a short final delay. Asset bundling and concurrency are unchanged. The later measured AOT publish
trims framework assemblies while retaining the PDF engine surface.

`FreeDF.EditorSmoke --boot <published-wwwroot> <evidence-directory>` measures
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

Pages with form fields show a keyboard-accessible **Fill out | Edit** toggle
above the page. Each opened document starts in **Edit**, for selecting, moving,
resizing and authoring fields. Choose **Fill out** to type values or operate
checkboxes and other form controls without moving them. The mode stays selected
while navigating or saving that document; changing mode does not modify the PDF,
its dirty state or undo history. `--forms` covers both modes and their toggle.
Rapid on-page field edits commit in order; editor undo and save wait for queued
values so undoing one field cannot restore stale values in another.
`--annotations` checks real note creation, property editing and save/reopen.
The render-replica pool is enabled on eligible devices, with `?pool=0` retaining
the single-worker path. Low-resource devices and replica startup/crash failures
fall back to the primary worker; pool smoke checks compare decoded pixels.

| Existing editor action | Browser host |
|---|---|
| New document, paper sizes/orientation; open picker; PDF drop | Local; dropping onto an open document imports its pages, as in EditorWeb |
| Rendered pages/thumbnails; navigation; fit width/page; zoom | C# software renderer in WASM; no PDF.js or remote rendering |
| Select/move/resize/delete/recolor existing editable graphics | Same content-rewriter operations, including object transforms and reset |
| Select existing editable text; add text; typography/color/alignment/spacing | Selection/focus/cancel does not replace PDF content. Actual text changes reuse supported original embedded glyphs and text transforms; the PDF renderer paints edited originals rather than approximate browser fonts |
| Rectangle, ellipse, line, PNG/JPEG insertion; fill/stroke/opacity | Same builder and image decoders |
| Copy/paste, duplicate, arrange, bring to front, undo, geometry lock | Same UI operations and existing undo scope (object edits, not a document-wide history) |
| Redaction | Same content-removing redactor: arbitrary paths/strokes/clips, rotated content, per-invocation shared/nested Forms, partial inline/stencil image blanking and hidden optional content; not a painted-cover substitute |
| Forms: text, button and choice fields | The in-page Fill out / Edit toggle defaults to Edit for layout authoring. Fill out in Select mode exposes text/comb/multiline, editable combo/multi-select list, checkbox and radio controls with native Tab/Space interaction and field undo; save/reopen retains values and appearances. Push-button actions stay inert. Preview mode hides the editing layer; the Properties field list remains available |
| Page rotate/delete/reorder/duplicate; blank insert; merge/import | Same page editor |
| Title/document metadata; search/case matching/highlights | Same local session |
| Save / Save as / Download; PDF page-range extraction | File System Access browsers can save to an explicitly chosen local handle after permission checks; other browsers download a copy. Protected Save / Save as retain the protection dialog rather than entering the plain file-picker path. Stored handles contain no PDF bytes or passwords |
| Reload / previous edit recovery | Explicit Resume previous edit or Discard and clear; unencrypted editable state, undo and clipboard remain local in IndexedDB |
| PNG, all-pages PNG ZIP, text, compressed PDF exports | Local downloads |
| Print, fullscreen, keyboard shortcuts, panels | Browser APIs and existing editor UI; printing uses locally rendered page images |

Printing renders each page at 150 dpi and requests its own paper size from the
PDF CropBox, rotation and UserUnit, including landscape and mixed-size documents.
Images retain their aspect ratio. The browser/printer can still override paper
size or impose non-printable margins; choose matching paper and fit to the
printable area when printing to a device without borderless support.
The focused Chromium regression checks actual print-to-PDF page sizes, page
count and unclipped corner markers:

```powershell
dotnet run -c Release --project tools\FreeDF.EditorSmoke -- --print-layout `
  "$PWD\tools\FreeDF.EditorWeb\wwwroot\index.html"
```
| Password-protected export | The protection dialog writes AES-256 (R6) with open/owner passwords and permission checks; protection is not silently dropped. See [browser-encryption.md](browser-encryption.md) |
| Password/certificate-protected input | Supported password-encrypted PDFs open through an accessible password prompt with cancel/retry; unsupported certificate input remains an explicit limitation. Encrypted input disables recovery; decrypted bytes and passwords are never cached for recovery |
| Installed fonts and system font fallback | Local Font Access browsers offer permission-based search/preview and embed a chosen eligible font; only metadata is cached, not font bytes. Restricted embedding is refused with an explicit substitute choice. Unsupported browsers retain standard families and original embedded fonts. Non-embedded font substitution remains bounded; see [fallback-fonts.md](fallback-fonts.md) |

Recovered startup retries are logged as `[PDF boot] retry ...` console warnings
and exposed as `pdfHost.bootRetries`; nothing is sent to telemetry. Smoke checks
record the count and accept transient framework HTTP/network failures only when
the same resource subsequently succeeds and the loader reports a retry. Missing
resources, terminal failures and unrelated browser errors remain failures.
Set `EDITOR_SMOKE_TRANSIENT_BOOT=1` for the ordinary Chromium smoke to inject
one framework HTTP 503 and verify its recovered-error accounting explicitly.

The existing PDF engine's format/coverage limitations still apply (see
`spec-matrix.md`). Complex or large documents and high-DPI multi-page exports
can take substantial worker time or exhaust browser memory. This host deliberately uses
single-threaded WASM in a dedicated module worker for Pages compatibility.

On-page form controls sit above content selection. Page-sized backgrounds and
imported page groups (at least 90% of the page area) require Alt+click or keyboard
focus followed by Enter/Space to select. Their tooltip, accessible name and
first-click status explain this; plain clicks no longer select the whole page
accidentally. Checkbox/radio widgets without appearance streams use their
declared export values (or the checkbox `Yes` fallback), not a literal `null`.
Run
`--forms <published-wwwroot-or-url>` for the focused UI/save/reopen regression
against the AcroForm sample, a locally merged collection and a rotated,
CropBox-offset form with missing appearances and multiple/export values. This does not claim
JavaScript/Submit/Reset action execution or form filling in Preview mode.
The local publish script defaults to untrimmed/non-AOT and needs no `wasm-tools`;
`-Aot` and the Pages workflow trim the framework while retaining the PDF engine
surface. Browser commands and shared editor payloads use source-generated JSON
metadata; reflection fallback is disabled in the WASM host. New payload types
must be registered in `EditorSession.Json.cs` or the browser command context.
The WASM build enables trim/AOT analyzers and treats IL2026/IL3050 as errors.
The Pages workflow selects AOT by default after the measured comparison; no threads or reduced final
render quality are introduced.

The WebAssembly runtime boots in an engine worker while the document is being
parsed; replica workers use the same fixed `en-US` runtime/ICU selection. The
PDF engine itself uses invariant culture, with ICU retained for Unicode
normalization. Browser language does not change decimal formatting or casing
in PDF operations.

The offline cache registers only after the editor becomes interactive. Asset
digests cover the shell, runtime, sample collection and fallback fonts; samples
and fallback fonts are fetched and verified on demand, not precached. Each
client, including a still-booting tab and its engine worker, stays pinned to the
build that served its first request. Activating an update preserves those
clients' caches until they close. An asset absent from an older pinned cache
returns 503 instead of fetching a different build; an older tab can therefore
need a reload before using a previously uncached sample or fallback font.
Unsaved edits, inline edits and in-flight requests defer update reloads.
`Publish-EditorPages.ps1 -KillServiceWorker` publishes a rollback that removes
the editor cache and client pins without discarding unsaved edits.

### Installed app and offline use

FreeDF's Pages/WASM editor is an installable PWA with a stable, repository-scoped
manifest identity, standalone window, a real 192px icon embedded in the published
manifest and an opaque 512px icon usable as both a normal and maskable icon.
**App and offline status** in the header (inside **More** on small screens)
shows whether the app cache is preparing, ready, or unavailable and whether the
browser is online. An **Install app** action uses the browser's installation
prompt only after an explicit click. Where that prompt is unavailable, the
dialog explains browser-menu installation and Safari's
**Share > Add to Home Screen** flow. Installation is optional; editing in a
browser remains supported. FreeDF project names and local storage use the new
identity; the manifest identity and repository path remain unchanged, so
existing installations and preferences continue to work.
PNG/ICO icons can be regenerated with `python tools\Generate-EditorIcons.py`
using Pillow and the sample suite's local Source Sans 3 font.

Wait for **App ready for offline use** after the first online visit. The core
editor can then start, open local PDFs, edit, merge and save/download without a
network connection. Samples and fallback fonts are only available offline after
their first successful online use. Browser storage can be evicted or cleared;
installation is not a backup. Download or save important PDFs separately.
The app-asset service worker never caches local PDF bytes; local edit
recovery and chosen file handles remain separate.

On browsers/operating systems supporting the File Handling API, installation
registers PDF files with the app. A launch waits for engine/recovery readiness
and retains the received local handle for **Save**. Existing documents,
unsaved edits and active dialogs are never replaced automatically: received
files wait behind **App > Open received PDFs**, with the existing discard
confirmation. Multiple files open the first PDF and offer the rest in the
existing merge dialog. Unsupported browsers retain the picker/drop/download
flows; OS file association is browser-controlled. A server-side share target,
push notifications and background PDF processing are not advertised.

**Check for updates** checks only this app's registration. Updates still use
the multi-tab dirty-work gate and version-pinned caches; unopened received PDFs
also defer activation so an update cannot discard a pending launch. The publisher's kill
switch unregisters only this app's scope, and a forced reload of a dirty tab
requires an explicit discard confirmation. Installation readiness, offline
open/edit/download, local launch-handle Save and deferred launches are exercised
by the `--service-worker` browser smoke.

`Test-EditorPayload.ps1` checks the published startup files against
`performance/editor-startup-budget.v1.json`, separately reporting the genuinely
deferred samples and fonts. Those numeric ceilings are unchanged. The integrated
AOT build must pass these unchanged ceilings; successful publishing or
non-AOT browser smokes alone do not make it release-ready.

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

`FreeDF.EditorSmoke --text-selection <published-wwwroot>` uses the embedded-font
typography sample and a sheared/scaled variant. It checks untouched state,
unchanged input/cancel, exact save/reopen PNGs and every text run's geometry,
then verifies supported actual edits retain embedded font, baseline and matrix.

## Local recovery

The browser retains one editable session in the origin's `freedf-editor`
IndexedDB database. Startup shows **Resume previous edit** with **Resume** and
**Discard and clear**, without opening or rendering the retained document.
Existing recovery records, file handles and installed-font metadata are copied
from the previous storage names on first use without replacing newer FreeDF data.
When both generations contain different file handles, Resume leaves both intact
and requires Save as to choose the file to update rather than guessing.
The previous records remain available to older versions; clearing a retained
edit or forgetting a file also clears its previous-version record. Migration
errors are reported without deleting the original data. Offline build pins are
carried forward for open tabs and mirrored when needed for safe rollback.
Migration is one-time: edits saved later by an older tab or a rolled-back version
remain in the previous storage and are not merged automatically. Save or
download those edits before switching back or clearing recovery.
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

Redaction downloads require a full rewrite, not an incremental save retaining
original bytes. Paths are cut after bounded curve flattening (0.05 page-space
units); unsupported/singular/over-budget geometry is removed whole and
unsupported clips become empty. Shared Forms are copied only for affected
invocations, so untouched uses intentionally keep their content. Partial raster/
inline/stencil images blank covered pixel footprints; hidden optional content
is included. Redacted Form copies drop Metadata/PieceInfo/LastModified and
unused replaced resource references are removed. Glyph/outline matching still
depends on extracted Unicode and geometry; this is not a document-wide search
for every copy of a secret or a purge of undo/clipboard/recovery storage.

`FreeDF.EditorSmoke --recovery <absolute-published-wwwroot>` checks real embedded-
font text edits and exact resumed raster pixels, image delete/undo/clipboard
paste across reload, prompt gating, durable writes/deletes, encrypted-input
limitations, corrupt-record retention and quota failure/retry. Pointer movement
alone is asserted to cause no PDF snapshot. Shared-session tests also cover
fields/metadata, page operations, direct graphics and unique Form-child edits,
repeated recovery, missing references and atomic failure.

## Measured editor performance

### Worker transfer and exact image conversion: 2026-10-30

The [dated performance checkpoint](performance/2026-10-30.md) separates
total-operation latency from UI blocking, records matched before/after runs
on the current C# sample corpus, and documents remaining misses. Worker-side
binary conversion/transfer removes the large save/recovery UI stalls; exact
byte conversion improves identity DeviceRGB/gray images without changing
rendered pixels. Fresh high-DPI raster work is still slow.

The profile now also covers a generated 12-page/high-DPI fixture, cached
zoom/navigation, event-to-next-frame latency, and overlay-only bare-raster
cache/pixel equivalence. It writes p50/p95 summaries and explicit budget
misses, then fails by default **after** collecting all three repetitions.
`EDITOR_PROFILE_REPORT_ONLY=1` captures a diagnostic run, not a passing gate.
Use `--profile-summary <raw.json>` to apply the same budgets without repeating
the browser workload. Sidebar continuity and raster-count assertions remain
hard failures even in diagnostic mode.

The [user-local AOT comparison](performance/2026-10-30-aot.md) records substantially
lower uncached raster/open latency, exact decoded pixel hashes and worker-inclusive
gzip-equivalent payload sizes. The latest three-repeat AOT profile has two
remaining strict-goal misses; binary uploads and neighbor-page preview reuse
cleared the large-open and three next-page-navigation misses. Browser PNG encoding
uses bounded row batches rather than another whole-page copy.
Fresh-render gates additionally require p50/p95 below 1000/1500 ms and managed
allocation below 200 MB; allocation is not peak browser working set.

The [snapshot-reuse comparison](performance/2026-10-30-aot.md#snapshot-reuse-matched-released-runtime-comparison)
uses the released runtime as its control, not the historical base64/interpreter
build. Sharing an unedited background with the pristine document and retaining
the initial parsed source avoids redundant full-document rewrites. Showcase
open improves from 330.5/332.7 to 296.9/298.9 ms; large open from 119.8/120.8
to 105.0/106.4 ms. Original-text/graphics/redaction changes retain the prepared
snapshot path and existing invalidation. Pixel, cache, allocation and AOT smoke
gates remain unchanged. That snapshot-only result passed cached typography
at 97.6/102.0 ms. The final five-branch integration reopens this miss at
103.1/103.1 ms against 100/200; it is now explicitly recorded alongside
showcase open (281.2/301.1 ms) and startup long task (71/72 ms).

Hosted CI uses `--profile-ci <raw.json> docs\performance\editor-ci-known-misses.v1.json`.
Its versioned policy records the three current known misses and prints
`KNOWN MISSES (baseline open work)` plus the full strict-goal miss table in the job summary,
and uploads both. The historical AOT reference CSV is preserved; fixed goals
removed from the allowlist will be classified as `NEW GOAL MISS` if they regress.
Timing regression ceilings are 2.5x recorded p50/p95 with a
50-ms floor for near-zero timings; deterministic allocation, cache/raster-count,
fixed-41-dpi pixel hashes and no-blank checks are not relaxed. New **CI-relative**
budget failures block the job. New strict-goal misses below those relative
ceilings remain visible as `NEW GOAL MISS`, not claims of goal attainment.
A runner that meets a known original goal is marked `MET`, not rejected for
being faster than the reference machine. The versioned baseline remains intact.
The reopened cached-typography entry retains the CSV's numeric reference
98.1/104.8 ms and separately records the observed 103.1/103.1 ms in its note.
Known-miss classification does not raise its timing ceiling or change 100/200.
Hosted timing decisions require a breach in at least two of three repeats:
the median of per-repeat p95s is compared with the unchanged tail ceiling,
while the median ceiling is retained. With only three repeat observations, the
old pooled p95 was their maximum, so a single scheduling stall could block a
release. Maxima and isolated-outlier warnings remain in the job summary/JSON.
This changes only hosted timing decisions, not the authoritative strict report
or any pixel, allocation, cache/raster-count, completeness or no-blank gate.
The profile also waits for Undo's page replacement before selecting text, so
the next operation cannot accidentally target a stale span. Original-text
selection uses Playwright's visibility/stability checks rather than a forced
click, then verifies conversion to the editable Properties field and a
repeat-specific changed value. A commit timeout retains the field and command
diagnostics; it is still a failure, never a skipped timing group.
`tools\Test-EditorProfileCi.ps1` verifies ten synthetic policy cases, including
rejection of regressions, acceptance of an improved goal and visibility when a
fixed goal regresses. A single-repeat timing outlier passes with a warning;
two-repeat breaches fail. Even one deterministic hash/allocation/raster-reuse
failure still fails; no timing ceiling is changed.

The full browser smoke also releases pending edit renders while the real Save
dialog owns input at 4x CPU slowdown. Page and thumbnail presentation is deferred
until confirmation paints or the dialog closes; asynchronous New/Merge callbacks
can then await rendering without deadlock. Superseded replacements are still
discarded. The test requires no pre-confirmation replacements or blank frames,
the exact requested image to resume, and confirmation input-to-frame latency
at or below 50 ms. This covers the edit/render overlap rather than waiting for
the editor to settle before Save. Hosted artifacts retain `save-overlap.json`.

For authoritative strict goals, unset `EDITOR_PROFILE_REPORT_ONLY` and run the
ordinary `--profile` (or `--profile-summary` on saved data). In the latest
three-repeat local AOT profile before the render-feature integration, startup's
maximum long task and showcase open remained over budget; the other four
original misses met their goals. The post-integration profile currently reports
three strict-goal misses: startup maximum long task 73/78 ms against 50/50,
showcase open 330/332.6 ms against 250/500, and cached typography next-page
presentation 100.1/100.9 ms against 100/200. The latter is a new strict-goal
miss, not added to the allowlist; ordinary next-page selection remains
98.8/99.7 ms for typography, 97.1/97.4 ms for the report and 97.2/98.2 ms for
the generated large document. The relative CI gate is separate and does not
relabel these strict misses as passing their original goals.

### Save-confirmation presentation isolation

Page and thumbnail replacements no longer commit while an interactive modal
owns input. The current decoded page stays visible. Confirmation yields two
animation frames before allowing dialog-owned asynchronous work and background
presentation; cancellation also releases pending presentation after a paint
opportunity. Existing epoch/URL checks still discard superseded images, and the
normal exact-resolution target resumes rather than being dropped. This also
allows New/Merge dialogs whose asynchronous actions await rendering to complete.

The full Chromium smoke includes a focused three-repeat `--save-overlap <site>`
regression at **4x CDP CPU slowdown**. It holds actual `09-images.pdf` edit-
triggered page/thumbnail responses, opens Save without settling those renders,
then releases them while the dialog owns input. It asserts no page/thumbnail
swap before confirmation, no blank frames, exact-target resumption, retention of
the edit in the saved PDF, and an unchanged **50 ms maximum** confirmation
input-to-next-frame ceiling. `EDITOR_SMOKE_ARTIFACTS` captures `save-overlap.json`.
For diagnostic CPU-throttled runs of the ordinary profile, set
`EDITOR_PROFILE_CPU_RATE=4`; unset it for the authoritative normal-speed profile
and its existing versioned CI policy.

### Navigation preview

After a page selection finishes, the editor schedules a 40-dpi render of the
next page after 250 ms, provided the document version and selection still match.
Pointer or keyboard input cancels queued speculative work before it can compete
with editing or Save; a completed navigation can schedule a fresh preview.
The Save-overlap regression holds the timer deterministically and verifies both
input paths cancel it, without relaxing the zero-bare-raster-miss gate.
It uses the existing bounded page-image cache; the normal exact-resolution
render remains unchanged and still replaces the preview. The full AOT profile
asserts that the preview is cached before navigation, then measures the actual
page-selection path. Across typography, report and the generated 12-page file,
next-page navigation improved from 115.5/116.6, 116.2/116.8 and 115.5/116.4 ms
(p50/p95) to 98.0/98.2, 97.8/98.8 and 98.3/98.3 ms. These are local,
three-sample results; first-open showcase and startup remain open work.

### Binary editor transport

The additive `RequestBytes(method, path, queryJson, bodyText, exchange, fileName)`
JS export accepts raw JSON text or `exchange.bytes` for PDF/image uploads. It
returns a `Uint8Array`; `exchange` receives `contentType`, `fileName`, optional
JSON `performance`, or a logged JSON `error`. The worker transfers the response
buffer to the UI rather than serializing/base64-encoding the payload. The runtime
still copies bytes across managed/JavaScript memory; this is not zero-copy.
The older `Request`/`RequestBinary` exports remain compatible.

`imageBlob(url, { current, pool } = {})` shares concurrent versioned page requests and caches up to
8 MiB of compressed image bytes. `image(url)` retains its data-URL contract for
assets, printing, thumbnails and older clients. Main-page replacements decode
temporary Blob URLs, retain the previous visible image until the replacement is
decoded and presentation is permitted, and revoke each temporary URL afterwards.
Retry/error injection and Save-overlap checks exercise this same Blob path.

`current` defaults to always true; visible-page and thumbnail callers provide
their render/document epoch predicate. Superseded queued primary requests and
pool fallbacks reject with `AbortError` before dispatch; obsolete results never
enter the cache. Document replacement cancels old timers/observers before the
command runs. A failed open re-observes the retained document's thumbnails.
`pool` defaults to true; uncached low-resolution visible previews use false to
avoid cold-replica setup. Exact refinement, thumbnails and print remain pooled.
The reviewed replicas now load the same lazy fallback/CJK fonts as the primary,
so the pool is default-on (`?pool=0` disables it); earlier opt-in notes below
describe the pre-fix integration.

The combined `/api/page/{i}/selection` response preserves the legacy text/object
endpoint shapes, caches serialized metadata until a document mutation, and avoids
shared-Form content scans when a page has no Form children. The UI presents the
first preview before requesting metadata, but remains inert until editable
metadata is ready. Its eight-page metadata cache and field cache are invalidated
on mutations; failed requests can be retried. Neighbor prefetch includes metadata
only while its page/version/input epoch remains current. Discrete zoom buttons
request exact refinement without the continuous-wheel 180-ms debounce; final
resolution, stale-response guards and Save presentation barriers are unchanged.

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
$pdfs = '01-spec-showcase.pdf', '03-typography-embedded.pdf', '09-images.pdf', '20-report.pdf' |
    ForEach-Object { (Resolve-Path (Join-Path samples $_)).Path }
$site = (Resolve-Path tools\FreeDF.EditorWasm\bin\pages\wwwroot).Path
dotnet run --no-build -c Release --project tools\FreeDF.EditorSmoke -- --profile https://ievangelist.github.io/FreeDF/ "$env:TEMP\editor-live.json" @pdfs
dotnet run --no-build -c Release --project tools\FreeDF.EditorSmoke -- --profile $site "$env:TEMP\editor-local.json" @pdfs
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

At the earlier seven-branch checkpoint, the Release build succeeded and the
full suite passed 1696/1696. A local
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

### Design system, accessibility and responsive modes

`theme.css` and `ui.js` add a token-based light/dark theme, motion with
reduced-motion support, and an accessibility layer; see
[accessibility.md](accessibility.md). Two smoke modes guard them:

- `--a11y <site> <sample.pdf>`: axe-core on every screen/theme/viewport plus
  keyboard, focus, target-size and text-spacing assertions. Install the test-only
  dependency first: `npm ci --prefix tools/FreeDF.EditorSmoke/a11y`.
- `--responsive <site> <sample.pdf>`: overflow, clipping, dialogs, menus, sheets
  and touch gestures across 10 viewports x DPR 1/2 x orientation x browser zoom
  100-400 %. Set `EDITOR_SMOKE_ARTIFACTS` to keep the screenshot matrix.

### Public release acceptance

Publish each reviewed, green integration batch independently rather than waiting
for a larger release. After the gated Pages deployment succeeds, run:

```powershell
.\tools\Test-EditorPublic.ps1 -TargetCommit <deployed-main-sha> `
  -ValidationLock 'E:\GitHub\copilot-worktrees\.validation-slot'
```

The command finds the successful deployment for that exact commit (or accepts
`-RunId`), downloads `editor-validation-aot`, and compares the public native WASM
**and UI entry assets** with it before and after acceptance. It rejects a site
at or above 1,000,000,000 bytes. It builds the existing smoke runner once, then
runs full editor/JSON/recovery/Save-overlap, merge, sample26 page-image and boot
injection checks in default-transport Chromium against the public URL.
Context-wide request monitors reject uploads, API calls and external requests.
Use `-Url` for another host serving `/FreeDF/`, and `-Output` for a fresh
evidence directory; logs and `acceptance.json` retain the commit, run, checksums,
payload size and result. The supplied shared validation lock is acquired
atomically, waited for without interrupting its owner, and released only by its
own exact owner token. Missing SDK/browser prerequisites fail visibly; install
them using the setup commands below.

Run from a checkout with the target runtime/source; harness-only changes are
allowed. `-FeatureSmoke` accepts dedicated `--selector` modes supported by the
checked-out runner and rejects unknown modes. Each receives the public URL;
`--viewport` additionally receives sample26. `EDITOR_SMOKE_SITE_ROOT` points
public-only fixture checks at the downloaded publish tree, not at a local site.
Review changed pixels when rendering changes, and repeat spot timing comparisons
only when performance code changes; neither expensive sweep is part of the lean
default command. A failed acceptance blocks promotion of further batches until
the regression is understood or the deployment is safely reverted.

### Local publish and smoke

Use the SDK declared in root `global.json`:

```powershell
.\tools\Publish-EditorPages.ps1
dotnet test tests\FreeDF.Tests -c Release --filter "FullyQualifiedName~Editor|FullyQualifiedName~ExistingObjectTransform"
dotnet build tools\FreeDF.EditorSmoke -c Release
.\tools\FreeDF.EditorSmoke\bin\Release\net10.0\playwright.ps1 install chromium
$site = (Resolve-Path tools\FreeDF.EditorWasm\bin\pages\wwwroot).Path
dotnet run --no-build -c Release --project tools\FreeDF.EditorSmoke -- $site
dotnet run --no-build -c Release --project tools\FreeDF.EditorSmoke -- --merge $site
```

The publish script checks required static assets and WASM magic bytes, rejects
sites at or above the 1 GB Pages limit, and reports total bytes/files and elapsed
publish time (including generated sample assets when present). Override
`-Output` to publish elsewhere and `-BasePath` for another repository path or `/`.
Always pass an absolute publish directory to the smoke runner (`dotnet run`
starts in its project directory).

For an explicit AOT comparison, install the pinned SDK and `wasm-tools` in a
user-writable dotnet root, set `DOTNET_ROOT`/`PATH` only for that command, then:

```powershell
.\tools\Publish-EditorPages.ps1 -Aot -Output "$env:TEMP\freedf-aot-pages" -ArtifactsPath "$env:TEMP\freedf-aot-build"
.\tools\Publish-EditorPages.ps1 -Output "$env:TEMP\freedf-control-pages" -ArtifactsPath "$env:TEMP\freedf-control-build"
```

Keep intermediates separate: reusing the default `obj` across AOT and non-AOT
can mix trimmed and untrimmed artifacts. AOT requires trimming; the opt-in retains
the existing application assembly roots for the PDF engine, not JSON reflection,
keeps globalization and enables SIMD without enabling threads. Run the same full,
boot, merge, page-image and profile checks against both outputs before changing
production publishing.

The production workflow installs the pinned SDK's workload, caches NuGet
dependencies and uses isolated intermediate/publish directories. Its manual
dispatch defaults to `aot=true`, `deploy=false`: validate and download the
artifact first. Explicit `aot=false` selects a non-AOT fallback but does not bypass
performance gates. Native compilation added about five minutes locally; the
hosted-runner duration is not yet measured. Full, merge, boot, page-image,
trim-safe JSON round trips and the profile CI gates all target the AOT output.
`--json-safety <site>` runs just the JSON round trips for focused validation.

The Chromium runner hosts **only static files** under `/FreeDF/`, with no
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
dotnet run -c Release --project tools\FreeDF.EditorSmoke -- --page-image $site $sample $artifacts
```

It samples the displayed page image on every animation frame while zooming,
toggling both sidebars and navigating pages; it also checks decoded pixels and
injects transient/permanent decode failures to verify cache-bypass retry,
last-good-image retention and visible error reporting. Failure screenshots and
the frame trace are written to the artifact directory. The command can be
repeated with other sample paths; keep PDFs local to the browser.

The original server host remains available:

```powershell
dotnet run -c Release --project tools\FreeDF.EditorWeb -- file.pdf
```

It uses the same `EditorSession` under its HTTP adapter, retains installed
fonts, startup-file saving and protected export, and does not load WebAssembly.

## Integration batch 2 (fonts, CMYK, inert models, editor)

Reviewed branches were cherry-picked in order onto the parity tip `bea1a95`: fallback fonts + lazy CJK data; DeviceCMYK table, nested group blend spaces and DeviceN attributes; inert U3D/PRC/media/RichMedia/XFA/JavaScript models; page-level undo/redo; GitHub repository links (Octicons notice); one-click sample collection; viewport-overflow fix; browser crypto (password dialog, owner unlock, AES-256 protected save, samples 17/18). Release build 0 errors; full suite **2144/2144**; fresh AOT publish 47 valid WASM artifacts (690 files, 74,916,843 bytes: 187 startup-set files / 56,340,200 bytes, 475 lazy `fallback-fonts` files / 13,733,097 bytes, 28 lazy `samples` files / 4,843,546 bytes). The startup request list (58 requests in the profile) contains no `fallback-fonts/` or `samples/` file; fonts, CJK chunks and samples are fetched on first use only. Chromium smokes: full, `--merge`, `--samples`, `--protection`, `--page-history`, `--viewport`, `--boot`, `--page-image` (sample 26) and the samples node tests pass; the 535-group profile CI gate passes. Pixel hashes (real WASM profile) match the checked-in file: only `09-images.pdf` changed (`33D9F80F...` -> `5A3B5223...`, CMYK table); 01, 03, 20 and the 12-page fixture are unchanged, so bundled fonts did not change those renders. Two stale smokes were updated for browser crypto (merge stack frame, samples 17/18 now offered) and sample opening now goes through the password-aware open path.

Caveats kept from the child reports:

* **CMYK table provenance:** values are measured from PDFium (BSD-3-Clause; Foxit-origin data whose upstream source is unverified). Residual risk; the `cmyk-srgb-9x9x9x9.bin` resource is swappable (`tools/make_cmyk_table.py`).
* **Adobe AFM notice:** the permission paragraph now matches the published Core 14 `MustRead` text verbatim, and the widths were re-derived from the published AFMs and match the embedded table. Copyright years differ per AFM file and are listed per family (the earlier single line was incomplete and was corrected).
* **U3D/PRC:** fixtures are synthetic; PRC layout and compressed-mode U3D block payloads are not verified against Adobe-produced files. XFA/JavaScript analysis is heuristic and inert.
* **CJK fallback:** the reference comparison against Edge was unreliable on the development machine (Edge drew mojibake); CJK is covered by fixtures and unit tests, not pixel parity. Serif CJK uses the sans design.
* **Samples 17/18:** now offered because `window.pdfEditorCapabilities.openEncrypted` is set in `<head>` before the menu renders; verified in `--samples` and `--protection` Chromium smokes.
* **Not full parity:** every matrix row that was Partial stays Partial (15 Partial / 0 Todo); nothing here is a full ISO 32000-2 parity claim. Startup byte delta versus the pre-batch tree was not re-measured on a control publish; the lazy font/sample bytes above are additive and outside startup.

## Integration batch 3 (worker pool, annotation and form authoring)

Cherry-picked in order on top of batch 2 (`27da5b9`): worker render-replica pool with band rendering and snapshot route (`343c8e2`, `91c9bfd`, `b23c588`); annotation authoring API, editor commands and fixes (`dcdbff0`, `107cd58`, `579b7b7`); AcroForm field authoring, editor UI and indexing (`2f62a3d`, `d7253b0`, `7617a72`). The signature-trust/PAdES-LTV commits (`6b58e96`, `a1464e8`) were **not integrated**: they add an `HttpClient` RFC 3161 client to `PortableDocument.Document`, which fails the guard tests `Document_assembly_has_no_process_or_network_dependency` and `Opening_listing_rendering_and_saving_never_touch_external_targets` (and the assembly is shipped to the browser). That branch needs the network client moved out of the Document assembly (and its pending re-review) before it can land.

Integration fix: form-fill appearance generation registered a standard font in the AcroForm `/DR`, which broke `DocMdpTests.Editor_form_fill_after_certification_is_permitted_by_p2`; appearance fonts are now created without touching `/DR`. Conflicts (publish asset list, smoke flags, `EditorSession` page-history capture for annotation/form operations, undo status text) were merged keeping both sides.

Status stays **Partial** for the annotation, form, action and rendering rows: authoring covers the implemented annotation/field types with generated appearances, not every ISO 32000-2 annotation or widget behaviour; signatures/PAdES-LTV remain as documented before this batch. **The worker pool ships opt-in (`?pool=1`), not on by default:** with it enabled the Chromium recovery smoke ("Resume retains exact original-font/geometry raster fidelity") fails, because render replicas have no fallback-font asset source (batch 2) and so can draw non-embedded text differently from the primary engine. Enabling it by default needs the replicas to receive the same lazy font assets (or a hash parity check) first. Pool speed-ups are measured on a non-AOT build in the child report (`docs/performance/2026-10-30-worker-pool.md`); AOT/SIMD pool timing was not measured there and the pool benchmark (`--pool`) was not rerun here.