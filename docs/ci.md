# Editor CI

`.github/workflows/editor-pages.yml` is a small DAG:

```
plan -> build (AOT site, content-hash cached) -> smoke[default|forms|authoring|boot|viewport|a11y|responsive|profile-invariants]
     \-> unit[node|dotnet-editor|dotnet-codecs|dotnet-render]
all of the above -> validated (single gate) -> package (manifest verified) -> deploy
```

- **Build once.** The published site is cached by a hash of every input that can change it; an exact hit skips the AOT publish. Misses restore incremental intermediates and otherwise fall back to a full build. The site is uploaded as the `editor-site` artifact with a SHA-256 manifest; smoke jobs and `package` verify it, so deploy ships exactly the validated bytes.
- **Isolation.** Each smoke chunk has its own runner, browser and artifact folder. Timing gates are not part of the blocking pipeline (see Performance).
- **Gate.** `validated` fails unless every planned job succeeded. Partial (single-chunk) runs never package or deploy.

## Run one chunk

CI: Actions -> "Publish browser PDF editor" -> Run workflow -> choose `chunk` (e.g. `forms`); or
`gh workflow run editor-pages.yml --ref <branch> -f chunk=forms`.

Locally (site published first with `tools/Publish-EditorPages.ps1 -Output <dir>`; Chromium installed via the Playwright script in `tools/FreeDF.EditorSmoke/bin/Release/net10.0/playwright.ps1`):

```powershell
./tools/Invoke-EditorChunk.ps1 -Chunk forms -Site <dir>/wwwroot
./tools/Invoke-EditorChunk.ps1 -Chunk dotnet-editor
```

Chunks: `node` (worker/selection invariants, sample/outline modules and publish bundler), `dotnet-editor`, `dotnet-codecs`, `dotnet-render`, `default`, `forms`, `authoring` (merge, samples, annotations, object styling, local file/font access, storage migration and Contents), `boot` (cold boot, injected failures and offline worker migration/rollback), `viewport` (viewport, page-image), `a11y`, `responsive`, `profile-invariants` (blocking: runs the profile with timing misses report-only, so functional invariants such as overlay-only bare-page pixel exactness still fail), `perf` (non-blocking, see below).

## Change detection

The plan job diffs the PR/push range. If every changed file is under docs/, acklog/, a root *.md, LICENSE, or non-code .github metadata, build/unit/smoke/package/deploy are skipped and the always-run alidated gate still passes. Dispatch, schedule, unreadable or 300+ file diffs run everything.

## Performance (non-blocking)

.github/workflows/editor-perf.yml runs nightly (and on workflow_dispatch) on its own runner: save-overlap ceiling, full profile, profile-ci and the synthetic regression check, with unchanged thresholds. It reports via job summary, artifacts and a single `Editor performance regression` issue (opened/commented on failure, closed on recovery). Locally: `./tools/Invoke-EditorChunk.ps1 -Chunk perf`. The site build is shared via the reusable `editor-site.yml`.

Synthetic fault-injection checks use a timing-normalized copy of the input
profile, validated before injecting faults. Real timing regressions still fail
the preceding `profile-ci` check; they no longer contaminate passing synthetic
controls such as the single-repeat outlier. Pixel hashes, allocations, raster
counts and required groups are never normalized. `dotnet-editor` also includes
page-tree inheritance, named-destination import and lexer-token regressions;
`node` discovers the theme persistence/contrast and PWA install/offline/launch
checks automatically. The service-worker smoke also verifies manifest
installability, real offline edit/download, and Save through a received local
file handle.

Raster goldens cover the fixed fixture/committed-sample corpus, not the optional
generated `27-full-collection.pdf`. Generating that local collection must not
change which pages the golden gate compares. The FreeDF rebrand refreshes only
sample 28's 18 intentional branding changes; all other page hashes retain the
pre-optimization renderer baseline.
