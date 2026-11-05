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

The UA-2 TOC/TOCI containment correction in `ValidationPdfUADeep.cs`
disables legacy raw-role checks in favor of these namespace-resolved
ISO 32005 facts. Six native controls and two corpus negatives were
independently cross-checked against the pinned combined profile.
Standalone UA-2 profile verdicts alone do not cover these exclusions.
The analogous UA-2 table-section correction retains the UA-1-specific
section and TBody constraints for UA-1 only. Eighteen native controls
and ten corpus fixtures were cross-checked against the combined profile;
known illegal section pairs retain the independently implemented nesting
checks. This does not establish grouping or full table-geometry semantics.
The legacy table section-count correction reuses namespace-resolved
Table-THead/Table-TFoot cardinality. Six native controls and two corpus
negatives were independently cross-checked against the same combined
profile; grouping-dependent limits remain deferred.
The legacy H-child count correction analogously reuses resolved
Document-H/Sect-H cardinality. Six corrected native controls and one
corpus negative were cross-checked against the combined profile; the
separate UA-2 H prohibition remains active. Heading and grouping
semantics are not broadened.

The namespace-aware list numbering/direct-content checks in
`ValidationPdfUA2.cs` were cross-checked against the Consortium's
[v1.30.3 PDF/UA-2 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-2.xml),
8.2.5.25 tests 1/2, under the same CC BY 4.0 licence. Existing C# checks
and list-shape endpoints reuse the independently implemented namespace
resolver; no validation-engine source or external control PDF is bundled.
Owner-aware local/ClassMap numbering lookup was additionally cross-checked
with nine native owner/precedence controls against test 1; it reuses the
independent bounded FENote attribute helper. Inheritance/revision and
direct-child/attribute scope are retained; grouping-dependent list
semantics are not derived from the reference's standalone verdicts.

The namespace-aware table/row/cell/header-ID endpoints in
`ValidationPdfUA.cs` were independently cross-checked against the same
profile, 8.2.5.26-4, with twelve native width controls. Existing C# table
algorithms reuse the independent namespace resolver. Exploratory tests 5/6
header-fallback differences are explicit, not claimed as completed coverage.
Owned local/ClassMap span lookup was additionally cross-checked against
test 4 with eight native owner/precedence controls. The independently
implemented bounded attribute helper is reused; full geometry and
attribute inheritance/revision are not claimed.
The terminal RowSpan overflow check was cross-checked against test 2
with six native controls. Two additional corpus diagnostics differ
from the reference's test-3-only reports; exact predicate agreement
and full table/group column-height coverage are not claimed.
The occupied-column ColSpan/RowSpan intersection check was independently
cross-checked against test 1 with six native controls. Existing C# carry
geometry is extended, not copied from a reference implementation. The
cross-check mapping is scoped overlap, not complete intersection coverage;
group geometry and span syntax remain deferred.
The UA-2 conditional header-presence correction follows the factual
conditional descriptions in tests 5/6 and six independent native file
controls. It retains UA-1 behavior and does not claim semantic table
classification or complete header-association fallback.

The null annotation StructParent boundary in `ValidationPdfUA.cs` follows
ISO 32000-1 7.3.9 and was independently cross-checked against the same
CC BY 4.0 profiles (UA-1 7.18.8-1; UA-2 8.9.2.4.9-1/8.9.2.4.14-1).
Existing exemption/tagging policies are retained; reporting's PrinterMark
family overlap does not establish full tagged-Artifact semantics.
No reference source, profile XML or external annotation PDF is bundled.

The tagged Artifact disposition exceptions for UA-2 annotations were
cross-checked against the same CC BY 4.0 profile, 8.9.2.2 tests 1/2
and 8.9.2.4.14-1. `ValidationPdfUA.cs` reuses the existing independently
implemented namespace/Artifact ancestry and strict parent-tree/OBJR
identity checks. No engine source or external control PDF is bundled;
grouping-dependent structure semantics are not expanded.

The namespace-aware H prohibition in `ValidationPdfUA2.cs` was
cross-checked against the same CC BY 4.0 UA-2 profile, 8.2.5.12-1.
The existing C# check reuses the independently implemented namespace
resolver; numbered-heading and hierarchy policies are not broadened.
No profile XML, engine source or external heading PDF is bundled.

The global namespace-aware caption ordering in `ValidationPdfUA2.cs`
was cross-checked against the same CC BY 4.0 UA-2 profile, 8.2.5.27-1.
The independently implemented role resolver is reused; UA-1 caption
policies in `ValidationPdfUADeep.cs` are retained only for that profile.
Grouping/visual caption semantics are not broadened; no engine source,
profile XML or external caption PDF is bundled.

The scoped TOCI Ref-presence check in `ValidationPdfUA2.cs` was
cross-checked against the same CC BY 4.0 UA-2 profile, 8.2.5.8-1.
Bounded iterative descendant/reference inspection is independently
implemented; target shape, identity and semantic completeness are
not derived from the reference's permissive presence results.
No profile XML, engine source or external TOC PDF is bundled.

The resolved MathML parent check in `ValidationPdfUA2.cs` was
cross-checked against the same CC BY 4.0 UA-2 profile, 8.2.5.29-1.
Endpoint namespace resolution is independently implemented; full MathML
schema/vocabulary/rendering semantics are not derived from these controls.
No reference-engine source, profile XML or external MathML PDF is bundled.

The resolved Note restriction and scoped PDF-2.0 Note vocabulary boundary
were cross-checked against the same CC BY 4.0 UA-2 profile (8.2.5.14-1
and native 8.2.4-1 outcomes). `ValidationPdfUA2.cs` and the shared Note-ID
selector in `ValidationPdfUADeep.cs` reuse the independently implemented
namespace resolver. Cyclic-role classification disagreements are documented;
reciprocal FENote references and NoteType semantics are not implemented
by this slice. No reference-engine source or external PDF is bundled.

The explicit FENote NoteType-name check in `ValidationPdfUA2.cs`
was cross-checked against the same CC BY 4.0 UA-2 profile, 8.2.5.14-4.
The bounded local/ClassMap/C owner/value inspection is independently
implemented; inheritance, revision selection and non-name syntax are
not implemented by this slice. No engine source, profile XML or
external note PDF is bundled.

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

The UA-1 reference-XObject prohibition was cross-checked against the
same CC BY 4.0 UA-1 profile, 7.20-1. Ref null equivalence follows primary
ISO 32000-1 7.3.9; indirect-null/nonexistent-reference acceptance differs
from two measured reference results. External-reference parsing/rendering
is not broadened and no profile XML or external PDFs are bundled.

The UA-2 text-field RV/V presence and XML text-content equality were
cross-checked against the same CC BY 4.0 UA-2 profile, 8.10.3.3-1.
Bounded XML parsing and text comparison are independently implemented;
rendered/CSS/schema/type semantics are not implemented. Primary ISO
32000-1 7.3.9 and 12.7.3/Table 220 establish null equivalence and
field-value inheritance; 12.7.3.4 establishes XML rich text and allowed
text-stream packaging. Dated presence disagreements and the additional
stream-attribute/inheritance controls remain explicit; no profile XML,
runtime, reference-engine source or external PDFs are bundled.

The scoped plain-value decoder also uses factual codepoint mappings
from ISO 32000-1 7.9.2.2 and Annex D.3/Table D.2, independently implemented
in `ValidationPdfUA2.cs` and cross-checked against the same CC BY 4.0
8.10.3.3-1 profile. The shared string decoder is unchanged.
The same scoped decoder is used for RV strings before independently
implemented XML parsing; retained stream-encoding disagreements are
documented rather than normalized to the reference.

The UA-2 file-attachment AFRelationship requirement was cross-checked
against the same CC BY 4.0 UA-2 profile, 8.9.2.4.10-1. Null handling
follows primary ISO 32000-1 7.3.9; bounded references are reported
separately from genuine omission. No profile XML or external PDFs are bundled.

The UA-2 Desc requirement's EmbeddedFiles name-tree membership boundary
was cross-checked against the same CC BY 4.0 UA-2 profile, 8.14.1-1.
The implementation replaces the EF-presence proxy with bounded tree
inspection. Null EF handling follows primary ISO 32000-1 7.3.9; four
measured filename/null reference disagreements remain explicitly recorded.
No profile XML, runtime or external PDF artifacts are bundled.

The UA-2 at-most-one-widget-per-Form fact was cross-checked against the
same CC BY 4.0 UA-2 profile, 8.10.1-2. Its existing direct OBJR counting
now uses namespace-aware Form resolution; no new grouping or descendant
count semantics are derived. No profile XML or external PDFs are bundled.

The UA-2 unconditional XFA-presence exclusion was cross-checked against
the same CC BY 4.0 UA-2 profile, 8.10.1-3. The implementation uses resolved
non-null entry presence rather than the old dynamic-rendering proxy.
Indirect-null/nonexistent-reference handling follows primary ISO 32000-1
7.3.9 and deliberately differs from six measured reference results.
No XFA packet-validation or complete PDF/UA coverage claim is made.

The UA-2 widget enclosing-Form requirement and Artifact exception were
cross-checked against the same CC BY 4.0 UA-2 profile, 8.10.1-1.
`ValidationPdfUA.cs` and `ValidationPdfUA2.cs` independently implement
namespace-aware Form resolution and shared Artifact ancestry inspection.
Nonfield-widget classification remains incomplete; no profile XML or
reference-engine source is bundled.

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

## PDF Association PDF 2.0 errata facts

The bounded structure Ref shape check in `ValidationPdfUA2.cs` follows
the factual array-type and no-null-element constraints in the official
[ISO 32000-2:2020 errata, 14.7.2 Table 355](https://pdf-issues.pdfa.org/32000-2-2020/clause14.html#H14.7.2).
The checks were independently implemented; no standard text is bundled.
The pinned veraPDF UA-2 profile accepts the three malformed native cases
that these primary-based checks reject. No reference-engine predicate
mapping or full target-identity conformance is claimed.

The bounded structure K-array null-child check follows Tables 354/355
at the same official errata source. It shares the independently implemented
Ref array scan; no standard text or external control PDFs are bundled.
All fifteen native combined-profile jobs accepted the controls, while
ten null/dangling cases now reject against the primary requirement.
These are explicit primary/reference disagreements, not a reference
family mapping or complete child shape/identity conformance.

The direct A/C array null-item checks follow the attribute/revision and
class-name/revision constraints in Table 355 at the same official source.
The independent bounded K/Ref scan is reused without changing owned lookup.
All ten combined-profile native jobs accepted the controls; six primary
null/dangling negatives now reject. These disagreements are explicit,
not a reference family mapping or full attribute/revision conformance.

The bounded structure R type/sign check follows Table 355's non-negative
integer fact at the same official source. It uses independently implemented
reference resolution. All eight corrected native combined-profile jobs
accepted the controls; four primary negatives now reject. No reference
predicate mapping or full revision/inheritance conformance is claimed.

The direct element NS dictionary check follows Table 355's indirect-reference
fact at the same official errata source. It is independently implemented
without changing role resolution. Four native positive reference jobs completed
normally; both direct-dictionary negative jobs threw reference exceptions.
Those outcomes are unavailable verdicts, not agreement or reference predicate
mapping. Root namespace registration and full namespace shape remain separate.

The parsed-element namespace registration check follows the dictionary
membership facts in Tables 354/355 at the same errata source. Dictionary
identity comparison and bounded inspection are independently implemented.
All six native reference jobs accepted the controls; four primary negatives
now reject. Two corpus files gain diagnostics absent from the reference's
other-rule rejections. These differences are explicit, not a reference
predicate mapping or complete namespace/traversal conformance.

The structure-root K shape check follows Table 354's dictionary or array of
dictionaries fact at the same errata source. It reuses independently written
bounded inspection. Nine corrected native controls have two retained
positives and seven primary negatives; six newly rejected files remain
accepted by the reference. Those differences are explicit. Full structure
dictionary identity/schema and element K shape are not established.

The element K nonempty-array check follows Table 355's minimum child count
at the same errata source, using independently written bounded inspection.
Five native controls retain three positives and reject two primary negatives
accepted by the reference. Two corpus files gain empty-child diagnostics
absent from the reference's other-rule rejections. Those differences are
explicit. Optional null/omission, root array policy and full element-child
type/schema are not expanded.

The element C declared-type check follows Table 355 and 14.7.6.2 at the same
errata source, using independently written bounded resolution. Fifteen
native controls retain eight positives and reject seven primary negatives
accepted by the reference; those differences are explicit. Undefined
classes remain empty. Complete array-item syntax, class revision ordering
and ClassMap attribute objects are not established.

The repeated attribute precedence correction follows the later-entry and
local A-over-C facts in 14.7.6.1/14.7.6.2 at the same errata source. Lookup
is independently implemented and bounded. Twelve native list controls
change eight reversed primary outcomes while the reference retains the
previous first-wins output. These disagreements in both verdict directions
are explicit; reference confirmation is not claimed. Full owner/schema,
stream, revision, inheritance and NSO semantics remain separate.

The attached attribute-owner check follows the mandatory O-name fact in
14.7.6.1 Table 360 at the same official errata source. Dictionary/stream
owner inspection and shared bounded local/used-class traversal are
independently implemented without decoding attribute stream data.
Sixteen native controls retain six positives and reject ten primary
negatives accepted by the reference; those disagreements are explicit.
No standard text or external controls are bundled. NSO namespace,
semantic owner validity, complete attribute schema, stream interpretation,
revision and inheritance requirements remain separate.

The NSO attribute namespace-presence check follows the conditional required
NS-entry fact in 14.7.6.1 Table 360 at the same errata source. It reuses
independently implemented bounded owner traversal and reference resolution.
Twenty-six native controls retain eight positives and reject eighteen
primary absence/null negatives accepted by the reference; these differences
are explicit. Namespace type/schema, owner identity/registration, stream
interpretation and complete attribute conformance are not derived from
these controls. No standard text or external controls are bundled.

The default RoleMap declared-shape check follows the dictionary/single-name
target facts in 14.7.3 at the same official errata source. Raw mapping
inspection is independently implemented with explicit reference/work
bounds, without changing the parsed role resolver. Twenty native controls
retain eight positives and reject twelve primary negatives accepted by
the reference; these disagreements are explicit. Global target semantics,
RoleMapNS schema and parser-wide bounds are not established. No standard
text or external controls are bundled.

The page structure-parent exclusivity check follows the at-most-one-key
fact in 14.7.5.4 Table 359 at the same official errata source. It is
independently implemented with bounded reference resolution. Six native
controls have matching combined-reference verdicts; negative reference
reports cite untagged content rather than an explicit exclusivity check,
so exact predicate agreement is not claimed. All-object coverage,
parent-tree identity and key types remain separate. No standard text or
external controls are bundled.

The page-annotation structure-parent exclusivity extension follows the
same Table 359 fact. Raw Annots traversal, dictionary-identity deduplication
and bounded key resolution are independently implemented, including
annotations exempt from other tagging checks. Six native controls retain
three positives and reject three primary negatives accepted by the
reference; those differences are explicit. XObjects/appearances and
complete key type/identity remain separate. No standard text or external
controls are bundled.

The resource Form/Image XObject structure-parent check follows the same
Table 359 exclusivity fact. Inherited/nested resource-graph traversal,
identity deduplication and explicit shared bounds are independently
implemented without decoding stream data. Twelve native unused-XObject
controls retain six positives and reject six primary negatives accepted
by the reference; these disagreements are explicit. Appearances, patterns,
other stream references and full key type/identity remain separate.
No standard text or external controls are bundled.

The annotation appearance structure-parent check follows the same
Table 359 exclusivity fact. AP N/R/D direct/state inspection, shared
bounded annotation traversal and stream-identity deduplication are
independently implemented without decoding data. Twelve native controls
retain six positives and reject six primary negatives accepted by the
reference; these disagreements are explicit. Appearance resource graphs,
patterns, soft masks and full key type/identity remain separate.
No standard text or external controls are bundled.

The nested appearance resource extension follows the same Table 359
exclusivity fact. Its shared iterative Form/Image resource traversal and
combined appearance work bounds are independently implemented without
decoding data. Eight native controls retain four positives and reject
four primary negatives accepted by the reference; these disagreements
are explicit. Patterns, soft masks, other stream references and complete
key type/identity remain separate. No standard text or external controls
are bundled.

The structure-parent integer type check follows the official 14.7.5.4
paragraph's integer-key requirement and Table 359's StructParents integer
type. Existing bounded page/annotation/appearance/resource traversal is
independently reused; no key ranges or parent-tree identity are inferred.
Thirty-nine native controls contain thirteen integer controls (twelve
retained clean positives) and twenty-six type negatives. Three of these
controls already have PrinterMark errors. Twenty-three type negatives
remain reference-accepted; reference
negative predicates are recorded separately from type agreement. No
standard text or external controls are bundled.

The class-array item check follows the official 14.7.6.2 requirement
for class names possibly accompanied by revision numbers. Independently
implemented bounded raw C inspection rejects only non-name/non-number
items; numeric revision integrality, ranges and ordering are not inferred.
Eighteen native controls retain eight name/numeric positives and reject
ten primary negatives accepted by the reference; these disagreements are
explicit. ClassMap schema and complete element K types remain separate.
No standard text or external controls are bundled.

The terminal text-field V type check follows the official PDF Association
12.7.5.3 errata's non-null text string/stream requirement. Existing bounded
inherited value traversal and rich equality are independently reused.
Twenty native controls include five retained clean positives, seven newly
rejected no-RV negatives accepted by the reference and existing rich-value
negatives whose diagnostics are retained; these differences are explicit.
Full field schema/traversal and appearance semantics remain separate.
No standard text or external controls are bundled.

The inherited terminal text-field MaxLen check follows the official
12.7.5.3/Table 232 non-negative integer requirement, added by ISO-approved
PDF Association errata issue 191. Bounded inherited lookup is independently
reused; metadata validation does not enforce text length or infer layout.
Twenty native local/inherited controls retain six positives and reject
fourteen primary negatives accepted by the reference; these disagreements
are explicit. Full field-reader/schema and appearance semantics remain
separate. No standard text or external controls are bundled.

The default appearance DA type check follows the official PDF Association
Table 224/Table 228 byte-string requirements. Independently reused bounded
lookup checks non-null AcroForm defaults and inherited terminal Tx values
without decoding/executing them. Twenty-four native local/inherited/
document controls retain six positives and reject eighteen primary
negatives accepted by the reference; these disagreements are explicit.
Required presence, operator grammar, resource matching and layout remain
separate. No standard text or external controls are bundled.

The default appearance text-matrix count check follows the official
PDF Association 12.7.4.3 at-most-one-Tm requirement. It independently
reuses the existing bounded content lexer and inherited default lookup.
Eighteen native local/inherited/document controls retain fifteen positives
and reject three primary negatives accepted by the pinned reference;
these disagreements are explicit. The permissive lexer does not prove
complete DA grammar; inline-image/budget/depth uncertainty remains
explicit. No standard text or external controls are bundled.

The default appearance text-font presence check follows the operator-
presence part of the same official 12.7.4.3 minimum Tf requirement.
Existing independent lexer/traversal work is reused; absence is not
claimed after an incomplete scan. Eighteen native controls retain six
positives and reject twelve primary negatives accepted by the pinned
reference; disagreements are explicit. Font/size operands, font-resource
matching, complete syntax and appearance generation remain separate.
No standard text or external controls are bundled.

The default appearance font-resource matching check follows the same
official 12.7.4.3 requirement to match Tf font names against AcroForm
DR/Font. Independently implemented operand tracking and bounded resource
lookup retain explicit uncertainty for malformed operands or unknown
resources. Twenty-four isolated native controls retain nine positives and
reject fifteen primary negatives accepted by the pinned reference.
Three already-negative corpus fixtures gain a missing-default-/Helv
finding without verdict changes. Full font schema, strict grammar and
layout remain separate; no standard text or external controls are bundled.

The default appearance Tf operand-count check follows the same official
12.7.4.3 two-operand requirement, independently reusing the existing capped
operand counter. Twenty-one native controls retain nine positives; twelve
already-negative resource-incomplete controls gain a precise count error
and remain accepted by the pinned reference. Full operand-type/numeric/
operator grammar remains separate. No standard text or external controls
are bundled.

The choice-field I array-shape check follows official PDF Association
12.7.5.4/Table 234 integer, ascending, zero-based index facts. It is
independently implemented with existing bounded reference/field traversal.
Eleven native controls retain five shape positives and reject six primary
negatives accepted by the pinned reference; disagreements are explicit.
Presence, Opt range, duplicate-selection semantics and V matching remain
separate. No standard text or external controls are bundled.

The signature-lock Fields raw string-array and conditional-presence check
follows official PDF Association 12.7.5.5/Table 236 facts. It independently
reuses bounded references and recognized terminal field traversal.
Twenty-one native controls retain seven shape/presence positives and reject
fourteen primary negatives accepted by the pinned reference; disagreements
are explicit. Encoding/name identity, complete schema and signing/locking
enforcement remain separate. No standard text or external controls bundled.

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
