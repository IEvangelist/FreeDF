# FreeDF for .NET

**Read, render, extract, and edit PDFs in C# - without a PDF service.**
FreeDF is a managed .NET 10 PDF engine. Choose the packages you need; the
rendering package brings the core document and content libraries with it.
PDF files stay in your application unless you explicitly send them elsewhere.

[Homepage and browser editor](https://ievangelist.github.io/FreeDF/) |
[Source](https://github.com/IEvangelist/FreeDF) |
[Release notes](https://github.com/IEvangelist/FreeDF/releases) |
[PDF feature coverage](https://github.com/IEvangelist/FreeDF/blob/main/docs/spec-matrix.md)

## Get started

```shell
dotnet add package PortableDocument.Render --version 0.1.0-preview.5
```

```csharp
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

var document = PdfDocument.Load(File.ReadAllBytes("input.pdf"));
var page = document.Pages[0];
var text = string.Concat(Pdf.ExtractText(page).Select(span => span.Text));
File.WriteAllBytes("page-1.png", Pdf.RenderPage(page, dpi: 144).ToPng());
Console.WriteLine(text);
```

`PdfDocument.Load` reads a complete byte array into memory. `Pages` is
zero-indexed. The `Pdf` alias distinguishes the rendering entry point from
the `PortableDocument` namespace.

## Choose your packages

| Package | Use it for |
| --- | --- |
| [PortableDocument.Core](https://www.nuget.org/packages/PortableDocument.Core) | PDF objects, parsing, filters, writing, encryption, and signature primitives |
| [PortableDocument.Document](https://www.nuget.org/packages/PortableDocument.Document) | Pages, editing, forms, annotations, metadata, outlines, and validation |
| [PortableDocument.Content](https://www.nuget.org/packages/PortableDocument.Content) | Content interpretation, fonts, text shaping, color, and image decoding |
| [PortableDocument.Render](https://www.nuget.org/packages/PortableDocument.Render) | Page-to-PNG rendering, text extraction, annotation tools, and redaction |
| [PortableDocument.FallbackFonts](https://www.nuget.org/packages/PortableDocument.FallbackFonts) | Optional Latin fonts and CJK glyph assets for PDFs without embedded fonts |
| [PortableDocument.Signing.Http](https://www.nuget.org/packages/PortableDocument.Signing.Http) | Optional HTTP RFC 3161 timestamp-authority client |

Use the same version across explicitly referenced FreeDF packages. Package
dependencies are resolved automatically; you don't need to install all six.

## Render with bundled fallback fonts

```shell
dotnet add package PortableDocument.FallbackFonts --version 0.1.0-preview.5
```

```csharp
using PortableDocument.FallbackFonts;
using PortableDocument.Render;

var options = new PdfRenderOptions
{
    FontFallback = new BundledFontFallback(new DirectoryAssetSource())
};
var rendered = Pdf.RenderPage(page, options, dpi: 144);
File.WriteAllBytes("page-1.png", rendered.ToPng());
```

The font package copies its assets beside your application. This example uses
only bundled substitutes for consistent output across hosts; omitting
`FontFallback` uses installed system fonts. The optional timestamp client
performs HTTP requests only when invoked.

For render diagnostics, set `StrictContentValidation = true` on
`PdfRenderOptions` passed to `Pdf.RenderPage` or `Pdf.RenderBand`; inspect
`RenderedPage.Warnings` for recognized malformed content-stream operands and
state transitions. This opt-in reports warnings only and does not reject or
change permissive rendering. It does not apply to `Pdf.ExtractText` or
`Pdf.Analyze`, which do not expose content-validation warnings, and it is not
a PDF conformance validator.

## Preview versions and compatibility

FreeDF is actively developed and **does not claim complete PDF 2.0 parity**.
Preview releases may change APIs or output; pin a version and test your PDF
corpus before upgrading. Use `0.1.0-preview.3` or later: `preview.2` contains a
fallback-font packaging defect.

All six packages share a version. Tags follow `vMAJOR.MINOR.PATCH-preview.N`
for previews and `vMAJOR.MINOR.PATCH` for stable releases. See
[GitHub Releases](https://github.com/IEvangelist/FreeDF/releases) for changes,
known limitations, and downloadable packages.

PDF actions and JavaScript are inspected as data, not executed. Validation and
signature APIs report what they can establish; they are not a substitute for
independent PDF/A, PDF/UA, or signature-trust certification. Review the
[coverage matrix](https://github.com/IEvangelist/FreeDF/blob/main/docs/spec-matrix.md)
before relying on a particular feature.

## License and support

FreeDF code is [MIT licensed](https://github.com/IEvangelist/FreeDF/blob/main/LICENSE).
Copyright (c) 2026-Present David Pine. Included third-party data and fonts
retain their own licenses; packages include `THIRD-PARTY-NOTICES.md` and the
applicable license files.

[Report an issue](https://github.com/IEvangelist/FreeDF/issues) with a minimal
reproduction, package version, and runtime details. Share a PDF only if you
have permission and it contains no sensitive information.
