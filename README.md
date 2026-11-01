# FreeDF

**A free PDF editor and PDF 2.0 engine written in C#/.NET.** Open, inspect,
render, edit, and save PDFs with FreeDF's browser editor, or use the
`PortableDocument.*` libraries directly in your .NET application.

[![Open FreeDF in your browser](https://img.shields.io/badge/try-FreeDF%20editor-be185d)](https://ievangelist.github.io/csharp-pdf/)
[![PDF 2.0 coverage](https://img.shields.io/badge/PDF-2.0%20in%20progress-0e7490)](docs/spec-matrix.md)

## Try the editor

**[Open the browser editor](https://ievangelist.github.io/csharp-pdf/)** and try
the [18-page showcase](https://ievangelist.github.io/csharp-pdf/#sample=28).
The app also includes a set of small sample PDFs covering forms, annotations,
layers, encryption, accessible documents, typography, images, charts, and more;
see the [sample index](samples/README.md).

The GitHub Pages editor runs as WebAssembly in your browser. Your PDFs are
opened and processed on your device; they are not uploaded to a PDF service.
The installable PWA caches the app for offline reuse. For architecture,
privacy, offline behavior, and editor details, see
[docs/browser-editor.md](docs/browser-editor.md).

## The app

These are screenshots of FreeDF itself, with two real sample documents opened
in the editor. GitHub selects the matching image for light or dark mode.

**A dashboard of charts, sparklines, and KPIs**

![FreeDF editor in light mode with the dashboard charts sample open](docs/images/editor-dashboard-light.png#gh-light-mode-only)
![FreeDF editor in dark mode with the dashboard charts sample open](docs/images/editor-dashboard-dark.png#gh-dark-mode-only)

**A filled, interactive event-registration form**

![FreeDF editor in light mode with the AcroForm event-registration sample open](docs/images/editor-form-light.png#gh-light-mode-only)
![FreeDF editor in dark mode with the AcroForm event-registration sample open](docs/images/editor-form-dark.png#gh-dark-mode-only)

In the editor you can:

- Open and render PDFs; navigate pages and outlines, search text, and inspect
  document properties.
- Edit supported page text and graphic objects; add text, shapes, images, and
  annotations; fill and edit AcroForms.
- Insert, duplicate, delete, rotate, reorder, and merge pages; undo changes,
  preview the rendered page, and download the result.
- Export pages as PDFs or images, extract text, and create compressed or
  password-protected PDFs.
- Work with encrypted documents using a password prompt. Passwords are not
  stored, and the browser build does not save decrypted recovery copies.
- Choose light/dark/system appearance, adjust the interface layout, and install
  the PWA for offline use.

Editing is intentionally scoped: it is not a general-purpose vector design
program, and some complex PDF structures have conservative or unsupported edit
paths. See the [editor operation inventory](docs/browser-editor.md#editor-operation-inventory)
and [PDF 2.0 coverage matrix](docs/spec-matrix.md) for implementation details
and known gaps.

## Run locally

The local HTTP editor is useful for development and opens an optional PDF on
startup:

```powershell
dotnet run -c Release --project tools/FreeDF.EditorWeb
dotnet run -c Release --project tools/FreeDF.EditorWeb -- .\samples\25-dashboard-charts.pdf
```

By default, the editor is available at `http://localhost:5080`. To choose a
different port, pass `--port 5081`. This local host serves the editor UI and
its PDF API from your machine; it is separate from the browser-local WebAssembly
deployment described above.

## Use the .NET libraries

The libraries are split by responsibility: PDF file structure and writing,
document-level features, content interpretation, and rendering. For example:

```csharp
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

var document = PdfDocument.Load(bytes);
var text = Pdf.ExtractText(document.Pages[0]);
var renderedPage = Pdf.RenderPage(document.Pages[0], dpi: 96);
```

`PdfDocument` loads and exposes a PDF; `PortableDocument.Render.PortableDocument`
provides the rendering and text-extraction entry points. The alias keeps that
class distinct from the `PortableDocument` root namespace.

| Project | What it provides |
| --- | --- |
| `PortableDocument.Core` | PDF objects, parsing, file structure, filters, writing, encryption, and signature primitives |
| `PortableDocument.Document` | Document/page models, editing and page import, annotations, forms, metadata, navigation, structure, and validation |
| `PortableDocument.Content` | PDF page-content interpretation, fonts and text shaping, color spaces, images, graphics, and optional-content layers |
| `PortableDocument.Render` | Software page rendering, structured text extraction, annotation tools, and redaction |
| `PortableDocument.FallbackFonts` | Bundled font fallback assets used by the browser editor |
| `PortableDocument.Signing.Http` | HTTP RFC 3161 timestamp-authority client |

PDF actions and embedded content are inspected as data; parsing a PDF does not
execute its JavaScript, launch actions, or open remote destinations. Validation
and signature/trust APIs report what they can establish; they are not a
substitute for a qualified PDF/A or PDF/UA certification workflow.

## What's in this repository

- `src/` — the `PortableDocument.*` libraries above.
- `tools/FreeDF.EditorWeb/` — local HTTP-hosted PDF editor.
- `tools/FreeDF.EditorWasm/` — browser-local WebAssembly editor used by the
  static GitHub Pages app.
- `tools/FreeDF.Samples/` and `samples/` — C# sample-PDF generators and the
  committed example documents. The [sample README](samples/README.md) explains
  each document, the encrypted samples, and how to generate the full collection.
- `tests/FreeDF.Tests/` — parser, document, rendering, editor, conformance, and
  regression tests, with PDF fixtures under `tests/fixtures/`.
- `tools/FreeDF.EditorSmoke/` — Chromium editor, accessibility, responsive,
  offline, and performance smoke checks.
- `bench/` and `docs/performance/` — benchmark tooling and recorded results.
- `docs/spec-matrix.md` — current ISO 32000-2 coverage and known gaps.

## Build and test

Requires the .NET SDK version pinned in [`global.json`](global.json).
`PortableDocument.slnx` is the canonical solution:

```powershell
dotnet build PortableDocument.slnx -c Release
dotnet test PortableDocument.slnx -c Release
```

Package versions are centrally managed in `Directory.Packages.props`.

## Project status

This is an actively developed PDF 2.0 implementation, not a claim of complete
ISO 32000-2 parity. The coverage matrix marks implemented clauses as Done,
Partial, or Todo and links to the remaining gaps. See
[docs/spec-matrix.md](docs/spec-matrix.md) and
[docs/spec-backlog.md](docs/spec-backlog.md) before relying on a feature for a
particular class of PDFs.
