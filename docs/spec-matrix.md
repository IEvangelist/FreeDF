# ISO 32000-2:2020 (PDF 2.0) coverage matrix

Status: **Done** = implemented and covered by tests, **Partial** = implemented with known gaps, **Todo** = not implemented.
Full spec parity is the goal; this table is the honest current state.

## Parser (`Pdf.Core`, `Pdf.Document`)

| Clause | Feature | Status | Notes |
|---|---|---|---|
| 7.2-7.3 | Lexical conventions, all object types | Done | Names `#xx`, literal/hex strings, tolerant numbers |
| 7.5 | File structure, xref tables/streams, hybrid, `Prev` chain, object streams | Done | |
| 7.5.6 | Recovery of damaged files | Done | GregsEx (PCRE2, byte mode) scans for `n g obj`; test `RepairsBrokenXref` |
| 7.4 | Filters: Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors | Done | |
| 7.4 | DCT (baseline), CCITT, JBIG2, JPX | Partial | DCT baseline/extended/progressive **Done** (Huffman; arithmetic and lossless unsupported; nearest-neighbour chroma upsampling); CCITT G3 1D/2D + G4 **Done**; JBIG2/JPX **Todo** |
| 7.6 | Encryption (Standard RC4/AES, public-key) | Partial | Standard handler R2-R6 (RC4 40/128, AESV2, AESV3) **Done**, read-only; public-key handlers and encrypted save **Todo** |
| 7.7 | Document structure: page tree, inheritance | Done | |
| 7.8-7.9 | Content streams, resources, functions (types 0,2,3,4) | Done | |
| 7.10-7.11 | File specs, embedded files, data structures | Todo | |
| 12 | Annotations, forms, outlines, actions | Partial | Read-only: outlines, named dests, annotation list (URI/GoTo), AcroForm field tree, page labels, embedded files (`DocumentFeatures`). Not yet: annotation appearance rendering, form editing, other action types |
| 14 | Metadata, tagged PDF, logical structure | Partial | Info, XMP packet, tagged flag, language. Structure tree walking not yet |
| 14.8/PDF/A,UA | Validation | Todo | |

## Renderer (`Pdf.Content`, `Pdf.Render`)

| Clause | Feature | Status | Notes |
|---|---|---|---|
| 8.4 | Graphics state, paths, clipping | Done | AA scanline raster, non-zero/even-odd |
| 8.4.3 | Stroking: width, caps, joins, miter, dash | Done | |
| 8.6 | Device, Cal*, Lab, Indexed, Separation, DeviceN | Partial | Cal*/ICC treated as device spaces, no ICC transform; CMYK is an approximation |
| 8.7 | Tiling and shading patterns | Partial | Shading 1-3 done; mesh types 4-7 **Todo** |
| 8.9-8.10 | Images, inline images, XObject forms | Partial | See filters above; stencil + colour-key + SMask alpha |
| 9 | Fonts: TrueType, Type0/CID, Type3, ToUnicode | Partial | CFF/Type1 outlines **Todo** (system-font fallback); no predefined CJK CMaps |
| 11 | Transparency: alpha, blend modes | Partial | Alpha + separable blend modes; groups/soft masks (SMask in ExtGState) **Todo** |
| 8.11 | Optional content (OCG/OCMD) | Partial | Default config only |
| 10 | Rendering intents, overprint, halftones, transfer funcs | Todo | |
| 13 | Multimedia, 3D, RichMedia, XFA, JavaScript | Out of scope | Not renderable statically |

## Editor

| Feature | Status |
|---|---|
| Mutable object model, full/incremental save, merge/split, forms, redaction, signing | **Todo** |

## Render comparison

`tools/Pdf.RenderCompare` renders with our renderer and with the browser's built-in PDF viewer (PDFium in Chrome/Edge, via Playwright), crops the page, and reports the fraction of pixels that differ beyond a 3x3-neighbourhood tolerance, plus diff images and `report.json`.

```
dotnet run --project tools/Pdf.RenderCompare -- tests/fixtures --out compare-out [--channel msedge|chrome] [--dpi 96] [--page-tolerance 0.02]
```

Independent rasterizers differ in anti-aliasing and font hinting, so tolerances are per-fixture, not zero.
