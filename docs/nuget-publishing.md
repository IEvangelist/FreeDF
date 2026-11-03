# NuGet packages and trusted publishing

FreeDF publishes these .NET 10 libraries as separate NuGet packages:

| Package | Purpose |
| --- | --- |
| `PortableDocument.Core` | PDF objects, parsing, file structure, filters, encryption, and signature primitives |
| `PortableDocument.Document` | Document model, editing, forms, annotations, metadata, navigation, structure, and validation |
| `PortableDocument.Content` | Content interpretation, fonts, text shaping, color, images, graphics, and optional content |
| `PortableDocument.Render` | Software rendering, structured text extraction, annotation tools, and redaction |
| `PortableDocument.FallbackFonts` | Optional fallback font assets used by the renderer |
| `PortableDocument.Signing.Http` | Opt-in HTTP RFC 3161 timestamp-authority client |

Project references become package dependencies. The fallback-font and font-license
assets are included in `PortableDocument.FallbackFonts`; that package is optional
unless an application needs those fallback assets. `PortableDocument.Signing.Http`
is likewise optional and is the only package that performs HTTP timestamp requests.

FreeDF code uses the MIT license, copyright (c) 2026-Present David Pine.
Each package includes `LICENSE`, `README.md`, `THIRD-PARTY-NOTICES.md` and the
Unicode license. Bundled third-party data and fonts retain their own licenses.
The packages currently target `net10.0` and default to `0.1.0-preview.4`.
The embedded README comes from [the package quick start](nuget-readme.md), not
the editor-focused repository README. Package metadata links to the FreeDF
homepage, source repository, release notes, and MIT license.

## Publishing

Publishing is triggered by pushing a semantic-version tag such as
`v0.1.0-preview.1`. Start with prerelease package versions while validating
consumer compatibility, then publish a stable version such as `v0.1.0` when
ready. Each preview needs a new increasing prerelease number (for example,
`v0.1.0-preview.2`). The workflow tests the solution, packs all six libraries
and symbol packages, then publishes them to NuGet.org. It uses NuGet Trusted
Publishing through GitHub Actions OIDC; no long-lived NuGet API key is stored
in GitHub.
Release tags must point to a commit on `main`. Pull requests also test and
pack the libraries, but never request an OIDC credential or publish packages.

Before the first release:

1. Confirm the package IDs are available or owned by the intended NuGet.org
   account. A policy permitting new packages can authorize their first publish;
   no API-key bootstrap publish is required.
2. In GitHub repository settings, add the repository variable `NUGET_USER` with
   the NuGet.org account username (not the email address).
3. In NuGet.org account settings, create a Trusted Publishing policy for:
   - Repository owner: `IEvangelist`
   - Repository: `FreeDF`
   - Workflow file: `publish-nuget.yml`
   - Environment: leave blank
4. Ensure the policy permits publishing these package IDs and new versions.

The workflow requests the OIDC credential only in its separate publish job,
after testing and packing have completed. NuGet issues a short-lived API key
through `NuGet/login@v1`; the workflow uses it immediately and skips already
published package versions so a partially completed release can be retried.

After successful publication, a separate job creates a GitHub Release with
installation instructions, generated change notes, and all `.nupkg` and
`.snupkg` assets. Tags containing a prerelease suffix create prereleases, not
stable releases. Only this release job has `contents: write`; NuGet OIDC
permission remains scoped to the publish job. Retrying an existing release
refreshes its assets without overwriting manually edited notes.
Add curated highlights and compatibility notes in `docs/releases/<version>.md`
before tagging; the release job includes them alongside generated notes.
An already-published package version is immutable: README or metadata changes
appear on NuGet only after publishing a new version.

The release tests use bundled fonts without installed-font fallback for
host-independent raster golden hashes. Fuzz fixture names use `/` separators
so Windows and Linux exercise the same deterministic mutations.
JBIG2 text regions are limited to 1,000,000 symbol instances; exceeding that
limit produces a render warning rather than unbounded decoding work.
Before publishing, a separate package consumer restores all six libraries
from the packed artifacts into an isolated cache, builds, extracts PDF text,
and verifies that Latin and CJK font assets are copied to its output.

For a local package build, pass an explicit version:

```powershell
dotnet pack src\PortableDocument.Render\PortableDocument.Render.csproj -c Release -p:Version=0.1.0-preview.4
```

See the [NuGet Trusted Publishing documentation](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
for policy ownership and package-scope details.
