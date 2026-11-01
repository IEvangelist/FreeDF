using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

sealed partial class Gen28
{
    // Status words and clause numbers follow docs/spec-matrix.md (read at commit aa127037); the caveats are condensed from the same rows and from docs/spec-backlog.md.
    public static readonly string[][] ParserRows =
    [
        ["7.2\u20137.3", "Lexical conventions, all object types", "Done", "Tolerant numbers, #xx names, literal and hex strings."],
        ["7.5", "File structure: xref tables and streams, hybrid, Prev chain, object streams", "Done", "Read and written, including object streams."],
        ["7.5.6", "Recovery of damaged files", "Done", "Scans for \u201Cn g obj\u201D; covered by the RepairsBrokenXref test."],
        ["7.4", "Filters: Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors", "Done", "Image codecs are the next row."],
        ["7.4", "Image codecs: DCT, CCITT, JBIG2, JPX", "Partial", "Gaps: SOF11 arithmetic lossless, hierarchical JPEG, subsampled interleaved lossless. JBIG2 non-byte-aligned symbol widths and nonzero RDW/RDH, and non-default 12-bit DAC, lack independent references."],
        ["7.6", "Encryption: Standard RC4/AES, public-key", "Done", "Standard R2\u2013R6 read and write (pikepdf-verified). Public-key handler is round-trip tested only; no third-party validator."],
        ["7.7", "Document structure: page tree, inheritance", "Done", "No caveats recorded."],
        ["7.8\u20137.9", "Content streams, resources, functions (types 0, 2, 3, 4)", "Done", "No caveats recorded."],
        ["7.10\u20137.11", "File specifications, embedded files, data structures", "Partial", "Remaining: broader tree-boundary, attachment and preservation combinations. /FS, /Thumb, /EP, /RF and /CI stay inert; external payloads are never opened."],
        ["12", "Annotations, forms, outlines, actions", "Partial", "Typed inert action models; nothing executes. Open: exhaustive action-bearing objects and triggers, annotation appearances, complete form and action semantics."],
        ["14", "Metadata, tagged PDF, logical structure", "Partial", "Open: external streams, arbitrary owner graphs, per-invocation tagging. Plain ExtractText excludes annotation text. Diagnostics are not PDF/UA certification."],
        ["14.8", "PDF/A and PDF/UA validation", "Partial", "Heuristic validators. Rules were compared by hand against published veraPDF profiles; veraPDF was not run. Not certification."],
        ["Robustness", "Fuzz and truncation tests", "Done", "Deterministic mutation of all fixtures (400-seed run green); caps on lexer nesting and inflate size."],
        ["Perf", "Hot paths", "Partial", "Tuned rasteriser, JPEG and caches. Fresh-vector and text CPU gains were not reliable (see the benchmark pages)."],
    ];

    public static readonly string[][] RendererRows =
    [
        ["8.4", "Graphics state, paths, clipping", "Done", "Anti-aliased scanline raster; non-zero and even-odd."],
        ["8.4.3", "Stroking: width, caps, joins, miter, dash", "Done", "No caveats recorded."],
        ["8.6", "Device, Cal*, Lab, Indexed, Separation, DeviceN, ICCBased", "Partial", "ICC matrix/TRC and LUT to sRGB; LittleCMS-compatible black-point compensation is a compatibility choice, not ISO 18619. D2B/B2D, device-link, general CMM and gamut mapping are open."],
        ["8.7", "Tiling and shading patterns", "Partial", "Types 1\u20137 including Coons and tensor surfaces. Tessellation is finite and bounded; broader function and colour-space combinations and independent fixtures are open."],
        ["8.9\u20138.10", "Images, inline images, XObject forms", "Partial", "Stencil, colour-key and soft-mask alpha work; completeness depends on the codec gaps above."],
        ["9", "Fonts: TrueType, Type0/CID, Type3, ToUnicode", "Partial", "CFF, Type 1 (incl. Multiple Master) and all 59 predefined CMaps. CJK fallback is covered by fixtures, not pixel parity; serif CJK reuses the sans design."],
        ["11", "Transparency: alpha, blend modes", "Partial", "Alpha, 16 blend modes, groups and soft masks are Done. CMYK/Cal/ICC page-group blending is analytic-tested, not compared with Chrome or PDFium."],
        ["8.11", "Optional content (OCG/OCMD)", "Partial", "Visibility expressions, configurations, locks, radio groups. State changes are in memory; language validation is structural only."],
        ["10", "Rendering intents, overprint, halftones, transfer", "Partial", "Overprint simulation is opt-in and RGB-approximate; halftone, flatness and smoothness remain inert."],
        ["13", "Multimedia, 3D, RichMedia, XFA, JavaScript", "Partial", "Typed inert metadata only; nothing executes or plays. U3D and PRC fixtures are synthetic."],
    ];

    public static readonly string[][] EditorRows =
    [
        ["Editor", "Moving and resizing existing images and vector objects", "Partial", "Page-level and uniquely invoked Form children. Shared, repeated or nested Form children are withheld; complex clip ranges unsupported."],
        ["Editor", "Redaction (Redactor.Apply)", "Done*", "Done for this bounded API, not universal sanitisation: full rewrite, whole-object fallback, glyph and outline-text limits."],
        ["12.8", "Digital signatures", "Done*", "Detached SHA-256, DocMDP, RFC 3161 and DSS reading. Chain trust, revocation, PAdES LTV and DSS authoring are unsupported."],
    ];

    static int Count(string[][] rows, string status) => rows.Count(r => r[2] == status);

    // ---- 14: overview ----------------------------------------------------------------------------------------------------------------

    void SpecOverviewPage()
    {
        var done = Count(ParserRows, "Done") + Count(RendererRows, "Done");
        var limited = Count(EditorRows, "Done*");
        var partial = Count(ParserRows, "Partial") + Count(RendererRows, "Partial") + Count(EditorRows, "Partial");
        var total = done + limited + partial;
        var y = ContentPage("Part 3  /  Spec parity", Amber, "ISO 32000-2: where parity stands",
            $"Full parity is the goal. This is the honest state: {partial} Partial rows, 0 Todo rows, and {limited} Done rows that carry stated limits. Nothing on these pages is a conformance claim.",
            "Spec parity: overview", "Part 3: Spec parity");
        y -= 14;
        SubHeading(y, "Rows by status", Amber);
        var top = y - 14;
        double cx = M + 92, cy = top - 92, r = 64;
        Fig($"Donut chart of {total} counted matrix rows: {done} Done, {limited} Done with limits and {partial} Partial. There are 0 Todo rows.", () =>
        {
            var a = 90.0;
            (int N, Color C)[] segs = [(done, "#059669"), (limited, "#84CC16"), (partial, "#F59E0B")];
            foreach (var (n, c) in segs)
            {
                var sweep = 360.0 * n / total;
                _pg.Save(); _pg.Stroke(c); _pg.Width(26); _pg.ArcPath(cx, cy, r, a, a - sweep + 1.2, true); _pg.StrokePath(); _pg.Restore();
                a -= sweep;
            }
            Tx(_t.SerifB, 34, cx, cy - 4, partial.ToString(), Ink, 'c');
            Tx(_t.SansB, 9, cx, cy - 20, "Partial rows", Slate, 'c');
        });
        T("P", () =>
        {
            var lx = cx + r + 34;
            (string Name, int N, Color C)[] key = [("Done", done, "#059669"), ("Done with limits", limited, "#84CC16"), ("Partial", partial, "#F59E0B"), ("Todo", 0, "#E11D48")];
            for (var i = 0; i < key.Length; i++)
            {
                Box(_pg, lx, cy + 38 - i * 20, 10, 10, fill: key[i].C, r: 2.5);
                Tx(_t.SansB, 9.4, lx + 16, cy + 39 - i * 20, key[i].N.ToString(), Ink);
                Tx(_t.Sans, 9.4, lx + 34, cy + 39 - i * 20, key[i].Name, Slate);
            }
        });
        // stacked bars
        var bx = M + 300;
        var bw = W - 300;
        (string Name, int D, int L, int P)[] bars =
        [
            ("Parser tables", Count(ParserRows, "Done"), 0, Count(ParserRows, "Partial")),
            ("Renderer tables", Count(RendererRows, "Done"), 0, Count(RendererRows, "Partial")),
            ("Editor rows counted", 0, limited, Count(EditorRows, "Partial")),
        ];
        var unit = bw / bars.Max(b => b.D + b.L + b.P);
        Fig("Stacked bars of matrix rows by status. Parser tables: 8 Done and 6 Partial. Renderer tables: 2 Done and 8 Partial. Editor rows counted: 2 Done with limits and 1 Partial.", () =>
        {
            for (var i = 0; i < bars.Length; i++)
            {
                var b = bars[i];
                var by = top - 24 - i * 50;
                Tx(_t.SansB, 8.8, bx, by + 20, $"{b.Name}  ({b.D + b.L + b.P})", Ink);
                var x = bx;
                foreach (var (n, c, fg) in new (int, Color, Color)[] { (b.D, "#059669", White), (b.L, "#84CC16", Ink), (b.P, "#F59E0B", Ink) })
                {
                    if (n == 0) continue;
                    Box(_pg, x, by - 6, n * unit - 1.5, 20, fill: c, r: 3);
                    Tx(_t.SansB, 9, x + n * unit / 2 - 0.7, by - 0.5, n.ToString(), fg, 'c');
                    x += n * unit;
                }
            }
        });
        T("Caption", () => Para(_t.SansI, 8.6, bx, top - 24 - 3 * 50 + 20, bw, "Figure 13. Counts of table rows in docs/spec-matrix.md. Other editor-table rows are not counted.", Slate, 12));

        y = top - 190;
        SubHeading(y, "How strong is the evidence behind a status?", Amber);
        var cw = (W - 24) / 3;
        (string Head, Color C, Color Bg, string[] Items)[] cols =
        [
            ("Independent reference", Emerald, "#ECFDF5", [
                "PDFium: selected shading and rendering fixtures compared clean.",
                "OpenJPEG 2.5.4: JPX packed-header fixtures match exactly (384 samples each).",
                "libjpeg-turbo 3.2.0: 12-bit arithmetic DCT, maximum error 3, default conditioning only.",
                "Pillow 12.1.1 and LittleCMS 2.17: ICC within two 8-bit levels.",
                "pikepdf and pyHanko: encryption and signature interoperability.",
            ]),
            ("Self-derived or analytic", Amber, "#FFFBEB", [
                "JPX subsampling fixtures are self-derived.",
                "Some overprint tests are self-consistent only.",
                "Timestamp unit tokens are self-built; no live TSA.",
                "Public-key encryption is round-trip tested only.",
                "U3D and PRC fixtures are synthetic.",
                "Page-group CMYK and ICC blending: analytic tests.",
            ]),
            ("Not run, or unreliable", Rose, "#FFF1F2", [
                "veraPDF was not run; PDF/A and PDF/UA rules were compared by hand.",
                "New page-group blending was not compared with Chrome or PDFium.",
                "CJK pixel parity: the Edge comparison was unreliable.",
                "Non-byte-aligned JBIG2 widths and nonzero RDW/RDH: no reference.",
                "Non-default 12-bit DAC: untested.",
            ]),
        ];
        var ch = 0.0;
        foreach (var c in cols) ch = Math.Max(ch, 40 + c.Items.Sum(i => ParaH(_t.Sans, 8.6, cw - 34, i, 11.8) + 5));
        for (var i = 0; i < 3; i++)
        {
            var x = M + i * (cw + 12);
            var col = cols[i];
            var cy2 = y - 12;
            Art(() => { Box(_pg, x, cy2 - ch, cw, ch, fill: col.Bg, stroke: Mist, r: 10); Box(_pg, x, cy2 - 3, cw, 3, fill: col.C, r: 1.5); });
            var list = new Node("L", _sec);
            T("H3", () => Tx(_t.SansB, 9.8, x + 12, cy2 - 19, col.Head, col.C == Amber ? "#78350F" : col.C));
            var yy = cy2 - 34;
            foreach (var item in col.Items)
            {
                var li = new Node("LI", list);
                var lbl = new Node("Lbl", li);
                var body = new Node("LBody", li);
                var y0 = yy;
                Mc(lbl, () => Dot(x + 15, y0 + 3, 1.8, col.C));
                Mc(body, () => Para(_t.Sans, 8.6, x + 24, y0, cw - 34, item, Ink, 11.8));
                yy -= ParaH(_t.Sans, 8.6, cw - 34, item, 11.8) + 5;
            }
        }
        Evidence(AddedChips(y - 12 - ch - 18), "docs/spec-matrix.md and docs/spec-backlog.md", "read at aa127037", "Not a measurement: a count of status words in two markdown tables.",
            "Status words are the repository's own. Counted: the parser table (14 rows), the renderer table (10 rows), and the one Partial and two limited-Done rows of the editor table. Test counts are recorded per checkpoint in the matrix and are not restated here.");
    }

    // ---- 15 / 16: tables ---------------------------------------------------------------------------------------------------------------

    /// <summary>Chips for work integrated recently that did not change any row's status (docs/spec-matrix.md, integration batches 2 and 3). Returns the y below them.</summary>
    double AddedChips(double top)
    {
        string[] added =
        [
            "ICC v4 black point, B2A tables", "Coons and tensor patches", "Arbitrary-path redaction", "Annotation authoring API", "AcroForm authoring and flatten",
            "Fallback fonts, CJK chunks", "DeviceCMYK table", "Page-level undo and redo", "Worker-pool band rendering", "Inert U3D, PRC, RichMedia models",
        ];
        SubHeading(top, "Landed recently; no Partial row was promoted", Amber);
        double x = M, y = top - 18;
        T("P", () =>
        {
            foreach (var a in added)
            {
                var w = _t.Sans.Width(a, 8.4) + 18;
                if (x + w > M + W) { x = M; y -= 23; }
                Pill(x, y - 14, w, 17, a, White, Ink, 8.4, _t.Sans, Line_);
                x += w + 6;
            }
        });
        return y - 30;
    }

    // ---- 15 / 16: tables (cont.) -------------------------------------------------------------------------------------------------------

    static readonly Col[] SpecCols = [new("Clause", 56, true), new("Area", 150, true), new("Status", 54, false, true), new("Honest caveat", W - 260, false, false, 8)];

    void SpecParserPage()
    {
        var y = ContentPage("Part 3  /  Spec parity", Amber, "Parser and file structure",
            "Clause by clause, in the order of the matrix. Done means implemented and covered by tests, never that every corner of the clause is proven.",
            "Spec parity: parser");
        var yb = DataTable(M, y - 12, SpecCols, ParserRows, "ISO 32000-2 parser and file-structure rows: clause, area, status and the honest caveat.", 22);
        T("Caption", () => Para(_t.SansI, 8.6, M, yb - 14, W, "Source: docs/spec-matrix.md, Parser table, read at commit aa127037. Caveats are condensed; the matrix and docs/spec-backlog.md hold the full text.", Slate, 12));
    }

    void SpecRendererPage()
    {
        var y = ContentPage("Part 3  /  Spec parity", Amber, "Renderer and editor",
            "Eight of ten renderer rows are Partial. The editor's two Done rows are bounded APIs, marked Done* because their limits are part of the status.",
            "Spec parity: renderer and editor");
        var yb = DataTable(M, y - 12, SpecCols, [.. RendererRows, .. EditorRows], "ISO 32000-2 renderer and editor rows: clause, area, status and the honest caveat.", 22);
        T("Caption", () => Para(_t.SansI, 8.6, M, yb - 14, W, "Done* = Done for the stated bounded scope only. Source: docs/spec-matrix.md (Renderer and Editor tables), read at commit aa127037.", Slate, 12));
    }

    // ---- 17: method -----------------------------------------------------------------------------------------------------------------------

    void MethodPage()
    {
        var y = ContentPage("Method", Slate, "Method and provenance",
            "Where each figure came from, how this file was made, and what it cannot tell you.",
            null, "Method and provenance");
        y -= 12;
        SubHeading(y, "Sources", IndigoD);
        string[][] src =
        [
            ["bench/FreeDF.Bench/results/2026-10-30.md", "BenchmarkDotNet tables: open, extract, render stages", "2026-10-30"],
            ["bench/FreeDF.Bench/README.md", "Corpus, preflight, measurement boundaries", "2026-10-30"],
            ["docs/performance/2026-10-30.md", "Editor before/after, heartbeat, budgets", "2026-10-30"],
            ["docs/performance/2026-10-30-aot.md", "JIT versus AOT, download size, goal misses", "2026-10-30"],
            ["docs/performance/2026-10-30-aot-budgets.json", "The six strict-goal misses", "2026-10-30"],
            ["docs/performance/2026-10-30-worker-pool.md", "Worker pool and band rendering", "2026-10-30"],
            ["docs/spec-matrix.md, docs/spec-backlog.md", "Parity rows and caveats", "aa127037"],
            ["tools/FreeDF.EditorWeb/wwwroot (surface.css, index.html)", "FreeDF palette and F mark", "Current source"],
            ["samples/_gen/fonts/LICENSES.txt", "Font licences", "aa127037"],
        ];
        var yb = DataTable(M, y - 8, [new("File", 232, true, false, 8), new("Used for", W - 294, false, false, 8), new("Version", 62, false, false, 8)], src,
            "Source files used for this document, what each provided and its date or commit.", 20);
        y = yb - 24;
        SubHeading(y, "How this file was made", IndigoD);
        string[] made =
        [
            "Written by tools/FreeDF.Samples (Gen28) with this repository's own lexer, writer and embedded-font subsetter. Charts are vector paths, shadings and transparency; there are no images and no external references.",
            "Tagged: a structure tree with headings, paragraphs, lists, tables with header scope, figures with alternative text and artifacts for page furniture; document language, title, outline and tab order are set.",
            "Fonts embedded as subsets: Source Serif 4, Source Sans 3, Source Code Pro (SIL OFL 1.1) and Bitstream Vera Sans. Regeneration is byte-identical.",
            "Rendered with this repository's renderer and compared with Chromium through tools/FreeDF.RenderCompare during development.",
        ];
        var list = new Node("L", _sec);
        var yy = y - 22;
        foreach (var m in made)
        {
            var li = new Node("LI", list);
            var lbl = new Node("Lbl", li);
            var body = new Node("LBody", li);
            var y0 = yy;
            Mc(lbl, () => Dot(M + 4, y0 + 3.5, 2.2, Indigo));
            Mc(body, () => Para(_t.Sans, 9, M + 16, y0, W - 20, m, Ink, 12.6));
            yy -= ParaH(_t.Sans, 9, W - 20, m, 12.6) + 7;
        }
        y = yy - 10;
        SubHeading(y, "What this document cannot tell you", Rose);
        string[] limits =
        [
            "Every benchmark comes from one development machine (Windows 11, Core i9-10900) and from reports dated 2026-10-30. Results do not transfer to other hardware, corpora or runtimes.",
            "The PDF/UA check used here is this repository's heuristic validator. It is not veraPDF, not Matterhorn, and not certification.",
            "The brand pages are design exploration. No name, mark or palette has been chosen, vetted or approved.",
            "Parity percentages are deliberately not given: rows are not equally large, and Partial rows differ widely in how much remains.",
        ];
        var h = 26 + limits.Sum(l => ParaH(_t.Sans, 9, W - 40, l, 12.6) + 6);
        Art(() => { Box(_pg, M, y - 8 - h, W, h, fill: "#FFF1F2", stroke: "#FDA4AF", r: 9); Box(_pg, M, y - 8 - h + 6, 3.5, h - 12, fill: Rose, r: 1.75); });
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + 16, y - 24, "LIMITS", Rose, tracking: 1.4);
            var ly = y - 38;
            foreach (var l in limits) ly = Para(_t.Sans, 9, M + 16, ly, W - 40, l, Ink, 12.6) - 6;
        });
        var cmd = "dotnet run --project tools/FreeDF.Samples -- generate samples 28-";
        T("P", () =>
        {
            Tx(_t.SansB, 7.4, M, 70, "REGENERATE", Slate, tracking: 1.3);
            Tx(_t.Mono, 8.2, M, 57, cmd, IndigoD);
        });
    }
}
