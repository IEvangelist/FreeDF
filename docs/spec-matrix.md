# ISO 32000-2:2020 (PDF 2.0) coverage matrix

Status: **Done** = implemented and covered by tests, **Partial** = implemented with known gaps, **Todo** = not implemented.
Full spec parity is the goal; this table is the honest current state.
See [spec-backlog.md](spec-backlog.md) for the actionable remaining work.

The [2026-11-03 full PDF/UA corpus remeasurement](spec-backlog.md#2026-11-03-full-pdfua-corpus-remeasurement)
compares all 297 UA-1 and 138 UA-2 files with veraPDF 1.30.3: 14 UA-1 misses
and 9 UA-2 misses, zero false positives/exclusions. Explicit standalone and
combined UA-2 XML profiles give the same file verdicts; `-f ua2` selects the
combined profile in this runtime. Eight rejected role-mapping cases explicitly
have incomplete nesting checks. Unmapped predicates and limited Table 5 corpus
coverage remain; these figures are not full conformance or certification.

The [2026-11-04 embedded CIDFont entry correction](spec-backlog.md#2026-11-04-pdfua-embedded-cidfont-mapping-entries)
subsequently closes four missed invalid `/CIDToGIDMap` cases. Fresh FreeDF
results against the same retained reference reports give 12 UA-1 and 7 UA-2
misses, zero false positives/exclusions, with exactly those four verdicts
changed. This is entry-type validation, not complete font/glyph conformance.

The [named encoding CMap reference check](spec-backlog.md#2026-11-04-pdfua-named-encoding-cmap-references)
closes two more public misses with no positive changes: 11 UA-1 / 6 UA-2
misses, zero false positives/exclusions. This covers named dictionary parents,
not program-level `usecmap`, stream parents or complete CMap validity.

The [UA-1 terminal-field description correction](spec-backlog.md#2026-11-04-pdfua-1-terminal-field-descriptions)
then closes the widget-only-tooltip miss: 10 UA-1 / 6 UA-2 remaining misses,
zero false positives/exclusions. Actual field tooltips and each widget's
structure Alt are distinguished; UA-2 rows are unchanged.

The [symbolic TrueType cmap-record check](spec-backlog.md#2026-11-04-pdfua-symbolic-truetype-cmap-records)
adds profile-specific `/FontFile2` encoding-record requirements and explicit
incomplete-header errors. Ten mutated real-font controls agree with the exact
reference rules; all 435 corpus verdicts remain unchanged (10/6 misses).
Subtable mappings, glyph programs, non-symbolic use and FontFile3 remain open.

The [literal TrueType Differences-name check](spec-backlog.md#2026-11-04-pdfua-truetype-differences-glyph-names)
closes the two character-encoding misses: 9 UA-1 / 5 UA-2 remaining misses,
zero false positives/exclusions. The shared official AGL data now resides in
Core; extraction's algorithmic-name support is unchanged. Twenty exact-rule
controls agree; glyph lookup and embedded-program requirements remain partial.

The [literal CMap WMode comparison](spec-backlog.md#2026-11-04-pdfua-literal-cmap-writing-modes)
replaces regex matching with bounded token inspection, defaults and explicit
incompleteness. Twenty-eight exact-rule reference controls agree; all 435
corpus verdicts remain unchanged. General PostScript/inheritance is not evaluated.

PDF/A image/Form checks also reject prohibited Alternates/OPI/PS entries and
profile-invalid image/mask bit depths. Fifty-one independent controls close
22 reference-reported misses; agreement is 48/51, with three explicitly recorded
standalone-stencil disagreements where FreeDF enforces the base standard's
one-bit constraint. Null-key controls remain accepted. See
[dictionary-policy evidence and limits](spec-backlog.md#pdfa-imageform-dictionary-policies);
inline parsing and general image validity remain open.

The [standalone PostScript XObject check](spec-backlog.md#2026-11-04-pdfa-standalone-postscript-xobjects)
now rejects typed `/Subtype /PS` streams across PDF/A-1/2/3, including unused
objects. Nine painted negatives and three image positives agree with veraPDF;
three unused-object controls explicitly disagree (FreeDF rejects, veraPDF
accepts). This is a file-wide prohibition, not complete XObject well-formedness.

The [annotation visibility flag correction](spec-backlog.md#2026-11-04-pdfa-annotation-visibility-flags)
adds Invisible and part-specific ToggleNoView rejection and corrects Popup's
overbroad exemption. Non-Popup `/F` remains mandatory in every part;
PDF/A-2/3 Popup may omit it but supplied flags must be valid. All 108
independent controls agree on exact rules and verdicts. General annotation
validity remains partial.

The [external-stream null correction](spec-backlog.md#2026-11-04-pdfa-external-stream-null-entries)
accepts null `/F`, `/FFilter` and `/FDecodeParms` while retaining the
non-null prohibition and exact part-specific clauses. All 36 independent
controls agree on exact rules and verdicts. General stream/reference
validity remains outside this check.

The [Hide/GoToE action policy correction](spec-backlog.md#2026-11-04-pdfa-hidegotoe-action-policy)
rejects Hide in all parts and GoToE in PDF/A-1, and prevents explicit-null
Type from bypassing the known-action scan. All 36 independent controls
agree on exact rules and verdicts. Unknown/deprecated action kinds and
complete context/target validation remain open.

The [PDF/A-2/3 embedded CIDFont map check](spec-backlog.md#2026-11-04-pdfa-23-embedded-cidfont-mapping-entries)
requires a stream or `/Identity`, including invisible text. All 52
real-font controls agree on the exact predicate, but remain non-PDF/A
whole files and do not close measured corpus verdicts. PDF/A-1's distinct
rendering-mode requirement and full map/glyph validity remain open.

The [PDF/A-2/3 literal Differences correction](spec-backlog.md#2026-11-04-pdfa-23-literal-truetype-differences)
accepts AGL names instead of banning every array, reusing bounded PDF/UA
inspection. PDF/A-1's ban and base-encoding requirements remain. Thirty
exact-rule real-font controls agree; all 435 public UA verdicts and finding
ID sets are unchanged. Embedded (3,1) cmap and glyph-use requirements remain
open; this is not complete font conformance.

The [PDF/A symbolic cmap record check](spec-backlog.md#2026-11-04-pdfa-symbolic-truetype-cmap-records)
requires one encoding in PDF/A-1 or one/Symbol (3,0) in PDF/A-2/3,
reusing the unchanged bounded PDF/UA scanner. Fifteen real-font controls
agree on exact predicates; malformed/unsupported headers produce explicit
incomplete Errors. Subtable, glyph, non-symbolic and FontFile3 completeness
remain open.

The [PDF/A annotation subtype vocabulary](spec-backlog.md#2026-11-04-pdfa-annotation-subtype-vocabulary)
now applies explicit part-specific allowed types, including Screen/3D
prohibitions in PDF/A-2/3 and PDF 1.4-only types in PDF/A-1. All 198
independent controls agree on exact rules and verdicts (120 positives,
78 negatives). Other annotation dictionary/appearance requirements remain
partial.

The [PDF/A-2/3 appearance presence check](spec-backlog.md#2026-11-04-pdfa-23-annotation-appearance-presence)
requires AP except for Link, Popup or rectangles with both dimensions zero.
All 225 independent controls agree on exact rules and verdicts (201 positives,
24 negatives). PDF/A-1 presence is unchanged. The separate
[normal-only appearance dictionary check](spec-backlog.md#2026-11-04-pdfa-normal-only-appearance-dictionary-keys)
applies to all three parts, ignoring null-valued extra keys while rejecting
empty/rollover/down/custom active keys. All 66 independent controls agree
on its exact rule and whole-file verdict (18 positives, 48 negatives).
The [non-Widget N stream check](spec-backlog.md#2026-11-04-pdfa-non-widget-normal-appearance-streams)
now rejects scalar/array/dictionary normal values. All 144 non-Widget
control verdicts agree; 135 exact stream predicates agree and nine
indirect-null diagnostic disagreements are retained explicitly.
The [inherited Widget type check](spec-backlog.md#2026-11-04-pdfa-inherited-widget-normal-appearances)
requires non-button streams or button dictionaries with at least one stream,
with explicit incomplete Errors for exhausted parent/state inspection.
Across 600 controls, 558 appearance-rule sets and 588 verdicts agree;
42 indirect-null rule differences include 12 FT-inheritance verdict
disagreements, all retained with ISO null/inheritance provenance.
All-state/AS/V validity, field-tree membership, Form/content validity and
general annotation well-formedness remain open.

The [PDF/A-1 annotation opacity check](spec-backlog.md#2026-11-04-pdfa-1-annotation-opacity)
rejects optional numeric CA other than 1 without imposing that restriction
on parts 2/3. All 192 controls agree on the exact rule and verdict
(172 positives, 20 negatives); general CA type/range validity remains open.

Exceeding the existing 10000-entry per-page PDF/A annotation budget now
produces an [explicit incomplete Error](spec-backlog.md#2026-11-04-explicit-pdfa-annotation-traversal-incompleteness),
rather than silently omitting later entries. The limit is a library resource
bound, not a normative annotation-count restriction.

PDF/A-1b/2b/3b now reject explicit true image-XObject `/Interpolate` flags.
This is not full image-policy coverage: inline `/I` and malformed/unresolved
flags remain deferred. Six targeted veraPDF 1.30.3 image-rule comparisons agree,
and the separate baseline-reproduced public-positive OutputIntent rejection is
now corrected by honoring CalRGB/ICCBased DefaultRGB and ICCBased DefaultCMYK.
Twelve matching-profile RGB controls agree with veraPDF (six positives/six
negatives); CMYK controls are authored, not an independent comparison.
Lab/DeviceN replacements and exhaustive colour-use/completeness remain open;
see [the scoped evidence and remaining disagreements](spec-backlog.md#pdfa-image-xobject-interpolation-policy).
Historical corpus totals are unchanged.

Painted DeviceRGB/CMYK now require matching OutputIntent profiles when no
supported default replaces them; DeviceGray accepts any PDF/A OutputIntent,
correcting the earlier Gray-only restriction. Eighteen authored controls with
real public ICC data agree with veraPDF 1.30.3 (twelve positives/six exact-rule
negatives, zero exclusions). Unused resources do not create the new mismatch
finding. Shading/pattern/inline usage and complete colour-use analysis remain
open; see [the scoped OutputIntent evidence](spec-backlog.md#pdfa-painted-device-colour-outputintents).

The editor-pipeline/current-main integration retains **15 Partial / 0 Todo**
and the limited redaction/signature Done rows. Binary transport, cached
selection metadata and reviewed pooled rendering improve bounded paths, but
do not establish full clause coverage or the requested 5x performance targets.
The [performance checkpoint](performance/2026-10-30-aot.md#current-main-pipeline-and-pool-integration)
records the exact remaining timing misses and withdrawn first-paint experiment.

## Latest reviewed integration: five parity branches

The first three reviewed branches add typed inert action subtypes and trigger
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
[AOT measurements](performance/2026-10-30-aot.md). These are the first-three
checkpoint results, before the ICC and redaction additions below.

The final two branches add ICC v4 perceptual reference-black mapping,
LittleCMS-compatible LUT black-point compensation, optional B2A/mBA destination
conversion and a standalone ncl2 named-colour reader. Perceptual/saturation
conversion to sRGB implicitly compensates LUT black even with Default/Off,
matching LittleCMS; relative intent obeys UseBlackPtComp, absolute disables it.
This compatibility choice is not an ISO/PDF mandate. Black estimation follows
`cmsDetectBlackPoint` (and destination round-trip fitting), **not ISO 18619**.
Corrupt B2A tables disable destination use without discarding valid A2B
rendering. Bounded synthetic RGB/CMYK references match Pillow 12.1.1 /
LittleCMS 2.17 within two 8-bit levels. Unsupported D2B/B2D elements, device-link/abstract/N-colour
transforms, partial observer adaptation, general CMM/gamut mapping and
exhaustive transparency-group conversion remain open; ncl2 inspection does
not make named profiles valid PDF ICCBased spaces.

Redaction now subtracts arbitrary filled/stroked paths and clips under
non-axis-aligned transforms, with curves flattened to a 0.05 page-space-unit
bound before polygon subtraction. Unsupported/singular/over-budget geometry
is removed whole (an unsupported clip becomes empty). Forms are copied per
edited invocation, including nested/shared forms; partial inline images and
stencil masks are re-encoded with covered pixel footprints blanked. Hidden
optional content is included. Replaced original resources are dropped unless
an untouched use still needs them, and redacted Form copies drop Metadata,
PieceInfo and LastModified. Full rewrite remains required; conservative
whole-object fallback, finite geometry budgets and glyph/outline-text
extraction limitations still apply. The content-only overload removes
intersecting forms/images whole; per-invocation editing requires `Apply`.

Integrated ICC selectors pass **182/182**, redaction **66/66** and the one
final full Release suite **2002/2002** (zero failures/skips).
Final fresh AOT/full/merge/page-image/boot smokes, the 535-group regression
gate, 15/15 fixed hashes and ten synthetic policy controls pass. Sample 08 and
the graphics colour-space fixture also retain exact before/after hashes at
41/137/240 dpi. Forced 4x Save overlap measures 11.2/11.9/12.6 ms with no
premature swap or blank. Three strict-goal misses remain: startup 71/72 ms,
showcase open 281.2/301.1 ms, and reopened cached typography navigation
103.1/103.1 ms (p50/p95). The latter is explicitly recorded as known work;
CSV references, regression ceilings and all deterministic gates are unchanged.

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

Latest codec integration supersedes the older 12-AT/colour implementation
limitation below: JBIG2 extended templates decode with an explicit unverified
warning, and colour-extension regions expose monochrome ink only, not colour
playback. Their hand-derived fixtures are deliberately named `unverified-*`;
striped unknown-height pages have separate reference coverage. Broader external
verification remains a gap. JPX adds manifest-provenanced OpenJPEG reference
matrices, packed multi-component references, strict container fallback/depth
checks and `SMaskInData=2` coverage. The combined JBIG2/JPX/image focus passes
392 tests. Neither addition promotes the overall codec row to Done.

Validation evidence is historical, not a fresh run against the current tree.
The [repository-fixture compatibility report](validation-compat.md) records
PDF/A-1b/2b/3b verdict agreement on 551/551/549 comparable files, without a
veraPDF-clean PDF/A positive fixture; its UA-1 figures predate the separate
[2026-10-31 public-corpus reconciliation](spec-backlog.md#tagged-pdf-and-pdfua-reconciliation-2026-10-31).
That later comparison reports 297 UA-1 and 138 UA-2 files with no corpus
exclusions, 0 false positives, and 17/11 missed failing files respectively.
Sample exclusions are separate from those public-corpus denominators.
Mapped-rule overlap does not establish coverage for unmapped rules or turn
file-verdict agreement into certification. See the [audit chronology](spec-audit.md)
for superseded findings.

Targeted PDF/A color regressions now detect implicit initial DeviceGray text and
path painting, honor calibrated `DefaultGray` resources, and require a PDF/A
OutputIntent when no usable default applies. CalRGB, DefaultGray, and authored
Type 3 text-with-DefaultGray fixtures are accepted by veraPDF 1.30.3 for their
matching profiles; corresponding Gray negatives fail. These controls are
targeted color-rule evidence only, not full PDF/A conformance.

| Clause | Feature | Status | Notes |
|---|---|---|---|
| 7.2-7.3 | Lexical conventions, all object types | Done | Names `#xx`, literal/hex strings, tolerant numbers |
| 7.5 | File structure, xref tables/streams, hybrid, `Prev` chain, object streams | Done | |
| 7.5.6 | Recovery of damaged files | Done | Vectorized byte scan for `n g obj`, replacing the recovery PCRE2 scan; test `RepairsBrokenXref` |
| 7.4 | Filters: Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors | Done | TIFF predictor 2 covers 1/2/4/8/16-bit components, with component-wise packed-sample reconstruction and big-endian 16-bit differencing. PNG predictors round packed pixel sizes up to whole bytes. Invalid/oversized predictor parameters raise `PdfParseException` instead of silently returning encoded samples. LZW/libtiff, ASCII85/Python, filter-chain and predictor fixtures provide bounded cross-checks. |
| 7.4 | DCT (baseline), CCITT, JBIG2, JPX | Partial | DCT baseline/extended/progressive **Done** (Huffman; SOF1/SOF2 support 8/12-bit precision, native-precision colour then sample >> 4 for 12-bit; nearest-neighbour chroma upsampling); arithmetic SOF9/SOF10 sequential + progressive, restarts and DAC **Done**, including default and non-default 12-bit DAC conditioning cross-checked with libjpeg-turbo 3.2.0. Earlier arithmetic fixtures were cross-checked with Pillow. Lossless SOF3/SOF11 supports Huffman/arithmetic coding, predictors 1-7, 2-16 bits, point transforms, restarts and subsampled interleaved scans; Huffman subsampling has libjpeg-turbo references, while arithmetic-lossless fixtures are independently spec-derived. Hierarchical DHP/EXP and differential SOF5/6/7/13/14/15 frames have spec-derived reconstruction fixtures, not external-decoder verification. CCITT G3 1D/2D + G4 **Done**, with libtiff option-matrix fixtures and damaged-row EOL resynchronization. These bounded cases do not establish exhaustive codec parity. JBIG2 supports generic MMR/arithmetic templates 0-3 + TPGD, refinement, symbol dictionaries including Huffman refinement/aggregation, text regions, halftone/pattern and /JBIG2Globals. Huffman single-symbol refinement and aggregate-text fixtures have exact bitmap regressions; broader combinations remain unproven. Unsupported: 12-AT extended templates and colour extensions; MMR halftone lightly tested. JPX **Done** for supported forms (JP2 boxes, codestream, tier-1/tier-2, 5/3 + 9/7 DWT, MCT, >8-bit sample precision, bilinear chroma subsampling, JP2 ICC, palette/channel definitions, alpha/SMaskInData, PPM/PPT packed packet headers, main/tile-header POC progression ranges with duplicate-packet suppression and coverage validation, and code-block style/ROI flags). OpenJPEG-referenced fixtures include main/tile RLCP POC and PPM/PPT; malformed POC length/order/coverage and packed-header boundaries are covered. Bilinear subsampling expectations use self-derived fixtures; code-block style/ROI coverage is fixture-based and does not establish exhaustive independent parity. Remaining: 12-AT/colour extensions and broader multi-range/spatial-order POC and feature combinations |
| 7.6 | Encryption (Standard RC4/AES, public-key) | Done | Standard handler R2-R6 read **and write** (RC4 40/128, AESV2, AESV3; pikepdf-verified). Public-key handler (Adobe.PubSec, adbe.pkcs7.s5, CMS EnvelopedData, AES-256 content) read/write with X509 certs - round-trip tested only, no third-party validator available. Permission flags enforced by `PdfEditor` (`PdfPermissions`) |
| 7.7 | Document structure: page tree, inheritance | Done | |
| 7.8-7.9 | Content streams, resources, functions (types 0,2,3,4) | Done | |
| 7.10-7.11 | File specs, embedded files, data structures | Partial | Reads file specifications from Names/EmbeddedFiles and associated-file arrays on catalog, AcroForm/fields, structure elements, pages, annotations and nested Form XObjects, with bounded traversal/resource paths. Exposes file-name variants, description, IDs, volatility, AFRelationship, embedded Params, raw objects and decoded bytes. `/FS`, `/Thumb`, `/EP`, recursive `/RF` and `/CI` remain inert. Collection schema/view/sort metadata now includes folders/navigators. Bounded tree diagnostics check ordering, limits, duplicates, malformed pairs and cycles; embedded-file diagnostics check decoded size and specified MD5 checksum. Encrypted metadata strings are preserved across save paths. No embedded code is executed or external payload opened. Remaining: broader tree-boundary, attachment and preservation combinations |
| 12 | Annotations, forms, outlines, actions | Partial | Outlines, named destinations, page labels, bounded action `/Next` traversal and scope-specific additional-action events, including link `/PA`. Typed inert action subtypes retain original dictionaries, malformed values and unknown entries without script/media execution, external access or navigation. Common/markup/subtype annotation details and inherited field metadata/flags/options, signature lock/seed dictionaries and XFA presence are exposed. Normal-appearance `/AP /N` and `/AS` rendering plus generated appearances remain available for supported annotations; text/button/choice filling includes multiline, comb, alignment and MK borders/backgrounds. See latest integration above and action-specific notes below. Remaining: exhaustive action-bearing objects/triggers, annotation appearances and complete form/action semantics |
| 14 | Metadata, tagged PDF, logical structure | Partial | Info, XMP, tagged flag and language; bounded StructTreeRoot/StructElem, RoleMap, ParentTree, page/Form MCIDs and `/Stm` content; Alt/ActualText/Lang and structure-ordered extraction. Form-local marked-content state is isolated/restored on errors and page-owned Do associations survive. Selected local normal-appearance MCRs remain stream-scoped with StmOwn owner checks. Whole-object OBJR now reads locally attributed annotation/Form text, including bounded nested Forms, suppressing MCR/Artifact duplication and scoping roots per page. Parse/parent/duplicate ownership/ParentTree consistency diagnostics and one-pass bounded ParentTree indexing expose truncation. Plain ExtractText/Analyze still exclude annotation text. Remaining: external streams, arbitrary owner graphs, per-invocation tagging and exhaustive malformed/reused-form semantics; not PDF/UA certification. The editor can now create, move and remove structure elements, add MCR/OBJR kids and rebuild `/ParentTree`/`/ParentTreeNextKey` (page, `/Stm` and OBJR owners), and `AutoTagUntagged` tags text as P and other painted content as Artifact without inferring semantics; extraction still has no per-invocation Form tagging and the editor does not yet edit `/Stm`-owned content streams or structure namespaces |
| 14.8/PDF/A,UA | Validation | Partial | Heuristic validators: PDF/A-1b/2b/3b now add ICC profile header/version/class/colour-space/component checks, XMP packet-header and extension-schema checks, PDF/A-1 Info/XMP value/date equivalence, font widths/encoding/CIDSystemInfo checks, `/NeedAppearances`, and named-action restrictions. PDF/UA-1/2 add document/optional-content flags, embedded-file `/F`/`/UF`, font mapping checks, MCID/Artifact/content-tagging consistency, heading/table/list/TOC/note structure rules, alongside MarkInfo, Lang, Title + DisplayDocTitle, Figure alt text and existing table-header checks. Missing TH Scope is informational per cell for UA-1, but `PDFUA-TABLE-HEADER-CONNECTION` is an error when no TD has `/Headers` and no TH has a valid Scope (ISO 14289-1 7.5; veraPDF 7.5-1); a TD sharing a row/column with a Scope-less TH is only the advisory `PDFUA-TABLE-HEADER-SCOPE-PARTIAL`; no visual/semantic header inference or higher-header inference. Deep-rule references were compared by hand against published veraPDF profile XMLs and, since 2026-10-31, run against veraPDF 1.30.3 (user-local, not bundled): over the veraPDF-corpus PDF_UA-1 files (297) our verdict has 0 false positives against veraPDF's own pass verdicts and misses 17 of 156 failing files (font-program rules 7.21.4.2/7.21.5/7.21.7-2/7.21.8, media clip 7.18.6.2, one widget case, one partial-Scope table); these are file-verdict figures only, and the per-rule overlap printed by `tools/ua_crosscheck.py` shows many veraPDF rules (e.g. 7.2-x structure nesting, 7.4.x headings, 7.9 notes, UA-2 8.4.4) have no mapped rule of ours, so rule-level coverage is established only for the mapped rules; over PDF_UA-2 (138) 0 false positives and 11 misses of 84 (font-program rules and 8.2.5.20-2 link targets); PDF/UA-2 now parses structure namespaces (`/NS`, `RoleMapNS`) and checks PDF 2.0 version, pdf2 Document root, role mapping, H/Note/Math/ListNumbering/Caption, structure destinations, annotation artifacts and file-spec Desc/AFRelationship; ISO/TS 32005 Table 5 nesting is partially implemented as detailed below, not a complete matrix. Sample 19 is veraPDF-clean for UA-1. The corpus is external evidence only and not committed; this is not certification. Unconfirmed rules/heuristics remain: `PDFA-ICC-SIZE`, `PDFA-XMP-EXTENSION-INVALID`, `PDFA-FONT-CMAP-SYSTEMINFO`, heuristic `PDFUA-FONT-TOUNICODE` and `PDFUA-TABLE-HEADER-CONNECTION` (empirically derived), advisory `PDFUA-BOOKMARKS`, and `PDFUA-LINK-DESCRIPTION` for UA-2. |
| Robustness | Fuzz/truncation tests | Done | Deterministic mutation of all fixtures (400-seed run green): only PdfException (or NotSupported for unsupported filters), timeouts enforced. Caps added: lexer nesting 200, inflate 1 GiB, raster 100M px, JBIG2/CCITT budgets |
| Perf | Hot paths | Partial | Rasterizer/JPEG already tuned earlier; added static system-font cache and bounded immutable lexer operator-token reuse. On identical base `60107c7`, already-open vector rendering was 9.4–16.1% faster across two run orders with about 15.5% fewer managed allocations; fresh-vector and text CPU gains were not reliable. Six-document decoded-byte/token hashes matched. Existing results: text.pdf 96dpi render 107→98 ms, vector.pdf 246→243 ms; load scales linearly (1000 pages ≈12 ms) |

PDF/UA-2 containment now checks all 1534 unconditional parent-child exclusions
and 14 direct-content bans derived from the pinned veraPDF 1.30.3 combined
profile's 1636 ISO/TS 32005 Table 5 rules. The earlier "1722-check Table 5"
description conflated profile totals with nesting rules. ISO 14289-2 6.2 and
8.2.4 normatively incorporate ISO/TS 32005; the rules are not attributed to an
ISO 14289-2 Table 5. Namespace-aware role resolution applies to both ends of a
relationship, `Part`/`Div`/`NonStruct` inherit containment, and incomplete
traversal produces an Error. All 33 unconditional occurrence predicates are
also implemented, with counts combined through transparent wrappers and
isolated by effective parent. The 110 authored controls agree with veraPDF
1.30.3 at the exact occurrence-rule level (39 failures, 71 nonfailures, no
exclusions); four independent UA-2 positives remain compliant in both.
The 55 grouping-dependent predicates (including four conditional occurrence
limits), Ruby/Warichu sequence rules, and broader semantic/visual checks
remain outside this slice. See [scope and evidence](validation-compat.md#2026-11-03-pdfua-2-containment-slice).
The dated corpus-wide counts above are unchanged; this is not certification.

PDF/UA Link-parent validation applies the profile-specific rule: UA-1 requires
Link; UA-2 accepts Link or Reference, including role-mapped types (ISO 14289-2
8.2.5.20; veraPDF 8.2.5.20-1). UA-2 different-target grouping (8.2.5.20-2)
now compares local structure targets of direct OBJR Link annotations within
each Link/Reference, including named destinations and GoTo `/SD` precedence.
Cycles/depth exhaustion in destination chains produce an explicit error.
The four public 8.2.5.20-t02 fail/pass a/b controls agree with veraPDF 1.30.3,
including exact-rule overlap for both failures. URI/remote grouping, general
destination validity and exhaustive named-tree syntax validation remain
open. This focused comparison does not update the dated corpus-wide counts.

PDF/UA-1 media clip data checks require string `/CT` and even-length `/Alt`
string arrays with nonempty descriptions (7.18.6.2-1/2). Reachable rendition
selectors and clip sections are traversed inertly with explicit incomplete-check
errors at cycle/reference/depth/work/array boundaries. All five public 7.18.6.2
controls agree with veraPDF 1.30.3, including exact-rule overlap for three
failures. Empty content-type strings/alternate arrays remain accepted; MIME
grammar, description quality and broader media semantics are not validated.
UA-2 and the historical corpus-wide counts are unchanged.

PDF/UA-2 local page-destination detection (8.8) includes named/dictionary
destinations, catalog OpenAction and the existing inert GoTo trigger walk,
with `/SD` precedence. Shared bounded resolution preserves Link grouping;
action-walk and destination-chain boundaries report incomplete-check errors.
Named lookup also reports cycles, depth beyond 64, indirect chains beyond 32
and a per-lookup 10,000-operation validation budget, without changing ordinary
inspection/import lookup policy. Completed shared subtrees are not cycles;
reachable siblings remain searchable after a cycle/depth boundary. Evidence is
self-authored boundary regressions, not external malformed-tree agreement.
Both public 8.8 negatives retain exact-rule agreement with veraPDF 1.30.3.
General destination validity, named-tree syntax/key-byte matching and exhaustive action-owner
coverage remain open; historical corpus counts are not remeasured.

The clause-12 action model also exposes inert typed local `/GoTo` metadata through `GoToDetails`: `/D` names, byte strings, and explicit arrays are retained without navigation, named-destination lookup, or array-element resolution. Missing and wrong-type destinations remain null in the typed view while the original dictionary is preserved; array contents are not conformance-validated.

Typed `/Named` metadata exposes `/N` through `NamedDetails`, retaining standard and unrecognised names and the original action dictionary without dispatching actions. Missing or wrong-type names are null in the typed view.

Typed `/GoToE` metadata exposes top-level `/F`, `/D`, unchanged raw `/T`, and nullable `/NewWindow` through `EmbeddedGoToDetails` (12.6.4.4, Tables 204/205). Its additive `TargetPath` view exposes `/R` names (including unknown names), `/N` byte strings, `/P` integers or byte strings, `/A` integers or text strings, and nested `/T` dictionaries, resolving only indirect values in the current PDF. Paths expose at most 64 dictionaries; `NestedTargetBoundary` reports cycles or depth limits while the original raw `/T` remains reachable through each node's `Dictionary`. Missing, malformed, and unknown entries are preserved. No conditional requiredness, attachment/tree conformance, name-tree/page/annotation lookup, destination resolution, embedded-content access, file opening, or navigation is implemented; `/SD` is not a GoToE field. Focused target-path cases in `EmbeddedGoToTargetTests` pass in the integrated Release run.

Typed inert `/GoToDp` metadata (12.6.4.5, Table 206) exposes `/Dp` through `GoToDpDetails` only when it is a local indirect reference resolving to a dictionary; `/Type` is optional, but if present must be `/DPart`. Missing `/Dp`, direct dictionary values, and wrong-type values remain inspectable in the unchanged action dictionary. This is inert inspection, not navigation or DPart-tree validation: `/DPartRoot` is not searched, and `/Start` and `/End` are not resolved. Focused cases in `GoToDpActionTests`, including omitted `/Type`, pass in the integrated Release run.

The separate `doc.GetDPartRoot()` API exposes bounded inert hierarchy metadata (14.12, Tables 408/409): original dictionaries/references, grouped depth-first children, optional types, and explicit malformed/cycle/repeat/limit boundaries. Budgets are 64 expanded dictionaries per path and 4096 global node/group slots each, preserving raw unvisited tails. `/Start`, `/End`, `/Parent`, `/DPM`, `/Metadata`, and `/AF` are inspectable without navigation or payload decoding. `DPartHierarchyTests` passes in the integrated Release run. This inspection API does not validate tree conformance; clause 14 stays Partial.

The separate additive `doc.ValidateDParts()` checks bounded parent/group links, page references/ranges, coverage/non-overlap/DFS order, and page `/DPart` back-links (14.12.2-14.12.4). Optional RecordLevel identifies an existing zero-based level; NodeNameList must have one PDF/XML Name per longest-path level (XML colons allowed), with an independent 4096-item inspection budget. Independent page-tree traversal honors budgets and reference generations; incomplete inspection cannot report `IsConformant`. Semantic record-component interpretation and payload semantics remain excluded; raw boundaries are preserved. Tests pass in the integrated Release run; clause 14 remains Partial.

JPX POC coverage additionally includes a three-layer, multi-precinct fixture that changes from one PCRL layer to the remaining CPRL layers. Its decoded pixels match Pillow's OpenJPEG-backed decode (OpenJPEG/libopenjp2 2.5.4) exactly, and tests verify both progression records. POC-plus-PPM/PPT fixtures exercise component reordering and packed segments ending at complete packet-header boundaries, with exact decoded samples. Legacy PPM/PPT references now also end segments at complete packet headers and verify exact RGB samples. The decoder retains marker-segment boundaries and rejects mid-packet-header splits per T.800 A.7.4/A.7.5; byte-position repartition regressions pass in the integrated Release run. These are bounded reference cases, not exhaustive POC coverage; multi-tile/multi-tile-part packed headers, including PPT headers preceding bodies in an earlier tile-part, remain backlog items.

## Renderer (`PortableDocument.Content`, `PortableDocument.Render`)

Strict rendering diagnostics also warn on non-stencil images in Type 3 `d1`
glyphs (9.6.5), including inline images and images reached through Forms.
Resolved `/ImageMask true` stencils remain allowed; `d0` is unaffected.
The warning does not skip/recolour images or change permissive pixels, and
does not establish rejecting validation or complete Type 3 semantics.

| Clause | Feature | Status | Notes |
|---|---|---|---|
| 8.4 | Graphics state, paths, clipping | Done | AA scanline raster, non-zero/even-odd. Opt-in `PdfRenderOptions.StrictContentValidation` reports recognized-operator operand arity/type issues, graphics-state underflow, unbalanced text/marked-content state, and Type 3 `d0`/`d1` ordering as warnings only; rendering remains permissive and the default is unchanged. |
| 8.4.3 | Stroking: width, caps, joins, miter, dash | Done | |
| 8.6 | Device, Cal*, Lab, Indexed, Separation, DeviceN, ICCBased | Partial | Matrix/TRC RGB, gray kTRC and LUT mft1/mft2/mAB RGB/CMYK XYZ/Lab to sRGB with Bradford D50->D65. ri/ExtGState RI/image Intent reaches path/text/shading/image/backdrop and wrapper conversions; q/Q preserves it, unknown/default intent is RelativeColorimetric. A2B0/1/2 intent selection falls back to A2B0 then matrix/TRC; absolute uses media-white XYZ scaling, with v2 display D50. Hash-cached profiles use intent/BPC-specific synchronized 17-point grids. Matching non-Pattern Alternate remains context-sensitive. Added v4 perceptual reference black, LittleCMS-compatible implicit perceptual/saturation LUT BPC (even Off/Default), relative UseBlackPtComp, optional B2A/mBA destination conversion and standalone ncl2 inspection. Corrupt B2A does not discard valid A2B. Black detection follows cmsDetectBlackPoint, not ISO 18619; implicit BPC is compatibility policy, not an ISO/PDF mandate. Bounded Pillow/LittleCMS references match within two levels. Remaining: unsupported D2B/B2D elements (including bACS/eACS), device-link/abstract/N-colour transforms, partial observer adaptation, general CMM/destination/gamut support and exhaustive transparency-group conversion. Named ncl2 is not accepted as PDF ICCBased. Device/Cal/Lab fixed gamut clipping remain. DeviceCMYK now uses a 9x9x9x9 table measured from PDFium output (within 9 levels of PDFium off-grid, mean 0.75), not Adobe's or a press profile; table provenance (PDFium/Foxit data, upstream source unverified) is a residual risk and the resource is swappable (see [colour-conversion.md](colour-conversion.md)). DeviceN `/Attributes` are parsed and exposed as hints only; no mixing-hint simulation. DefaultGray/RGB/CMYK remap Device spaces for painting (not independently compared); D2Bx/B2Dx multiProcessElements are read (LittleCMS 2.17 checked on self-derived fixtures); CalRGB/Lab with D65 white match PDFium within 2 levels, while non-D65 white points and CalGray differ from PDFium by design (see colour-conversion.md); CalGray/CalRGB/Lab fixture comparisons match Edge within +-2; hand-built ICC PDFium-white cases use independent lcms/synthetic evidence instead |
| 8.7 | Tiling and shading patterns | Partial | Types 1-7 for supported forms, including Gouraud triangles, type-5 bit alignment, edge continuation, actual Coons/tensor surface evaluation and bounded device-space tessellation. Inputs interpolate before per-sample Function evaluation; malformed/unsupported/wrong-arity mesh functions warn and skip. Typed PostScript calculator semantics, finer axial/radial ramps, shading BBox/background and tiling/pattern-stroke fixes have regressions and selected PDFium comparisons. Remaining: finite bounded tessellation, broader function-engine/colour-space combinations and independent fixture coverage; not exhaustive parity |
| 8.9-8.10 | Images, inline images, XObject forms | Partial | See filters above; stencil + colour-key + SMask alpha and Matte. `/Interpolate true` (inline `/I` too) selects pixel-centred bilinear decoded-RGB/premultiplied-alpha sampling, including stencil coverage and independently sampled explicit/soft-mask grids; absent/false preserves the existing default path. Analytic regressions cover transforms, clips, alpha, masks and cached bands. Reader-dependent policy under 8.9.5.3, not viewer pixel equivalence or full image/Form parity; bounded box-filter minification and RGB colour-conversion limitations remain. Deprecated `/F` external stream data and Reference/PostScript XObjects remain unsupported and are not opened or executed. See `spec-audit.md` 3.2 |
| 9 | Fonts: TrueType, Type0/CID, Type3, ToUnicode | Partial | Embedded CFF (Type1C, CIDFontType0C, OpenType-CFF) outlines via `CffFont` (Type 2 charstrings, subrs, flex, FDSelect, endchar-seac accents, arithmetic/storage operators, full 391-entry standard string table and StandardEncoding generated from fontTools). Legacy Type 1 (`FontFile`, PFB segments / PFA hex / raw binary) via `Type1Font`: eexec + charstring decryption (lenIV), built-in `/Encoding` including named Adobe ExpertEncoding, `/FontMatrix`, Subrs, hsbw/sbw, flex/hint replacement, div, seac, and Multiple Master WeightVector/Blend metadata and supported blend othersubrs. Up to 16 masters and four axes are bounded; regular-grid design-coordinate interpolation is supported, not arbitrary font PostScript execution. All 59 PDF-predefined CMaps (Adobe-Japan1/GB1/CNS1/Korea1 incl. UniJIS/UniGB/UniCNS/UniKS UCS2+UTF16, RKSJ, EUC, GBK/GBK2K, Big5/ETen/HKscs, KSC/UHC, `-V` variants) are embedded as a compact deflated resource (`predefined-cmaps.bin`, ~320 KB) generated by `tools/cmapgen/gen_cmaps.py` from adobe-type-tools/cmap-resources (BSD-3-Clause, commit f5cf3bc); embedded CMap streams (codespaces, cidrange/cidchar, usecmap) are parsed too. Adobe Glyph List 2.9's 4,281 mappings and 201 Zapf Dingbats mappings are embedded; glyph names support suffixes, underscore sequences, and strict `uni`/`u` Unicode forms. `/W2` and `/DW2` are applied for WMode 1 text origins, advances, and geometry. Supported embedded OpenType glyf/CFF fonts apply script-selected GSUB `vrt2` or `vert` single substitutions to vertical glyphs. Unsupported lookup types/flags are warned and skipped; unsupported FeatureVariations are warned and default vertical features are used, while malformed GSUB data keeps the original glyph. This is not full OpenType shaping. Non-embedded CJK fonts fall back to an installed system CJK font (CID to Unicode inverted from the Uni*-UTF16 CMaps, half-width Latin via legacy CMaps); if none is installed nothing is drawn but text extraction still works. Not done: bundled CJK glyph data, arbitrary MM PostScript design procedures/irregular master layouts, OpenType font-name based CJK fallback beyond a fixed candidate list, Type 1 hinting (intentionally ignored), and complete OpenType shaping. Strict diagnostics also check text-object balance/text operators outside `BT`/`ET` and diagnose Type 3 `d0`/`d1` ordering; these are warning-only checks, not rejecting validation |
| 11 | Transparency: alpha, blend modes | Partial | Alpha + all 16 blend modes (separable and Hue/Saturation/Color/Luminosity, checked against the spec formulas; /BM arrays use the first known name) + isolated/non-isolated/knockout form groups (11.4, layers allocated only when needed; backdrop removal per 11.4.8) + ExtGState soft masks (Luminosity and Alpha, /BC in the group's /CS, /TR function) **Done**. Page-group blending now supports DeviceCMYK, calibrated and ICC blend spaces; same-operator fill+stroke and text knockout, plus `/AIS`, have bounded rasterizer support. Final page output remains isolated over opaque white; page elements rendered with `asPageElement: true` honor page-group `/I`, CropBox clipping, and ignore inherited `/CS` for non-isolated groups. Analytic tests pass, but the new output has not been compared with Chrome/PDFium. The CMYK non-separable K rule follows Adobe/Splash behavior rather than explicit ISO text; text-knockout work is clipped to the group bounds, not tight glyph bounds. RGB and tested blend modes match prior references; DeviceGray retains known conversion differences from Edge. Remaining: arbitrary backdrop/transparency output and per-object knockout outside Form XObjects. Clips applied while a soft mask is active now survive clearing the mask |
| 8.11 | Optional content (OCG/OCMD) | Partial | Evaluates `/OCMD /VE` Boolean visibility expressions (nested `/And`, `/Or`, `/Not`) before legacy `/P`/`/OCGs` policies; malformed expressions hide their content with a warning. `RenderPage` can select named catalog `/Configs`; `/Unchanged` inherits default-config state. Applies `/AS` `/View`, `/Print`, and `/Export` states plus `/Zoom` bounds. `OptionalContentState` exposes `/RBGroups` and `/Locked`, applies exclusive radio-group selection, and rejects visibility changes that violate locks without changing state; changes are in-memory and actions/scripts are not executed. Explicit per-render Language and User context now selects `/AS` OCGs using Language exact/preferred-primary fallback and User `/Ind`/`/Ttl`/`/Org` matching; context is caller-supplied, with no host-setting reads. Language validation is structural only (not full BCP 47 registry/canonicalization), CreatorInfo/PageElement policy is not inferred, and web UI/save-back for OCG state remains unsupported |
| 10 | Rendering intents, overprint, halftones, transfer funcs | Partial | Intent/ICC selection, media-white conversion, perceptual black and LittleCMS-compatible BPC are described under 8.6; these do not complete general CMM support. Supported TR/TR2 apply to eligible fully opaque paints. Opt-in OverprintSimulation handles OP/op/OPM using an RGB approximation, off by default. Analytic expectations include self-consistent cases; no independent print/raster comparison. Halftone, flatness and smoothness remain inert. Remaining: exact general overprint/colorants, halftones, transfer combinations and general perceptual/gamut/CMM matching |
| 13 | Multimedia, 3D, RichMedia, XFA, JavaScript | Partial | XFA packets and JavaScript payloads remain opaque, never executed. RichMedia assets/configurations/activation/presentation retain typed metadata and raw dictionaries without activation. Additional inert media readers expose rendition/selector/clip graphs, criteria, players, play/screen parameters, offsets, durations, Movie and Sound dictionaries; rendition/clip traversal is memoized and depth/node bounded with observable truncation. 3D readers expose U3D/PRC stream/reference metadata, activation/animation, projection/background/render/lighting, nodes/cross sections and measurement subtype, preserving raw model streams and `/OnInstantiate` without decoding or execution. Inert U3D (header/block structure, profile bits, modifier chains) and PRC (header/section scan, bounded inflate) models plus media-evaluation, RichMedia, XFA and JavaScript static-analysis models were added; fixtures are synthetic, the PRC layout and compressed-mode U3D block payloads are not verified against Adobe-produced files, and JS/XFA analysis is heuristic (memoized, budgeted). Remaining: complete dictionary semantics, model/measurement interpretation and broader malformed/preservation coverage. Playback, host-software matching, external file access and script execution are not performed |

Additional clause-9 coverage: CFF1 predefined ISOAdobe/Expert/ExpertSubset charsets and Standard/Adobe Expert built-in encodings (Adobe TN 5176 Appendices B/C, fontTools 4.63.0 tables). For embedded non-CID CFF, absent PDF Encoding/BaseEncoding uses the built-in encoding, Differences overlays it, and ToUnicode wins for extraction. Explicit PDF MacExpertEncoding is supported as a distinct glyph-name table (ISO 32000-2 Annex D.4), not Adobe ExpertEncoding; holes map to .notdef, Differences override and ToUnicode takes precedence. Legacy Type 1 named ExpertEncoding and bounded MM blends are now supported as described in the row above. CFF, annotation/AP, DPart hierarchy and JPX boundary regressions passed 583 focused / 1160 full Release cases before this extension; these are historical, not current integration counts.

Additional bounded clause-8.6 coverage: `/UseBlackPtComp` state is propagated, with Default treated as Off and absolute intent disabling BPC. Supported relative XYZ matrix/TRC RGB/gray v2/v4 input/display ICC profiles use a prepared white-preserving affine XYZ map; `SupportsBlackPointCompensation` exposes the strict supported subset. LUT/CMYK/Lab, general black estimation and perceptual PCS/reference-black handling remain incomplete; this does not resolve the LittleCMS perceptual discrepancy above. All ICC plus annotation/validator selectors passed 184/184 focused Release cases, followed by the expanded run below.

Additional bounded PDF/UA annotation checks verify `/StructParent` -> ParentTree -> direct `/OBJR` with indirect `/Obj` identity, preserving missing-key and Link Contents/Alt behavior. ParentTree values must be raw indirect references to current in-use xref entries with matching generations (zero for compressed objects); the focused `AnnotationStructureValidationTests` selector passes 48/48. Existing parser/10k-per-page limits and subtype exemptions remain; validators are not certification.

The expanded BPC/UA/DPart/root-field/MacExpert batch passes 833/833 combined focused and 1393/1393 full Release cases. The Release solution build succeeds with four warnings (EditorWeb CS8602, TransparencyTests CS0675, FontCmapTests xUnit2029, SecurityTests xUnit2013). Clause statuses remain 15 Partial / 0 Todo; full parity is not established.

Subsequent additions pass the expanded Release run below and are not covered by those earlier totals: inert `SubmitFormDetails` exposes local file-specification/Fields/Flags/CharSet values and raw dictionaries without submission, field lookup, defaults or inheritance interpretation (12.7.6.2). New JPX fixtures cover two tiles/two tile-parts each with packed headers preceding bodies in the same tile-part. Marker ordering and complete-header placement were reviewed against T.800 A.7.4/A.7.5. The transformed PPM stream has independent OpenJPEG evidence; PPT expected samples derive from the decoded original source, since OpenJPEG 2.5.4 rejects the transformed multipart PPT. No decoder code changes accompany these fixtures; earlier-tile-part headers and broader combinations remain gaps.

The whole-object OBJR annotation-text gap in clause 14 now has bounded local selected-appearance support, covered by the expanded Release run. OBJR page selection and annotation StructParent/ParentTree ownership gate direct normal-appearance text; artifacts are excluded and repeated references deduplicate per annotation/page. ActualText/opt-in Alt retain precedence. Tagged appearance streams are not categorically excluded: explicit stream/MCID MCR text stays on its existing path to avoid duplication, and remaining appearance text can contribute through OBJR. Conservative MCR reservation on invalid page/owner links, nested Forms, arbitrary whole-object XObjects and ambiguous overlap remain limitations. No annotation Contents inference, appearance synthesis or action execution is added, and plain extraction remains annotation-free.

The integrated SubmitForm/JPX/OBJR/JBIG2/Pages batch passes 965/965 combined focused and 1471/1471 full Release cases (zero failures/skips), including hand-derived MMR halftone pattern/grid coverage and existing Form-child HTTP editing regressions migrated into shared EditorSession. The incremental Release solution build succeeds with zero warnings/errors; the preceding focused compile emits five existing warnings documented in the backlog. Browser-only parallelism guards and optional null-preserving font fallback are retained. The integrated WASM project builds; published Chromium evidence is from the Pages child's pre-merge branch, not a rerun here. Clause statuses remain 15 Partial / 0 Todo; no full-parity claim.

The later 12-bit Huffman SOF1/SOF2 DCT addition is covered by native-precision reference fixtures and PDF image-pipeline regressions; arithmetic 12-bit remains unsupported. JPEG/editor focus passes 120/120. After undo-safe asset retention and deterministic ten-minute unclaimed-upload expiry/cancellation tests, editor/session/HTTP focus passes 18/18 and the full Release suite passes 1502/1502. Incremental solution build succeeds with zero warnings/errors. Integrated published WASM Chromium smoke now passes image insert/delete/undo and the existing local edit/export/mobile/no-network scenarios; this supersedes the earlier pre-merge-only smoke boundary. Scope and residuals remain documented; counts stay 15 Partial / 0 Todo.

### Current all-branch integration evidence

The combined Release solution build has zero compiler warnings/errors and all
3,079 native tests pass, including sequential/threaded rendering of the same
396 golden entries without baseline changes. The Node chunk passes 55 tests.
Fresh mixed-AOT startup meets unchanged ceilings: 62 files, 24,913,330 raw bytes,
8,882,757 gzip bytes and a 16,104,279-byte native module; 488 lazy files are
counted separately. AOT publishing still emits reflection-based JSON IL2026
diagnostics; it is not a warning-free publish.

Published browser checks pass startup/cache version pinning, culture, recovery,
protected saving, local file/font access, forms, object editing, merge, samples,
Contents, worker-pool pixel equality/fallback, image continuity, selection, JSON,
accessibility and all 41 responsive configurations. Save-overlap input-to-frame
is 10.3-11.7 ms at 4x CPU against the unchanged 50 ms ceiling. The performance
profile covers 535 metric groups and passes the unchanged CI regression policy
and ten synthetic controls, but reports four strict timing-goal misses, including
two new goal misses; regression-policy success is not strict-goal success.
Current clause-table status is 14 literal Partial rows and zero Todo rows
(the separate editor Partial entry is not a clause row). Full parity remains open.

## Editor

| Feature | Status |
|---|---|
| Page delete/reorder/rotate, merge/split by page import, Info edit, full save (`PdfEditor`, `PdfWriter`; unreachable objects dropped, encryption removed unless requested) | **Done** |
| Overlay content editing (text, shapes, images; move/resize/restyle; web UI) | Done |
| Editing existing page text in place (convert run to editable object, original operator removed, advance preserved). Text inside non-shared form XObjects is listed (`TextSpan.Form`) and editable/removable via `SetStreamOverride` on the form stream. Forms drawn more than once are NOT exposed for editing (edits would be ambiguous). The original embedded font is reused when the font/bold/italic is unchanged and the font can encode every character (`PdfFont.TryEncode`); otherwise a catalog/standard font is used | Done |
| Moving/resizing existing images and vector objects | Partial; supports page-level objects and direct painted children inside uniquely invoked indirect Form XObjects. Shared, repeated, hidden, nested, or otherwise ambiguous Form children remain withheld; graphic-group transforms remain available. Complex vector paint/clip ranges remain unsupported. |
| Form filling (text and checkbox/radio fields, with editor UI) | Done |
| Redaction (`Redactor.Apply`): glyph removal with surviving advances; arbitrary filled/stroked paths and clips under rotated/sheared transforms, curves flattened with a 0.05 page-space-unit bound before polygon subtraction; per-invocation shared/nested Form copies; partial raster/inline/stencil image blanking; hidden optional content included. Intersecting annotations/widgets/field entries and XMP/Info/thumbnail metadata are removed; copied redacted Forms drop Metadata/PieceInfo/LastModified, and replaced resources are pruned unless untouched uses still need them. Opaque boxes follow content removal. Outline-title matching retains canonical Unicode/whitespace equivalence, literal punctuation, no cross-operator phrase joins or confusable folding; font extraction and whole-span fallback remain limits. **Done for this bounded API**, not universal sanitization: full rewrite required, unsupported/singular/over-budget geometry removed whole or clipped empty; complex paint may lose unredacted surroundings. The content-only overload removes intersecting forms/images whole. Unredacted content intentionally used elsewhere and editor undo/clipboard/recovery copies are not a document-wide secret purge |
| Web editor: context menus (clipboard, arrange/z-order, align, redact), Shift/Alt drag modifiers, collapsible responsive panels | Done |
| Web editor fonts: standard 14 + installed fonts embedded as subsets: glyf TrueType (Type0/Identity-H, CIDFontType2), CFF-flavoured OpenType (.otf; name-keyed CFF subset and embedded as FontFile3 /CIDFontType0C, CID-keyed CFF embedded whole), .ttc/.otc collections (face extracted to a standalone sfnt), ToUnicode on all. Characters missing from the chosen font are split into runs using a fallback font (Segoe UI, Arial, symbol/CJK families, then any installed face). Synthetic bold/italic when a face is missing. Alignment, underline, strikethrough, letter spacing, line height. TrueType subsets truncate loca/hmtx to the highest used glyph id. Extra font folders via env `PDF_EDITOR_FONT_DIRS`. Verified end to end (`tools/FreeDF.EditorWeb/e2e_fonts.py` against a live server: original-font edit, CFF .otf, ttc Cambria Math, CJK fallback): pikepdf/qpdf decode clean, pdfminer.six and pypdf extract every inserted string, own extractor round-trips, and Edge (`FreeDF.RenderCompare --channel msedge`) renders the edited files within tolerance (1.5% and 0.2% differing pixels). xunit coverage in `EditorFontTests`. CID-keyed CFF and CJK subsets remain large (whole table / high-gid prefix) | Done |
| Web editor shell: brand, adjacent File menu, document name/tooltip and saved/unsaved status; one-time Sponsor link; File > Merge PDFs with local multi-selection/drop, ordering, page ranges and insertion point; existing save/export/find/print/navigation/zoom/recovery | Done for these UI operations; merge is atomic and carries forms, outlines and page labels, not every catalog-level structure. Encrypted inputs unsupported by WASM are explicitly flagged, never silently cached decrypted |
| Non-embedded fonts: bundled Liberation (Helvetica/Arial, Times, Courier families) and DejaVu (Symbol/ZapfDingbats) with a compact Adobe Core 14 width table, plus lazily fetched Noto Sans CJK glyph chunks for Japan1/GB1/CNS1/Korea1 (see [fallback-fonts.md](fallback-fonts.md)) | **Done for substitution, not full parity**: widths from `/Widths` win; outlines are scaled to match; no Type 1 hinting; CJK serif requests use the sans design; Adobe-private-use Symbol glyphs draw nothing. Fonts and CJK data are never in the startup payload. The CJK reference comparison against Edge was unreliable on the development machine (Edge drew mojibake), so CJK output is verified by fixtures/unit tests, not a pixel comparison |
| Web editor page-level undo/redo (delete, move, rotate, blank page, duplicate and page import/merge, restored together with their assets; Ctrl+Z/Ctrl+Y) | Done for page operations; asset retention follows the state being left; not a whole-document history |
| Web editor links: GitHub repository links beside Download and the Sponsor badge (Octicons mark, MIT) | Done |
| Web editor one-click samples: lazy same-origin `samples/manifest.json` + PDFs, sha256-verified, `File > Open sample`, empty-state button, `#sample=`/`?sample=` | Done for unencrypted samples; samples 17/18 (encrypted) work end to end only once browser crypto lands (`window.pdfEditorCapabilities.openEncrypted`): 17 is blocked as password-required and 18 is attempted and reports the precise limitation |
| Incremental save, object/xref streams on save, encrypted save | **Done** |
| 12.8 Digital signatures: detached SHA-256 approval/certification signing, DocMDP and FieldMDP checks, encrypted incremental signing, RFC 3161 timestamps and DSS | **Done** for bounded APIs, not full signature conformance. Explicit `PdfTrustPolicy` separates cryptographic integrity from chain trust and offline CRL/OCSP evidence; missing evidence remains Indeterminate. Dedicated TSA certificates, validity, revocation and strong timestamp imprints are checked before trusting time. PAdES signing options and incremental DSS/VRI authoring have pinned pyHanko interoperability fixtures. Timestamp acquisition is caller-supplied; HTTP is separately opt-in through `PortableDocument.Signing.Http`, never used by the browser. General PAdES LTV/revision-graph coverage remains open. Unclassifiable DocMDP/FieldMDP changes are violations/undetermined, not proof of permission; `Sign` may rewrite sources when incremental prerequisites are absent |

## Samples

`samples/` holds 27 committed PDFs (01-26 and 28) authored by `tools/FreeDF.Samples` in C# using this repository's lexer/writer/editor APIs. Python sample authoring has been removed; `_gen/fonts` retains licensed font assets. `GeneratedSamplesTests` requires byte-identical regeneration, including deterministic fixture-only encryption, and checks a generated-on-demand 27th collection containing all 50 pages of 01-26. Sample 28 (benchmarks, brand directions, spec parity; 18 tagged pages) is standalone and is not merged into the collection. The full collection preserves selected navigation/forms/layers/attachments/tag structures, is not fully tagged, and deliberately contains the public encrypted samples' pages unencrypted. Independent pikepdf/PDFium comparison remains validation tooling, not authoring. See [samples/README.md](../samples/README.md) for evidence and visual/size caveats.

## Render comparison

`tools/FreeDF.RenderCompare` renders with our renderer and with the browser's built-in PDF viewer (PDFium in Chrome/Edge, via Playwright), crops the page, and reports the fraction of pixels that differ beyond a 3x3-neighbourhood tolerance, plus diff images and `report.json`.

```
dotnet run --project tools/FreeDF.RenderCompare -- tests/fixtures --out compare-out [--channel msedge|chrome] [--dpi 96] [--page-tolerance 0.02]
```

Independent rasterizers differ in anti-aliasing and font hinting, so tolerances are per-fixture, not zero.

| Writer: object streams + xref streams, Flate compression, encrypted save (AES-256, AES-128, RC4 128/40, public-key), incremental save, signing; verified with qpdf/pikepdf/pyHanko | Done |

## Integration batch 2 (fonts, CMYK, inert models, editor)

Reviewed branches were cherry-picked in order onto the parity tip `bea1a95`: fallback fonts + lazy CJK data; DeviceCMYK table, nested group blend spaces and DeviceN attributes; inert U3D/PRC/media/RichMedia/XFA/JavaScript models; page-level undo/redo; GitHub repository links (Octicons notice); one-click sample collection; viewport-overflow fix; browser crypto (password dialog, owner unlock, AES-256 protected save, samples 17/18). Release build 0 errors; full suite **2144/2144**; fresh AOT publish 47 valid WASM artifacts (690 files, 74,916,843 bytes: 187 startup-set files / 56,340,200 bytes, 475 lazy `fallback-fonts` files / 13,733,097 bytes, 28 lazy `samples` files / 4,843,546 bytes). The startup request list (58 requests in the profile) contains no `fallback-fonts/` or `samples/` file; fonts, CJK chunks and samples are fetched on first use only. Chromium smokes: full, `--merge`, `--samples`, `--protection`, `--page-history`, `--viewport`, `--boot`, `--page-image` (sample 26) and the samples node tests pass; the 535-group profile CI gate passes. Pixel hashes (real WASM profile) match the checked-in file: only `09-images.pdf` changed (`33D9F80F...` -> `5A3B5223...`, CMYK table); 01, 03, 20 and the 12-page fixture are unchanged, so bundled fonts did not change those renders. Two stale smokes were updated for browser crypto (merge stack frame, samples 17/18 now offered) and sample opening now goes through the password-aware open path.

Caveats kept from the child reports:

* **CMYK table provenance:** values are measured from PDFium (BSD-3-Clause; Foxit-origin data whose upstream source is unverified). Residual risk; the `cmyk-srgb-9x9x9x9.bin` resource is swappable (`tools/make_cmyk_table.py`).
* **Adobe AFM notice:** the permission paragraph now matches the published Core 14 `MustRead` text verbatim, and the widths were re-derived from the published AFMs and match the embedded table. Copyright years differ per AFM file and are listed per family (the earlier single line was incomplete and was corrected).
* **U3D/PRC:** fixtures are synthetic; PRC layout and compressed-mode U3D block payloads are not verified against Adobe-produced files. XFA/JavaScript analysis is heuristic and inert.
* **CJK fallback:** the reference comparison against Edge was unreliable on the development machine (Edge drew mojibake); CJK is covered by fixtures and unit tests, not pixel parity. Serif CJK uses the sans design.
* **Samples 17/18:** now offered because `window.pdfEditorCapabilities.openEncrypted` is set in `<head>` before the menu renders; verified in `--samples` and `--protection` Chromium smokes.
* **Not full parity:** every matrix row that was Partial stays Partial (15 Partial / 0 Todo); nothing here is a full ISO 32000-2 parity claim. Startup byte delta versus the pre-batch tree was not re-measured on a control publish; the lazy font/sample bytes above are additive and outside startup.

## Integration batch 3 (worker pool, annotation and form authoring)

Cherry-picked in order on top of batch 2 (`27da5b9`): worker render-replica pool with band rendering and snapshot route (`343c8e2`, `91c9bfd`, `b23c588`); annotation authoring API, editor commands and fixes (`dcdbff0`, `107cd58`, `579b7b7`); AcroForm field authoring, editor UI and indexing (`2f62a3d`, `d7253b0`, `7617a72`). The signature-trust/PAdES-LTV commits (`6b58e96`, `a1464e8`) were **not integrated**: they add an `HttpClient` RFC 3161 client to `PortableDocument.Document`, which fails the guard tests `Document_assembly_has_no_process_or_network_dependency` and `Opening_listing_rendering_and_saving_never_touch_external_targets` (and the assembly is shipped to the browser). That branch needs the network client moved out of the Document assembly (and its pending re-review) before it can land.

Integration fix: form-fill appearance generation registered a standard font in the AcroForm `/DR`, which broke `DocMdpTests.Editor_form_fill_after_certification_is_permitted_by_p2`; appearance fonts are now created without touching `/DR`. Conflicts (publish asset list, smoke flags, `EditorSession` page-history capture for annotation/form operations, undo status text) were merged keeping both sides.

Status stays **Partial** for the annotation, form, action and rendering rows: authoring covers the implemented annotation/field types with generated appearances, not every ISO 32000-2 annotation or widget behaviour; signatures/PAdES-LTV remain as documented before this batch. **The worker pool ships opt-in (`?pool=1`), not on by default:** with it enabled the Chromium recovery smoke ("Resume retains exact original-font/geometry raster fidelity") fails, because render replicas have no fallback-font asset source (batch 2) and so can draw non-embedded text differently from the primary engine. Enabling it by default needs the replicas to receive the same lazy font assets (or a hash parity check) first. Pool speed-ups are measured on a non-AOT build in the child report (`docs/performance/2026-10-30-worker-pool.md`); AOT/SIMD pool timing was not measured there and the pool benchmark (`--pool`) was not rerun here.

## Authored text shaping and extraction integration

Editor-authored text now uses bounded OpenType GSUB/GPOS lookup processing,
script/language selection, Arabic joining, a pragmatic Devanagari shaper,
Unicode 16 bidi tables and line-breaking support. Existing PDF text is not
reshaped. Generated glyph/CID text includes multi-code-point ToUnicode entries
and ActualText; replacement extraction is opt-in, uses shared PDF string
decoding and preserves Form-local state even with malformed marked content.
HarfBuzz glyph fixtures and Unicode bidi conformance inputs provide bounded
references, not complete OpenType or script parity. Clause 9 remains Partial.

## Annotation rendering fidelity batch (clause 12)

Annotations without `/AP` now render through the shared generator used for authoring (`AnnotationAppearance.Build(viewer: true)`), replacing a weaker duplicate: cloudy `/BE` borders, Line/PolyLine endings, `/RD` insets on Square/Circle, `/BS /D` and `/Border` dash arrays, Text note icons, Caret and FileAttachment glyphs; FreeText uses `/C` as background and the `/DA` colour for the border. Annotation-dictionary `/OC` is evaluated, the Print flag is required for the Print event, NoView hides only on screen, Invisible hides unknown subtypes, NoRotate keeps annotations upright on rotated pages, and a 50,000-annotation page budget guards hostile `/Annots`. Stamp and Redact never get viewer-generated appearances.

Evidence: `tools/make_annot_matrix.py` generates 41 self-authored single-annotation fixtures and renders them with PDFium (pypdfium2). Highlight, Underline, StrikeOut, Square, Circle, dashed borders, Ink and the flag cases match PDFium pixel-for-pixel (Ink within 20 px); Squiggly, FreeText text placement and Text icon artwork differ in detail. PDFium draws no Line/Polygon/PolyLine/Caret/FileAttachment/cloud/`/RD` without `/AP`, so those follow the spec instead of PDFium and have no independent pixel reference. Not done: NoZoom, FreeText callout leader lines and `/RC` rich text, Line leader lines/captions, widgets without `/AP` at render time, Link border drawing. Clause 12 stays **Partial**.
