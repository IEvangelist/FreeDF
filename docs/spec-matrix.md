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
| 7.4 | DCT (baseline), CCITT, JBIG2, JPX | Partial | DCT baseline/extended/progressive **Done** (Huffman; arithmetic and lossless unsupported; nearest-neighbour chroma upsampling); CCITT G3 1D/2D + G4 **Done**; JBIG2 **Done** (generic MMR/arithmetic templates 0-3 + TPGD, refinement, symbol dict, text region, halftone/pattern, /JBIG2Globals; verified vs Annex H and generated fixtures; unsupported: Huffman symbol dict with refinement/aggregation, 12-AT extended templates, colour extensions; MMR halftone lightly tested); JPX **Todo** (not implemented); DCT arithmetic/lossless **Todo** |
| 7.6 | Encryption (Standard RC4/AES, public-key) | Partial | Standard handler R2-R6 (RC4 40/128, AESV2, AESV3) **Done**, read-only; public-key handlers and encrypted save **Todo** |
| 7.7 | Document structure: page tree, inheritance | Done | |
| 7.8-7.9 | Content streams, resources, functions (types 0,2,3,4) | Done | |
| 7.10-7.11 | File specs, embedded files, data structures | Todo | |
| 12 | Annotations, forms, outlines, actions | Partial | Outlines, named dests, annotation list (URI/GoTo), AcroForm field tree, page labels, embedded files. Annotation normal-appearance (/AP /N, /AS) rendered per 12.5.5 with generated default appearances for annotations lacking /AP (highlight, underline, strikeout, squiggly, square, circle, line, polygon, ink, free text). Form filling for text/button/choice fields (`PdfEditor.SetFieldValue`; multiline, comb, alignment, MK borders/backgrounds). Not yet: other action types |
| 14 | Metadata, tagged PDF, logical structure | Partial | Info, XMP packet, tagged flag, language. Structure tree walking not yet |
| 14.8/PDF/A,UA | Validation | Todo | |

## Renderer (`Pdf.Content`, `Pdf.Render`)

| Clause | Feature | Status | Notes |
|---|---|---|---|
| 8.4 | Graphics state, paths, clipping | Done | AA scanline raster, non-zero/even-odd |
| 8.4.3 | Stroking: width, caps, joins, miter, dash | Done | |
| 8.6 | Device, Cal*, Lab, Indexed, Separation, DeviceN, ICCBased | Partial | ICCBased: `IccProfile` parses matrix/TRC RGB, gray kTRC and LUT (mft1/mft2/mAB) RGB/CMYK profiles (A2B0, else A2B1; PCS XYZ/Lab) to sRGB via Bradford D50->D65, cached per profile (SHA-1) with a baked 17-pt grid for LUT profiles; verified against lcms for a CMYK SWOP profile (exact on corners). sRGB profiles pass through; unparseable/mismatched profiles fall back to the device space. Not done: rendering-intent selection / black-point compensation, B2A, named/N-colour profiles; CalGray/CalRGB are still treated as device spaces; DeviceCMYK itself stays an approximation |
| 8.7 | Tiling and shading patterns | Partial | Shading 1-3 done; mesh types 4-7 **Done** (Gouraud triangles; patches tessellated adaptively 8-40 per side by size; types 4/5/6/7 tested incl. type 5 per-vertex bit alignment, type 7 and edge-flag continuation; no per-pixel function eval, colour interpolated then mapped through /Function) |
| 8.9-8.10 | Images, inline images, XObject forms | Partial | See filters above; stencil + colour-key + SMask alpha |
| 9 | Fonts: TrueType, Type0/CID, Type3, ToUnicode | Partial | Embedded CFF (Type1C, CIDFontType0C, OpenType-CFF) outlines via `CffFont` (Type 2 charstrings, subrs, flex, FDSelect; seac and arithmetic ops not supported). Legacy Type 1 (FontFile, PFA/PFB) **Todo**; no predefined CJK CMaps; SIDs 229-378 of the CFF standard strings are placeholders |
| 11 | Transparency: alpha, blend modes | Partial | Alpha + all 16 blend modes (separable and Hue/Saturation/Color/Luminosity, checked against the spec formulas; /BM arrays use the first known name) + isolated/non-isolated/knockout form groups (11.4, layers allocated only when needed; backdrop removal per 11.4.8) + ExtGState soft masks (Luminosity and Alpha, /BC in the group's /CS, /TR function) **Done**. Not done: page-level /Group (page is an opaque RGB surface, no blending colour space other than sRGB), per-object knockout inside non-form content, a clip applied while a mask is active is lost when the mask is cleared before `Q` |
| 8.11 | Optional content (OCG/OCMD) | Partial | Default config only |
| 10 | Rendering intents, overprint, halftones, transfer funcs | Todo | |
| 13 | Multimedia, 3D, RichMedia, XFA, JavaScript | Out of scope | Not renderable statically |

## Editor

| Feature | Status |
|---|---|
| Page delete/reorder/rotate, merge/split by page import, Info edit, full save (`PdfEditor`, `PdfWriter`; unreachable objects dropped, encryption removed) | **Done** |
| Overlay content editing (text, shapes, images; move/resize/restyle; web UI) | Done |
| Editing existing page text in place (convert run to editable object, original operator removed, advance preserved; font becomes a standard font; page-level text only, not inside form XObjects) | Done |
| Moving/resizing existing images and vector objects | **Todo** |
| Form filling (text and checkbox/radio fields, with editor UI) | Done |
| Redaction (`Redactor.Redact`: removes intersecting text runs, paths, images and forms from the content stream, then paints an opaque box; whole-object granularity; web tool + `X` shortcut, verified end to end) | Done |
| Web editor: context menus (clipboard, arrange/z-order, align, redact), Shift/Alt drag modifiers, collapsible responsive panels | Done |
| Web editor fonts: standard 14 + installed TrueType (glyf) fonts embedded as subsets (Type0/Identity-H, ToUnicode); synthetic bold/italic when a face is missing. Text alignment (left/centre/right), underline, strikethrough, letter spacing and line height. Not yet: .ttc collections, CFF .otf | Partial |
| Web editor shell: File menu (new, open, save, save as, insert pages, export PDF/images/text, print, properties, close), find with highlights, print, full screen, page navigation, fit width/page and zoom presets, dirty tracking | Done |
| Incremental save, signing, object/xref streams on save, encrypted save | **Todo** |

## Render comparison

`tools/Pdf.RenderCompare` renders with our renderer and with the browser's built-in PDF viewer (PDFium in Chrome/Edge, via Playwright), crops the page, and reports the fraction of pixels that differ beyond a 3x3-neighbourhood tolerance, plus diff images and `report.json`.

```
dotnet run --project tools/Pdf.RenderCompare -- tests/fixtures --out compare-out [--channel msedge|chrome] [--dpi 96] [--page-tolerance 0.02]
```

Independent rasterizers differ in anti-aliasing and font hinting, so tolerances are per-fixture, not zero.

| Writer: object streams + xref streams, Flate compression, AES-256 (V5/R6) encrypted save with user/owner passwords, incremental save (PdfEditor.Save(options), SaveIncremental); verified with qpdf/pikepdf. Not yet: AES-128/RC4 writing, signatures, public-key encryption | Done |
