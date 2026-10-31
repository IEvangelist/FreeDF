Test fonts for ShapingTests (all SIL OFL 1.1; subsets produced with fontTools/pyftsubset keeping all layout features):
  SourceSans3-subset.otf  - Modified Version of Source Sans 3 Regular (Basic Latin, Latin-1, combining marks U+0300-036F, U+FB00-FB06), renamed 'Shaping Test Sans' by rename_source_subset.py because of the Reserved Font Name 'Source'. Licence: OFL-SourceSans3.txt
  NotoSansArabic-subset.ttf, NotoSansHebrew-subset.ttf, NotoSansDevanagari-subset.ttf - Noto Sans subsets (no Reserved Font Name). Licence: OFL-Noto.txt
  hb-expected.json - reference HarfBuzz output produced by generate_expected.py (uharfbuzz).
  generate_composition.py - generates src/.../Shaping/CanonicalComposition.cs from the Unicode database used by the host Python (unicodedata).
See also ../ucd (Unicode data tables and conformance files).
