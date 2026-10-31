# Sample PDFs

Twenty-six committed example documents (plus a generated 27th) that exercise as much of ISO 32000-2 (PDF 2.0) as possible. Every file is generated from raw PDF operators by this repo's own C# tooling (`tools/AdoBye.Samples`), so the examples are valid, small (about 2.4 MB in total for 01-26) and free of stock images. Open `01-spec-showcase.pdf` first: its tiles link to the other files, or generate `27-full-collection.pdf` for all of them in one file.

Design system: Source Serif 4 (headings), Source Sans 3 (text), Source Code Pro (code), all embedded as OpenType/CFF subsets; an indigo / teal / amber / rose palette on a 54 pt margin grid.

## Index

| File | What it is | Features demonstrated |
|---|---|---|
| `01-spec-showcase.pdf` | Cover and index of the suite | Radial/axial shadings, transparency, vector icons, link annotations with `GoToR` actions to the sibling files |
| `02-standard-14-fonts.pdf` | Specimen of the standard 14 fonts | Non-embedded Base-14 fonts, WinAnsi encoding, character sets, sizes |
| `03-typography-embedded.pdf` | Typography lab (2 pages) | Embedded OpenType/CFF subsets (Identity-H, ToUnicode), GPOS pair kerning on/off, left/centre/right/justified text, columns, drop cap, `TJ` arrays |
| `04-text-render-modes.pdf` | Text render modes | `Tr` 0–7: fill, stroke, fill+stroke, invisible and the clip modes (text as a clipping path for a shading and a checker pattern), char/word spacing, rise |
| `05-vector-graphics.pdf` | Vector graphics (2 pages) | Paths, cubic Béziers, line caps/joins/miter limit, dash patterns, non-zero vs even-odd fill, clipping, generative art |
| `06-patterns-shadings.pdf` | Patterns and shadings | Tiling patterns (coloured and uncoloured), shading types 1 (function), 2 (axial), 3 (radial), 4–7 (free-form, lattice, Coons, tensor meshes), shading patterns |
| `07-transparency.pdf` | Transparency | `CA`/`ca` opacity, all 16 blend modes, luminosity and alpha soft masks, isolated/knockout transparency groups |
| `08-colour-spaces.pdf` | Colour spaces | DeviceGray/RGB/CMYK, CalRGB/CalGray, Lab, ICCBased, Indexed, Separation (spot), DeviceN, overprint-free tints |
| `09-images.pdf` | Image gallery | DCT (JPEG), Flate with PNG predictors, `/Mask` colour key and stencil masks, `/SMask`, `/Decode`, Indexed images, inline images (`BI/ID/EI`), interpolation |
| `10-annotations.pdf` | Annotations (2 pages) | Link, text note, highlight/underline/strike-out/squiggly, stamp, free text, ink, square/circle, with appearance streams |
| `11-acroform.pdf` | Fillable form | AcroForm text, multi-line, comb, password, checkbox, radio group, combo, list box, push buttons, `NeedAppearances`, default appearance, calculation-free layout |
| `12-navigation.pdf` | Navigation (7 pages) | Nested, coloured, collapsed outlines; named destinations name tree; page labels (roman, decimal, prefixed); GoTo links; `PageMode UseOutlines` |
| `13-page-boxes-rotation.pdf` | Page geometry (4 pages) | MediaBox, CropBox, BleedBox, TrimBox, ArtBox, `/Rotate` 0/90/180/270 |
| `14-embedded-files.pdf` | Attachments | `EmbeddedFiles` name tree, file attachment annotations, associated files (`/AF`, PDF 2.0), `/Params` (size, dates, checksum) |
| `15-optional-content.pdf` | Layers | OCGs, default on/off state, radio-button groups, usage/`AS` auto-state, OCMD membership policies, layer-dependent drawing |
| `16-truetype-specimen.pdf` | TrueType specimen | Embedded TrueType as composite `CIDFontType2` and as a simple `/TrueType` WinAnsi subset, `CIDToGIDMap`, `/W` widths |
| `17-encrypted-user-password.pdf` | Encrypted: password required | AES-256 (V5, R6), `/UE` `/OE` `/Perms` |
| `18-encrypted-owner-restricted.pdf` | Encrypted: opens freely, restricted | AES-256 with empty user password; print low-res only, no copy/modify/annotate (permission bits) |
| `19-tagged-accessible.pdf` | Tagged accessible document (2 pages) | `/MarkInfo`, `/StructTreeRoot`, role map, `ParentTree`, marked content with MCIDs, artifacts, `/Lang`, `/Title` with `DisplayDocTitle`, figure alt text, table structure (Table/TR/TH/TD) |
| `20-report.pdf` | Multi-page report (6 pages) | Cover, linked contents, drop cap and columns, pull quote, table, bar and line/area charts drawn as vectors, outlines |
| `21-brochure.pdf` | Tri-fold brochure | US Letter landscape outside/inside spreads, vector illustration, clipping, wave shapes, TrimBox/BleedBox |
| `22-invoice.pdf` | Invoice | Tabular layout, right-aligned numerals, totals block, baseline grid |
| `23-resume.pdf` | Résumé | Two-column layout, sidebar, timeline, skill bars |
| `24-poster.pdf` | A3 poster | Large display type, layered shadings, translucent rings, blend modes (`Screen`), outlined text |
| `25-dashboard-charts.pdf` | KPI dashboard (A4 landscape) | Bar, smooth line/area (Catmull-Rom Béziers + clipped gradient), donut (arc paths) and sparklines — all path operators |
| `26-repacked-object-streams.pdf` | Report rewritten by this repo's `PdfEditor`/`PdfWriter` | Compressed object streams and cross-reference streams (PDF 1.5+ file structure) |
| `27-full-collection.pdf` | *Generated on demand, not committed:* all of the above in one file (50 pages) | Page import/merge, rebuilt outline and page labels, retargeted links, form + layers + attachments + named destinations + structure tree carried over (see below) |

## Passwords

| File | User password | Owner password |
|---|---|---|
| `17-encrypted-user-password.pdf` | `user123` | `owner-user123` |
| `18-encrypted-owner-restricted.pdf` | *(none — opens freely)* | `owner123` |

**Try them in the browser editor.** Open `17` and the editor asks for the password (use `user123`; `owner-user123` opens it with owner rights, which unlocks "Remove protection" under Save as). Open `18` and it opens immediately but print, copy, modify and annotate actions are refused with a message; supply `owner123` through `PdfDocument.Load(bytes, "owner123")` or the server editor to lift that. Save as on a protected document writes an AES-256 (R6) copy with the passwords and permission checkboxes you choose. Passwords are used once and never stored, and encrypted documents never write recovery copies. See [docs/browser-encryption.md](../docs/browser-encryption.md).

## The full collection

`27-full-collection.pdf` is every other sample in one file (all 26, 50 pages, about 2.3 MB). It is **generated on demand and not committed** (it is a pure function of 01-26, and `.gitignore` excludes it); `tools/Validate-Samples.ps1` or `AdoBye.Samples generate samples 27-` creates it. It is built by `tools/AdoBye.Samples` through this repo's `PdfEditor.ImportPages`/`ImportValue` (real merged pages, no screenshots), never includes itself, and is always generated last.

* **Navigation:** one collapsed bookmark per sample (`NN  Title`) that lands on the sample's first page and nests that sample's own bookmarks (`12` and `20`/`26` have them); page labels read `NN · label` (sample `12` keeps its roman / decimal / `A-` labels under the prefix); `PageMode UseOutlines`.
* **Links:** the `GoToR` tiles of `01` are rewritten to plain `GoTo` destinations so they jump inside the merged file.
* **Structures kept:** the AcroForm of `11`, the layers of `15`, the attachments and associated files of `14`, the named destinations of `12` and the structure tree of `19` are carried over (each belongs to exactly one source; the generator fails if two sources define the same one).
* **Not kept:** per-sample viewer preferences, outline colours/styles, and the document-level tagging of the other samples (only `19` is tagged, so the merged file is only partly tagged).
* **Encrypted samples (deliberate choice):** `17` and `18` are opened with the passwords listed above and their pages are merged in the clear, so the full file is **not encrypted** and carries neither the passwords nor the `18` permission restrictions. They are public sample fixtures with published passwords; keep using `17` and `18` themselves to test decryption.

## Regenerating

Everything is authored in C# with this repo's own library: the generators in `tools/AdoBye.Samples/Gen` use a small raw-object kit (`Kit/`) whose object bodies are parsed by `PdfLexer` and serialized (and AES-256 encrypted) by `PdfWriter`, so each sample can use any feature without a high-level library getting in the way. Output is reproducible: `SamplesTests`/`GeneratedSamplesTests` regenerate everything and require byte-for-byte equality with the committed files, including the encrypted ones (`PdfEncryption.RandomBytes` is fed a fixed-seed generator and `PdfWriteOptions.FileId` is fixed; constant randomness is for fixtures only and must never be used to protect real documents).

```powershell
dotnet build tools/AdoBye.Samples -c Release
dotnet tools/AdoBye.Samples/bin/Release/net10.0/AdoBye.Samples.dll generate samples          # all 27
dotnet tools/AdoBye.Samples/bin/Release/net10.0/AdoBye.Samples.dll generate samples 12-nav   # a subset (file-name fragment)
dotnet tools/AdoBye.Samples/bin/Release/net10.0/AdoBye.Samples.dll validate samples --out compare-out/samples
```

Layout: `Kit/Doc.cs` (document, pages, content streams, form XObjects, resources), `Kit/Fonts.cs` + `Kit/FontFile.cs` (standard 14 metrics and OpenType/TrueType reading, kerning and glyph subsetting), `Kit/Raster.cs` (procedural images, baseline JPEG encoder, sRGB profile), `Kit/Theme.cs` (palette and page furniture), one `Gen/GenNN.cs` per sample (`Gen26` repacks `20` with `PdfEditor`, `Gen27` is the merge). `_gen/fonts/` holds the OFL/Bitstream fonts (see `LICENSES.txt`; also used by the browser editor). Sample-suite authoring no longer requires Python; independent test-fixture generators elsewhere are separate from this migration.

Tests: `SamplesTests` opens every committed sample (with the passwords above) and renders page 1 without warnings. `GeneratedSamplesTests` regenerates all samples into a temp folder and checks that 01-26 equal the committed files byte-for-byte, and that 27 has exactly the pages of the others, bookmarks and labels per sample, 17/18 merged with their text, retargeted and named-destination links, and the same form fields, layers, attachments, structure tree and nested bookmarks as the sources.

Integrated validation on 2026-10-30 passes 38 focused sample/reproducibility
cases and, after integration review fixes, the 1696-case full Release suite. The editor's separate local
**File > Merge PDFs** flow also passes Chromium save/render/reopen with these
regenerated samples. These integration runs do not repeat or extend the
independent comparison evidence below.

## Validation

* **pikepdf / pdfium** (independent validators, `tools/validate_samples.py check samples`): every file opens, re-saves cleanly and renders. `tools/validate_samples.py compare <old> <new>` diffs page counts, structure and pixels between two sample sets (used once to compare the C# port with the former Python output: all 26 matched in page count, boxes and structure counts; page pixels differed by at most 0.11% except `09`, whose procedural photo/plasma textures are not reproduced pixel-for-pixel).
  Run everything with `tools/Validate-Samples.ps1` (needs `pip install pikepdf pypdfium2 numpy`).
* **Our renderer**: all pages of all samples render with zero warnings.
* **Size**: the regenerated files are 12-25% larger than the Python ones (2.4 MB vs 2.05 MB for 01-26). The growth is entirely in the embedded font subsets (about 2.7 KB more per font: our CFF subsetter inlines subroutines instead of re-optimising them); content streams are the same size.
* **Edge (PDFium) comparison** (`tools/AdoBye.RenderCompare`, 2 % tolerance): page 1 of every sample passes (under 1 % differing pixels) except the two harness-related cases below.

```powershell
dotnet run --project tools/AdoBye.RenderCompare -c Release -- samples/24-poster.pdf --out compare-out
```

Known differences and notes:

* `17`: the harness opens files without a password, so it was compared after decrypting with `qpdf --decrypt`/pikepdf (passes).
* `18`: Chromium's viewer garbles any document whose permissions forbid copy/modify when driven headless (reproduced with pikepdf-encrypted copies of other files, including AES-128); our renderer and a decrypted copy both match. Not a defect in the file.
* `13`: Edge page 1 is offset in the harness crop (white TrimBox edge confuses the page locator); visual inspection shows identical output.
* Our renderer draws the Symbol/ZapfDingbats standard fonts as Latin text, so the form checkbox tick is vector-drawn in `11`; and a `PatternType 2` shading used as a stroke colour (tile 11 of `06`) renders dark rather than as the gradient. Both are renderer gaps, not file defects.
* The compare harness was extended to size the viewport to the page and to stop the crop at the gap between pages, so A4/A3 and multi-page samples compare correctly.
