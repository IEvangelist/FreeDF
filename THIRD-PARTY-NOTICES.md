# Third-party notices

Entries are grouped per component; each section is self-contained.

## Fallback fonts and CJK glyph data (PortableDocument.FallbackFonts, PortableDocument.Content)

Full texts live in `src/PortableDocument.FallbackFonts/fallback-fonts/licenses/` and are described by `licenses/manifest.json`.

* **Liberation Fonts 2.1.5** - SIL Open Font License 1.1. Digitized data copyright (c) 2010 Google Corporation with Reserved Font Arimo, Tinos and Cousine; copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation. (`Liberation-OFL-1.1.txt`, `Liberation-AUTHORS.txt`)
* **DejaVu Sans 2.37** - Bitstream Vera licence; DejaVu changes are public domain. Copyright (c) 2003 Bitstream, Inc. (`DejaVu-LICENSE.txt`, `DejaVu-AUTHORS.txt`)
* **Noto Sans JP/SC/TC/KR** (google/fonts commit `6c1dbe27dcf7d817d4b62cec033ba77fa8e2a5ec`) - SIL Open Font License 1.1. Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'. Distributed only as re-encoded, subset glyph outlines (`cjk/**`). (`NotoSansCJK-OFL-1.1.txt`)
* **Adobe Core 14 AFM metrics** - Copyright (c) 1985, 1987, 1989, 1990, 1997 Adobe Systems Incorporated. All Rights Reserved. Glyph names, encodings and advance widths only, in `PortableDocument.Content` (`std14-widths.bin`). The permission paragraph is embedded in that assembly (`std14-notice.txt`), at the head of the data table, and in `Adobe-Core14-AFM-NOTICE.txt`.
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
