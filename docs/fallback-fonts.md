# Fallback fonts and CJK glyph data

Non-embedded fonts (ISO 32000-2 9.6.2.2, 9.7.4.2, 9.8) are drawn from bundled, properly licensed data. Nothing in this folder is part of the browser startup payload.

## Layout (`src/PortableDocument.FallbackFonts/fallback-fonts/`)

| Path | Content | Licence |
|---|---|---|
| `latin/Liberation{Sans,Serif,Mono}-{Regular,Bold,Italic,BoldItalic}.ttf` | Metric-compatible Helvetica/Arial, Times/Times New Roman, Courier/Courier New | OFL-1.1 |
| `latin/DejaVuSans.ttf` | Symbol and ZapfDingbats glyphs (Greek, maths, arrows, Dingbats block) | Bitstream Vera + public domain |
| `cjk/{Japan1,GB1,CNS1,Korea1}/{xx}.bin` | Noto Sans JP/SC/TC/KR outlines, one deflated chunk per 256-scalar Unicode block (`xx` = scalar >> 8, hex) | OFL-1.1 |
| `licenses/` | licence texts, authors and `manifest.json` (licence manifest, embedded in the assembly too) | |
| `manifest.json` | every asset with size and SHA-256; hosts may precache the small files, everything is lazy by design | |

Glyph widths come from the Adobe Core 14 AFM numbers (`PortableDocument.Content/Resources/std14-widths.bin`, which also carries the built-in Symbol and ZapfDingbats encodings). The Adobe permission paragraph and per-file copyright years ship with the table (`std14-notice.txt`) and were checked verbatim against the published Core 14 AFM files; the widths were re-derived from those AFMs and match the embedded table exactly. Rebuild everything with `tools/FreeDF.FontPack`.

Size: Latin 4.9 MB, CJK 8.5 MB (438 chunks, 64-250 glyphs each, 1.5-3.5 MB per ordering; legacy GB2312, JIS X 0208/CP932, Big5 and KS X 1001 repertoires). The data lives in a separate project, `PortableDocument.FallbackFonts` (the assembly itself is a few KB; fonts are content files), so consumers that do not reference it keep today's size.

## Selection

`FontSubstitution` classifies a BaseFont (subset tag stripped) plus FontDescriptor `Flags`, `ItalicAngle`, `FontWeight` and `StemV` into bold/italic/mono/serif, and maps Arial/Helvetica, Times/Times New Roman, Courier/Courier New, Symbol(MT) and (ITC)ZapfDingbats variants to the standard-14 names (narrow/condensed/black variants are left to the generic path). Bundled fonts win over installed fonts for Latin text so output is deterministic. For CJK, installed fonts come first (desktop), then the bundled chunks.

## Widths

`/Widths` always wins. Without it, standard-14 fonts use the AFM advance; others use the substitute's advance. When `/Widths` disagrees with the substitute glyph advance, the outline is scaled horizontally to match (ratio clamped to 0.5-1.5, no change within 3%), so layout and selection geometry are preserved.

## Browser hosting

`ProvidedAssetSource` reports an asset as pending on first use and records the request. `worker.js` fetches the requested same-origin files (`fallback-fonts/<path>`), hands them to `BrowserEditor.ProvideFontAsset` (or `FontAssetUnavailable` on failure) and re-runs the GET render. Pending renders are never cached (`IFontAssetPending`). Offline or failed fetches fall back to the embedded Bitstream Vera for Latin text and to no glyphs for CJK, never to an error. No external network is used.

## Decisions

* Type 1 hinting: not implemented. Outlines are rendered unhinted with analytic anti-aliasing at the sizes the renderer targets; hint replacement and stem snapping change output by sub-pixel amounts only, so they were judged not worth the complexity. Revisit only if pixel-exact comparison against FreeType-hinted output becomes a goal.
* Serif/sans CJK: Noto Sans only; serif requests render with the sans design (documented limitation).
* Symbol glyph names that Adobe maps to private-use code points (for example `bracelefttp`) have no DejaVu equivalent and draw nothing.
