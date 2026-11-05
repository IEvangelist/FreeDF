# Third-party notices

Entries are grouped per component; each section is self-contained.

## veraPDF validation-profile facts

The factual relationships in `ValidationPdfUA2Nesting.cs` were derived from the
veraPDF Consortium's [v1.30.3 validation profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-2-ISO32005.xml),
published under [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).
The relationships were reorganized into parent-keyed sets and independently
implemented in C#; the profile XML and validation-engine source are not bundled.
Unconditional exclusions, direct-content bans, and unconditional occurrence
limits are implemented here; grouping-dependent requirements remain deferred.

The PDF/A image-XObject interpolation prohibition in `ValidationPdfA.cs` was
also cross-checked against the veraPDF Consortium's
[v1.30.3 PDF/A profiles](https://github.com/veraPDF/veraPDF-validation-profiles/tree/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_A),
under the same CC BY 4.0 licence (PDF/A-1b 6.2.4-3, PDF/A-2b/3b 6.2.8-3).
It is independently implemented in C#; inline-image coverage remains deferred.
The same profiles were used to cross-check device-colour replacement conditions
(PDF/A-1b 6.2.3.3 and PDF/A-2b/3b 6.2.4.3). Resource-scoped calibrated/ICCBased
replacement detection is independently implemented in `ValidationContext.cs`.
The same clause facts inform painted RGB/CMYK OutputIntent matching and Gray
acceptance of any PDF/A OutputIntent, independently implemented in
`ValidationContext.cs` and `ValidationPdfA.cs`.
The image Alternates/OPI and BPC/mask conditions (image tests 1/2/4/5), and Form
OPI/PS/Subtype2 bans (19005-1 6.2.4/6.2.5; 19005-2/3 6.2.9), were likewise
cross-checked against those CC BY 4.0 profiles and independently implemented.

The forbidden ToUnicode destination values in `ValidationPdfUADeep.cs`
were cross-checked against the veraPDF Consortium's
[v1.30.3 PDF/UA-1 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-1.xml)
(7.21.7-2) and
[PDF/UA-2 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-2.xml)
(8.4.5.8-2), under the same CC BY 4.0 licence. The bounded literal CMap
scanner and UTF-16BE destination inspection are independently implemented;
unused mappings are also checked, unlike the reference's used-glyph scope.

The PDF/UA-1 supplied subset CharSet/CIDSet requirements were cross-checked
against the same CC BY 4.0 PDF/UA-1 profile, 7.21.4.2 tests 1/2.
`ValidationPdfUADeep.cs` reuses independently implemented PDF/A listing
inspection in `ValidationPdfADeep.cs`; PDF/UA-2 is not given these rules.
The retained maxp-derived glyph-slot policy and its sparse-font reference
disagreement are documented in the specification backlog.

The explicit Identity CFF CID 0 (.notdef) check was cross-checked against
the same CC BY 4.0 PDF/UA-1 profile 7.21.8-1 and PDF/UA-2 profile 8.4.5.9-1.
Content-state collection in `ValidationContext.cs` and scoped rejection
in `ValidationPdfUADeep.cs` are independently implemented; other glyph
lookup surfaces remain incomplete.

The rendered ASCII TrueType width check was cross-checked against the
same CC BY 4.0 PDF/UA-1 profile 7.21.5-1 and PDF/UA-2 profile 8.4.5.6-1,
including the mode-3 exemption and one-unit tolerance. The bounded
format-4 cmap/hmtx inspection in `ValidationPdfA.cs`, content-state
collection and scoped PDF/UA comparison are independently implemented.
No reference-engine source or external font program is bundled.

The rendering-use scope of PDF/UA font embedding was cross-checked against
the same CC BY 4.0 UA-1 profile 7.21.4.1-1 and UA-2 profile 8.4.5.5.1-1.
State collection and the unused/mode-3-only exemption in
`ValidationContext.cs` and `ValidationPdfUA.cs` are independently implemented.
Observed mixed visible/invisible use remains subject to embedding, unlike
four explicitly documented reference controls. No profile XML or
reference-engine source is bundled.

The UA-2 field-widget additional-action Contents requirement was
cross-checked against the same CC BY 4.0 UA-2 profile, 8.10.2.3-2.
`ValidationPdfUA2.cs` independently implements this scoped entry-presence
condition with namespace-aware Artifact exclusion. The separate
label-or-Contents predicate remains incomplete; no model implementation
or profile XML is bundled.

The UA-1 complete Scope-presence check for tables without TD Headers was
cross-checked against the same CC BY 4.0 PDF/UA-1 profile, 7.5 tests 1/2.
`ValidationPdfUA.cs` independently implements this necessary presence
condition; complete directional/semantic header association is not claimed.

The conditional optional-content configuration Name requirement and AS
prohibition were cross-checked against the same CC BY 4.0 PDF/UA-2 profile,
8.7 tests 1/2. Configuration selection, decoded-name emptiness and null-entry
handling in `ValidationPdfUADeep.cs` are independently implemented.

The string-valued language-tag syntax checks were cross-checked against
the same CC BY 4.0 PDF/UA-1 profile 7.2-29 and PDF/UA-2 profile 8.4.4-2.
Page/invoked-Form Span-property inspection and shared grammar validation
in `ValidationPdfUA.cs` and `ValidationPdfUADeep.cs` are independently
implemented; no profile XML or reference-engine source is bundled.

The language-string private-use exclusion was cross-checked against the
same CC BY 4.0 PDF/UA-2 profile, 8.6-1. Decoded scalar inspection in
`ValidationPdfUA2.cs` and its catalog/structure/Span-language integration
are independently implemented; broader human-readable string classification
is not claimed.

The UA-1 missing/null encryption P check was cross-checked against the
same CC BY 4.0 PDF/UA-1 profile, 7.16-1, and independently against the
required-entry fact in ISO 32000-1:2008 Table 21. Presence checking in
`ValidationPdfUA.cs` is independently implemented; authentication and
complete permission-dictionary validation are not changed.

The UA-1 non-empty embedded-file UF requirement was cross-checked
against the same CC BY 4.0 PDF/UA-1 profile, 7.11-1. Decoded UF emptiness
and resolved-null EF classification in `ValidationPdfUADeep.cs` are
independently implemented using ISO 32000-1 text-string/null semantics;
F encoding and indirect-null reference disagreements are retained in
the specification backlog rather than represented as full agreement.

The UA-1 dynamicRender requirement was cross-checked against the same
CC BY 4.0 PDF/UA-1 profile, 7.15-1, and the publicly indexed PDF
Association [Matterhorn Protocol 1.1](https://pdfa.org/wp-content/uploads/2021/04/Matterhorn-Protocol-1-1.pdf),
checkpoint 25-001 (copyright 2021 PDF Association, CC BY 4.0).
Bounded namespace-aware configuration inspection in `ValidationPdfUADeep.cs`
independently reuses the repository's inert XML parser; no source XML,
schema or protocol PDF is bundled. Namespace/reference limits and source
retrieval limitations are documented in the specification backlog.

## OpenJPEG 2.5.4 (test-only reference fixtures)

`tests/fixtures/jpxopj` and `tests/fixtures/jpxmatrix` contain codestreams and
reference samples generated with the official OpenJPEG 2.5.4 Windows tools.
Their manifests record tool hashes and fixture provenance; no OpenJPEG source
or executable is shipped in the libraries or editor. The complete BSD-2-Clause
copyright notice, conditions and disclaimer for these fixtures are retained in
[OpenJPEG test-reference notices](docs/third-party-notices-openjpeg.fragment.md).

## Fallback fonts and CJK glyph data (PortableDocument.FallbackFonts, PortableDocument.Content)

Full texts live in `src/PortableDocument.FallbackFonts/fallback-fonts/licenses/` and are described by `licenses/manifest.json`.

* **Liberation Fonts 2.1.5** - SIL Open Font License 1.1. Digitized data copyright (c) 2010 Google Corporation with Reserved Font Arimo, Tinos and Cousine; copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation. (`Liberation-OFL-1.1.txt`, `Liberation-AUTHORS.txt`)
* **DejaVu Sans 2.37** - Bitstream Vera licence; DejaVu changes are public domain. Copyright (c) 2003 Bitstream, Inc. (`DejaVu-LICENSE.txt`, `DejaVu-AUTHORS.txt`)
* **Noto Sans JP/SC/TC/KR** (google/fonts commit `6c1dbe27dcf7d817d4b62cec033ba77fa8e2a5ec`) - SIL Open Font License 1.1. Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'. Distributed only as re-encoded, subset glyph outlines (`cjk/**`). (`NotoSansCJK-OFL-1.1.txt`)
* **Adobe Core 14 AFM metrics** - Copyright (c) Adobe Systems Incorporated. All Rights Reserved; years per AFM file: Helvetica 1985, 1987, 1989, 1990, 1997; Times 1985, 1987, 1989, 1990, 1993, 1997; Courier 1989-1993, 1997 (Bold variants omit 1992); Symbol 1985, 1987, 1989, 1990, 1997; ZapfDingbats 1985, 1987, 1988, 1989, 1997. The permission paragraph was verified verbatim against the published Core 14 AFM `MustRead` file and the widths against the 14 AFMs. Glyph names, encodings and advance widths only, in `PortableDocument.Content` (`std14-widths.bin`). The permission paragraph is embedded in that assembly (`std14-notice.txt`), at the head of the data table, and in `Adobe-Core14-AFM-NOTICE.txt`.
## PDFium (DeviceCMYK to sRGB table)

`src/PortableDocument.Content/Resources/cmyk-srgb-9x9x9x9.bin` (embedded in `PortableDocument.Content.dll`) holds DeviceCMYK to sRGB
values measured from PDFium's output at 6,561 grid colours (`tools/make_cmyk_table.py`). PDFium's table
(`core/fxge/dib/cfx_cmyk_to_srgb.cpp`, "Original code copyright 2014 Foxit Software Inc.") is released under PDFium's BSD-3-Clause licence;
the values are redistributed here under that licence.

Provenance caveat: PDFium names the conversion `AdobeCMYK_to_sRGB` and its values match Adobe-style SWOP swatches (for example
C=100% gives (0,174,239)), but PDFium's source does not state an upstream data source or any licence other than its own. No ICC profile
or Adobe-supplied file is included here. If that provenance is unacceptable, replace the `.bin` with a table generated from an independently
licensed characterisation; the rest of the pipeline is unaffected.

```
Copyright 2014 The PDFium Authors

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * Neither the name of Google Inc. nor the names of its
contributors may be used to endorse or promote products derived from
this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Octicons (GitHub mark icon)

The browser editor's "View source on GitHub" links use the `mark-github` glyph from [Octicons](https://github.com/primer/octicons), inlined as an SVG symbol in `tools/FreeDF.EditorWeb/wwwroot/index.html`.

Licence: MIT - <https://github.com/primer/octicons/blob/main/LICENSE>

```
MIT License

Copyright (c) 2023 GitHub Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
## Shaping, bidi and line breaking (text shaping integration)

Unicode Character Database 16.0.0 (generated range tables in `src/PortableDocument.Content/Shaping/UnicodeTables.cs`, and the BidiTest/BidiCharacterTest conformance files in `tests/fixtures/ucd`):
Copyright (c) 1991-2025 Unicode, Inc., used under the Unicode License v3. Full text: `tests/fixtures/ucd/UNICODE-LICENSE.txt`.

Test fonts in `tests/fixtures/shaping` (SIL Open Font License 1.1; used only by tests, not shipped in the library):
- Source Sans 3 subset (renamed "Shaping Test Sans" because of the Reserved Font Name "Source"): Copyright 2023 Adobe. Licence and copyright: `tests/fixtures/shaping/OFL-SourceSans3.txt`.
- Noto Sans Arabic, Hebrew and Devanagari subsets: Copyright The Noto Project Authors. Licence and copyrights: `tests/fixtures/shaping/OFL-Noto.txt`.

HarfBuzz (via the `uharfbuzz` Python package, MIT) was used only to generate the reference glyph sequences in `tests/fixtures/shaping/hb-expected.json`; no HarfBuzz code is included.


## veraPDF and veraPDF-corpus (test oracle only)

veraPDF (GPLv3 / MPLv2+) and the veraPDF-corpus are run locally as independent PDF/A and PDF/UA oracles, including by `tools/ua_crosscheck.py`. No veraPDF binaries, profiles, corpus files or validation-engine source are included in this repository or its packages; profile-derived facts are attributed above.
