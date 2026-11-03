# Page-tree allocations and CI controls: 2026-11-01

Baseline: `main` at `46bb8f65`. This slice follows the opportunities in
[the large-corpus profile](2026-10-31-large-corpus-profile.md), without claiming
the rendering/open targets are met.

## Measured allocation fix

`PdfDocument.Walk` copied its inheritance dictionary for every page, including
leaves that immediately discarded the copy. It now shares an immutable snapshot
until a branch overrides an inherited attribute. Leaves keep their original
parent snapshot; direct leaf values still take precedence. Branch overrides
cannot leak to siblings. `PdfLexer.TryRef` also reads the already-bounded
generation digits directly instead of allocating an ASCII string for `int.Parse`.
Its five-digit bound, comments, token boundary and failed-lookahead position
remain unchanged.

Method: Windows x64, .NET SDK 10.0.403, Release, default GC. Separate old/new
processes using the same `FreeDF.Profile` harness and identical generated PDF:
788,102 bytes, 10,000 empty pages, classic xref, one root inheritance dictionary.
The old binaries were copied before editing. Each process performed 30 opens,
then the harness's warmed `alloc_split` measurement. Allocation split uses
`GC.GetAllocatedBytesForCurrentThread`; other counters are process-wide totals.
This deliberately isolates page loading, not representative rendering.

| Measurement | Before | After |
|---|---:|---:|
| File load allocation | 0.43 MiB | 0.43 MiB |
| File + page loading allocation | 18.19 MiB | 15.67 MiB |
| 30-open process allocation at first report | 547.1 MiB | 471.6 MiB |
| Open median | 37.698 ms | 37.640 ms |
| Xref entries / page count | 10,003 / 10,000 | 10,003 / 10,000 |
| Xref fingerprint (SHA-256 prefix) | `8EC0A353DD39E150` | `8EC0A353DD39E150` |

**About 14% less allocation, not a demonstrated elapsed-time speed-up.**
The machine is shared; GC/tiering moved individual open times substantially.
The single-run medians are effectively equal. These changes do not remove
eager page loading, stream copies, full-resolution JPX decoding, or the raster
gap recorded in the earlier corpus report.

Reproduce with no Python dependencies:

```powershell
python tools\profile\gen_page_tree.py page-tree-10000.pdf
dotnet build tools\FreeDF.Profile -c Release
dotnet tools\FreeDF.Profile\bin\Release\net10.0\FreeDF.Profile.dll page-tree-10000.pdf --ops open,alloc_split,fingerprint --iters 30 --budget 30
```

Use separately built baseline/current binaries on the same PDF; do not rebuild
over a running profiler. The generated PDF is not committed. Page-tree
regressions exercise branch/leaf inheritance, direct overrides, sibling
isolation, duplicate/cyclic Kids and full-save preservation.

## CI findings and fix

The latest scheduled [run](https://github.com/IEvangelist/FreeDF/actions/runs/41311004728)
reported a real showcase-open regression: median 907.7 ms versus its 825.0 ms
relative median ceiling (repeat tail 907.7 versus 833.8 ms). It also claimed that
the synthetic single-repeat-outlier control failed. The latter was contaminated
by that unrelated showcase regression because each injected case started from
the live failing timing profile.

`Test-EditorProfileCi.ps1` now creates a timing-normalized control, verifies it
passes, and injects each fault into that control. Input acknowledgement, total,
interaction, presentation, heartbeat and long-task timings are normalized only
in this synthetic copy. Required groups, pixels, allocations and raster counts
are untouched. All ten controls pass against that same scheduled-run artifact,
including visible single-outlier warning/maximum and two-outlier rejection.
The real `profile-ci` check still sees the unmodified profile and still rejects
the showcase regression. Neither budgets nor known-miss allowlists changed.
The open regression remains tracked in [#4](https://github.com/IEvangelist/FreeDF/issues/4).

The earlier merge-stack failure was already fixed by `46bb8f65`; that classifier
is unchanged. No claim is made here that hosted deployment CI is green.

During the public FreeDF rebrand, generating the optional `27-full-collection.pdf`
also exposed a second reproducibility problem: the local sample wildcard copied
it into the raster fixture corpus, adding 50 pages absent from the checked-in
goldens. The golden enumerator now excludes that on-demand collection. Only
sample 28's 18 intentional wordmark/footer/palette changes refresh pixel hashes;
all 375 other fixture/sample page hashes remain unchanged. The library renderer
has not been changed to accommodate the rebrand.

The installation icons initially added two startup files, exceeding the existing
62-file AOT ceiling. The publisher now embeds the real 192px PNG in the manifest
and uses the opaque 512px PNG for both normal and maskable installation icons.
The same candidate drops from 64 to 62 startup files, with 23.75 MiB raw,
8.46 MiB gzip and a 15.36 MiB largest file. All four original payload ceilings
pass without changes. Chromium also accepts the published manifest and confirms
installation eligibility; offline editing, save-handle write-back and the
service-worker update/rollback protections pass against this exact AOT build.

### Showcase editor pixel baseline correction (2026-11-03)

The rebrand regenerated `samples/01-spec-showcase.pdf` in commits
`78906500` and `06a69266`, but its editor first-page pixel baseline still
expected the pre-rebrand hash. All three repeats in
[the pre-rebrand run](https://github.com/IEvangelist/FreeDF/actions/runs/41311004728)
matched `AD9BE49FD168998F562B4C008A8D7F79CD3DB76950B02E7B422B5150B49688E4`;
all three in
[the post-rebrand run](https://github.com/IEvangelist/FreeDF/actions/runs/41651437352)
match `6FDB4446C535CF55263C2849D9A5F0B233C927763A74B67ECC9CBCC1365BC8B2`
at the same 339 by 480 dimensions. The other four fixture hashes are unchanged.
Only the showcase hash in `editor-ci-known-misses.v1.json` is corrected.

Replaying the post-rebrand raw profile through `--profile-ci` fails solely on
the showcase decoded-pixel gate with the old policy and passes with the
corrected policy. A separate three-repeat Windows Chromium replay of that
run's retained Linux AOT site confirms all 15 expected fixture images, with
the same old-policy failure and corrected-policy pass.
`Test-EditorProfileCi.ps1` also passes all ten fault-injection controls,
including rejection of corrupted pixels. Timing ceilings, strict goals,
allocations, cache gates and all other pixel values remain unchanged.
This fixes stale pixel evidence, not proof of recovered hosted timing:
[#4](https://github.com/IEvangelist/FreeDF/issues/4) remains open pending a
current-main scheduled performance run.

## Selected parity gap

Named destinations deeper than 16 levels resolved during outline/link
inspection but lost their bookmark page targets during import. Import now
uses inspection's cycle-aware, depth-64 lookup and still remaps the imported
page references. Tests cover wrapped destinations, cycles followed by valid
siblings, depth 20/64, and full/incremental save preservation. This is a bounded
destination/editing fix, not complete ISO 32000-2 parity or catalog-lossless merging.
