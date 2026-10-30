# ISO 32000-2:2020 (PDF 2.0) coverage matrix

Status: **Done** = implemented and covered by tests, **Partial** = implemented with known gaps, **Todo** = not implemented.
Full spec parity is the goal; this table is the honest current state.
See [spec-backlog.md](spec-backlog.md) for the actionable remaining work.

## Latest reviewed integration: actions, shadings, structure and files

Three reviewed branches add typed inert action subtypes and trigger
classification, annotation and inherited form-field details (clause 12);
Coons/tensor patch evaluation, typed PostScript calculator fixes, shading
`/BBox` and `/Background`, and tiling/pattern-stroke fixes (7.10, 8.7);
and bounded `/OBJR` extraction, structure/ParentTree consistency diagnostics,
embedded-file checksum/size checks and collection/tree metadata (7.9-7.11, 14).
The clause rows below reflect these additions; **15 Partial / 0 Todo** and the
two limited Done rows are unchanged.

Action inspection adds Launch, Thread, Sound, Movie, Hide, SetOCGState,
Rendition, Trans, GoTo3DView and RichMediaExecute metadata, with typed
additional-action events and link `/PA` traversal. Annotation views retain
common/markup and subtype details; field views expose inherited metadata,
typed flags/options, signature lock/seed dictionaries and XFA presence.
Unknown/malformed entries stay raw; bounded walks report truncation. No action,
script, media, external target, XFA or signature seed is executed or enforced.
Traversal breadth, annotation rendering and complete form semantics still need
work; this is inspection support, not full clause-12 parity.

Types 6/7 now evaluate the Coons/tensor surfaces rather than approximating their
control-point geometry, with bounded device-space tessellation and hostile-input
division/tile guards. Calculator functions retain integer/real/boolean types;
shading bounds/background and negative-step/coloured/uncoloured tiling and
pattern strokes have focused regressions. Tessellation is still finite and
bounded, and general function/colour-space combinations remain open.
Reviewed PDFium comparisons were clean for the selected fixtures, not proof of
exhaustive parity. Finer axial/radial ramps and patch tessellation intentionally
change some sample colour channels by 1-6 levels; fixed-pixel references change
only for the reviewed showcase/report output and report-derived large fixture.

Structure extraction now attributes whole-object `/OBJR` annotation appearance
and local Form text in structure order, suppressing duplicates already owned by
MCRs/Artifacts. Form roots are page-scoped, selected annotation appearances stay
local, and nested discovery is bounded. Parse/consistency diagnostics cover
parents, duplicate MCID/OBJR ownership and ParentTree links; ParentTree indexing
is a single bounded pass. File inspection adds bounded name/number-tree audits
(ordering, limits, duplicates, malformed pairs and cycles), collection folders/
navigators and embedded-file size/MD5 checksum diagnostics. MD5 here is a file
checksum, not a security guarantee; external payloads remain inert. Arbitrary
owner graphs, external streams, per-invocation tagging and exhaustive attachment/
tree cases remain open. Structure diagnostics are not PDF/UA certification.

Focused integrated selectors pass 14 action/detail, 225 shading/transparency/
overprint and 114 structure/file cases (overlap; do not sum). The single full
Release suite passes **1949/1949**, with no failures or skips.
Fresh AOT publication (46 valid WASM artifacts), full/merge/page-image/boot
smokes, the 535-group CI profile and ten synthetic policy controls also pass.
All 15 first-page hashes match the reviewed references, including the generated
large fixture's independently measured report-derived first page. Forced 4x
Save overlap measures 9.7/10.6/11.2 ms with no premature swap or blank frame.
Startup and showcase open remain the two strict-goal misses; see the
[AOT measurements](performance/2026-10-30-aot.md).

## Earlier integration checkpoints

The 2026-10-30 integration adds deeper PDF/A/PDF/UA validation, opt-in overprint
and transfer functions, page-group CMYK/Cal/ICC blending with knockout support,
and >8-bit/ICC/subsampled JPX plus 12-bit arithmetic DCT. Their evidence limits
are recorded in the affected rows and backlog. Focused selectors passed
37/37 deep validation, 18/18 overprint/transfer, 134/134 transparency plus
overprint, and 148/148 JPX/JPEG-12 cases (overlapping selectors). The final
post-integration Release suite passes **1842/1842**, with no failures or skips.
A fresh AOT Pages publish produced 46 valid WASM artifacts at `/csharp-pdf/`;
trim-safe JSON, boot recovery, full editor, merge, and page-image Chromium
smokes pass. The final 535-group profile passes the versioned relative timing
and deterministic policy, including all 15/15 first-page pixel hashes; it
still reports three strict-goal misses. Overall status remains
**15 Partial / 0 Todo**, plus two Done rows with explicit limitations—not full
parity.

Earlier JPX packed-header coverage added earlier-tile-part PPT headers queued
per tile, empty first bodies, and interleaved tiles (T.800 A.7.5; packet-boundary
divisions per B.10.1). Both transformed fixtures independently match OpenJPEG
2.5.4 exactly (384 samples each). PPM remains main-header data mapped by Nppm
record to tile-part codestream order; the older same-part multipart PPT fixture
retains its source-derived-only evidence caveat. That checkpoint passed 85/85
JPX cases and 1514/1514 full Release cases.

Form-action inspection additionally exposes typed inert ResetForm `/Fields`/`/Flags` and ImportData `/F` (12.7.6.3-12.7.6.4, Tables 241-243). Raw entries and target bytes remain unchanged; field elements are not looked up, inheritance/defaults are not computed, no field is reset, and no data file is accessed or imported. Focused metadata and save-round-trip regressions pass with existing SubmitForm/document-feature tests (88/88); clause 12 remains Partial.

At the earlier seven-branch checkpoint, bounded JBIG2 Huffman refinement,
inert clause-13 metadata, Type 1 MM/Expert, DocMDP/timestamps/DSS inspection,
C# sample generation and local editor merging/header polish were integrated.
Focused Release initial selectors passed 29 JBIG2, 9 multimedia, 24 Type 1,
57 signature/security, 38 sample/reproducibility and 36 merge/generator
interaction cases (selectors overlap; do not sum them). After review fixes, 67
focused merge/3D/DocMDP/reproducible-write cases and full Release **1696/1696**
passed with zero failures/skips. That checkpoint also passed integrated WASM
publication and Chromium full-editor, merge and page-image smoke. The later
four-branch integration and current 1842-case Release result are summarized at
the top of this matrix; neither status establishes full parity.

Evidence boundaries: the JBIG2 Huffman refinement fixtures use an independent
encoder and PDFium cross-check; jbig2dec/pdf.js do not decode these fixtures.
Non-byte-aligned symbol widths and nonzero RDW/RDH refinements are not
independently verified. Inert rendition/clip traversal uses depth 64 and 1024
total nodes per top-level read with memoization and `Truncated` reporting.
Signature timestamp unit tokens are self-built (no live TSA); the pyHanko
interoperability fixtures provide independent evidence, not chain trust.
3D detail inspection has a shared 1024-work-item budget, 64 views per stream,
256 entries per node/measure/section list and per-read stream/view memoization;
`Truncated` reports incomplete typed lists while raw objects remain available.
Unsupported document-timestamp hash names are rejected, not silently mapped to
SHA-256. Fixture randomness controls the public-key seed but not the platform's
CMS-envelope randomness; public-key output is not byte-reproducible.

## Parser (`PortableDocument.Core`, `PortableDocument.Document`)

| Clause | Feature | Status | Notes |
|---|---|---|---|
| 7.2-7.3 | Lexical conventions, all object types | Done | Names `#xx`, literal/hex strings, tolerant numbers |
| 7.5 | File structure, xref tables/streams, hybrid, `Prev` chain, object streams | Done | |
| 7.5.6 | Recovery of damaged files | Done | GregsEx (PCRE2, byte mode) scans for `n g obj`; test `RepairsBrokenXref` |
| 7.4 | Filters: Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors | Done | |
| 7.4 | DCT (baseline), CCITT, JBIG2, JPX | Partial | DCT baseline/extended/progressive **Done** (Huffman; SOF1/SOF2 support 8/12-bit precision, native-precision colour then sample >> 4 for 12-bit; nearest-neighbour chroma upsampling); arithmetic SOF9/SOF10 sequential + progressive, restarts and DAC **Done**, now including 12-bit arithmetic DCT under default conditioning only (L=0, U=1, Kx=5, no DAC segment; fixtures cross-checked with libjpeg-turbo 3.2.0, maximum sample error 3; non-default 12-bit DAC is untested). Earlier arithmetic fixtures were cross-checked with Pillow. Lossless SOF3 (predictors 1-7, 2-16 bit, point transform, restarts) **Done**; remaining DCT gaps: SOF11 arithmetic lossless, hierarchical JPEG, and subsampled interleaved lossless. CCITT G3 1D/2D + G4 **Done**. JBIG2 supports generic MMR/arithmetic templates 0-3 + TPGD, refinement, symbol dictionaries including Huffman refinement/aggregation, text regions, halftone/pattern and /JBIG2Globals. Huffman single-symbol refinement and aggregate-text fixtures have exact bitmap regressions; broader combinations remain unproven. Unsupported: 12-AT extended templates and colour extensions; MMR halftone lightly tested. JPX **Done** for supported forms (JP2 boxes, codestream, tier-1/tier-2, 5/3 + 9/7 DWT, MCT, >8-bit sample precision, bilinear chroma subsampling, JP2 ICC, palette/channel definitions, alpha/SMaskInData, PPM/PPT packed packet headers, main/tile-header POC progression ranges with duplicate-packet suppression and coverage validation, and code-block style/ROI flags). OpenJPEG-referenced fixtures include main/tile RLCP POC and PPM/PPT; malformed POC length/order/coverage and packed-header boundaries are covered. Bilinear subsampling expectations use self-derived fixtures; code-block style/ROI coverage is fixture-based and does not establish exhaustive independent parity. Remaining: 12-AT/colour extensions and broader multi-range/spatial-order POC and feature combinations |
| 7.6 | Encryption (Standard RC4/AES, public-key) | Done | Standard handler R2-R6 read **and write** (RC4 40/128, AESV2, AESV3; pikepdf-verified). Public-key handler (Adobe.PubSec, adbe.pkcs7.s5, CMS EnvelopedData, AES-256 content) read/write with X509 certs - round-trip tested only, no third-party validator available. Permission flags enforced by `PdfEditor` (`PdfPermissions`) |
| 7.7 | Document structure: page tree, inheritance | Done | |
| 7.8-7.9 | Content streams, resources, functions (types 0,2,3,4) | Done | |
| 7.10-7.11 | File specs, embedded files, data structures | Partial | Reads file specifications from Names/EmbeddedFiles and associated-file arrays on catalog, AcroForm/fields, structure elements, pages, annotations and nested Form XObjects, with bounded traversal/resource paths. Exposes file-name variants, description, IDs, volatility, AFRelationship, embedded Params, raw objects and decoded bytes. `/FS`, `/Thumb`, `/EP`, recursive `/RF` and `/CI` remain inert. Collection schema/view/sort metadata now includes folders/navigators. Bounded tree diagnostics check ordering, limits, duplicates, malformed pairs and cycles; embedded-file diagnostics check decoded size and specified MD5 checksum. Encrypted metadata strings are preserved across save paths. No embedded code is executed or external payload opened. Remaining: broader tree-boundary, attachment and preservation combinations |
| 12 | Annotations, forms, outlines, actions | Partial | Outlines, named destinations, page labels, bounded action `/Next` traversal and scope-specific additional-action events, including link `/PA`. Typed inert action subtypes retain original dictionaries, malformed values and unknown entries without script/media execution, external access or navigation. Common/markup/subtype annotation details and inherited field metadata/flags/options, signature lock/seed dictionaries and XFA presence are exposed. Normal-appearance `/AP /N` and `/AS` rendering plus generated appearances remain available for supported annotations; text/button/choice filling includes multiline, comb, alignment and MK borders/backgrounds. See latest integration above and action-specific notes below. Remaining: exhaustive action-bearing objects/triggers, annotation appearances and complete form/action semantics |
| 14 | Metadata, tagged PDF, logical structure | Partial | Info, XMP, tagged flag and language; bounded StructTreeRoot/StructElem, RoleMap, ParentTree, page/Form MCIDs and `/Stm` content; Alt/ActualText/Lang and structure-ordered extraction. Form-local marked-content state is isolated/restored on errors and page-owned Do associations survive. Selected local normal-appearance MCRs remain stream-scoped with StmOwn owner checks. Whole-object OBJR now reads locally attributed annotation/Form text, including bounded nested Forms, suppressing MCR/Artifact duplication and scoping roots per page. Parse/parent/duplicate ownership/ParentTree consistency diagnostics and one-pass bounded ParentTree indexing expose truncation. Plain ExtractText/Analyze still exclude annotation text. Remaining: external streams, arbitrary owner graphs, per-invocation tagging and exhaustive malformed/reused-form semantics; not PDF/UA certification |
| 14.8/PDF/A,UA | Validation | Partial | Heuristic validators: PDF/A-1b/2b/3b now add ICC profile header/version/class/colour-space/component checks, XMP packet-header and extension-schema checks, PDF/A-1 Info/XMP value/date equivalence, font widths/encoding/CIDSystemInfo checks, `/NeedAppearances`, and named-action restrictions. PDF/UA-1/2 add document/optional-content flags, embedded-file `/F`/`/UF`, font mapping checks, MCID/Artifact/content-tagging consistency, heading/table/list/TOC/note structure rules, alongside MarkInfo, Lang, Title + DisplayDocTitle, Figure alt text and existing table-header checks. Missing TH Scope is informational for UA-1; no visual/semantic header inference or higher-header inference. Deep-rule references were compared by hand against published veraPDF profile XMLs; veraPDF was not run and this is not certification. Unconfirmed rules/heuristics remain: `PDFA-ICC-SIZE`, `PDFA-XMP-EXTENSION-INVALID`, `PDFA-FONT-CMAP-SYSTEMINFO`, heuristic `PDFUA-FONT-TOUNICODE`, advisory `PDFUA-BOOKMARKS`, and `PDFUA-LINK-STRUCT`. |
| Robustness | Fuzz/truncation tests | Done | Deterministic mutation of all fixtures (400-seed run green): only PdfException (or NotSupported for unsupported filters), timeouts enforced. Caps added: lexer nesting 200, inflate 1 GiB, raster 100M px, JBIG2/CCITT budgets |
| Perf | Hot paths | Partial | Rasterizer/JPEG already tuned earlier; added static system-font cache and bounded immutable lexer operator-token reuse. On identical base `60107c7`, already-open vector rendering was 9.4–16.1% faster across two run orders with about 15.5% fewer managed allocations; fresh-vector and text CPU gains were not reliable. Six-document decoded-byte/token hashes matched. Existing results: text.pdf 96dpi render 107→98 ms, vector.pdf 246→243 ms; load scales linearly (1000 pages ≈12 ms) |

The clause-12 action model also exposes inert typed local `/GoTo` metadata through `GoToDetails`: `/D` names, byte strings, and explicit arrays are retained without navigation, named-destination lookup, or array-element resolution. Missing and wrong-type destinations remain null in the typed view while the original dictionary is preserved; array contents are not conformance-validated.

Typed `/Named` metadata exposes `/N` through `NamedDetails`, retaining standard and unrecognised names and the original action dictionary without dispatching actions. Missing or wrong-type names are null in the typed view.

Typed `/GoToE` metadata exposes top-level `/F`, `/D`, unchanged raw `/T`, and nullable `/NewWindow` through `EmbeddedGoToDetails` (12.6.4.4, Tables 204/205). Its additive `TargetPath` view exposes `/R` names (including unknown names), `/N` byte strings, `/P` integers or byte strings, `/A` integers or text strings, and nested `/T` dictionaries, resolving only indirect values in the current PDF. Paths expose at most 64 dictionaries; `NestedTargetBoundary` reports cycles or depth limits while the original raw `/T` remains reachable through each node's `Dictionary`. Missing, malformed, and unknown entries are preserved. No conditional requiredness, attachment/tree conformance, name-tree/page/annotation lookup, destination resolution, embedded-content access, file opening, or navigation is implemented; `/SD` is not a GoToE field. Focused target-path cases in `EmbeddedGoToTargetTests` pass in the integrated Release run.

Typed inert `/GoToDp` metadata (12.6.4.5, Table 206) exposes `/Dp` through `GoToDpDetails` only when it is a local indirect reference resolving to a dictionary; `/Type` is optional, but if present must be `/DPart`. Missing `/Dp`, direct dictionary values, and wrong-type values remain inspectable in the unchanged action dictionary. This is inert inspection, not navigation or DPart-tree validation: `/DPartRoot` is not searched, and `/Start` and `/End` are not resolved. Focused cases in `GoToDpActionTests`, including omitted `/Type`, pass in the integrated Release run.

The separate `doc.GetDPartRoot()` API exposes bounded inert hierarchy metadata (14.12, Tables 408/409): original dictionaries/references, grouped depth-first children, optional types, and explicit malformed/cycle/repeat/limit boundaries. Budgets are 64 expanded dictionaries per path and 4096 global node/group slots each, preserving raw unvisited tails. `/Start`, `/End`, `/Parent`, `/DPM`, `/Metadata`, and `/AF` are inspectable without navigation or payload decoding. `DPartHierarchyTests` passes in the integrated Release run. This inspection API does not validate tree conformance; clause 14 stays Partial.

The separate additive `doc.ValidateDParts()` checks bounded parent/group links, page references/ranges, coverage/non-overlap/DFS order, and page `/DPart` back-links (14.12.2-14.12.4). Optional RecordLevel identifies an existing zero-based level; NodeNameList must have one PDF/XML Name per longest-path level (XML colons allowed), with an independent 4096-item inspection budget. Independent page-tree traversal honors budgets and reference generations; incomplete inspection cannot report `IsConformant`. Semantic record-component interpretation and payload semantics remain excluded; raw boundaries are preserved. Tests pass in the integrated Release run; clause 14 remains Partial.

JPX POC coverage additionally includes a three-layer, multi-precinct fixture that changes from one PCRL layer to the remaining CPRL layers. Its decoded pixels match Pillow's OpenJPEG-backed decode (OpenJPEG/libopenjp2 2.5.4) exactly, and tests verify both progression records. POC-plus-PPM/PPT fixtures exercise component reordering and packed segments ending at complete packet-header boundaries, with exact decoded samples. Legacy PPM/PPT references now also end segments at complete packet headers and verify exact RGB samples. The decoder retains marker-segment boundaries and rejects mid-packet-header splits per T.800 A.7.4/A.7.5; byte-position repartition regressions pass in the integrated Release run. These are bounded reference cases, not exhaustive POC coverage; multi-tile/multi-tile-part packed headers, including PPT headers preceding bodies in an earlier tile-part, remain backlog items.

## Renderer (`PortableDocument.Content`, `PortableDocument.Render`)

| Clause | Feature | Status | Notes |
|---|---|---|---|
| 8.4 | Graphics state, paths, clipping | Done | AA scanline raster, non-zero/even-odd |
| 8.4.3 | Stroking: width, caps, joins, miter, dash | Done | |
| 8.6 | Device, Cal*, Lab, Indexed, Separation, DeviceN, ICCBased | Partial | ICCBased: `IccProfile` parses matrix/TRC RGB, gray kTRC and LUT (mft1/mft2/mAB) RGB/CMYK profiles (PCS XYZ/Lab) to sRGB via Bradford D50->D65; existing CMYK SWOP checks match lcms on corners. Rendering intent from `ri`, ExtGState `/RI` and image `/Intent` propagates through current path/text, shading/mesh, image and soft-mask-backdrop conversions, including Indexed/Separation/DeviceN/Pattern wrappers; `q/Q` preserves it. Default and unknown-name fallback: RelativeColorimetric (ISO 32000-2 8.6.5.8, 8.4.2). ICC selection: A2B0 perceptual, A2B1 relative/absolute, A2B2 saturation; missing requested tables fall back to A2B0, then matrix/TRC when available. Fully adapted absolute colorimetric applies media-white scaling in XYZ (v2 display white defaults to D50). Parsed profiles are hash-cached; each intent has its own lazily baked 17-pt LUT grid with synchronized publication and allocation-free sample evaluation. Synthetic intent-table, fallback, state/override, wrapper and cache tests in `IccTests` pass in the integrated 367-case focused and 1100-case full Release runs. sRGB fast paths cannot discard differing intent transforms. Unusable or component-mismatched profiles use a valid matching non-Pattern `/Alternate` per resource context, otherwise the device space. Not done: black-point compensation and perceptual PCS/reference-black matching (the bounded LittleCMS comparison differs), B2A/D2B/B2D, named/N-colour profiles, arbitrary destination profiles, partial observer adaptation, and full transparency-group color conversion (11.7.5.3); this is not full clause-10 support. Device spaces and CalGray/CalRGB/Lab retain their existing fixed conversion/gamut-clipping behavior rather than new intent-specific gamut mapping; DeviceCMYK stays approximate. Existing CalGray/CalRGB/Lab gamma, matrix, white-point Bradford adaptation and Lab range tests match Edge within +-2 levels (`tests/fixtures/graphics/colorspaces.pdf`). PDFium renders hand-built non-sRGB ICC profiles white, so use synthetic/unit and lcms checks instead |
| 8.7 | Tiling and shading patterns | Partial | Types 1-7 for supported forms, including Gouraud triangles, type-5 bit alignment, edge continuation, actual Coons/tensor surface evaluation and bounded device-space tessellation. Inputs interpolate before per-sample Function evaluation; malformed/unsupported/wrong-arity mesh functions warn and skip. Typed PostScript calculator semantics, finer axial/radial ramps, shading BBox/background and tiling/pattern-stroke fixes have regressions and selected PDFium comparisons. Remaining: finite bounded tessellation, broader function-engine/colour-space combinations and independent fixture coverage; not exhaustive parity |
| 8.9-8.10 | Images, inline images, XObject forms | Partial | See filters above; stencil + colour-key + SMask alpha |
| 9 | Fonts: TrueType, Type0/CID, Type3, ToUnicode | Partial | Embedded CFF (Type1C, CIDFontType0C, OpenType-CFF) outlines via `CffFont` (Type 2 charstrings, subrs, flex, FDSelect, endchar-seac accents, arithmetic/storage operators, full 391-entry standard string table and StandardEncoding generated from fontTools). Legacy Type 1 (`FontFile`, PFB segments / PFA hex / raw binary) via `Type1Font`: eexec + charstring decryption (lenIV), built-in `/Encoding` including named Adobe ExpertEncoding, `/FontMatrix`, Subrs, hsbw/sbw, flex/hint replacement, div, seac, and Multiple Master WeightVector/Blend metadata and supported blend othersubrs. Up to 16 masters and four axes are bounded; regular-grid design-coordinate interpolation is supported, not arbitrary font PostScript execution. All 59 PDF-predefined CMaps (Adobe-Japan1/GB1/CNS1/Korea1 incl. UniJIS/UniGB/UniCNS/UniKS UCS2+UTF16, RKSJ, EUC, GBK/GBK2K, Big5/ETen/HKscs, KSC/UHC, `-V` variants) are embedded as a compact deflated resource (`predefined-cmaps.bin`, ~320 KB) generated by `tools/cmapgen/gen_cmaps.py` from adobe-type-tools/cmap-resources (BSD-3-Clause, commit f5cf3bc); embedded CMap streams (codespaces, cidrange/cidchar, usecmap) are parsed too. Adobe Glyph List 2.9's 4,281 mappings and 201 Zapf Dingbats mappings are embedded; glyph names support suffixes, underscore sequences, and strict `uni`/`u` Unicode forms. `/W2` and `/DW2` are applied for WMode 1 text origins, advances, and geometry. Supported embedded OpenType glyf/CFF fonts apply script-selected GSUB `vrt2` or `vert` single substitutions to vertical glyphs. Unsupported lookup types/flags are warned and skipped; unsupported FeatureVariations are warned and default vertical features are used, while malformed GSUB data keeps the original glyph. This is not full OpenType shaping. Non-embedded CJK fonts fall back to an installed system CJK font (CID to Unicode inverted from the Uni*-UTF16 CMaps, half-width Latin via legacy CMaps); if none is installed nothing is drawn but text extraction still works. Not done: bundled CJK glyph data, arbitrary MM PostScript design procedures/irregular master layouts, OpenType font-name based CJK fallback beyond a fixed candidate list, Type 1 hinting (intentionally ignored), and complete OpenType shaping |
| 11 | Transparency: alpha, blend modes | Partial | Alpha + all 16 blend modes (separable and Hue/Saturation/Color/Luminosity, checked against the spec formulas; /BM arrays use the first known name) + isolated/non-isolated/knockout form groups (11.4, layers allocated only when needed; backdrop removal per 11.4.8) + ExtGState soft masks (Luminosity and Alpha, /BC in the group's /CS, /TR function) **Done**. Page-group blending now supports DeviceCMYK, calibrated and ICC blend spaces; same-operator fill+stroke and text knockout, plus `/AIS`, have bounded rasterizer support. Final page output remains isolated over opaque white; page elements rendered with `asPageElement: true` honor page-group `/I`, CropBox clipping, and ignore inherited `/CS` for non-isolated groups. Analytic tests pass, but the new output has not been compared with Chrome/PDFium. The CMYK non-separable K rule follows Adobe/Splash behavior rather than explicit ISO text; text-knockout work is clipped to the group bounds, not tight glyph bounds. RGB and tested blend modes match prior references; DeviceGray retains known conversion differences from Edge. Remaining: arbitrary backdrop/transparency output and per-object knockout outside Form XObjects. Clips applied while a soft mask is active now survive clearing the mask |
| 8.11 | Optional content (OCG/OCMD) | Partial | Evaluates `/OCMD /VE` Boolean visibility expressions (nested `/And`, `/Or`, `/Not`) before legacy `/P`/`/OCGs` policies; malformed expressions hide their content with a warning. `RenderPage` can select named catalog `/Configs`; `/Unchanged` inherits default-config state. Applies `/AS` `/View`, `/Print`, and `/Export` states plus `/Zoom` bounds. `OptionalContentState` exposes `/RBGroups` and `/Locked`, applies exclusive radio-group selection, and rejects visibility changes that violate locks without changing state; changes are in-memory and actions/scripts are not executed. Explicit per-render Language and User context now selects `/AS` OCGs using Language exact/preferred-primary fallback and User `/Ind`/`/Ttl`/`/Org` matching; context is caller-supplied, with no host-setting reads. Language validation is structural only (not full BCP 47 registry/canonicalization), CreatorInfo/PageElement policy is not inferred, and web UI/save-back for OCG state remains unsupported |
| 10 | Rendering intents, overprint, halftones, transfer funcs | Partial | Rendering-intent state/ICC selection and fully adapted media-white conversion are covered under 8.6.5.8. Supported ExtGState `/TR` and `/TR2` transfer functions are applied to eligible fully opaque paints; opt-in `PdfRenderOptions.OverprintSimulation` handles `/OP`, `/op`, and `/OPM` using an RGB approximation and is off by default. Analytic tests exist, but some expectations are self-consistent and there is no independent print/raster comparison. Halftone, flatness, and smoothness entries remain inert (not simulated). Remaining: exact general overprint/colorant behavior, halftone behavior, transfer-function combinations, black-point compensation and perceptual PCS/CMM matching; selected tests do not establish full clause-10 parity |
| 13 | Multimedia, 3D, RichMedia, XFA, JavaScript | Partial | XFA packets and JavaScript payloads remain opaque, never executed. RichMedia assets/configurations/activation/presentation retain typed metadata and raw dictionaries without activation. Additional inert media readers expose rendition/selector/clip graphs, criteria, players, play/screen parameters, offsets, durations, Movie and Sound dictionaries; rendition/clip traversal is memoized and depth/node bounded with observable truncation. 3D readers expose U3D/PRC stream/reference metadata, activation/animation, projection/background/render/lighting, nodes/cross sections and measurement subtype, preserving raw model streams and `/OnInstantiate` without decoding or execution. Remaining: complete dictionary semantics, model/measurement interpretation and broader malformed/preservation coverage. Playback, host-software matching, external file access and script execution are not performed |

Additional clause-9 coverage: CFF1 predefined ISOAdobe/Expert/ExpertSubset charsets and Standard/Adobe Expert built-in encodings (Adobe TN 5176 Appendices B/C, fontTools 4.63.0 tables). For embedded non-CID CFF, absent PDF Encoding/BaseEncoding uses the built-in encoding, Differences overlays it, and ToUnicode wins for extraction. Explicit PDF MacExpertEncoding is supported as a distinct glyph-name table (ISO 32000-2 Annex D.4), not Adobe ExpertEncoding; holes map to .notdef, Differences override and ToUnicode takes precedence. Legacy Type 1 named ExpertEncoding and bounded MM blends are now supported as described in the row above. CFF, annotation/AP, DPart hierarchy and JPX boundary regressions passed 583 focused / 1160 full Release cases before this extension; these are historical, not current integration counts.

Additional bounded clause-8.6 coverage: `/UseBlackPtComp` state is propagated, with Default treated as Off and absolute intent disabling BPC. Supported relative XYZ matrix/TRC RGB/gray v2/v4 input/display ICC profiles use a prepared white-preserving affine XYZ map; `SupportsBlackPointCompensation` exposes the strict supported subset. LUT/CMYK/Lab, general black estimation and perceptual PCS/reference-black handling remain incomplete; this does not resolve the LittleCMS perceptual discrepancy above. All ICC plus annotation/validator selectors passed 184/184 focused Release cases, followed by the expanded run below.

Additional bounded PDF/UA annotation checks verify `/StructParent` -> ParentTree -> direct `/OBJR` with indirect `/Obj` identity, preserving missing-key and Link Contents/Alt behavior. ParentTree values must be raw indirect references to current in-use xref entries with matching generations (zero for compressed objects); the focused `AnnotationStructureValidationTests` selector passes 48/48. Existing parser/10k-per-page limits and subtype exemptions remain; validators are not certification.

The expanded BPC/UA/DPart/root-field/MacExpert batch passes 833/833 combined focused and 1393/1393 full Release cases. The Release solution build succeeds with four warnings (EditorWeb CS8602, TransparencyTests CS0675, FontCmapTests xUnit2029, SecurityTests xUnit2013). Clause statuses remain 15 Partial / 0 Todo; full parity is not established.

Subsequent additions pass the expanded Release run below and are not covered by those earlier totals: inert `SubmitFormDetails` exposes local file-specification/Fields/Flags/CharSet values and raw dictionaries without submission, field lookup, defaults or inheritance interpretation (12.7.6.2). New JPX fixtures cover two tiles/two tile-parts each with packed headers preceding bodies in the same tile-part. Marker ordering and complete-header placement were reviewed against T.800 A.7.4/A.7.5. The transformed PPM stream has independent OpenJPEG evidence; PPT expected samples derive from the decoded original source, since OpenJPEG 2.5.4 rejects the transformed multipart PPT. No decoder code changes accompany these fixtures; earlier-tile-part headers and broader combinations remain gaps.

The whole-object OBJR annotation-text gap in clause 14 now has bounded local selected-appearance support, covered by the expanded Release run. OBJR page selection and annotation StructParent/ParentTree ownership gate direct normal-appearance text; artifacts are excluded and repeated references deduplicate per annotation/page. ActualText/opt-in Alt retain precedence. Tagged appearance streams are not categorically excluded: explicit stream/MCID MCR text stays on its existing path to avoid duplication, and remaining appearance text can contribute through OBJR. Conservative MCR reservation on invalid page/owner links, nested Forms, arbitrary whole-object XObjects and ambiguous overlap remain limitations. No annotation Contents inference, appearance synthesis or action execution is added, and plain extraction remains annotation-free.

The integrated SubmitForm/JPX/OBJR/JBIG2/Pages batch passes 965/965 combined focused and 1471/1471 full Release cases (zero failures/skips), including hand-derived MMR halftone pattern/grid coverage and existing Form-child HTTP editing regressions migrated into shared EditorSession. The incremental Release solution build succeeds with zero warnings/errors; the preceding focused compile emits five existing warnings documented in the backlog. Browser-only parallelism guards and optional null-preserving font fallback are retained. The integrated WASM project builds; published Chromium evidence is from the Pages child's pre-merge branch, not a rerun here. Clause statuses remain 15 Partial / 0 Todo; no full-parity claim.

The later 12-bit Huffman SOF1/SOF2 DCT addition is covered by native-precision reference fixtures and PDF image-pipeline regressions; arithmetic 12-bit remains unsupported. JPEG/editor focus passes 120/120. After undo-safe asset retention and deterministic ten-minute unclaimed-upload expiry/cancellation tests, editor/session/HTTP focus passes 18/18 and the full Release suite passes 1502/1502. Incremental solution build succeeds with zero warnings/errors. Integrated published WASM Chromium smoke now passes image insert/delete/undo and the existing local edit/export/mobile/no-network scenarios; this supersedes the earlier pre-merge-only smoke boundary. Scope and residuals remain documented; counts stay 15 Partial / 0 Todo.

## Editor

| Feature | Status |
|---|---|
| Page delete/reorder/rotate, merge/split by page import, Info edit, full save (`PdfEditor`, `PdfWriter`; unreachable objects dropped, encryption removed unless requested) | **Done** |
| Overlay content editing (text, shapes, images; move/resize/restyle; web UI) | Done |
| Editing existing page text in place (convert run to editable object, original operator removed, advance preserved). Text inside non-shared form XObjects is listed (`TextSpan.Form`) and editable/removable via `SetStreamOverride` on the form stream. Forms drawn more than once are NOT exposed for editing (edits would be ambiguous). The original embedded font is reused when the font/bold/italic is unchanged and the font can encode every character (`PdfFont.TryEncode`); otherwise a catalog/standard font is used | Done |
| Moving/resizing existing images and vector objects | Partial; supports page-level objects and direct painted children inside uniquely invoked indirect Form XObjects. Shared, repeated, hidden, nested, or otherwise ambiguous Form children remain withheld; graphic-group transforms remain available. Complex vector paint/clip ranges remain unsupported. |
| Form filling (text and checkbox/radio fields, with editor UI) | Done |
| Redaction (`Redactor.Apply`): glyph-level removal of text under the rect (page and form XObject text, advances preserved for the rest), partially covered images are decoded and the covered region blanked (re-encoded Flate), forms recursed into (shared forms are over-redacted for every use), annotations whose /Rect intersects are removed, XMP/Info metadata scrubbed, opaque box painted. Page-content single positive-size rectangle fills and clip paths are clipped when their CTM is axis-aligned; unsupported vector paints remain removed whole, and unsupported clip geometry fails closed to an empty clip. Inline images and image masks that intersect are removed whole. AcroForm /Fields entries whose widgets were removed are pruned. Outline titles are transformed only for actually removed contiguous glyph phrases, using canonical Unicode decomposition and whitespace-run equivalence while preserving surrounding original text. Punctuation remains literal; no compatibility/confusable/zero-width folding is done, phrases are not joined across operators or surviving glyphs, and matching depends on font Unicode extraction (whole-span fallback when glyph pieces are absent). Redaction disables incremental saves so callers must use a full rewrite. Still limited: arbitrary paths, non-axis-aligned transforms, and shared-form over-redaction |
| Web editor: context menus (clipboard, arrange/z-order, align, redact), Shift/Alt drag modifiers, collapsible responsive panels | Done |
| Web editor fonts: standard 14 + installed fonts embedded as subsets: glyf TrueType (Type0/Identity-H, CIDFontType2), CFF-flavoured OpenType (.otf; name-keyed CFF subset and embedded as FontFile3 /CIDFontType0C, CID-keyed CFF embedded whole), .ttc/.otc collections (face extracted to a standalone sfnt), ToUnicode on all. Characters missing from the chosen font are split into runs using a fallback font (Segoe UI, Arial, symbol/CJK families, then any installed face). Synthetic bold/italic when a face is missing. Alignment, underline, strikethrough, letter spacing, line height. TrueType subsets truncate loca/hmtx to the highest used glyph id. Extra font folders via env `PDF_EDITOR_FONT_DIRS`. Verified end to end (`tools/AdoBye.EditorWeb/e2e_fonts.py` against a live server: original-font edit, CFF .otf, ttc Cambria Math, CJK fallback): pikepdf/qpdf decode clean, pdfminer.six and pypdf extract every inserted string, own extractor round-trips, and Edge (`AdoBye.RenderCompare --channel msedge`) renders the edited files within tolerance (1.5% and 0.2% differing pixels). xunit coverage in `EditorFontTests`. CID-keyed CFF and CJK subsets remain large (whole table / high-gid prefix) | Done |
| Web editor shell: brand, adjacent File menu, document name/tooltip and saved/unsaved status; one-time Sponsor link; File > Merge PDFs with local multi-selection/drop, ordering, page ranges and insertion point; existing save/export/find/print/navigation/zoom/recovery | Done for these UI operations; merge is atomic and carries forms, outlines and page labels, not every catalog-level structure. Encrypted inputs unsupported by WASM are explicitly flagged, never silently cached decrypted |
| Incremental save, object/xref streams on save, encrypted save | **Done** |
| 12.8 Digital signatures: detached SHA-256 approval/certification signing, DocMDP P=1/2/3 conservative structural change checks, encrypted incremental signing, RFC 3161 document timestamps and DSS reading | **Done** for the bounded APIs and pyHanko interoperability fixtures. Not full signature conformance: timestamp acquisition is caller-supplied; chain trust/revocation policy, full PAdES LTV validation and general DSS authoring remain unsupported. Unclassifiable DocMDP changes are violations/undetermined, not proof of permission; `Sign` may rewrite sources when incremental prerequisites are absent |

## Samples

`samples/` holds 26 committed PDFs authored by `tools/AdoBye.Samples` in C# using this repository's lexer/writer/editor APIs. Python sample authoring has been removed; `_gen/fonts` retains licensed font assets. `GeneratedSamplesTests` requires byte-identical regeneration, including deterministic fixture-only encryption, and checks a generated-on-demand 27th collection containing all 50 source pages. The full collection preserves selected navigation/forms/layers/attachments/tag structures, is not fully tagged, and deliberately contains the public encrypted samples' pages unencrypted. Independent pikepdf/PDFium comparison remains validation tooling, not authoring. See [samples/README.md](../samples/README.md) for evidence and visual/size caveats.

## Render comparison

`tools/AdoBye.RenderCompare` renders with our renderer and with the browser's built-in PDF viewer (PDFium in Chrome/Edge, via Playwright), crops the page, and reports the fraction of pixels that differ beyond a 3x3-neighbourhood tolerance, plus diff images and `report.json`.

```
dotnet run --project tools/AdoBye.RenderCompare -- tests/fixtures --out compare-out [--channel msedge|chrome] [--dpi 96] [--page-tolerance 0.02]
```

Independent rasterizers differ in anti-aliasing and font hinting, so tolerances are per-fixture, not zero.

| Writer: object streams + xref streams, Flate compression, encrypted save (AES-256, AES-128, RC4 128/40, public-key), incremental save, signing; verified with qpdf/pikepdf/pyHanko | Done |
