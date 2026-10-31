# Third-party notices

Entries are grouped per component; each section is self-contained.

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

The browser editor's "View source on GitHub" links use the `mark-github` glyph from [Octicons](https://github.com/primer/octicons), inlined as an SVG symbol in `tools/AdoBye.EditorWeb/wwwroot/index.html`.

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
