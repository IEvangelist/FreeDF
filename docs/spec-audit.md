# ISO 32000-2 conformance audit

Independent audit of the parser/renderer against ISO 32000-2 clauses 7-9 (clauses 10-14 only spot-checked), plus a
cross-check of the PDF/A and PDF/UA validators against veraPDF 1.30.3. This file is the working audit; it does not
replace `docs/spec-matrix.md`.

**Status vocabulary** - *implemented/tested*: code plus a deterministic test that would fail if it broke;
*implemented/untested*: code present, no test pins it (code review only); *partial*: some normative behaviour missing;
*not implemented*; *not audited*: no evidence gathered, **not** a claim of support. Rows owned by other sessions
(font fallback/CJK, OpenType shaping, image codecs, signatures, browser crypto, colour/overprint/halftones, 3D/multimedia/
XFA/JS, editor, performance) are listed only where they intersect an audited clause.

Everything below was verified against the tree at the time of writing; "tests" names are files in `tests/AdoBye.Tests`.

## 1. Validator cross-check against veraPDF

Method (reproducible, see `tools/validation/`):

1. `PDF_VALIDATION_DUMP=<dir> dotnet test --filter Validation|StructureTests` exports every validation fixture (the
   `ObjPdf.Build` hook is new; 386 files).
2. `tools/AdoBye.ValidateDump` writes our PDF/A-1b/2b/3b and PDF/UA-1 verdicts for `samples/`, `tests/fixtures/` and the
   fixtures (595 files).
3. veraPDF 1.30.3 CLI (`--format mrr`, flavours `1b 2b 3b ua1`) is run on the same files; `tools/validation/compare.py`
   joins both and writes `docs/validation-compat.md`.
4. `tools/validation/renderer_crosscheck.py` renders the new renderer fixtures with pdfium and MuPDF. Results: Type 3 `d1`
   colour-ignore, `d0` own colour, invisible (`3 Tr`) Type 3 text and `/Matte` (159 vs 158/159, 191 vs 189/190) agree with both
   engines; clip-only (`7 Tr`) Type 3 text is not painted by us and MuPDF but is by pdfium (spec: no painting, kept);
   inline `/L` agrees with MuPDF (pdfium ignores `/L` and fails the fixture).

Results (after the header rule below was added): **0 files where we are looser or stricter than veraPDF for PDF/A-1b/2b/3b**
(551/551/549 comparable files agree; before the rule: 16/28/2 looser, all `6.1.2-2`). For PDF/UA-1 we were stricter on 18 files, all from deliberately negative fixtures for three rules veraPDF has
no check for (`PDFUA-ANNOT-OBJR`, `PDFUA-ANNOT-PARENTTREE`, `PDFUA-ARTIFACT-TAGGED`); each is justified by the standard text
(ISO 32000 14.7.5.3/14.7.5.4 OBJR/ParentTree linkage, Matterhorn 01-004) and is recorded as *stricter*, not changed.
We were *looser* on 24 UA-1 files. The remaining causes:

| veraPDF rule | meaning | our status |
|---|---|---|
| PDFA 6.1.2-2 | header must be followed by a comment with 4 bytes > 127 | **fixed** (`PDFA-HEADER-BINARY`, `PDFA-HEADER-OFFSET`) |
| PDFA-1 6.2.3.3-3 / PDFA-2,3 6.2.4.3-4 | DeviceGray/RGB/CMYK used without OutputIntent or Default* | partial: explicit device-colour operators and resources are detected, the *implicit* initial DeviceGray (text/paths painted without a colour operator) is not |
| UA 7.1-3, 7.18.1-1/2, 7.18.5-2, 7.2-x | untagged content, untagged/undescribed annotations, structure containment (TR/TD/TH/LI/LBody) | partly covered by other rule IDs with stricter-than-veraPDF scope; containment rules for TR/TD/TH/LI/LBody **not implemented** |
| UA 7.18.2-1 | TrapNet annotations not permitted | not implemented |
| UA 7.5-1 | TH scope/headers determinability | partly (`PDFUA-TABLE-*`) |

Important caveat: **no fixture passes veraPDF PDF/A** (0 of 552 for 1b/2b, 0 of 271 for 3b; 42 of 551 pass UA-1). The
"conformant" PDF/A fixtures in the unit tests are conformant only with respect to the rules we implement; they are not
veraPDF-clean (typically implicit DeviceGray default colour, or missing identification/XMP details). `IsCompliant` must therefore be read as
"no errors among implemented checks" (the doc comment on `ValidationReport.IsCompliant` already says so). The false-positive
direction (we reject, veraPDF accepts) is the one that matters for users, and the corpus shows none for PDF/A.

veraPDF itself threw `StackOverflowError` on two of our fixtures (cyclic structure trees; `obj-0006`/`obj-0213` in the
3b run), which are therefore not comparable; 12 more files cannot be loaded by us (password-protected samples and the
deliberately unsupported-filter fixtures). Full per-rule table: `docs/validation-compat.md`.

## 2. Clause 7 - syntax, filters, encryption, document structure

| Area | Status | Evidence / notes |
|---|---|---|
| 7.2 lexical conventions (names with `#xx`, strings, hex strings, numbers, comments) | implemented/partly tested | `PdfLexer.cs`; `LexerKeywordTests`, `FuzzTests`. No dedicated table-driven lexer test for escapes/octal/line continuation |
| 7.3 objects, indirect refs, streams (`/Length` repair) | implemented/tested | repair path covered by `EditorRecoveryTests`, `brokenXref` fixtures |
| 7.4.2 general filters, abbreviations, chains | implemented/tested | `StreamFilterTests` (new) |
| 7.4.3 ASCIIHex / ASCII85 | implemented/tested | `StreamFilterTests` (z shortcut, partial group, whitespace) |
| 7.4.4 LZW / Flate, `EarlyChange` | implemented/tested | LZW spec vector; truncated-Flate recovery in `StreamFilterTests` |
| 7.4.4.4 predictors | PNG predictors implemented/untested (exercised by samples); **TIFF predictor was 8-bit only - fixed**, now 1/2/4/8/16 bpc, tested | `TiffPredictorTests` (new) |
| 7.4.5 RunLength | implemented/tested | `StreamFilterTests` |
| 7.4.6-7.4.10 CCITT / DCT / JBIG2 / JPX | owned by image-codec session | `CcittTests`, `Jbig2Tests`, `Jpeg*Tests`, `JpxTests` |
| 7.4.10 Crypt filter | partial: `Identity` honoured, named crypt filters resolved via the decryptor; the filter itself is a no-op pass-through | `PdfDecryptor.cs` |
| `/F` external stream data | not implemented (deprecated in 2.0) | `PdfFilters.GetFilters` ignores file-spec `/F` |
| 7.5.2-7.5.4 header, xref tables, trailers, `Prev`, hybrid `XRefStm` | implemented/tested via fixtures | samples incl. `26-repacked-object-streams`; `PdfFile.ReadXrefSection` |
| 7.5.7 object streams | implemented/tested | same fixtures; `/Extends` is not needed for resolution |
| 7.5.8 xref streams | implemented/tested | `samples/26-*` |
| 7.5.5-7.5.6 incremental updates / linearization | implemented (read-transparent) | linearization hints are ignored; editor writes incremental saves (`EditorTests`) |
| 7.5.9 xref repair | implemented/tested | `FuzzTests`, `EditorRecoveryTests` |
| 7.6 encryption: RC4 40/128, AESV2, AESV3 (R5/R6), public-key, `EncryptMetadata`, identity crypt filters | implemented/tested | `tests/fixtures/encrypted/*` (11 files), `EncryptionTests`, `SecurityTests` |
| 7.6.4 permissions | implemented, enforcement is advisory | `PdfPermissions.cs` |
| 7.7 document structure (catalog, page tree, inheritance), 7.8 content streams/resources | implemented/tested | render tests, samples |
| 7.9 common structures (dates, text strings with UTF-16/UTF-8 BOM, rectangles) | implemented/partly tested | `PdfString.Decode`; no date-parser test audited |
| 7.10 functions (types 0, 2, 3, 4) | implemented/tested | `Functions.cs`, `PostScriptFunction.cs`, `GraphicsGapTests` |
| 7.11 file specifications | implemented/tested | `FileSpecificationMetadataTests`, embedded-file samples |
| 7.12 extensions dictionary, 7.5 `/Version` override | not audited | |

## 3. Clause 8 - graphics

### 3.1 Content-stream operators (every operator of ISO 32000-2 Table 51 and friends)

All operators below are dispatched in `ContentInterpreter.Exec`. Coverage is through render-level tests (`RenderTests`,
`GraphicsGapTests`, `TransparencyTests`, `SamplesTests`) and the generated samples; there is no per-operator unit-test
matrix, so most rows are "implemented/tested at render level".

| Group | Operators | Status |
|---|---|---|
| Graphics state | `q Q cm w J j M d ri i gs` | implemented/tested; `i` (flatness) is parsed and ignored (it is a hint); `gs` supports LW LC LJ ML D CA ca BM Font SMask AIS TK plus overprint/transfer (colour session) |
| Path construction | `m l c v y h re` | implemented/tested |
| Path painting | `S s f F f* B B* b b* n` | implemented/tested |
| Clipping | `W W*` | implemented/tested |
| Text objects | `BT ET` | implemented/tested; nested `BT`, `ET` without `BT`, and text operators outside `BT` are accepted silently (no diagnostic) |
| Text state | `Tc Tw Tz TL Tf Tr Ts` | implemented/tested (`TextPerformanceRegressionTests`, samples `03`/`04`); `Tr` values > 7 are not rejected |
| Text positioning | `Td TD Tm T*` | implemented/tested |
| Text showing | `Tj TJ ' "` | implemented/tested |
| Type 3 | `d0 d1` | `d0` implemented; **`d1` was a no-op - fixed** (colour operators ignored, stroke uses fill colour); tested (`Type3AndCompatibilityTests`, new). Colour *images* inside `d1` glyphs are still painted (spec: only image masks) |
| Colour | `CS cs SC SCN sc scn G g RG rg K k` | implemented/tested (colour session owns depth); ignored inside uncoloured patterns and `d1` glyphs |
| Shading | `sh` | implemented/tested |
| Inline images | `BI ID EI` | implemented/tested; **`/L` (PDF 2.0 length) was ignored - fixed**; **`/IM false` was treated as a stencil when computing the data length - fixed** (`InlineImageTests`, new) |
| XObjects | `Do` | implemented/tested (images, forms); PostScript XObjects ignored |
| Marked content | `MP DP BMC BDC EMC` | `BMC/BDC/EMC` implemented/tested (MCID, `/OC`, Artifact); `MP/DP` accepted and ignored; unmatched `EMC` warns; an unterminated `BDC` is silently closed at end of stream |
| Compatibility | `BX EX` | **unknown operators inside BX/EX warned - fixed** (nesting counted); tested |
| Operand errors | missing/ill-typed operands | partial: missing operands default to 0, every operator runs inside a catch that records a warning (`operator X: ...Exception`), operand stack capped at 512; **no operand-arity or type validation per operator, no strict mode**; unbalanced `Q` is ignored silently |

### 3.2 Images (8.9) and forms (8.10)

| Requirement | Status | Evidence |
|---|---|---|
| `ImageMask`, `Decode`, `BitsPerComponent` 1/2/4/8/16, colour-key `/Mask`, stencil `/Mask`, `/SMask`, `/Intent`, `SMaskInData` | implemented/tested | `ImageDecoder.cs`; `GraphicsGapTests`, `IccTests`, `JpxTests` |
| SMask `/Matte` (pre-blended images, 11.6.5.3) | **was not implemented - fixed** for RGB/Gray (matte is converted through the image colour space and un-premultiplied in RGB) | `SoftMaskMatteTests` (new, pdfium/MuPDF cross-check in `renderer_crosscheck.py`) |
| `/Interpolate` | not implemented (hint) | no references in `ImageDecoder`/`ContentInterpreter` |
| Image `/Alternates`, `/OPI`, `/Measure`, `/PtData` | not implemented (informational) | |
| Image `/OC` | implemented/tested | `OptionalContentStateTests` |
| Form XObject `Matrix`, `BBox` clip, `Resources`, transparency `Group`, `/OC` | implemented/tested | `FormStructureTests`, `TransparencyTests` |
| Reference XObjects (`/Ref`), PostScript XObjects | not implemented (deprecated in 2.0) | |
| Type 3 glyph resources absent -> current resources | **was an empty dictionary - fixed** | `Type3AndCompatibilityTests` |

## 4. Clause 9 - text and fonts

| Requirement | Status | Evidence |
|---|---|---|
| Text state parameters Tc/Tw/Tz/TL/Tf/Tr/Ts and matrices Tm/Tlm, `'`/`"` side effects | implemented/tested | `ContentInterpreter.ShowOp/ShowText` |
| `TJ` adjustments, horizontal and vertical | implemented/tested | `VerticalFontTests` |
| Rendering modes 0-7 incl. clip accumulation until `ET` | implemented/tested for outline fonts; **Type 3 glyphs were painted in modes 3 and 7 - fixed** (matches pdfium's invisible-text rule) | `Type3AndCompatibilityTests` |
| Word spacing only on single-byte code 32 | implemented/untested for composite fonts | `PdfFont.IsSpace` |
| Simple fonts: Type1 (PFA/PFB/raw, MM), TrueType, CFF/Type1C, standard 14 metrics | implemented/tested | `Type1MultipleMasterTests`, `CffTests`, `tests/fixtures/fonts/*` |
| Encodings: base encodings, `/Differences`, glyph-name lookup (AGL, `uniXXXX`, `gNN`) | implemented/tested | `GlyphNameTests`, `FontCmapTests` |
| Type 3 fonts: `FontMatrix`, `CharProcs`, widths, `d0/d1` | implemented/tested (see 3.1) | |
| Composite fonts, CIDFontType0/2, `CIDToGIDMap`, `W`/`DW`/`W2`/`DW2`, predefined and embedded CMaps, `UseCMap` | implemented/tested | `FontCmapTests`, `cjk-*.pdf` fixtures, `VerticalFontTests` |
| ToUnicode extraction | implemented/tested | `StructuredTextExtractor`, `FontCmapTests` |
| Fallback fonts / CJK without embedding, OpenType shaping | owned by other sessions | |
| Text knockout `TK` | implemented/untested outside `TransparencyTests` | |

## 5. Clauses 10-14 (spot checks only)

| Clause | Status |
|---|---|
| 10 rendering (rasterizer, halftones, overprint) | owned by colour/overprint session; `SoftwareRasterizer.cs` implemented |
| 11 transparency (blend modes, groups, soft masks, `AIS`) | implemented/tested (`TransparencyTests`, 54 tests); matte added here |
| 12 interactive features: annotations, actions, forms, outlines | implemented/tested for many action types (`*ActionTests`), AcroForm (`FormStructureTests`); not audited per annotation subtype for appearance generation |
| 13 multimedia/3D | owned by 3D/multimedia session |
| 14 document interchange: marked content, structure tree, tagged PDF, artifacts, metadata, `MarkInfo` | implemented/tested (`StructureValidationTests`, `AnnotationStructureValidationTests`); TR/TD/TH/LI/LBody containment checks missing from the PDF/UA validator |

## 6. Gap list, ranked by impact

Implemented in this change (tests added):

1. TIFF predictor for 1/2/4/16-bit data (was silently returned undecoded = corrupted images).
2. Type 3 `d1` semantics; invisible/clip-only Type 3 text no longer painted; Type 3 fonts without `/Resources` use the current resources.
3. SMask `/Matte`.
4. Inline images: `/L`, `/IM false`.
5. `BX`/`EX` compatibility sections suppress unknown-operator diagnostics.
6. PDF/A header rules (6.1.2): header offset and binary comment.

Remaining, highest impact first:

1. **PDF/A implicit DeviceGray** (6.2.3.3 / 6.2.4.3): text and paths painted with the initial colour need an OutputIntent or `DefaultGray`; we only detect explicit device colour use. Without it a document can be `IsCompliant` yet fail veraPDF.
2. **Validator is not veraPDF-clean on any PDF/A fixture**: needs a checked-in positive fixture that veraPDF accepts (requires a redistributable ICC profile and an embedded font; none is in the repo) and a CI job that re-runs `tools/validation`.
3. PDF/UA structure containment rules (7.2: TR in Table/THead/TBody/TFoot, TD/TH in TR, LI in L, LBody in LI) and 7.18.2 TrapNet.
4. Per-operator operand validation / strict mode, with diagnostics for `Q` underflow, text operators outside `BT`, unterminated `BDC`.
5. `d1` colour images (only image masks allowed) and Type 3 `d0/d1` must be first operator.
6. Image `/Interpolate` (visual quality hint only).
7. `/F` external streams, Reference/PostScript XObjects (deprecated, low impact).
8. Clause 12 per-subtype appearance generation audit and clauses 10-13 line-by-line audit were not done.

## 7. Notes for the owners of `docs/spec-matrix.md` / `docs/spec-backlog.md`

Suggested edits (not applied here): 7.4 filters -> note TIFF predictor now complete and all non-image filters tested; 8.9
images -> `/Matte` done, `/Interpolate` open; 8.x operators -> `d1`, `BX/EX`, inline `/L` done; 9 text -> Type 3 modes 3/7
and resource fallback done; PDF/A -> header rule added, implicit DeviceGray still open; add the veraPDF compatibility
table (`docs/validation-compat.md`) and the caveat that no fixture is veraPDF-clean.
