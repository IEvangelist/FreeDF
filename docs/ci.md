# Editor CI

`.github/workflows/editor-pages.yml` is a small DAG:

```
plan -> build (AOT site, content-hash cached) -> smoke[default|forms|authoring|boot|viewport|a11y|responsive|profile]
     \-> unit[node|dotnet-editor|dotnet-codecs|dotnet-render]
all of the above -> validated (single gate) -> package (manifest verified) -> deploy
```

- **Build once.** The published site is cached by a hash of every input that can change it; an exact hit skips the AOT publish. Misses restore incremental intermediates and otherwise fall back to a full build. The site is uploaded as the `editor-site` artifact with a SHA-256 manifest; smoke jobs and `package` verify it, so deploy ships exactly the validated bytes.
- **Isolation.** Each smoke chunk has its own runner, browser and artifact folder. `profile` and the save-overlap timing in `default` never share a runner with other heavy work.
- **Gate.** `validated` fails unless every planned job succeeded. Partial (single-chunk) runs never package or deploy.

## Run one chunk

CI: Actions -> "Publish browser PDF editor" -> Run workflow -> choose `chunk` (e.g. `forms`); or
`gh workflow run editor-pages.yml --ref <branch> -f chunk=forms`.

Locally (site published first with `tools/Publish-EditorPages.ps1 -Output <dir>`; Chromium installed via the Playwright script in `tools/AdoBye.EditorSmoke/bin/Release/net10.0/playwright.ps1`):

```powershell
./tools/Invoke-EditorChunk.ps1 -Chunk forms -Site <dir>/wwwroot
./tools/Invoke-EditorChunk.ps1 -Chunk dotnet-editor
```

Chunks: `node`, `dotnet-editor`, `dotnet-codecs`, `dotnet-render`, `default`, `forms`, `authoring` (merge, samples, annotations), `boot`, `viewport` (viewport, page-image), `a11y`, `responsive`, `profile` (profile, CI regressions, synthetic regression check).
