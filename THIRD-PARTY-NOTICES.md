# Third-party notices

## Shaping, bidi and line breaking (text shaping integration)

Unicode Character Database 16.0.0 (generated range tables in `src/PortableDocument.Content/Shaping/UnicodeTables.cs`, and the BidiTest/BidiCharacterTest conformance files in `tests/fixtures/ucd`):
Copyright (c) 1991-2025 Unicode, Inc., used under the Unicode License v3. Full text: `tests/fixtures/ucd/UNICODE-LICENSE.txt`.

Test fonts in `tests/fixtures/shaping` (SIL Open Font License 1.1; used only by tests, not shipped in the library):
- Source Sans 3 subset (renamed "Shaping Test Sans" because of the Reserved Font Name "Source"): Copyright 2023 Adobe. Licence and copyright: `tests/fixtures/shaping/OFL-SourceSans3.txt`.
- Noto Sans Arabic, Hebrew and Devanagari subsets: Copyright The Noto Project Authors. Licence and copyrights: `tests/fixtures/shaping/OFL-Noto.txt`.

HarfBuzz (via the `uharfbuzz` Python package, MIT) was used only to generate the reference glyph sequences in `tests/fixtures/shaping/hb-expected.json`; no HarfBuzz code is included.
