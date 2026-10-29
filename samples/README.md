# Sample PDFs

Twenty-six example documents that exercise as much of ISO 32000-2 (PDF 2.0) as possible. Every file is generated from raw PDF operators by a repeatable generator (`_gen/`), so the examples are valid, small (about 2 MB in total) and free of stock images. Open `01-spec-showcase.pdf` first: its tiles link to the other files.

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

## Passwords

| File | User password | Owner password |
|---|---|---|
| `17-encrypted-user-password.pdf` | `user123` | `owner-user123` |
| `18-encrypted-owner-restricted.pdf` | *(none — opens freely)* | `owner123` |

## Regenerating

```powershell
pip install reportlab pikepdf pillow numpy fonttools     # only pikepdf, pillow and numpy are needed by the current generators
samples\_gen\run.ps1            # rebuild every sample, repack 26 with PdfEditor, validate with our renderer
samples\_gen\run.ps1 -Only 20   # rebuild a subset (file-name fragment)
python samples\_gen\check.py samples\*.pdf   # structural check with pikepdf
```

Layout: `_gen/pdfkit.py` is a small raw-PDF builder (objects, content streams, CFF/TrueType subsetting, shadings, annotations, tagging); `_gen/theme.py` holds the palette and page furniture; one `gen_NN_*.py` per sample; `_gen/fonts/` holds the OFL/Bitstream fonts (see `LICENSES.txt`). Encryption is applied last with pikepdf.

`tools/AdoBye.Samples` (C#, not in the solution) validates every sample with this repo's parser/renderer and can repack files:

```powershell
dotnet run --project tools/AdoBye.Samples -c Release -- validate samples --out samples/_gen/out
dotnet run --project tools/AdoBye.Samples -c Release -- repack samples/20-report.pdf samples/26-repacked-object-streams.pdf
```

`tests/AdoBye.Tests/SamplesTests.cs` opens every `samples/*.pdf` (using the passwords above) and renders page 1 without exceptions or renderer warnings.

## Validation

* **pikepdf / qpdf**: every file opens and passes `check()` (`_gen/check.py`).
* **Our renderer**: all pages of all samples render with zero warnings.
* **Edge (PDFium) comparison** (`tools/AdoBye.RenderCompare`, 2 % tolerance): page 1 of every sample passes (under 1 % differing pixels) except the two harness-related cases below.

```powershell
dotnet run --project tools/AdoBye.RenderCompare -c Release -- samples/24-poster.pdf --out samples/_gen/cmp
```

Known differences and notes:

* `17`: the harness opens files without a password, so it was compared after decrypting with `qpdf --decrypt`/pikepdf (passes).
* `18`: Chromium's viewer garbles any document whose permissions forbid copy/modify when driven headless (reproduced with pikepdf-encrypted copies of other files, including AES-128); our renderer and a decrypted copy both match. Not a defect in the file.
* `13`: Edge page 1 is offset in the harness crop (white TrimBox edge confuses the page locator); visual inspection shows identical output.
* Our renderer draws the Symbol/ZapfDingbats standard fonts as Latin text, so the form checkbox tick is vector-drawn in `11`; and a `PatternType 2` shading used as a stroke colour (tile 11 of `06`) renders dark rather than as the gradient. Both are renderer gaps, not file defects.
* The compare harness was extended to size the viewport to the page and to stop the crop at the gap between pages, so A4/A3 and multi-page samples compare correctly.
