# ISO 32000-2 parity backlog

This backlog expands every incomplete status in [spec-matrix.md](spec-matrix.md), plus known limitations recorded under rows currently marked Done. Full spec parity is the target. “Safe support” for PDF JavaScript, XFA, RichMedia, and related executable content means parsing, preserving, and exposing the structures without running embedded code.

The current matrix has **15 Partial rows and 0 Todo rows**. Two additional rows marked Done (redaction and signatures) document limitations that still need follow-up. The benchmark source and checked-in results remain in `bench/FreeDF.Bench`; they are repository artifacts, not a separately published NuGet package.

## 2026-11-04 PDF/A standalone PostScript XObjects

The existing all-object dictionary scan now rejects streams with resolved
`/Type /XObject` and `/Subtype /PS` through `PDFA-XOBJECT-POSTSCRIPT`.
The clause is ISO 19005-1 6.2.7 or ISO 19005-2/3 6.2.9. The pinned
veraPDF validation profiles at commit
`c4f57effb0d3618827c87bd71882930d752be6e3` express the prohibition as
`PDXObject`, test 1 for PDF/A-1 or test 3 for PDF/A-2/3:
"A conforming file shall not contain any PostScript XObjects."
Form `/PS` and `/Subtype2 /PS` checks remain separate and unchanged.

There are 195 authored cases across PDF/A-1b/2b/3b: direct and indirect
Type/Subtype names, painted and unused objects, non-stream dictionaries,
other/missing types, other/string/null subtypes and finding deduplication.
The initial 96-case set had 12 failures before the implementation.
Fifteen external controls were independently mutated from the retained clean
image PDFs and checked with veraPDF 1.30.3. Nine painted PS negatives,
including indirect Type/Subtype names, and three image positives agree on
the exact rule and whole-file verdict. The three unused PS controls are
**explicit disagreements**: veraPDF accepts them, whereas FreeDF's
all-object scan rejects them under the file-wide prohibition. They remain
in the retained XML/JSON evidence, not excluded or counted as agreement.
These controls establish 12/15 agreement, not reference equivalence.

This adds a typed-stream prohibition, not general XObject well-formedness,
PostScript execution or a claim that malformed/missing-Type objects are
covered. No external PDFs/XML or executable PostScript are bundled.
The integrated Release suite passed 4439 tests with none failed or skipped.

## 2026-11-03 full PDF/UA corpus remeasurement

All 435 public PDFs at veraPDF-corpus commit
`bb75f4f0073d9350dfd058c0162a367e6fadf25e` were downloaded and verified
against their Git blob SHA-1 and byte length: 297 UA-1 and 138 UA-2 files.
FreeDF validation code `46e8daa26f3cbde9eb84e3e56c1c8cdfbc16e289`
was compared with veraPDF 1.30.3 on JDK 25.0.5.8. Raw JSON/XML, logs,
the manifest and assembly/runtime SHA-256 values are retained as session
artifacts, not bundled external data. The corrected repository tool below
independently reproduces the same whole-file results.

| Reference profile | Total / compared | Both pass | Both fail | Reference fail / FreeDF pass | Reference pass / FreeDF fail | Excluded |
|---|---|---|---|---|---|---|
| UA-1 | 297 / 297 | 141 | 142 | 14 | 0 | 0 |
| UA-2 standalone, explicit XML | 138 / 138 | 54 | 75 | 9 | 0 | 0 |
| UA-2 + ISO 32005, explicit XML | 138 / 138 | 54 | 75 | 9 | 0 | 0 |

Profile selection matters: **veraPDF 1.30.3 `-f ua2` selects the combined
profile**, byte-identical to the pinned combined XML, not the standalone
92-rule XML. The standalone source has no Table 5 rules. Both explicit files
come from validation-profiles commit `c4f57effb0d3618827c87bd71882930d752be6e3`:
`PDF_UA/PDFUA-2.xml` (SHA-256
`9ca42e3f19474bf3eb2dbf253a223eed491a979921c89d74b6314e0d4c7cd782`)
and `PDF_UA/PDFUA-2-ISO32005.xml` (SHA-256
`7f7bb7be37c7f89026ce4e5733341d339a92d8cb004349055aac685d1765655b`).
The latter contains 92 UA-2 plus 1636 Table 5 rules. Its only reported
Table 5 failure in this UA-2 corpus is `StructTreeRoot-Document` in
`8.2.5.2-t01-fail-a.pdf`; identical file verdicts do not establish exhaustive
containment coverage or eliminate the deferred 55 grouping-dependent rules.

Eight UA-2 role-mapping negatives report `PDFUA2-NESTING-INCOMPLETE` alongside
the independent missing/circular-role errors: `8.2.4-t01-fail-a/b/c`,
`t02-fail-a/b/c`, `t03-fail-b`, and `t04-fail-a`. They remain negative
whole-file comparisons, not complete nesting checks or hidden exclusions.
No FreeDF file-parse failures, `VALIDATION-INTERNAL` warnings or abnormal
reference jobs occurred. Family-ID overlap remains weaker than exact-predicate
evidence: the per-rule output still contains unmapped rules and cases rejected
only through other findings.

The remaining whole-file misses are:

| Area | UA-1 clause / files | UA-2 clause / files |
|---|---|---|
| CIDFonts | 7.21.3.2 / 2 | 8.4.5.3.2 / 2 |
| CMaps | 7.21.3.3 / 1 | 8.4.5.4 / 1 |
| Subset embedding | 7.21.4.2 / 3 | None in this run |
| Font metrics | 7.21.5 / 1 | 8.4.5.6 / 1 |
| Character encodings | 7.21.6 / 1 | 8.4.5.7 / 1 |
| Unicode maps | 7.21.7 / 3 | 8.4.5.8 / 3 |
| `.notdef` use | 7.21.8 / 1 | 8.4.5.9 / 1 |
| Widget description | 7.18.1 / 1 (`t03-fail-d`) | None in this run |
| Table header connection | 7.5 / 1 (`t01-fail-c`) | None in this run |

Reproduce after installing veraPDF and setting `VERAPDF`/`JAVA_HOME`:

```powershell
python tools\ua_crosscheck.py corpus <corpus>\PDF_UA-1 1 --misses --strict
python tools\ua_crosscheck.py corpus <corpus>\PDF_UA-2 2 --profile <profiles>\PDF_UA\PDFUA-2.xml --misses --strict
python tools\ua_crosscheck.py corpus <corpus>\PDF_UA-2 2 --profile <profiles>\PDF_UA\PDFUA-2-ISO32005.xml --misses --strict
```

The old tool failed on this host before running UaDump: its UA-1 command was
43,343 characters (`WinError 206`). It now batches quoted UTF-16 command lines
below 24,000 units, builds only the first batch and aborts on any failed batch
instead of publishing a partial matrix. It prints the actual reference profile,
preserves per-file parse diagnostics and excludes incomplete internal checks.
Fifteen standard-library regressions run in the existing `node` CI chunk.
No production validation behavior or public profile is changed by this
remeasurement. The older dated totals below remain historical; these new
14/9/9 misses are not a claim of full rule coverage or PDF/UA certification.

## 2026-11-04 PDF/UA embedded CIDFont mapping entries

The embedded CIDFontType2 check now resolves `/CIDToGIDMap` and accepts only
a stream or the name `/Identity`. Merely having a key no longer accepts
`/NoIdentity`, an empty name, null, numbers, strings, arrays or dictionaries.
Indirect entries follow the same rule, including unresolved/cyclic references.
The existing nonembedded-font and CIDFontType0 exemptions remain unchanged.
The finding is still `PDFUA-FONT-CIDTOGID`; its UA-2 clause is corrected to
8.4.5.3.2 (UA-1 remains 7.21.3.2). This verifies the dictionary entry's
representation, not stream decoding/length, CID/glyph coverage, font-program
validity or actual glyph use. PDF/A behavior is unchanged by this slice.

All eight pinned public CIDFont controls agree on the exact veraPDF 1.30.3
rule: six negatives and two positives, zero exclusions. Four formerly missed
negatives (`t01-fail-a/c` in each profile) now fail the intended finding.
The retained reference reports from the full remeasurement above were compared
against fresh FreeDF results for all 435 SHA-verified PDFs; exactly these four
whole-file verdicts change and no public positive changes.

| Reference profile | Compared | Both pass | Both fail | Misses | False positives | Excluded |
|---|---|---|---|---|---|---|
| UA-1 | 297 | 141 | 144 | 12 | 0 | 0 |
| UA-2 standalone, explicit XML | 138 | 54 | 77 | 7 | 0 | 0 |
| UA-2 + ISO 32005, explicit XML | 138 | 54 | 77 | 7 | 0 | 0 |

`PdfUaCidToGidTests` adds 112 rule-specific cases for both profiles,
embedded/nonembedded Type0/Type2 descendants and direct/indirect/null/invalid
map values. Twenty-one fail before the fix (including the old UA-2 clause).
Synthetic font streams exercise the validator and are not conformant real
fonts. The baseline 14/9/9 totals above remain dated evidence; remaining font,
widget, table, grouping/sequence and semantic gaps stay open under #11.

## 2026-11-04 PDF/UA named encoding CMap references

`PDFUA-FONT-CMAP-REFERENCE` rejects a non-predefined name in `/UseCMap`
of an embedded Type0 encoding CMap dictionary. Indirect names are resolved;
Identity-H/V and the existing PDF standard CMap vocabulary remain accepted.
Clauses are ISO 14289-1 7.21.3.3 and ISO 14289-2 8.4.5.4 (profile test 3).
This is not a general CMap evaluator: program-level `usecmap`, stream-valued
parents, ToUnicode references, inheritance and malformed-reference completeness
remain open. No external CMap is loaded or executed.

All four pinned public `t03` CMap controls agree on the exact veraPDF rule
(two negatives/two positives, zero excluded). Both former missed negatives
reference `/Adobe-Korea1-2`; the positives reference `/H`. Forty-eight
authored cases cover eight accepted names and four rejected names, both UA
profiles and direct/indirect representation; sixteen fail before the fix.
Their synthetic CMap/font data establishes only the selected check.

Fresh results for all 435 verified corpus files against the retained explicit
reference profiles change exactly these two verdicts relative to the CIDFont
checkpoint: UA-1 has 145 both-fail, 141 both-pass and 11 misses; both UA-2
profiles have 78 both-fail, 54 both-pass and 6 misses. No false positives or
exclusions arise, and no positive changes. Family-rule overlap mappings in
`ua_crosscheck.py` name the new finding, not exhaustive predicate coverage.
Earlier measurements remain dated; remaining scope stays open under #11.

## 2026-11-04 PDF/UA-1 terminal field descriptions

For widgets indexed through the AcroForm field tree, the UA-1 description
check now reads `/TU` from the actual terminal field, not a widget-only
dictionary or a nonterminal ancestor. A field without a nonempty tooltip
still passes when each visible widget has `/Alt` on its own enclosing
structure element. Hidden/outside-CropBox exemptions and merged field/widget
dictionaries are preserved. UA-2 is unchanged. The existing bounded field-detail
walker supplies ownership; truncation or multiple terminal owners reports
`PDFUA-FORM-INCOMPLETE` as an Error rather than a complete check.

All ten public UA-1 `7.18.1-t03` controls match the exact veraPDF rule
(four negatives/six positives, zero excluded). The previously missed
`t03-fail-d` has tooltips only on its two widget dictionaries, not on their
shared terminal field. Sixty-four authored cases cover tooltip placement,
null/empty/indirect text, per-widget Alt, hidden widgets, ambiguous owners
and the depth-64/65 boundary; ten of the initial 61 fail before the fix.
ISO 32000-1 Table 220 (printed page 433, PDF page 441) defines `/TU` on the
field without the inheritable designation. This checks presence/ownership,
not the description's human usefulness.

All 435 verified corpus files were compared again with the retained reports.
Exactly one UA-1 whole-file verdict changes relative to the CMap checkpoint:
146 both-fail, 141 both-pass, 10 misses; both UA-2 profiles remain at
78 both-fail, 54 both-pass, 6 misses with identical error/warning rows.
There are no false positives/exclusions or changed public positives.
Unindexed/malformed field graphs retain the existing fallback and are not
claimed as complete ownership validation. No PDF is bundled; remaining
font/table/grouping/semantic work stays open under #11.

## 2026-11-04 PDF/UA symbolic TrueType cmap records

Simple symbolic TrueType fonts with an embedded `/FontFile2` now have their
sfnt directory and `cmap` encoding records inspected. The requirements differ:
UA-1 (7.21.6, profile test 4) permits exactly one encoding of any platform
or the presence of Microsoft Symbol (3,0); UA-2 (8.4.5.7, test 4) requires
Microsoft Symbol (3,0) or Macintosh Roman (1,0), regardless of count.
`PDFUA-FONT-SYMBOLIC-CMAP` reports the missing required records.
Bounds cover directories, table lengths, encoding-record counts and subtable
offsets; malformed/unsupported headers or stream filters produce the explicit
Error `PDFUA-FONT-CMAP-INCOMPLETE`, not a successful symbolic-encoding check.
A well-formed directory with no `cmap` has zero encodings and fails the rule.

Twenty-six authored cases include profile-specific positive/negative record
sets, missing tables, truncated/oversized headers, unsupported filters and
non-symbolic/nonembedded/FontFile3 scope controls. Eleven of the initial
22 cases fail before the fix. Ten mutated public-font PDFs independently
agree with veraPDF 1.30.3 on the exact profile test: four rule failures and
six rule positives. In particular, a single Microsoft Unicode (3,1) record
passes UA-1 but fails UA-2; a pair containing Macintosh Roman without Symbol
fails UA-1 but passes UA-2. These are isolated font-rule controls, not ten
wholly conformant PDF/UA documents. Raw reports and mutations are retained as
session artifacts, not bundled.

Fresh results on all 435 verified corpus files preserve every whole-file
verdict relative to the field-description checkpoint: 10 UA-1 and 6 UA-2
misses, zero false positives/exclusions, and no incomplete font headers.
The pinned profile facts above and Microsoft's public OpenType
[`cmap` header/encoding-record specification](https://learn.microsoft.com/en-us/typography/opentype/spec/cmap)
define the inspected metadata. This does not validate subtable mappings,
glyph programs/checksums, actual glyph lookups, non-symbolic rendering use,
FontFile3 OpenType programs or TrueType collections. Remaining character
encoding misses concern `/Differences` glyph names, not these symbolic record
requirements. Font/glyph and other PDF/UA parity gaps remain open under #11.

The final integrated Release suite passes 3958/3958, with a successful solution
build (two machine-feed NU1507 warnings) and 15 comparison-tool regressions.
An earlier integrated run had one fuzz failure; it did not reproduce alone,
with the new invocation disabled (3932/3932), or in the restored final suite.
The first quiet log does not establish its cause; no deterministic cause or
unrelated-failure attribution is claimed. Diagnostic final/control TRX files
are retained.

## 2026-11-04 PDF/UA TrueType Differences glyph names

Non-symbolic TrueType `/Differences` names are now checked against the
literal Adobe Glyph List, with reserved `.notdef` accepted separately.
Names such as `/uni0060`, `/u0060`, `/grave.alt` and `/grave_grave` remain
supported by the extraction algorithm, but do not meet this literal-name
validation requirement. `PDFUA-FONT-TRUETYPE-DIFFERENCES` reports violations
under UA-1 7.21.6 / UA-2 8.4.5.7, profile test 2. Symbolic and nonembedded
cases retain their respective rules; this name requirement does not depend
on embedding. The related existing encoding finding now cites UA-2 8.4.5.7
instead of the old broad 8.8 citation.

The existing internal glyph-list reader and both exact official resources
move from Content to Core so Document can reuse literal membership without
a circular dependency, duplicate tables or a new public API. Content retains
the same lookup algorithm. Resource SHA-256/count and all 4,281 official
name-to-text mappings remain pinned; the generation script targets the new
location. Adobe's embedded notices and data bytes are unchanged.

There are 190 authored cases: 160 direct/indirect name tests across both
profiles, symbolic/non-symbolic and embedded/nonembedded fonts (40 red before
the fix), plus 30 malformed/empty/null/budget-boundary cases. Invalid array
syntax/types/byte-code ranges or more than 10,000 entries produce Error
`PDFUA-FONT-ENCODING-INCOMPLETE`; optional null values, including missing
indirect references resolved as PDF null, retain absent-entry semantics.
Twenty mutated public-font controls agree with veraPDF on exact test 2
(ten rule positives/ten negatives), including the distinction between literal
and algorithmic names and the `.notdef` exception. These controls do not
establish whole-file conformance or usefulness of extracted Unicode.

The full 435-file differential closes exactly the two public `t02-fail-d`
character-encoding misses relative to the symbolic-header checkpoint.
UA-1 now has 147 both-fail / 141 both-pass / 9 misses; both explicit UA-2
profiles have 79 both-fail / 54 both-pass / 5 misses. No public positive
changes, false positives, exclusions or incomplete encoding scans occur.
Embedded-program Microsoft Unicode (3,1) presence for Differences,
non-symbolic glyph lookup, subset coverage, widths, used-character Unicode,
`.notdef` use, table headers and other remaining parity gaps stay open.
Mapped family IDs in the comparison tool are not complete predicate coverage.

After integrating upstream PNG fixes and the delivered dictionary/field
corrections, the full Release suite passes 4164/4164 (zero skipped), the
solution build has zero errors/two machine-feed NU1507 warnings, and all
15 comparison-tool regressions pass.

## 2026-11-04 PDF/UA literal CMap writing modes

The embedded Type0 encoding-CMap `/WMode` comparison no longer uses a regex.
A bounded token scan reads literal `/WMode 0 def` or `/WMode 1 def` definitions
(including `+0`/`+1`), ignores comments/strings/array literals, and uses the
last definition. Missing/null dictionary values default to zero, as specified
by ISO 32000-1 Table 120 (printed page 277, PDF page 285). With no inherited
CMap or explicit program definition, the program default is also zero.
PostScript names are literal: `/W#4Dode` is not `/WMode`, unlike PDF name
escape syntax. No external CMap or PostScript code is executed.

`PDFUA-FONT-CMAP-WMODE` remains the mismatch finding, now with the exact UA-2
8.4.5.4 citation (UA-1 7.21.3.3, both profile test 2).
`PDFUA-FONT-CMAP-WMODE-INCOMPLETE` is an Error for invalid dictionary values,
non-literal/procedural definitions, inherited mode without an explicit
override, malformed tokenization, unsupported filters, or the 16 MiB /
one-million-step / procedure-depth-64 limits. The scanner is not an
arbitrary PostScript interpreter, full CMap evaluator or inheritance resolver.

Eighty authored cases cover lexical definitions, redefinitions, dictionary
defaults/indirection, false matches and explicit incompleteness/scan limits.
Forty-six of the initial 78 cases fail before the fix. Twenty-eight mutated
public-font controls independently agree with veraPDF 1.30.3 on exact test 2:
14 rule negatives and 14 rule positives, including signs, comments, strings,
dictionary/program absence and final-definition precedence. These controls
do not establish whole-file conformance. All 435 verified corpus verdicts
remain unchanged relative to the Differences checkpoint: 9 UA-1 / 5 UA-2
misses, zero false positives/exclusions, and no incomplete WMode scans.
Raw reference reports, mutations and hashes remain session artifacts.
Actual glyph mappings, program semantics and other open parity gaps remain.

The integrated Release suite passes 4244/4244, zero skipped; the solution
build has zero errors/two machine-feed NU1507 warnings, and all 15
comparison-tool regressions pass.

## PDF/A image/Form dictionary policies

Image XObjects now reject non-null `/Alternates` and `/OPI`, and Form XObjects
reject non-null `/OPI` or `/PS` and explicit `/Subtype2 /PS`. Image
`BitsPerComponent`, when present/non-null, must be an integer in 1/2/4/8 for
PDF/A-1b or 1/2/4/8/16 for PDF/A-2b/3b; an image mask permits only 1.
Absent/null BPC is not rejected by these conditional rules (including the JPX
omission case); general required-field/filter-sample consistency is separate.
Null and indirect-null banned keys retain ordinary absent-entry semantics.
Each rule emits at most one finding per document. No PostScript is executed.

Clauses: image rules use ISO 19005-1 6.2.4 or ISO 19005-2/3 6.2.8.
Form OPI uses 19005-1 6.2.4; Form PS/Subtype2 uses 19005-1 6.2.5;
both use 19005-2/3 6.2.9. The pinned v1.30.3 profiles supply the corresponding
image tests 1/2/4/5 and Form test 1. Base-standard mask semantics are independently
confirmed in [ISO 32000-1:2008](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf),
8.9.5, the image-dictionary BPC entry on printed page 207 (PDF page 215):
an explicitly specified mask BPC must be 1, including standalone stencils.

Fifty-one independent controls are incremental derivatives of the same pinned
public image positive used below. They preserve the original source objects,
replace the page/image/Form dictionaries with a one-pixel unfiltered image or
Form wrapper, supply calibrated DefaultGray/DefaultRGB, and select matching
1b/2b/3b metadata. They cover clean dictionaries, null keys, Alternates, OPI,
Form PS/Subtype2, BPC 3/16 and standalone/attached mask BPC 1/2.
FreeDF baseline `6b7e6d39` missed all 22 failures reported by veraPDF 1.30.3.
The correction closes those 22 and agrees on 48/51 whole-file verdicts,
zero exclusions. Three explicit disagreements remain: veraPDF accepts standalone
two-bit stencils while rejecting equivalent attached two-bit masks at image
test 5; FreeDF rejects both, following the explicit base-standard one-bit
constraint. These are not hidden exclusions or a claim of perfect agreement.
FreeDF accepts 26 controls and rejects 25.

See `PdfAXObjectPolicyTests.cs`. Inline-image parsing, standalone PostScript
XObject bans, general image/Form validity and unresolved-reference completeness
remain open. Rendering is unchanged; no external PDF/ICC/report is bundled,
and historical corpus measurements are not replaced with these targeted totals.

## PDF/A painted-device-colour OutputIntents

DeviceGray does not require a Gray OutputIntent profile: any PDF/A OutputIntent
qualifies, subject to the separate profile-validity checks. Painted DeviceRGB
and DeviceCMYK instead require matching RGB and CMYK destination profiles unless
a supported device-independent default replaces that space.
`PDFA-OUTPUTINTENT-COLORSPACE` rejects mismatches at ISO 19005-1 6.2.3.3 or
ISO 19005-2/3 6.2.4.3. This corrects the former over-strict Gray assertion in
the tests/documentation; it is not a relaxation of the standard.

The new mismatch decision follows actual path/text/image-XObject painting,
including q/Q restoration, text rendering modes and stencil fill colour.
Unused resource entries, unpainted colour setters and invisible text do not
create a mismatch. Form invocation memoization distinguishes Gray/RGB/CMYK
and inherited resource contexts, so a prior invocation cannot hide later
painting under a different colour/default-space state.

Eighteen authored matching-profile controls use real ICC data extracted from
the pinned public image pass (RGB) and
`PDF_A-1b/6.2 Graphics/6.2.3.3 Uncalibrated color space/veraPDF test suite
6-2-3-3-t02-pass-a.pdf` (CMYK), corpus commit
`bb75f4f0073d9350dfd058c0162a367e6fadf25e`. Each 1b/2b/3b control has a
single Gray/RGB/CMYK painted rectangle and an RGB or CMYK OutputIntent,
matching XMP identification and a valid xref/trailer. veraPDF 1.30.3 accepts
twelve and rejects six for the exact RGB/CMYK rule. FreeDF baseline
`f2547e29` disagrees on twelve (six false rejections, six missed failures);
the correction agrees on all eighteen, zero exclusions.
See `PdfAOutputIntentColorTests.cs`. Synthetic unit-test ICC headers are not
the complete profiles used in these external controls.

This is not exhaustive colour-use analysis: shading/pattern usage,
binary-safe inline images, unsupported replacement spaces, general malformed
colour-space validation and traversal completeness remain open. The existing
no-OutputIntent resource/operator heuristic is not made actual-use-complete
by the new painted-colour mismatch check. No external PDF/ICC/report is bundled;
historical corpus totals remain unchanged.

## PDF/A default RGB/CMYK replacements

The device-colour scan now honors resource-local `/DefaultRGB` with CalRGB
or matching three-component ICCBased data, and `/DefaultCMYK` with matching
four-component ICCBased data. Existing CalGray/ICCBased `/DefaultGray` behavior
is retained. Malformed arrays, calibration parameters, ICC signatures/component
counts and cyclic aliases do not suppress the missing-OutputIntent finding.
Explicit colour operators, direct named device-space selection and image
XObjects use the replacement check. A shared image is checked in each page/Form
resource context, rather than allowing a mapped occurrence to hide an unmapped
one. This is still a bounded heuristic resource/content scan, not exhaustive
actual-use analysis or full ICC validation.

The public image-positive OutputIntent rejection recorded below is corrected.
Two public PDF/A-1b sources at corpus commit
`bb75f4f0073d9350dfd058c0162a367e6fadf25e` supply independent RGB controls:
the image pass below (ICCBased) and
`PDF_A-1b/6.2 Graphics/6.2.3.3 Uncalibrated color space/veraPDF test suite
6-2-3-3-t03-pass-b.pdf` (CalRGB). Equal-length `pdfaid:part` substitutions
produce matching-profile 1b/2b/3b controls; renaming `/DefaultRGB` to
`/DefaultXYZ` removes the replacement without changing xref offsets.
All twelve whole-file verdicts agree with veraPDF 1.30.3: six compliant
positives and six negatives failing the exact DeviceRGB rule
(1b 6.2.3.3-1; 2b/3b 6.2.4.3-2), zero exclusions.
CMYK coverage here is authored rule-specific controls, not an independent CMYK
comparison. See `PdfADefaultDeviceColorTests.cs`; its synthetic ICC headers
are not claimed to be complete ICC-positive files.

Lab/DeviceN replacements, binary-safe inline-image handling, exhaustive
colour-use/traversal completeness remain open. Basic painted-colour
OutputIntent matching is recorded above.
External PDFs/reports are not bundled; historical corpus totals are unchanged.

## PDF/A image-XObject interpolation policy

`PDFA-IMAGE-INTERPOLATE` rejects explicit `/Interpolate true` on image-XObject
streams, including image masks and indirectly stored Boolean flags, in PDF/A-1b,
2b and 3b. The clauses are ISO 19005-1 6.2.4 and ISO 19005-2/3 6.2.8.
False, absent and null flags do not trigger this rule; ordinary rendering and
its scoped interpolation policy are unchanged. Inline `/I`, non-Boolean values,
unresolved-reference completeness and general image validity remain open.

The public veraPDF corpus at `bb75f4f0073d9350dfd058c0162a367e6fadf25e`
provides `PDF_A-1b/6.2 Graphics/6.2.4 Images/veraPDF test suite
6-2-4-t03-pass-a.pdf` with an XObject `/Interpolate false`. An equal-length
replacement with `/Interpolate true ` preserves its xref offsets. These two
controls agree on the exact image rule with veraPDF 1.30.3 in all three profiles
(six comparisons, zero exclusions; profile rule 6.2.4-3 or 6.2.8-3).
At that checkpoint they were not clean matching-profile positives: FreeDF
rejected the public pass for `PDFA-OUTPUTINTENT-MISSING`, reproduced on exact
committed baseline `e9d3141c`; the PDF/A-2/3 runs also used PDF/A-1 source
metadata. The default-colour correction above supersedes that rejection, not
the historical measurement. The public
`6-2-4-t03-fail-a.pdf` instead uses inline `/I true` and remains a miss in
FreeDF. Keep that miss open under #5; neither corpus-wide agreement nor full
image-policy coverage is claimed.
External PDFs/reports are not bundled. See `PdfAImageInterpolationTests.cs`.

## 2026-11-01 named-destination consistency

Outline/link inspection and outline import now share cycle-aware named-destination
lookup. Merge previously stopped at depth 16, losing valid targets that inspection
could resolve; both paths now use the existing depth-64 bound. Regressions cover
20/64-level trees, cycles followed by valid siblings, dictionary-wrapped `/D`,
full/incremental preservation and imported page remapping. This closes a selected
12.3.2/12.3.3 editing gap, not complete destination conformance: raw byte-name
matching, arbitrary-depth trees, remote/embedded target resolution and general
catalog-preserving merges remain outside this slice. No action is executed.

## Editor-pipeline recovery and current-main integration

Main `6b474b18`, the binary/Blob pipeline and reviewed default-on worker pool
are integrated without changing the coverage counts. Release 2225/2225,
13 Node checks, fresh AOT, full/merge/forms/protection/pool/page-image/boot
and the 535-group profile gate pass. See
[the measured integration checkpoint](performance/2026-10-30-aot.md#current-main-pipeline-and-pool-integration).
The requested 5x budgets are not achieved. Remaining pipeline work includes
cold-replica exact-render admission, exact zoom latency, navigation wall time, and sample-26
performance measurements. Startup long tasks/repeat visits and further raster
work remain independent workstreams. All three delegated sessions returned;
none needs recreation. Startup's separate branch now includes per-client cache
pinning and real mid-boot update validation (`a7c88436`, `ef0a201d`):
service-worker, recovery, default and boot smokes pass on its trimmed non-AOT
build. That branch is not integrated here; its final AOT check remains open.

The document-scoped first-paint follow-up stops stale queued thumbnails on
replacement and uses the primary for low-resolution visible previews.
Image-open improves 384.0/399.5 -> 133.2/133.5 ms, with unchanged hashes/gates.
Typography zoom and Save's 12-ms target still need work; see the subsequent
measurement table rather than treating the active miss allowlist as full success.

## Latest reviewed five-branch slice

This checkpoint follows the local round-3 snapshot-reuse performance commits.
Six reviewed commits were cherry-picked in branch order, preserving authorship
and messages, without conflicts. Integrated focused coverage passes 14 actions/
details, 225 graphics/transparency/overprint, and 114 structure/file cases; the
first full Release suite passes **1949/1949**. This first-three checkpoint
precedes the ICC/redaction integration below.
Fresh AOT/full/merge/page-image/boot, the 535-group CI profile, all 15 fixed
hashes and ten synthetic policy controls pass. Startup (71/79 ms long task)
and showcase open (280.9/300.1 ms) remain strict-goal misses; no timing gate or
allowlist is changed. The generated large fixture independently matches the
reviewed report first-page hash.

| Clauses | What moved | Exact remaining scope / evidence limits |
|---|---|---|
| 12.5-12.7 | Typed inert remaining action subtypes, scope-specific additional-action events and link `/PA`; common/markup/subtype annotation metadata; inherited field details, flags/options and signature lock/seed metadata. Container work is charged per action root rather than globally starving unrelated roots. | Keep every original dictionary and unknown/malformed value. Bounded/truncated inspection is not execution, navigation, field reset/import, media playback, XFA processing, appearance parity or complete field/action conformance. See `ActionSubtypeFeatures.cs`, `AnnotationDetailFeatures.cs`, `FormFieldDetailFeatures.cs`, `SafeContentFeatures.cs`, `ActionAnnotationFormDetailTests.cs`. |
| 7.10, 8.7 | Actual Coons/tensor surface evaluation with bounded device-space tessellation; integer/real/boolean PostScript semantics; shading BBox/background; tiling and pattern-stroke fixes with hostile count guards. | Selected PDFium comparisons clean; finite tessellation and broad function/colour-space combinations remain open. Some reference channels change 1-6 levels due to finer ramps/geometry, not a blanket tolerance increase. See `MeshShading.cs`, `PostScriptFunction.cs`, `ContentInterpreter.cs`, `SoftwareRasterizer.cs`, `GraphicsGapTests.cs`. |
| 14.7-14.8 | Whole-object OBJR text for locally reached annotation appearances and Forms; page-scoped Form roots, MCR/Artifact duplicate suppression, parse/parent/duplicate/ParentTree diagnostics, one-pass bounded ParentTree index. | Still no arbitrary owner graphs, external streams, per-invocation tagging or full PDF/UA validation. ParentTree is required for content-bearing trees, not a blanket error for empty trees. See `Structure.cs`, `StructuredTextExtractor.cs`, `ObjectReferenceTextTests.cs`, `StructureValidationTests.cs`. |
| 7.9-7.11 | Bounded tree ordering/limits/duplicate/cycle/pair audits, collection folder/navigator metadata and embedded-file size/MD5 checksum diagnostics; embedded metadata strings are preserved across encryption/write paths. | Broader attachment/tree boundary and preservation combinations remain open; MD5 is only the specified checksum. No external files or executable payloads are opened. See `TreeDiagnostics.cs`, `DocumentFeatures.cs`, `DocumentFeatureTests.cs`, `FileSpecificationMetadataTests.cs`. |
| 8.6, 10 | ICC v4 perceptual reference black; LittleCMS-compatible implicit perceptual/saturation LUT BPC, explicit relative BPC, optional B2A/mBA destination conversion and standalone ncl2 inspection. Corrupt B2A cannot discard valid A2B rendering. D2Bx/B2Dx now read bounded cvst/curf, matf and float clut elements. | Not ISO 18619 estimation or a mandated PDF policy; ncl2 is not a valid named ICCBased renderer. Unsupported mpet elements (including bACS/eACS), device-link/abstract/N-colour transforms, partial observer adaptation, general gamut mapping/CMM and exhaustive group conversion remain open. See `IccProfile.cs`, `IccMultiProcess.cs`, `IccMultiProcessTests.cs`, `IccNamedColorProfile.cs`, `IccCmmTests.cs`, `IccLcmsReference.cs`; bounded LittleCMS 2.17 comparisons are not full CMM parity. |
| Editor redaction | Arbitrary paths/strokes/clips with rotated/sheared transforms, bounded curve flattening, per-invocation nested/shared Form copies, partial inline/stencil image blanking, hidden optional content and redacted-Form side-data removal. | Full rewrite only; unsupported/singular/over-budget geometry removes whole objects or empties clips. Content-only overload removes intersecting forms/images whole; use Apply for partial edits. Original content intentionally used outside the redaction can remain; glyph/outline extraction, geometry budgets and unsupported complex paint remain limitations. See `PathRedaction.cs`, `Redactor.cs`, `PdfEditor.cs`, `RedactionCoverageTests.cs`. |

The remaining-work rows below reflect these additions. All affected clauses
remain Partial; previous independent-evidence and unconfirmed PDF/A/PDF/UA-rule
caveats remain in force.

The final four reviewed ICC/redaction commits also integrate without conflicts.
Focused ICC **182/182**, redaction **66/66**, and the single final full Release
suite **2002/2002** pass with zero failures/skips. LittleCMS-style implicit
perceptual/saturation BPC is an explicit compatibility choice, not a PDF/ISO
requirement; black detection follows cmsDetectBlackPoint rather than ISO 18619.
Final fresh AOT/full/merge/page-image/boot, the 535-group regression gate, all
15 pixel hashes and ten synthetic policy controls pass. Colour-space sample
08 and the graphics fixture retain exact hashes at 41/137/240 dpi. Strict
misses are now startup 71/72 ms, showcase open 281.2/301.1 ms and reopened
cached typography navigation 103.1/103.1 ms. The latter is recorded explicitly
in the known-miss policy with its observation; numeric reference values remain
the existing CSV's 98.1/104.8 ms, so no regression ceiling is increased.

## 2026-10-30 integrated checkpoint

The seven child workstreams are integrated without rewriting their commits.
Initial focused Release tests passed: JBIG2 29, multimedia/3D 9, Type 1 24,
signatures/security 57, samples/reproducibility 38 and merge/generator interaction
36 (overlapping selectors). After review fixes, the focused merge/3D/DocMDP/
reproducible-write selector passes 67 cases and full Release passes 1696/1696
with no skips/failures. The solution build succeeds (four warnings emitted by
the latest incremental build; the six previously reported warning sites remain).
Full Chromium editor smoke
preserves text fidelity, recovery and desktop/mobile header behavior; separate
merge and page-image smoke pass on the integrated WASM output.

An integration regression verifies that a failed atomic import batch restores
the generator's prior weak source-object import maps as well as pages/objects.
The original three-argument `ImportPages` overload is retained for compiled
callers; the merge-options overload is additive, with a reflection regression
for both signatures.
Local File > Merge PDFs carries forms, outlines and page labels, not arbitrary
catalog structures. The generated full collection additionally preserves the
specific non-colliding sample structures described in `samples/README.md`.
It is not a generally lossless catalog-merging promise.

Remaining decoder/font/signature/clause-13 work is listed below. Historical
validation counts in later paragraphs describe earlier checkpoints. Browser
performance work has resumed: binary Blob upload preparation measured 120.4 ms
to 6.7 ms p50, and next-page navigation measured about 98 ms p50 after bounded
neighbor-page preview. The post-integration strict AOT profile reports misses
for startup maximum-long-task, showcase open, and cached typography
next-page presentation; the library benchmark rerun remains separate. These
gains do not imply full parity or close the remaining latency gaps.

## 2026-10-30 reviewed decoder, rendering, and validation integration

The additional PDF/A/PDF/UA, overprint/transfer-function, transparency-group,
and JPX/JPEG-12 branches are integrated locally. Focused Release selectors
passed: deep validation 37/37, overprint/transfer 18/18, transparency plus
overprint 134/134, and JPX/JPEG-12 148/148 (selectors overlap; do not sum).
The one final post-integration Release build/suite, fresh AOT publish, and
browser smokes have passed. Profile CI and deterministic pixel-hash gate
passed under the versioned relative policy, including all 15/15 first-page
pixel hashes. The profile reports three strict misses; the new cached-navigation
miss remains visible as `NEW GOAL MISS` and is not added to the two-entry
known-miss allowlist.

The added PDF/A checks cover ICC profile headers, XMP packet/extension metadata,
PDF/A-1 Info/XMP equivalence, font widths/encoding/CIDSystemInfo, form
NeedAppearances, and named actions. PDF/UA checks add document and optional
content flags, embedded-file names, font mappings, MCID/Artifact/content
tagging, and selected heading/table/list/TOC/note relationships. Rules were
compared by hand against published veraPDF profile XMLs; **veraPDF was not
run**, so these heuristics are not certification.

Evidence and scope limits: `PDFA-ICC-SIZE`, `PDFA-XMP-EXTENSION-INVALID`, and
`PDFA-FONT-CMAP-SYSTEMINFO` remain unconfirmed; `PDFUA-FONT-TOUNICODE` is a
heuristic, `PDFUA-BOOKMARKS` is advisory, and `PDFUA-LINK-STRUCT` is
unconfirmed. Overprint simulation is opt-in and RGB-approximate; halftone,
flatness, and smoothness remain inert, and some tests are self-consistent.
Page-group blending now covers CMYK/Cal/ICC, fill+stroke and text knockout,
and `/AIS`, but tests are analytic rather than Chrome/PDFium comparisons; the
CMYK non-separable K rule follows Adobe/Splash behavior rather than explicit
ISO text, and text-knockout storage is clip-bounded rather than text-bounded.
JPX subsampling fixtures are self-derived. 12-bit arithmetic DCT was
cross-checked against libjpeg-turbo 3.2.0 (maximum error 3) only for default
conditioning (L=0, U=1, Kx=5, no DAC segment); non-default 12-bit DAC remains
untested.

Independent-evidence limits: Huffman refinement fixtures are encoder-generated
and PDFium-cross-checked, not decodable by jbig2dec/pdf.js; non-byte-aligned
symbol widths and nonzero RDW/RDH still need independent references.
Multimedia rendition/clip traversal is capped at depth 64 and 1024 total nodes
per top-level read with memoization/`Truncated`, not unbounded graph support.
DocTimeStamp unit tokens are self-built, not issued by a TSA; pyHanko fixtures
provide independent interop evidence, while chain trust remains reported only.
3D detail traversal shares a 1024-work-item budget across one annotation read,
caps each stream at 64 views and each node/measure/section list at 256 entries,
memoizes streams/views by resolved object identity, and exposes `Truncated`
without discarding raw dictionaries/arrays. Regression fixtures use 500 shared
annotations, 100 views and up to 10000 shared detail entries.
Document timestamps reject unsupported hash names before invoking the provider.
Public-key seed randomness uses the validated fixture hook, but CMS recipient
envelopes still use platform randomness and are not byte-reproducible.
Merge keyboard/failure regressions now reach backend rejection, not just client
validation; the existing mid-import rollback test checks prior source import maps.

## Parser and document model

| Clause | Remaining work | Evidence and completion checks |
|---|---|---|
| 7.4 — Image filters | Remaining DCT: SOF11 arithmetic lossless, hierarchical JPEG, and subsampled interleaved lossless. 12-bit arithmetic SOF9/SOF10 is implemented for default conditioning (L=0, U=1, Kx=5, no DAC segment); fixtures cross-check with libjpeg-turbo 3.2.0 to maximum error 3, but non-default 12-bit DAC is untested. JBIG2 Huffman symbol refinement/aggregation has single-refinement and aggregate-text bitmap regressions; remaining: 12-AT extended templates, colour extensions, broader Huffman combinations, and MMR halftone coverage. JPX now covers >8-bit precision, bilinear subsampling, JP2 ICC, and code-block style/ROI fixtures in addition to packed headers and POC ranges; subsampling fixtures are self-derived and style/ROI coverage is not exhaustive independent parity. Continue multi-range/spatial-order POC and broader feature-combination tests. | `docs/spec-matrix.md`; decoder coverage in `tests/FreeDF.Tests/JpegTests.cs`, `JpegExtendedTests.cs`, `Jpeg12BitTests.cs`, `Jbig2Tests.cs`, `JpxTests.cs`, and `CcittTests.cs`. PPM/PPT and selected POC fixtures are cross-checked against OpenJPEG; preserve the explicit self-derived-fixture limits. |
| 7.10–7.11 — File specifications, embedded files, related structures | Inert file specs, recursive RF and AF owners, typed collection schema/view/sort/folders/navigators and opaque CI/FS/Thumb/EP remain available. Bounded name/number-tree diagnostics now cover ordering, limits, duplicates, malformed pairs and cycles; embedded-file diagnostics validate specified size/MD5 checksum, and metadata strings survive encrypted saves. Continue with broader tree-boundary, attachment and full/incremental preservation combinations. | `src/PortableDocument.Document/DocumentFeatures.cs`, `TreeDiagnostics.cs`; `tests/FreeDF.Tests/DocumentFeatureTests.cs`, `FileSpecificationMetadataTests.cs`, `EditorTests.cs`. Retain unknown raw dictionaries/streams; never execute payloads or open external targets. |
| 12 — Annotations, forms, and actions | Typed inert action subtypes, scope-specific triggers and link PA now supplement existing dictionary/Next traversal. Common/markup/subtype annotation metadata and inherited form-field flags/options/signature lock/seed/XFA details are exposed with explicit truncation. Continue with exhaustive object/trigger coverage, annotation appearances and complete field semantics. Prior GoToDp limitations remain: local indirect DPart dictionary only, no DPartRoot search, Start/End resolution or navigation. | `src/PortableDocument.Document/SafeContentFeatures.cs`, `ActionSubtypeFeatures.cs`, `AnnotationDetailFeatures.cs`, `FormFieldDetailFeatures.cs`; `tests/FreeDF.Tests/ActionAnnotationFormDetailTests.cs` plus existing action/editor/security tests. Prove opening, listing, saving and rendering do not execute actions, process XFA or access remote targets. |
| 14 — Tagged PDF and logical structure | Page/Form MCIDs and selected local annotation MCRs retain stream/owner scoping. Whole-object OBJR now extracts locally attributed annotation/Form text with bounded nested discovery, page-scoped roots and MCR/Artifact duplicate suppression. Added parse/parent/duplicate/ParentTree diagnostics and one-pass bounded indexing. Continue with external streams, arbitrary owner graphs, per-invocation tagging and independently checked malformed/reused-form logical order. Plain ExtractText/Analyze remain annotation-free; diagnostics are not accessibility certification. | `src/PortableDocument.Document/Structure.cs`, `src/PortableDocument.Render/StructuredTextExtractor.cs`, `src/PortableDocument.Content/ContentInterpreter.cs`; `tests/FreeDF.Tests/ObjectReferenceTextTests.cs`, `FormStructureTests.cs`, `StreamOwnerStructureTests.cs`, `StructureValidationTests.cs`. Add independently checked page/Form/appearance ordering and alternate-text fixtures. |
| 14.8 / PDF/A and PDF/UA — Validation | Added deep PDF/A checks for ICC profile header/version/class/space/components, XMP packet and extension metadata, PDF/A-1 Info/XMP equivalence, font widths/encoding/CIDSystemInfo, form NeedAppearances, named actions, and a bounded content scan for implicit initial DeviceGray text/path painting. DeviceGray checks honor calibrated page/Form `/DefaultGray` spaces and require a PDF/A OutputIntent when no usable default applies; its profile need not be Gray. Painted RGB/CMYK require matching profiles unless a supported default replaces the device space, as detailed above. Authored CalRGB, calibrated DefaultGray, and Type 3 text-with-DefaultGray fixtures pass veraPDF 1.30.3 for matching PDF/A-1b/2b/3b profiles; implicit/explicit Gray negatives fail the corresponding rules. These are narrow controls, not general font/ICC output-profile coverage; keep the validators heuristic and Partial. The historical repository-fixture comparison in [validation-compat.md](validation-compat.md) records PDF/A-1b/2b/3b verdict agreement on 551/551/549 comparable files; its older UA-1 results are not the later public-corpus results. The [2026-10-31 reconciliation](#tagged-pdf-and-pdfua-reconciliation-2026-10-31) records the separate PDF/UA-1/2 corpus comparison, exclusions, misses, and unmapped-rule limitations. File-verdict agreement does not establish per-rule coverage or certification. Unconfirmed rules and heuristics remain as listed in the matrix; expand tagged-content/font edge cases without visual/semantic inference. | `src/PortableDocument.Document/ValidationPdfA.cs`, `ValidationContext.cs`, `ValidationPdfADeep.cs`, `ValidationPdfUADeep.cs`, `ValidationPdfUA.cs`, and `tests/FreeDF.Tests/PdfADeviceColorTests.cs`, `DeepValidationTests.cs`, `StructureValidationTests.cs`; [targeted color evidence](validation-compat.md#2026-11-03-targeted-pdfa-color-fixtures), [historical compatibility report](validation-compat.md), [audit](spec-audit.md#1-validator-cross-check-against-verapdf), and `tools/ua_crosscheck.py`. The corpus measurements remain historical; distinguish mapped-rule evidence from file verdicts and color-only controls. |
| Performance — Hot paths (non-conformance work) | A bounded immutable lexer operator-token reuse change shows a repeatable already-open vector result; fresh-vector and text CPU gains were not reliable. Continue selecting representative hot paths from measured results and record repeatable before/after results before optimizing. | `bench/FreeDF.Bench/README.md` and checked-in dated report/CSV. Do not treat noisy short-iteration cases as ranking evidence or weaken correctness tests for speed. |

Local `/GoTo` action metadata (12.6.4.2, Table 202) is also exposed through `GoToDetails`, retaining `/D` names, byte strings, and explicit arrays without navigation, named-destination lookup, or resolving array elements. `tests/FreeDF.Tests/GoToActionTests.cs` covers direct/indirect entries, destination modes, malformed values, and cyclic `/Next` chains. This is inert inspection, not destination conformance validation or navigation.

Typed `/Named` action metadata (12.6.4.11, Table 216) now retains `/N` names, including unrecognised names, and original dictionaries without execution or dispatch. `tests/FreeDF.Tests/NamedActionTests.cs` covers direct/indirect names, missing and malformed entries, unrelated subtypes, and cyclic `/Next` chains.

Typed inert `/ResetForm` and `/ImportData` metadata now covers 12.7.6.3-12.7.6.4 (Tables 241-243). ResetForm exposes nullable `/Fields` and raw integer `/Flags`, retaining unresolved array elements and malformed/unknown raw entries without inheritance, defaults, field lookup or resetting values. ImportData exposes a local string/dictionary `/F` without decoding target bytes, accessing files/embedded data, or importing form values. `FormDataActionTests` covers local indirection, malformed types, reserved/negative flags, subtype isolation, cyclic chains, and full/incremental save round trips with unchanged field values. The combined FormData/SubmitForm/DocumentFeature Release selector passes 88/88. This completes bounded inspection only, not action execution, FDF/XFDF import, reset semantics, or clause-12 parity.

Typed `/GoToE` action metadata (12.6.4.4, Tables 204/205) exposes top-level `/F`, `/D`, unchanged raw `/T`, and nullable `/NewWindow` through `EmbeddedGoToDetails`. The additive `TargetPath` view exposes Table 205 `/R`, `/N`, `/P`, `/A`, and nested `/T` metadata using only current-PDF indirect resolution. It preserves unknown relationship names and all original dictionaries/values, with a 64-dictionary path limit and observable cycle/depth boundaries retaining raw `/T`. `tests/FreeDF.Tests/EmbeddedGoToActionTests.cs` retains top-level compatibility coverage; `EmbeddedGoToTargetTests.cs` covers direct/indirect fields, nested paths, cycles, bounds, and malformed/unknown-value cases. Conditional requiredness, attachment/tree validation, target opening, name-tree/page/annotation lookup, and destination resolution are deliberately outside this inert inspection slice, not implemented navigation semantics; `/SD` is not defined for GoToE.

Typed inert `/GoToDp` action metadata (12.6.4.5, Table 206) exposes `/Dp` only when the original value is a local indirect reference resolving to a dictionary; `/Type` is optional but must be `/DPart` when present. Missing `/Dp`, direct values, and wrong-type values remain in the unchanged action dictionary. `tests/FreeDF.Tests/GoToDpActionTests.cs` covers indirect DPart metadata, optional missing `/Type`, malformed values, unrelated subtypes, and `/Next` chain ordering/cycles. This is inert inspection, not navigation or DPart-tree validation: `/DPartRoot` is not searched, and `/Start` and `/End` are not resolved.

Separate inert document-part hierarchy inspection is available through `doc.GetDPartRoot()` (14.12, Tables 408/409). It preserves original dictionaries/references, grouped depth-first `/DParts` children, and explicit malformed/cycle/repeat/limit boundaries. Expansion allows 64 dictionaries per path, 4096 global node slots, and 4096 global child-group slots; omitted tails remain reachable through raw arrays. Optional `/Type` is accepted when absent and flagged when explicitly wrong. `/Start`, `/End`, `/Parent`, `/DPM`, `/Metadata`, and `/AF` remain inert metadata. `DPartHierarchyTests` passes in the combined Release run. This inspection API does not validate tree conformance, navigate, or decode metadata/attachment payloads; the separate bounded validator below covers selected constraints.

The additive `doc.ValidateDParts()` checks a separate bounded 14.12.2-14.12.4 tree-link/page-range subset without changing inert inspection: parent references, grouped children, page references, ranges/coverage/non-overlap/DFS order, and page `/DPart` back-links. It independently walks the page tree under depth/node budgets and checks xref generations. Missing `/End` denotes a single-page range; coverage catches omitted multi-page ends. Optional `/RecordLevel` must be an integer identifying an existing zero-based level; `/NodeNameList` must contain one PDF name per level of the longest path, each a valid XML Name (colons allowed). Name inspection has an independent 4096-item budget and preserves raw invalid entries/tails. `IsConformant` requires complete traversal and zero issues for this subset only; `IsComplete` is false at inspection boundaries. `DPartValidationTests` passes in the combined Release run. Semantic record-component interpretation and payload semantics remain excluded; this is not full DPart/PDF conformance.

JPX multi-range POC coverage now includes a reference fixture combining an initial PCRL layer with remaining CPRL layers across multiple precincts, pixel-identical to Pillow's OpenJPEG-backed decode (OpenJPEG/libopenjp2 2.5.4). `JpxTests.MultiRangeSpatialPocFixtureContainsBothProgressionChanges` verifies both encoded ranges. POC-plus-PPM/PPT fixtures additionally verify reordered component samples and packed segments ending at complete packet-header boundaries. Continue with other range/order/packed-header combinations rather than treating these cases as exhaustive.

The legacy `ppm-lossless.pdf` and `ppt-lossless.pdf` references have been replaced with three-component fixtures whose packed marker segments end at complete packet-header boundaries (T.800 A.7.4/A.7.5), retaining the original grayscale values in all RGB channels. Boundary assertions and exact source/OpenJPEG sample checks accompany the replacements; the POC packed fixtures are unchanged. The decoder now retains PPM/PPT marker-segment ends and rejects packet headers crossing those ends, as well as PPM segments ending within or immediately after an Nppm field without a completed packet header. Passing mutation tests repartition identical header bytes at each byte position, accepting complete-header boundaries and rejecting mid-header boundaries. Both accepted split positions for both markers independently preserve all 192 RGB samples through Pillow 12.1.1/OpenJPEG 2.5.4. Multi-tile/multi-tile-part and earlier-PPT coverage is described below; broader combinations remain unproven.

## Rendering and content

### Issue #8: content-stream diagnostics and remaining dispositions

`PdfRenderOptions.StrictContentValidation` opts into warning-only checks for
recognized operator operand arity/types, graphics-state underflow, unmatched
or unclosed text objects, text operators outside `BT`/`ET`, unclosed marked
content, and missing/repeated/non-first Type 3 `d0`/`d1`. It does not reject
content or alter the renderer's permissive operator execution; the default is
unchanged. `Type3AndCompatibilityTests` covers strict/permissive diagnostics,
rendered-pixel compatibility, and Type 3 ordering.

| Issue #8 item | Disposition |
|---|---|
| Operand arity/types, `Q` underflow, text outside `BT`/`ET`, and unclosed marked content | Implemented as opt-in warning-only diagnostics; complete semantic/value-range validation and rejecting validation remain deferred |
| Type 3 `d0`/`d1` ordering | Missing, repeated, and non-first metrics operators are diagnosed in strict mode; enforcement is deferred |
| Type 3 `d1` colour-image restriction | Strict mode warns on non-stencil XObject/inline images, including images reached through Forms; permissive pixels are unchanged and rejecting enforcement remains deferred |
| Image `/Interpolate` | Implemented with scoped pixel-centred bilinear decoded-RGB/premultiplied-alpha sampling, independent explicit/soft-mask grids and unchanged false/default behavior; see `spec-audit.md` 3.2 for reader-policy limits |
| External stream `/F`, Reference and PostScript XObjects | Intentionally unsupported legacy/deprecated features; external data is not opened and PostScript is not executed |

The strict-diagnostics and scoped image-interpolation slices do not close issue
#8 or establish PDF-spec parity. The Type 3 rendering gaps and broader image/Form
coverage recorded below remain actionable.

The subsequent annotation/AP, DPart hierarchy, CFF Expert and JPX marker-boundary batch passed 583/583 combined focused cases and 1160/1160 full Release cases, with no failures or skips. The Release solution build succeeded with one EditorWeb CS8602 warning. The initial focused attempt stopped before tests on an invalid generated Content reference assembly (CS0009); a targeted non-incremental Content rebuild regenerated it, then serial MSBuild (`-m:1`) focused/build/full runs passed without source changes for that error.

CFF1 predefined ISOAdobe/Expert/ExpertSubset charsets and Standard/Adobe Expert built-in encodings are supported. Absent PDF `/Encoding` or `/BaseEncoding` selects embedded non-CID CFF encoding; `/Differences` overlays that base, `/ToUnicode` takes extraction precedence, and encoding uses the selected code/glyph mapping. `CffTests` passes in the combined runs. Generated tables cite Adobe TN 5176 Appendices B/C and fontTools 4.63.0; this closes the narrow CFF Expert table gap, not legacy Type 1 Expert/MM support. Explicit PDF MacExpertEncoding is now supported as a distinct code-to-glyph-name table (Adobe PDF Reference 6th ed Appendix D.3 / ISO 32000-2 Annex D.4), with holes mapping to .notdef, Differences overrides and ToUnicode precedence; it is not aliased to Adobe ExpertEncoding. Focused mapping/width/encoding tests cover this addition.

Integrated Release validation passed 367/367 focused ICC/action/DocumentFeature/JPX cases and 1100/1100 full-suite cases, with no failures or skips. The solution build succeeded with one EditorWeb CS8602 warning. The tiling-pattern intent fixture now declares its pattern-local ICC color-space resource.

A bounded Pillow 12.1.1 / LittleCMS 2.17 comparison of the same distinct constant-Lab A2B0/1/2 payloads confirms relative (119), saturation (198), and absolute red-channel results (119 for D50 and v2 display; 86 for v4 half media white), within one level of C# expectations. It uses RGB8 input/output, `NOOPTIMIZE` (0x100), no explicit BPC flag or adaptation override, and LittleCMS-created sRGB output. Perceptual output is **not** pixel-identical: LittleCMS returns 45 (v4 input) or 0 (v2 constant input), versus C# 48. LittleCMS `_cmsLinkProfiles` forces black-point compensation for v4 perceptual/saturation; a relative-intent control using the same L*=20 payload returns 48. This control does not isolate BPC from perceptual PCS/reference-black handling. ICC.1:2022 6.3.4.3/Table 16 defines nonzero perceptual reference black and pre-v4 white-preserving PCS adjustment separately. Both PCS/reference-black handling and CMM black-point behavior remain follow-up; this is not a completed perceptual parity claim.

Subsequent bounded BPC support reads ExtGState `/UseBlackPtComp` `/ON`, `/OFF`, and `/Default` (deterministically Off), restores it through `q/Q`, and propagates it through existing color-conversion surfaces. Invalid values warn and preserve state; absolute intent disables compensation. Relative-colorimetric ICCBased v2/v4 input/display XYZ matrix/TRC RGB or gray profiles use a prepared white-preserving XYZ affine map to analytic sRGB black zero only when strict monotone/unit-white/neutral-black constraints hold (`SupportsBlackPointCompensation`). Unsupported profiles retain baseline conversion. LUT/CMYK/Lab, perceptual/saturation estimation, general ISO 18619 black estimation, and perceptual PCS/reference-black matching remain unsupported. All `IccTests` plus annotation/validator regressions passed 184/184 focused Release cases, then the expanded combined run below.

Annotation PDF/UA validation now checks nonnegative integer `/StructParent` keys resolve through ParentTree to a parsed structure element with a direct parsed `/OBJR` child whose raw indirect `/Obj` identifies that annotation. Raw ParentTree values must be indirect references with a current in-use xref entry and matching generation; compressed references require generation zero rather than the object-stream index. Missing-key and Link Contents/Alt checks remain; the focused `AnnotationStructureValidationTests` Release selector passes 48/48. Remaining: existing parser/10k-annotations-per-page limits and subtype exemptions, plus broader semantic/accessibility validation. These are bounded checks, not certification.

The expanded BPC/UA/DPart/root-field/MacExpert batch passed 833/833 combined focused cases and 1393/1393 full Release cases, with no failures or skips. Release solution build succeeded with four warnings: EditorWeb CS8602, TransparencyTests CS0675, FontCmapTests xUnit2029 and SecurityTests xUnit2013. New xUnit2031 warnings in annotation/DPart tests were corrected before that build/full run. Matrix status remains 15 Partial and 0 Todo, with two Done rows retaining explicit limitations; none of these counts establishes full parity.

The subsequent SubmitForm and multi-tile JPX additions pass the expanded Release run below; the 1393-case run above predates them. `PdfAction.SubmitFormDetails` exposes local nullable file-specification/Fields/Flags/CharSet values and the original dictionary (12.7.6.2), without target access, field lookup, submission, defaults, inheritance, or flag/encoding interpretation. Malformed values remain inspectable through the raw dictionary.

The initial multipart JPX fixtures cover two tiles with two tile-parts each, unequal packed-header records and nonempty bodies. A primary-text review against T.800 (11/2015) A.7.4/A.7.5 and inspection of the actual streams confirm ordered PPM records per tile-part, PPT indices restarting in each header, and complete packet headers placed before their corresponding bodies in the same tile-part. PPM has a transformed-stream OpenJPEG comparison; the same-part multipart PPT fixture's expected bytes come from the OpenJPEG-decoded original source, not an independently decoded transformed PPT stream (OpenJPEG 2.5.4 rejects that distribution).

Earlier-tile-part PPT headers are now queued per tile until their corresponding bodies arrive, including an empty first body and interleaved tiles. T.800 A.7.5 permits the same or a lower TPsot for headers; B.10.1 requires packet-boundary tile-part divisions. PPM remains in the main header, with the kth Nppm record mapped to the kth tile-part in codestream order, not arbitrarily shifted between parts. Both new transformed earlier-PPT fixtures independently decode through Pillow/OpenJPEG 2.5.4 with exact equality for all 384 samples each; this does not remove the older same-part PPT caveat. Regressions reject later-than-body headers, incomplete marker-segment headers, inconsistent tile-part indices/counts, packet bodies crossing tile-part boundaries, and missing final bodies. All JPX cases pass 85/85; the Release solution build succeeds with two warnings (ContentInterpreter CS8604 and the existing JpxCodestream.HeaderDone CS0649), and the full Release suite passes 1514/1514 with zero failures/skips. The image-filter row remains Partial; output precision, subsampling, ICC, and broader progression/codeblock/ROI coverage gaps remain.

The whole-object OBJR annotation-text gap listed above now has a bounded implementation, covered by the expanded Release run (`ObjectReferenceTextTests` plus existing stream-owner/Form/structure regressions). Under 14.7.5.3-14.7.5.4, extraction follows OBJR page selection and requires the referenced local annotation's StructParent entry to map back to the containing element. It reads direct text from the selected indirect normal appearance, excluding explicit Artifact sequences; repeated references contribute once per annotation/page. ActualText and opt-in Alt retain precedence. An annotation's StructParent does not prohibit its separate appearance stream's StructParents: appearance MCID text without an explicit MCR can contribute through OBJR, while explicitly referenced stream/MCID text is reserved for the existing MCR path to prevent duplication. That reservation is conservative even when the MCR later fails page/owner gates. Remaining: nested appearance Forms, arbitrary whole-object XObjects, ambiguous/malformed overlap, external streams and per-invocation tagging. Contents, synthesized appearances and action targets are never inferred as text; plain extraction remains annotation-free.

JBIG2 coverage adds a hand-encoded MMR pattern dictionary and halftone grid: white/black 2x2 patterns selected by a 2x2 grid with rows 01/10 yield packed PDF rows CF/CF/3F/3F. This is a manually derived expected bitmap, not an independent decoder comparison; no decoder fix is claimed. Integration review corrected the fixture's segment-number width, immediate-region type and page associations, and made horizontal run codes explicit. All `Jbig2Tests` pass in the expanded Release run; this one-bit grid is not exhaustive MMR halftone coverage.

The integrated SubmitForm/JPX/OBJR/JBIG2/Pages batch passes 965/965 combined focused and 1471/1471 full Release cases, with zero failures/skips. The incremental Release solution build succeeds with zero warnings/errors; the preceding focused compile reports existing ContentInterpreter CS8604, JpxCodestream CS0649, TransparencyTests CS0675, FontCmapTests xUnit2029 and SecurityTests xUnit2013 warnings. Pages integration preserves four browser-only parallelism guards, null-preserving host font fallback and the existing unique-Form child editing/rewrite/409 conflict policy in shared EditorSession; full-suite HTTP regressions pass. The Pages child's published Chromium evidence predates this merge; the integrated WASM build passes, but Chromium smoke was not rerun here. See `browser-editor.md` for browser limitations. Status remains 15 Partial / 0 Todo plus two Done rows with explicit limitations.

The subsequent 12-bit Huffman DCT slice supports SOF1/SOF2 sequential/progressive frames, native-precision colour conversion and clamped sample >> 4 output. Generated native samples use a floating-point IDCT oracle cross-checked against libjpeg-turbo; tests allow one output level for integer rounding. SOF0/SOF9/SOF10 are not enabled at 12 bits. JPEG plus editor/session/HTTP regressions pass 120/120 before the final asset-lifecycle additions.

Editor asset liveness now includes live page items, retained undo snapshots and pending uploads. Unclaimed uploads expire at ten minutes on the next command, with explicit cancellation; neither expiry nor cancellation removes live/history references. Pending UI files survive failed saves and can retry the same ID after expiry. Deterministic expiry/cancel and editor HTTP selectors pass 18/18. The integrated published WASM Chromium smoke now passes real image insert/delete/undo, alongside existing edit/save/reopen, exports, mobile and no-network checks (208 valid WASM artifacts). An initial harness click on the child image timed out because its parent intercepted pointer events; selecting the parent fixes the harness. Final Release solution build succeeds incrementally with zero warnings/errors; full suite passes 1502/1502, zero failures/skips. No deployment occurred.

| Clause | Remaining work | Evidence and completion checks |
|---|---|---|
| 8.6 — Color spaces | Intent propagation/fallback/media-white scaling and cached allocation-free ICC sampling remain covered. Added v4 perceptual black, LittleCMS-compatible LUT BPC, B2A/mBA destination conversion and standalone ncl2 reading. Implicit perceptual/saturation BPC is compatibility policy, not PDF/ISO-required; detection follows cmsDetectBlackPoint, not ISO 18619. DefaultGray/RGB/CMYK now remap Device spaces for operators, `cs`, images and shadings (previously only group blending). D2Bx/B2Dx multiProcessElements (cvst/curf parf0-2+samf, matf, float clut; D2B preferred, unsupported content falls back to A2B/B2A) are now read and match LittleCMS 2.17 on two hand-assembled profiles; bACS/eACS and other elements are unsupported. Remaining: device-link/abstract/N-colour transforms (not valid PDF ICCBased), partial observer adaptation, general destination/gamut/CMM and exhaustive group conversion, intent-specific Device/Cal/Lab gamut mapping and exact DeviceCMYK. Named ncl2 is not a valid PDF ICCBased renderer. | `IccTests.cs`, `IccCmmTests.cs`, `IccLcmsReference.cs`, `IccMultiProcessTests.cs` (self-derived fixtures from `tools/make_mpet_fixtures.py`) and `CalLabPdfiumReferenceTests.cs` (PDFium D65 CalRGB/Lab within two levels) cover intent/state/media-white/wrappers/cache, B2A failure isolation and bounded Pillow 12.1.1/LittleCMS 2.17 comparisons within two levels. Primary reference: [ICC.1:2022](https://www.color.org/specification/ICC.1-2022-05.pdf) 6.3.4.3/Table 16, 8.10.2 and 9.1-9.2; black detection/destination fitting follows LittleCMS, not ISO 18619. Expand real profiles and independent references rather than treating selected synthetic matches as full CMM parity. |
| 8.7 — Patterns and shadings | Mesh inputs interpolate before per-sample Function evaluation; malformed/unsupported functions warn and skip. Actual Coons/tensor evaluation, typed PostScript semantics, finer ramps, BBox/background and tiling/pattern-stroke fixes are integrated. Remaining: finite bounded tessellation, broader function/colour-space combinations and independent fixtures; selected PDFium comparisons do not prove full parity. | `src/PortableDocument.Content/MeshShading.cs`, `PostScriptFunction.cs`, `ContentInterpreter.cs`, `src/PortableDocument.Render/SoftwareRasterizer.cs`; `tests/FreeDF.Tests/GraphicsGapTests.cs`. Expand analytic/independent references without weakening fixed pixel gates. |
| 8.9–8.10 — Images and Form XObjects | `/Interpolate` now has a scoped reader policy: bilinear decoded RGB/premultiplied alpha with independently sampled explicit/soft masks; false/default unchanged. Remaining: broader image/mask colour-space and nested Form coverage, independent interpolation references and the other image/Form gaps recorded in the audit. Bounded minification is not ideal reconstruction; this does not close #8 or establish full clause parity. | `docs/spec-audit.md` 3.2, `ImageInterpolationTests.cs` (analytic transforms, clips, alpha/masks, matte/transfer and cached bands), `JpegTests.cs`, `JpxTests.cs`, `Jbig2Tests.cs`, `CcittTests.cs`, and `RenderTests.cs`. Compare rendered pixels and decoded image data against independent implementations. |
| 9 — Fonts | Adobe Glyph List 2.9/Zapf mappings, WMode 1 metrics and supported vertical GSUB single substitutions remain covered. Legacy Type 1 named ExpertEncoding, WeightVector/Blend metadata and supported MM blend othersubrs now work, bounded to 16 masters/four axes, with regular-grid design-coordinate interpolation. Remaining: arbitrary font PostScript design procedures and irregular MM layouts; bundled CJK glyph data; complete shaping/GSUB; OpenType-name-based CJK fallback beyond fixed candidates; and Type 1 hinting if needed for parity. | `Type1Font.cs`, `Type1MultipleMasterTests.cs` (generated fixtures, exact geometry/metrics, 16-master stack bounds), plus existing `GlyphNameTests.cs`, `VerticalFontTests.cs`, `FontCmapTests.cs`, `CffTests.cs` and `EditorFontTests.cs`. Extend independent font-render references without executing font PostScript. |
| 11 — Transparency | Page-group blending supports DeviceCMYK, calibrated, and ICC blend spaces; same-operator fill+stroke and text knockout plus `/AIS` are covered by analytic tests. The new rendering has not been compared with Chrome/PDFium. The CMYK non-separable K rule follows Adobe/Splash behavior rather than explicit ISO text; text-knockout storage is clipped to group bounds, not glyph bounds. Final page output remains isolated over opaque white; page elements rendered with `asPageElement: true` honor page-group `/I`, CropBox clipping, and ignore inherited `/CS` for non-isolated groups. Remaining: arbitrary backdrop/transparency output, per-object knockout outside Form XObjects, independent references, and broader color-space/group combinations. | `docs/spec-matrix.md`, `src/PortableDocument.Render/SoftwareRasterizer.cs`, `src/PortableDocument.Content/GroupBlendSpaces.cs`, `tests/FreeDF.Tests/TransparencyTests.cs`, and `GraphicsGapTests.cs`. Add independent reference images for remaining color spaces and non-form knockout. |
| 8.11 — Optional content | `/OCMD /VE` expressions are evaluated with depth/node limits; `RenderPage` can select named `/Configs` (including `/Unchanged` inheritance from `/D`); `/AS` View/Print/Export state and View `/Zoom` bounds are applied. In-memory optional-content state exposes radio-button groups and locked layers, enforces radio selection and lock constraints, and does not execute PDF actions. Caller-supplied Language context applies `/AS` Language exact/preferred-primary fallback per OCG list; User context matches `/Ind`, `/Ttl`, and `/Org`, preserving manual overrides and the no-context behavior. No host settings are read. Remaining: CreatorInfo/PageElement policy is intentionally not inferred, Language tags are not registry-canonicalized, and web UI/save-back plus broader usage conditions remain unsupported. | `docs/spec-matrix.md`, optional-content handling in `src/PortableDocument.Content/ContentInterpreter.cs`, `OptionalContentContext.cs`, `OptionalContentState.cs`, and `src/PortableDocument.Render/PortableDocument.cs`, and `tests/FreeDF.Tests/RenderTests.cs` plus `OptionalContentStateTests.cs`. Test multiple configurations, usage events, contexts, and OCG/OCMD expressions. |
| 10 — Rendering intents, overprint, halftones, and transfer functions | TR/TR2 eligible opaque paints and opt-in RGB-approximate OP/op/OPM simulation remain covered. ICC perceptual black/BPC additions are bounded as in 8.6, not complete CMM support. Halftone/flatness/smoothness remain inert. Remaining: exact overprint/colorants, halftones, transfer combinations and general perceptual/gamut/CMM matching; no independent print/raster reference for overprint and some expectations remain self-consistent. | `TransferFunctions.cs`, `ContentInterpreter.cs`, `PortableDocument.Render/PortableDocument.cs`, `OverprintTransferTests.cs`, `IccTests.cs`, `IccCmmTests.cs`. Expand independent output references without claiming full clause-10 parity. |
| 13 — Multimedia, 3D, RichMedia, XFA, and JavaScript | Inert readers now add rendition/selector/clip graphs, criteria, players, play/screen parameters, offsets/durations, Movie/Sound metadata, 3D stream/reference/activation/animation and typed projection/background/render/lighting/node/cross-section dictionaries. Rendition/clip graphs are memoized and depth/node bounded with truncation markers. Existing XFA/JavaScript/RichMedia remain opaque/non-executable. Remaining: complete dictionary semantics, model/measurement interpretation and broader malformed/preservation variants; no playback, external file access or host-software matching is performed. | `MultimediaFeatures.cs`, `ThreeDFeatures.cs`, `DocumentFeatures.cs`, `MultimediaThreeDTests.cs`. Assert raw dictionaries and model bytes survive round trips, graph boundaries terminate, and no code/media executes. |

The subsequent asset review fixes renew successful same-ID upload leases and retain clipboard image references across source deletion, page removal and navigation/history release. Clipboard replacement releases the old reference; successful document replacement clears the clipboard and pending upload files. Clipboard-only updates are ordered without marking document contents dirty. Deterministic regressions cover the original-deadline retry race, shared live/clipboard retention and final release. Combined JPX/session/HTTP coverage passes 107/107; integrated WASM publish validates 213 artifacts, and Chromium passes copy/delete/navigate/paste in addition to undo and the existing no-outbound-traffic checks. The final combined incremental Release build reports zero warnings/errors, with 1518/1518 full-suite passes and zero failures/skips. Earlier compilation emitted existing CS8604, CS0649, CS0675, xUnit2029 and xUnit2013 warnings; this is not a warning-free clean-rebuild claim.

## Editing and signatures

| Clause | Remaining work | Evidence and completion checks |
|---|---|---|
| Editor — Existing images and vectors | The editor serializes queued move/resize/reset transforms in order and blocks document mutations/saves until pending edits succeed. Page-level object transforms and direct painted children of uniquely invoked indirect Form XObjects are supported with HTTP save/reopen/render coverage. Shared, repeated, hidden, nested, or otherwise ambiguous Form children are conservatively withheld while group transforms remain available; incompatible group/child edits and page identity mutations return visible conflicts. Unused resource aliases and streams containing inline images can conservatively disable child editing. Complex vector paint/clip/operator ranges remain unsupported. | `docs/spec-matrix.md`, `tools/FreeDF.EditorWeb/FormObjectEditPolicy.cs`, `Program.cs`, `wwwroot/index.html`, `e2e_object_saves.test.cjs`, and `tests/FreeDF.Tests/FormObjectTransformTests.cs`, `FormObjectHttpTests.cs`, and `ExistingObjectTransformTests.cs`. Verify saved PDFs render equivalently except for the intended transform and that ambiguous ownership or failed PUTs cannot be silently saved. |
| Editor — Redaction limitations (row currently marked Done) | Full rewrite remains required. Arbitrary paths/strokes/clips now subtract under rotated/sheared transforms; curves flatten to a 0.05 page-space-unit bound. Shared/nested Forms are copied per invocation; inline/stencil images support partial blanking and hidden optional content is included. Remaining: finite 64-cut/40k-vertex geometry budgets and flattening, singular/unsupported complex paint removed whole or clipped empty, content-only overload whole-object image/Form removal, glyph geometry and Unicode-dependent outline matching. No confusable folding/cross-operator phrases. Untouched uses intentionally retain content; editor undo/clipboard/recovery are not purged by export. | `PdfEditor.cs`, `Redactor.cs`, `PathRedaction.cs`; `RedactionCoverageTests.cs`, `VectorRedactionTests.cs`, `OutlineRedactionTests.cs`, `EditorTests.cs`, `SecurityTests.cs`. Saved compressed/object-stream fixtures inspect decoded streams/resources and redacted-Form side-data removal; broaden malformed/paint/decode combinations and independent references. |
| 12.8 — Digital signatures (bounded APIs marked Done) | Certification/DocMDP and FieldMDP checks, encrypted incremental signing, RFC 3161 timestamps, explicit chain-trust policy, offline CRL/OCSP validation, PAdES signing options and incremental DSS/VRI authoring are implemented. Dedicated TSA/time/revocation and strong-imprint checks guard trusted time. Remaining: general PAdES LTV/revision graphs, broader permission classifications and malformed inputs. Timestamp acquisition is caller-supplied; HTTP is a separate opt-in assembly, never a browser side effect. Unsupported/unclassifiable changes cannot be reported as permitted; non-incremental sources may still require a rewrite. | `PdfDocMdp.cs`, `PdfSignatureTrust.cs`, `PdfTrust.cs`, `PdfRevocation.cs`, `PdfSignatures.cs`, `PdfEditor.Signing.cs`, `PdfEditor.Ltv.cs`, `SignatureTrustTests.cs`, `PadesInteropTests.cs` and pinned pyHanko fixtures/results under `tests/fixtures/signatures/pades`. Missing trust evidence is Indeterminate; cryptographic integrity alone does not imply trusted signer or full conformance. |

## Delivery order

The measured browser-editor performance slice removes graphics painting during
metadata analysis, bounds exact PNG reuse to 8 MiB per session, schedules only
visible thumbnails after page preparation, and avoids full refreshes on save.
Three-run local median save latency falls 62-78% across text/vector/image/report
samples; image-heavy open/undo/text-edit latency falls 26-30%, with exact
first-page PNG equality. See `browser-editor.md` for commands, full before/after
tables, CPU/blocking/heap caveats and remaining synchronous-raster bottlenecks.
Focused coverage passes 142/142 and full Release 1549/1549. This does not change
any conformance row or claim that all editor latency is resolved.

1. Finish safe object-model exposure for clauses 7.10–7.11 and 13, preserving opaque dictionaries/streams and proving there is no execution path.
2. Close decoder and document-model gaps with focused reference fixtures and fuzz/robustness coverage.
3. Address rendering gaps with independent output comparisons, especially color, shading, transparency, and optional content.
4. Complete editing and signature capabilities with round-trip, security, and external-validator checks.
5. Revisit every Partial/Todo matrix row against the normative clause text before changing its status; a passing selected fixture is not proof of full parity.

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

## Tagged PDF and PDF/UA reconciliation (2026-10-31)

This is the historical checkpoint. See the [2026-11-03 full remeasurement](#2026-11-03-full-pdfua-corpus-remeasurement)
for current counts and verified standalone/combined profile selection.

Independent evidence: veraPDF 1.30.3 (JDK 17, installed user-locally; nothing vendored) was cross-run with `tools/ua_crosscheck.py` against the generated samples and the public veraPDF-corpus (`PDF_UA-1`: 297 files, `PDF_UA-2`: 138; not committed). Baseline before this work on PDF_UA-1: 36 false positives, 42 misses. Now: PDF_UA-1 0 false positives / 17 misses of 156 failing (297 compared, 0 excluded); PDF_UA-2 4 -> 0 false positives, 40 -> 11 misses of 84 failing (138 compared, 0 excluded). Reproduce with `python tools/ua_crosscheck.py corpus <corpus>/PDF_UA-1 1 --misses` (and `PDF_UA-2 2`); the script prints total/compared/excluded, the matrix and a per-rule overlap table. These are file-verdict figures: the per-rule table shows many veraPDF rules without a mapped rule of ours (unmapped, marked `*`) and a few mapped rules where files fail only through other rules (e.g. 7.2-29, 7.5-1, 8.2.4-3), so rule-level conformance is not established. The two samples that cannot be read without a password (17) are excluded; no other sample (01-26) disagrees on the pass/fail verdict; sample 19 gained an XMP packet and is veraPDF-clean for UA-1.

Closed (validator): RFC 3066 language tags and per-text language determinability instead of a blanket catalog `/Lang`; PDF 1.7 role vocabulary, circular and unused-remap handling; outside-CropBox/hidden annotations; Annot/Link/TrapNet/PrinterMark/Popup rules; Link `/Contents` only in UA-1; TD header connections; Form XObject invocation context, Artifact/tagged nesting and Ref XObjects; pdfuaid prefixes and `rev`; CMap/CIDSystemInfo and TrueType encodings; UA-2 structure namespaces (`/NS`, `RoleMapNS`), PDF 2.0 version, pdf2 Document root, H/Note/Math/ListNumbering/Caption/LI content, structure destinations, file-spec Desc/AFRelationship.

The Link-parent check now distinguishes profiles: PDF/UA-2 permits either Link
or Reference (ISO 14289-2 8.2.5.20; published veraPDF rule 8.2.5.20-1),
including role-mapped types; PDF/UA-1 still requires Link. Focused
`AnnotationStructureValidationTests` cover both accepted and rejected parents.
This corrects a false rejection. A subsequent bounded check for 8.2.5.20-2
compares local structure-target identity across direct OBJR Link annotations
inside each Link/Reference element. It resolves local GoTo `/SD` before `/D`,
direct `/Dest`, named destinations and destination dictionaries; different
fit/zoom parameters do not make a different structure target. Cyclic or
excessively deep destination chains produce an explicit incomplete-check error.
Four independent public veraPDF-corpus controls (8.2.5.20-t02 fail/pass a/b)
were compared with veraPDF 1.30.3: both failures now fail the exact mapped
`PDFUA2-LINK-TARGETS` rule and both passes remain accepted (4 compared, 0
excluded). Self-authored `PdfUaLinkTargetTests` cover this slice without
vendoring the external PDFs. URI/remote target grouping, general destination
validity and exhaustive named-tree syntax validation remain open. The dated
corpus-wide counts above were not remeasured by this slice; see the later
full remeasurement linked at the start of this section.

PDF/UA-1 media clip data now requires string `/CT` and multilingual `/Alt`
string pairs with nonempty description strings (7.18.6.2; published veraPDF
rules 7.18.6.2-1/2). The inert action walk follows Rendition selectors, media
renditions and clip sections to data dictionaries, including direct dictionaries
without optional `/Type`. Shared nodes are checked once; cycles, reference/depth,
work and alternate-array boundaries report explicit incomplete-check errors.
Five independent public 7.18.6.2 controls were compared with veraPDF 1.30.3:
all three negatives now fail their exact mapped rules and both positives remain
accepted (5 compared, 0 excluded). `PdfUaMediaClipTests` uses self-authored
fixtures. This checks syntax, not MIME grammar, language suitability or useful
human descriptions; empty `/CT` strings and empty `/Alt` arrays are not rejected
by these rules, matching the published controls. Media is never accessed,
decoded or played. UA-2 is unchanged; broader media conformance remains open.
Corpus-wide remeasurement was deferred at this checkpoint and is recorded above.

PDF/UA-2 clause 8.8 page-destination detection also follows named destinations
and `/SD`/`/D` dictionaries, catalog `/OpenAction`, and GoTo actions reached
through the existing inert action-trigger walk (including `/Next` and additional
actions). It reuses the bounded local target resolver from Link grouping;
destination-reference cycles/depth and action-walk truncation report
`PDFUA2-DEST-INCOMPLETE`. Self-authored `PdfUaDestinationTests` cover the
new paths and preserve UA-1/remote-action behavior. Both public 8.8 negative
controls still fail their exact mapped rules against veraPDF 1.30.3 (2 compared,
0 excluded); these controls were already detected, not new corpus misses
closed. Named lookup now reports incomplete validation for indirect-reference
cycles or chains beyond 32 references, name-tree cycles/depth beyond 64, and
a 10,000-operation per-lookup validation budget (nodes, pairs, child entries
and indirect-reference hops). Completed shared subtrees are not cycles, and
reachable siblings remain searched after cycle/depth boundaries. Both destination
and Link-grouping checks receive these failures; ordinary inspection/import
retain their previous lookup policy. Self-authored boundary controls establish
this behavior, not independent engine agreement on malformed trees.
Exhaustive destination validity, named-tree syntax/key-byte matching and
all possible action-bearing objects remain open. No navigation or target
access occurs, and dated corpus-wide counts are unchanged.

Closed (editor): structure tree editing, `RebuildParentTree` (page, `/Stm`, OBJR owners; unique keys kept, clashes reassigned) and conservative `AutoTagUntagged` (text and text-painting Forms -> P, images -> Figure without alt, vector paths -> Artifact; catalog set `/Suspects true`; render-equality tested; result reports figure/artifact counts and `NeedsReview`). `RebuildParentTree` refuses (throws, writes nothing) beyond the depth/size bounds and clears keys of owners that left the tree.

The 2026-11-03 containment slice adds 1534 unconditional parent-child
exclusions and 14 direct-content bans from ISO/TS 32005:2023 Table 5,
incorporated by ISO 14289-2:2024 6.2 and 8.2.4. Both parent and child types
resolve through namespace role maps, including inherited containment through
`Part`, `Div`, and `NonStruct`. Findings are capped at 1000 with a separate
Error when work is omitted; malformed namespaces and skipped/truncated
structure are not reported as complete checks. PDF 2.0 `Em`/`Strong` types
are recognized. The pinned combined veraPDF profile contains 1636 Table 5
rules, not the previously stated 1722. A follow-up implements all 33 unconditional
occurrence predicates: at most one specified child type and exactly one root
Document. Counts share an effective parent through transparent wrappers and
remain independent for nontransparent parents, using namespace-aware roles.
The 110 self-authored reference controls agree with veraPDF 1.30.3 on all 33
exact predicates (39 failures, 71 nonfailures, zero excluded); four independent
UA-2 positives remain compliant in both. These are rule-level controls, not
proof that the authored fixtures fully conform. The remaining 55 grouping-dependent
predicates (including four conditional occurrence limits) are deferred, as are Ruby/Warichu
sequences; existing checks may overlap individual deferred predicates, but
complete coverage is not established. See [provenance and narrow controls](validation-compat.md#2026-11-03-pdfua-2-containment-slice).
The later full corpus comparison is recorded in the dated remeasurement above;
it does not replace these predicate-specific controls.

Still open: arbitrary owner graphs and external (`/Stm`) streams in text extraction; per-invocation tagging of reused Form XObjects (UA-1 7.20-2 is only a heuristic `PDFUA-FORM-REUSED-TAGGED`); editing `/Stm` content, structure namespaces and role maps in the editor; auto-tagging of links/forms/figures/tables/headings and alternate text; remaining veraPDF misses listed above (font-program completeness 7.21.4.2/7.21.5/7.21.7-2/7.21.8, media clips, URI/remote link-target grouping, remaining ISO/TS 32005 nesting conditions); logical-order checks against an independent renderer/reader for malformed or reused forms. Empirical, not spec-derived: `PDFUA-TABLE-HEADER-CONNECTION`. Status stays Partial.