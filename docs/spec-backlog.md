# ISO 32000-2 parity backlog

This backlog expands every incomplete status in [spec-matrix.md](spec-matrix.md), plus known limitations recorded under rows currently marked Done. Full spec parity is the target. “Safe support” for PDF JavaScript, XFA, RichMedia, and related executable content means parsing, preserving, and exposing the structures without running embedded code.

The current matrix has **15 Partial rows and 0 Todo rows**. Two additional rows marked Done (redaction and signatures) document limitations that still need follow-up. The benchmark source and checked-in results remain in `bench/FreeDF.Bench`; they are repository artifacts, not a separately published NuGet package.

## 2026-11-05 complete empty Flate encoding

ISO 32000-1 7.4.4.1 (printed p25) defines Flate through RFCs 1950 and 1951.
[RFC 1950 sections 2.2-2.3](https://www.rfc-editor.org/rfc/rfc1950.html#section-2.2)
require the zlib header, compressed data and Adler-32 checksum even for empty
decoded input. The public Flate encoder previously returned zero bytes for
empty input on the current runtime. Empty file-attachment annotation streams,
sample blank pages, sample Form XObjects and sample embedded files consequently
advertised FlateDecode without a complete encoded stream.

The shared encoder now returns a fresh eight-byte empty zlib stream: header,
final fixed-Huffman block and the empty-input Adler-32 value of one.
Annotation appearance/attachment compression uses that encoder. The sample
builder routes only empty compression through it, preserving its existing
SmallestSize compression level for nonempty input. The core writer already
leaves short unfiltered streams uncompressed; its valid zero-length raw stream
behavior is unchanged. No decoder strictness or permissive recovery is relaxed.

Nineteen tests cover both public entry points, independent mutable return
buffers, exact nonempty compressed bytes, strict decoding, full/incremental
attachment saves from classic/object-stream sources, unchanged raw empty
writer streams, sample blank pages/forms/embedded files and the sample
builder's unchanged nonempty compression. The initial fourteen-test
unchanged-production baseline was six failures/eight passes. Five later sample
controls reproduced three failures/two passes before changing sample production
code. Final focused selector: 163 passed. Full Release: 19,257 passed,
zero failed/skipped. Final solution build: zero warnings/errors.

Thirteen native PDF controls retain exact encoded bytes and stream identities.
Python zlib independently rejects the nine original zero-byte encodings and
confirms complete EOD, exact decoded bytes and no unused/unconsumed data for
all corrected streams. The empty encoding matches Python's independently
generated output; four nonempty encodings remain byte-identical.
MuPDF opens and decodes every before/after probe, including the original
incomplete empty streams; permissive acceptance is not conformance evidence.
Fresh pinned 435-PDF/870-result full corpus JSON remains wholly unchanged
against the pooled-decoder baseline.

The [encoder benchmark](../bench/FreeDF.Bench/README.md#flate-encoder-workloads)
records empty, 256-byte and 1 MiB inputs with independent decompression preflight.
The empty path materializes only its fresh eight-byte result (32 managed bytes
including array overhead in this run). This is a new valid-encoding baseline,
not a speedup comparison against the former invalid empty output.
Full filter EOD/parameter consistency and resource-budget parity remain separate.

## 2026-11-05 encryption dictionary reference chains

ISO 32000-1 7.5.7 (printed p45) prohibits compressed encryption dictionaries;
7.3.10 (printed p22) preserves indirect-value semantics. Encryption setup now
checks each reference before following it and retains the terminal dictionary's
object identity, rather than treating the first alias as the encryption object.
This rejects compressed terminal dictionaries hidden behind uncompressed aliases
and preserves the valid terminal dictionary's unencrypted O/U bytes after cache
reset. The existing 32-reference limit and password-error distinction remain.

Thirteen new tests cover one/two/four intermediaries, both normal and forced
repair, user/owner/wrong passwords, exact O/U bytes, terminal cache identity,
31 aliases plus the terminal reference and the unchanged depth limit.
Unchanged-production baseline: ten failed/three passed. Focused selector:
718 passed. Full Release: 19,229 passed, zero failed/skipped. Solution: zero
errors/two retained machine-feed NU1507 warnings.

Sixteen native controls use independently generated RC4 encryption metadata.
All ten valid normal/repair files still open; eight alias cases now preserve
the exact source O/U bytes that were previously incorrectly decrypted, while
two direct-reference controls remain unchanged. Three formerly accepted
compressed-terminal chains are rejected; three forced-repair negatives already
failed and remain rejected. MuPDF exposes Catalog for all sixteen probes,
including the malformed compressed chains; opening alone does not establish
encryption-dictionary conformance. All ten valid terminal O/U byte pairs match
the independent source. Fresh pinned 435-PDF/870-result full corpus JSON is
wholly unchanged against the Length-chain baseline.

This is bounded encryption-entry resolution, not a document-wide forbidden-object
inventory or exhaustive encryption-handler conformance.

## 2026-11-05 compressed Length reference chains

ISO 32000-1 7.5.7 (printed p45) prohibits storing the object representing
an object stream's Length value in an object stream. Indirect-value semantics
are equivalent under 7.3.10 (printed p22). An uncompressed reference intermediary
must not hide a compressed final integer. The shared index reader now checks
each reference identity while resolving Length, before publishing members,
instead of checking only the first reference.

The existing 32-reference resolution limit, generation-aware lookup and
non-negative integer requirement remain unchanged. This is a bounded local
reference-chain check, not a document-wide forbidden-object inventory.
Ordinary streams may still use compressed Length integers through references.

Fifteen new tests cover one/two/four intermediaries, both physical container
orders, normal/forced repair, valid all-uncompressed chains through 32 total
references, rejection beyond the existing limit, exact cached member identity
and ordinary-stream bytes. Unchanged-production baseline: six failed/nine
passed. Focused selector: 705 passed. Full Release: 19,216 passed, zero
failed/skipped. Solution: zero errors/two retained machine-feed NU1507 warnings.

Twenty-six native controls preserve fourteen valid outputs exactly, including
six retained independently generated AES-128/AES-256/RC4 files. Nine malformed
chains formerly exposed Target and now produce repaired nulls without publishing
the target member. Three container-before-Length forced-repair negatives already
produced the same null and remain unchanged; they are not new rejections.
MuPDF exposes Target for all twelve malformed chains and agrees on all fourteen
positives. Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged
against the endobj-framing baseline.

Remaining gaps include generic indirect-object grammar, complete filter EOD
consistency, external-stream schemas, complete member lexical source alignment,
global forbidden-object inventory/lifecycle and resource budgets.

## 2026-11-05 object-stream endobj grammar

ISO 32000-1 7.3.8.1 (printed p19) permits only whitespace between endstream
and endobj; 7.3.10 (printed p21) requires endobj around an indirect value.
The object-stream closing boundary now requires the complete endobj keyword
after whitespace. Comments count as whitespace under 7.2.3 (printed p13).
Extra scalar/container values, wrong keyword prefixes, EOF and an unterminated
comment containing endobj do not satisfy that boundary.

Valid boundaries retain lazy Type resolution, avoiding recursive loading of a
self-referential Type merely to classify an otherwise well-formed stream.
Malformed boundaries still resolve Type, including valid indirect ObjStm names,
before applying the scoped error. Ordinary-stream closing recovery is unchanged.
The boundary check inspects bytes, not arbitrary following object values.

Twenty-four new tests cover normal/forced repair, all six PDF whitespace bytes,
complete comments, exact cached member identity, indirect Type, absent-member
stability, malformed closings, EOF and ordinary-stream recovery. The initial
22-case unchanged-production selector had ten failures/twelve passes; two later
indirect-Type cases are also reproduced by the saved exact parent assembly in
native controls. Focused selector: 690 passed. Full Release: 19,201 passed,
zero failed/skipped. Solution: zero errors/two retained machine-feed NU1507
warnings.

Forty-four native controls preserve twenty-four valid outputs exactly, including
six retained independently generated AES-128/AES-256/RC4 files. Twenty formerly
accepted malformed closings now yield nine explicit normal-load errors and
eleven repaired-null results, without publishing members. MuPDF still exposes
Target for all twenty malformed probes and agrees on all twenty-four positives.
Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged against
the opening-EOL baseline.

This closes object-stream endobj framing only. Generic indirect-object grammar,
complete filter EOD consistency, external-stream schemas, complete member lexical
source alignment, global forbidden-object inventory/lifecycle and resource
budgets remain separate.

## 2026-11-05 object-stream opening EOL

ISO 32000-1 7.3.8.1 (printed p19) explicitly requires the stream keyword
to be followed by LF or CRLF, not CR alone. Object streams now enforce that
opening before resolving Length or decrypting data. Missing opening EOL cannot
be disguised as correctly counted whitespace/comment padding in the member
header. Both direct and indirect Type are covered. Ordinary-stream opening
recovery remains permissive.

Fourteen new tests cover CR-only/missing-EOL openings, correctly counted
space/tab/NUL/form-feed/comment prefixes, valid LF/CRLF, empty object streams,
indirect Type, normal/forced repair, repeated missing-member lookups and ordinary
stream bytes. Unchanged-production baseline: seven failed/seven passed.
Focused selector: 666 passed. Full Release: 19,177 passed, zero failed/skipped.
Solution: zero errors/two retained machine-feed NU1507 warnings.

Twenty-four native controls preserve ten valid outputs exactly, including six
retained independently generated AES-128/AES-256/RC4 controls. Fourteen formerly
accepted malformed openings now yield seven explicit normal-load errors and
seven repaired-null results without member publication. MuPDF still exposes
Target in four CR-only direct/indirect-Type probes and returns null for the ten
counted-prefix probes; it agrees on all ten positives. These lookup results do
not establish whole-file conformance. Fresh pinned 435-PDF/870-result full corpus
JSON is wholly unchanged against the endstream-boundary baseline.

The object-stream opening-EOL limitation is closed. Object endobj grammar,
complete filter EOD consistency, external-stream schemas, general strict stream
parsing and resource budgets remain separate.

## 2026-11-05 object-stream endstream boundaries

ISO 32000-1 7.3.8.2/Table 5 (printed p20) permits one additional
uncounted EOL before endstream, not arbitrary skipped whitespace/comments.
Object-stream data now requires the complete endstream keyword at the declared
physical extent, either immediately or after one LF, CR or CRLF. Uncounted
spaces, tabs, NUL, form feed, repeated EOL and comments are rejected. Trivia
included in Length remains stream data and is still parsed normally.
An EOF or endstream-prefixed non-keyword cannot substitute for the marker.
These checks precede decryption; ordinary-stream terminator recovery remains
permissive and invalid Length types retain their separate schema validation.

Twenty-five new tests cover those distinctions, normal/forced repair, absent
member stability and ordinary recovery. The initial 23-case selector had nine
failures/fourteen passes on unchanged production. Two later complete-keyword
suffix cases are also reproduced as accepted by the saved exact parent
assembly in the native baseline. Focused selector: 652 passed. Full Release:
19,163 passed, zero failed/skipped. Solution: zero errors/two retained
machine-feed NU1507 warnings.

Forty-nine native controls preserve twenty-eight valid outputs exactly,
including six retained independently generated AES-128/AES-256/RC4 controls.
Twenty-one formerly accepted malformed boundaries now yield ten explicit
normal-load errors and eleven repaired-null results, without publishing members.
MuPDF still exposes Target for all twenty-one malformed probes and agrees on
all twenty-eight positives. Reference permissiveness does not override the
primary boundary requirement. Fresh pinned 435-PDF/870-result full corpus JSON
is wholly unchanged against the recovered-byte-count baseline.

This supersedes the earlier normal-terminator-padding limitation for object
streams. Stream-opening EOL, object endobj grammar, complete filter EOD
consistency, external-stream schemas, general strict stream parsing and
resource budgets remain separate; full PDF parser parity is not claimed.

## 2026-11-05 recovered object-stream byte counts

ISO 32000-1 7.3.8.2/Table 5 (printed p20) requires stream Length and
encoded byte extent to be consistent, allowing an additional uncounted EOL
before endstream. When ordinary terminator recognition fails and the reader
scans for endstream, an object stream's resolved non-negative integer Length
may no longer silently be replaced by a different recovered byte count.
The comparison happens on physical bytes before decryption: comparing Length
with an AES-decrypted RawData length would reject valid encrypted files.
Integer Length values remain lossless, with a subtraction-based bounds check
that cannot overflow when Length is long.MaxValue.

Eighteen new tests cover short/long/zero/long.MaxValue counts, indirect Length
and Type, exact counts with counted/uncounted EOL, unchanged ordinary-stream
recovery and AES-128/AES-256/RC4 object streams in normal and forced repair.
The AES controls explicitly assert that declared and decrypted lengths differ.
Corrected unchanged-production baseline: ten failed/eight passed. The previous
schema-only fixture's body is 23 bytes, not 22; its three integer-spelling
positives now use 23. Earlier Length-22 outputs demonstrated integer-type
acceptance, not valid byte extent, and are deliberately superseded here.

Normal mismatched-container lookup raises the explicit recovered-count error
after repair, while forced repair discards that container without publishing
members. Subsequent absent-member lookups remain stable nulls. Invalid Length
types retain their separate required-schema check.

Focused selector: 627 passed. The first full run had 19,137 passed and one
certificate-fixture setup failure: a fresh requested notAfter crossed a DER
second boundary after its issuer was created. Its exception matches the retained
unchanged-test iteration-128 control, before PDF validation. No signature source
changed; both failure records are retained. One full confirmation run passed
19,138 with zero failed/skipped. Solution: zero errors/two retained machine-feed
NU1507 warnings.

Thirty-four native PDFs preserve fourteen valid outputs exactly, including six
independently generated encrypted normal/repair files. Twenty formerly accepted
mismatches now produce ten explicit normal-load errors and ten forced-repair
null results. MuPDF exposes Target for eighteen of those malformed probes,
returns null for the normal Length-1 probe, and cannot open the forced-repair
long.MaxValue probe; it agrees on all fourteen positives. These are lookup
measurements, not claims of complete reference conformance. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the required
Length-type baseline.

This closes silent byte-count replacement during object-stream endstream
scanning. The existing normal terminator heuristic, arbitrary whitespace/comment
padding, complete filter EOD consistency, external-stream schemas, general
strict stream parsing and resource budgets remain separate.

## 2026-11-05 required object-stream Length type

ISO 32000-1 7.3.8/Table 5 (printed p20) requires an integer Length byte
count. Before publishing members, the shared index reader now requires the
resolved Length to be a non-negative integer. Recovery locating stream bytes
through endstream does not by itself satisfy this required metadata.
Ordinary stream byte recovery and valid uncompressed indirect Length values
remain supported.

Seventeen new controls cover missing/null/real/negative/wrong-type/unresolved
Length, valid integer spellings and unchanged ordinary-stream recovery.
Initial selector: ten failed/seven passed. The existing eight compressed
Length negatives now uniformly assert atomic repaired-null/no-member results:
an unavailable compressed Length can no longer provisionally install members
before a later container is scanned. This deliberately supersedes the earlier
order-dependent repaired-null versus subsequent-error behavior.
Full Release: 19,120 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Twenty-six native PDFs preserve six positive outputs unchanged. Twenty
formerly accepted malformed metadata targets are no longer indexed.
MuPDF still exposes Target for all twenty negatives and agrees on six
positives. Fresh pinned 435-PDF/870-result full corpus JSON is wholly
unchanged against the stream-Length-reentrancy baseline.

This enforces required presence/type/non-negativity for object-stream
indexing, not exact declared-versus-physical byte counts, complete stream
schemas, generic strict parsing, arbitrary forbidden-Length reference-chain
inventory or decode budgets. Those remain separate.

## 2026-11-05 cyclic indirect stream Length loading

ISO 32000-1 7.3.8/Table 5 (printed p20) requires an integer Length byte
count; 7.3.10 (printed p22) permits indirect values. Uncompressed object
loading now detects reentry at the same physical offset while its value
is unfinished, with cleanup in finally. Self, one-hop and mutual stream
Length cycles fail explicitly rather than recursively loading unfinished
streams until stack exhaustion. Legitimate indirect lengths remain supported.

Pre-fix probes ran in separate bounded processes: six self/chain/mutual
normal/forced-repair cases exited with Windows stack-overflow status
3221225725. Twelve tests added after the guard cover those cases, repeated
errors, unrelated subsequent lookups, direct/indirect/two-hop valid lengths
and exact cached stream bytes. Full Release: 19,103 passed, zero failed/skipped.
Solution: zero errors/two retained machine-feed NU1507 warnings.

Ten native PDFs preserve four valid outputs with exact `abc` stream bytes.
Six former process crashes now produce `cyclic indirect object loading`.
MuPDF recovers `abc` for all normal probes and the forced-repair direct
positive; it includes an extra LF for the other four forced-repair indirect
probes, including the valid indirect positive. Neither that recovery nor
this guard establishes complete stream-Length schema validation.
Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged against
the index-reentrancy baseline.

This bounds active physical-object reentry, not all indirect-reference
cycles, maximum acyclic dependency depth, complete Length type/range
enforcement, decoding limits or eager whole-file validation.

## 2026-11-05 recursive object-stream index metadata

ISO 32000-1 Table 16 in 7.5.7 (printed p46) requires integer N/First;
7.3.10 (printed p22) permits indirect values unless otherwise specified.
The shared index reader now detects reentry for the same container while
its metadata/index is still being built, with cleanup in finally. Ordinary
indirect metadata remains supported. A reference to a member requiring
that unfinished index can no longer recurse until process stack exhaustion.

Pre-fix probes ran in separate bounded processes, not the unit-test host:
both normal own-member N/First cases exited with Windows stack-overflow
status 3221225725 and explicit stderr. Forced-repair cases already returned
null because their compressed entries were not yet available. Twelve tests
added after the guard cover normal/forced cases, direct and one-hop
uncompressed reference chains, repeat access and ordinary indirect metadata.
Full Release: 19,091 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Eight native PDFs preserve four valid ordinary-indirect outputs and two
forced-repair null results. The two former process crashes now terminate
with `bad object stream header` after repair discards the cyclic members.
MuPDF returns null for the two N negatives, still exposes Target for the two
First negatives and agrees on four positives. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the
self-member baseline.

This bounds same-index reentrancy, not arbitrary object/reference cycles,
stream-byte Length recursion, maximum distinct-container dependency depth,
eager metadata schema or global resource budgets. No broad cycle-safety
or full-file conformance claim is made.

## 2026-11-05 object-stream container self identities

ISO 32000-1 7.5.7 (printed p45) prohibits stream objects as compressed
members; 7.3.10 (printed p21) requires unique object-number/generation
identity. The shared complete index now rejects its container's own object
number, before publishing any members. Recovery can no longer replace the
container entry with a compressed entry pointing back to itself.

Eight controls cover first/later self entries, normal/forced recovery,
atomic preservation of an existing uncompressed Target and valid distinct
member numbers. Initial build hit a corrupt generated Content reference
assembly; rebuilding that project serially on unchanged production restored
compilation. The actual baseline then had four failed/four passed controls.
Recovery negatives assert index state before any unsafe dereference; the
old self-referential entry was not traversed. Full Release: 19,079 passed,
zero failed/skipped. Solution: zero errors/two retained machine-feed
NU1507 warnings.

Six native PDFs preserve four valid normal/recovery outputs unchanged.
Two normal self-member lookups now recover original Target instead of
Compressed; MuPDF still exposes Compressed for both and agrees on four
positives. Native baseline lookups deliberately omit forced-repair negatives,
which are covered by the safe index-state unit controls instead.
Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged against
the token-start baseline.

This enforces the container's own identity, not all cross-stream cycles,
duplicate definitions, metadata-reference reentrancy, incremental lifecycle
rules or eager global stream inventory. Those remain separate.

## 2026-11-05 compressed member regular-token starts

ISO 32000-1 7.2.2 (printed p13) defines consecutive regular bytes as
a single token; 7.5.7 (printed p46) locates member values through offsets.
A requested member now rejects an offset between two regular bytes, rather
than exposing a suffix of an existing token as an independent value.
This local check does not parse the preceding member or change global indexing.

Twelve controls cover split numbers, booleans, null and names, repeated
failed lookups, valid whitespace/comment boundaries and delimiter adjacency
without intervening whitespace. Initial selector: six failed/six passed.
Full Release: 19,071 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Twenty-four native PDFs preserve twelve positive outputs unchanged.
Twelve formerly accepted suffix lookups now report an explicit token-start
diagnostic. MuPDF still exposes the six suffix values in both normal/repair
variants: 43, 23, true, null, 42 and true. These were checked as object values,
not inferred from absent Type probes. It agrees on twelve positive dictionary
targets. Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged
against the padded-generation baseline.

This catches definite regular-token splits, not every offset inside a name,
string, comment or container. Full source alignment, eager validation and
decode/resource budgets remain separate; lazy unrelated members stay lazy.

## 2026-11-05 padded compressed reference generations

ISO 32000-1 7.3.3 (printed p14) defines integer spelling as one or more
decimal digits with an optional sign; 7.3.10 (printed p21) requires
non-negative reference generations. Member-only reference lookahead now
ignores leading zeros when applying its existing five-significant-digit
supported magnitude limit, instead of rejecting a valid value solely for
source width. Accumulation uses only those significant digits.
Public/default reference recognition remains unchanged.

Fourteen controls cover signed/unsigned padding, exact nested reference
identities, 4,096 leading zeros, invalid/missing separators, unsupported
magnitudes and default compatibility. Initial selector: seven failed/seven
passed. Full Release: 19,059 passed, zero failed/skipped. Solution: zero
errors/two retained machine-feed NU1507 warnings.

Twenty-six native PDFs include twelve formerly rejected valid padded
references now exposing Target, four unchanged unpadded positives and ten
still-rejected malformed/unsupported controls. MuPDF agrees on sixteen
positives, exposes outer Target for six negative controls and returns null
for four; this is not proof of correct nested reference identity or complete
generation-range conformance. Fresh pinned 435-PDF/870-result full corpus
JSON is wholly unchanged against the signed-generation baseline.

This supersedes the earlier compressed-reference source-width limitation,
not the retained supported magnitude policy, default parser or generation
lifecycle validation. Token-length/decode resource budgets remain separate.

## 2026-11-05 signed compressed reference generations

ISO 32000-1 7.3.3 (printed p14) permits an optional integer sign;
7.3.10 (printed p21) requires a non-negative generation number.
Member-only reference lookahead now accepts an explicit plus sign and
negative zero while retaining the existing one-to-five-digit magnitude
policy. Negative nonzero, repeated signs, non-integers and missing R
separators still fail. Public/default reference recognition is unchanged.

Twenty controls cover signed generation values in dictionaries/arrays,
comment separators, malformed values and default compatibility.
Initial selector: six failed/fourteen passed. Full Release: 19,045 passed,
zero failed/skipped. Solution: zero errors/two retained machine-feed
NU1507 warnings.

Thirty-two native PDFs include twelve formerly rejected valid signed
references that now expose Target, four unchanged unsigned positives and
sixteen still-rejected malformed controls. MuPDF agrees on all sixteen
positives, exposes Target for ten malformed controls and returns null for
six; an exposed outer Type does not prove nested reference correctness.
Unit assertions pin the nested PdfRef identities. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the
reference-separator baseline.

This corrects signed spelling in compressed values, not the general lexer,
long zero-padding support, complete generation ranges or indirect-reference
lifecycle validation. Those remain separate.

## 2026-11-05 compressed reference keyword separators

ISO 32000-1 7.3.10 (printed p21) requires whitespace between each
indirect-reference component; 7.2.3 (printed p13) treats comments as
whitespace. Member-only reference parsing now rejects a generation directly
adjacent to R, such as `7 0R`, instead of constructing a reference from it.
References inside arrays/dictionaries are covered without resolving them.
Public/default lexer recovery remains unchanged.

Fourteen controls cover malformed nested references, zero-padded
generations, PDF whitespace, complete comment separators and existing
default recovery. Initial selector: six failed/eight passed. Full Release:
19,025 passed, zero failed/skipped. Solution: zero errors/two retained
machine-feed NU1507 warnings.

Twenty-two native PDFs preserve ten positive outputs unchanged. Twelve
formerly accepted missing-separator targets now produce an explicit
reference-whitespace diagnostic. MuPDF still exposes Target in all twelve
malformed controls and agrees on ten positives. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the
byte-bounded-lexer baseline.

This enforces the missing keyword separator in compressed values, not every
reference spelling/range, undefined-reference inventory or default parser
strictness. Valid comment separators and existing generation behavior remain
supported; broader numeric/generation policy remains separate.

## 2026-11-05 byte-bounded compressed value parsing

ISO 32000-1 7.5.7 (printed p46) supplies the consecutive member offsets;
7.3.4, 7.3.6 and 7.3.7 require complete string/array/dictionary syntax.
The internal member lexer now has an exclusive end for value consumption,
including container recursion, string escapes and reference lookahead.
It retains the original buffer without copying the member. One physical
following byte is inspected for token separation so a declared boundary
cannot turn part of a regular token or name into a valid prefix.
Public/default lexer bounds remain the full buffer.

Twenty-one new controls cover four borrowed closing delimiters, complete
values beside an independently malformed member, split regular/name tokens,
and exact lexer positions before a one-MiB unrelated tail. Default lexing of
the complete tail remains supported. The first selector had eleven failures:
seven incorrectly assumed cache identity survives repair; after correcting
that test assumption on unchanged production, four diagnostic controls
failed/thirteen passed. Four direct bounded-position controls were added
with the internal constructor. Full Release: 19,011 passed, zero failed/skipped.
Solution: zero errors/two retained machine-feed NU1507 warnings.

Fourteen native PDFs preserve six positive outputs unchanged. Eight negatives
were already rejected after overreading; they now stop at their member end
with the specific unterminated string/container error. This is bounded
consumption, not eight newly rejected files. MuPDF returns null for all eight
negative Type probes and agrees on six positives; an absent probe is not a
whole-file conformance result. Fresh pinned 435-PDF/870-result full corpus JSON
is wholly unchanged against the trailing-comment baseline.

The complete decoded object stream is still retained. This does not impose
decode/allocation budgets, eagerly validate other members or establish every
member's global source alignment. Those remain separate from per-member
value consumption and token-end checks.

## 2026-11-05 compressed member trailing comment boundaries

ISO 32000-1 7.2.3 (printed p13) defines comments through line end;
7.5.7 (printed p46) requires consecutive object values at the declared
offsets. Trailing member trivia now stops at its boundary and rejects a
comment that continues into the following member, rather than silently
consuming that member's bytes. The bounded whitespace/comment scanner is
shared with the existing header-padding check; public/default lexer scanning
still uses the complete buffer.

Fifteen controls cover crossing comments, complete LF/CR/CRLF comments,
all six PDF whitespace bytes, percent signs in strings, caching, lazy access
to the other member and existing final-member EOF comment support.
Initial selector: six failed/nine passed. Full Release: 18,990 passed,
zero failed/skipped. Solution: zero errors/two retained machine-feed
NU1507 warnings.

Thirty native PDFs preserve eighteen valid outputs unchanged. Twelve
formerly accepted crossing-comment targets now produce the explicit member
boundary diagnostic. MuPDF still exposes First in all twelve malformed
controls and agrees on all eighteen positives. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the positive
object-number baseline.

This bounds trailing trivia, not the complete member lexer or global source
alignment. Another member remains independently lazy even when the previous
member is malformed; it is not evidence that the whole stream is conforming.
Full byte-limited value parsing, allocation budgets and eager whole-file
validation remain separate.

## 2026-11-05 positive compressed object numbers

ISO 32000-1 7.3.10 (printed p21) requires positive indirect-object numbers;
7.5.7 (printed p46) identifies compressed objects through the header pairs.
The complete shared object-stream index now rejects number zero before
publishing any member. A later zero entry cannot install a partial index or
replace a recoverable uncompressed identity. This supersedes earlier scoped
retention of zero while its primary identity requirement was unverified.

Ten new controls cover first/later zero, normal/forced recovery, positive
numbers through the supported integer range, and empty streams. The initial
six negative controls all failed before the correction. Full Release:
18,975 passed, zero failed/skipped. Solution: zero errors/two retained
machine-feed NU1507 warnings.

Eight native PDFs preserve four positive/empty-stream outputs unchanged.
Two formerly exposed zero-member dictionaries become null; two streams with
a later zero member now preserve original Target instead of Compressed.
MuPDF also returns null for the zero-reference probes, but still exposes the
earlier Compressed target in both later-zero controls. It agrees on the four
positive outputs. Fresh pinned 435-PDF/870-result full corpus JSON is wholly
unchanged against the recovery-generation baseline.

This validates compressed header identities, not every ordinary indirect
definition/reference, incremental free-list lifecycle or global file schema.
Arbitrary positive-number order and existing optional empty streams remain
supported; no broader PDF-spec parity is claimed.

## 2026-11-05 recovery object-stream container generations

ISO 32000-1 7.5.7 (printed p47) requires object-stream generation
zero. Recovery now checks the current uncompressed xref entry and actual
container header generation before installing compressed members, matching
normal generation-zero container lookup. A malformed container cannot
replace a recoverable uncompressed identity and leave it unloadable.

Eight controls cover nonzero generations in normal/forced repair, valid
generation-zero object streams and existing ordinary stream generation/byte
compatibility. The initial selector had three failures. Full Release:
18,965 passed, zero failed/skipped. Solution: zero errors/two retained
machine-feed NU1507 warnings.

Sixteen native PDFs preserve ten positive/compatibility outputs unchanged.
Six previously failing lookups now recover the original uncompressed Target
instead of indexing a member from a nonzero-generation container. MuPDF still
returns Compressed for four malformed inputs; it fails to open both
forced-repair generation-99999 controls and agrees on nine positive probes.
The existing generation-99999 ordinary-stream compatibility is not a new
normative range claim. Fresh pinned 435-PDF/870-result full corpus JSON is
wholly unchanged against the Extends baseline.

This is recovery indexing enforcement, not eager rejection of every unused
object stream or general generation/free-list lifecycle validation.

## 2026-11-05 object-stream Extends collection links

ISO 32000-1 7.5.7 Table 16 (printed p46) defines Extends as a
reference to another object stream and requires collection links to form a
directed acyclic graph; printed p47 requires generation zero for object
streams. Before publishing a member index, an iterative metadata-only walk
now checks link shape, current uncompressed stream identity, ObjStm type and
cycles. Null optional entries remain absent under 7.3.7.

Eighteen controls cover non-reference/missing/non-stream/wrong-type targets,
generations, self/two-node cycles, invalid ancestor links, null/absent links,
valid chains and indirect type names. Valid linked ancestors deliberately
contain a malformed unrequested member, demonstrating that collection checks
do not decode or parse ancestor bodies. The initial selector had thirteen
failures. Full Release: 18,957 passed, zero failed/skipped. Solution: zero
errors/two retained machine-feed NU1507 warnings.

Thirty-six native PDFs preserve ten valid target outputs unchanged and
remove twenty-six previously accepted targets, ending repaired/null. MuPDF
still returns all twenty-six malformed targets and agrees on all ten positives.
Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged against
the header-padding baseline.

Collection traversal does not merge member sets, bypass xref identity,
validate all ancestor stream schemas or claim eager whole-file conformance.
Traversal is iterative with repeated object numbers rejected, not a new
parser-wide work budget or resource guarantee.

## 2026-11-05 object-stream header padding

ISO 32000-1 7.5.7 (printed p46) requires N integer pairs followed by
N object values and identifies First as the first object's offset; 7.2.3
(printed p13) treats comments as whitespace. The complete header reader
now permits only whitespace and completed comments between the final pair
and First. Extra values/keywords or a header comment crossing First cannot
be ignored before member indexing.

Sixteen controls cover hidden values/pairs/keywords/containers, unfinished
comments, all PDF whitespace bytes, CR/LF/CRLF comments, normal/forced
repair and preservation of an uncompressed identity. The initial selector
had eleven failures. Full Release: 18,939 passed, zero failed/skipped.
Solution: zero errors/two retained machine-feed NU1507 warnings.

Twenty-eight native PDFs preserve ten valid target outputs unchanged and
remove eighteen previously accepted targets, ending repaired/null. MuPDF
still returns all eighteen malformed targets and agrees on all ten positives.
Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged against
the first-offset baseline.

This scans the header gap without parsing member bodies or changing their
lazy isolation. It is not complete source-token alignment, a byte-limited
member lexer or parser-wide resource enforcement.

## 2026-11-05 first compressed member relative offset

ISO 32000-1 7.5.7 (printed p46) defines header offsets relative to the
first object and First as the decoded offset of that same first object.
The first relative offset must therefore be zero. The shared header reader
now rejects nonzero initial offsets before publishing lazy or recovery
indices, preventing skipped values from redirecting the first identity.

Twelve controls cover skipped values/keywords/trivia, normal/forced repair,
uncompressed identity preservation and existing leading-trivia support with
a zero relative offset. The initial selector had eight failures. Full Release:
18,923 passed, zero failed/skipped. Solution: zero errors/two retained
machine-feed NU1507 warnings.

Twenty native PDFs preserve eight valid target outputs unchanged and remove
twelve previously accepted redirected targets, ending repaired/null. MuPDF
still returns all twelve malformed targets and agrees on all eight positives.
Fresh pinned 435-PDF/870-result full corpus JSON is wholly unchanged against
the encryption-dictionary baseline.

This establishes relative-offset consistency, not a byte-limited reader or
complete First/source-token boundary validation. Existing leading whitespace
and comments at zero offset remain tolerated; header padding and resource
budgets remain separate.

## 2026-11-05 compressed encryption dictionary references

ISO 32000-1 7.5.7 (printed p45) prohibits storing the document's
encryption dictionary in an object stream. Encryption setup now checks a
direct trailer Encrypt reference against the xref before resolving it or
attempting password authentication. A compressed dictionary produces a parse
failure instead of being accepted or misreported as a bad password.

Thirteen controls use actual writer-generated RC4-40 dictionaries and the
same identifiers, testing normal/forced repair, empty/user/owner passwords
and wrong-password disposition. The initial selector had four failures.
Valid uncompressed dictionaries retain password behavior. Full Release:
18,911 passed, zero failed/skipped. Solution: zero errors/two retained
machine-feed NU1507 warnings.

Four native PDFs independently use a MuPDF-generated encryption dictionary.
Two uncompressed controls remain unchanged; both compressed controls now
fail explicitly (one was previously accepted, one already failed during
repair). Existing repair replaces the initial specific diagnostic with
bad /Encrypt when the compressed dictionary cannot be rediscovered.
MuPDF still returns the catalog for both forbidden controls. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the
compressed-Length baseline.

This checks direct trailer reference identity, not arbitrary indirect-reference
chains or an eager global forbidden-object inventory. General encryption
dictionary schema and parser-wide budgets remain separate.

## 2026-11-05 object-stream compressed Length references

ISO 32000-1 7.5.7 (printed p45) prohibits storing the object
representing an object stream's Length value in an object stream. The shared
header reader now rejects direct Length references to indexed compressed
objects or its own declared members before publishing the index. A scoped
pre-load check prevents loading the Length member through its own container
recursively. Ordinary stream lengths may still be compressed.

Twenty controls cover separate containers in both physical/recovery orders,
direct/indirect ObjStm types, normal/forced repair, valid uncompressed indirect
lengths, ordinary stream bytes and own-container Length members. The initial
sixteen-case selector had eight failures; four own-container controls were
added after the guard, without executing potentially recursive old-code
fixtures. Full Release: 18,898 passed, zero failed/skipped. Solution: zero
errors/two retained machine-feed NU1507 warnings.

Sixteen native PDFs preserve eight valid outputs unchanged and prevent eight
previous target exposures: four now give explicit Length errors and four end
repaired/null, depending on recovery order. Ordinary stream bytes are pinned
in unit controls; the native helper's Type field only describes dictionaries.
MuPDF still exposes all eight forbidden targets. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the
header-integer baseline.

This enforces the direct-reference relationships while loading affected
members; it does not scan all stream dictionaries eagerly, reject a Length
object loaded independently before its use is known, or validate arbitrary
indirect-reference chains. Recovery may provisionally index a member before
a different later container identifies its compressed Length; subsequent
access rejects it. A global forbidden-object inventory, encryption dictionary
exclusion and byte-limited/resource enforcement remain separate.

## 2026-11-05 object-stream header integer spellings

ISO 32000-1 7.5.7 (printed p46) requires integer header pairs;
7.3.3 (printed p14) defines their single optional sign and digits.
The shared complete header reader now uses the same scoped lexical checks
as member bodies, preventing permissive repeated-sign normalization before
both lazy indexing and recovery. Positive signs, leading zeroes, signed
zero and comments remain supported.

Eighteen controls cover malformed number/offset spellings, existing
real/negative failures, valid forms, normal/forced repair, preservation of
uncompressed identities and atomic rejection of invalid later pairs.
The initial selector had six failures; refining four later-pair controls
to positive-normalizing repeated signs exposed four more failures.
Full Release: 18,878 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Twenty-eight native PDFs preserve eight valid target outputs unchanged.
All twenty malformed controls end repaired with no target: ten previously
exposed target dictionaries disappear; six were already null; four later-pair
controls have no Type even when their numeric member had been indexed, so
the unit controls independently assert absence of partial indexing. MuPDF
agrees on all eight positives and sixteen malformed null results, but still
exposes four malformed targets. Fresh pinned 435-PDF/870-result full corpus
JSON is wholly unchanged against the name-escape baseline.

This does not validate original numeric spellings already normalized while
reading the outer stream dictionary, change metadata dependencies or prove
complete First/offset semantics. Byte-limited parsing and resource budgets
remain separate.

## 2026-11-05 compressed name escape grammar

ISO 32000-1 7.3.5 (printed pp16-17) excludes null from names and
requires number-sign escapes to contain two hexadecimal digits.
Compressed-member lexing now rejects malformed escapes and decoded null
bytes, including dictionary keys and nested values. Default lexer recovery
is unchanged.

Twenty-three controls cover missing/invalid hex digits, decoded nulls, nested
names/keys, empty names, escaped delimiters/whitespace/number signs, a high-byte
fallback, decoded values, repeated lookup, lazy isolation and default lexer
compatibility. The initial selector had thirteen failures. Full Release:
18,860 passed, zero failed/skipped. Solution: zero errors/two retained
machine-feed NU1507 warnings.

Thirty-eight native PDFs preserve sixteen valid outputs unchanged and reject
twenty-two previously accepted malformed names with explicit diagnostics.
MuPDF reports no probe exceptions; absent Type is not decoded-name agreement.
Positive decoded names are pinned in unit controls. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the
numeric-token baseline.

This does not change the existing Unicode/Latin-1 representation or claim
complete byte-identity handling for names. Parser-wide strictness,
object-stream header/reference rules and byte-limited/resource enforcement
remain separate.

## 2026-11-05 compressed numeric token spelling

ISO 32000-1 7.3.3 (printed p14) defines decimal numbers with an
optional single sign and, for reals, a decimal period. Compressed-member
lexing now rejects repeated signs, digitless periods and trailing garbage
that the shared permissive number parser previously normalized/truncated.
It preserves the default lexer's existing recovery behavior.

Twenty-eight controls cover malformed direct/nested tokens, signed integers,
leading/trailing decimal periods, leading zeroes, exact ordinary numeric
values, repeated lookup, lazy isolation and default lexer compatibility.
The initial selector had fifteen failures. Full Release: 18,837 passed,
zero failed/skipped. Solution: zero errors/two retained machine-feed NU1507
warnings.

Forty-eight native PDFs preserve twenty-two valid outputs unchanged and
reject twenty-six previously accepted malformed tokens with explicit
diagnostics. MuPDF reports no probe exceptions; absent Type is not numeric
value agreement. Positive ordinary numeric values are pinned in unit
controls. Fresh pinned 435-PDF/870-result full corpus JSON is wholly
unchanged against the dictionary-entry baseline.

This is spelling validation, not a change to numeric precision/ranges,
reference generation policy, name grammar or object-stream headers.
Parser-wide strictness and byte-limited/resource enforcement remain separate.

## 2026-11-05 compressed dictionary entry grammar

ISO 32000-1 7.3.7 (printed p18) requires name keys, object values and
unique keys within a dictionary. Compressed-member lexing now rejects non-name
keys, a closer in place of a value, and duplicate decoded keys instead of
skipping, substituting null or overwriting. Default lexer recovery is unchanged.

Twenty-six controls cover each failure, nested dictionaries, escaped duplicate
names, null values, scoped/case-sensitive keys, nested references, empty names,
repeated lookup, lazy isolation and default lexer compatibility. The initial
selector had seventeen failures. Full Release: 18,809 passed, zero failed/skipped.
Solution: zero errors/two retained machine-feed NU1507 warnings.

Forty-eight native PDFs preserve eighteen valid outputs unchanged and reject
thirty previously accepted malformed dictionaries with explicit diagnostics.
MuPDF reports no probe exceptions; absent Type is not complete entry validation.
Valid Target dictionaries agree. Fresh pinned 435-PDF/870-result full corpus
JSON is wholly unchanged against the hexadecimal-string baseline.

Numeric/name grammar, parser-wide strictness and byte-limited/resource
enforcement remain separate. This does not change recovery for general
uncompressed dictionaries or content-stream lexing.

## 2026-11-05 compressed hexadecimal string grammar

ISO 32000-1 7.3.4.3 (printed p16) permits hexadecimal digits and
whitespace inside the enclosing angle brackets, with an odd final nibble
padded with zero. Compressed-member lexing now rejects other bytes instead
of silently discarding them, and explicitly diagnoses a missing closer.
Default lexer recovery remains unchanged.

Twenty-one controls cover invalid characters/comments/delimiters, nested
strings, missing closers, exact decoded bytes, all six PDF whitespace bytes,
odd nibble padding, lazy isolation and default lexer compatibility. The
initial selector had eleven failures. Full Release: 18,783 passed, zero
failed/skipped. Solution: zero errors/two retained machine-feed NU1507 warnings.

Thirty-six native PDFs preserve twelve valid outputs unchanged. Twenty-four
malformed inputs now produce explicit hexadecimal diagnostics: eighteen
previously accepted arbitrary bytes, while six missing-closer cases already
failed the member boundary check. MuPDF reports no probe exceptions; absent
Type does not establish decoded-byte agreement. Positive decoded bytes are
pinned in unit controls. Fresh pinned 435-PDF/870-result full corpus JSON is
wholly unchanged against the literal-closer baseline.

Discarded dictionary keys/missing values, numeric/name grammar and
byte-limited/resource enforcement remain separate.

## 2026-11-05 compressed literal string termination

ISO 32000-1 7.3.4.1 (printed p14) requires literal strings to be
enclosed in parentheses. The internal compressed-member lexer mode now rejects
EOF while parentheses remain unbalanced, including an escaped last closer or
trailing backslash/line continuation. Default lexer recovery is unchanged.

Nineteen controls cover malformed direct/nested strings, exact decoded bytes
for balanced parentheses and escaped/octal/continued strings, repeated lookup,
lazy member isolation and default lexer compatibility. The initial selector
had ten failures. Full Release: 18,762 passed, zero failed/skipped. Solution:
zero errors/two retained machine-feed NU1507 warnings.

Thirty-two native PDFs preserve sixteen valid outputs unchanged. Sixteen
malformed inputs now explicitly report unterminated literal strings: twelve
previously returned partial strings; four nested cases already failed on
missing container closers. MuPDF reports no probe exceptions; absent Type is
not proof that complete string contents agree. Exact decoded positive bytes
are covered by the unit controls. Fresh pinned 435-PDF/870-result full corpus
JSON is wholly unchanged against the container-closer baseline.

Hexadecimal grammar, discarded dictionary keys/missing values, numeric/name
grammar and byte-limited/resource enforcement remain separate.

## 2026-11-05 compressed container closing delimiters

ISO 32000-1 7.3.6 and 7.3.7 (printed p18) require arrays and
dictionaries to be enclosed in their respective delimiters. Compressed member
parsing now explicitly requires closing delimiters, including nested containers.
The internal lexer mode is enabled only for compressed member bodies; existing
general/content lexer recovery behavior remains unchanged.

Twenty-one controls cover EOF after openers, entries and comments, nested
containers, complete empty/nonempty containers, repeated lookup, lazy isolation
and default-lexer compatibility. The initial selector had twelve failures.
Full Release: 18,743 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Thirty-six native PDFs preserve sixteen valid outputs unchanged and reject
twenty previously accepted unterminated containers with explicit diagnostics.
MuPDF reports no probe exceptions; absent Type results do not prove full
container acceptance. The valid Target dictionaries agree. Fresh pinned
435-PDF/870-result full corpus JSON is wholly unchanged against the
nested-value baseline.

This does not validate discarded dictionary keys, missing values before a
closing delimiter or complete string syntax. Lexing still uses the decoded
buffer with a post-parse member boundary check, not a byte-limited reader.
Parser-wide resource budgets remain separate.

## 2026-11-05 compressed nested object values

ISO 32000-1 7.3.1 (printed p13), 7.3.6 and 7.3.7 (printed p18)
require array elements and dictionary values to be PDF objects. Compressed
member loading now rejects parsed keywords recursively within these containers
before caching. It does not resolve nested references or change the shared
lexer used for content-stream operators.

Seventeen controls cover nested operators, object wrappers and unmatched
closers, valid references/names/strings/comments, repeated lookup and lazy
isolation of other members. The initial selector had twelve failures.
Full Release: 18,722 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Thirty native PDFs preserve ten valid outputs unchanged and reject twenty
previously accepted invalid containers with an explicit parse diagnostic.
MuPDF reports no exception for these probes; its absent Type result for
most containers does not establish acceptance of their complete contents.
The valid Target dictionaries agree. Fresh pinned 435-PDF/870-result corpus
full JSON is wholly unchanged against the required-Type baseline.

This validates retained parsed values, not complete source grammar: skipped
dictionary keys, substituted missing values and missing delimiters in the
permissive lexer remain separate. No byte-limited parsing or new resource
budget is claimed.

## 2026-11-05 required object-stream type

ISO 32000-1 7.5.7 Table 16 (printed p46) requires Type to be the name
ObjStm. The shared header reader now validates the resolved value before
decoding/indexing instead of trusting any stream selected by a compressed
xref entry. Recovery uses the same resolved-name classification, retaining
direct names and recovering valid indirect names that it previously skipped.
No xref-stream-only directness restriction is imposed.

Twenty-one controls cover missing/null/wrong types, case sensitivity,
indirect names/non-names, cached member lookup and reentrant type resolution.
The initial fifteen-case selector had twelve failures. A scoped in-progress
set prevents type resolution from recursively loading the same stream header;
it is unwound even when resolution fails. This is a dependency-resolution
guard, not a normative ban on all compressed metadata. Full Release: 18,705
passed, zero failed/skipped. Solution: zero errors/two retained machine-feed
NU1507 warnings.

Thirty-four native PDFs preserve three valid outputs unchanged and recover
one previously lost valid indirect-name target. Thirty malformed/dependent
controls produce twenty-eight repaired null outcomes and two explicit
post-repair type errors; fifteen previously exposed targets in normal lookup.
MuPDF still returns targets for fifteen malformed normal-index inputs, while
agreeing on all four valid target lookups and fifteen malformed forced-repair
null results. Dispositions and dependency limits are explicit. Fresh pinned
435-PDF/870-result corpus full JSON is wholly unchanged against the
trailing-value baseline.

Type dependencies that themselves require an unfinished object-stream
header are not claimed supported. Extends collections, other forbidden
object categories, complete delimiters and parser-wide budgets remain
separate.

## 2026-11-05 compressed member trailing tokens

ISO 32000-1 7.5.7 (printed p46) requires N consecutive object values,
with header offsets identifying their starts, and excludes obj/endobj
wrappers. Lazy loading now requires only whitespace/comments between the
parsed value and its recorded end. Extra values, keywords and closing
tokens explicitly raise PdfParseException before caching; the existing
specific stream-value diagnostic remains intact.

Nineteen controls include thirteen initially red trailing-data/isolation
cases, four whitespace/comment positives and two next-member controls.
Repeated invalid requests cannot become cache hits. Three older boundary
isolation fixtures now use complete second values rather than trailing
closing tokens; their malformed first values still consume the next member
and are rejected. Full Release: 18,684 passed, zero failed/skipped. Solution:
zero errors/two retained machine-feed NU1507 warnings.

Twenty-six native PDFs preserve eight valid outputs unchanged. Eighteen
extra-token values now give the explicit diagnostic instead of silently
returning the first value. MuPDF still exposes the first dictionary's Type
in four malformed dictionary cases and lacks a Type without an exception
for other malformed probes. Positive outputs agree; it additionally
reports repair on the normal commented-null positive and null-plus-true
negative. Fresh pinned 435-PDF/870-result corpus full JSON is wholly
unchanged against the keyword-value baseline.

This uses existing whitespace/comment tokenization; it is not a byte-limited
lexer or complete closing-delimiter validator. Recursive container grammar,
remaining object-stream dictionary/forbidden-object rules and parser-wide
budgets remain separate. Other members remain on demand.

## 2026-11-05 compressed keyword and missing values

ISO 32000-1 7.3.1 (printed p13) defines PDF object types; 7.5.7
(printed p46) requires N object values and explicitly excludes obj/endobj
keywords from their representation. Lazy loading now rejects a parsed
PdfKeyword or missing value before caching instead of returning keywords
or silently replacing whitespace/comment-only bodies with PdfNull.
Literal null remains a valid object.

Eighteen added controls include fourteen initially red keyword/missing
value/isolation cases, repeated lookup in normal/forced-recovery paths,
and valid false, real, name and commented literal-null values. Existing
standalone-reference/nested-reference controls remain unchanged. Full
Release: 18,665 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Thirty-six native PDFs preserve twelve valid outputs unchanged. Twenty-four
keyword/missing-value inputs now give the explicit diagnostic instead of
returning a keyword or substituted null. MuPDF returns null for those probes
without surfacing an error. Positive lookup outputs agree; MuPDF additionally
reports repair on the normal-index literal-null and commented-null controls.
Fresh pinned 435-PDF/870-result corpus full JSON is wholly unchanged against
the stream-value baseline.

Only the requested top-level member value is checked; this does not change
the general lexer, recursively validate keyword values inside containers,
or claim complete closing-delimiter/trailing-token validation. Remaining
object-stream dictionary/forbidden-object rules and budgets are separate.

## 2026-11-05 forbidden compressed stream values

ISO 32000-1 7.5.7 (printed p45) explicitly prohibits stream objects inside
object streams. Lazy loading now checks whether a parsed dictionary is
followed by a stream keyword within that member's recorded boundary and
raises PdfParseException before caching it as an ordinary dictionary.
Whitespace/comments are recognized by the existing lexer; names, strings,
dictionary Type entries and comments containing the word stream are not
mistaken for a stream body.

Ten controls include six initially red stream-value/isolation cases and
four dictionary positives, through normal and forced-recovery paths.
Repeated invalid requests cannot become cache hits. A valid dictionary
remains usable when another unrequested member is a prohibited stream.
Full Release: 18,647 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Sixteen native PDFs preserve eight valid dictionary outputs unchanged.
Eight prohibited values now give the explicit diagnostic instead of
silently returning their dictionary. MuPDF still exposes the dictionary
Type in two malformed ObjStm-typed cases; other malformed probes lack a
Type, without an exception. All positive outputs/repair states agree.
These differing dispositions are documented, not normative authority.
Fresh pinned 435-PDF/870-result corpus full JSON is wholly unchanged against
the member-boundary baseline.

This does not parse a nested stream's payload or validate its Length/filter
schema. Remaining forbidden encryption/length objects, complete delimiter/
trailing-token validation, object-stream schema and global budgets remain
separate. Other members are still parsed only on demand.

## 2026-11-05 compressed member consumption boundaries

ISO 32000-1 7.5.7 (printed p46) identifies each member's start by its
header offset and requires N consecutive objects. The validated header
index now records each member's end (the next offset, or decoded EOF for
the last member). Lazy loading checks lexer consumption before caching,
rejecting values that consume bytes from a following member instead of
silently accepting a cross-member array, dictionary, string or alias.

Ten controls include five initially red cross-member cases, five complete
value positives and normal/forced-recovery paths. The following member is
loaded first and remains usable after the malformed member is requested.
Repeated invalid requests cannot become cache hits. Full Release: 18,637
passed, zero failed/skipped. Solution: zero errors/two retained machine-feed
NU1507 warnings.

Twenty native PDFs retain ten complete-value outputs unchanged. Ten
cross-boundary values now give the explicit diagnostic instead of caching
the consumed result; two previously aliased the next dictionary. MuPDF
returns null for the malformed probes rather than surfacing an error.
Positive outputs agree and repair reporting retains the fixture state.
Fresh pinned 435-PDF/870-result corpus full JSON is wholly unchanged against
the standalone-reference baseline.

This is a post-parse consumption check, not a byte-limited lexer or global
resource bound. It does not claim complete closing-delimiter/last-member
validation, trailing-token exhaustion, stream-valued-member enforcement or
all object-stream rules. Bodies remain on demand and no member data is
copied merely to impose this check.

## 2026-11-05 compressed standalone reference values

ISO 32000-1 7.5.7 (printed p46) explicitly prohibits an object-stream
member consisting solely of an indirect reference, while permitting
references inside arrays and dictionaries. Lazy member loading now raises
PdfParseException before caching a standalone PdfRef instead of allowing
Resolve to alias another indirect identity. Recovery still indexes headers
without eagerly parsing bodies; an invalid requested body cannot become
valid merely because recovery ran.

Twelve controls include six initially red standalone-reference/isolation
cases, repeated invalid lookup, six valid primitive/container types and
nested references. A valid member remains usable when another unrequested
member is a forbidden reference. Normal and forced-recovery paths are
covered. Full Release: 18,627 passed, zero failed/skipped. Solution: zero
errors/two retained machine-feed NU1507 warnings.

Twenty native PDFs retain twelve valid outputs unchanged. Eight forbidden
values now give the explicit diagnostic; four previously resolved to the
catalog through an alias and four resolved null through missing/self
references. MuPDF returns null for all forbidden values rather than
exposing a reference/error, so rejection disposition differs. Valid lookup
outputs agree; MuPDF additionally reports repair on the normal-index
literal-null control. Fresh pinned 435-PDF/870-result corpus full JSON is
wholly unchanged against the offset-order baseline.

Stream-valued members, complete member-boundary/exhaustion validation,
remaining object-stream schema and parser-wide budgets remain separate.
This only rejects a parsed standalone reference, not nested references or
reference-looking string contents.

## 2026-11-05 object-stream member offset order

ISO 32000-1 7.5.7 (printed p46) explicitly requires increasing member
offsets while permitting arbitrary object-number order. The shared header
reader now rejects equal/decreasing offsets before indexing any members.
This prevents aliases/reordered bodies in normal lazy lookup and recovery
without requiring sorted object numbers or eagerly parsing bodies.

Eight controls include four initially red equal/decreasing cases exercised
through normal and forced-recovery paths, plus four valid increasing-offset
controls with both object-number orders. Existing unrelated malformed-body
isolation remains covered. Full Release: 18,615 passed, zero failed/skipped.
Solution: zero errors/two retained machine-feed NU1507 warnings.

Twelve native PDFs preserve four valid outputs unchanged. Eight malformed
cases now resolve null with repair reported; six previously returned aliased/
reordered dictionaries, while two already lacked a dictionary type but
still installed invalid header members. MuPDF agrees on all valid outputs
and seven malformed null outcomes, but recovers a reordered dictionary in
one forced-repair case and does not report repair for the four malformed
normal-index fixtures. These explicit recovery differences do not determine
the normative rule. Fresh pinned 435-PDF/870-result corpus JSON is wholly
unchanged against the recovery-header baseline.

First-offset semantics, object-number-zero handling, complete body/value
restrictions, remaining object-stream schema and parser-wide budgets remain
separate. Existing zero-count and lazy-body behavior remain unchanged.

## 2026-11-05 object-stream recovery header identity

ISO 32000-1 7.5.7 (printed p46) requires N integer object-number/offset
pairs, with First locating the first body. Recovery now reuses the lazy
loader's complete header validation before installing any compressed
identities. This removes its unchecked object-number narrowing and
partial-index mutation. Unsupported/negative numbers, invalid offsets,
non-integer/truncated pairs, duplicate numbers and header pairs extending
past First explicitly raise the existing parse diagnostic; recovery skips
that stream without overwriting existing uncompressed identities.

Sixteen controls include thirteen initially red cases, valid cached lookup
with an unrelated malformed body and an empty stream. No body is eagerly
parsed. Full Release: 18,607 passed, zero failed/skipped. Solution: zero
errors/two retained machine-feed NU1507 warnings.

Seventeen native PDFs retain three valid outputs unchanged. Fourteen
malformed headers preserve the original target and report repair; eleven
previously raised a second header exception after corrupting the index,
while three previously retained the target but could install unrelated
invalid/partial entries. MuPDF agrees on all valid outputs and two malformed
target preservations; four malformed headers instead redirect to the
compressed target and eight lose the target. All native fixtures trigger
repair by construction. These recovery disagreements are recorded, not
treated as a normative recovery policy. Fresh pinned 435-PDF/870-result
corpus JSON is wholly unchanged against the dictionary-bounds baseline.

Increasing offsets, object-number-zero semantics, all remaining object
stream restrictions and parser-wide budgets remain separate. The shared
index is bounded by the existing decoded-header/count guard, not a new
global resource policy.

## 2026-11-05 object-stream dictionary integer bounds

ISO 32000-1 7.5.7 Table 16 (printed p46) requires integer N and First
entries for object streams. Normal member loading and recovery now share
resolved PdfInteger validation before narrowing. Missing/null, real,
negative, unsupported or decoded-header-out-of-range metadata explicitly
raises PdfParseException. Existing recovery skips malformed streams and
does not reinstall their compressed members. Valid indirect integer
metadata remains supported; no xref-stream-only directness rule is applied.

Twenty-four controls cover types, missing entries, overflow, integer
spellings, indirect values, normal lazy loading and forced recovery.
The initial 18-case selector had twelve failures. Valid member lookup/cache
behavior and existing malformed-unrelated-body isolation remain unchanged.
Full Release: 18,591 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Twenty-six native controls preserve all four valid integer/forced-recovery
outputs. All 22 malformed controls now resolve null with repair reported;
eight formerly accepted targets disappear, ten formerly repeated header
errors terminate through existing recovery, and four already-null outcomes
remain null. MuPDF agrees on the valid cases, but accepts real-valued
integral metadata and several invalid First/overflow cases; fractional
non-integral values and invalid N types instead resolve null, without
repair unless the fixture already has invalid startxref. These are explicit
compatibility/recovery differences, not normative proof. Fresh pinned
435-PDF/870-result corpus JSON is wholly unchanged against the classic
duplicate baseline.

Increasing member offsets, complete recovery-header identity validation,
all object-stream dictionary constraints and parser-wide budgets remain
separate. Existing zero-count/header bounds and on-demand body parsing
are retained; this does not claim complete object-stream conformance.

## 2026-11-05 classic xref same-section uniqueness

ISO 32000-1 7.5.4 (printed p40) prohibits an object number from appearing
in multiple subsections of one section, while explicitly allowing
subsections in any order. The classic parser now tracks decoded numbers
per section and explicitly raises PdfParseException on repeats, using
existing recovery instead of silently choosing the first entry. The set
resets at every section, preserving newest-revision precedence; it grows
with decoded entries, not declared subsection sizes.

Ten controls include six initially red same-section cases in fixed/fallback
paths: free-first, in-use-first and overlapping ranges. Four positives
retain disjoint out-of-order subsections and legitimate repeated numbers
across revisions, including a newer free entry. Short fallback formatting
remains compatibility behavior. Full Release: 18,567 passed, zero
failed/skipped. Solution: zero errors/two retained machine-feed NU1507
warnings.

Ten native PDFs retain all four positives unchanged and report repair on
six duplicates, recovering four formerly lost targets. MuPDF accepts the
three fixed-format duplicate cases without repair, retaining null targets
for two of them; it repairs the three short-format duplicates. Standard
fixed-format positives agree, but it additionally repairs both short-format
positives and recovers the freed target in one. These are explicit recovery/
format-compatibility disagreements. Fresh pinned 435-PDF/870-result corpus
JSON is wholly unchanged against the status-token baseline.

Complete classic syntax/free-list semantics, normative generation maxima
and parser-wide memory/resource budgets remain separate.

## 2026-11-05 classic xref entry status tokens

ISO 32000-1 7.5.4 (printed p41) requires n for in-use entries and f
for free entries. The fallback parser now requires those exact tokens
instead of treating every other keyword as free. Invalid status tokens
explicitly raise PdfParseException and use existing recovery.

Nine controls include four initially red keyword cases, three existing
wrong-object-type repairs and unchanged n/f positives. Final fixtures
use fixed-format rows apart from the deliberately malformed status token;
the free control links object 0 to deleted object 2 with next generation 1.
This isolates the status requirement from short-entry formatting.
Full Release: 18,557 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Nine native PDFs retain both valid outputs unchanged and report repair on
all seven malformed statuses (three already repaired); four formerly lost
targets recover. MuPDF agrees on all outputs and repair states. Fresh
pinned 435-PDF/870-result corpus JSON is wholly unchanged against the
classic-generation baseline. Accepted whitespace/short-format compatibility,
normative generation maxima, full free-list semantics and resource budgets
remain separate.

## 2026-11-05 classic xref generation storage

The fallback classic-table parser now checks generation values before its
long-to-int cast, complementing the binary-entry storage fix. Negative or
larger-than-int generations explicitly raise PdfParseException and use
existing recovery; they cannot wrap to generation zero or negative values.
The fixed five-digit path already fits this representation and is unchanged.
ISO 32000-1 7.5.4 (printed pp40-41) defines the generation field; this is a
lossless storage boundary, not the normative 65535 generation maximum.

Nine regression cases include five initially red invalid-value theories,
each running in-use/free variants after correction, fixed/fallback controls
and explicit-reference int.MaxValue compatibility. Values beyond 65535 and
short fallback formatting are retained compatibility, not conformance
positives. Full Release: 18,548 passed, zero failed/skipped. Solution: zero
errors/two retained machine-feed NU1507 warnings.

Sixteen native PDFs retain six positive/compatibility outputs unchanged;
all ten invalid entries now report repair. Eight formerly lost targets
recover; two formerly wrapped identities now explicitly recover. MuPDF
agrees on all ten invalid repairs and both standard fixed-format controls.
It additionally repairs the three short-format compatibility controls and
returns null for generation 99999 in either format. Fresh pinned
435-PDF/870-result corpus JSON is wholly unchanged against the chain-offset
baseline. Complete classic entry syntax, normative generation maxima,
free-list semantics and resource budgets remain separate.

## 2026-11-05 cross-reference chain offset types

Prev and XRefStm now require present non-null values to be direct integers
within the input byte range. Fractional Prev values no longer truncate;
negative or wrong-type Prev and wrong-type XRefStm no longer silently end
or omit part of the index. Invalid values explicitly raise PdfParseException
and use existing recovery. Null retains absent-entry semantics.

Primary provenance: ISO 32000-1 Table 17 (printed p50), Table 19 (p51),
and 7.5.8.2 direct-entry restrictions (p49), plus the
[official PDF 2.0 Table 15 correction](https://pdf-issues.pdfa.org/32000-2-2020/clause07.html#H7.5.5)
specifying direct integer Prev. The 2008 Table 15 printed p43 incorrectly
says Prev shall be indirect; that conflicting wording was not silently
used to justify the opposite policy.

Eighteen controls include twelve invalid values (eleven initially red),
three integer-offset positives and three null positives. Latest free-entry
precedence and supplemental-stream lookup are pinned. The hybrid control
was corrected against 7.5.8.4 (printed p52): its free entry belongs in the
previous main section with generation 65535, not the current update.
Full Release: 18,539 passed, zero failed/skipped. Solution: zero errors/two
retained machine-feed NU1507 warnings.

Eighteen native PDFs retain all six positives unchanged and report repair
for all twelve invalid offsets (one already repaired). Ten formerly lost
targets recover. MuPDF agrees on all positives and four invalid repairs;
it retains null targets without repair for the other eight invalid cases.
These are explicit recovery-policy disagreements. Fresh pinned
435-PDF/870-result corpus JSON is wholly unchanged against the binary-entry
baseline. Cycle reporting, reused supplemental streams, complete hybrid
schema and parser-wide budgets remain separate.

## 2026-11-05 xref-stream entry storage identity

ISO 32000-1 7.5.8.3 Table 18 (printed p51) assigns fields to byte
offsets, generations, object-stream numbers and member indices. Binary
decoding now preserves all eight bytes in ulong before checking values
against their destination representation: known-type third fields fit int,
type-1 offsets fit long and type-2 container numbers fit int. Unsupported
values explicitly raise PdfParseException and use existing recovery, rather
than wrapping generations/indices or later narrowing a container number.
Unknown types still denote null as required by 7.5.8.3, even with high-bit
values and otherwise oversized fields. Existing precedence is unchanged.

Seventeen controls include eight initially failing unsupported-field cases,
retained direct/compressed targets, generation compatibility and unknown
types through ulong.MaxValue. int.MaxValue generation is checked through
explicit PdfRef lookup, not a parsed reference token (the lexer retains
its existing five-digit generation policy). Values beyond 65535 are
compatibility boundaries, not normative generation limits; enforcing the
PDF generation maximum everywhere is separate. Full Release: 18,521 passed,
zero failed/skipped. Solution: zero errors/two retained machine-feed
NU1507 warnings.

Twelve native controls retain four positive/compatibility outputs unchanged
and report repair on eight unsupported cases (one already repaired on
target access). The free-generation case recovers a formerly lost target;
other cases now distinguish recovery from silently wrapped identities.
MuPDF agrees on three standard positives and repairs of offset/container
overflow, returns null for the 99999-generation compatibility and free-entry
cases, and accepts oversized generation/member-index values without repair.
Those are explicit reference disagreements. Fresh pinned 435-PDF/870-result
corpus JSON is wholly unchanged against the row-completeness baseline.

This checks lossless storage, not full field semantics: free-list links,
normative generation maxima, Size consistency across updates and parser-wide
budgets remain separate.

## 2026-11-05 xref-stream declared row completeness

ISO 32000-1 7.5.8.2 Table 17 (printed p50) relates each subsection's
Index count to W's total byte length per entry. Before allocating xref
capacity or installing entries, the parser now compares the total declared
rows with complete decoded rows. Short data explicitly raises
PdfParseException and uses existing whole-section recovery instead of
silently breaking out of individual subsection loops. The comparison uses
division and a long count, not attacker-sized allocation or multiplication.

Fourteen controls cover partial/missing final rows, missing target rows,
empty data and split subsections; eight were red before correction.
Complete/split controls and surplus decoded-byte compatibility remain
unchanged. Surplus acceptance is retained behavior, not a complete stream
conformance claim. Full Release: 18,504 passed, zero failed/skipped;
solution: zero errors/two retained machine-feed NU1507 warnings.

Twelve native PDFs retain three positives and now report repair on all
nine truncated inputs (two already repaired before this change). Three
previously lost targets recover. MuPDF agrees on all target results and
on repair except for two one-byte-short inputs, which it accepts without
repair. Fresh pinned 435-PDF/870-result corpus JSON is wholly unchanged
against the raw-width baseline.

Entry field ranges/free lists, exact highest-used Size across updates and
resource budgets remain separate.

## 2026-11-05 xref-stream raw field widths

ISO 32000-1 7.5.8.2 Table 17 (printed p50) defines W as an array of
integer byte widths, including zero-width defaults. Raw widths now require
PdfInteger values in the existing supported 0..8 range before conversion.
Fractional widths no longer truncate into apparently valid row layouts.
Invalid array shapes and elements explicitly raise PdfParseException and
use existing recovery rather than depending on cast/null exceptions.

Nineteen controls cover all three fractional core fields, fractional
extension fields (including negative fractions), oversized/negative widths,
indirect values, invalid array shapes and all-zero rows. Six were red
before correction. Positives preserve type/generation zero-width defaults,
eight-byte offsets and the existing extra-field stride policy. The latter
is a compatibility control, not a claim that extra fields conform to every
PDF version. Full Release: 18,490 passed, zero failed/skipped. Final solution
build: zero errors/two retained machine-feed NU1507 warnings.

Ten native controls retain all four compatibility positives unchanged;
five fractional cases now report repair, while an oversized case already
repaired. All retain/recover the target. MuPDF agrees on three standard
positives and fractional offset repair, but returns null for the extra-field
compatibility layout and fractional third field, errors on fractional first
field, and accepts fractional extension/oversized cases without repair.
These are explicit reference disagreements. Fresh pinned 435-PDF/870-result
corpus JSON is wholly unchanged against the Index/Size baseline.

Complete row counts, field value ranges/free lists, exact highest-used Size
across updates and resource budgets remain separate.

## 2026-11-05 xref-stream Index and Size ranges

ISO 32000-1 7.5.8.2 Table 17 (printed p50) requires integer Size and
integer Index pairs, ascending non-overlapping subsections, and default
Index [0 Size]. The parser now checks those raw types, pairing, ordering,
overlap and each range against Size before narrowing object numbers.
Size is bounded to 1 through int.MaxValue + 1 and starts to int.MaxValue
by the current representation, not a normative PDF maximum. Invalid values
raise PdfParseException and enter existing repair. Null Index retains the
dictionary's absent-entry semantics (7.3.9); zero-count compatibility is
preserved without claiming conformance of every empty subsection.

Twenty-three controls cover malformed types (including indirect entries),
fractional values, negative/oversized ranges, overlap, ordering, Size
boundaries, missing/null Index and supported boundaries. Twelve malformed
cases in the initial selector were red before correction (an initial null
negative was corrected to a positive because null means absent). Full
Release: 18,471 passed, zero failed/skipped. Solution: zero errors and two
retained machine-feed NU1507 warnings.

Eleven native PDFs retain two no-repair positives and now explicitly
repair nine invalid cases, recovering the intended target. Seven previously
lost targets are recovered. MuPDF agrees on both positives and on repair
of negative Size; it retains null targets for six malformed cases, accepts
the unsorted case without repair and errors on the wrong Index type.
These are documented recovery-policy disagreements, not normative proof
from reference acceptance. Fresh pinned 435-PDF/870-result corpus JSON is
wholly unchanged against the classic-range baseline.

This does not validate all W raw types/widths, row completeness, entry fields,
free lists, exact highest-used Size across updates, or parser-wide budgets.
Hybrid/newest-section precedence remains unchanged.

## 2026-11-05 classic xref subsection ranges

ISO 32000-1 7.5.4 (printed p40) defines a subsection's first object
number and count as a contiguous object-number range. Both classic-table
entry paths previously narrowed the computed number unchecked: a start
of 4294967300 wrapped to object 4, allowing a free entry to hide the real
target without reporting repair. Negative starts/counts were also
accepted. The shared subsection header now checks nonnegative start/count
and the complete range against the current int object-number representation
before allocation or entry iteration. Violations explicitly raise
PdfParseException and enter the existing repair path. Iteration uses a
long counter so a range ending at int.MaxValue cannot overflow the counter.
This is an implementation boundary, not a normative PDF maximum.

Nine regression cases exercise fixed and fallback entries, complete
end-overflow entries (not merely truncated input), huge counts, supported
int.MaxValue endpoints and retained zero-count behavior. Five cases were
red before correction; the huge truncated count already triggered repair.
Focused Release: 210 passed; full Release: 18,448 passed, zero failed/
skipped. Solution zero errors/two retained machine-feed NU1507 warnings.

Fourteen native PDFs reproduce two wrapped-target losses and eight other
invalid-range cases, all now recovering the intended target with
Repaired=true. Four sparse/empty compatibility controls remain unchanged.
MuPDF recovers the target in all fourteen; it also repairs the two sparse
controls (whose Size is smaller than the sparse subsection), unlike
FreeDF. Those are recovery-policy controls, not clean conformance positives.
Zero-count acceptance is preserved, not asserted as a new primary rule.
Fresh 435-PDF/870-result corpus JSON is wholly unchanged against 12f5819f.
This does not validate xref-stream Index/W/Size or all classic entry fields,
subsection ordering/overlap, free-list integrity or parser-wide budgets.
Standards/native PDFs remain session-local; hosted CI/full parity is not claimed.

## 2026-11-05 compressed member index and container identity

ISO 32000-1 7.5.8.3/Table 18 (printed p51; p48 example) defines a type-2
xref entry's third field as its member index and its container reference
as implicitly generation zero. The loader previously ignored the index,
overwrote duplicate header numbers and looked up containers by number alone.
Object-stream header entries now retain index and offset per number,
reject duplicate numbers, and require the selected number/index pair
before consulting the parsed-member cache. Containers use generation-aware
zero-generation references. Violations produce explicit PdfParseException
and use the existing bounded-to-one-repair recovery path.

Six new controls were red before correction. The earlier generation test
now uses an actual nonzero member index, instead of treating three different
indices as aliases for one member (two artificial theory cases removed).
On-demand parsing still allows a valid requested body when an unrelated
member body is malformed. Focused parser/generation/annotation/encryption/
signature Release: 419 passed; full Release: 18,439 passed, zero failed/
skipped. Solution zero errors/two retained machine-feed NU1507 warnings.

Seven native PDFs retain two valid controls; three incorrect/out-of-range
indices recover the intended target with Repaired=true, while duplicate
numbers and a nonzero-generation container produce explicit errors.
MuPDF accepts all seven: three explicit repair-reporting policy differences
and two rejection disagreements. The duplicate previously redirected
FreeDF to the second body while MuPDF chose the first; FreeDF now rejects
the ambiguous header rather than choosing either. Fresh 435-PDF/870-result
corpus JSON is wholly unchanged against 1fef152d.
This is reached member identity, not complete object-stream/xref schema,
global parser resource limits or full PDF parity. External controls and
standards remain unbundled; hosted CI is not claimed.

## 2026-11-05 object header generation consistency

The ISO 32000-1 7.3.10/7.5.4 identity requirements also apply to the
actual object header, not just the reference and xref entry. Uncompressed
object loads now compare the header generation with the selected xref
generation, alongside the existing object-number comparison. A mismatch
uses the existing explicit object-header PdfParseException and repair
path. Generation-aware one-hop resolution rechecks the repaired xref,
so a reference to the old erroneous generation cannot alias the recovered
object. A subsequent reference to the actual generation resolves normally.
Xref-stream discovery still has no preselected expected object generation.

One new regression, red before correction, covers four actual/indexed
generation disagreements at correct byte offsets. Focused parser/
incremental/encryption/signature Release: 179 passed; full Release:
18,435 passed, zero failed/skipped. Solution zero errors/two retained
machine-feed NU1507 warnings.
Eight native PDFs preserve four matching header/xref controls and recover
four mismatches without resolving the old-generation reference. MuPDF
resolves all eight: four explicit identity/recovery-policy disagreements,
not independent negative confirmation. Fresh 435-PDF/870-result corpus
JSON is wholly unchanged against c07fc7ce.
This closes the reached uncompressed header-generation omission only;
complete xref validation, compressed-member identity and malformed-graph
rewrites remain separate. No standards/external PDFs bundled or hosted
CI/full parity claimed.

## 2026-11-05 indirect reference generations and incremental preservation

ISO 32000-1 7.3.10 (printed p21) identifies an object by both number and
generation; 7.5.4/7.5.6 (pp41/44) distinguish reused entries from updates
of the same object. 7.5.7 (p45) requires compressed objects to have
generation zero. The shared resolver previously discarded generation,
including on cache hits. PdfFile.GetObject(PdfRef) now reads one hop only
when the current xref entry is in use with the matching generation,
rechecking after loading/repair; missing, free and stale targets yield
PdfNull. Resolve retains its existing 32-hop bound. The by-number
GetObject(int) API deliberately remains a by-number lookup.

Bounded name-tree/media/form/namespace/DPart consumers now use that same
one-hop operation without losing their own budgets or incompleteness.
Unresolved namespace generations remain nesting-incomplete rather than
default namespaces. DPart invalid-reference diagnostics and optional-content
invalid-application warnings are preserved. Signature appearance indexing
does not invent generation-zero references for already indexed fields.

Strict reads exposed a coupled incremental writer defect: references were
reset to generation zero. The editor now supplies source xref metadata to
an additive WriteIncremental overload, preserving emitted object headers,
table/stream xref generations, Root/Info and existing references. String/
stream encryption uses the emitted generation (7.6.2, printed p58).
Existing public overload signatures remain; the legacy low-level writer
overload still defaults emitted object/trailer generations to zero, so
callers editing nonzero-generation sources must use the xref-aware overload.
Full rewrites of valid graphs still renumber objects/references to zero.

Initial twelve identity controls all failed; thirty total added controls
cover caches, repaired files, chained/compressed/free targets, bounded
validation, namespace diagnostics, incremental metadata, both xref formats,
four encryption methods, valid full rewrites and certified field appearances.
Focused validation: 7,323 passed before the writer integration; final
writer/encryption/signature selector: 69 passed. Final full Release:
18,434 passed, zero failed/skipped. Solution zero errors/two retained
machine-feed NU1507 warnings.

Eight native PDFs preserve four matching-generation targets and treat four
stale targets as null. MuPDF accepts all eight: the four primary-identity/
reference disagreements are explicit, not independent confirmation.
Fresh 435-PDF/870-result corpus JSON is wholly unchanged against 363b17a7;
the prior native overflow controls also retain their recorded outcomes.
Header/xref integrity as a whole, malformed-graph normalization on full
rewrites, complete DocMDP classification and full PDF parity remain open.
External PDFs/standards stay unbundled; hosted CI is not claimed.

## 2026-11-05 indirect reference object-number overflow

ISO 32000-1 7.3.10 (printed p21) identifies an indirect object by its
object/generation numbers; 7.3.9 treats a nonexistent target as null, not
as another numbered object. The lexer previously narrowed a long object
number to int unchecked: 4294967297 and 8589934593 both resolved to
object 1 in native catalog Probe controls.

After recognizing a complete reference token, the lexer now throws
PdfParseException for an object number above int.MaxValue, the current
PdfRef representation limit. This is an explicit unsupported-range
failure, **not a normative PDF maximum** or support for larger object
numbers. Standalone large integers, failed reference lookahead,
ReadObject(false), supported reference boundaries and existing
generation-number compatibility remain unchanged. Other object-number
read paths and integers above long.MaxValue are outside this slice.

Twelve added controls had seven failures before the fix. Focused
lexer/object-stream/fuzz Release: 113 passed; full Release: 18,404 passed,
zero failed/skipped. Solution zero errors/two retained machine-feed
NU1507 warnings. Six native PDFs preserve two supported-number controls
and explicitly reject four unsupported references, including both
catalog aliases after parser recovery. MuPDF treats the four oversized
targets as null: both avoid aliasing, but FreeDF's explicit rejection is
a documented recovery-policy difference, not identical reference output.
Fresh 435-PDF/870-result corpus JSON is wholly unchanged against
e14e9157. External PDFs and standard text are not bundled; parser-wide
identity/range coverage, full parity and hosted CI are not claimed.

## 2026-11-05 primary local choice option shape

ISO 32000-1 12.7.4.4/Table 231 (printed p445) declares the choice Opt
array's items to be text strings or exact two-text-string export/display
pairs. PDF 2.0 12.7.5.4/Table 234 retains the Opt-index relationship in
the public errata; this slice uses the legacy primary item definition,
not the reference validator's acceptance as evidence.
Recognized terminal Ch dictionaries now check local raw Opt shape:
PDFUA2-FORM-CHOICE-OPT-TYPE reports a non-array, non-string/non-pair
item, wrong pair length or known non-string pair member, once per field.
Optional null/dangling root entries remain absent; null/dangling items
or pair members are not strings. Empty arrays/strings remain valid shapes.

Root/item/pair-member references reuse the 32-hop/cycle guard. Shared
200,000-item work includes option items and pair members across all fields.
Truncation, cyclic/bounded resolution and work exhaustion produce
PDFUA2-FORM-CHOICE-OPT-INCOMPLETE. Known shape errors survive later
unknowns or work exhaustion; unknowns alone do not claim a type failure.
Nonterminal parent Opt is not projected onto children by this local pass.
Encoding, inherited-option policy, selected-value matching and complete
choice semantics remain separate; raw reader/editor behavior is unchanged.

Initial 169 authored controls had 25 failures. Focused Release:
5,449 passed; full Release: 18,392 passed, zero failed/skipped. Solution
zero errors/two retained machine-feed NU1507 warnings.
Eleven isolated native controls retain four raw-shape positives and reject
seven primary negatives. All pinned combined-reference jobs complete
normally and accept: **seven explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against range baseline 6b0f79f0. No standard text or external controls
are bundled; full form/PDF parity and hosted CI are not claimed.

## 2026-11-05 primary explicit local choice index bounds

Official PDF Association PDF 2.0 errata 12.7.5.4/Table 234 defines I
as zero-based indices into Opt. The existing bounded terminal Ch index
pass now reports PDFUA2-FORM-CHOICE-I-RANGE when a reached integer
is at least the length of an explicitly resolved local Opt array.
Negative indices retain the existing order finding. Option count is checked
without inspecting or certifying the option items' types.

Opt resolution is lazy, once per field, only after a known integer is reached.
Cyclic/over-32-hop references or unsupported non-array Opt produce separate
PDFUA2-FORM-CHOICE-I-RANGE-INCOMPLETE. Reader truncation reports both
index and range incompleteness. Known range errors survive unknown indices
and existing shared 200,000-item exhaustion; no extra unbounded scan is added.
Absent/null/dangling local Opt is outside this explicit-array predicate,
not inferred to be an empty array. Parent options are not substituted.
Missing options, inheritance policy, complete option schema, duplicate
selection semantics, required I presence and V precedence remain separate.

Initial 109 authored controls had sixteen failures; final 111 include
exact shared-work boundaries. Focused Release: 5,280 passed;
full Release: 18,223 passed, zero failed/skipped. Solution zero errors/two
retained machine-feed NU1507 warnings. Eight native controls retain four
bounded-range positives and reject four primary negatives. All pinned
combined-reference jobs complete normally and accept:
**four explicit primary/reference disagreements**. Native UA-1 and fresh
435-PDF/870-result corpus JSON are wholly unchanged against shared-text
baseline fdb71d55. No standard text or external controls are bundled;
full form/PDF parity and hosted CI are not claimed.

## 2026-11-05 shared PDFDocEncoding text-string decoding

ISO 32000-1 7.9.2.2/.3 and Annex D.3/Table D.2 specify PDFDocEncoding
for non-Unicode text strings. The shared PdfString.Text/Decode helper
previously used Latin-1, misreading all forty differing defined codes:
eight accents, thirty-one symbols/ligatures and the euro sign.
The shared helper now maps those codes to their specified Unicode values.
Outline titles, annotation text, field names/values, optional-content names
and ActualText consumers inherit the correction. Raw Bytes, font glyph
decoding and the existing UTF-16BE/LE and UTF-8 BOM paths are unchanged.
The rich-field validator reuses the shared map after retaining its strict
Unicode/undefined-byte checks, removing its duplicate character map.

The general reader's existing leniency for undefined PDFDocEncoding bytes
is retained, not newly certified as valid input. Full text-string validation,
Unicode language escape handling and complete PDF parity remain separate.
No writer encoding, raw font interpretation or binary payload is changed.

Eighty of 86 authored controls fail on unchanged production; all pass after
the correction. Controls cover every changed code at the core and across
outline/field/annotation/ActualText surfaces, unchanged raw bytes and glyph
text, plus BOM/ASCII/Latin/undefined-byte compatibility.
Focused Release: 5,317 passed; full Release: 18,112 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings.
Forty self-authored native PDFs produce 160 corrected outline-title,
field-name, field-value and annotation-content outputs, all exactly matching
MuPDF (PyMuPDF 2.0). All 160 differ on the baseline. This is text-output
agreement, not a conformance claim. Fresh 435-PDF/870-result UA corpus JSON
is wholly unchanged against terminal-entry baseline 8d5da8d0.
No standard text or external controls are bundled; hosted CI is not claimed.

## 2026-11-05 primary terminal field entry types

Official PDF Association PDF 2.0 errata 12.7.4.1/Table 226 declares
optional T a text string and optional AA a dictionary. Recognized terminal
field dictionaries now check those local raw types for all field subtypes:
PDFUA2-FORM-T-TYPE and PDFUA2-FORM-AA-TYPE. Omission/null/dangling
entries remain absent; no name or additional-actions presence is required
by this predicate. Empty strings/dictionaries remain valid raw types.
This only checks PdfString shape, not its text encoding or name syntax.

Entry references reuse the 32-hop/cycle guard. Field-reader truncation and
unknown/bounded entry references produce PDFUA2-FORM-ENTRY-INCOMPLETE.
One unknown entry does not suppress the other's known type error.
Only local entries on recognized terminal fields are checked; nonterminal
parents are not projected onto children. Full field traversal, partial-name
syntax/encoding and complete additional-actions schema remain separate.
No action is executed.

Initial 167 authored controls had 48 failures. Focused Release:
5,169 passed; full Release: 18,026 passed, zero failed/skipped. Solution
zero errors/two retained machine-feed NU1507 warnings.
Fifteen isolated native controls retain five raw-type positives and reject
ten primary negatives. All pinned combined-reference jobs complete
normally and accept: **ten explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against lock-permission baseline 93960b3d. No standard text or external
controls are bundled; full form/PDF parity and hosted CI are not claimed.

## 2026-11-05 primary signature-lock permission type

Official PDF Association PDF 2.0 errata 12.7.5.5/Table 236 declares
Lock/P a number. The existing bounded local terminal Sig Lock inspection
now reports PDFUA2-FORM-LOCK-P-TYPE for a known non-number value.
Both PdfInteger and PdfReal are retained, including zero, negative and
fractional values: this is not a permission-range or integrality assertion.
Optional null/dangling entries remain absent. References reuse the existing
32-hop/cycle guard and PDFUA2-FORM-LOCK-INCOMPLETE, whose message now
describes metadata rather than only field names. Unknown P does not
suppress known Fields type/presence errors, and unknown Fields does not
suppress a known P error. Nonterminal parent Lock is not inherited.

Initial 79 authored controls had ten failures. Focused Release:
5,002 passed; final full Release: 17,859 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings.
Nine isolated native controls retain five raw-type positives and reject
four primary negatives. All pinned combined-reference jobs complete
normally and accept: **four explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against certificate baseline 59b9151f. Permission ranges, incremental-update
policy, complete lock schema and signing enforcement remain separate.
No standard text or external controls are bundled; full signature/PDF
parity and hosted CI are not claimed.

## 2026-11-05 primary certificate-seed ASCII arrays

Official PDF Association PDF 2.0 errata 12.7.5.5/Table 238 declares
KeyUsage and SignaturePolicyCommitmentType arrays of ASCII strings.
The existing local SV/Cert path is corroborated by ISO 32000-1 Table 234
(printed p448); Table 35 (printed p86) defines single-byte ASCII strings.
Recognized terminal Sig dictionaries now inspect those raw arrays:
PDFUA2-FORM-SEED-CERT-ASCII-TYPE reports a non-array, non-string item
or inspected byte above 127, once per key/field. This does not decode
text strings: UTF-16 BOMs are not ASCII. Empty strings/arrays and all
7-bit byte values remain valid raw shapes. Optional null/dangling root
entries remain absent; null/dangling array items are not strings.

SV/Cert/array/item references reuse the 32-hop/cycle guard. Shared
200,000-item and 1 MiB inspected-byte budgets cover both keys and all
terminal fields. Truncation, unsupported dictionary shapes, unknown
references and exhausted budgets report PDFUA2-FORM-SEED-CERT-INCOMPLETE.
Known prefix/type errors survive later unknown or uninspected bytes.
Nonterminal parent SV is not inherited; other seed errors remain independent.
Complete certificate schema, policy syntax, KeyUsage payload semantics,
flag-dependent applicability and signing enforcement remain separate.

Initial 202 authored controls had 33 failures; final 204 include shared
item work and independent seed errors. Focused Release: 4,923 passed;
full Release: 17,780 passed, zero failed/skipped. Solution zero errors/two
retained machine-feed NU1507 warnings. Eighteen isolated native controls
retain eight raw-shape positives and reject ten primary negatives.
All pinned combined-reference jobs complete normally and accept:
**ten explicit primary/reference disagreements**. Native UA-1 and fresh
435-PDF/870-result corpus JSON are wholly unchanged against scalar baseline
0fa9ba6e. No standard text or external controls are bundled; full
signature/PDF parity and hosted CI are not claimed.

## 2026-11-05 primary signature-seed scalar types

Official PDF Association PDF 2.0 errata 12.7.5.5/Table 237 declares
SV/V an integer and SV/AddRevInfo a boolean. Recognized terminal Sig
dictionaries now check those local raw types independently:
PDFUA2-FORM-SEED-V-TYPE and PDFUA2-FORM-SEED-ADDREVINFO-TYPE.
Omission/null/dangling entries remain absent. Signed integers including
zero and negative values are retained by this type-only predicate;
the descriptions of capabilities 1/2/3 are not treated as an exclusive range.

SV and scalar references reuse the 32-hop/cycle guard. Field-reader
truncation, unsupported SV shape and unresolved/bounded references produce
PDFUA2-FORM-SEED-SCALAR-INCOMPLETE. An unknown scalar does not suppress
the other scalar's known type error. Nonterminal parent SV is not inherited.
Digest and lock findings remain independent. Full seed schema, flags,
capability support, revocation behavior and signing enforcement remain separate.

Initial 162 authored controls had nineteen failures; final 169 also cover
SV dictionary uncertainty/bounds and parent exclusion. Focused Release:
4,719 passed; full Release: 17,576 passed, zero failed/skipped. Solution
zero errors/two retained machine-feed NU1507 warnings.
Twelve isolated native controls retain six shape positives and reject six
primary raw-type negatives. All pinned combined-reference jobs complete
normally and accept: **six explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against digest baseline 7e75a245. No standard text or external controls
are bundled; full signature/PDF parity and hosted CI are not claimed.

## 2026-11-05 primary signature-seed digest array shape

Official PDF Association PDF 2.0 errata 12.7.5.5/Table 237 declares
DigestMethod an unordered array of names. Recognized terminal Sig
dictionaries now check local SV/DigestMethod raw shape independently of
Lock. PDFUA2-FORM-SEED-DIGEST-TYPE reports a known non-array value or
non-name item, aggregating once per terminal field. Omission/null/dangling
root entries remain absent; null/dangling items are not names. Empty,
unordered, repeated and custom-name arrays are retained by this type-only
predicate. This is not an algorithm allow-list or signing-policy decision.

SV/array/item references reuse the 32-hop/cycle guard; field-reader
truncation, unsupported SV shape and shared 200,000-item exhaustion
produce PDFUA2-FORM-SEED-DIGEST-INCOMPLETE. Known item errors survive
later unknown items. Lock and seed uncertainty do not suppress each
other's known findings. Nonterminal parent SV is not inherited.
Full seed schema, flags, digest vocabulary/extensions/deprecation,
credential-specific applicability and signing enforcement remain separate.

Initial ninety authored controls had fourteen failures. Final ninety-four
include shared work, parent exclusion and independent seed/lock errors.
Focused Release: 4,550 passed; full Release: 17,407 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings.
Nine isolated native controls retain five shape positives and reject four
primary raw-type negatives. All pinned combined-reference jobs complete
normally and accept: **four explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against lock-field baseline 97a33e8b. No standard text or external
controls are bundled; full signature/PDF parity and hosted CI are not claimed.

## 2026-11-05 primary signature-lock field-name array shape

Official PDF Association PDF 2.0 errata 12.7.5.5/Table 236 declares
signature-lock Fields an array of text strings, required for Action Include
or Exclude. Recognized terminal Sig dictionaries now check local Lock:
PDFUA2-FORM-LOCK-FIELDS-TYPE reports a known non-array Fields or non-string
item; PDFUA2-FORM-LOCK-FIELDS-MISSING reports resolved absence under a
resolved Include/Exclude action. Empty arrays remain valid shapes.
Other field types and nonterminal parent Lock inheritance are excluded.
This only checks raw PdfString item types: text encoding, fully qualified
name syntax, identity/existence and lock enforcement are not established.
All-action omission remains allowed by this presence predicate.

Root Lock, Action, Fields and item references reuse the existing
32-hop/cycle guard; a shared 200,000-item budget and field-reader truncation
produce PDFUA2-FORM-LOCK-INCOMPLETE. Non-dictionary known Lock is unsupported
and incomplete. Unknown Fields never becomes claimed absence; unknown
Action does not suppress known Fields type errors. Known item type errors
survive later unknown items. Null/dangling Fields is absence; null/dangling
items are not strings. Full Action/P schema, signature validity, permissions,
seed values and complete field traversal remain separate.

Initial 206 authored controls had twenty-seven failures. Final 216 also
cover exact 32/33-hop root/action/array/item bounds, shared work and parent
exclusion. Focused Release: 4,456 passed; full Release: 17,313 passed,
zero failed/skipped. Solution zero errors/two retained machine-feed
NU1507 warnings. Twenty-one isolated native controls retain seven
shape/presence positives and reject fourteen primary negatives.
All pinned combined-reference jobs complete normally and accept:
**fourteen explicit primary/reference disagreements**. Native UA-1 and
fresh 435-PDF/870-result corpus JSON are wholly unchanged against
choice-index baseline dcae53b6. No standard text or external controls
are bundled; full signature/PDF parity and hosted CI are not claimed.

## 2026-11-05 primary choice-field index array shape

Official PDF Association PDF 2.0 errata 12.7.5.4/Table 234 declares a
choice field's I entry an ascending array of integer zero-based indices,
without restricting that shape requirement to MultiSelect fields.
Recognized terminal Ch dictionaries now receive PDFUA2-FORM-CHOICE-I-TYPE
for a known non-array value or non-integer array item, and
PDFUA2-FORM-CHOICE-I-ORDER for negative integers or descending adjacent
known integers. Each diagnostic aggregates per terminal field.
Omission/null/indirect-null/dangling top-level values remain absent;
null or dangling array items are not integers. Empty arrays and repeated
equal indices are retained by this nondecreasing shape check.

The check is local, not inherited from nonterminal parent I entries.
Existing field-type projection identifies terminal Ch fields; other types
remain excluded. Root/item references reuse the 32-hop/cycle guard.
A shared 200,000-item budget and field-reader truncation produce the
separate PDFUA2-FORM-CHOICE-I-INCOMPLETE. Known type/order errors survive
later unknown items; unknown/non-integer items break adjacent-order
comparison, rather than inventing the missing index. Presence conditions,
Opt upper bounds, duplicate-selection semantics, V consistency, full
field schema/traversal and layout remain separate.

Initial 102 authored controls had thirteen failures; final 107 also cover
exact 32/33-hop root/item boundaries and shared work across fields.
Focused Release: 4,240 passed; full Release: 17,097 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings.
Eleven isolated native controls retain five shape positives and reject six
primary shape/order negatives; all pinned combined-reference jobs complete
normally and accept: **six explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against Tf-count baseline b0636d02. No standard text or external controls
are bundled; full choice-field/PDF parity and hosted CI are not claimed.

## 2026-11-05 primary default appearance Tf operand count

Official PDF Association PDF 2.0 errata 12.7.4.3 explicitly requires Tf
with its two operands, font and size. The existing bounded DA scan now
adds PDFUA2-FORM-DA-TF-OPERANDS when a reached Tf has zero, one or more
than two top-level operands. Its existing count capped at three is reused;
comments are ignored and structured operands count as single objects.
Findings aggregate per inspected string identity and survive later unknown
resources or unsupported tails. Operators beyond the token budget do not
produce speculative arity findings.

The separate resource-matching incomplete finding remains: a known wrong
operand count does not establish a resource match. Exactly two operands
with invalid types are still only incomplete for resource matching; this
slice does not claim complete operand-type, numeric or operator grammar.
Existing permissive-lexer limitations and shared byte/token/nesting bounds
remain. Bare Tf still satisfies the presence predicate but now receives
the independent count error.

Eighty-one authored controls had twenty-three red assertions on unchanged
production. The first build attempt encountered unreadable generated Core
reference metadata (CS0009); a non-shared-compiler control ran the unchanged
production successfully and established those failures before implementation.
Focused Release: 4,133 passed; full Release: 16,990 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings.
Twenty-one isolated native controls retain nine positives; twelve existing
font-incomplete negatives gain the count error, not new verdict corrections.
All pinned combined-reference jobs complete normally and accept:
**twelve retained primary/reference disagreements**. Native UA-1 and fresh
435-PDF/870-result corpus JSON are wholly unchanged against resource-match
baseline c897537a. No standard text or external controls are bundled;
full parity and hosted CI are not claimed.

## 2026-11-05 primary default appearance font-resource matching

Official PDF Association PDF 2.0 errata 12.7.4.3 requires a Tf font name
to match an entry in AcroForm DR/Font. Known document defaults and inherited
recognized terminal Tx defaults now receive PDFUA2-FORM-DA-FONT-RESOURCE
when a decoded Tf name has no resolved non-null default font entry.
Field-local/page resources are not substituted for AcroForm resources.
All recognizable Tf operators are checked, including zero-size autosizing.
One resource finding is aggregated per inspected string identity.

The existing bounded content scan retains only the first two operands and
a count capped at three, clearing them at every operator. Resource matching
requires exactly a name and finite numeric size; malformed/ambiguous operand
shapes instead receive PDFUA2-FORM-DA-FONT-INCOMPLETE. This is a matching
precondition, not complete operand/grammar validation. Default resource maps
are resolved lazily once; DR, Font and selected entries reuse the existing
32-reference-hop/cycle bound. Absent/null/dangling entries are missing;
unresolved/cyclic or unsupported map/entry shapes are explicitly incomplete,
not silently valid or claimed absent. A known missing name survives later
unknown entries, malformed Tf operands or unsupported content.
Full font schema, strict numeric syntax, allowed operators, required DA
and layout remain separate.

Initial 63 authored controls had thirty-six failures; final seventy-five
include exact 32/33-hop resource bounds, decoded names, operator boundaries,
default-resource scope and lazy inspection. Focused Release: 4,052 passed;
full Release: 16,909 passed, zero failed/skipped. Solution zero errors/two
retained machine-feed NU1507 warnings. Twenty-four isolated native
local/inherited/document controls retain nine positives and reject fifteen
missing-resource negatives; all pinned combined-reference jobs complete
normally and accept: **fifteen explicit primary/reference disagreements**.

The native fixture originally also carried an unrelated document /Helv
default. Local/inherited controls explicitly null that default; document
controls null the nearer field default. Exact committed baseline 7176ca44
was archived and built session-locally to verify the corrected controls,
rather than counting contaminated positives. Native UA-1 is unchanged.
Fresh 435-PDF/870-result corpus measurement adds the resource finding to
three already-negative UA-2 fixtures, with no removed findings or verdict
changes; all UA-1 JSON is unchanged. The affected 8.10.1-t02-fail-a,
8.10.2.3-t01-fail-a and 8.6-t01-fail-a document defaults use /Helv, while
their resolved DR/Font maps contain only /F1-/F4. Their field /F4 overrides
remain resolvable, but do not excuse the independently checked document
default. No standard text or external controls are bundled; full parity
and hosted CI are not claimed.

## 2026-11-05 primary default appearance text-font presence

Official PDF Association PDF 2.0 errata 12.7.4.3 explicitly requires
at least a Tf operator and its font/size operands in a DA byte string.
The bounded DA pass now implements the operator-presence part:
PDFUA2-FORM-DA-TF-MISSING reports a known AcroForm or inherited recognized
terminal Tx byte string with no top-level Tf. Names, comments,
literal/hex strings and structured operands containing Tf do not count.
Zero-size Tf is not rejected by this presence check.

A missing-operator finding requires a completed scan of that string
under the existing shared byte/token and lexer-depth limits. Unsupported
inline images, closing delimiters, parser exceptions and exhausted work
retain PDFUA2-FORM-DA-INCOMPLETE instead of an unsupported absence claim.
Known absence in another already-inspected default survives independent
field type/reference/scan errors. Omitted/null defaults still do not
trigger required-presence checks. Existing matrix-count findings remain
independent. A bare Tf passes this presence-only predicate: arity,
font/size types, allowed operators, resource matching, required DA,
complete grammar and layout remain separate requirements.

Initial 75 authored controls had twenty-seven failures; the final
eighty include exact complete/incomplete token boundaries and independent
errors. Focused Release: 3,977 passed; full Release: 16,834 passed,
zero failed/skipped. Solution zero errors/two retained machine-feed
NU1507 warnings. Eighteen native local/inherited/document controls retain
six font/autosize positives and reject twelve absent-Tf negatives.
All pinned combined-reference jobs complete normally and accept:
**twelve explicit primary/reference disagreements**. Native UA-1 and
fresh 435-PDF/870-result corpus JSON are wholly unchanged against
committed matrix-count baseline 331924f3. No standard text or external
controls are bundled; full field or PDF/UA parity is not claimed.

## 2026-11-05 primary default appearance text-matrix cardinality

Official PDF Association PDF 2.0 errata 12.7.4.3 explicitly limits a DA
byte string to at most one Tm operator. Known AcroForm defaults and
inherited recognized terminal Tx defaults now receive
PDFUA2-FORM-DA-TM-COUNT for two or more top-level Tm keywords.
The existing content lexer distinguishes operators from comments,
names, literal/hex strings and structured operands; no appearance is
executed or rendered. Existing DA type and inherited-reference checks
remain independent.

Inspection shares a 1 MiB input-byte budget and 200,000 top-level
object-token budget across distinct string identities in a validation
pass. Aliases are inspected once; the existing lexer bounds nested
arrays/dictionaries at 200. Budget exhaustion, parser depth failures,
unexpected top-level closing delimiters and inline-image operators
produce PDFUA2-FORM-DA-INCOMPLETE. Inline-image payloads are not scanned
heuristically for Tm. A matrix-count finding already established before
later incompleteness is retained. Oversized strings are not parsed.
These are implementation safety bounds, not normative PDF size limits.
The lexer is permissive about malformed numbers and unterminated operands:
this does not establish complete DA syntax, operand arity, allowed
operators, required Tf/presence, font matching or layout. Field-specific
choice/button defaults remain outside this terminal Tx pass.

The initial 53 authored cases had eight failures. The final 89 cases
also cover exact byte/token/nesting boundaries, shared work, aliases,
indirect/cyclic values, independent type findings and excluded fields.
Focused Release: 3,897 passed; full Release: 16,754 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings.
Eighteen native local/inherited/document controls retain fifteen
positives and reject three two-Tm negatives. All pinned combined-reference
jobs complete normally and accept: **three explicit primary/reference
disagreements**. Native UA-1 and fresh 435-PDF/870-result corpus JSON are
wholly unchanged against committed DA-type baseline 47aad3fd.
No standard text or external controls are bundled; full parity is not claimed.

## 2026-11-05 primary default appearance byte-string types

Official PDF Association PDF 2.0 errata declares DA a byte string in
AcroForm Table 224 and variable-text field Table 228 (12.7.3/12.7.4.3).
Known non-null document defaults and inherited terminal Tx defaults now
receive PDFUA2-FORM-DA-TYPE when they are not strings, including streams.
Document defaults are checked even when a field overrides them or no
text fields exist. Per-field inherited checks are limited to recognized
terminal Tx fields; choice/button field-specific DA rules remain separate.

Bounded inherited lookup is reused. Unknown AcroForm/default references,
field parents or reader truncation produce PDFUA2-FORM-DA-INCOMPLETE.
Known default and V findings survive incompleteness of the other input.
Null/omission/indirect null/dangling field defaults retain inherited
fallback; known non-null invalid values are not silently replaced.
Required DA presence, operator grammar, font-resource matching and
layout are not evaluated; byte strings are not decoded or executed.
Existing value/RV/MaxLen findings and UA-1 remain unchanged.

The initial 102 authored cases had nineteen failures. Twelve additional
document-without-fields/reference-bound/independent-incomplete cases pass.
Focused Release: 3,808 passed; full Release: 16,665 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings. Twenty-four
native local/inherited/document controls retain six positives and reject
eighteen primary negatives. All combined-reference jobs complete normally
and accept: **eighteen explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against committed MaxLen baseline. No standard text or external controls
are bundled; full field schema/traversal and full parity are not claimed.

## 2026-11-05 primary inherited text-field MaxLen shape

Official PDF Association PDF 2.0 errata 12.7.5.3/Table 232 explicitly adds
the non-negative integer requirement to optional, inheritable MaxLen
(ISO-approved issue 191). The existing terminal Tx value pass now checks
that resolved inherited metadata with PDFUA2-FORM-MAXLEN-TYPE.
Omission/null/indirect-null/dangling values retain inherited fallback;
zero and positive integers are valid shapes. Known invalid nearer values
are not silently replaced by valid ancestor values. Choice/button fields
remain outside this text-field requirement.

The inherited V resolver was extracted for reuse without changing its
32-reference-hop/64-parent-dictionary bounds, cycle handling or non-null
override policy. MaxLen has separate PDFUA2-FORM-MAXLEN-INCOMPLETE for
unknown traversal or field-reader truncation. Known limit and text-type
findings are independent: unknown MaxLen does not suppress known V errors,
and unknown V does not suppress known MaxLen errors. Existing RV comparison,
value-presence diagnostics, typed field reader and UA-1 remain unchanged.

The initial 108 authored part/type/shape/inheritance/reference/depth controls
had nineteen failures. Nine additional stream/unknown-limit independence
cases pass. Focused Release: 3,694 passed; full Release: 16,551 passed,
zero failed/skipped. Solution zero errors/two retained machine-feed NU1507
warnings. Twenty native local/inherited controls retain six positives and
reject fourteen primary negatives. All combined-reference jobs complete
normally and accept: **fourteen explicit primary/reference disagreements**.
Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against committed V-type baseline.

This validates metadata type/range only: text length is not counted or
enforced, including MaxLen zero, because complete character/layout semantics
are outside this slice. Full field-reader/schema/ownership traversal,
RV inheritance/schema and appearance/layout/CSS remain separate.
No standard text or external controls are bundled; hosted CI/full parity
are not claimed. Earlier dated exclusions retain their delivery-time scope.

## 2026-11-05 primary text-field V declared types

Official PDF Association PDF 2.0 errata 12.7.5.3 requires a non-null text
field V to be a text string or stream. The text-field value pass now
inspects inherited V before RV, so this declared-type requirement also
applies without a rich value. Known non-string/non-stream values emit
PDFUA2-FORM-TEXT-V-TYPE; existing RV presence/equality/incomplete findings
remain separate and unchanged. Choice/button fields are not classified
as text fields.

The existing bounded V/Parent traversal is reused: 32 reference hops,
64 parent dictionaries beyond the terminal field, identity-cycle detection,
and field-reader truncation. Null/omission/indirect null/dangling local
values retain inherited fallback; a known non-null override stops lookup.
Unknown V/parent traversal explicitly retains FORM-VALUE-INCOMPLETE even
without RV. Known V type errors survive later unresolved RV.
String/stream decoding, XML equality and UA-1 behavior are unchanged.

The initial 320 authored part/type/RV/V/indirect/inherited/reference cases
had thirty-five failures. Six additional parent-depth/cycle/malformed/known
override controls pass. Focused Release: 3,577 passed; full Release:
16,434 passed, zero failed/skipped. Solution zero errors/two retained
machine-feed NU1507 warnings.

Twenty native controls contain six null/string/stream type controls
(five retained clean positives; rich/null already fails RV presence)
and fourteen type negatives. All type negatives now receive the new
type finding. Seven no-RV negatives newly reject and remain reference-
accepted: **seven explicit primary/reference disagreements**. Seven rich
negatives already reject under FORM-VALUE-INCOMPLETE; those and rich/null
retain their existing findings. All combined-reference jobs complete
normally; rich negatives reject under 8.10.3.3-1, not independent declared-
type agreement. Native UA-1 and fresh 435-PDF/870-result corpus JSON are
wholly unchanged against committed class-array baseline.

This covers terminal fields recognized by the existing field reader,
not complete field schema/ownership/traversal. Full RV inheritance/schema,
text encodings, layout/CSS and MaxLen remain separate. No standard text
or external controls are bundled; hosted CI/full parity are not claimed.

## 2026-11-05 primary attribute-class array item types

Official PDF Association 14.7.6.2 requires C class names or arrays of class
names possibly accompanied by revision numbers. The existing raw C-array
scan now rejects non-name, non-number items with one
PDFUA2-STRUCT-C-ITEM-TYPE finding per element. Strings, booleans,
dictionaries and nested arrays are not class names/revision numbers,
including resolved indirect items. This check runs regardless of ClassMap
presence or local A values; it does not depend on class-value lookup.

The existing 32-hop/200,000-item null scan is reused. Prior known item errors
survive later incomplete references/work; existing null and incomplete
diagnostics remain separate. Undefined classes remain empty. Existing
attribute lookup/owner checks and UA-1 behavior are unchanged.

The available primary passage does not establish integer-only revision
types, ranges or placement. An initial integer-only revision rejection
was discarded before commit. Integer and real numeric candidates remain
outside this type rejection; their revision validity and existing
unsupported-interpretation diagnostics are not inferred or altered.
Complete ClassMap schema and element K types remain separate.

The scoped 172 authored part/local-A/ClassMap/direct/indirect/null/reference/
work cases had forty baseline failures and now pass. Focused Release:
3,251 passed; final full Release: 16,108 passed, zero failed/skipped.
Solution zero errors/two retained machine-feed NU1507 warnings.
Eighteen native controls retain eight name/numeric positives and reject
ten primary negatives. All combined-reference jobs complete normally and
accept: **ten explicit primary/reference disagreements**. Native UA-1
and fresh 435-PDF/870-result corpus JSON are wholly unchanged against the
committed parent-key type baseline. No standard text/external controls
are bundled; full PDF/UA/spec parity is not claimed.

## 2026-11-05 primary structure-parent integer key types

The official PDF Association 14.7.5.4 paragraph requires integer values
for StructParent/StructParents; the public Table 359 row also declares
StructParents integer. The existing bounded key resolver now checks both
types on pages, page annotations, AP direct/state streams and known
Form/Image XObjects in page/nested appearance resource graphs. It emits
one PDFUA2-{PAGE,ANNOT,AP,XOBJECT}-STRUCT-PARENT-TYPE finding per checked
dictionary when either resolved non-null key is non-integer.

Omission, explicit/indirect null and dangling references remain absent.
Signed integers, including zero and negative values, pass this type-only
check; no key-range or parent-tree identity requirement is inferred.
Known type errors survive incomplete resolution of the other key; both
non-null keys still independently trigger the existing conflict finding.
Reference/work bounds, deduplication and UA-1 behavior remain unchanged.

The initial 399 authored surface/part/key/type/indirect/cycle cases had
105 failures after isolating the page fixture's default StructParents key.
Thirty-five additional exact-reference-bound and two-invalid-key aggregation
cases pass. Focused Release: 939 passed; full integrated Release: 15,936
passed, zero failed/skipped, including the earlier four appearance-alias
cases. Solution zero errors/two retained machine-feed NU1507 warnings.

Thirty-nine native controls retain twelve clean integer positives and
reject all twenty-six type negatives. Three PrinterMark/StructParent
controls already carried PDFUA-ANNOT-PRINTERMARK, including the integer
control; they are explicitly not clean positives/new verdict corrections.
Twenty-four previously compliant negatives now reject. All reference jobs
complete normally; twenty-three type negatives remain reference-accepted,
including one already rejected by FreeDF before this slice: twenty-two
new verdict disagreements. The reference rejects the two page negatives
under 8.2.2-1 untagged content and the PrinterMark string under 8.9.2.4.14-1,
not explicit integer-type agreement.

Native UA-1 and fresh 435-PDF/870-result corpus JSON are wholly unchanged
against the committed nested-appearance baseline. No standard text/external
controls are bundled. Full object traversal, key ranges, required-key
conditions, parent-tree identity and full PDF/UA/spec parity remain open.
Earlier dated exclusions below describe their delivery-time scope.

## 2026-11-05 primary nested appearance resource parent-key exclusivity

The Table 359 at-most-one StructParent/StructParents fact now also covers
Form/Image XObjects in annotation appearance Resources, including unused
entries, named-state appearances and nested Form resources. Appearance and
page resource checks reuse an iterative resource walker; the page-resource
scope and diagnostics remain unchanged. Nested appearance conflicts use
PDFUA2-AP-STRUCT-PARENT-CONFLICT without decoding stream data.

Appearance inspection shares its 200,000-entry budget across annotation
entries, non-null N/R/D entries, named states and nested resource map
entries, including duplicates. Stream/resource identities are deduplicated
per page across all appearances; ordinary shared/cyclic resource graphs
are visited once. References retain the 32-hop bound. Unsupported resource
containers, cyclic references and exhausted bounds explicitly produce
PDFUA2-AP-STRUCT-PARENT-INCOMPLETE; prior known conflicts survive.

Eight native direct/state appearance controls retain four positives and
reject four primary negatives. All eight pinned combined-reference jobs
complete normally and accept: **four explicit primary/reference
disagreements**, not normative confirmation by the reference.
Twelve authored nested/shared-budget/reference-bound/graph/malformed cases
had ten initial failures and now pass. Focused Release: 501 passed; full
Release: 15,498 passed, zero failed/skipped. Fresh 435-PDF/870-result corpus
JSON and native UA-1 reports are wholly unchanged. Solution zero errors/two
retained machine-feed NU1507 warnings.

An additional alias control exposed a deduplication edge: a stream skipped
as an unknown/non-Form/Image resource must not be marked inspected and
suppress a later direct appearance check. Inspection identities are now
recorded only after the resource subtype gate. Four absent/unknown/Form/Image
alias cases had two failures before this correction; all pass. Follow-up
focused Release: 505 passed; native and fresh corpus outcomes remain
unchanged. The 15,498 full-suite count above predates these four cases;
the full suite was not repeated for this focused correction.

Tiling patterns, soft masks/other referenced streams, full appearance
schema and parent-key integer types/parent-tree identity remain separate.
No standard text/external controls are bundled; hosted CI and full parity
are not claimed. Earlier dated scope exclusions below describe those
slices at delivery, not this extension.

## 2026-11-05 primary appearance structure-parent exclusivity

The Table 359 at-most-one StructParent/StructParents fact now covers
annotation AP streams in N/R/D entries and their named-state dictionaries,
including unselected states and tagging-exempt annotations. The shared
page/annotation key resolver reports PDFUA2-AP-STRUCT-PARENT-CONFLICT
without decoding stream data. Stream identities are deduplicated per page,
including streams shared across annotations and appearance states.

The extracted bounded page-annotation visitor preserves the earlier
annotation-key check. Appearance inspection shares a 200,000-entry budget
across annotation array entries, non-null N/R/D entries and named states.
References retain the 32-hop bound. Unsupported containers, cycles and
exhausted bounds produce PDFUA2-AP-STRUCT-PARENT-INCOMPLETE explicitly;
known earlier conflicts survive later incomplete inspection. Null/omission
and indirect null/dangling key behavior and UA-1 remain unchanged.

Twelve native normal/rollover/down direct/state controls retain six positives
and reject six primary negatives. All combined-reference jobs complete
normally and accept: **six explicit primary/reference disagreements**.
Thirty-nine authored UA-1/route/state/shared-stream/malformed/reference/work
cases had twenty-one initial failures and now pass. Focused Release: 489
passed; full Release: 15,486 passed, zero failed/skipped. Fresh
435-PDF/870-result corpus JSON and native UA-1 reports are wholly unchanged.
Solution zero errors/two retained machine-feed NU1507 warnings.

This is appearance stream dictionaries, not their nested resource graphs.
Tiling patterns, soft masks/other stream references, full appearance schema
and parent-key type/identity remain separate. No standard text/external
controls are bundled; hosted CI and full parity are not claimed.

## 2026-11-05 primary resource XObject structure-parent exclusivity

The Table 359 at-most-one StructParent/StructParents fact now covers
Form/Image stream dictionaries reachable through page resource XObject
maps, including unused entries and nested Form resources. Page resource
inheritance is inspected with a 32-dictionary ancestor bound; references
use the existing 32-hop bound. Iterative resource-graph traversal deduplicates
dictionary/stream identities per page, so shared/cyclic resource graphs are
visited once without decoding stream data.

PDFUA2-XOBJECT-STRUCT-PARENT-CONFLICT uses the shared page/annotation
key resolver. Null/omission and indirect null/dangling keys remain absent.
Unsupported resource/map/stream containers, unresolved references and
more than 200,000 total map entries per page produce
PDFUA2-XOBJECT-STRUCT-PARENT-INCOMPLETE. The work budget includes
duplicates and is shared across nested resources; known earlier conflicts
survive later incomplete inspection. Existing rendering/tagging and UA-1
behavior are unchanged.

Twelve native unused Form/Image controls retain six positives and reject
six primary negatives. All combined-reference jobs complete normally and
accept: **six explicit primary/reference disagreements**. The initial 38
authored cases had twenty failures; eight additional malformed-resource,
ancestor-bound and shared-work cases cover exact boundaries. All 46 pass;
focused Release: 450 passed; full Release: 15,447 passed, zero failed/skipped.
Fresh 435-PDF/870-result corpus JSON and native UA-1 reports are wholly
unchanged. Solution zero errors/two retained machine-feed NU1507 warnings.

This is page resource XObject graph coverage, not all stream/object
coverage. Annotation appearances, tiling patterns, soft-mask/other stream
references outside those maps, complete parent-key types and parent-tree
identity remain separate. Unknown non-Form/Image XObject subtypes are not
interpreted. No standard text/external controls are bundled; hosted CI
and full parity are not claimed.

## 2026-11-05 primary annotation structure-parent exclusivity

The Table 359 at-most-one StructParent/StructParents fact now also covers
known dictionaries in each page's Annots array, including PrinterMark and
other annotations exempt from separate tagging checks. A shared bounded
key resolver retains the previous page check unchanged.
PDFUA2-ANNOT-STRUCT-PARENT-CONFLICT reports conflicting non-null keys,
deduplicated by dictionary identity per page. Null/omission and indirect
null/dangling values remain absent; zero remains present.

Raw Annots containers/entries and key values use the 32-hop reference
bound. Unsupported containers, cycles and more than 200,000 array entries
per page report PDFUA2-ANNOT-STRUCT-PARENT-INCOMPLETE explicitly; known
earlier conflicts survive later incomplete inspection. The work limit
counts duplicate entries too. This is not the existing tagging loop's
silent 10,000-entry projection, nor a claim of complete annotation schema.

Six native tagged-widget controls retain three positives and reject three
primary negatives. All six combined-reference jobs complete normally and
accept: **three explicit primary/reference disagreements**. Thirty-seven
authored UA-1/exempt-annotation/null/zero/reference/container/work cases
had nineteen initial failures and now pass. Focused Release: 420 passed;
full Release: 15,401 passed, zero failed/skipped. Fresh 435-PDF/870-result
corpus JSON and native UA-1 results are wholly unchanged. Solution zero
errors/two retained machine-feed NU1507 warnings.

This closes page-annotation dictionaries only. XObjects, appearances,
complete parent-key type/identity and other container surfaces remain
separate. No standard text/external controls are bundled; hosted CI and
full parity are not claimed.

## 2026-11-05 primary page structure-parent exclusivity

Published PDF 2.0 errata, 14.7.5.4 Table 359, allows at most one of
StructParent and StructParents in an object. UA-2 now checks parsed page
dictionaries and reports PDFUA2-PAGE-STRUCT-PARENT-CONFLICT when both
resolved values are non-null. Zero is a present value; omission, explicit
null and indirect null/dangling references retain absent-key behavior.
Cycles or more than 32 reference hops produce the separate
PDFUA2-PAGE-STRUCT-PARENT-INCOMPLETE rather than guessed exclusivity.
UA-1 and existing content/parent-tree interpretation are unchanged.

Six native page controls retain three positives and reject three primary
negatives. All six independent combined-reference jobs complete normally
with matching verdicts. The reference rejects the negatives under
ISO 14289-2 8.2.2-1 (untagged content), not an explicit parent-key
exclusivity predicate; exact rule-level agreement is not claimed.
Twenty-eight authored null/zero/indirect/UA-1/depth/cycle cases had ten
initial failures and now pass. Focused Release: 709 passed; full Release:
15,364 passed, zero failed/skipped. Fresh 435-PDF/870-result corpus JSON
and native UA-1 reports are wholly unchanged. Solution zero errors/two
retained machine-feed NU1507 warnings.

This is page-dictionary exclusivity, not all-object coverage. Annotation
and XObject dictionaries, complete key type/parent-tree identity and
content-container semantics remain separate. No standard text or external
controls are bundled; hosted CI and full parity are not claimed.

## 2026-11-05 primary default RoleMap declared shape

Published PDF 2.0 errata, 14.7.3, requires a RoleMap dictionary with single
name targets. The parsed RoleMap projects only name values; existing
used-role checks cannot expose malformed unused values. UA-2 now inspects
the raw root entry and reports PDFUA2-ROLEMAP-SHAPE for a non-null
non-dictionary container or non-null non-name mapping value. Optional
omission/null, null/dangling values treated as absent, and indirect names
are preserved. Role resolution, namespace RoleMapNS handling and UA-1
behavior are unchanged.

The independent scan reports PDFUA2-ROLEMAP-SHAPE-INCOMPLETE for cycles,
more than 32 reference hops or work beyond 200,000 mappings. Known shape
errors survive a later incomplete value; findings aggregate once per root.
This bounds this validation scan, not the existing parser's eager RoleMap
projection or all role/namespace traversal.

Twenty native unused-value/container controls retain eight positives and
reject twelve primary negatives. All combined-reference jobs complete
normally and accept: **twelve explicit primary/reference disagreements**.
All native UA-1 results and the fresh 435-PDF/870-result corpus JSON are
wholly unchanged. Forty-eight authored type/null/UA-1/depth/work/used-role
cases had 17 initial failures and now pass. Focused Release: 422 passed;
full Release: 15,336 passed, zero failed/skipped. Solution zero errors/two
retained machine-feed NU1507 warnings.

This is declared shape, not global target reachability, complete role
mapping semantics, RoleMapNS schema/registration, or complete structure
conformance. No standard text/external controls are bundled; hosted CI
and full parity are not claimed.

## 2026-11-05 primary NSO attribute namespace presence

Published PDF 2.0 errata, 14.7.6.1 Table 360, requires an NS entry when
the O owner is NSO. UA-2 now reports PDFUA2-ATTR-NSO-NS for absent or
resolved-null NS values, including indirect null and dangling references.
Direct/indirect O names are resolved before applying the condition.
Other owners and malformed non-name owners do not trigger an inferred
NSO requirement. The existing mandatory O-name check remains independent.

The attached-attribute traversal covers local A, flat arrays, used ClassMap
attributes and stream dictionaries without decoding data. Used classes
remain inspected even when A wins; undefined/unused classes are unchanged.
Cycles and reference/work exhaustion retain PDFUA2-ATTR-OWNER-INCOMPLETE
without guessing absence. One known NSO-namespace error per element survives
later incomplete inspection. Standard-owner value lookup is unchanged.

Twenty-six native controls retain eight positives and reject eighteen
primary negatives. All independent combined-reference jobs complete
normally and accept: **eighteen explicit primary/reference disagreements**.
The native UA-1 reports and fresh 435-PDF/870-result corpus JSON are wholly
unchanged. Sixty authored UA-1/route/stream/conditional/depth/work controls
had 28 initial failures and now pass; focused Release: 1,617 cases.
Full Release: 15,288 passed, no failures/skips; solution zero errors/two
retained machine-feed NU1507 warnings.

This closes absence/null only. Namespace declared type, dictionary schema,
owner identity/URI semantics and registration are not inferred from the
element NS rules or these controls. Full namespace/attribute conformance,
stream interpretation, revisions and inheritance remain deferred.
No external controls/standard text are bundled; hosted CI/full parity
are not claimed.

## 2026-11-05 primary mandatory attribute owner names

Published PDF 2.0 errata, 14.7.6.1 Table 360, requires attribute objects
(dictionaries or streams) to have an O name. UA-2 now reports
PDFUA2-ATTR-OWNER for a known missing/null/non-name owner, aggregated once
per parsed element. Direct A, flat A arrays, and attributes attached by
used C/ClassMap entries share bounded traversal with standard-owner value
lookup. Stream dictionaries are inspected without decoding their data.
Undefined classes remain empty; unused class definitions are not inspected.
Referenced class owners are checked even when local values override C;
standard-owner value lookup still avoids unused ClassMap lookup when A wins.

Cycles, more than 32 reference hops, unsupported containers and shared
work beyond 200,000 entries produce PDFUA2-ATTR-OWNER-INCOMPLETE rather
than guessed missing owners. Known owner violations survive later incomplete
inspection. Null/omission fallthrough and integer array revision markers
retain existing behavior. Shared ListNumbering, FENote and table value lookup
retains last-specified non-null precedence and unsupported-stream disposition.

Sixteen native controls retain six positives and reject ten primary negatives
across dictionary/array/class/stream and missing/null/non-name/indirect routes.
All sixteen independent combined-reference jobs complete normally and accept:
**ten explicit primary/reference disagreements**, not reference confirmation.
All native UA-1 results and the fresh 435-PDF/870-result corpus JSON are
unchanged. The initial 72 authored cases had 28 failures; 15 further
depth/work/known-error/class-use cases cover the boundaries. All 87 new
cases pass in the full Release suite of 15,228, with no failures or skips.
The final focused selector passes 1,549 cases; eight stream-owner cases
were covered in the earlier focused run and full suite. Solution build:
zero errors/two retained machine-feed NU1507 warnings.

This is owner-name shape, not complete attribute conformance. NSO namespace
requirements, owner-name semantic validity/Annex E, UserProperties and full
ClassMap schema, attribute stream interpretation, revisions and inheritance
remain deferred. No external PDF/XML or standard text is bundled; hosted
CI and full parity are not claimed.

## 2026-11-05 primary repeated attribute precedence

Published PDF 2.0 errata, 14.7.6.1/14.7.6.2, gives later repeated attributes
precedence in arrays and preserves local A precedence over C. The shared
UA-2 OwnedAttribute lookup now retains the last specified non-null owned
value across local A arrays, ClassMap attribute arrays and C class lists.
This supersedes the historical first-non-null policy described below.
All ListNumbering, FENote NoteType and table attribute consumers share the
correction. Unrelated owners, omitted/null values and revision markers
remain non-overrides. Local values still avoid unused ClassMap lookup.
Later inspected entries cannot be skipped after an earlier known value:
cycles/depth/shared 200,000-item bounds produce the existing consumer-specific
incomplete errors rather than a guessed selection.

Twelve native list controls correct eight reversed primary outcomes: four
false rejections are removed and four missed rejections are corrected.
Four unrelated/null/omitted/revision tails remain valid. All independent
combined-reference jobs completed normally and matched the previous
first-wins output: **eight explicit primary/reference disagreements**, in
both verdict directions, not reference-confirmed corrections. The published
primary precedence fact is the basis for the behavior change.

The initial 28 authored list/note/table cases had 24 failures. Nine additional
late-cycle/shared-work boundary cases preserve incomplete handling. Six
pre-existing FENote expectations encoded first-wins and now assert last-wins;
their inputs are unchanged. All 37 new cases and 1,470 focused cases pass.
Fresh 435-PDF/870-result comparison is wholly unchanged, including all
verdicts and UA-1 outputs. This is standard-owner dictionary lookup, not full
attribute revision selection, inheritance, NSO namespace semantics, stream
interpretation or global owner/schema validation.
Full Release: 15,141 cases pass without skips; solution zero errors/two
retained machine-feed NU1507 warnings. Hosted checks/full parity not claimed.

## 2026-11-05 primary attribute-class entry type

Published PDF 2.0 errata, 14.7.2 Table 355 and 14.7.6.2, defines element C
as a name or array. UA-2 now reports PDFUA2-STRUCT-C-TYPE for a resolved
non-null value of another type. This uses existing bounded resolution and
retains PDFUA2-STRUCT-C-INCOMPLETE for unresolved/depth-bound values. Optional
null/omission, name references, empty arrays, revision-bearing arrays and
existing null-array diagnostics are unchanged. The published note that an
undefined class is empty is respected; no ClassMap existence restriction
was added.

Fifteen native controls retain eight positives and reject seven primary
negatives (integer, real, string, boolean, dictionary, indirect integer and
stream). All fifteen independent combined-reference jobs normally accept:
**seven explicit primary/reference disagreements**. Twenty-six authored
UA-1/type/null/depth cases had seven initial failures; all pass after the
scoped check, along with 447 focused cases. Fresh 435-PDF/870-result comparison
is wholly unchanged, including all file verdicts and UA-1 outputs. Complete
array-item syntax, class revision ordering and ClassMap-defined attribute
objects remain deferred; this is a declared-entry-type slice, not full
attribute-class conformance.
Full Release: 15,104 cases pass without skips; solution zero errors/two
retained machine-feed NU1507 warnings. No hosted CI/full-conformance claim.

## 2026-11-05 primary nonempty element child arrays

Published PDF 2.0 errata, 14.7.2 Table 355, specifies one or more children
when an element K value is an array. UA-2 now reports PDFUA2-STRUCT-K-EMPTY
for a resolved empty element array, including an indirect array. The shared
bounded shape helper retains its existing null-item checks. Root K empty
arrays and optional null/omission are not changed; unresolved references
still produce PDFUA2-STRUCT-K-INCOMPLETE without guessing an empty array.

Five native controls retain three positives (omission, null and one child)
and reject two primary negatives (direct/indirect empty arrays). All five
independent combined-reference jobs normally accept: **two explicit
primary/reference disagreements**. Fourteen authored root/UA-1/null/size/depth
cases had three initial failures; all pass after the scoped check. No existing
conforming fixtures or assertions needed changing. Release: 508 focused and
15,078 full cases pass without skips; solution zero errors/two retained
machine-feed NU1507 warnings.

Fresh 435-PDF/870-result comparison adds only the empty-K diagnostic to two
already-invalid UA-2 fixtures. Reachable Document objects 14 (8.6-t01-fail-a)
and 10 (8.8-t01-fail-a) have literal empty K arrays. Exact reference jobs reject
for text/annotation/destination rules, not empty child arrays. These additional
diagnostic differences are explicit. All 870 verdicts and all UA-1 outputs
are unchanged. Full permitted element-child types, dictionary identity and
ClassMap syntax remain outside this parsed-element size slice.

An accompanying declared namespace-type investigation found no missed
FreeDF verdict: eight malformed native NS values (integer, real, name,
string, boolean, array, indirect name and stream) already reject through
PDFUA2-NESTING-INCOMPLETE. Three positives (omission, null and registered
PDF 2.0 namespace) remain valid. All eleven reference jobs normally accept,
so eight existing primary/reference differences are retained. No duplicate
production check was added. Complete namespace dictionary schema remains
unestablished.

## 2026-11-05 primary structure-root child types

Published PDF 2.0 errata, 14.7.2 Table 354, defines root K as a dictionary
representing a structure element or an array of such dictionaries. UA-2 now
rejects non-dictionary scalar values and non-null non-dictionary array entries
with PDFUA2-STRUCT-ROOT-K-TYPE. Nested arrays are not dictionaries. This reuses
the existing bounded null-item scanner; root null diagnostics are retained,
and PDFUA2-STRUCT-K-INCOMPLETE still reports cycles, reference depth beyond 32
and array work beyond 200,000. Known type/null errors remain when a later item
cannot be inspected. Optional null/omission, root empty arrays and complete
structure-element dictionary identity/schema are not newly enforced.

Nine corrected native controls retain two positives and reject seven primary
negatives. The integer case was already invalid for root-content containment.
Six other files were previously accepted and now reject; all six are still
accepted by normal independent combined-reference jobs. The two positives
and existing integer negative agree. These are **six explicit
primary/reference disagreements**, not six reference-confirmed corrections.
Initial controls accidentally referenced source object 7 (an integer), not
Document 11; they and their reports are preserved and excluded from the
conforming control count. Corrected controls derive and verify the source
Document reference. Token spacing in PyMuPDF output is checked separately
from object type.

The initial 29 authored cases had ten failures; two further work-bound cases
retain an already-known invalid type. All 31 new cases and 479 focused cases
pass. Fresh 435-PDF/870-result comparison is wholly unchanged, including all
file verdicts and UA-1 outputs. Full element K type/nonempty-array rules,
ClassMap array syntax and root dictionary identity remain separate; no
reference predicate mapping or complete structure-conformance claim is made.
Full Release: 15,064 cases pass without skips. Initial solution compilation
reported CS0009 on the generated Samples Debug reference assembly; metadata
inspection confirmed that artifact lacked managed metadata. A targeted
non-incremental Samples rebuild and unchanged solution rerun succeeded with
zero errors/two retained machine-feed NU1507 warnings. No source outside this
slice was changed; the artifact failure's cause is not established.

## 2026-11-05 primary namespace registration identity

Published PDF 2.0 errata, 14.7.2 Tables 354/355, requires every namespace
dictionary referenced by an element to occur in the root Namespaces array.
UA-2 now compares dictionary identity, not namespace URI equality, for parsed
elements. Missing membership reports PDFUA2-NS-UNREGISTERED. Root-array
shape problems, reference cycles/depth beyond 32 and inspection beyond
200,000 array items report PDFUA2-NS-REGISTRY-INCOMPLETE; incomplete registry
inspection does not infer absent membership. A known missing membership
reported before a later unresolved element reference is retained.

Six native controls have two primary positives and four primary negatives.
All six independent combined-profile jobs completed normally and accepted
every file: **four explicit primary/reference disagreements**, not agreement.
Initial missing/null controls both serialized as null; the corrected missing
control removes the key and retains the initial evidence separately.
Eleven initial authored cases had six failures; five additional cases cover
root shape/depth and optional registration when namespaces are unused.
Three focused failures after implementation exposed previously incomplete
conforming fixtures; their root arrays now register their actual namespaces
without relaxing assertions.
Five additional full-suite failures exposed the same omission in the shared
link-target fixture; its existing namespace is likewise registered for UA-2.

Fresh 435-PDF/870-result comparison adds only the registration diagnostic to
two already-invalid UA-1 fixtures validated as UA-2. Object inspection confirms
namespace references and no root registry in both. Exact reference jobs reject
both for other rules, not namespace registration; the additional-diagnostic
difference is explicit. Every file verdict and all UA-1 outputs are unchanged.
Full namespace dictionary shape, RoleMapNS target registration and complete
structure traversal are not established by this parsed-element slice; existing
nesting-incomplete diagnostics remain. No reference family mapping, external
PDFs or standard text is bundled.
Corrected Release validation: 445 focused and 15,033 full cases pass without
skips; solution builds with zero errors and two retained machine-feed NU1507
warnings. No hosted CI or full PDF/UA conformance is claimed.

## 2026-11-05 primary indirect element namespaces

The official PDF 2.0 errata, 14.7.2 Table 355, requires an element's
NS namespace dictionary to be an indirect reference. UA-2 now reports
PDFUA2-NS-INDIRECT for a direct dictionary. Existing role resolution still
runs so known role findings are not erased; default-namespace omission
and null behavior, UA-1 behavior and reference-resolution boundaries
are unchanged.

Six native controls retain four positives (omission, null, registered
standard and registered mapped namespace). These four reference jobs
completed normally and accepted the files. Two direct-dictionary primary
negatives now reject in FreeDF, which previously accepted both; the reference
throws `COSObject argument can not be direct` for both. The initial reference
batch assertion detected two veraExceptions, which are retained as **unavailable
negative verdicts**, not counted as compliance agreement or normal failures.
Ten authored cases initially had two failures.

Fresh 435-PDF/870-result corpus JSON output, including UA-1, is unchanged.
This slice does not establish root Namespaces registration, namespace
dictionary type/shape, or indirect references within RoleMapNS; those
requirements remain separate. No standard text, external PDFs or reference
predicate mapping is bundled.

Release validation: 394 focused namespace/nesting/FENote/list cases passed;
full suite passed 15,017 cases with no failures/skips. Solution build had
no errors and two retained machine-feed NU1507 warnings.

## 2026-11-05 primary structure revision shape

The official PDF 2.0 errata, 14.7.2 Table 355, specifies optional deprecated
R as a non-negative integer. UA-2 now reports PDFUA2-STRUCT-R-TYPE
for negative integers and non-integer types, including integral real
objects. Bounded reference resolution reports separate
PDFUA2-STRUCT-R-INCOMPLETE for cycles or depth beyond 32. Optional
omission/null (including dangling references under generic null semantics)
and non-negative integers are retained.

Eight corrected native controls completed normally: four primary positives
remain accepted and four malformed values now reject. Previous FreeDF and
the combined reference accept all eight: **four explicit primary/reference
disagreements**, not reference-verified corrections. The initial native 1.0
real was canonicalized to integer by the generator; the corrected 1.5 control
retains a real token. Authored cases separately pin 1.0 real rejection.
Twenty-three authored cases initially had seven failures. Fresh 435-PDF/
870-result corpus JSON and all UA-1 outcomes are unchanged.

This does not implement revision selection, inherited attributes, revision
markers or deprecation enforcement; only the supplied R value's type/sign.
No reference family mapping, external PDFs or standard text is bundled.

Release validation: 141 focused structure cases passed; full suite passed
15,007 cases with no failures/skips. Solution build had no errors and
two retained machine-feed NU1507 warnings.

## 2026-11-05 primary attribute and class array nulls

The official PDF 2.0 errata, 14.7.2 Table 355, prohibits null A attribute/
revision entries and C class-name/revision entries. UA-2 now checks these
arrays on every parsed structure element with the same bounded scanner
as K and Ref. PDFUA2-STRUCT-A-NULL/PDFUA2-STRUCT-C-NULL are separate
from their respective INCOMPLETE diagnostics. Known nulls survive a later
cyclic item; owner-aware value lookup, local/ClassMap precedence and
revision-marker handling are not changed.

Ten native controls completed normally against the combined reference:
four valid/revision positives remain accepted, while six explicit null,
indirect null and dangling entries now reject against the primary constraint.
The previous implementation and the reference accept all ten: **six explicit
primary/reference disagreements**, not reference-verified parity. Thirty-six
authored cases initially had fourteen failures. Fresh 435-PDF/870-result
corpus JSON output, including all UA-1 results, is unchanged.

This slice only checks immediate A/C array items on parsed elements.
Optional scalar null/omission, nested arrays, ClassMap-defined attribute
arrays, complete attribute syntax, revision and inheritance semantics are
not established. Existing K and Ref findings are preserved (K diagnostic
wording now says null-item rather than null-child). No reference family
mapping, external PDFs or standard text is bundled.

Release validation: 206 focused structure/owned-lookup cases passed.
The initial full run passed 14,983 and failed the unchanged signature
fixture during certificate creation: its later notAfter exceeded the
issuer by one second, matching the independently reproduced pre-slice
timestamp control. The isolated case and unchanged full rerun passed
(14,984 cases, no skips). Signature code was not changed. Solution build
had no errors and two retained machine-feed NU1507 warnings.

## 2026-11-05 primary structure child array nulls

The official [PDF 2.0 errata, 14.7.2 Tables 354/355](https://pdf-issues.pdfa.org/32000-2-2020/clause14.html#H14.7.2)
prohibits null children in structure-root and structure-element K arrays.
UA-2 now reports PDFUA2-STRUCT-K-NULL for resolved null array entries,
including dangling references. Root and every parsed element are checked.
Reference cycles, depth beyond 32 and arrays exceeding 200,000 inspected
items report separate PDFUA2-STRUCT-K-INCOMPLETE. A known null discovered
before a later incomplete item is retained. The bounded array scan is shared
with the existing Ref check without changing Ref behavior.

Fifteen native controls cover root, Document, Table, TR and a content-bearing
leaf: five unchanged positives remain accepted; ten explicit/dangling null
negatives now fail the primary constraint. All fifteen independent combined
reference jobs completed normally and accepted every control: **ten explicit
primary/reference file disagreements**, not reference-verified corrections.
The unchanged FreeDF implementation also accepted all fifteen.

The initial 36 authored cases had 16 failures; six additional cases pin
depth and known-null/incomplete coexistence. Optional scalar null/omission
policy is unchanged. Empty arrays, nested arrays and complete child type,
identity and traversal validation are outside this null-item slice; a clean
null check does not establish their validity. Fresh 435-PDF/870-result corpus
JSON output is unchanged, including UA-1. No reference family mapping, external
PDF or standard text is bundled.

Release validation: 151 focused child/Ref/nesting cases passed; full suite
passed 14,948 cases with no failures/skips. Solution build had no errors
and two retained machine-feed NU1507 warnings.

## 2026-11-05 namespace-resolved heading cardinality

Legacy raw H-child counts now remain UA-1-only. UA-2 retains resolved
ISO 32005 Document-H/Sect-H cardinality and its existing H prohibition.
Raw foreign H mapped to Sect no longer produces a duplicate-heading
error; genuine direct and mapped H duplicates still fail the resolved
cardinality check.

Six corrected native controls completed normally: two positives and
four negatives agree with independent combined-profile file outcomes
and cardinality failures, correcting two false rejections. Negatives
also fail the separate H prohibition, not cardinality alone. Initial
exploratory controls used Art in the PDF-2.0 namespace and retained an
H1: they were all independently invalid and are not positive evidence.
The corrected set uses Document/Sect and changes that original H1 to P.
Six authored cases initially had four failures; two preserve UA-1 counts.

Fresh 435-PDF/870-result comparison removes one redundant UA-2 legacy
diagnostic on an already-invalid UA-1 heading fixture. Its exact independent
job confirms Sect-H cardinality and the H prohibition. Every file verdict
and all UA-1 outputs remain unchanged. Heading semantics and grouping
cardinality are not broadened; no reference source or external PDF is bundled.

Release validation: 148 focused heading/cardinality cases passed; full
suite passed 14,906 cases with no failures/skips. Solution build had
no errors and two retained machine-feed NU1507 warnings.

## 2026-11-05 occupied-column table span intersections

UA-2 table geometry now checks every column covered by a cell's owned
ColSpan, not only whether its starting column is free. Crossing a column
still occupied by an earlier RowSpan reports PDFUA2-TABLE-SPAN-OVERLAP
(8.2.5.26). An overlapping new span cannot shorten the earlier carry:
the longer remaining span is retained so an intersection cannot hide
terminal RowSpan overflow. UA-1 geometry and diagnostics are unchanged.

Six native controls completed normally against the pinned combined profile:
four positives and two negatives agree with file outcomes and test 1,
correcting two missed equal-width intersections, including a ClassMap span.
The initial trailing control assigned Column Scope to a TD, exposing a
separate existing stricter Scope policy; the corrected geometry control
omits that irrelevant attribute and passes both engines. Seven authored
cases initially had two failures; another protects remaining-span overflow.
Fresh 435-PDF/870-result comparison has unchanged JSON results:
no diagnostic, verdict or UA-1 changes.

The test-1 cross-check mapping is scoped overlap only, not complete
intersection coverage. Existing span clamping and immediate-row flattening
remain; full group geometry, inheritance/revision, span syntax and
algorithmic header fallback remain incomplete. No reference engine source,
profile XML or external control PDF is bundled.

Release validation: 354 table/cardinality cases plus the dedicated
overflow-retention case passed; full suite passed 14,900 cases with
no failures/skips. Solution build had no errors and two retained
machine-feed NU1507 warnings; Python cross-check tests passed all 20.

## 2026-11-05 namespace-resolved section cardinality

Legacy Table section-count checks now remain UA-1-only; UA-2 already
uses namespace-resolved ISO 32005 cardinality. Raw custom THead/TFoot
names mapped to TR no longer count as duplicate sections. Genuine
direct and namespace-mapped duplicate sections still fail
PDFUA2-NESTING-CARDINALITY under Table-THead/Table-TFoot.

Six native controls completed normally against the combined profile:
all file/cardinality outcomes agree (two positives, four negatives),
correcting two false rejections. Four authored cases initially had
four failures. Fresh 435-PDF/870-result comparison removes two redundant
legacy diagnostics from UA-1 negatives run as UA-2; independent reference
jobs confirm both Table 5 limits. All file verdicts and UA-1 outputs
are unchanged. Full grouping-dependent cardinality remains deferred.
No reference-engine code, external PDFs or reports are bundled.

Release validation: 347 focused cases passed; full rerun passed 14,892
cases and solution build had no errors. The initial full run failed
one unchanged lexer allocation assertion; its isolated rerun passed,
an exact committed-base full control passed 14,888 cases, and the
unchanged current full rerun passed. The initial failure's root cause
is not established; no lexer code or assertion was changed.

## 2026-11-05 conditional UA-2 table header presence

The unconditional Table-must-contain-TH error now remains UA-1-only.
Pinned UA-2 8.2.5.26 tests 5/6 condition header-association requirements
on the presence of header cells. Existing header association, row-width,
terminal-span and namespace containment checks remain active; the absence
of TH does not by itself prove semantic accessibility or correct tagging.

Six native direct/mapped headerless and header-bearing controls completed
normally against the combined profile: all file outcomes agree (three
positives, three width-irregular negatives), correcting two false rejections.
Four authored cases initially had two failures and retain UA-1 behavior.
All six native UA-1 outputs are unchanged.

Fresh 435-PDF/870-result comparison removes one redundant header-presence
diagnostic from an already invalid UA-1 fixture run as UA-2; all verdicts
and UA-1 outputs are unchanged. The prior independent section-corpus
reference report for this exact fixture rejects THead-TH containment,
not missing headers. No semantic table classification or full header
fallback conformance is claimed. No external PDFs or XML are bundled.

## 2026-11-05 terminal table RowSpan overflow

UA-2 table validation now reports PDFUA2-TABLE-ROWSPAN-END when a cell's
resolved owned RowSpan remains active after the final flattened table
row. Previously the carry state was discarded. Span lookup/clamping and
UA-1 behavior are retained. A legitimate two-row span ending at the
final row remains valid; row-group boundaries and intersections are
not newly evaluated by this terminal check.

Six native controls completed normally against the combined profile;
all rule/file outcomes agree (three positives, three negatives, test
8.2.5.26-2). Three missed violations are corrected, including a class
span and overflow on every data cell. Seven authored cases initially
had two failing assertions and cover valid terminal spans and UA-1.

Fresh 435-PDF/870-result comparison adds two diagnostics to already
invalid width-irregular fixtures, with every verdict and UA-1 output
unchanged. Independent reference jobs reject both under 8.2.5.26-3,
not test 2: this is an explicit diagnostic difference. Both fixtures
actually contain RowSpan 5 in tables of three rows; the terminal
overflow is independently inspectable but exact predicate agreement
is not claimed for those fixtures. Rule-overlap mapping for test 2
only reflects this measured slice, not full column-height/group regularity.
Full geometry, grouping and header fallback remain incomplete.

## 2026-11-05 owned table span and class attributes

UA-2 table spans now use /O /Table local/ClassMap attributes, not generic
local attribute lookup. Scope and Headers also reuse the same bounded
owner-aware helper. Local non-null values win, null/omission falls through
to classes in order, and unrelated owners do not affect table geometry.
Malformed/cyclic attribute references or shared 200,000-item exhaustion
produce PDFUA2-TABLE-ATTRIBUTE-INCOMPLETE errors. Existing span clamping,
placement and header policies are retained; incomplete inspection is
not evidence of regularity even when default spans permit continuation.
UA-1 attribute selection is unchanged.

Eight native ColSpan controls completed normally against the combined
profile, all file/width outcomes agreeing (four positives, four negatives).
Two false rejections from wrong/omitted owners and two missed irregularities
from class values/null fallback are corrected. All eight UA-1 outputs and
fresh 435-PDF/870-result output are unchanged. Twenty-two authored cases
cover owner/precedence, cycles, shared work bounds and local short-circuit.
Full attribute inheritance/revision, geometry and algorithmic header
fallback remain incomplete. No external PDFs or reference XML are bundled.

## 2026-11-05 PDF 2.0 structure Ref array shape

The official [PDF Association ISO 32000-2:2020 errata, 14.7.2 Table 355](https://pdf-issues.pdfa.org/32000-2-2020/clause14.html#H14.7.2)
identifies Ref as an array and prohibits null array elements. UA-2 now
checks these shape constraints on every structure element independently
of TOCI Ref presence. Optional null entries retain omission semantics;
resolved missing array references count as null. Cycles, reference depth
and the 200,000-item array work bound produce separate
PDFUA2-STRUCT-REF-INCOMPLETE errors rather than guessed null values.
Non-array and null-element errors have ISO 32000-2 Table 355 provenance,
not a claimed veraPDF UA-2 predicate mapping.

Eight retained native TOCI controls were rerun against the exact previous
FreeDF runner. Three formerly accepted malformed cases now fail the
primary shape constraint: integer Ref, dangling array entry and explicit
null array entry. The pinned reference accepts all three; these are
explicit primary/profile differences, not claimed reference agreements.
The other five file outcomes agree; TOCI presence outcomes and all eight
UA-1 outputs are unchanged. Fresh 435-PDF/870-result output is unchanged.
Forty authored shape/profile/cycle/work cases cover the boundary.

This does not validate non-null array item types, structure-target
identity or semantic destinations. Empty arrays and non-null integers/
strings inside arrays pass this narrow shape check, not full target
validation. Complete TOCI reference semantics remain deferred.
No external PDF, report, standard text or engine code is bundled.

## 2026-11-05 UA-2 table section containment

Legacy THead/TBody/TFoot parent/child checks and the UA-1-specific
THead/TFoot-requires-TBody check now run only for UA-1. UA-2 already
enforces namespace-resolved ISO 32005 Table 5 containment and table
regularity. This removes raw-role false rejections without accepting
known illegal parent/child pairs or expanding grouping predicates.

Eighteen native controls completed normally against the combined profile:
all file/containment outcomes agree (twelve clean positives, six negatives),
correcting eleven false rejections across direct/mapped tables and rows
and all three section types. THead-only and TFoot-only tables are accepted
by the reference; no UA-1 TBody requirement is inferred for UA-2.
Twelve authored section cases initially had eleven failures.

Fresh 435-PDF/870-result comparison removes ten redundant UA-2 diagnostics
on UA-1 negative fixtures run as UA-2. Independent reference jobs confirm
the eight illegal section parent/child exclusions; the two THead/TFoot-only
fixtures have no reference table/containment failures but remain invalid
UA-2 files for version/metadata reasons. All file verdicts and UA-1 outputs
are unchanged. Full grouping, table geometry and algorithmic header
determination remain incomplete. No external PDFs or XML are bundled.

## 2026-11-05 namespace-aware table row and header endpoints

UA-2 table selection, row/cell selection, header-ID targets, cell-parent
checks and header-attribute evaluation now reuse the namespace resolver.
UA-1 selection and existing span, attribute and header policies are
unchanged. Unknown parent types retain role/nesting inspection errors
rather than being treated as known incompatible PDF types.

Twelve native regularity controls completed normally against the pinned
combined profile: all file/width outcomes agree (six clean positives,
six negatives, reference 8.2.5.26-4). Four false rejections of mapped
tables/rows/cells and one missed irregular fully mapped table are corrected.
All twelve UA-1 outputs and fresh 435-PDF/870-result output are unchanged.
Twelve authored cases additionally cover mapped header-ID targets.

Two exploratory twelve-control sets are retained separately, not used
as negative regularity evidence. Undefined Headers on all TDs, and empty
Headers on one TD, are accepted by the reference when valid Scope permits
algorithmic determination. FreeDF's existing header-reference/consistency
policy still rejects their six broken cases in each set. Pinned tests 5/6
explicitly allow algorithmic determination; complete header fallback,
legacy section/grouping endpoints and row-span/intersection geometry
remain incomplete. No external PDFs, reports or engine code are bundled.

## 2026-11-05 UA-2 TOC containment without legacy roles

Legacy TOC-child and TOCI-parent checks now run only for UA-1.
UA-2 containment already uses namespace-resolved ISO 32005 Table 5
rules through ISO 14289-2 6.2/8.2.4 incorporation; the raw-role
checks falsely rejected mapped TOC parents and mapped TOCI children.
The independent TOCI Ref-presence check remains unchanged.

Six native controls completed normally in both standalone and combined
profiles. Combined file/containment outcomes all agree (four clean
positives, two negatives), correcting two false rejections. Standalone
also accepts the two negatives because their exclusions belong to
ISO 32005, not the standalone UA-2 profile. Known illegal pairs remain
rejected by FreeDF's nesting check, not silently accepted.

Fresh 435-PDF/870-result comparison changes only two redundant UA-2
diagnostics on UA-1 negative fixtures run as UA-2. Independent combined
reference jobs confirm Document-TOCI and TOC-P exclusions; both files
remain rejected. All file verdicts and UA-1 outputs are unchanged.
Six authored cases initially had four failing legacy-diagnostic assertions.
Full TOCI target identity/shape and grouping-dependent semantics remain
deferred; no external PDFs, reports or engine code are bundled.

## 2026-11-05 owned list numbering attributes

UA-2 ListNumbering lookup now selects /O /List dictionaries from local A
and ClassMap/C, reusing the bounded FENote attribute lookup. Local non-null
owned values win; null/omission falls through to classes in order.
Unrelated owners cannot satisfy numbering. Cyclic references, malformed
containers and shared 200,000-item work exhaustion report a separate
PDFUA2-LIST-ATTRIBUTE-INCOMPLETE Error, not a guessed missing value.
Local values avoid inspecting unused cyclic class maps.

Nine native controls completed normally against pinned veraPDF 1.30.3
UA-2 8.2.5.25-1: all rule/file outcomes agree (four clean positives,
five negatives). Two missed violations from wrong/omitted owners and
two false rejections from class values/null fallback are corrected.
All nine UA-1 outputs and the fresh 435-PDF/870-result corpus are unchanged.
Authored owner/precedence/cycle/work-bound cases also cover retained
FENote behavior through the shared helper. Inheritance, revision semantics,
grouping-dependent list rules and full attribute conformance remain deferred.
No engine code, external PDFs or XML reports are bundled.

## 2026-11-05 rich XML string decoding

RV strings now reuse the scoped PDF text decoder before XML parsing,
instead of assuming their raw bytes are XML UTF-8. PDFDocEncoding,
UTF-16 BOM and UTF-8 BOM selection follow the PDF text-string boundary;
byte streams retain the existing XML-reader byte-encoding path. DTD,
external resolution, XML depth/work and malformed Unicode limits remain.
The shared general PdfString decoder is unchanged.

Eight native string/stream/encoding controls completed normally.
All four string controls agree on rule/file outcomes (three positives,
one negative), correcting one false rejection of PDFDocEncoded text
and one missed mismatch from non-BOM UTF-8 bytes interpreted as a PDF
string. All four stream outputs are unchanged: one UTF-16 stream remains
a whole-file disagreement, and a non-UTF-8 PDFDocEncoded stream remains
incomplete rather than the reference's equality violation. This is not
complete text-stream encoding/syntax conformance, and the prior
XFA-attribute/inheritance disagreements remain explicit.

All eight UA-1 outputs and fresh 435-PDF/870-result corpus output are
unchanged. Four authored string-encoding cases were added. Full Release:
14,760 passed, zero failed/skipped; solution: zero errors and two retained
machine-feed NU1507 warnings. The decoder was moved from local to shared
within the existing UA-2 class for reuse; no general decoding behavior
was broadened. External controls/reports remain session-only.

## 2026-11-05 resolved MathML parent rule

The MathML parent check now selects resolved namespace endpoints, not
raw /Math names. Direct or mapped MathML elements require a resolved
Formula or MathML parent. The existing role resolver exposes its successful
endpoint namespace without changing PDF-type selection or treating a
successful MathML endpoint as an unresolved PDF role. Unresolved parents
retain existing role/nesting-incomplete errors rather than guessed types.

Nine native controls agree with pinned veraPDF v1.30.3 8.2.5.29-1 on
parent-rule outcomes; eight agree on whole-file outcomes (four clean
positives and four negatives), correcting two missed file violations.
A foreign raw Math mapped to P loses an inaccurate MathML finding but
retains its P-within-P containment error; the standalone reference accepts
that file. This is an explicit containment disagreement, not a corrected
false-positive file verdict. All nine UA-1 outputs and fresh
435-PDF/870-result corpus output are unchanged; all native jobs were normal.

Ten authored cases cover lowercase/uppercase tags, non-root MathML
elements, mapped endpoints, foreign lookalikes and valid nested MathML
under Formula. The existing PDF-2.0 /Math fixture was corrected to expect
its unresolved-role error, not an invented MathML parent finding.
Full Release: 14,756 passed, zero failed/skipped. Solution: zero errors,
two retained machine-feed NU1507 warnings. MathML vocabulary/schema,
rendering and broader parent/grouping semantics are not claimed complete;
external controls/reports remain session-only.

## 2026-11-05 TOCI reference presence

Resolved UA-2 TOCI elements now require a non-null Ref on themselves or
a descendant, matching the presence portion of pinned veraPDF v1.30.3
8.2.5.8-1. Iterative descendant inspection bounds scheduled/visited work,
detects cycles and reuses bounded reference resolution. Cyclic/exhausted
inspection reports separate `PDFUA2-TOCI-REF-INCOMPLETE`, not a guessed
missing Ref. Namespace mappings and UA-1 selection are retained.

This is explicitly presence only, not target shape, identity or semantic
destination validation. Eight native controls agree on rule/file outcomes:
two missing/null negatives and six positives, correcting two missed file
violations. Four of the reference-accepted positives have empty/malformed
or dangling values; they are not evidence of valid target conformance.
The valid direct/descendant controls are measured separately. All eight
UA-1 outputs are unchanged; all reference jobs completed normally.

Fresh 435-PDF/870-result output adds this predicate to six already-invalid
UA-1 fixtures evaluated as UA-2, with no file-verdict changes. The six
new findings independently agree with the pinned reference; all UA-1
output is unchanged. Fifty-six profile/mapping/self/descendant/value cases,
one cycle case and exact 32/33-reference boundaries were added.
Full Release: 14,746 passed, zero failed/skipped. Solution: zero errors,
two retained machine-feed NU1507 warnings. Reporting: 20 passing tests,
with only this scoped presence finding mapped to 8.2.5.8-1.
No external PDF/XML/engine source is bundled; full parity is not claimed.

## 2026-11-05 namespace-aware UA-2 caption ordering

The existing global UA-2 caption-position check now resolves child roles
and applies to every parent, including Table/L/TOC. The legacy caption
checks are retained only for UA-1; they must not impose UA-1 list/TOC
first-only or table single-caption policy on UA-2. Incorporated nesting
cardinality remains independent. Foreign raw Caption mapped to P no
longer gains a caption finding; mapped Caption in the middle is rejected.
Unknown mappings retain existing role/incomplete findings, not guessed
caption identities. No grouping/visual caption inference is added.

Pinned veraPDF v1.30.3 8.2.5.27-1 specifies first-or-last placement for
all structure-element parents. Nine direct/mapped/foreign-to-P Figure
controls and three list controls agree on exact and whole-file outcomes:
nine clean positives and three negatives. One missed mapped-caption
verdict and two false rejections (foreign-to-P and trailing list Caption)
are corrected. Three additional PDF-1.7 namespace TOC controls agree on
caption-rule outcomes but are not clean controls: the reference rejects
missing TOCI targets under 8.2.5.8-1, which remains separate parity work.
The first TOC namespace attempt used PDF-2.0 TOC, rejected by the reference;
its XML is retained but excluded from the final control set.

Fresh 435-PDF/870-result comparison has six UA-2 diagnostic changes on
UA-1 fixtures evaluated as UA-2, with all file verdicts unchanged.
All six changes independently agree with reference caption outcomes:
five obsolete profile-specific findings removed and one middle-caption
finding moved to the correct UA-2 rule. All UA-1 outputs are unchanged.
All 21 final native/corpus reference jobs completed normally.
Seventy-two authored profile/parent/mapping/position cases had 15 failures
before correction. Full Release: 14,687 passed, zero failed/skipped;
solution: zero errors, two retained NU1507 machine-feed warnings.
External PDFs/XML remain session-only; full PDF/UA parity is not claimed.

## 2026-11-05 rich-field plain text encoding

The UA-2 rich-field equality check now decodes plain V using PDFDocEncoding
instead of the shared decoder's Latin-1 fallback. All 40 non-Latin-1
character mappings follow ISO 32000-1 7.9.2.2 and Annex D.3/Table D.2:
eight accent characters, 31 punctuation/ligature/letter characters and euro.
The shared PdfString decoder and other consumers are unchanged.
Existing Unicode BOM selection is retained, but malformed UTF-16/UTF-8
and undefined PDFDocEncoding bytes produce separate incomplete inspection
rather than replacement-character equality or a guessed mismatch.
This does not establish complete PDF-2.0 string syntax conformance.

Twelve native symbol controls agree with pinned veraPDF v1.30.3
8.10.3.3-1 on exact rule and whole-file outcomes: six clean positives
and six negatives. The exact pre-change runner rejects all six positives;
the scoped decoder corrects those six false-positive file verdicts.
All 12 UA-1 outputs and fresh 435-PDF/870-result corpus output are unchanged.
Every native reference job completed normally. The first report-writing
attempt failed on Windows default text encoding; the retained complete
reports were regenerated explicitly as UTF-8, not counted as partial results.

One hundred sixty authored mapping/profile/mismatch cases and seven
undefined-byte/malformed-Unicode cases cover the new boundary. Full Release
suite: 14,615 passed, zero failed/skipped. Solution build: zero errors,
two retained machine-feed NU1507 warnings. The preceding XML/stream/
inheritance disagreements and rendered/CSS/schema/type limits remain
unchanged. External control PDFs and reports remain session-only;
full PDF/UA parity is not claimed.

## 2026-11-05 UA-2 rich-field XML text equality

The terminal-text-field RV/V presence check now also compares decoded
plain V against RV's XHTML body text nodes in document order. XML
entities and nested formatting are decoded, whitespace inside the body
is preserved, and paragraph/br boundaries do not synthesize characters.
This is XML text-content equality, not rendered/layout equivalence,
CSS interpretation or complete rich-text schema/type conformance.
Text-string/stream packaging is accepted; local/null/inherited V selection
and UA-1 behavior are retained.

Primary ISO 32000-1 12.7.3.4 (printed pp436/438) establishes fully formed
XML rich text, paragraph interpretation and allowed text-stream packaging.
The text-content comparison was independently measured against pinned
veraPDF v1.30.3 UA-2 8.10.3.3-1, without copying engine source. Thirty-six
native string/stream controls agree on exact and whole-file outcomes:
18 reference-clean positives and 18 negatives, correcting 18 missed
file violations. All 36 UA-1 outputs and fresh 435-PDF/870-result corpus
output are unchanged.

Eight additional identical-text controls isolate the earlier stream
disagreement: the pinned reference rejects RV streams with XFA body
attributes for both ASCII and UTF-16 plain V, but accepts the matching
RV strings and attribute-free streams. FreeDF retains allowed stream
packaging; these two differences are explicit, not normalized away.
Eight retained inheritance controls still have four differences when a
parent supplies V. All 52 reference jobs completed normally. The six
differences in this comparison set do not replace the earlier dated
18 null/presence/stream/inheritance differences or imply full coverage.

XML parsing prohibits DTDs and external resolution, bounds bytes/characters,
depth and element work, and preserves text order without a lossy XFA-tree
conversion. Strict stream decoding reports malformed/unsupported/cyclic
filters and unresolved/oversized/XML inspection as separate
`PDFUA2-FORM-VALUE-INCOMPLETE` Error, not a guessed equality finding.
Ninety-five added cases include both profiles, entities/Unicode/whitespace,
stream attributes, DTDs, malformed filters, exact 64-depth/200,000-element
and 32-MiB plain-byte boundaries. The initial four-case selector had two
mismatch failures. Prior presence/bound fixtures now use equal well-formed
XML so their original selection expectations remain tested.
Full Release suite: 14,448 passed, zero failed/skipped. Solution build:
zero errors, two retained machine-feed NU1507 warnings. Reporting tests:
20 passed. Full PDF/UA parity is not claimed; external PDFs/XML/reports
remain session-only.

## 2026-11-05 class-mapped FENote NoteType names

The explicit FENote NoteType-name check now also resolves ClassMap/C.
Local non-null FENote-owned attributes take precedence; null/omission
falls through to class attributes. C names/arrays are inspected in order,
with the first non-null owned value winning. Missing classes and unrelated
owners remain omission. Local values do not inspect unused class maps.
The namespace-resolved FENote selector and UA-1 behavior are unchanged.

A, C and class attribute arrays share one 200,000-item work budget;
cyclic/exhausted references and malformed inspected containers produce
`PDFUA2-FENOTE-INCOMPLETE`, not a guessed known-name verdict. Integer
revision markers are skipped without implementing full revision selection.
Inheritance, non-name syntax and reciprocal references remain deferred.
The preceding local-only delivery is retained below as historical scope.

Twenty native direct/mapped controls agree on exact rule and whole-file
outcomes: ten clean positives and ten negatives, correcting eight missed
file violations. All 20 UA-1 outputs and fresh 435-PDF/870-result corpus
output are unchanged. Every reference job completed normally using pinned
veraPDF v1.30.3 8.2.5.14-4. The initial 23-case selector had seven failures;
25 added cases cover class precedence, reference cycles and exact shared
work boundaries. Full Release suite passed 14,352 tests, zero failed/skipped;
an additional unused-cyclic-map/local-override regression was then added
and checked separately. Solution build: zero errors, two retained NU1507
machine-feed warnings. Reporting tests: 20 passed. External controls and
reference reports remain session-only; complete PDF/UA parity is not claimed.

## 2026-11-05 local FENote NoteType names

Resolved UA-2 FENote elements now report `PDFUA2-FENOTE-TYPE` for a
local, explicit FENote-owned NoteType name other than Footnote, Endnote
or None. The check resolves the element's A dictionary/array and matches
the O owner before reading NoteType. The first non-null owned value wins,
consistent with the independent ordering controls and existing local
attribute lookup behavior. Unrelated owners, omission/null, allowed names
and UA-1 retain their behavior.

This is the explicit local-name portion of pinned veraPDF v1.30.3
8.2.5.14-4, not complete attribute conformance. ClassMap/C lookup,
inheritance, revision-selection semantics and non-name value syntax are
not evaluated. In particular the reference accepts a Footnote string;
the check does not reinterpret it as a PDF name or claim syntax validity.
Reciprocal FENote references remain deferred.

Bounded resolution is reused for A, O and NoteType. Malformed outer
attribute containers, cyclic references and exhausted reference/work
bounds produce separate `PDFUA2-FENOTE-INCOMPLETE` Error, rather than
an inaccurate known-name finding. Integer revision markers in A arrays
are skipped; they do not implement complete revision selection.
Fifty-two authored profile/mapping/value cases, three cycle cases and
four exact 32/33-reference and 200,000/200,001-work boundaries were added.
The initial 55-case selector had nine failures before correction.

Twenty-four native direct/mapped controls agree on all rule/file outcomes
(18 clean positives, six negatives), correcting six false-negative
file verdicts. Array controls include invalid-then-valid, valid-then-invalid
and unrelated-owner-then-valid values. All 24 corresponding UA-1 outputs
and fresh 435-PDF/870-result public-corpus output are unchanged.
Every native reference job completed normally. Full Release suite:
14,327 passed, zero failed/skipped. Solution build: zero errors and two
retained machine-feed NU1507 warnings. Reporting tool: 20 passing tests;
the 8.2.5.14-4 overlap maps only this scoped predicate portion.
External controls/profile reports remain session-only; full PDF/UA
parity is not claimed.

## 2026-11-05 signature verification-fixture control

The intermittent signature path-length test failure observed during the
Artifact and Note slices is now reproduced by a session-only bounded
runner invoking the unchanged original test. It stopped on iteration 128:
certificate creation threw because the requested intermediate notAfter
was one second later than its issuer's NotAfter. Separate UtcNow samples
in the fixture cross the certificate timestamp's second boundary.
The exception occurs in TestPki.Make before production certificate/PDF
validation, not as a failing path-length verdict. Signature test and
production signing sources are unchanged from the pre-Note baseline.
Earlier dated run counts and uncertainty statements are preserved
as historical evidence; the unrelated fixture-time correction is deferred.

## 2026-11-05 UA-2 namespace-aware Note restriction

`PDFUA2-NOTE` now selects resolved Note roles, instead of raw Note plus
mere default RoleMap membership. Default-mapped custom Note and foreign
RoleMapNS types resolving to PDF-1.7 Note no longer escape the restriction.
Foreign raw Note mapped to P/FENote does not impersonate Note in the
shared ID check: that selector also uses resolved roles for UA-2 while
retaining its existing ID policy. UA-1 is unchanged.

The shared resolver no longer accepts Note as a PDF-2.0 standard type
merely because it is in the combined PDF vocabulary. Direct and mapped
PDF-2.0 Note now produce existing role-missing/nesting-incomplete Errors,
matching the native reference's vocabulary rejection. This is a scoped
Note vocabulary correction, not a complete namespace-vocabulary audit.
Reciprocal FENote references, NoteType attribute validation and full note
semantics remain outside this slice.

Twenty-two authored cases cover both profiles, default/PDF-1.7/PDF-2.0
Note, default/namespace mappings, P/FENote mappings and cyclic/unmapped
roles. Four initial restriction/vocabulary cases were red; eight
additional ID-selector assertions exposed the coupled legacy-name issue.
Eleven native controls give 11 whole-file agreements (two clean positives,
nine negatives), correcting one false-negative and two false-positive
file verdicts. Nine of 11 Note-rule outcomes agree; two cyclic mappings
are rejected by both validators but differ in classification. The
reference reports Note restriction findings on those cycles, while
FreeDF retains explicit role/nesting-incomplete Errors and does not assert
a known resolved Note type. These are not exact-rule agreements.

All 11 corresponding UA-1 outputs and fresh 435-PDF/870-result public-corpus
output are unchanged. Native reference jobs used registered indirect
namespaces and completed normally. The FENote positive has NoteType None;
its acceptance is not proof of complete FENote semantics. Existing
8.2.5.14-1 reporting overlap is explicitly tested.

Final full Release suite: 14,268 passed, zero failed/skipped. A preceding
full run hit the recurring signature path-length test failure, also
observed in the earlier Artifact slice; the detailed full rerun passed.
The previous isolated pre-change signature control passed. No signature
sources were changed, but the intermittent cause remains unestablished
and tracked separately rather than dismissed as proven unrelated.
Solution build: zero errors and two retained machine-feed NU1507 warnings.
Reporting tool: 20 passing tests. External native controls/reports remain
session-only; full PDF/UA parity is not claimed.

## 2026-11-05 UA-2 namespace-aware H prohibition

`PDFUA2-HEADING-H` now uses the existing UA-2 namespace-aware standard
type resolver, not the legacy type name. A RoleMapNS custom type resolving
to H is prohibited, while foreign raw H mapped to P/H1 does not impersonate
an H heading. Unmapped/cyclic roles retain the existing role and nesting
incomplete Errors without being asserted to be known H. UA-1, Note,
Caption and heading-hierarchy policies are unchanged.

The diagnostic's contradictory H-within-Sect alternative is removed.
Pinned veraPDF v1.30.3 UA-2 8.2.5.12-1 prohibits H unconditionally;
both default and mapped H inside Sect remain violations. Numbered
headings remain unaffected. This applies the existing prohibition to
resolved roles; it does not establish complete heading semantics.

Twenty-two authored cases cover both profiles, default/PDF-2/default-map/
namespace-map H, foreign names mapped to P/H1, unmapped/cyclic roles,
H1 and the Sect boundary. Four of the original 18 selector cases failed
before correction. Eleven native controls agree on every heading-rule
and whole-file verdict (three clean positives, eight negatives), correcting
two false-negative and two false-positive file verdicts. An additional
irrelevant H finding is removed from the foreign-role negative without
changing its invalid verdict. All 11 corresponding UA-1 outputs are
unchanged. Native namespace objects are indirect and registered; every
reference job completed normally.

Fresh 435-PDF/870-result public-corpus output is identical to the immediate
baseline. Full Release suite: 14,246 passed, zero failed/skipped.
Solution build: zero errors and two retained machine-feed NU1507 warnings.
Reporting tool: 20 passing tests, with the existing 8.2.5.12-1 family
mapping explicitly tested. External native PDFs/reports remain
session-only; full PDF/UA parity is not claimed.

## 2026-11-05 UA-2 tagged annotation artifacts

UA-2 PrinterMark and Invisible/NoView disposition checks now honor
resolved Artifact parents and Artifact ancestry, including RoleMapNS
mapping, rather than rejecting every non-null StructParent key.
The existing strict parent-tree resolver is evaluated once and reused:
the key must be an in-range non-negative integer, the parent-tree value
must be an in-use indirect reference with the current xref generation,
and its target must be a parsed structure element before Artifact
disposition can apply. This does not infer Artifact membership from an
annotation name or an unrelated dictionary.

Accepted tagged PrinterMark Artifacts now also pass through the existing
direct OBJR identity check instead of bypassing it. Missing OBJR still
produces `PDFUA-ANNOT-OBJR`; Artifact status does not hide structural
errors. UA-1 disposition, untagged exemptions, hidden/outside behavior,
ToggleNoView, TrapNet prohibition and UA-2 Popup exclusion are unchanged.
Popup is still forbidden even inside Artifact structure.

The factual disposition conditions were independently cross-checked
against pinned veraPDF v1.30.3 UA-2 8.9.2.2 tests 1/2 and 8.9.2.4.14-1;
existing namespace/Artifact ancestry helpers are reused. Sixty authored
profile/context/structure cases had 12 failures before correction.
Six further cases cover malformed parent-tree identities (15 subcases)
and missing OBJR. Thirty native controls give exact-rule/whole-file
agreement: 22 clean positives, eight negatives and 12 corrected
false-positive file verdicts. All 30 corresponding pre/post UA-1
outputs are unchanged. The missing NoView reporting-family overlap
is added; grouping-dependent structure semantics are not claimed.

An initial reference batch threw runtime exceptions for direct namespace
dictionaries in mapped controls. Those failed jobs are retained as failed
evidence, not compliant controls. The completed native matrix uses
registered indirect namespace objects and descriptive widget Contents;
all 30 reference jobs completed normally with no parse/encryption/OOM/
engine exceptions. Authored FreeDF direct-namespace coverage is separate.

Fresh 435-PDF/870-result public-corpus output is identical to the immediate
baseline. Final full Release suite: 14,224 passed, zero failed/skipped.
An initial full run had one signature path-length test failure; its
focused current-code run, the isolated exact ee16f845 pre-change control,
and the final detailed full run passed. No signature sources were changed;
the cause of that intermittent failure is not established by this slice.
Solution build: zero errors and two retained machine-feed NU1507 warnings.
Reporting tool: 20 passing tests. External controls/reports remain
session-only; full PDF/UA parity is not claimed.

## 2026-11-05 null annotation structure-parent keys

Resolved null StructParent entries now mean omission under primary
ISO 32000-1 7.3.9, rather than a malformed present parent-tree key.
Direct null, indirect null and nonexistent references share the existing
missing-key behavior in both PDF/UA profiles. Visible annotations still
report `PDFUA-ANNOT-UNTAGGED`; hidden/outside exemptions and the existing
Popup/PrinterMark policies are retained. Real non-null malformed keys,
parent-tree mapping checks and direct OBJR identity checks are unchanged.
Raw dictionaries/references are preserved.

The check reuses the bounded reference resolver. Cyclic/depth-exhausted
StructParent resolution now produces `PDFUA-ANNOT-TAG-INCOMPLETE`
(Error), not an apparent null omission or inaccurate known-key finding.
Other independent annotation checks continue. Seventy-two authored cases
cover both profiles and visible/hidden/outside/Popup/PrinterMark/hidden-widget
contexts, null representations, valid keys and cycles. Four further cases
pin the exact 32/33-reference boundary; two existing dangling-key tests
now expect the omission diagnostic. The pre-fix annotation selector had
51 failures out of 136 cases.

Fifty native UA-1/UA-2 controls give 41 whole-file agreements with pinned
veraPDF v1.30.3 (34 clean positives, seven negatives), correcting 21
false-positive file verdicts. Null/dangling controls match their respective
omission controls exactly; omitted and valid-key outputs are unchanged.
All 15 applicable PrinterMark/UA-2 Popup rule results agree (UA-1 7.18.8-1,
UA-2 8.9.2.4.14-1/8.9.2.4.9-1). Reporting adds the missing UA-2
PrinterMark family overlap, not proof of its full artifact predicate.
Tagged Artifact semantics and broader annotation/field classification
are not expanded in this slice.

Nine existing whole-file reference disagreements remain explicit:
five UA-1 Popup controls are accepted by the existing FreeDF exemption
but rejected by the reference's generic Annot rule; four UA-2 visible
untagged widgets (omitted/null/indirect-null/dangling keys) remain rejected
by FreeDF's retained tagging policy while the reference accepts them.
Those file verdicts did not change and are not counted as clean positives.
The null controls' diagnostic changes are distinct from corrected
false-positive file verdicts.

Fresh 435-PDF/870-result public-corpus output is identical to the immediate
baseline. Full Release suite: 14,158 passed, zero failed/skipped.
Solution build: zero errors and two retained machine-feed NU1507 warnings.
Reporting tool: 20 passing tests. External native PDFs/reference reports
remain session-only; full PDF/UA parity is not claimed.

## 2026-11-05 UA-2 namespace-aware list checks

The existing `PDFUA2-LIST-NUMBERING` and `PDFUA2-LI-CONTENT` checks
now select resolved UA-2 L/LI roles. Direct LI/Lbl endpoints used for
label detection, the shared list-child/LI-parent/LI-child checks and the
LBody parent check use the same namespace-aware resolver for UA-2.
Custom RoleMapNS names no longer escape numbering/direct-content checks
or create legacy-name list-shape errors. Foreign types merely named L
or Lbl are not treated as those PDF roles. UA-1 behavior is unchanged.

The factual numbering/direct-content predicates were cross-checked
against pinned veraPDF v1.30.3 UA-2 8.2.5.25 tests 1/2, with existing
family mappings retained. Direct-child label detection, attribute lookup,
caption policy and list-shape policy are not broadened. Grouping,
descendant labels, attribute inheritance and complete list semantics
remain outside this slice. Unresolved/non-PDF endpoint types are not
asserted to violate a known list-shape predicate; unresolved mappings
retain the existing namespace/role and `PDFUA2-NESTING-INCOMPLETE`
Errors rather than gaining misleading known-containment findings.

Sixty-six authored cases cover both profiles, L/LI/Lbl/LBody mappings,
missing/None/Decimal numbering, foreign names mapped to Div/Span,
direct LI content, invalid mapped containment and unresolved/cyclic
roles. The original 50-case selector had 18 failures before correction.
Eighteen independently executed native namespace controls give all
18 numbering-rule agreements and 14 whole-file agreements (six clean
positives, eight negatives). Five mapped-list false-positive file verdicts
are corrected. Four missing numbering findings are added on files already
rejected by legacy shape errors; those are not counted as corrected
false-negative file verdicts. Two additional native direct-content
controls (default and mapped LI) both agree on 8.2.5.25-2 rejection.
All 18 corresponding pre/post UA-1 namespace-control outputs are unchanged.

Four standalone UA-2 reference disagreements are explicitly retained:
Div-mapped lists and Span-mapped labels, each with None/Decimal.
All four were already rejected by FreeDF's ISO/TS 32005 containment
checks. Independent combined UA-2 reference execution also rejects
all four, with Table 5 Document-LI/Document-Caption or LI-Span findings.
They are not counted as clean standalone-reference positives, and the
incorporated normative nesting implementation is not weakened to match
the standalone profile.

Fresh 435-PDF/870-result public-corpus output is identical to the immediate
baseline, including the two circular-role negatives that exposed the
unknown-endpoint boundary during development. Full Release suite:
14,082 passed, zero failed/skipped. Solution build: zero errors and two
retained machine-feed NU1507 warnings. Reporting tool: 20 passing tests.
External native PDFs/profile reports remain session-only; full PDF/UA
parity is not claimed.

## 2026-11-05 UA-1 null reference-XObject entries

The UA-1 reference-XObject prohibition now requires resolved non-null
Ref presence on a Form stream. Missing, direct-null, indirect-null and
nonexistent-reference entries are equivalent under primary ISO 32000-1
7.3.9; the old raw-presence check rejected all explicit null forms.
Genuine non-null reference dictionaries retain `PDFUA-XOBJECT-REF`
Error behavior, cross-checked against pinned veraPDF v1.30.3 UA-1
7.20-1. This changes only the prohibition's null boundary, not external
reference parsing, rendering or reference-dictionary syntax validation.
UA-2 and non-Form streams are unchanged.

Cyclic/depth-exhausted Ref chains produce the separate
`PDFUA-XOBJECT-REF-INCOMPLETE` Error through the existing bounded
reference resolver, not a false known-reference finding or apparent
omission. Thirty authored cases cover both profiles, Form/non-Form
streams, the null representations and exact 32/33-reference boundaries;
six failed before correction.

Six independent native UA-1 controls give four exact-rule/file agreements
(two clean positives, two negatives). Three prior rejections become
acceptances: direct null now agrees with the reference, while indirect
null and nonexistent-reference acceptances deliberately retain two
reference disagreements under primary null equivalence. They are not
counted as reference-agreeing positives. All six corresponding UA-2
outputs are unchanged. Fresh 435-PDF/870-result public-corpus output
is identical to the immediate baseline.

Full Release suite: 14,016 passed, zero failed/skipped. Solution build:
zero errors and two retained machine-feed NU1507 warnings. Reporting tool:
20 passing tests, including the existing 7.20-1 family mapping.
External controls/reference artifacts remain session-only; full PDF/UA
parity is not claimed.

## 2026-11-05 UA-2 rich-field plain-value presence

Text fields enumerated by the existing terminal-field reader now report
`PDFUA2-FORM-RV-V` (Error, 8.10.3.3) when their own non-null RV entry
has no plain V value. This implements only the value-presence part of
the pinned veraPDF v1.30.3 UA-2 8.10.3.3-1 predicate, not rich/plain
textual equivalence. Empty strings and text streams establish presence;
their semantic equivalence is not inferred. XML and stream payloads
remain unread for this check, so no XML normalization or execution occurs.
Choice fields and UA-1 are unchanged.

Primary ISO 32000-1 12.7.3.1/Table 220 (printed 432-433), 12.7.3.4
and 12.7.4.3 (printed 436-438/443) establish inheritable V, rich XML
string/stream representation and plain V representation. Together with
7.3.9 null equivalence, null local V does not shadow an inherited value.
Only own RV on an enumerated terminal field triggers this check.
Full primary UA-2 8.10 remains unavailable in the public ISO preview.

The check reuses the terminal-field reader and bounded reference
resolver. Reader truncation, malformed non-text values, cyclic references
and exhausted reference/parent bounds produce a separate
`PDFUA2-FORM-VALUE-INCOMPLETE` Error, not an inaccurate missing-value
finding. Exact 32-reference, 64-parent/tree-depth and 100,000-reader-work
boundaries are covered. The reader's existing tolerant field classification
is not complete malformed-field-tree validation; skipped malformed/cyclic
branches and rich/plain equivalence remain separate parity work.

There are 186 new authored cases covering both profiles, text/choice
types, null representations, string/stream values, inheritance and
incomplete boundaries. The initial 174-case run had 13 failures.
Forty-four independent native UA-2 controls give 26 exact-rule/file
agreements (14 clean positives, 12 negatives), including 12 corrected
false-negative verdicts. Eighteen disagreements remain explicit:
12 indirect-null/dangling RV cases where primary null equivalence
exempts RV; two equal XML-stream RV cases rejected by the reference;
and four inherited V cases rejected by the reference despite primary
field inheritance. These disagreements are not counted as clean positives.
All 44 corresponding UA-1 outputs are unchanged.

Fresh 435-PDF/870-result public-corpus output is identical to the
immediate baseline. Full Release suite: 13,986 passed, zero failed/skipped.
Solution build: zero errors and two retained machine-feed NU1507 warnings.
Reporting tool: 20 passing tests. The 8.10.3.3-1 mapping measures this
scoped presence overlap, not complete coverage of its equality predicate.
External evidence remains session-only; full PDF/UA parity is not claimed.

## 2026-11-05 UA-2 null file-attachment relationships

The `PDFUA2-FILEATTACH-AFRELATIONSHIP` check now treats missing,
direct-null, indirect-null and nonexistent-reference relationships as
omission. Previously every raw entry, including null, satisfied the
requirement. Direct/indirect non-null values keep the existing presence
policy; this slice does not add relationship vocabulary/type validation.
The scoped facts follow the pinned veraPDF v1.30.3 UA-2 profile,
8.9.2.4.10-1, together with primary ISO 32000-1 7.3.9 null equivalence.
Full primary UA-2 8.9 remains unavailable in the public ISO preview.

File-specification and relationship references use the existing
32-reference bounded resolver. Cycles or exhausted bounds produce one
`PDFUA2-FILEATTACH-INCOMPLETE` Error, not a known-missing relationship
finding. Genuine null file specifications retain the existing exemption;
other attachment requirements and UA-1 behavior are unchanged.

Thirty-two authored cases cover both profiles, direct/indirect file
specifications, null boundaries and cyclic/depth-exhausted references;
nine failed before correction. Twelve independent native UA-2 controls
give exact-rule and whole-file agreement (four clean positives, eight
negatives), correcting six false-negative verdicts. All twelve
corresponding UA-1 outputs are unchanged. A fresh 435-PDF/870-result
public-corpus run is identical to the immediate baseline.

Full Release suite: 13,800 passed, zero failed/skipped. Solution build:
zero errors and two retained machine-feed NU1507 warnings. Reporting tool:
20 passing tests, including the existing 8.9.2.4.10-1 family mapping.
External controls/reference artifacts remain session-only; no full
PDF/UA or complete attachment conformance is claimed.

## 2026-11-05 File descriptions and null embedded-file entries

The UA-2 `PDFUA2-FILESPEC-DESC` check now inspects file specifications in
the catalog's EmbeddedFiles name tree, rather than assuming every object
with EF is in that tree. It requires Desc there even when EF is omitted,
and does not require Desc merely because an orphan or associated-file
dictionary has EF. This boundary follows the pinned veraPDF v1.30.3
UA-2 profile, 8.14.1-1; full primary 8.14 remains unavailable in the
public ISO preview. Existing Desc string/type policy is unchanged.

The traversal reads only dictionaries/references, not embedded payloads.
It reuses the existing bounded reference resolver and name-tree audit
depth/work constants: 32 indirect references, depth 64 and 200,000 total
node/pair work. Cycles, malformed entries and exhausted bounds produce
one `PDFUA2-FILES-INCOMPLETE` Error while known missing descriptions
remain reported. This is description inspection, not complete name-tree
syntax validation. Limits, ordering and other tree semantics are not
silently claimed as covered.

The shared embedded-file F/UF check also treats missing, direct-null,
indirect-null and nonexistent EF entries consistently under primary
ISO 32000-1 7.3.9. Cyclic/depth-exhausted EF references produce the
separate `PDFUA-FILESPEC-INCOMPLETE` Error, not apparent null/omission.
Other real embedded-file filename requirements are retained.

There are 140 scoped authored cases: 24 updated null-EF expectations
and 116 new description/scope/boundary cases. The initial 124-case run
had 19 failures before correction; expanded controls exposed four
further unresolved-reference failures until bounded resolution was reused.
Exact depth, reference and work boundaries, cyclic siblings, malformed
pairs, both profiles and direct/nested name trees are covered.

Of 108 independent native-profile controls, all 54 UA-2 description-rule
results agree with the reference. Whole-file agreement is 104/108:
90 clean positives and 14 negatives. Two missed description violations
and 13 false-positive verdicts are corrected. Four existing disagreements
remain explicit: two UA-1 indirect-null/dangling EF cases accepted under
primary null equivalence, and two UA-2 real orphan embedded-file cases
still rejected by the retained shared F/UF check while the reference
accepts them. The latter lose only an irrelevant Desc finding, not their
file rejection. All 108 corresponding UA-1 outputs are unchanged.

Fresh measurement of all 435 public PDFs/870 profile results preserves
every verdict and every UA-1 output. Seven UA-2 results lose only the
irrelevant Desc finding: five cross-profile UA-1 attachment fixtures and
two native UA-2 negatives that retain their namespace/attachment errors.
Full Release suite: 13,768 passed, zero failed/skipped. Solution build:
zero errors and two retained machine-feed NU1507 warnings. Reporting tool:
20 passing tests, including the 8.14.1-1 family mapping. External evidence
remains session-only; full PDF/UA parity is not claimed.

## 2026-11-05 UA-2 namespace-aware Form widget cardinality

The existing `PDFUA2-FORM-WIDGETS` rule now selects Form structure
elements through the UA-2 namespace-aware standard-type resolver.
Previously a foreign role mapped to Form escaped the limit, while a
foreign type merely named Form could receive an irrelevant error even
when mapped to Artifact or P. The direct OBJR widget-counting predicate
and Error severity are unchanged. This does not infer transparent-group,
descendant, or semantic counting rules.

The at-most-one requirement was cross-checked against the pinned
veraPDF v1.30.3 UA-2 profile, 8.10.1-2. The public primary ISO preview
still does not include full 8.10. Fifty-one authored cases cover zero,
one and two widgets; default/PDF 2.0 Forms; direct/chained foreign
mappings; Artifact/P mappings; unmapped foreign types; both profiles;
and non-widget object references. Five cases failed before the fix.

Twenty-seven independent native UA-2 controls give exact-rule and
whole-file agreement (15 clean positives, 12 negatives). Two mapped
Form false-negative verdicts and one mapped-Artifact false positive are
corrected. Two other invalid controls lose only the irrelevant Form
cardinality finding: the P-mapped type still fails the enclosing-Form
requirement, and the unmapped type retains role/incomplete errors.
Three non-widget controls intentionally remain invalid for Link nesting;
their missing widget-count error is not presented as whole-file acceptance.
All 27 corresponding UA-1 outputs are unchanged.

A fresh 435-PDF/870-result public-corpus run is identical to the
immediate previous baseline. Full Release suite: 13,652 passed, zero
failed/skipped. Solution build: zero errors and two retained machine-feed
NU1507 warnings. Reporting tool: 20 passing tests, including the existing
8.10.1-2 family mapping. External controls/reference artifacts remain
session-only; this is not full PDF/UA parity.

## 2026-11-05 UA-2 unconditional XFA exclusion

PDF/UA-2 now reports `PDFUA2-XFA` (Error, ISO 14289-2 8.10.1)
for a resolved non-null AcroForm XFA entry, regardless of NeedsRendering.
The old dynamic-rendering proxy missed static XFA and omitted/false
rendering flags. UA-1 configuration inspection and its bounded
incomplete findings are unchanged; `PDFUA-XFA-DYNAMIC` is now UA-1-only.
This is an entry-presence exclusion, not XFA packet validity or execution.

The factual predicate was cross-checked against the pinned veraPDF
v1.30.3 UA-2 profile, 8.10.1-3. Full primary 8.10 is not in the public ISO
preview. Missing, direct-null, indirect-null and nonexistent-reference
entries are equivalent under primary ISO 32000-1 7.3.9. The reference
rejects the latter two representations under UA-2; those six measured
disagreements are retained rather than copying raw-reference rejection.

Ninety authored cases cover the rendering flags, stream/packet
configuration values and null/presence boundaries: 30 new cases and 60
updated expectations, with 36 failures before the fix. Fifty-four
independent native-profile controls give 48 exact-rule and whole-file
agreements (27 clean positives, 21 negatives), plus the six explicit
UA-2 null-reference disagreements. Ten native UA-2 false negatives are
corrected. Three previous rejections become acceptances: one direct-null
case agrees with the reference; two indirect-null/dangling cases remain
reference disagreements. Five other cases change only the finding ID.
All 54 corresponding UA-1 outputs are unchanged.

A fresh 435-PDF/870-result public-corpus run preserves every verdict
and every UA-1 output. Its only finding change replaces the old dynamic
ID with `PDFUA2-XFA` when the UA-1 dynamic-XFA negative is checked as
UA-2; that cross-profile result is not a clean UA-2 control. Full Release
suite: 13,601 passed, zero failed/skipped. Solution build: zero errors
and two retained machine-feed NU1507 warnings. Reporting tool: 20 passing
tests, including the 8.10.1-3 family mapping. External evidence remains
session-only; no full PDF/UA parity is claimed.

## 2026-11-05 UA-2 namespace-aware widget structure

The widget Form-parent check now uses the UA-2 namespace-aware standard
type resolver and excludes Artifact ancestors. A foreign role correctly
mapped to PDF 2.0 Form is accepted, as are direct/mapped Artifact widgets;
a type merely spelled Artifact in the PDF 1.7/default namespace is not
silently treated as a PDF 2.0 Artifact. UA-1 keeps its existing policy.
The additional-action check reuses the same Artifact ancestry predicate;
its behaviour is unchanged. The reported UA-2 Form-parent clause is now
8.10.1 rather than the old generic 8.9.

The scoped facts are cross-checked against the pinned veraPDF v1.30.3
UA-2 profile, 8.10.1-1: the enclosing Form requirement has an Artifact
exception. Full primary 8.10 remains unavailable in the public ISO
preview; this is not complete widget classification or label semantics.
The legacy check still applies to nonfield widgets with an enclosing
structure element, unlike the profile's isFieldWidget exemption. This
slice does not broaden that unverified classification boundary.

Eighteen authored cases cover both profiles, direct/mapped Form and
Artifact, ancestor Artifacts, wrong namespaces and unresolved foreign
roles; five failed before the correction. Nine independent native UA-2
PDF controls give exact-rule and whole-file agreement: four clean
positives and five negatives. Three previous false-positive verdicts
are fixed. Two deliberately invalid document-root controls lose only
the irrelevant Form-parent error and remain negative in both validators;
they are not Artifact-positive documents. All nine corresponding UA-1
outputs are unchanged.

The earlier 32-widget collection gains two further Artifact-positive
verdicts. Comparison to its older retained baseline also shows four
cross-profile UA-1 tooltip findings removed by the preceding null-name
ownership fix; those cumulative changes are explicitly retained, not
attributed to this UA-2-only structure correction. Fresh measurements
of all 435 public PDFs/870 profile results are identical to the immediate
previous baseline. Full Release suite: 13,571 passed, zero failed/skipped.
Solution build: zero errors and two retained machine-feed NU1507 warnings.
Reporting tool: 20 passing tests, including the Form-parent family
mapping. External reference/runtime/PDF artifacts remain session-only.

## 2026-11-05 Null widget names and terminal-field ownership

The simple form reader, typed form-detail reader, editor value updater
and editor form index now treat a widget child's null T as an omitted
partial field name. Previously they could select the widget dictionary
as a new terminal field instead of retaining the owning field, losing
that field's tooltip or writing a value to the wrong dictionary.
Direct/indirect null and nonexistent references follow the same primary
ISO 32000-1 7.3.9 null-equivalence rule as the FT correction below.
The editor index also treats null Kids as omission; existing non-null
name/child-array policies are unchanged, including empty-string names.
This does not redesign field-tree ownership or repair arbitrary malformed
field hierarchies.

The editor deliberately does not retain imported null objects in its
object map. Its local form-entry predicate therefore accounts for absent
indirect targets as well as direct null, without changing the general
editor resolver. Reader traversal uses the file's existing resolver.

The initial sixteen authored cases produced eleven failures, after the
save assertions were corrected to follow references rather than assuming
the writer preserved object numbers. Final nineteen cases cover both
read APIs, form-index handles, saved field/widget values, real and
empty-string partial names, indirect names, null Kids and UA-1 terminal
tooltip ownership. Six independent native UA-1 terminal-tooltip controls
now agree with the reference at rule 7.18.1-3 and whole-file level,
correcting six false-positive verdicts. The same 36-control collection
now has 24 whole-file agreements and twelve explicitly retained field/
label disagreements; every corresponding UA-2 output is unchanged.

Fresh measurement of all 435 public PDFs/870 profile results is identical
to the null-FT baseline. Full Release suite: 13,553 passed, zero
failed/skipped. Solution build: zero errors and two retained machine-feed
NU1507 warnings. External PDFs and reference reports remain session-only.

## 2026-11-05 PDF/UA null field-type equivalence

The shared widget tooltip prerequisite now resolves FT before deciding
that a field type exists. Omission, direct null, indirect null and a
nonexistent indirect object are equivalent; a null on the widget does not
hide a real FT on its parent. Non-null malformed FT values retain the
previous behaviour. This is a dictionary-entry correction for both UA
profiles, not a new field-widget classifier or tooltip/label policy.

Primary provenance is ISO 32000-1 7.3.9 (printed p21, public Adobe PDF
physical p29): null dictionary values are equivalent to omission, and
references to nonexistent objects are null. Seventy-two authored cases
cover both profiles, direct/indirect types, parent inheritance and
tooltips; fourteen failed before the one-line correction.

Thirty-six independent native-profile PDF controls verify the null/
omission invariant in six profile/parent contexts. Six native file
verdicts change: three UA-2 controls become reference-agreeing positives;
three UA-1 controls become equivalent to the already-accepted omission
control, but disagree with the reference's stricter widget-description
requirement. Only 18/36 whole-file verdicts agree. The eighteen explicit
disagreements also include six UA-1 null-T widget-child ownership cases,
six UA-2 tooltip-versus-label cases and two UA-2 nameless-field
classification cases. These are not claimed as full field conformance.
In particular, raw null T in field-tree traversal can misidentify a
terminal field; that separate mechanism remains pending.

The existing 32 widget-context controls gain two UA-2 reference-agreeing
nonfield positives; their cross-profile UA-1 runs only lose the spurious
tooltip finding and remain invalid for version/identification. Historical
additional-action counts above/below are not rewritten. Fresh measurements
of all 435 corpus PDFs/870 profile results are identical to the previous
baseline. Full Release suite: 13,534 passed, zero failed/skipped. Solution
build: zero errors and the two retained machine-feed NU1507 warnings.
No reference engine, profile XML or external PDF is bundled.

## 2026-11-05 UA-2 widget additional-action context

`PDFUA2-WIDGET-AA-CONTENTS` checks the independently verified
8.10.2.3-2 predicate: a structurally tagged field widget with an AA
dictionary requires its own string-valued Contents entry. A tooltip,
structure Alt, label, or inherited field Contents is not a substitute.
An empty string establishes entry presence; this does not assert that its
description is semantically adequate. Null AA is omission. Artifact
ancestors are resolved through the namespace-aware role mapper; untagged
and nonfield widgets are outside this predicate. Hidden/outside-crop
widgets with a structure parent are not exempted by their visual state.
UA-1 and existing widget-description checks are unchanged.

The factual predicate is cross-checked against the pinned veraPDF
v1.30.3 UA-2 profile, 8.10.2.3-2, and independently implemented in
`ValidationPdfUA2.cs`. A field widget is recognized through its own
FT/name or a parent field dictionary; this does not validate field-tree
ownership or arbitrary malformed field definitions. The public ISO
14289-2 preview does not include 8.10, so the complete primary clause
was not independently obtained. No scripting or additional action is
executed.

Verification:

- The initial 144 authored cases had six failures before implementation.
  Final coverage adds 148 cases for own/missing/null/empty Contents,
  AA omission/null/dictionary, both profiles, visual-state boundaries,
  field-parent inheritance and mapped/ancestor Artifact exclusions.
- Thirty-two independent native UA-2 controls give 32 exact-rule
  agreements. Eleven new findings include ten newly rejected files and
  one control already rejected for an unrelated nesting violation.
  All 32 corresponding UA-1 outputs are unchanged.
- Only 18/32 whole-file verdicts agree: six clean positives and twelve
  negatives. Eight controls still miss the separate 8.10.2.3-1
  label-or-Contents condition; six other reference-clean controls retain
  existing FreeDF field-type/Form-structure/ParentTree rejections. Those
  fourteen disagreements are not additional-action-rule agreements or
  evidence of complete widget conformance.
- Label investigation remains open. Initial empty Lbl nodes did not
  establish a label in the reference; direct non-empty and role-mapped
  Lbl children did, but grouping them under Div did not. Without primary
  verification of that containment meaning, no label predicate was
  guessed and the legacy UA-2 tooltip/Alt proxy remains explicitly
  incomplete. Raw null FT classification and Artifact/Form structure
  handling also need separate boundary review.
- Fresh 435-PDF/870-result corpus output is identical to the rendering-use
  baseline, including findings and verdicts. Full Release suite: 13,462
  passed, zero failed/skipped. Solution build: zero errors and two
  retained machine-feed NU1507 warnings. Reporting tool: 20 passing tests,
  now including the new rule mapping. External evidence remains
  session-only.

## 2026-11-05 PDF/UA rendering-only font embedding

`PDFUA-FONT-EMBEDDED` now requires observed text-showing use in a rendering
mode other than 3, rather than merely finding a font resource. This applies
to both UA profiles, including fonts selected through ExtGState, invoked
Forms with inherited/restored state, and inspected annotation appearances.
Selecting a font without showing text does not require embedding. Empty
text-showing operators and adjustment-only TJ arrays remain conservatively
in scope; no empty-glyph exemption is claimed. Mode 7 (glyph clipping)
requires embedding, and any observed non-mode-3 use defeats the exemption.
Other font checks and PDF/A policies are unchanged.

The factual requirements were cross-checked against the pinned veraPDF
v1.30.3 UA-1 7.21.4.1-1 and UA-2 8.4.5.5.1-1 profiles, whose descriptions
limit embedding to fonts used for rendering and exempt mode 3.
ISO 32000-1 9.3.6/Table 106 (printed pp246-247, public Adobe PDF physical
pp254-255) independently establishes invisible mode 3 versus clipping
mode 7. Full ISO UA font clauses have not been independently obtained;
this is not complete font conformance.

An unembedded font without observed rendering use is not silently exempted
when the usage scan is incomplete. A single `PDFUA-FONT-USAGE-INCOMPLETE`
Error reports that uncertainty. UA inspection now reuses the existing
strict content-decoding checks, records token exhaustion, and marks
uninspected pattern/Type3 glyph content as incomplete. Controlled decoding
failures also produce `PDFUA-CONTENT-SCAN-INCOMPLETE`. These findings are
not counted as mapped embedding-rule overlap. This remains bounded static
inspection, not text rendering, pattern execution, or complete usage
discovery inside Type3 glyph procedures.

Verification:

- The initial 76 authored controls produced 28 failures before the fix.
  Final coverage adds 151 cases for both profiles, all rendering modes,
  page/Form/appearance/uninvoked-Form surfaces, malformed content, missing
  references, pattern/Type3 uncertainty and token limits. The existing
  ExtGState tests now expect only the intentional UA mode-3 embedding
  exemption; their CIDToGID expectations remain unchanged. An older
  structure fixture now actually links the text stream it intended to test.
- Forty-eight independent native-profile PDF controls give 44 exact-rule
  and whole-file agreements: 16 clean positives and 28 negatives.
  Sixteen previous resource-only/mode-3 false-positive verdicts are fixed.
  Four explicit disagreements remain: the reference accepts a font first
  shown in mode 3 and then shown visibly in mode 0, on both page and Form
  surfaces in both profiles. FreeDF rejects that observed rendered,
  unembedded use; the reference acceptance is not copied.
- Fresh measurements of all 435 public PDFs/870 profile results preserve
  every file verdict. Only the unused-font finding on the native dynamic
  XFA negative disappears, under both profiles; its dynamic-XFA rejection
  remains. No new incomplete/internal findings appear in that corpus.
- Full Release suite: 13,314 passed, zero failed/skipped. The reporting
  tool suite has 20 passing tests, including non-overlap for incomplete
  embedding inspection. External PDFs, profile XML and runtime artifacts
  remain session-only.

## 2026-11-04 UA-1 dynamic XFA configuration

`PDFUA-XFA-DYNAMIC` now inspects the inert XFA configuration for UA-1
instead of using catalog NeedsRendering as a proxy. The supported path
is `config/acrobat/acrobat7/dynamicRender` in the
`http://www.xfa.org/schema/xci/3.0/` namespace, with exact text `required`.
Both named config packets and a config child of a single XDP stream are
checked. A forbidden/missing/differently cased/whitespace-padded value or
wrong-path element does not establish that predicate. UA-2 retains its
previous behaviour; this does not add XFA rendering or execute scripts.

The factual predicate is cross-checked against the pinned veraPDF v1.30.3
UA-1 profile 7.15-1. The PDF Association's CC BY 4.0
[Matterhorn Protocol 1.1](https://pdfa.org/wp-content/uploads/2021/04/Matterhorn-Protocol-1-1.pdf),
checkpoint 25-001, identifies the dynamicRender value `required` as the
failure condition. Its publicly indexed content was available, but direct
PDF retrieval and the Adobe XFA 3.3 specification download returned 403.
Full XFA schema semantics and the full ISO UA-1 clause were not
independently obtained. The implemented namespace/path subset is supported
by the native fixture and independent controls, not claimed as exhaustive.

The existing inert `XfaReader.ParseXml` supplies DTD prohibition, no XML
resolver, and byte/element/depth/text bounds. Validation visits at most
1000 packet pairs with a shared 32 MiB decoded configuration/XML budget.
Malformed packet references, undecodable selected streams, malformed or
truncated XML and inspection limits produce a separate bounded Error,
`PDFUA-XFA-INCOMPLETE`, not a dynamic-rendering finding or a silent pass.
The byte budget is checked after the existing stream decoder returns;
this is not a new bounded-inflate guarantee. Other packet XML is not
parsed merely to classify configuration. Foreign/no-namespace lookalikes,
other XCI versions, full schema validity and template/data interpretation
remain outside this subset.

The 76 authored controls cover both profiles, packet/single-XDP forms,
three rendering-flag states, five values, namespace/path lookalikes,
malformed/DTD XML, null/dangling/malformed packet references and depth/
packet bounds. The pre-fix run has 24 failures; focused XFA/deep-validation
passes 843 tests, full Release **13163/13163**, zero skipped. Solution
build has zero errors and the two retained machine-feed NU1507 warnings.
All 19 Python tool tests pass; incomplete XFA does not count as overlap.

The first 48 real-document controls exposed a pre-existing, independent
font Error: the native dynamic fixture has no page content but an unused
unembedded page font resource. That initial masked run is retained rather
than counted as clean positives. Corrected controls remove only that
unused page font resource, preserve all XFA packets/template/data and
structure, and confirm the page content remains empty. An isolated build
of exact pre-fix commit `672d83b6` measures those same corrected files.

All **36 canonical-namespace controls agree on exact rule and whole-file
verdict**: 30 clean positives and six dynamic negatives. Four required
configurations with absent/false NeedsRendering are newly rejected; ten
canonical static controls with NeedsRendering true are newly accepted.
Twelve additional foreign/no-namespace controls explicitly disagree with
the reference, which matches their local names despite the namespace
difference. FreeDF does not turn these lookalikes into the supported XCI
predicate; four lose the previous flag-only rejection. Those twelve are
not counted as exact-rule agreement or established normative positives.
Every UA-2 output is unchanged.

Fresh measurement of all 435 public PDFs/870 profile results is identical
to the preceding file-specification run: no finding/verdict changes,
regressions or new incomplete/internal findings. Native UA-1 7.15-1 now
has mapped-family overlap 1/1; this is not complete dynamic-XFA or PDF/UA
parity. No external PDFs, XML, schema or runtime are bundled.

## 2026-11-04 UA-1 embedded-file Unicode name boundaries

The existing `PDFUA-FILESPEC-UF` check now measures the decoded UF text
length for UA-1 rather than accepting a UTF-16BE byte-order marker alone
as a non-empty name. Direct/indirect strings retain the same treatment.
The UA-1 embedding trigger also resolves EF before classifying null as
omission; absent, direct-null, indirect-null and dangling-reference EF
do not alone require embedded-file names. Actual direct/indirect EF
dictionaries still require names. UA-2 and reader decoding are unchanged.

The non-empty requirement is cross-checked against the pinned CC BY 4.0
veraPDF v1.30.3 UA-1 7.11-1 predicate. Independently, public ISO
32000-1:2008 Table 44, printed p102/physical p110, defines UF as a
Unicode text string; 7.9.2.2 defines its leading UTF-16BE marker.
Clause 7.3.9, printed p21/physical p29, makes a null dictionary value
equivalent to omission and a nonexistent indirect object equivalent to
null. Table 44's EF entry, printed p103, identifies embedded streams.

There are 72 authored controls across both profiles, direct/indirect
names, ASCII/UTF-16BE names, marker-only/empty/null/whitespace values,
and six EF states with/without names. The pre-fix run has five failures:
two marker-only UF cases and three resolved-null EF cases. Focused
deep-validation passes 607 tests; full Release passes **13087/13087**,
zero skipped. Solution build has zero errors and the two retained
machine-feed NU1507 warnings.

Eighteen independently mutated native UA-1 PDFs produce 16 exact-rule
and whole-file agreements. FreeDF has 12 clean positives and six
negatives; three verdicts change: marker-only UF is newly rejected,
while missing names with direct/indirect null EF are newly accepted.
Every corresponding UA-2 result is unchanged. The two disagreements
are retained explicitly, not called agreement or complete parity:

- The reference rejects marker-only F, whereas FreeDF preserves its
  existing byte-length treatment. Table 44 defines F as a file
  specification string rather than UF's Unicode text string, and
  7.11.2.4 allows multiple-byte file specification strings. A stricter
  Unicode interpretation of F was not established and does not ship.
- The reference rejects indirect-null EF with missing names while
  accepting direct-null/absent EF. FreeDF treats both null forms as
  absent under the primary null-equivalence requirement.

The UA-2 reference also reports description errors for these reachable
file specifications even when EF is absent. Its semantics are therefore
not inferred from UA-1; no new UA-2 filename/description restrictions or
relaxations ship. Fresh public-corpus measurement, 435 PDFs/870 profile
results, is identical to the preceding permissions run with no finding
or verdict changes, regressions or new incomplete/internal results.
Malformed non-null EF values, complete filename encoding/path validity,
broader UF decoding and full UA-2 file-description classification remain
open. External PDFs/profiles stay session-only evidence.

## 2026-11-04 UA-1 encryption permission presence

The reporting audit exposed a real partial predicate: a readable encrypted
file with no P entry could be reported as UA-1 compliant. The existing
`PDFUA-ENCRYPT-ACCESS` Error now also rejects missing or resolved-null P
for UA-1. Existing numeric bit-10 checking and all UA-2 behaviour are
unchanged. This supersedes the missing-P limitation recorded in the
preceding reconciliation below, without claiming complete permission
dictionary validation.

The pinned veraPDF v1.30.3 UA-1 7.16-1 predicate requires P presence as
well as bit 10. Independently, public ISO 32000-1:2008 7.6.3.2 Table 21,
printed p60/physical p68, makes P a required integer for the standard
security handler; the same clause explains its permission flags.
The implementation adds no reader/authentication restriction and does
not modify decryption or editor permission enforcement.

Four serialized AES-256 authored controls initially have two failures
(missing/null P) and two passes (bit enabled/disabled); all pass after the
fix. An unencrypted control also confirms no P requirement is introduced
for unencrypted documents. The focused encryption/security/deep-validation
run passes 568 tests before that final unencrypted control is added; the
final full Release suite passes **13015/13015**, zero skipped. Solution
build has zero errors and the two retained machine-feed NU1507 warnings.

Six independent AES-256 controls are derived from a public native UA-1
positive using PyMuPDF encryption and length-preserving P mutations.
FreeDF now has one clean positive and five negatives: all four missing/null
variants were previously compliant and are now rejected. Both original
bit-value variants retain their outcomes; all six UA-2 outputs are
unchanged. PyMuPDF can open all six. veraPDF produces only two complete
reports, with exact-rule/whole-file agreement on those two bit-value
controls. Its four missing/null variants fail to open as encrypted files,
even on explicit owner-password retry. These four are **reference
exclusions, not exact-rule agreement or clean reference negatives**.
The primary requirement and reproducible before/after controls justify
the narrow fix; an incomplete oracle is not represented as full coverage.

Fresh measurement of all 435 public PDFs/870 profile results is byte-for-
byte equivalent as decoded result objects to the preceding private-use
run: no finding/verdict changes, regressions or new incomplete/internal
results. No external PDFs or encryption runtime are bundled. Malformed
non-null P types, complete permission/security-handler validation and
reader authentication edge cases remain outside this slice.

## 2026-11-04 remaining implemented-family reconciliation

A second reporting audit adds twelve missing reference-to-Error-family
mappings: UA-1 optional-content Name/AS, embedded-file F/UF and accessibility
permissions; UA-2 identification presence/prefix, catalog language, composite
CIDSystemInfo, CIDToGIDMap, CMap embedding, font embedding and ToUnicode
availability. Existing validator behaviour is unchanged. The related Error
implementations were compared with the pinned v1.30.3 standalone profile
predicates; these are family mappings, not claims of complete predicates.
In particular, the permissions implementation does not yet reject missing P,
and composite ToUnicode inspection remains scoped and partly heuristic.

The expanded tool controls initially fail 24 subtests (mapped overlap and
other-rule-only agreement for each addition); all 18 tool tests then pass.
Reconciliation reuses retained complete reference rules and the latest
435-file/870-result FreeDF run from the private-use slice, without rerunning
Java or changing validation outputs. All 10 UA-1 occurrences of the four
new mappings overlap. The eight UA-2 mappings overlap 21/23 occurrences:
`8.4.5.4-t01-fail-a.pdf` has no matching CIDSystemInfo finding, and
`8.4.5.4-t03-fail-a.pdf` has a CMap-reference Error rather than the mapped
CMap-embedding Error. Neither is silently counted as predicate coverage.

Current native reference counts are UA-1 97 distinct failing tests, 56
mapped families and 42 with no observed family overlap (including one
mapped test); UA-2 48 distinct failing tests, 45 mapped and three with no
observed overlap. Partially covered families can still have missed
occurrences, as the two font cases demonstrate. These are conservative
reporting counts, not standards-coverage percentages or new runtime fixes.
Historical reconciliation counts below remain dated evidence.

## 2026-11-04 PDF/UA-2 private-use language strings

The next masked reference failure is now reported by its own rule:
`PDFUA2-TEXT-STRING-PUA` checks catalog, parsed structure and visited Span
property-list language strings. It enumerates decoded Unicode scalars,
not UTF-16 code units, covering U+E000..U+F8FF, U+F0000..U+FFFFD and
U+100000..U+10FFFD. Neighbouring non-private codepoints are not classified
as PUA. UA-1 and base-reader behaviour are unchanged. Property-list PUA
and grammar diagnostics have separate per-page/invoked-Form budgets, so
an earlier non-PUA syntax error cannot hide a later private-use finding.

The predicate provenance is the veraPDF Consortium's CC BY 4.0 v1.30.3
standalone UA-2 profile 8.6-1, pinned at
`c4f57effb0d3618827c87bd71882930d752be6e3`.
The [Unicode private-use FAQ](https://www.unicode.org/faq/private_use.html)
independently defines the three immutable ranges. The public UA-2 normative
preview stops before clause 8.6; full ISO clause text was not independently
obtained. The change is explicitly a language-string subset, not a complete
classification of all human-readable PDF strings.

The corrected authored set has 146 cases, with 43 failures when the new PUA
predicate is disabled. It includes both UTF-16BE and PDF 2.0 UTF-8 strings,
all range edges, UA-1 exclusions, field-string exclusions and diagnostic
ordering/bounding. Final full Release passes **13010/13010**, zero skipped;
the 535-test focused class passes after correcting two new xUnit2031
assertion-style warnings. The final solution has zero errors and only the
two retained machine-feed NU1507 warnings. All 18 Python tool tests pass.

Independent mutations of one native UA-2 document produce **78 exact-rule
and whole-file agreements**: 42 PUA rejections and 36 non-PUA predicate
controls. Only six are clean whole-file positives; the other 30 non-PUA
strings remain grammar-invalid language tags and are not counted as clean
positives. The retained pre-fix UaDump has no PUA finding on any of these
controls, although grammar already rejects the invalid tags.

Fresh measurement of all 435 public PDFs/870 profile results adds exactly
one finding, to native UA-2 `8.6-t01-fail-a.pdf`, without changing its verdict.
UA-1 outputs are unchanged. There are no reference-positive regressions,
internal or new incomplete findings. Native reference 8.6-1 now has mapped
family overlap 1/1; zero file-verdict misses remains distinct from full
rule coverage or certification.

Rejected draft: an initial 96-control field-name/tooltip/mapping-name probe
had 42 reference acceptances of field PUA. The source document's Document
element has empty children, and several P elements are unused; those
acceptances do not settle human-readable field classification or normative
permission. A first structure mutation also hit an unused P; final controls
instead mutate the reachable Document element. No unverified field-string
rejection or field-traversal requirement ships. Other human-readable strings,
binary-versus-text classification, non-string Lang values, unused/non-Span
properties, appearance-only streams and existing traversal limits remain
open. External PDFs/profiles stay session-only evidence.

## 2026-11-04 PDF/UA property-list language syntax

The existing page/invoked-Form content walk now checks string-valued `Lang`
in Span marked-content property lists, including named resource properties
and indirect language strings in those resource dictionaries. Both UA profiles
reuse the existing RFC 3066 shape expression; absolute anchors reject a final
line feed rather than accepting the prefix before it. Present empty/BOM-only
catalog and structure language strings now also receive `PDFUA-LANG-INVALID`.
UA-2 language finding references are corrected to 8.4.4. This is grammar
validation, not IANA registration or semantic language identification.

The predicate provenance is the veraPDF Consortium's CC BY 4.0 v1.30.3
UA-1 profile 7.2-29 and standalone UA-2 profile 8.4.4-2, pinned at
`c4f57effb0d3618827c87bd71882930d752be6e3`. Full ISO PDF/UA clause text
was not independently obtained. Public ISO 32000-1 14.9.2 (printed pp611-612)
defines language identifiers and their hierarchy; base PDF permits empty
identifiers as unknown, whereas these UA profile predicates require a tag.
[RFC 3066 2.1](https://www.rfc-editor.org/rfc/rfc3066.html#section-2.1)
supplies the exact primary/subtag character grammar.

The corrected authored set has 140 cases. The first valid-syntax baseline
had 44 failures/132 cases; subsequently enabling matching UA-2 coverage and
empty catalog/structure cases exposed 48 failures/136 cases in a separate
red stage. An initial draft incorrectly used indirect references inside
inline content dictionaries; those illegal content-stream cases were removed,
not accommodated by changing the parser. Indirect resource values remain
tested. Final full Release passes **12864/12864**, zero skipped; solution
build has zero errors and the same two machine-feed NU1507 warnings.
All 18 Python cross-check tool tests pass.

Independent mutations of native UA-1 and UA-2 public fixtures produce
**76 exact-rule/whole-file agreements: 28 clean positives, 48 negatives**.
Direct/named/empty invoked-Form declarations, mixed-case and grandfathered
tag shapes, invalid primary subtags, empty/BOM-only strings, and final LF
are covered. They are mutations of two documents, not 76 independent layouts.
The retained pre-fix UaDump accepts all initial 30 UA-1 controls, including
18 independently invalid property declarations.

A fresh 435-file/870-result run changes exactly 24 finding sets: six native
UA-1 documents, six native UA-2 documents, and their cross-profile evaluations
gain the previously missing syntax Error. There are no verdict changes,
reference-positive regressions, new internal or incomplete findings.
Mapped syntax-family overlap is now **16/16 UA-1** and **17/17 UA-2** native
reference failures; matching a finding family still does not establish full
predicate or standards coverage. File-verdict misses remain **0 UA-1 / 0 UA-2**.

Scope remains bounded by the existing parsed-structure, page/invoked-Form
walk: non-Span or unused properties, appearance-only/unreachable streams,
non-string language values, traversal truncation, complete language
determinability and registry checks are not newly covered. No base-PDF reader
restriction is added.

The preceding tooltip investigation is explicitly **not implemented**:
12 real-field mutations show veraPDF 7.2-25 accepts catalog or the associated
Form element's own language, but not inherited structure language or a Widget
Lang entry. ISO 32000-1 14.9.2.3 defines structure-language inheritance, but its
precise application to field tooltip strings needs stronger PDF/UA evidence.
FreeDF's inherited-language fallback is retained; reproducing the reference's
stricter behaviour without that evidence is not treated as a parity fix.

## 2026-11-04 PDF/UA optional-content configuration boundaries

The clause-level review after corpus exhaustion found an over-rejection:
UA-2 requires non-empty configuration names only when the optional-content
`Configs` array contains a configuration. Default-only, absent/null Configs
and empty-array cases no longer acquire that Error. UA-1's name requirement
is unchanged. Names use decoded text length, so a UTF-16BE BOM alone is not
a non-empty name. Direct/indirect null `AS` is treated as absent in both
profiles; actual automatic-state arrays remain prohibited. The UA-2 finding
references now correctly cite 8.7, not 8.9.

The conditional requirement and AS prohibition were cross-checked against
the veraPDF Consortium's CC BY 4.0 v1.30.3 standalone UA-2 profile,
8.7 tests 1/2, at pinned commit
`c4f57effb0d3618827c87bd71882930d752be6e3`.
Public ISO 32000-1 7.3.9 (printed p21) specifies that a null-valued dictionary
entry is equivalent to omission. Full ISO PDF/UA clause text was not
independently obtained for this slice.

There are 212 authored controls: the initial conditional-name cases had
18 baseline failures, subsequent null-AS controls exposed 80 failures, and
the BOM-only controls exposed two more. The final focused selector passes
249 tests; full Release passes **12724/12724**, zero skipped. Solution build
has zero errors and the same two machine-feed NU1507 warnings; all 18 Python
cross-check tool tests pass. The UA-2 name family is now mapped by the tool.

Independent mutations of the real public optional-content fixture give
**58 exact-rule and whole-file agreements: 13 clean positives, 45 negatives**.
These include null/empty/non-empty Configs, direct/indirect configuration
dictionaries, AS-null versus actual AS, and four decoded-name BOM controls.
The first reference comparison exposed the AS-null bug before it shipped.
They are mutations of one document, not 58 independent document layouts.

Fresh measurement of all 435 public PDFs/870 profile results retains
**0 UA-1 / 0 UA-2 file-verdict misses**. Native UA-1 and UA-2 results are
unchanged. Exactly two cross-profile UA-2 evaluations of UA-1 optional-content
negatives lose the unnecessary name Error; their other Errors and verdicts
are unchanged. No new internal or incomplete findings. Existing configuration
traversal bounds and malformed-structure limitations remain; complete
optional-content validation, rule-level parity and certification are not
claimed. External PDFs/profiles remain session-only evidence.

## 2026-11-04 PDF/UA-1 tables without Headers require complete Scope

When a table has TD cells and none supplies a Headers attribute,
PDF/UA-1 now requires every TH in that table to supply a valid
Row/Column/Both Scope. A partly scoped table is no longer treated as
an advisory merely because some other header has Scope. The existing
`PDFUA-TABLE-HEADER-CONNECTION` Error is reused. Direct, array and
ClassMap attribute lookup is unchanged; explicit/mixed Headers tables
retain their existing reference validation and advisory fallback.
UA-2 is unchanged by this slice.

The requirement provenance is the published veraPDF Consortium v1.30.3
[UA-1 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-1.xml)
7.5 tests 1/2, under CC BY 4.0: when structure is not determined through
Headers/IDs, TH elements need Scope. This is profile cross-checking,
not independent access to the full ISO PDF/UA clause. Public ISO
32000-1 Table 349 (printed page 610) separately defines Scope direction;
the new check enforces presence, not full directional/semantic validity.

All 256 combinations of absent/Row/Column/Both on four header cells
in the retained public partial-Scope table were independently evaluated
with veraPDF 1.30.3. Saved dictionaries and absent TD Headers are checked
separately in Python. All 256 agree on exact rule and whole-file verdict:
81 clean positives, 175 negatives. The preceding implementation accepted
174 of those negatives. All opposite-profile UA-2 outputs are unchanged.
These are mutations of one table, not 256 independent table layouts.

The initial directional-connection model was rejected before shipping:
76 of the 256 reference outcomes differed from that model, while all
matched complete valid Scope presence. No claim is made that the
reference correctly evaluates Row versus Column direction. Missing
Scope is a structural requirement here; accurate associations, spanning
geometry, higher-header relationships and visual semantics remain open.

A fresh FreeDF pass over 435 public files / 870 UA profile results
against retained reference and prior output closes the last UA-1
file-verdict miss. Exactly three finding sets change: add the connection
Error and remove the superseded partial-Scope advisory, including two
already-invalid evaluations. All UA-2 results are unchanged.
**0 UA-1 / 0 UA-2 measured file-verdict misses remain**, with zero
reference-positive regressions or internal errors. No new incomplete
rule was introduced. Thirty-seven of 67 authored cases failed on the
preceding implementation; final focused coverage passes 131 tests.
The full Release suite passes 12512/12512 and the solution builds with
the same two machine-feed NU1507 warnings.

This exhausts the measured public file-verdict gap, not PDF/UA parity,
per-rule coverage or certification. The validators retain explicit
unsupported/incomplete font, content, structure and semantic surfaces.
External PDFs/profiles remain session-only evidence; historical dated
counts below are preserved.

## 2026-11-04 PDF/UA rendered ASCII TrueType widths

PDF/UA-1 and UA-2 now compare the widths of actually shown printable
ASCII bytes (32-126) in non-symbolic simple TrueType fonts with raw
FontFile2, WinAnsiEncoding and no Differences overrides. Resolved
encoding dictionaries with that BaseEncoding and absent/null Differences
are included. The selected Microsoft Unicode (3,1) format-4 cmap maps
these ASCII Unicode values to glyph indices; hmtx advance widths are
scaled by head.unitsPerEm to 1000-unit PDF widths. The repeated final
hmtx advance is supported, as are format-4 delta and range-offset maps.
Widths differ by more than one unit before `PDFUA-FONT-WIDTH` is emitted.
Text rendering mode 3 is exempt; mode 7 and all other rendering modes
are checked. Existing Tf/ExtGState, q/Q, Form and appearance state
determines which bytes are referenced for rendering.

Requirement provenance is the published veraPDF Consortium v1.30.3
[UA-1 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-1.xml)
7.21.5-1 and
[UA-2 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-2.xml)
8.4.5.6-1, under CC BY 4.0, including their one-unit tolerance and
mode-3 exemption. This is profile cross-checking, not independent
access to the full ISO PDF/UA clauses. Public ISO 32000-1 Table 111
(printed page 255) defines the PDF width array and units; 9.6.6.4
(printed page 266) defines the non-symbolic (3,1) mapping path.
The [OpenType cmap](https://learn.microsoft.com/en-us/typography/opentype/spec/cmap#format-4-segment-mapping-to-delta-values)
and [hmtx](https://learn.microsoft.com/en-us/typography/opentype/spec/hmtx)
specifications supply the independently implemented binary layout.

Existing SFNT table-directory checks are shared. The new bounded
inspection checks required metric tables, maxp glyph count,
head/hhea versions and units, hmtx bounds, selected cmap uniqueness,
format-4 array bounds, ordered segments, terminal segment and glyph
indices. Unsupported/malformed inspected metadata and invalid supplied
width arrays produce `PDFUA-FONT-WIDTH-INCOMPLETE` Errors, not guessed
advances. The decoded font bound is 16777216 bytes, applied after
existing stream decoding rather than through a new streaming decoder.
The scan records at most 95 distinct byte codes/pages per font, not
unbounded glyph occurrences.

Both missed public files specify width 192 for space, while independent
FontTools inspection gives advance 512 / unitsPerEm 2048 = 250.
Forty-eight mutations of those files cover 192, 249, 250, 251,
248.99 and 251.01, modes 0/3/7, Tj/TJ/single-quote/double-quote.
Independent saved-file width, program cmap/hmtx and byte-use checks
verify all 48. All were accepted before the fix; 18 are now rejected.
Forty-six agree with veraPDF 1.30.3 on exact rule and whole-file verdict
(30 clean positives, 16 negatives). Two invalid double-quote cases
are explicit disagreements: reference accepts, FreeDF rejects;
PyMuPDF traces independently confirm that the space glyph is shown.
They are not counted as reference agreement. No external PDF, font,
profile XML or reference-engine source is bundled.

A fresh FreeDF pass over 435 public files / 870 UA profile results
against retained reference and prior output closes exactly the two
metrics file-verdict misses. Four finding sets change solely by this
rule, including opposite-profile evaluations already rejected for
version/metadata. **1 UA-1 / 0 UA-2 misses remain**, with zero
reference-positive regressions, internal errors or new width-incomplete
findings. Final parser-boundary corrections preserve all 870 results.
The initial 128 authored cases exposed 61 baseline failures; the final
150 cover tolerance, rendering-state, mapping/dictionary exclusions,
every truncated metadata prefix, malformed tables and resource bounds.
The final full Release suite passes 12445/12445 and the solution builds
with the same two machine-feed NU1507 warnings.

This is not full glyph-width conformance: non-ASCII WinAnsi, other
encodings, Differences, symbolic fonts, CIDFonts, CFF/Type1/Type3,
FontFile3, other cmap formats, missing-width fallback and post-table
glyph-name recovery remain open. Unmapped glyphs are not assigned
guessed widths. Complete font/outline validity, unsupported content
and exhausted traversal bounds remain outside this slice. Zero measured
UA-2 corpus misses is not full PDF/UA-2 parity or certification;
the remaining UA-1 table-scope advisory and historical measurements
remain explicit.

## 2026-11-04 PDF/UA explicit Identity CFF notdef references

The shared content walk records complete high-byte-first two-byte zero
codes in actual text-showing strings, independently per string, under
the selected font. PDF/UA-1 and UA-2 now reject such references for
Type0 fonts with resolved Identity-H/Identity-V encoding and a
CIDFontType0 descendant as `PDFUA-FONT-NOTDEF` Errors. This does not
confuse Unicode U+0000, zero-valued positioning numbers, comments,
unused string operands or adjacent zero bytes across code boundaries
with CID 0. No text extraction or rendering behavior changes.

The requirement provenance is the published veraPDF Consortium v1.30.3
[UA-1 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-1.xml)
7.21.8-1 and
[UA-2 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-2.xml)
8.4.5.9-1, under CC BY 4.0; this is not independently obtained full ISO
PDF/UA clause text. Public ISO 32000-1 9.7.4.2 specifies CID 0 as
analogous to .notdef (printed page 271); 9.7.5.2 defines Identity-H/V
strings as high-byte-first CID pairs (printed page 275). The C# check
is independently implemented; external PDFs/fonts/profiles remain
session-only evidence.

Actual Tj, TJ, single-quote and double-quote operations are inspected
regardless of rendering mode, including modes 3 and 7. Existing q/Q,
Tf/ExtGState font selection, inherited/local Form state and annotation
appearance walking are reused. Only one finding is emitted per font,
using the page of the detected reference. Separate TJ strings are not
concatenated into a synthetic two-byte code.

Forty-four mutations of the two public real CFF files compare CID 0
with CID 1 across all eight modes and four operators. Independent
Python byte-pair checks and FontTools CFF inspection verify the shown
CID and .notdef program slot. Forty-two agree with veraPDF 1.30.3 on
exact rule and whole-file verdict: 22 clean positives, 20 negatives.
Two double-quote CID 0 controls explicitly disagree: the reference
accepts them, while FreeDF rejects. PyMuPDF text traces independently
show GID 0 in both; they are not counted as reference agreement.

A fresh FreeDF pass over 435 public files / 870 UA profile results,
against retained reference reports and preceding subset-listing output,
closes exactly the two .notdef file-verdict misses. Four finding sets
change solely by adding this rule (including the opposite-profile
evaluations already rejected for version/metadata). Remaining misses
are **2 UA-1 / 1 UA-2**, with zero reference-positive regressions,
internal errors or newly added incomplete findings.
Fifty authored cases include font-state, byte-boundary, Form,
appearance, ExtGState and operator controls; 28 failed on the preceding
implementation. The combined focused selector passes 319 tests;
the full Release suite passes 12295/12295, and the solution builds
with the same two machine-feed NU1507 warnings.

This is explicit Identity CID 0 detection, not complete .notdef or
glyph-reference conformance. CIDFontType2 CIDToGIDMap/cmap lookup,
simple fonts, other encoding CMaps and undefined-character fallback
remain open. The existing bounded content walk does not establish
complete coverage of patterns, Type3 charprocs, malformed/unsupported
content or exhausted traversal bounds. Metrics and overall PDF/UA
parity remain partial; historical dated measurements are retained.

## 2026-11-04 PDF/UA-1 supplied subset font listings

PDF/UA-1 now inspects supplied CharSet and CIDSet listings for embedded
subset fonts through the existing PDF/A CFF/TrueType inspection helpers.
The requirement provenance is the published veraPDF Consortium v1.30.3
[UA-1 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-1.xml)
7.21.4.2 tests 1/2, under CC BY 4.0. This is profile cross-checking,
not independent access to the full ISO PDF/UA clause text. The parsers
are independently implemented; no external PDF, font or profile XML
is bundled.

Type1 FontFile3 Type1C/OpenType CharSet compares glyph names excluding
.notdef, accepting reordered/duplicate names and valid PDF name escapes.
CIDFontType0 raw/OpenType CFF compares actual charset CIDs, including
name-keyed CFF's sequential GIDs. CIDFontType2 raw/OpenType glyf follows
resolved Identity or supported CIDToGIDMap streams. Missing and extra
entries produce `PDFUA-FONT-CHARSET-LIST` or `PDFUA-FONT-CIDSET-LIST`
Errors; unsupported/malformed inspected metadata and bounds produce the
corresponding `-INCOMPLETE` Error. Existing decoding, parser bounds and
PDF/A behavior are retained rather than duplicated. Absent listings and
non-subset fonts are not made subject to new presence requirements.
PDF/UA-2 receives no subset-listing rule.

Twenty-four mutated real-font controls use public name-keyed CFF,
CID-keyed CFF and TrueType programs. FontTools independently enumerates
their names/CIDs/glyph counts; Python set/bitmap checks verify the saved
controls. Twenty agree with veraPDF 1.30.3 on exact rule and whole-file
verdict (12 clean positives, eight negatives). Three malformed-program
controls are explicitly incomplete in FreeDF and fail a different
embedding rule in the reference; these are not exact-rule agreements.
One disagreement is retained explicitly: the shared maxp/Identity policy
requires every glyph slot, while veraPDF accepts omission of unused empty
slot 1531 in the sparse FreeMonoBold program. FreeDF rejects it; this is
not counted as independent conformance evidence. General sparse-glyph
interpretation remains a limit, not a proven normative disagreement.

A fresh FreeDF pass over all 435 retained public files (870 UA profile
results), compared with the retained reference and prior output, closes
exactly three UA-1 file-verdict misses: both CharSet cases and the CIDSet
case. Only those three finding sets change; all UA-2 rows are unchanged.
**3 UA-1 / 2 UA-2 misses remain**, with zero reference-positive
regressions, internal errors or new subset-incomplete findings.
Eighteen of the initial 56 authored controls failed before implementation;
the final 66 include actual CID charsets, zero-valued mappings,
direct/indirect entries, optional absence and malformed programs.
The full Release suite passes 12245/12245 and the solution builds with
the same two machine-feed NU1507 warnings.

Legacy Type1 FontFile programs, unsupported font/mapping shapes, complete
font/outline validity, sparse-slot semantics, full metrics, used-glyph
validity and general PDF/UA parity remain open. The PDF/A helpers' decoded
size limits apply after existing stream decoding, not through a new
bounded streaming decompressor. Historical measurements are preserved.

## 2026-11-04 PDF/UA forbidden ToUnicode destinations

PDF/UA-1 and UA-2 now reject effective literal ToUnicode destinations
containing U+0000, U+FEFF or U+FFFE, including within multi-character
UTF-16BE strings. Source codes and comments are not mistaken for
destinations. Counted bfchar, sequential bfrange and array bfrange blocks
are inspected; later mappings replace earlier mappings of the same
source code and byte length. Sequential ranges increment only the final
destination byte, not a Unicode scalar or the whole string; overflow is
explicitly incomplete, as ISO 32000-1 9.10.3 defines it as undefined
(public edition printed page 295).

Requirement provenance is the published veraPDF Consortium v1.30.3
[UA-1 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-1.xml)
7.21.7-2 and
[UA-2 profile](https://github.com/veraPDF/veraPDF-validation-profiles/blob/c4f57effb0d3618827c87bd71882930d752be6e3/PDF_UA/PDFUA-2.xml)
8.4.5.8-2, under CC BY 4.0; this is profile cross-checking, not a claim
that the full ISO PDF/UA clause text was independently obtained.
The decoder/scanner is independently implemented. No reference-engine
source, profile XML, external font or PDF is bundled.

The check uses strict complete supported filter decoding and bounds input
to 16 MiB, tokens to one million, expanded mappings to 65536 and each
destination to 512 bytes. Malformed strings/counts/blocks/arrays, undefined
range-byte overflow, inherited mappings, unsupported procedures/operators
or filters, and limits produce `PDFUA-FONT-TOUNICODE-INCOMPLETE` Errors
instead of silent success or guessed Unicode values. This does not
validate every CMap rule, UTF-16 scalar sequence, code-space coverage or
used-glyph mapping; normal text-extraction recovery is unchanged.

Forty independent real-font controls cover direct, compressed, multi-code
unit, range, array, comment, supplementary/ligature and override cases.
An independent Python mapping/UTF-16BE check verifies all 40. Thirty-eight
agree with veraPDF 1.30.3 on exact rule and whole-file verdict: 24 formerly
missed negatives and 14 clean positives. Two unused U+0000 mappings are
deliberate disagreements: the profiles describe all specified Unicode
values, while the reference engine only checks used glyphs. FreeDF
rejects them; they are not counted as reference agreement.

A fresh FreeDF pass over all 435 retained public files (870 UA profile
results), compared with the retained reference and prior FreeDF results,
closes exactly six file-verdict misses and adds only this rule's findings:
three UA-1 and three UA-2 forbidden-value files. Remaining misses are
**6 UA-1 / 2 UA-2**, with zero reference-positive regressions, internal
errors or new incomplete results. Historical dated totals are preserved.
The initial 59 authored cases exposed 37 baseline failures; the final
74 cases include malformed decoding and bound controls. At that delivery,
the full Release suite passed 12174/12174 and the solution built.

A subsequent boundary correction preserves zero-padding of an odd final
hexadecimal nibble, reusing the normal lexer instead of treating valid
strings as incomplete. ISO 32000-1 7.3.4.3 (public printed page 16)
describes the padding; six additional real-font controls agree exactly
and whole-file with the reference and an independent Python UTF-16BE
check: four clean positives (`<004>` becomes U+0040, `<FEF>` becomes
U+FEF0) and two U+0000 negatives (`<000>`). The four former conservative
rejections are removed; the two negatives now receive the specific value
error instead of incomplete. No newly detected reference-negative file
is claimed for that correction. All 79 focused cases pass, including six
new baseline failures. The full12174 run precedes this small correction;
no repeated full-corpus or full-suite measurement is claimed.
Full font metrics,
subset/glyph validity, mapping completeness and overall parity remain open.

## 2026-11-04 Complete Flate predictor inspection

The internal A1 content scan now validates integer predictor parameters,
supported algorithms, bounded row sizes, complete TIFF/PNG frames and PNG
algorithm tags before reusing the existing predictor decoder. TIFF packed
components and PNG byte prediction retain their existing semantics;
PNG tags need not match the particular Predictor value from 10 through 15.
The normal reader's partial-row recovery is unchanged. ISO 32000-1
7.4.4.4 and Tables 8-10 (public edition printed pages 27-29) establish
these requirements. A1 still cannot establish an exemption using 16-bit
predictors: those were introduced in PDF 1.5, beyond A1's PDF 1.4 base.
This conservative limit is not a new complete PDF-version validator.

Forty retained real-font controls include 30 valid predictor encodings and
ten malformed parameter/frame controls. A separate Python sample/row
decoder, standard outer filters/zlib and PyMuPDF reproduce every byte of
all 30 valid encodings. Eighteen eligible controls agree exactly and
whole-file with veraPDF 1.30.3: nine rendered negatives/nine invisible
positives. Eight packed TIFF controls are not counted as reference
agreements: the reference logs unsupported 1/2/4-bit decoding while
reporting compliance, including for rendered invalid maps. FreeDF follows
the normative algorithm and independent byte checks instead. Four PDF
1.5-only 16-bit controls retain explicit font-usage-incomplete errors.
Thirteen eligible invisible controls lose their former conservative
incomplete rejection, including four independently decoded packed cases.

All ten malformed controls retain explicit content/font-usage Errors.
The reference accepts four, rejects four and throws on two; these are not
counted as exact agreements. Other profiles' complete findings/verdicts
are unchanged across all 40. Thirty-one initial filter cases exposed
20 baseline failures; 18 document cases cover rendering modes, chains,
errors and the A1 version limit. Existing packed/16-bit TIFF and packed
PNG fixtures now also exercise the strict decoder. The full Release
suite passes 12100/12100 and the solution builds. No external PDF/font
is bundled; no historical public-corpus totals are changed.

An investigated LZW extension was deliberately not shipped: existing
`PDFA-FILTER-LZW` enforcement already rejects all 48 direct/chained
controls because PDF/A prohibits LZW (6.1.10). Strict exemption decoding
for it would add no compliance coverage. Other unsupported execution,
complete font/glyph validity and broader PDF-spec parity remain open.

## 2026-11-04 Strict RunLength content-filter inspection

The internal A1 content-decoding path now checks complete RunLength literal
and repeated packets, the 128 EOD marker and the existing decoded-size
bound before using decoded content to establish a font-use exemption.
Maximum 128-byte packets and filter chains are supported. Normal reader
recovery remains unchanged. ISO 32000-1 7.4.5 (public edition printed page
29) defines the packet lengths and EOD; uncompleted inspections retain
explicit content/font-usage Errors, not invented glyph-use verdicts.

Eighteen retained real-font controls cover literal/repeated/maximum packets,
RunLength preceding Flate and ASCIIHex/ASCII85 preceding RunLength, each
with rendered and invisible text. All twelve valid encodings agree exactly
and whole-file with veraPDF 1.30.3 (six positives/six negatives), removing
six previous conservative incomplete positive rejections. An independent
Python packet walker, standard-library outer/inner filters and PyMuPDF
agree on every valid decoded byte. All six malformed packet/EOD controls
remain explicit incomplete errors; five reference acceptances and one
reference rejection are recorded, not counted as exact rule agreements.
Other profiles' complete findings/verdicts are unchanged across all 18.

Fifteen authored filter cases initially exposed seven baseline failures;
eight document-integration cases exercise rendered/invisible direct and
chained use. All 8672 combined profile/filter/discovery cases pass and
the solution builds. The preceding full11980 predates both strict-filter
follow-ups. No external PDF/font bundled, no new historical corpus totals.
LZW, predictors, executable-content interpretation and complete font/glyph
validity remain open.

## 2026-11-04 Strict ASCII content-filter inspection

The A1 font-use scan can now establish complete ASCIIHex/ASCII85 decoding,
including chains preceding Flate, instead of conservatively marking every
non-Flate filter incomplete. The internal strict path verifies hexadecimal
characters/odd-nibble padding, base-85 groups and partial groups, zero-group
placement, numeric overflow and complete EOD markers. PDF whitespace is
accepted; the PostScript ASCII85 opening delimiter is not PDF filter data.
The existing public decoder and its recovery behavior are unchanged.

ISO 32000-1 7.4.2 and 7.4.3 (public edition printed pages 24-25) supply the
character/group/error semantics. Missing complete markers are conservatively
incomplete inspections, not an invented PDF/A filter-specific violation.
Invalid or unterminated decoding emits `PDFA-CONTENT-SCAN-INCOMPLETE`;
an invalid-map candidate additionally retains `PDFA-FONT-USAGE-INCOMPLETE`,
without inventing a rendered-use mapping verdict from undecoded bytes.
Other profiles retain their existing decoding behavior.

Thirty-two real-font controls preserve the clean A1 metadata, embedded
program and mapping candidate. Sixteen valid direct/chained encodings
agree with veraPDF 1.30.3 on exact 6.3.3.2 and whole-file verdicts:
eight rendered negatives and eight invisible positives. The latter remove
eight old conservative incomplete rejections. Python standard-library
base85/hex/zlib decoding and PyMuPDF independently reproduce every valid
decoded byte, including whitespace, odd hex digits and zero groups.

The remaining sixteen malformed controls all emit both incomplete Errors,
not silent success. The reference accepts eleven and rejects five; these
results are retained explicitly, not counted as sixteen exact agreements.
No malformed control is excluded, and no new reference-negative closure
is claimed for an already-incomplete input. Other profiles' complete
findings and verdicts are unchanged across all 32 controls.

The initial 33 authored filter cases had 15 baseline failures; boundary
and document-integration controls add 15 more cases. All 8649 combined
PDF/A/UA/deep/filter/discovery cases pass and the solution builds.
The preceding full11980 measurement predates this slice. No external
PDF/font is bundled; full font validity and unsupported RunLength/LZW,
predictor and executable-content interpretation remain open.

## 2026-11-04 ExtGState-only shown-font discovery

The shared content walk now follows Tf and ExtGState Font under every
PDF/A/UA profile, not only A1, and collects a supplied font when a valid
text-showing operator uses it. Fonts absent from Resources Font therefore
no longer bypass existing embedding, CID mapping and encoding metadata
checks. A2/3 and UA1/2 include mode-3 text; A1's mode-3 exemption and
existing resource-font policy are unchanged. An unused graphics-state
definition, or selecting it without showing text, does not discover a
new font. No duplicate font checks or new metadata-rule semantics are added.

The same q/Q restoration and inherited/local Form font state are reused.
Other profiles retain their existing resource/report-page policy rather
than adopting A1's rendering-page label. Malformed state interpretation
and complete font traversal/usage remain broader limits, not new claims
of glyph or full program validity.

Thirty-two clean real-font controls cover selected/unselected/no-text
versus visible/invisible use. Embedded A2/3 CIDFontType2 controls exercise
Identity versus Other; tagged UA1/standalone-UA2 simple-TrueType controls
exercise Unicode versus Roman-only cmap with Differences. All agree with
veraPDF 1.30.3 on exact rules and whole-file verdicts: 24 clean positives
and eight newly rejected negatives, with no collateral failures/exclusions.
These reuse the retained program and cmap variants; no external fonts/PDFs
are bundled.

The initial 150 authored profile/state/embedding/map cases had 60 baseline
failures. Thirty further q/Q and inherited/local Form cases pass, including
mode-3 use and the same Form under different inherited fonts. The full
Release suite passes 11980 cases, zero failed/skipped; the solution builds.
Fresh pre/post execution of all 435 public UA files (870 profile results)
has zero changed verdicts or findings, no reference-positive regressions
and no internal/incomplete-cmap errors. Historical corpus totals and broad
Partial status are unchanged.

## 2026-11-04 PDF/A-1 rendering-aware CID mapping entries

Embedded CIDFontType2 fonts used for rendering now require a resolved
CIDToGIDMap stream or Identity name under ISO 19005-1 6.3.3.2, not the
previous 6.3.5 citation. Direct/indirect null, other names, wrong kinds and
cyclic references no longer satisfy the entry requirement. The mode-3
exemption is preserved: resource presence or Tf alone does not establish
rendered use. Every valid text-showing operator counts, including empty
strings/arrays; mode 7 is not exempt. A2/3 still require the entry even
for mode-3-only text. This does not change embedding-policy exemptions.

The existing bounded content scanner tracks Tf and ExtGState Font,
q/Q restoration, Form-local and inherited font/rendering state, annotation
appearances and shared fonts across pages. Form memoization includes the
inherited font, and a later rendered use cannot be hidden by an earlier
invisible use. Findings identify the first observed rendering page.
Resource-based font discovery remains in place for other checks.

The ordinary decoder intentionally recovers partial Flate output, which
cannot prove the exemption. An internal, A1-only path verifies the zlib
header and Adler-32 checksum without changing the existing public decoder
or normal reader recovery. Decode failures emit an explicit content-scan
Error, not only an internal Warning. Invalid mapping candidates whose
exemption cannot be established receive `PDFA-FONT-USAGE-INCOMPLETE`.
Malformed/limited scans, unresolved font state, Form cycles/depth limits,
heuristic inline-image skipping, selected tiling patterns, rendered Type3
procedures, non-Flate filters, ignored/unsupported DecodeParms shapes and
predictors other than 1 do not silently
establish that exemption. Complete interpretation of these deferred
surfaces is not claimed; already valid mapping entries need no exemption.

The pinned veraPDF 1.30.3 PDF/A-1 profile describes the rendering requirement
and mode-3 exemption in clause 6.3.3.2 test 1. Independent controls preserve
the clean A1 metadata, embedded Liberation Sans program, complete CIDSet,
actual CID 36 widths and device-independent colour. Twenty-six entry-kind/
mode controls agree on exact rules and whole-file verdicts. Twenty-five
additional text-state/Form/appearance controls give 22 further agreements.
Together these are 24 clean agreed positives and 24 agreed negatives,
including 23 reference negatives newly rejected and one old false positive
removed. Other profiles' finding IDs/clauses and verdicts are unchanged.

Three reference disagreements remain explicit, with no collateral failures
or excluded controls: the reference accepts a font first shown invisibly
then rendered after Tr or Q, and the same Form invoked under two different
inherited fonts. FreeDF rejects the later rendered use of the invalid map,
as required by the rendering condition; reference acceptance is not treated
as evidence that such a use is exempt.

The initial 144 authored cases had 34 baseline failures. Follow-up decode,
empty-text, shared-page and unsupported-surface controls bring the total
to 167. The complete Release suite passes 11800 cases, zero failed/skipped;
the solution builds with existing machine source warnings unchanged.
No external fonts/PDFs are bundled. These controls reuse one real program,
not 51 independent fonts. Full glyph-map/width/Unicode validity and complete
font/outline validity remain open; historical corpus totals are unchanged.

## 2026-11-04 Differences Unicode cmap prerequisite

Embedded non-symbolic simple-TrueType fonts defining a Differences array
now also require Microsoft Unicode (3,1) cmap encoding under PDF/A-2/3
6.2.11.6, PDF/UA-1 7.21.6 and PDF/UA-2 8.4.5.7. Empty and indirect arrays
are included; explicit null is absent. Existing AGL-name validation and
A1's Differences prohibition remain unchanged. The shared raw/OpenType
metadata pass is reused, not decoded twice. Missing (3,1) emits
`PDFA-FONT-DIFFERENCES-CMAP` or `PDFUA-FONT-DIFFERENCES-CMAP`; malformed
selected metadata retains its incomplete error without an invented
missing-record verdict. Unembedded programs do not invent cmap findings.

The independent control design avoids the prior symbolic-to-WinAnsi
used-glyph/width side effects: FontTools adds actual Roman/Unicode cmap
tables to the retained Liberation Sans glyf program, explicitly mapping
A to independently retained GID 36 and preserving its exact scaled width.
Three cmap variants (Roman-only, Unicode-only, both), raw/OpenType
placement and six direct/indirect/null/absent array variants produce
72 clean A2/3 controls. Matching 72 UA1/standalone-UA2 controls preserve
the source tag tree and MCID while painting that same glyph.
All 144 agree with veraPDF 1.30.3 on exact test 2 and whole-file verdicts:
112 clean positives and 32 new negatives. No unrelated reference rule
fails, no controls are excluded, and no external fonts/PDFs are bundled.
These are three derived variants of one retained font, not 144 programs.

There are 162 authored cases, 32 failing before implementation, covering
profile/wrapper/array/record combinations, unembedded and malformed scope
and A1's unchanged ban. All 8523 combined PDF/A/UA/deep/reader/cmap cases
pass; the solution builds with existing machine source warnings unchanged.
Fresh execution of all 435 public UA files (870 profile results) against
the immediately preceding runtime shows zero changed verdicts, no
reference-positive regressions and no incomplete-header/internal errors.
The preceding full11344 measurement predates the last three cmap follow-ups.

Required record presence and AGL names still do not establish all glyph
lookups, mapping correctness, width agreement or valid subtables/programs.
Complete font/outline validity, legacy Type1 and used-glyph/width/Unicode
completeness remain open; no new decode-byte cap is implied. Broad Partial
status and historical corpus measurements are unchanged.

## 2026-11-04 Non-symbolic TrueType cmap records

Embedded non-symbolic simple-TrueType fonts now receive bounded cmap
record inspection for raw FontFile2 and FontFile3 OpenType programs.
PDF/A-2/3 (6.2.11.6) and PDF/UA-1 (7.21.6) require at least one
non-symbolic entry; PDF/UA-2 (8.4.5.7) specifically requires Microsoft
Unicode (3,1) or Macintosh Roman (1,0). PDF/A-1 has no new non-symbolic
record predicate. Existing symbolic policy, raw-program priority,
unknown FontFile3 exclusion and stream-filter errors are preserved.
The shared reader now reports typed Count/Symbol/Roman/Unicode/NonSymbolic
metadata; malformed selected headers produce existing incomplete Errors.
This intentionally changes the prior exclusion of embedded non-symbolic
programs from cmap inspection, not its glyph-level completeness.

Thirty-two retained real-font mutations cover eight record combinations,
raw/OpenType placement and both source UA parts. FontTools independently
reads the intended records. All 64 external A2/A3/UA1/standalone-UA2
profile/control pairs agree on exact test 1: 46 record positives and
18 newly detected negatives. Changing symbolic fonts to WinAnsi non-symbolic
introduces independent used-glyph, width and Unicode failures; every raw
reference failure is retained, and none of these mutations is claimed
as a new whole-file corpus closure. No controls are excluded and no
external fonts/PDFs are bundled.

There are 82 authored cases, 26 failing before implementation, covering
profile/record/wrapper combinations, truncation/filter incompleteness,
unembedded scope and unchanged A1 behavior. All 8361 combined
PDF/A/UA/deep/reader/cmap cases pass; the solution builds with existing
machine source warnings unchanged. A fresh exact pre-change and post-change
FreeDF comparison of all 435 public UA files (870 profile results) shows
zero changed file verdicts, no reference-positive regressions and no
incomplete-header/internal errors. Retained symbolic OpenType controls
remain 20/20 exact-rule agreements. The preceding full11344 measurement
predates this and the OpenType selection follow-up.

Record presence is only a necessary condition, not proof of every needed
glyph lookup or a valid subtable. Differences-dependent (3,1) requirements,
full subtable/program/outline validity, legacy Type1 and used-glyph/width/
Unicode completeness remain open. No new decode-byte cap is implied;
broader Partial status and historical corpus measurements are unchanged.

## 2026-11-04 OpenType symbolic cmap inspection

PDF/A-2/3 and PDF/UA-1/2 symbolic simple-TrueType checks now inspect
FontFile3 OpenType programs, not just FontFile2. The same selection
helper serves the preceding glyf CIDSet path, retaining raw FontFile2
priority and requiring the resolved OpenType subtype for FontFile3.
Malformed selected cmap/SFNT metadata or unsupported stream filters
produce the existing profile-specific incomplete errors; unknown
FontFile3 subtypes and unembedded/non-symbolic fonts remain outside
the cmap check. Record requirements are unchanged: A2/3 and UA1
require one encoding or Microsoft Symbol; UA2 requires Microsoft Symbol
or Macintosh Roman. This does not establish glyph mapping, used-glyph
availability, complete subtable validity or full OpenType conformance.
The existing cmap path has no new decoded-program byte cap.

Ten retained real-font symbolic cmap controls are relabeled from
FontFile2 to FontFile3 OpenType without changing embedded bytes.
FontTools independently reads their intended encoding records.
Twenty external profile/control pairs (A2, A3, UA1 and standalone UA2)
agree exactly with veraPDF 1.30.3: 12 record positives and eight newly
detected record negatives. The PDF/A controls are UA-derived and are
not clean PDF/A positives or new whole-file corpus closures.
Some UA controls independently fail used-glyph availability; every raw
reference failure is retained, not relabeled as symbolic-cmap failure.
Five UA controls are whole-file reference positives. No external
font/PDF is bundled and no controls are excluded.

The 44 authored profile cases cover encoding combinations, missing and
malformed headers, every wrapper truncation, filters and excluded scope;
21 failed before implementation. An additional shared-selection case
pins raw priority, null-as-absent handling and the required OpenType
subtype. All 8279 combined PDF/A/UA/deep/reader/OpenType cases pass and
the solution builds with existing machine source warnings unchanged.
Independent glyf CIDSet outcomes remain 126/136 with their same ten
mandatory-CID0 disagreements. The preceding full11344 measurement
predates this small selection follow-up. Complete font/program/outline
validity, legacy Type1 and used-glyph/width/Unicode completeness remain
open; broad Partial status and historical corpus measurements are unchanged.

## 2026-11-04 PDF/A OpenType glyf CIDSet completeness

The subset CIDFontType2 CIDSet path now also selects FontFile3 OpenType
programs when FontFile2 is absent. It reuses the bounded TrueType maxp
reader and existing Identity/stream CIDToGIDMap comparison without
changing raw FontFile2 selection. ISO 32000-1 Table 126 and 9.9 permit
OpenType TrueType outlines for CIDFontType2. CFF/OTTO programs cannot
pass the TrueType metadata reader. Existing explicit incomplete results,
16 MiB decoded-program, 1 MiB bitmap and 131072-byte map bounds remain.
This only establishes selected metadata, not all required-table,
outline or checksum validity.

Independent A2/3 controls reuse one retained real Liberation Sans glyf
program under FontFile3 OpenType: 40 Identity and 96 explicit stream-map
controls (aliases, unused-GID permutations and short maps included).
FontTools confirms 2589 glyphs and the required glyf/head/hhea/hmtx/loca/maxp
tables; independent Python set comparison confirms every intended bitmap.
All 44 reference negatives now fail their exact 6.2.11.4.2 test 2 predicate.
Exact-rule/whole-file agreement with veraPDF 1.30.3 is 126/136.
The ten normative disagreements all omit mandatory CID 0 and are
reference-positive but FreeDF-negative. No collateral reference rule fails,
no controls are excluded, and no external fonts/PDFs are bundled.

There are 184 new authored cases (76 failed before implementation),
covering both map kinds, subset/plain scope, indirect Identity, malformed
maxp/CFF metadata, invalid map bounds/GIDs and exact program-byte bounds.
Minimal fixtures isolate metadata and do not claim full OpenType validity.
Full Release verification of all integrated font slices passes 11344 tests,
zero failed/skipped; the solution builds with existing machine source
warnings unchanged. Independent OpenType CFF CIDSet (72/80) and CharSet
(22/24) outcomes retain exactly their documented disagreements.
OpenType simple-TrueType encoding inspection, full wrapper/program
validity, legacy Type1 and used-glyph/width/Unicode completeness remain
open; broad Partial status and historical corpus measurements are unchanged.

## 2026-11-04 PDF/A OpenType CFF CIDSet completeness

The CIDFontType0 subset CIDSet path now follows FontFile3 OpenType
wrappers through the same bounded CFF extraction as Type1 CharSet.
CID-keyed CFF selects actual charset CIDs; name-keyed CFF validates its
charset names and selects sequential GIDs as CIDs. ISO 32000-1
9.7.4.2 and Table 126/9.9 define this selection. The complete decoded
wrapper retains the 16777216-byte program limit; malformed wrapper or
CFF metadata yields `PDFA-FONT-CIDSET-INCOMPLETE`. Bitmap limits and
exact all-defined-CID comparison, including CID 0, remain unchanged.

Eighty A2/3 controls wrap two retained real raw CFF programs with
FontTools-generated OTTO directories and cmap tables: Kozuka Mincho
CID-keyed and Adobe Naskh name-keyed. Source widths are preserved and
the name-keyed control paints the independently verified C GID 3.
FontTools independently agrees on exact selected CFF bytes and ordered
CIDs for both programs. No unrelated reference rule fails.
Exact-rule and whole-file agreement is 72/80; 28 of 32 reference negatives
are rejected. Eight explicit normative disagreements comprise two
CID-keyed omitted-CID0 lists, two name-keyed empty lists, and four
complete/zero-padded name-keyed lists rejected by the reference.
FreeDF accepts those four complete-list positives. No controls are
excluded or relabelled; source fonts/PDFs are not bundled.

The 172 authored cases cover keyed/name-keyed selection, charset formats
0/1/2, subset/plain scope, malformed selected wrapper/CFF metadata and
exact whole-wrapper byte limits; 63 failed before implementation.
Their minimal wrapper fixtures isolate metadata, not full font validity.
All 8050 combined PDF/A/UA/deep/reader cases pass and the solution
builds with existing machine source warnings unchanged. Independent
OpenType CharSet outcomes remain 22/24 with the same two explicit
disagreements. The latest full10914 predates these wrapper slices.
OpenType glyf CIDSet support, full required-table/checksum/program
validity, legacy Type1 and used-glyph/width/Unicode completeness remain
open; broad Partial status and historical corpus measurements are unchanged.

## 2026-11-04 PDF/A OpenType CFF CharSet completeness

The Type1 subset CharSet check now follows FontFile3 OpenType wrappers
to their CFF table and reuses the raw name-keyed glyph-list comparison.
The existing bounded SFNT directory reader is shared while preserving
the TrueType signature policy of cmap/maxp checks. OpenType CFF extraction
requires the OTTO signature, a unique nonempty CFF table and in-file
table bounds; missing, duplicate or malformed selected tables produce
`PDFA-FONT-CHARSET-INCOMPLETE`, not an omitted successful check.
This does not validate all required OpenType tables or SFNT checksums.
ISO 32000-1 Table 126 and 9.9 (PDF pages 298/299) define these wrappers
and Type1 CFF glyph selection. The complete decoded wrapper, not just
its selected table, remains subject to the 16777216-byte program limit.

Twenty-four independent A2/3 controls reuse the retained real Myriad Pro
OpenType program, with subset/plain names and six list mutations.
FontTools 4.68.0 independently agrees on the extracted CFF table bytes
and all 852 ordered names. All eight reference-negative controls now
fail their exact 6.2.11.4.2 test 1 predicate. Of the 24 controls, 22 agree
with veraPDF 1.30.3 on exact rules and whole-file verdicts; two explicit
normative disagreements retain the prohibition on listing `.notdef`.
No controls are excluded. This is one reused real font, not 24 programs;
no external PDF or font is bundled.

The 68 authored cases cover subset/plain, indirect strings, PDF escapes,
malformed/truncated/missing/duplicate table metadata and exact decoded
wrapper limits; 23 failed on the preceding implementation. Their minimal
table fixtures isolate selected metadata and are not claimed as complete
OpenType fonts. All 7878 combined PDF/A, PDF/UA, deep-validation and reader
cases pass; the solution builds with existing machine feed warnings
unchanged. Prior TrueType CIDSet controls retain their measured outcomes.
The latest full 10914-case measurement predates this and the name-keyed
CID selection follow-up. OpenType CIDSet wrappers, full wrapper/program
validity, legacy Type1 and used-glyph/width/Unicode completeness remain
open. Historical corpus measurements and broad Partial status are unchanged.

## 2026-11-04 PDF/A name-keyed CFF CIDSet selection

The raw CFF CIDSet metadata path now also handles a Top DICT without
CIDFont operators: CIDs are sequential GIDs, not charset string IDs.
ISO 32000-1 9.7.4.2 (PDF page 278) explicitly distinguishes this case
from ROS-bearing CID-keyed charsets. SID references are still checked;
malformed names do not become successful metadata inspection.

The earlier private name-keyed transplant painted GID 2 (slash) using
the width of C. FontTools independently establishes C at GID 3.
Sixty corrected controls paint that GID with its source width and have
no unrelated reference rule failures. The original 60 width-failing
controls remain separate, not retrospectively relabeled as clean.
The corrected set uses one retained Adobe Naskh program, not 60 fonts.
Core's exact GID/CID sequence agrees with the independently read
FontTools glyph order.

Of the corrected controls, 46 agree with veraPDF 1.30.3 on exact rules
and whole-file verdicts. Fourteen normative disagreements remain:
the reference accepts all eight malformed A1 subset lists and empty
A2/3 subset lists, while rejecting the complete and zero-padded complete
A2/3 lists. FreeDF follows the explicit GID selection and complete-defined-CID
requirements: it rejects those ten incomplete/extra lists and accepts
the four complete-list positives. The other 14 reference-negative
controls fail the shared exact predicate in both engines.
No corrected controls are excluded.

Six authored tests, all failing on the preceding implementation, cover
custom formats and 126 predefined-charset/profile/subset/list combinations.
All 7810 combined PDF/A, PDF/UA, deep-validation and reader cases pass;
the solution builds with existing machine feed warnings unchanged.
The full 10914-case measurement predates this small selection extension.
Existing raw CID-keyed controls remain at 52/60 agreement with eight
explicit normative disagreements. OpenType wrappers, complete font/outline
validity and used-glyph/width/Unicode completeness remain open; historical
corpus measurements and broad Partial status remain unchanged.

## 2026-11-04 PDF/A raw CID-keyed CFF CIDSet completeness

Embedded subset CIDFontType0 fonts with a raw CIDFontType0C program and
CIDSet stream now compare the exact CIDs in the program's charset.
The shared bounded Core reader distinguishes ROS-bearing CID charsets
from glyph-name SIDs, supports custom formats 0/1/2, validates ROS string
references and rejects duplicate CIDs or predefined CID charsets.
The glyph-name API continues rejecting CID-keyed programs. Neither API
interprets charstrings or establishes complete CFF/outline validity.
The existing high-bit-first CIDSet comparison is shared with the
TrueType paths, preserving their controls, limits and findings.

ISO 32000-1 9.7.4.2 (PDF page 278) explicitly selects a CID-keyed CFF's
GID through its charset; GIDs must not be mistaken for CIDs.
The same clause requires CID 0, and Table 124 defines CIDSet bit order.
The pinned profile commit `c4f57effb0d3618827c87bd71882930d752be6e3`
supplies A1 6.3.5 test 3 and A2/3 6.2.11.4.2 test 2.
Sixty clean listing controls reuse the retained positive Kozuka Mincho
CID-keyed program with sparse CIDs, subset/plain names and ten bitmap
variants. All 16 reference-negative controls now fail their exact rules.
Of the 60 controls, 52 agree on exact rules and whole-file verdicts with
veraPDF 1.30.3. Eight explicit disagreements retain the complete-defined-CID
policy: A1 accepts some unused/extra-bit lists, and all three parts accept
omitted CID 0. No clean keyed controls are excluded.

FontTools 4.68.0 independently agrees on the exact ordered CIDs for five
distinct retained raw CID-keyed programs, not just the one reused by
these mutations. A separate Python set comparison checks every clean
bitmap control. A name-keyed Type1C-to-CIDFontType0C transplant experiment
has unrelated width failures and unexpected reference CID-list behavior;
its 60 PDFs and reports remain separate, not claimed as clean positive
evidence. Name-keyed inspection in this CIDFontType0C path explicitly
reports incomplete rather than inferring a CID set. External fonts/PDFs
remain private artifacts and are not bundled.

The 133 authored validator cases and four added reader cases cover sparse
CID 65535, all custom formats, unused/missing/extra bits, duplicate CID/ROS
metadata, every truncation, unsupported name-keyed data and exact decoded
font/bitmap byte boundaries. Fifty validator cases failed before wiring
the new check. All 10914 full Release tests pass, zero failed/skipped;
the solution builds with existing machine feed warnings unchanged.
Prior Identity/kind and stream-map reference outcomes remain unchanged.
Limits apply after existing stream decoding. Name-keyed CFF CIDFonts,
OpenType wrappers, complete program/outline validity and used-glyph,
width and Unicode completeness remain open. Historical corpus measurements
and broad Partial status remain unchanged.

## 2026-11-04 PDF/A stream-mapped TrueType CIDSet completeness

The preceding raw TrueType CIDSet check now also handles resolved
CIDToGIDMap streams, not just Identity names. Table 117 in
[ISO 32000-1 9.7.4.2](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf)
defines each two-byte entry as an explicit CID-to-GID assignment.
Every in-range entry defines a CID, including assignments to GID 0;
zero entries are not inferred holes. Unmapped program GIDs are not
silently treated as CID values. The separate mandatory CID 0 requirement
is retained, including empty maps. Odd-length maps, more than 131072
decoded map bytes, or GIDs outside the inspected maxp range produce
`PDFA-FONT-CIDSET-INCOMPLETE` Errors. These checks occur only within the
existing subset/embedded raw FontFile2/CIDSet-stream scope, not as a claim
of exhaustive standalone CIDToGIDMap validation.

The 144 corrected independent controls reuse the retained Liberation Sans
program with identity streams, nonzero aliases, unused-GID permutations
and short maps, varying subset/plain names and six bitmap layouts across
all three parts. All 38 reference-negative controls now fail their exact
mapped rules. Of these, 125 agree with veraPDF 1.30.3 on exact rules and
whole-file verdicts; 19 normative disagreements remain explicit:
12 omit CID 0, and seven A1 cases use extra bits or GID-indexed lists in
place of the full assigned CID set. No controls are excluded.
FontTools confirms the reused program's 2589 glyphs and an independent
Python CID-set comparison checks every corrected control.

The initial sparse-map experiment incorrectly excluded explicit GID 0
entries, so several supposed full-list positives failed A2/3. Its 144
PDFs and reports remain separate, not claimed as clean positive evidence.
Table 117 and inspection of the shipped 1.30.3
`CIDToGIDMapping.contains` / `CIDFontType2Program.containsCID` logic
confirmed that explicit zero entries count before the corrected control
set and implementation were selected. No reference implementation code
was copied. Both experiment stages and source/hash evidence remain
private session artifacts; no external font or PDF is bundled.

The 159 authored stream-map cases cover alias/zero assignments, CID/GID
distinctions, empty maps, invalid GID ranges and exact 131072-byte bounds;
69 cases failed in the initial baseline. Existing Identity/kind controls
retain their previous 112/120 agreement and eight explicit disagreements.
All 7656 combined PDF/A, PDF/UA and deep-validation tests pass, and the
solution builds with existing machine feed warnings unchanged.
The full 10480-case measurement predates both CIDSet completeness slices.
All byte limits apply after existing stream decoding.
CID-keyed CFF, OpenType wrappers, complete font/outline validity and
used-glyph/width/Unicode completeness remain open. Historical corpus
measurements and broad Partial status remain unchanged.

## 2026-11-04 PDF/A Identity CIDSet completeness

Embedded subset CIDFontType2 fonts with an explicit resolved Identity
CIDToGIDMap, raw FontFile2 program and CIDSet stream now compare the exact
defined CID set against the TrueType maxp glyph count. Missing CIDs,
including unused glyphs and CID 0, and extra set bits produce
`PDFA-FONT-CIDSET-LIST`. Zero trailing bytes are permitted.
Malformed/unsupported SFNT/maxp metadata or exhausted resource bounds
produce `PDFA-FONT-CIDSET-INCOMPLETE` Errors. The existing A1 stream-presence
rule is unchanged. The table-directory bounds checks are shared with the
existing cmap inspection, retaining its missing/invalid-table distinction.

The pinned profile commit `c4f57effb0d3618827c87bd71882930d752be6e3`
supplies A1 6.3.5 test 3 and A2/3 6.2.11.4.2 test 2.
[ISO 32000-1](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf)
9.7.4.2 and Table 117 define Identity CID/GID mapping; the same clause
requires every CIDFont to contain a CID 0 description (PDF pages 278/279).
Table 124 in 9.8.3.1 defines CIDSet as high-bit-first CID-indexed data
(PDF page 293). No arbitrary CID mapping or painted-glyph inference is used.

Sixty new bitmap controls reuse the retained public Liberation Sans
CIDFontType2 program and paint CID 36 in calibrated-RGB documents.
FontTools independently confirms 2589 program glyphs; a separate Python
set comparison verifies every listing control against their Identity-mapped
CIDs. All 16 reference-negative listing controls now fail their exact
mapped rules. Of the 60 controls, 52 agree on exact rules and whole-file
verdicts with veraPDF 1.30.3. Eight explicit normative disagreements remain:
the reference accepts A1 omitted unused CIDs, only-used lists and extra
bits, and omission of CID 0 in all three parts; FreeDF requires the complete defined CID set,
including the required CID 0. No controls are excluded.
All 60 retained kind controls now agree, closing their three remaining
empty-stream misses. This reuses one font program, not 120 independent
programs. Raw reports, hashes and before/after results remain session
artifacts; no external PDF or font is bundled.

The 139 authored metadata cases include direct/indirect Identity names,
byte and maximum glyph-count boundaries, missing/duplicate/truncated
SFNT/maxp metadata, zero padding, explicit scope exclusions and exact
resource bounds; 55 failed on the preceding implementation. These
self-authored maxp-only fixtures isolate metadata, not complete font
validity. All 7498 combined PDF/A, PDF/UA and deep-validation cases pass,
and the solution builds with existing machine feed warnings unchanged.
The latest full 10480-case measurement predates this slice.
Decoded font and CIDSet limits are 16777216 and 1048576 bytes respectively,
both checked after existing stream decoding, not by a new streaming
decompressor. Non-Identity mapping streams, CID-keyed CFF, OpenType
wrappers, complete font/outline validity and used-glyph/width/Unicode
completeness remain open. Historical corpus measurements and broad
Partial status remain unchanged.

## 2026-11-04 PDF/A Type1C CharSet completeness

Embedded Type1 subsets with a string CharSet and raw FontFile3 Type1C
program now compare the complete defined glyph-name set, not just painted
glyphs. `PDFA-FONT-CHARSET-LIST` rejects missing or extra names, invalid PDF
name syntax and inclusion of `.notdef`. Order and duplicate listing entries
do not change set membership; whitespace, comments and valid `#` escapes
are supported. The existing A1 string-presence check remains separate.
The pinned profile commit `c4f57effb0d3618827c87bd71882930d752be6e3`
supplies A1 6.3.5 test 2 and A2/3 6.2.11.4.2 test 1.
[ISO 32000-1 9.8.1](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf),
PDF page 291, requires names in PDF syntax, permits any order, and requires
omitting `.notdef` from the listing.

A bounded Core metadata reader supports CFF1 name-keyed predefined
ISOAdobe/Expert/ExpertSubset charsets and custom formats 0/1/2, standard and
custom SIDs, without executing charstrings. The existing internal standard
tables moved from Content to Core with no data changes; Content and Document
share them without a circular dependency or public API addition.
Malformed/unsupported metadata or exhausted resource bounds produce
`PDFA-FONT-CHARSET-INCOMPLETE` Errors, never a successful omitted check.
Limits are 1048576 CharSet bytes, 65535 parsed name entries including
duplicates, and 16777216 decoded font bytes. The font-byte limit applies
after the existing stream decode; this is not a new bounded streaming
decompressor.

Sixty listing mutations and the retained 66 kind controls reuse the public
positive Adobe Naskh Type1C program in calibrated-RGB documents.
All 66 kind controls now agree on exact rules and whole-file verdicts,
closing their three remaining empty-string misses. Of the 60 listing
controls, 55 agree, including all ten reference negatives. Five explicit
normative disagreements remain: veraPDF 1.30.3 accepts missing/extra names
in the A1 controls and inclusion of `.notdef` in all three parts; FreeDF
rejects them under the complete-list/PDF-syntax requirements above.
Contrary to an initial expectation, A2/3 missing/extra controls are already
reference-negative. No controls are excluded. FontTools 4.68.0 independently
agrees on the exact ordered program names across 136 font references,
representing five distinct raw Type1C programs, including repository CFF
fixtures. These are not 136 independent font programs. Per-file hashes,
raw reference reports, before/after results and independent name comparisons
remain session artifacts; no external PDF or font is bundled.

The 188 authored validator cases and 11 reader cases cover listing scope,
PDF escapes, exact resource boundaries, custom/predefined charsets, INDEX
offsets, truncated data, undefined SIDs and explicit unsupported CID metadata.
Of the initial 185 listing cases, 58 failed before implementation.
All 10480 full Release tests pass, with zero failed/skipped; the solution
builds with the existing machine feed warnings unchanged.
Legacy FontFile Type1, OpenType wrappers, CID-keyed programs, CIDSet
bitmaps, complete CFF/charstring validity and used-glyph/width/Unicode
completeness remain open. Historical corpus measurements and broad Partial
status are unchanged.

## 2026-11-04 PDF/A-1 null Differences entries

The non-symbolic TrueType encoding check now resolves Differences before
applying the PDF/A-1 no-Differences restriction. Absent and direct/indirect
null are treated as omitted under ISO 32000-1 7.3.9. Required WinAnsiEncoding
or MacRomanEncoding remains enforced, and non-null arrays, including
empty arrays, remain prohibited by ISO 19005-1 6.3.7 test 1 in pinned
profile commit `c4f57effb0d3618827c87bd71882930d752be6e3`.
Parts 2/3 are unchanged.

Thirty independent controls use a locally installed Arial TrueType program,
paint an A with its measured advance, and vary both permitted base
encodings with absent/null/indirect-null and two non-null array values.
Of these, 28 exact predicates and whole-file verdicts agree with
veraPDF 1.30.3. Two direct-null false rejections are corrected.
Two A1 indirect-null verdict disagreements are explicit: FreeDF accepts
resolved null as omission while veraPDF rejects. No controls are excluded.
The installed font is used only in private session artifacts and is not
bundled or published. Earlier barcode/Liberation mutation attempts had
unrelated used-glyph/width failures; no-text controls exercised no reference
font predicate at all. All attempts remain separate artifacts, not
claimed as clean evidence.

The 90 authored cases include direct/indirect encoding dictionaries and an
unsupported base encoding; eight failed before this correction.
All 6368 integrated PDF/A/deep tests pass and the solution builds with
existing machine source warnings unchanged. The full 9951 measurement
predates this and both latest subset-entry prerequisites. General used-glyph,
width, font-program and mapping completeness remain open. Historical corpus
figures and broad Partial status remain unchanged.

## 2026-11-04 PDF/A-1 CharSet entry kinds

The existing `PDFA1-FONT-CHARSET` presence check now requires a resolved
string for embedded Type1 subsets instead of raw key presence.
Null, indirect null, number, name, array, dictionary and stream entries
no longer satisfy it. The rule ID, subset/embedding scope and
ISO 19005-1 6.3.5 reference remain unchanged; parts 2/3 are unchanged.
Pinned profile commit `c4f57effb0d3618827c87bd71882930d752be6e3`
supplies A1 test 2. This is its string-presence prerequisite, not its
complete glyph-list predicate.

Sixty-six independent controls transplant the retained public UA corpus's
positive Adobe Naskh Type1C font into calibrated-RGB documents, varying
subset/plain names and eleven CharSet kinds. Complete direct/indirect
strings remain reference-positive; empty strings deliberately expose
the still-open listing requirement. Of these controls, 63 exact predicates
and whole-file verdicts agree with veraPDF 1.30.3, closing seven A1
wrong-kind misses. All three empty-string subset listing misses remain
explicitly reference-negative/FreeDF-positive, one per part; none are
excluded. Reports, source provenance, hashes and before/after results
remain session artifacts; no font program or external PDF is bundled.

The 132 authored metadata cases cover subset/plain, embedded/unembedded
and all three parts; seven failed before this correction.
All 6278 integrated PDF/A/deep tests pass and the solution builds with
existing machine feed warnings unchanged. The full 9951 measurement
predates both latest subset-entry corrections. CharSet name parsing,
embedded-program/listing completeness, CIDSet bitmap/program checks
and complete font validity remain open. Historical corpus figures and
broad Partial status remain unchanged.

## 2026-11-04 PDF/A-1 CIDSet entry kinds

The existing `PDFA1-FONT-CIDSET` presence check now requires a resolved
stream for embedded subset CIDFonts, not merely a raw non-null dictionary
entry. Null, indirect null, number, name, string, array and dictionary
entries no longer satisfy it. The rule ID, subset/embedding scope and
ISO 19005-1 6.3.5 reference remain unchanged; parts 2/3 are unchanged.
Pinned profile commit `c4f57effb0d3618827c87bd71882930d752be6e3`
supplies A1 test 3. This implements its stream-presence prerequisite,
not the complete glyph-list predicate.

Sixty independent controls use the retained public UA corpus's embedded
Liberation Sans CIDFontType2 program (2589 glyphs), subset/plain PDF font
names and ten CIDSet kinds. A maxp-sized all-glyph bitmap supplies a
reference-positive stream; an empty stream deliberately exposes the
remaining listing gap. Across all controls, 57 exact predicates and
whole-file verdicts agree with veraPDF 1.30.3. Seven previous A1
wrong-kind misses are closed. All three empty-stream subset misses
(one per part) remain explicitly reference-negative/FreeDF-positive;
no controls are excluded. The initial DeviceGray attempt had a separate
missing-output-intent failure and is retained separately, not called
clean evidence. Corrected calibrated-RGB controls isolate the font rule.
Reports, font-source provenance, hashes and before/after results remain
session artifacts; no font program or external PDF is bundled.

The 108 authored cases cover subset/plain, embedded/unembedded and all
three parts; seven failed before this correction. All 6146 integrated
PDF/A/deep tests pass and the solution builds with existing machine feed
warnings unchanged. The full 9951 measurement predates this correction.
CIDSet bit decoding, font-program/CID mapping completeness, CharSet
string kinds/listing and general font validity remain open.
Historical corpus figures and broad Partial status remain unchanged.

## 2026-11-04 PDF/A known-prohibited action context correction

The legacy `PDFA-ACTION` check now shares the bounded inert action
report and its established event/root filters with subtype and Named
checks, instead of scanning unrelated dictionaries. The same known
prohibited set, part-specific GoToE policy, optional Type filter,
clauses and single legacy finding are preserved. Actual disallowed
actions still also receive `PDFA-ACTION-TYPE`. Action traversal or
finding exhaustion remains an explicit incomplete Error, not a pass.
The pinned rule is ISO 19005-1 6.6.1 test 1 or ISO 19005-2/3 6.5.1
test 1 at profile commit `c4f57effb0d3618827c87bd71882930d752be6e3`.

All 63 independent controls agree with veraPDF 1.30.3 on the exact
rule and whole-file verdict: 42 positives and 21 negatives.
Launch, Hide and GoToE are tested in actual OpenAction, Link and Next
contexts versus unused, extension, unknown-AA-event and array-root
dictionaries. This corrects 28 prior false rejections without losing
any reference negative in these controls. The 84 Named controls were
also rerun unchanged and still agree. Reports, before/after results
and per-file hashes remain session artifacts.
The 252 authored cases cover all 12 legacy known kinds in all seven
contexts and three parts; 136 failed before this correction.
The full Release suite now passes 9951 tests, zero failed/skipped;
the solution builds with existing machine feed warnings unchanged.

This closes the demonstrated action-context false positives, not
exhaustive action-source discovery, unknown event/root-kind validity,
required parameters, destination validity or dispatch semantics.
No actions or external files are executed. Historical corpus counts
and broad Partial status remain unchanged.

## 2026-11-04 PDF/A named-action context correction

`PDFA-ACTION-NAMED` now checks the existing bounded inert action report
instead of every dictionary whose S entry happens to be Named. Actual
catalog OpenAction, Link actions and Next chains still require N to be
NextPage, PrevPage, FirstPage or LastPage. The rule ID, clauses and
single named-action finding behavior are retained. The pinned profiles
at commit `c4f57effb0d3618827c87bd71882930d752be6e3` target
PDNamedAction under ISO 19005-1 6.6.1 test 2 or ISO 19005-2/3 6.5.1 test 2.

All 84 independent controls agree with veraPDF 1.30.3 on the exact rule
and whole-file verdict. This corrects 36 prior false rejections, with no
reference-negative verdict lost in these controls: unused objects,
extension dictionaries, unknown AA events and array-valued OpenAction
roots do not establish Named action contexts. Each was tested with an
explicit Type Action dictionary and absent, Print, NextPage or
string-valued N. Raw before/after outputs, reference reports and hashes
remain session artifacts. The 231 authored cases additionally cover all
four permitted names and direct/indirect null/name values; 72 failed
on the preceding implementation. The older deep-validation fixture now
registers its tested action as OpenAction instead of leaving it unused.

All 5786 integrated PDF/A/deep tests pass and the solution builds with
existing machine feed warnings unchanged. The full 9468 measurement
predates this correction. Unknown-event and root-kind validity, exhaustive
action sources, target/required-key semantics and the separate all-object
known-prohibited subtype scan remain outside this correction. Existing
action traversal/finding exhaustion still produces an incomplete Error.
Historical corpus counts and broad Partial status remain unchanged.

## 2026-11-04 PDF/A NeedAppearances value kinds

The existing `PDFA-FORM-NEEDAPPEARANCES` Error now rejects every resolved
non-null value other than boolean false, rather than only boolean true.
This closes number, name, string, array and dictionary values without
changing the rule ID, clauses or null-as-omitted policy. Pinned profiles
at commit `c4f57effb0d3618827c87bd71882930d752be6e3` require
`NeedAppearances == null || NeedAppearances == false`: ISO 19005-1
6.9 test 1 and ISO 19005-2/3 6.4.1 test 3.

Of 42 independent controls, 39 exact predicates and whole-file verdicts
agree with veraPDF 1.30.3. All three indirect-null differences are retained:
FreeDF accepts resolved null as omission under ISO 32000-1 7.3.9;
veraPDF rejects. The controls close 21 wrong-kind reference-reported
misses, not those deliberate null-policy disagreements. Raw reports,
per-file hashes and FreeDF outputs remain session artifacts.
The 57 authored direct/indirect cases had 36 baseline failures.
The full Release suite now passes 9468 tests, zero failed/skipped,
including the latest field AA and page Widget appearance changes.
The solution builds with existing machine NuGet source warnings unchanged.

This is the AcroForm flag predicate, not general form validity, appearance
generation/association, XFA interpretation or field value correctness.
Historical corpus figures and broad Partial status remain unchanged.

## 2026-11-04 PDF/A-1 page Widget appearance presence

`PDFA1-FIELD-APPEARANCE` now rejects page Widget annotations without a
resolved appearance dictionary, including point rectangles and Widgets
not registered in AcroForm. ISO 19005-1 6.9 test 2 in pinned
validation-profiles commit `c4f57effb0d3618827c87bd71882930d752be6e3`
targets PDWidgetAnnot and requires `AP != null`. This is separate from
the parts 2/3 general annotation appearance-presence rule, whose
point-rectangle exception remains unchanged.

All 87 independent controls agree with veraPDF 1.30.3 on the exact
predicate and whole-file verdict: 75 rule positives and 12 negatives.
Sixty-three controls distinguish field-only, merged Widget and separate
child Widget layouts with seven AP kinds in all three parts; 24 further
controls vary page membership, AcroForm registration and point rectangles.
Missing, direct/indirect null and number-valued AP fail on A1 page Widgets;
empty dictionaries satisfy this presence predicate but still fail the
separate normal-only-key check. The reference does not reject off-page
Widgets under this predicate, even when registered in AcroForm.
Raw reference reports, hashes and FreeDF outputs remain session artifacts.

The 87 authored tests had 16 baseline failures. All 5498 integrated
PDF/A/deep tests pass and the solution builds, with the existing machine
NuGet source warnings unchanged. The preceding full 9252 run predates
both this correction and the field AA traversal. This establishes only
page Widget presence, not general field/data appearance association,
off-page validity, field-tree membership, all-state selection or visual
appearance correctness. Historical corpus measurements remain unchanged.

## 2026-11-04 PDF/A field additional-action entries

The AcroForm Fields/Kids tree now receives a dedicated additional-action
entry scan, including nonterminal ancestors and terminal non-Widget fields,
not just page Widget annotations. Any resolved non-null AA value produces
`PDFA-FIELD-AA` Error under ISO 19005-1 6.6.2 or ISO 19005-2/3 6.4.1.
These are test 2 in each pinned validation profile at commit
`c4f57effb0d3618827c87bd71882930d752be6e3`.

The scan reuses the shared form-detail depth/work constants (64 and
100000), with cycle/repeated-node detection and a 1000-finding bound.
Malformed child nodes/Kids kinds or exhausted bounds emit
`PDFA-FIELD-INCOMPLETE` Error rather than silently validating omitted
fields. Root Fields presence/type validity, orphan fields and arbitrary
field-tree membership are not established by this traversal.
The limits are library resources, not normative tree-size constraints.

Of 63 independent ancestor/terminal controls, 54 exact predicates and
whole-file verdicts agree with veraPDF 1.30.3. All nine indirect-null AA
differences remain explicit: FreeDF treats resolved null as omitted under
ISO 32000-1 7.3.9 while veraPDF rejects. No controls are excluded.
Raw reference XML and per-file hashes remain session artifacts.
The 72 authored tests include internal 64/65 depth, 100000/100001 work
and 1000/1001 finding boundaries plus cycles/malformed children.
The initial 69 tests had 42 failures. All 5411 integrated PDF/A/deep
tests now pass; the solution builds without new warnings. The preceding
full 9252 run predates this field-tree correction and is not represented
as a current full-suite measurement.

This closes bounded field AA entry checking, not non-Widget field A,
form appearance association, required field attributes or general
field-tree/reference validity. Historical corpus totals and broad
Partial status remain unchanged.

## 2026-11-04 PDF/A action-context subtype vocabulary

The existing inert bounded action report now supplies actual action
dictionaries for subtype validation. `PDFA-ACTION-TYPE` rejects missing,
non-name, unknown, deprecated and prohibited subtypes under ISO 19005-1
6.6.1 or ISO 19005-2/3 6.5.1. The whitelist is GoTo, GoToR, Thread, URI,
Named and SubmitForm, with GoToE additionally permitted in parts 2/3.
Pinned validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3` supplies test 1 for each.
The prior all-object known-prohibited-action scan remains, preserving its
ID and broader resource-object coverage.

Destination arrays are not action dictionaries. The report exposes unknown
AA events and arrays even in root slots requiring a dictionary; subtype
validation deliberately excludes unknown events and root-array descendants
instead of misclassifying their arbitrary dictionaries as actions.
Known events and legitimate Next chains/arrays remain checked. Initial
reference cross-checks caught both over-classifications before delivery;
regression tests pin the corrected scope. Root-kind and unknown-event
validity remain open rather than being silently claimed established.

All 90 new independent Link/OpenAction/Next controls agree with veraPDF
1.30.3 on the exact rule and whole-file verdict: 24 rule positives and
66 negatives. Rechecking the earlier 168 entry controls closes all six
empty-Link-action misses; 157 verdicts now agree, with the original 11
indirect-null differences retained. The earlier 151-agreement checkpoint
is historical, not overwritten. Raw reference XML and per-file hashes
remain session artifacts.

There are 165 authored tests. The initial 159 cases had 87 failures;
additional cases cover recognized AA events and exactly 1000 versus 1001
invalid subtype findings. Exhausting the existing report traversal budget/
depth/cycle bounds or the new 1000 subtype-finding bound emits
`PDFA-ACTION-INCOMPLETE` Error. These are library limits, not normative
action-count restrictions.

The first full run had 9251 passes and one failure in
`SignatureTrustTests.Path_length_constraint_violation_is_invalid`.
That test passed in isolation. An exact-parent archive at `64ad33ec`
then passed all 9087 tests (and its 71 signature-trust tests), while the
unchanged current implementation passed all 9252 in the comparison run.
This establishes a non-reproduced failure, not proof of its cause or
that it was unrelated. The failed run and both controls are retained;
temporary baseline checkout/archive were removed. The solution builds
without new warnings.

This closes bounded action-context subtype vocabulary, not all action
sources, target validity, required action keys, field-entry prohibitions,
malformed root kinds, unknown event keys, named-action semantics or
general reference resolution. Historical corpus totals and broad Partial
status remain unchanged.

## 2026-11-04 context-specific PDF/A action entries

The existing catalog AA prohibition now rejects every resolved non-null
entry, not only dictionaries, and its part-2/3 attribution is corrected
to ISO 19005-2/3 6.5.2 (part 1 remains 6.6.2).
New `PDFA-PAGE-AA` Errors reject page AA entries in parts 2/3, including
pages without annotations. Widget A/AA entries produce separate
`PDFA-WIDGET-ACTIONS` Errors in all parts; part 1 attributes A to 6.6.1
and AA to 6.6.2, while parts 2/3 use 6.4.1.
These checks prohibit the entries even when their action would otherwise
be permitted. Link actions are not indiscriminately banned, and part-1
page AA is not given the separate part-2/3 prohibition.

The pinned validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3` provides part-1 catalog 6.6.2
test 3 and Widget 6.6.1 test 3 / 6.6.2 test 1, plus parts-2/3 catalog/page
6.5.2 tests 1/2 and Widget 6.4.1 test 1. Across 168 independent controls,
157 scoped predicates and 151 whole-file verdicts agree with veraPDF
1.30.3. All 17 verdict differences are retained: 11 indirect-null entries
treated as omitted in FreeDF under ISO 32000-1 7.3.9, and six empty Link
A dictionaries rejected by the reference's action-subtype rule but not
yet by FreeDF. The latter are real remaining validator misses, not clean
positives or exclusions. The exact predicate comparison does not hide
that broader action-dictionary gap.

There are 174 authored cases: the initial final-policy 168 cases exposed
41 failures, and six additional cases cover pages without Annots and
simultaneous Widget A/AA findings. Full Release now passes 9087 tests,
none failed/skipped; the solution builds without new warnings. This
full run includes the preceding opacity, colour and XFA corrections.
External PDFs/XML and per-file hashes remain session artifacts.

This establishes catalog/page/Widget entry policies, not complete action
classification or field-tree traversal. Non-Widget field A/AA, malformed
and unknown action subtypes, arbitrary action-bearing structures and
reference-resolution limits remain open. Historical corpus totals and
broad Partial status remain unchanged.

## 2026-11-04 PDF/A-2/3 XFA and NeedsRendering policy

Parts 2/3 now reject non-null AcroForm XFA with `PDFA-FORM-XFA`, and
catalog NeedsRendering values other than absent/null/boolean false with
`PDFA-NEEDSRENDERING`. Both are Errors under ISO 19005-2/3 6.4.2.
This follows tests 1/2 in pinned validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3`. False XFA is still a present
entry and prohibited; non-boolean NeedsRendering is not silently treated
as false. Part 1 remains outside these new predicates.

Of 108 independent controls, 104 agree with veraPDF 1.30.3 on exact
rule sets and whole-file verdicts. Four indirect-null controls (XFA and
NeedsRendering in each of parts 2/3) are explicit verdict disagreements:
FreeDF treats resolved null as omitted, while veraPDF rejects. ISO 32000-1
7.3.9 makes null dictionary values equivalent to omission, as documented
in the Widget inheritance checkpoint. The differences are retained, not
excluded or called agreement. Inputs are inert values/streams; no XFA
content is executed or rendered. Raw reference XML and per-file hashes
remain session artifacts.

There are 156 authored cases spanning direct/indirect AcroForms and
entry values across all parts. The initial set exposed 48 failures before
the reference comparison additionally established malformed NeedsRendering
rejection. All 5000 integrated PDF/A/deep cases now pass; the solution
builds without new warnings. The preceding full 8373 run predates this
and the last two scoped corrections, not a current full-suite result.

This closes these two PDF/A-2/3 policy predicates, not arbitrary form-tree
validity, XFA parsing/preservation, rendering completeness or PDF/A-1 XFA
validity. Existing missing/cyclic-reference-to-null resolution remains a
limit. Historical corpus totals and broad Partial status remain unchanged.

## 2026-11-04 PDF/A-1 annotation colour arrays

Annotation C/IC arrays, including empty and indirect arrays, now require
an RGB PDF/A DestOutputProfile in part 1. `PDFA1-ANNOT-COLORSPACE` is a
page-attributed Error under ISO 19005-1 6.5.3 test 3 in pinned
validation-profiles commit `c4f57effb0d3618827c87bd71882930d752be6e3`.
The check reuses the already decoded OutputIntent colour-space set, not
a second ICC scanner. DefaultRGB alone does not satisfy this separate
annotation requirement; parts 2/3 are not given the part-1 restriction.
Absent/null/non-array entries do not fail this array-presence predicate.

All 144 independent controls agree with veraPDF 1.30.3 on the exact rule
and whole-file verdict: 128 rule positives and 16 rule negatives. These
are predicate counts, not clean conformance positives: the generated RGB
ICC is version 4, and the CMYK control deliberately mutates its header
without rebuilding its RGB tags. Both profile classes fail separate
PDF/A-1 ICC checks. The no-profile source is the retained clean calibrated
PDF/A control. No valid CMYK profile or clean PDF/A-1 RGB-profile control
is claimed by this experiment. Raw XML, per-file hashes and whole-file
findings remain session artifacts.

There are 192 authored cases spanning absent/RGB/CMYK/Gray output profiles,
both keys, direct/indirect/null values and all three parts, with DefaultRGB
present to prevent conflating the two policies. Initially 24 failed;
all 4844 integrated PDF/A/deep cases now pass and the solution builds
without new warnings. The previous full 8373 run predates opacity and
this correction, not a current full-suite measurement.

General C/IC kinds, array lengths/components, complete ICC validity and
malformed/conflicting OutputIntents remain outside this predicate.
Historical corpus totals and broad Partial status remain unchanged.

## 2026-11-04 PDF/A-1 annotation opacity

The page annotation scan now rejects optional numeric CA values other than
1 in PDF/A-1 with `PDFA1-ANNOT-OPACITY` Error, page attribution and
ISO 19005-1 6.5.3. This includes Link/Popup and Widget annotations and
resolves indirect values. Absent/null values and integer/real 1 do not
violate this predicate. Parts 2/3 are not given the PDF/A-1-only restriction.

The pinned validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3` supplies PDF/A-1 6.5.3 test 1.
All 192 independently mutated controls agree with veraPDF 1.30.3 on
the exact rule and whole-file verdict: 172 positives and 20 negatives.
There are 192 matching authored cases, initially with 20 failures;
the integrated PDF/A/deep selector now passes 4652 tests and the solution
builds without new warnings. The preceding full 8373 run predates this
opacity correction, not a new full-suite result. External PDFs/XML and
per-file hashes remain session artifacts.

This is the optional numeric PDF/A-1 opacity restriction, not general
annotation transparency or CA well-formedness. Non-numeric CA kinds and
general numeric range validity are not established by this predicate;
reference-positive malformed-kind/out-of-range controls are not claimed
generally valid PDFs. Historical corpus totals and broad Partial status
remain unchanged.

## 2026-11-04 PDF/A inherited Widget normal appearances

The normal-appearance type check now also classifies Widgets using the
nearest non-null inherited FT value. Non-button Widgets require an N
stream; Btn Widgets require an N dictionary containing at least one stream.
These are ISO 19005-1 6.5.3 tests 5/6 and ISO 19005-2/3 6.3.3 tests 3/4 in
validation-profiles commit `c4f57effb0d3618827c87bd71882930d752be6e3`.
`PDFA-ANNOT-APPEARANCE-BUTTON` and the existing
`PDFA-ANNOT-APPEARANCE-STREAM` are page-attributed Errors.

Parent traversal is cycle-aware and bounded to 64 dictionaries including
the Widget. An unresolved classification due to that bound, a dictionary
cycle or a malformed non-null Parent kind emits
`PDFA-ANNOT-APPEARANCE-INCOMPLETE`, not a guessed type. The state existence
search inspects up to 10000 entries; finding a stream establishes this
existential predicate even in a larger dictionary, but exhausting the bound
without finding one emits incomplete rather than falsely claiming none
exists. Both numeric limits are library resource bounds, not ISO limits.

Six hundred retained independent controls comprise 360 new inheritance/
state cases and the earlier 240 appearance-type cases. With veraPDF 1.30.3,
558 appearance-rule sets and 588 whole-file verdicts agree. All 42 rule-set
differences remain explicit: 15 indirect N-null cases and 27 indirect
FT-null inheritance cases. Twelve of the latter also differ in whole-file
verdict (three reference-positive/FreeDF-negative and nine
reference-negative/FreeDF-positive); the other 30 differences are
diagnostic-only. No cases are excluded or represented as full agreement.
The reference has 102 compliant files and FreeDF 108, not a new corpus
conformance or parity total.

The indirect FT-null controls resolve to null locally and inherit Btn from
Parent in FreeDF, consistently with direct null. veraPDF instead classifies
these indirect-null cases as non-button. The public
[ISO 32000-1 text](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/PDF32000_2008.pdf),
7.3.9 (PDF page 29), makes null dictionary values equivalent to omission;
12.7.3.1 (PDF page 440) marks FT inheritable. Those semantics are retained
rather than special-casing the reference's indirect-null behavior.
Raw XML, per-file hashes and disagreements remain session artifacts.

There are 369 authored tests, including 51 internal boundary/malformed
parent scenarios. The initial 366-test set had 252 failures. Full Release
now passes 8373 tests with none failed/skipped; the solution builds without
new warnings. The earlier non-Widget and key-only checkpoints retain their
original measurements and scope.

This is the at-least-one-stream predicate, not validation of every state:
mixed stream/non-stream entries can meet it, as independently confirmed.
AS/V state selection, all-state well-formedness, Form dictionaries and
content, required/valid FT values, complete field-tree membership and
general annotation validity remain open. Existing resolution collapses
missing/cyclic indirect references to null, so such references are not
claimed diagnosed by the new dictionary-parent traversal. Historical
corpus totals and broad Partial status remain unchanged.

## 2026-11-04 PDF/A non-Widget normal appearance streams

For a non-Widget annotation with a normal-only AP dictionary, its N value
must resolve to a stream. `PDFA-ANNOT-APPEARANCE-STREAM` reports an Error
with page attribution under ISO 19005-1 6.5.3 or ISO 19005-2/3 6.3.3.
This applies even to Link/Popup or point rectangles: exceptions to AP
presence do not permit wrong-kind supplied normal appearances. Widget
annotations are deliberately excluded pending inherited field-type and
button appearance-state validation.

The pinned validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3` supplies the exact non-button
predicates: PDF/A-1 6.5.3 test 6 and PDF/A-2/3 6.3.3 test 4.
Of 144 independently mutated non-Widget controls, all whole-file verdicts
agree with veraPDF 1.30.3 (18 positives, 126 negatives); 135 also agree on
the exact stream predicate. The nine indirect-null N controls are retained
diagnostic disagreements, not excluded or called exact agreement:
FreeDF resolves null and reports the normal-only key violation, while
veraPDF reports the stream-type violation. Both reject. This also exposes
an indirect-null limitation in the earlier normal-only key comparison;
its original 66 controls did not contain indirect N null values.

The remaining 96 exploratory controls are Widgets; no Widget conformance
or rule closure is claimed. All PDFs/XML, per-file hashes and disagreements
remain session artifacts. The 528 authored cases cover wrong scalar/array/
dictionary N values, direct/indirect resolution, nulls, invalid extra AP
keys, presence exceptions and the deliberate Widget exclusion. The initial
384-case set had 144 failures; all 4091 integrated PDF/A/deep cases now pass.
The preceding full 7476-case run predates this stream correction and is
not represented as a new full-suite result.

This establishes a non-Widget type check, not valid Form dictionaries,
stream content/rendering, button states, inherited field metadata or
general annotation well-formedness. Historical corpus totals and broad
Partial status remain unchanged.

## 2026-11-04 PDF/A normal-only appearance dictionary keys

Resolved annotation appearance dictionaries now require exactly one effective
key, N, in every PDF/A part. Null-valued entries are absent for this check:
N plus null R or a null custom entry passes, while an empty dictionary,
null N alone, rollover/down-only entries or non-null additional keys fail.
The rule also applies to Link/Popup when an AP dictionary is supplied;
their presence exceptions do not waive dictionary contents.
`PDFA-ANNOT-APPEARANCE-KEYS` is an Error with page attribution under
ISO 19005-1 6.5.3 or ISO 19005-2/3 6.3.3.

The pinned validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3` supplies the exact normal-only
predicates: PDF/A-1 6.5.3 test 4 and PDF/A-2/3 6.3.3 test 2.
All 66 independently mutated controls agree with veraPDF 1.30.3 on
the exact rule and whole-file verdict: 18 positives and 48 negatives,
covering direct/indirect dictionaries across all three parts. External
PDFs/XML and per-file hashes remain session artifacts.

There are 342 authored cases across Square/Link/Popup, including indirect
null entries and deliberately wrong-kind N values that do not fail this
key-only rule. Before implementation 180 failed; the integrated PDF/A/deep
selector now passes 3563 cases; full Release passes 7476, none failed/skipped.
The solution builds without new warnings. Missing/non-dictionary AP remains the
separate presence rule, not this dictionary-key predicate.

This does not validate N stream/state dictionary types, inherited button
field classification, appearance content or general AP well-formedness.
Existing resolution collapses missing/cyclic references to null, so this
does not establish malformed-reference detection. Traversal incompleteness
is reported separately; historical corpus totals and broad Partial status
are unchanged.

## 2026-11-04 explicit PDF/A annotation traversal incompleteness

The existing 10000-entry per-page annotation limit is retained, but
exceeding it now reports `PDFA-ANNOT-INCOMPLETE` Error on the affected page
under the annotation section (ISO 19005-1 6.5 / ISO 19005-2/3 6.3).
Earlier entries are still checked, and entries beyond the bound are not
silently treated as validated. The numeric limit is a FreeDF resource
bound, not an ISO constraint or a veraPDF predicate.

Thirty authored cases cover zero, 9999, 10000 and 10001 entries; valid and
invalid final entries; all parts; and independent two-page budgets/page
attribution. Six of the initial 24 cases failed before the change.
Exactly 10000 entries remain inspectable, and a prohibited subtype beyond
the bound is reported as incomplete inspection, not falsely claimed found.
This closes silent budget exhaustion only, not unbounded traversal,
malformed annotation-array handling or full conformance.

## 2026-11-04 PDF/A-2/3 annotation appearance presence

Non-exempt PDF/A-2/3 annotations now require an appearance dictionary.
Link and Popup annotations are exempt, as are annotations whose Rect has
both opposing coordinates equal (both width and height zero). A single
zero dimension does not qualify; reversed nonzero rectangles do not qualify.
Rect arrays/coordinates and AP values are resolved, including indirect null.
Missing or non-dictionary AP reports `PDFA-ANNOT-APPEARANCE-MISSING` Error
with page attribution under ISO 19005-2/3 6.3.3. PDF/A-1 has no corresponding
presence requirement and remains unchanged.

The pinned veraPDF predicate is 6.3.3 test 1 in validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3`. All 225 independently mutated
controls agree with veraPDF 1.30.3 on the exact presence rule and whole-file
verdicts: 201 positives and 24 negatives. They span Square/Link/Popup, all
three parts, five rectangle shapes, omitted/direct-null/indirect-null and
direct/indirect valid appearances. A separate 27-control exploratory batch
confirmed malformed AP kinds fail the presence rule in parts 2/3, while
empty/rollover dictionaries instead fail the separate normal-only rule.
These results are retained rather than conflating presence with contents.

There are 458 authored cases, including eight indirect rectangle/coordinate
cases; the initial 450-case set had 48 failures before the implementation.
Final full Release passed 7104 tests, none failed/skipped. External PDFs/XML
remain session artifacts; historical corpus totals are unchanged.

This establishes appearance presence only. Empty dictionaries meet this
presence predicate but are not claimed valid appearances: normal-only
keys, N stream/state dictionary rules, field inheritance, general Rect/AP
well-formedness and appearance rendering/content remain open. The existing
annotation traversal limit and null-resolution limitations also remain.

## 2026-11-04 PDF/A annotation subtype vocabulary

The page annotation scan now enforces the explicit PDF/A subtype
vocabularies rather than a short media-type blacklist. PDF/A-1 permits
the 16 PDF 1.4 types in ISO 19005-1 6.5.2 test 1; PDF/A-2/3 additionally
permit Polygon, PolyLine, Caret, FileAttachment, Watermark and Redact under
6.3.1 test 1. Screen and 3D remain prohibited in parts 2/3 as well as part 1.
Unknown, missing, null, non-name and wrong-case subtypes produce the existing
`PDFA-ANNOT-TYPE` Error with page/clause attribution.

The pinned veraPDF profiles at commit
`c4f57effb0d3618827c87bd71882930d752be6e3` provide those exact vocabularies.
All 198 independently mutated controls agree with veraPDF 1.30.3 on the
exact predicate and whole-file verdict: 120 clean subtype positives and
78 negatives, spanning direct/indirect entries and all three parts.
The controls use retained clean PDF/A source files with inert appearances;
external PDFs/XML remain session artifacts. A first control-generation
attempt collided `Text`/`text` filenames on Windows; it was discarded from
comparison. The final collision-free batch asserts all 198 distinct files
and retains its own raw outputs.

The 198 authored cases had 62 failures against the original implementation;
all now pass. The integrated PDF/A/deep-validation selector passed 2733
tests and the solution builds without new warnings. This establishes
subtype vocabulary, not complete annotation dictionaries, appearance,
file-attachment or form validity. The 10000-annotation traversal limit and
broader conformance gaps remain; historical corpus totals are unchanged.

## 2026-11-04 PDF/A symbolic TrueType cmap records

Simple symbolic TrueType `/FontFile2` programs now receive part-specific
cmap encoding-record checks. PDF/A-1 requires exactly one record;
PDF/A-2/3 accept exactly one or at least one Microsoft Symbol (3,0) record.
Missing required records report `PDFA-FONT-SYMBOLIC-CMAP` Error under
ISO 19005-1 6.3.7 or ISO 19005-2/3 6.2.11.6. Truncated, malformed or
unsupported headers and stream filters report a separate explicit
`PDFA-FONT-CMAP-INCOMPLETE` Error rather than silently skipping the check.

The existing bounded sfnt directory/record scanner is relocated from the
PDF/UA implementation to internal `FontChecks`, with a byte-identical body
(SHA-256 `66d73aed7728890414e95a5fe98fd3005decb770ec8eab4d1a502e484deedf60`).
PDF/UA's distinct predicates remain unchanged. No new font parser or
duplicated production scanner is introduced.

The pinned veraPDF predicates (validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3`) are PDF/A-1 6.3.7 test 3
and PDF/A-2/3 6.2.11.6 test 4. All 15 independently mutated real-font
controls agree on those exact rules: eight record positives and seven
record negatives. They reuse the retained UA-derived one/two-record
controls (Macintosh Roman, Unicode and Symbol combinations), not clean
PDF/A files or new whole-file conformance measurements.

There are 33 authored cases, including all parts, missing cmap, malformed
offset/header boundaries, unsupported filters and excluded font kinds.
Sixteen failed before the implementation; all now pass. The integrated
PDF/A, UA symbolic/Differences and deep-validation selector passed 2751
tests; the solution builds without new warnings. The preceding full
Release 6415-test result remains a dated earlier checkpoint, not a new
full-suite claim for these additional cases.

This validates record headers only, not cmap subtable mappings, checksums,
glyph programs or used-glyph completeness. Non-symbolic requirements,
FontFile3/OpenType and collections remain outside this check. Existing
bounded-decoding/resolution limitations and broad PDF/A gaps remain.

## 2026-11-04 PDF/A-2/3 literal TrueType Differences

Non-symbolic TrueType fonts in PDF/A-2/3 may now use `/Differences` with
literal Adobe Glyph List names (plus the reserved `.notdef`), instead of
rejecting every Differences array. MacRomanEncoding or WinAnsiEncoding is
still required directly or through BaseEncoding. PDF/A-1's separate
Differences prohibition and symbolic-font Encoding prohibition are unchanged.
Invalid names retain the existing `PDFA-FONT-TRUETYPE-ENCODING` Error and
ISO 19005-2/3 6.2.11.6 clause. Algorithmic extraction names such as
`uni0060`, `u0060`, suffix and underscore combinations are not literal AGL
members and remain invalid.

The bounded PDF/UA scanner is extracted into internal `FontChecks` and
reused, not duplicated. Official AGL resources and text extraction are
unchanged. Malformed arrays and the 10000-entry limit produce explicit
`PDFA-FONT-ENCODING-INCOMPLETE` Errors rather than being accepted as valid
Differences. PDF/UA rule IDs/severities/clauses and scan outcomes are
preserved; incomplete diagnostic wording is now shared.

Pinned veraPDF validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3` distinguishes PDF/A-1 6.3.7
test 1 from PDF/A-2/3 6.2.11.6 test 2. All 30 independently mutated
real-font controls agree on the exact predicate: 10 valid-name positives
in parts 2/3 and 20 negatives, including all part-1 Differences arrays.
These UA-derived files are not claimed clean PDF/A controls or new
whole-file corpus closures.

There are 294 authored cases: direct/indirect names, symbolic/non-symbolic
and embedded/unembedded fonts, profile differences, malformed/null/budget
boundaries and valid/invalid BaseEncoding. The initial 270-case set had
70 failures before the fix. Final full Release passed 6415 tests, none
failed/skipped; the solution builds without new warnings. All 435 public
UA PDFs (870 profile rows) retain their previous verdicts and exact
error/warning ID sets after the shared extraction. The retained reference
comparisons remain 9 UA-1 and 5 UA-2 misses with zero false positives or
exclusions; historical corpus measurements below are not overwritten.

This is literal-name policy, not complete character mapping. Embedded
Microsoft Unicode (3,1) cmap requirements, actual-use glyph lookup and font
program completeness remain open. Existing null-resolution limitations
also remain. No external PDF/XML is bundled or full conformance claimed.

## 2026-11-04 PDF/A-2/3 embedded CIDFont mapping entries

Embedded CIDFontType2 `/CIDToGIDMap` in PDF/A-2/3 must now resolve to
a stream or the literal name `/Identity`; merely containing the key is no
longer sufficient. The existing `PDFA-FONT-CIDTOGID` Error, page attribution
and ISO 19005-2/3 6.2.11.3.2 clause are retained. The embedding predicate and
resolved-value test follow the existing PDF/UA entry check.

The pinned veraPDF profiles (validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3`) express 6.2.11.3.2 test 1
for embedded Type 2 CIDFonts without a rendering-mode exemption. All 52
independently mutated real-font controls agree with veraPDF 1.30.3 on that
exact predicate: 12 map positives and 40 map negatives, across PDF/A-2/3
and text rendering modes 0/3. They derive from the verified public
UA-1 CIDFont positive and are not clean PDF/A documents: **all whole-file
verdicts remain noncompliant**, so they establish exact-rule behavior, not
new whole-file conformance or corpus-miss reductions. No external PDF/XML
is bundled.

There are 224 authored cases spanning embedded/unembedded fonts,
CIDFontType0/2, visible/invisible text and 14 map representations including
direct/indirect Identity, stream, null, wrong kinds and a cyclic reference.
Forty failed against the original implementation; all now pass.
The integrated PDF/A, UA CID-map and deep-validation selector passed 2320
tests. This is entry-type validation, not stream mapping/glyph completeness.

PDF/A-1 is deliberately unchanged: its separate rendering-mode-dependent
6.3.3.2 rule still needs a use-aware implementation, and the old missing-key
check/clause is not claimed complete. General malformed-reference diagnosis,
unused font traversal and complete embedding semantics remain open.

## 2026-11-04 PDF/A Hide/GoToE action policy

The existing known-action scan now rejects `/Hide` in all three parts and
`/GoToE` in PDF/A-1 only. PDF/A-2/3 keep GoToE permitted by this rule.
Optional action `/Type` is resolved consistently: absent, direct null and
indirect null are equivalent, so null no longer bypasses existing prohibited
action checks. A different explicit Type remains outside the existing
action-dictionary heuristic.

The pinned veraPDF action predicates (validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3`) are ISO 19005-1 6.6.1 test 1
(including Corrigendum 2's Hide prohibition) and ISO 19005-2/3 6.5.1
test 1. Thirty-six independently mutated catalog OpenAction controls agree
with veraPDF 1.30.3 on the exact rule and whole-file verdict: 20 positives
and 16 negatives, spanning Hide, GoToE and URI with absent, Action,
direct-null and indirect-null Type. No action is executed or external
destination opened.

The 540 authored cases cover 18 known action kinds, all parts, direct and
indirect S names and five Type variants, retaining existing prohibited
kinds and allowed-kind controls. Against the original implementation 152
failed and 388 passed; all now pass. The integrated PDF/A/deep-validation
selector passed 1984 tests. Rule ID, severity, clause attribution and
one-finding-per-document behavior are unchanged.

This is not a complete action whitelist or action-target validity check:
deprecated/unknown action kinds and context-aware classification beyond the
existing heuristic remain open. Other keys and target dictionaries are not
validated by these cases. As elsewhere, null resolution does not distinguish
intentional null from missing/cyclic references. No new corpus-wide
conformance or executable-content support is claimed.

## 2026-11-04 PDF/A external-stream null entries

The all-object stream scan now resolves `/F`, `/FFilter` and `/FDecodeParms`
and rejects only non-null values, preserving `PDFA-STREAM-EXTERNAL` Error
severity. Direct null and indirect references to null no longer produce
false positives. The PDF/A-2/3 clause is corrected to 6.1.7.1; PDF/A-1
remains 6.1.7. Pinned veraPDF validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3` expresses test 3 as
`F == null && FFilter == null && FDecodeParms == null` in each part.

There are 126 authored cases across three keys, direct/indirect values and
three parts. Ninety failed against the original implementation (null
false positives and PDF/A-2/3 clause attribution); all now pass.
All 36 independently mutated page-content stream controls agree with
veraPDF 1.30.3 on the exact rule and whole-file verdict: 18 null positives
and 18 non-null negatives. No external file is provided or loaded by FreeDF;
the reference reports and mutated PDFs remain session artifacts.
The integrated PDF/A/deep-validation selector passed 1444 tests.

This corrects optional-entry semantics, not general stream well-formedness,
filter completeness or object-resolution diagnostics. `PdfFile.Resolve`
already collapses missing/cyclic references to null; this slice does not
distinguish them from intentional nulls or claim new malformed-reference
validation. Historical corpus measurements below remain unchanged.

## 2026-11-04 PDF/A annotation visibility flags

The annotation scan now rejects the Invisible bit in PDF/A-1/2/3 and
ToggleNoView in PDF/A-2/3, alongside the existing Hidden/NoView prohibitions
and Print requirement. Existing rule IDs, Error severity, page attribution
and ISO 19005-1 6.5.3 / ISO 19005-2/3 6.3.2 clauses are retained.
ToggleNoView remains permitted by this rule in PDF/A-1.

Popup is no longer a blanket exemption. PDF/A-1 requires valid flags on
Popup annotations too. PDF/A-2/3 exempt Popup only from `/F` presence;
if supplied, its bits must meet the same visibility/Print requirements.
All non-Popup annotations still require `/F` in every part. The
`if present` wording of PDF/A-2/3 test 2 does not remove test 1's separate
non-Popup presence requirement. Explicit null is treated as absent.

The exact pinned veraPDF predicates (validation-profiles commit
`c4f57effb0d3618827c87bd71882930d752be6e3`) are PDF/A-1 6.5.3 test 2
and PDF/A-2/3 6.3.2 tests 1/2. All 108 independently mutated Square/Popup
controls agree with veraPDF 1.30.3 on those predicates and whole-file
verdicts: 36 positives and 72 negatives. Popup controls include a valid
parent annotation. Direct and indirect flags, absent/null, individual
forbidden bits, Print and allowed extra bits are covered without bundling
the external PDFs/XML.

The 792 authored cases cover all combinations of Print, Invisible, Hidden,
NoView and ToggleNoView, with/without allowed extra bits, direct/indirect
entries, Popup/non-Popup and all three parts, plus absent/null controls.
Against the original implementation 436 failed and 356 passed; all now pass.
The integrated PDF/A/deep-validation selector passed 1318 tests, and the
solution builds without new warnings. This is flag-policy validation, not
complete annotation type/range, appearance or general PDF/A conformance.
Historical corpus measurements below have not been overwritten.

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
### 2026-11-04 PDF/UA rule-family overlap after file-verdict closure

The cross-check tool now maps the eight delivered subset-listing, font-width,
forbidden ToUnicode value and explicit `.notdef` finding families to their pinned
UA-1/UA-2 profile tests. Incomplete-inspection Errors do **not** count as overlap;
neither does rejecting the same file for an unrelated rule. Tests preserve those
distinctions and keep unimplemented rules unmapped.

Reconciled the latest 870 FreeDF results with retained exact reference rules,
without rerunning Java: native UA-1 remains 141 pass/156 fail agreements and UA-2
54 pass/84 fail agreements, with no exclusions. The eight new mappings overlap
all their 13 observed rule/file occurrences. Of 97 distinct failing UA-1 reference
tests, 52 are mapped and 46 have no observed mapped-family overlap (including one
mapped test); of 48 UA-2 tests, 34 are mapped and 14 have no overlap. These are
**mapping lower bounds**, not counts of missing implementation: existing checks
may still lack mappings, and a family match does not prove an exact predicate.
No percentages of standards coverage are inferred. Classify those unmapped and
other-rule-only cases before choosing further fixes; the prior zero file-verdict
misses do not close this work.
