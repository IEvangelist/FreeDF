# Colour conversion and print-rendering notes

Scope: on-screen (sRGB) output. Print-only behaviour is either simulated opt-in or documented as ignored.

## DeviceCMYK to sRGB (ISO 32000-2 8.6.4.4, 10.3.5)

ISO 32000-2 gives `R = 1 - min(1, C + K)` only as a device-dependent fallback; viewers substitute a press characterisation.
`CmykConversion` uses a 9x9x9x9 grid (nodes 0, 32, ... 224, 255 per ink) with multilinear interpolation, embedded as
`Resources/cmyk-srgb-9x9x9x9.bin` (19,683 bytes, `c,m,y,k` row-major, RGB triples). Allocation-free; trimming/AOT/browser safe.

* **Why:** the previous pdf.js polynomial rendered pure yellow as (255, 235, 61) where Acrobat/PDFium give (255, 241, 1)
  (625-colour swatch grid vs PDFium: mean max-channel delta 9.7, max 60, 48% of colours off by more than 8 levels).
* **Provenance and licence:** values were measured by rendering the 6,561 grid colours with PDFium (via `pypdfium2`; PDFium is BSD-3-Clause,
  the same engine as Chrome and the reference of `tools/AdoBye.RenderCompare`). PDFium's table is in `core/fxge/dib/cfx_cmyk_to_srgb.cpp`
  ("Original code copyright 2014 Foxit Software Inc."), under PDFium's BSD-3-Clause licence; the notice ships in `THIRD-PARTY-NOTICES.md`.
  PDFium calls the conversion `AdobeCMYK_to_sRGB` and its values look like Adobe SWOP swatches, but upstream states no data source or other licence;
  that is an unverified residual risk and the `.bin` can be swapped for an independently licensed characterisation. No ICC profile is redistributed.
* **Error against PDFium** on 3,000 random off-grid colours: mean 0.75, p95 2, p99 4, max 9 levels, 0.3% above 4 levels. The
  remaining difference is PDFium's axis-wise interpolation vs multilinear.
* **Regeneration / re-check:** `python tools/make_cmyk_table.py --out src/PortableDocument.Content/Resources/cmyk-srgb-9x9x9x9.bin` rebuilds the table
  from PDFium and prints the off-grid error; `--check <bin>` re-runs the validation (exit 1 above `--max-error`, default 12 levels).

### Impact of replacing the polynomial (a47207c)

Every DeviceCMYK conversion changes (fills, image samples, Separation/DeviceN alternates); differences against the old polynomial reach 60 levels
(pure yellow: blue channel 61 -> 1). Measured on `samples/*.pdf`, first six pages at 96 dpi, before vs after (max-channel delta):

| Page | Changed pixels | > 8 levels | > 32 levels | Max |
|---|---|---|---|---|
| 08-colour-spaces p0 | 6.07% | 2.44% | 0.08% | 58 |
| 09-images p0 | 2.48% | 1.55% | 0.00% | 23 |

All other sample pages (including 21-brochure, 24-poster, 01, 03, 20) are byte-identical. The full unit suite (2,033 tests) passed without re-baselining:
no test encodes a polynomial-specific CMYK value. Editor pixel gate (`docs/performance/editor-ci-known-misses.v1.json`, SHA-256 of the 339x480 page-0 RGBA):
only `09-images.pdf` changed (`33D9F80F...` -> `5A3B5223...`, re-baselined; the pre-change native hash reproduced the stored value exactly);
01/03/20 and the 12-page fixture (same first page as 20) are unchanged. This used the native renderer, which is the engine the editor runs; the WASM/browser
profile run itself was not repeated.

Accuracy against Chrome (Edge/PDFium via `AdoBye.RenderCompare`, 96 dpi, bad-pixel fraction / mean abs, before -> after):
08-colour-spaces 0.31% / 4.93 -> 0.26% / 4.60; 09-images 0.26% / 6.30 -> 0.26% / 6.26; 21-brochure and 24-poster unchanged (0.11%, 0.02%).
The residual difference is dominated by anti-aliasing and text, not colour.
* Tests: `ColorPrintEdgeTests.DeviceCmyk_*` (14 PDFium reference colours, monotonicity, clamping/NaN).
* Not implemented: black-point-aware or intent-dependent DeviceCMYK, `DefaultCMYK` overrides apply as described below.

## Colour-space edge cases (8.6.5, 8.6.6)

* **Indexed:** `hival` is clamped to 0-255 (negative/oversized values no longer throw); indices beyond a short lookup string read zero components;
  Lab bases use the Lab default decode. Palettes of 256 entries render per entry.
* **Lab:** non-finite or non-positive `WhitePoint` falls back to the default white; non-finite `Range` is ignored; an inverted range is ordered.
* **CalGray/CalRGB:** invalid white point falls back to Device spaces; non-positive/non-finite `Gamma` is treated as 1; non-finite `Matrix` uses identity.
  `BlackPoint` is not applied (no black-point scaling is mandated for screen output).
* **Separation/DeviceN:** a tint transform that returns fewer values than the alternate needs is zero-padded instead of throwing.
  `DeviceN /Attributes` (`Subtype` DeviceN/NChannel, `Process`, `Colorants`, mixing-hint keys) are parsed and exposed as
  `PdfColorSpace.Attributes`. They are hints (8.6.6.5); on-screen conversion always uses the tint transform and the alternate space,
  and mixing hints (solidity, printing order, dot gain) are not simulated.

## Default colour spaces (8.6.5.6)

`/DefaultGray`, `/DefaultRGB` and `/DefaultCMYK` in the resource `/ColorSpace` dictionary now remap a Device space selected by `g/G/rg/RG/k/K`, by `cs`/`CS` with a Device name, or as an
image or shading `/ColorSpace` (`PdfColorSpace.ParseWithDefaults`, `DefaultColorSpaceTests`). Entries with a different component count, Pattern, Indexed, Separation/DeviceN or unparsable values are ignored.
Device spaces nested in another space (Indexed base, Separation alternate, ICC alternate) are not remapped. Previously the entries were honoured only for page-group blending spaces and
never for painting. Not compared against PDFium/MuPDF output; the initial graphics-state colour is not remapped.

## Transparency group blending spaces (11.4.7, 11.7.5.3)

* Page group: DeviceGray/RGB/CMYK, CalGray, CalRGB, ICCBased (N=1/3/4) blend spaces (existing).
* **New:** isolated nested form groups pass their `/CS` to the backend, so blend functions run in the group's space
  (for example a CMYK group using Multiply). Unsupported `/CS` (DeviceN, Lab, Separation, unparsable ICC) keeps the inherited space.
  Non-isolated groups inherit the backdrop space, as required.
* Soft-mask `/BC` is converted through the mask group's `/CS` (existing).
* Limits: layers remain sRGB; only blend functions and non-separable operators evaluate in the group space, so Normal compositing
  of partial alpha in a non-linear/CMYK space is approximate. ICC blend spaces select the model by component count (profile curves/primaries
  are not inverted); CMYK blending uses the 10.3.5 complement with maximal black generation, not the screen CMYK table above.
  DeviceN/Lab group blending and colour conversion at group boundaries between differing spaces are not implemented.

## Overprint (8.6.7, 11.7.4.3) - opt-in, `PdfRenderOptions.OverprintSimulation`

Unchanged semantic: DeviceCMYK / process-only Separation/DeviceN with OPM 0/1 and spot colorants (multiplicative) on an RGB surface.
Off by default; ignored for non-Normal blend modes, patterns and non-CMYK colour spaces (DeviceGray/RGB/CIE, ICCBased).
Limits: backdrop CMYK is recovered with the naive complement model; spot colorants are simulated as multiply of their alternate;
no separations, no `OPM` for ICCBased CMYK, no Acrobat "black overprint" gray-as-K rule. Not a substitute for Acrobat Overprint Preview.

## Halftones, transfer functions, BG/UCR, flatness, smoothness (10)

* **Decision:** `HT`/`HTP`/`HTO` (all halftone types 1/5/6/10/16, named defaults), `BG`/`BG2`, `UCR`/`UCR2`, `FL` and `SM` are device-dependent
  print parameters and are intentionally **inert** for sRGB screen output. Tests assert pixel equality with and without them and that no warning is raised.
* `TR`/`TR2` apply to fully opaque fills, strokes, shadings, image samples (including Indexed and stencil-mask ink) and are
  never applied to soft-mask alpha or partially transparent objects (11.7.5.2). PDFium/Chrome ignore transfer functions entirely, so
  Chrome comparisons of TR files are expected to differ.

## ICC (8.6.5.5)

PDF restricts `ICCBased` profiles to input/display/output/colour-space classes with N = 1, 3 or 4. DeviceLink, abstract and named-colour
(`ncl2`) profiles are rejected and fall back to `/Alternate` or the Device space by design; that is the specified behaviour, not a gap.
Perceptual/saturation LUT handling, v4 reference black, BPC and rendering intents are covered by `IccTests`/`IccCmmTests` (within two 8-bit
levels of LittleCMS 2.17). ICC.1:2010 `D2Bx`/`B2Dx` multiProcessElements (`mpet`: `cvst` with `curf` segments of `parf` types 0-2 and middle `samf` segments, `matf`, float `clut`) are read, preferred over `A2Bx`/`B2Ax`, and fall back to them when an element is unsupported (`bACS`/`eACS`, non-finite or out-of-bounds data). `IccMultiProcessTests` match Little CMS 2.17 (Pillow, NOOPTIMIZE) within two levels on two hand-assembled profiles (`tools/make_mpet_fixtures.py`; self-derived, not vendor profiles). Still open: v4 gamut tags, partial observer adaptation (not required by PDF) and a general gamut-mapping CMM.

## CalGray/CalRGB/Lab vs PDFium

Constant fills rendered here and by PDFium (pypdfium2, 72 dpi): CalRGB and Lab with a D65 white point agree within 2 levels (`CalLabPdfiumReferenceTests`).
Documented differences, kept because they follow the spec: PDFium does not adapt non-D65 white points (a D50 CalRGB green is 81,255,0 in PDFium vs 0,255,0 here
after Bradford adaptation; Lab with a D50 white differs by up to 12 levels) and keeps CalGray close to identity (0.1 -> 26 vs 19 here; 0.25 -> 64 vs 61) rather than applying gamma then the sRGB curve.
MuPDF was not compared.
