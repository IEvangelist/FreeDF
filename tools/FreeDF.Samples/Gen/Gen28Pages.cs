using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

sealed partial class Gen28
{
    static (double Min, double Max) Range(IEnumerable<Pair> rows) => (rows.Min(r => r.Theirs / r.Ours), rows.Max(r => r.Theirs / r.Ours));

    // ---- 1: cover --------------------------------------------------------------------------------------------------------------------

    void CoverPage()
    {
        NewPage("Cover");
        Outline("Cover", 0);
        Color lav = "#C7D2FE", mint = "#5EEAD4", sun = "#FCD34D", soft = "#CBD5E1";
        Art(() =>
        {
            Background(_pg, Night);
            _pg.Save(); _pg.Rect(0, 0, PW, PH); _pg.Clip();
            _pg.Sh(Axial(0, PH, PW, 0, [(0, "#1E1B4B"), (0.55, Night), (1, "#042F2E")]));
            _pg.Restore();
            _pg.Save(); _pg.Gs(("ca", 0.55)); _pg.Rect(0, 0, PW, PH); _pg.Clip();
            _pg.Sh(Radial(PW * 0.82, PH * 0.60, 0, PW * 0.82, PH * 0.60, 300, [(0, Indigo), (1, Night)], false));
            _pg.Restore();
            _pg.Save(); _pg.Gs(("ca", 0.40)); _pg.Rect(0, 0, PW, PH); _pg.Clip();
            _pg.Sh(Radial(PW * 0.12, PH * 0.24, 0, PW * 0.12, PH * 0.24, 260, [(0, Teal), (1, Night)], false));
            _pg.Restore();

            // rings: a decorative nod to the donut and gauge charts inside
            double cx = PW * 0.72, cy = 335;
            (double R, double Sweep, Color C)[] rings = [(132, 300, TealL), (104, 240, Violet), (76, 170, AmberL)];
            foreach (var (r, sweep, c) in rings)
            {
                _pg.Save(); _pg.Stroke(White); _pg.Gs(("CA", 0.07)); _pg.Width(14); _pg.Circle(cx, cy, r); _pg.StrokePath(); _pg.Restore();
                _pg.Save(); _pg.Stroke(c); _pg.Width(14); _pg.Cap(1); _pg.ArcPath(cx, cy, r, 90, 90 - sweep, true); _pg.StrokePath(); _pg.Restore();
            }
            // faint horizon lines behind the ring
            for (var i = 0; i < 5; i++) { _pg.Save(); _pg.Gs(("CA", 0.05)); Line(0, 200 + i * 42, PW, 200 + i * 42, "#FFFFFF", 0.6); _pg.Restore(); }
        });

        T("P", () => Tx(_t.SansB, 9, 48, PH - 70, "FREEDF  /  SAMPLE 28", mint, tracking: 2.2));
        T("H1", () =>
        {
            Tx(_t.SerifB, 64, 44, PH - 150, "Measured.", lav);
            Tx(_t.SerifB, 64, 44, PH - 218, "Named.", mint);
            Tx(_t.SerifB, 64, 44, PH - 286, "Mapped.", sun);
        });
        T("P", () => Para(_t.Sans, 15, 48, PH - 330, 330,
            "FreeDF's benchmark results drawn as vector charts, its official public identity alongside archived brand studies, and an honest map of ISO 32000-2 parity.", soft, 21));

        var (omin, omax) = Range(Open);
        var (emin, emax) = Range(OpenExtract);
        var jitLarge = AotRows.Single(a => a.Name.StartsWith("Large uncached 240"));
        var chips = new (string Big, string Small, Color C)[]
        {
            ($"{Num(omin, "0.00")}\u2013{Num(omax, "0.00")}\u00D7", "open vs PdfPig, 6 documents", lav),
            ($"{Num(jitLarge.Jit50 / jitLarge.Aot50, "0.00")}\u00D7", "AOT fresh 240-dpi render, editor", mint),
            ("15 / 0", "Partial / Todo spec rows", sun),
        };
        var list = new Node("L", _sec);
        double cyChip = 410;
        foreach (var (big, small, c) in chips)
        {
            var li = new Node("LI", list);
            var lbl = new Node("Lbl", li);
            var body = new Node("LBody", li);
            var yy = cyChip;
            Mc(lbl, () => Dot(52, yy + 8, 3, c));
            Mc(body, () =>
            {
                Tx(_t.SerifB, 25, 64, yy, big, c);
                Tx(_t.Sans, 10.5, 64, yy - 17, small, soft);
            });
            cyChip -= 56;
        }
        // part cards
        var cw = (W - 24) / 3;
        (string No, string Title, string Line, Color C)[] parts =
        [
            ("01", "Benchmarks", "Library vs PdfPig, renderer stages, editor before / after, AOT, worker pool.", mint),
            ("02", "FreeDF brand", "Official name and mark, palettes with contrast ratios, archived studies.", lav),
            ("03", "Spec parity", "ISO 32000-2 area by area: Done, Partial, and what is still open.", sun),
        ];
        for (var i = 0; i < 3; i++)
        {
            var x = 48 + i * (cw + 12);
            Art(() =>
            {
                _pg.Save(); _pg.Gs(("ca", 0.09)); Box(_pg, x, 66, cw, 104, fill: White, r: 12); _pg.Restore();
                _pg.Save(); _pg.Gs(("CA", 0.28)); Box(_pg, x, 66, cw, 104, stroke: White, r: 12); _pg.Restore();
                Box(_pg, x + 14, 142, 22, 3, fill: parts[i].C, r: 1.5);
            });
            T("H2", () => Tx(_t.SerifB, 16, x + 14, 118, parts[i].Title, parts[i].C));
            T("P", () =>
            {
                Tx(_t.SansB, 8, x + 14, 152, parts[i].No, parts[i].C, tracking: 1.4);
                Para(_t.Sans, 9, x + 14, 100, cw - 28, parts[i].Line, soft, 12.5);
            });
        }
        T("P", () => Tx(_t.Sans, 8, 48, 40, "Exploratory edition. Figures from checked-in reports dated 2026-10-30. Built with this repository's own PDF writer.", "#94A3B8"));
    }

    // ---- 2: read first --------------------------------------------------------------------------------------------------------------

    void KpiCard(double x, double y, double w, double h, string big, string label, Color accent)
    {
        Art(() =>
        {
            Shadow(x, y, w, h, 12, 0.08, 2, 5);
            Card(x, y, w, h, White, 12);
            Box(_pg, x + 14, y + h - 16, 22, 3, fill: accent, r: 1.5);
        });
        T("P", () =>
        {
            var bs = 23.0; while (_t.SerifB.Width(big, bs) > w - 24) bs -= 0.5;
            Tx(_t.SerifB, bs, x + 14, y + h - 46, big, accent == AmberL ? Amber : accent);
            Para(_t.Sans, 8.8, x + 14, y + h - 62, w - 26, label, Slate, 11.6);
        });
    }

    void ReadFirstPage()
    {
        var y = ContentPage("Start here", Indigo, "Read this first",
            "Three questions, each answered with figures that can be traced to a file in this repository: how fast is it, what might it look like, and how much of PDF 2.0 does it cover?",
            null, "Read this first");
        y -= 14;
        var (omin, omax) = Range(Open);
        var (emin, emax) = Range(OpenExtract);
        var big = AotRows.Single(a => a.Name.StartsWith("Large uncached 240"));
        var kw = (W - 30) / 4;
        var kh = 106;
        (string Big, string Label, Color C)[] kpis =
        [
            ($"{Num(omin, "0.00")}\u2013{Num(omax, "0.00")}\u00D7", "faster to open than PdfPig 0.1.16, across six documents (JIT, means)", Indigo),
            ($"{Num(emin, "0.00")}\u2013{Num(emax, "0.00")}\u00D7", "faster to open and extract all text; PdfPig allocates less on one of six", Violet),
            ($"{Num(big.Jit50 / big.Aot50, "0.00")}\u00D7", $"faster fresh 240-dpi page in the editor with AOT ({Num(big.Jit50, "0")} \u2192 {Num(big.Aot50, "0")} ms p50)", Teal),
            ("15 / 0", "Partial / Todo rows in the ISO 32000-2 matrix, plus two limited Done rows", AmberL),
        ];
        for (var i = 0; i < 4; i++) KpiCard(M + i * (kw + 10), y - kh, kw, kh, kpis[i].Big, kpis[i].Label, kpis[i].C);
        y -= kh + 22;

        // what did not improve
        var missText = "Not good enough yet: the large fresh 240-dpi render did not improve in the first checkpoint (3440 \u2192 3524 ms). Six strict interaction goals were still missed in the AOT run and three in the latest integrated profile. The worker pool is opt-in.";
        var mh = ParaH(_t.Sans, 9.5, W - 28, missText, 13.5) + 22;
        Art(() => { Box(_pg, M, y - mh, W, mh, fill: "#FFF1F2", stroke: "#FDA4AF", r: 9); Box(_pg, M, y - mh, 4, mh, fill: Rose); });
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + 16, y - 14, "WHERE IT FALLS SHORT", Rose, tracking: 1.4);
            Para(_t.Sans, 9.5, M + 16, y - 28, W - 32, missText, Ink, 13.5);
        });
        y -= mh + 24;

        // contents (left) and ground rules (right)
        var colW = 214;
        SubHeading(y, "Contents");
        var toc = new Node("L", _sec);
        (string Pg, string Title)[] entries =
        [
            ("3", "Library: open and extract"), ("4", "Library: extract and allocations"), ("5", "Library: render stages"),
            ("6", "Editor: before and after"), ("7", "Editor: JIT vs AOT"), ("8", "Editor: goals vs reality"), ("9", "Editor: worker pool"),
            ("10", "Brand: names and marks"), ("11", "Brand: Caret due diligence"), ("12", "Brand: palettes and contrast"), ("13", "Brand: type pairings"), ("14", "Brand: mock applications"),
            ("15", "Spec parity: overview"), ("16", "Spec parity: parser"), ("17", "Spec parity: renderer, editor"), ("18", "Method and provenance"),
        ];
        var ty = y - 24;
        foreach (var (pg, title) in entries)
        {
            var li = new Node("LI", toc);
            var lbl = new Node("Lbl", li);
            var body = new Node("LBody", li);
            var yy = ty;
            Mc(lbl, () => Tx(_t.SansB, 8.6, M + 18, yy, pg, Indigo, 'r'));
            Mc(body, () => Tx(_t.Sans, 9.2, M + 28, yy, title, Ink));
            ty -= 17;
        }

        var rx = M + colW + 26;
        var rw = W - colW - 26;
        T("H2", () => Tx(_t.SansB, 13, rx, y, "Ground rules for every page", IndigoD));
        (string Head, string Body)[] rules =
        [
            ("Traceable", "Each chart carries a strip naming its source file, run date, hardware and build mode."),
            ("Misses stay visible", "Unmet goals are drawn in rose; slower results point right, not hidden."),
            ("No favourable rounding", "Values are as reported. Ratios are computed from reported means, to two decimals."),
            ("FreeDF is the name", "FreeDF names the editor and its tools. The repository path and PortableDocument APIs stay stable."),
            ("Parity is not complete", "Partial rows stay Partial here. Validators are heuristic, not certification."),
        ];
        var ry = y - 22;
        foreach (var (head, body) in rules)
        {
            var bh = ParaH(_t.Sans, 9, rw - 24, body, 12.4) + 24;
            Art(() => { Box(_pg, rx, ry - bh, rw, bh, fill: White, stroke: Mist, r: 8); Box(_pg, rx, ry - bh + 6, 3, bh - 12, fill: Indigo, r: 1.5); });
            var by = ry;
            T("P", () =>
            {
                Tx(_t.SansB, 9.6, rx + 14, by - 15, head, Ink);
                Para(_t.Sans, 9, rx + 14, by - 28, rw - 26, body, Slate, 12.4);
            });
            ry -= bh + 8;
        }

        // colour key
        var ky = Math.Min(ty, ry) - 8;
        var kh2 = 70.0;
        Art(() => Card(M, ky - kh2, W, kh2, White, 10));
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + 14, ky - 16, "COLOUR KEY USED IN EVERY CHART", IndigoD, tracking: 1.4);
            (string Name, Color C)[] key =
            [
                ("PortableDocument / candidate", Indigo), ("PdfPig / before / non-AOT", Pig), ("AOT / faster", Teal),
                ("Miss / slower", Rose), ("Goal met", Emerald), ("Evidence strip", "#C7D2FE"),
            ];
            for (var i = 0; i < key.Length; i++)
            {
                var kx = M + 14 + (i % 3) * (W - 28) / 3;
                var kyy = ky - 34 - (i / 3) * 18;
                Box(_pg, kx, kyy - 1, 9, 9, fill: key[i].C, r: 2);
                Tx(_t.Sans, 8.8, kx + 15, kyy, key[i].Name, Ink);
            }
        });
    }

    // ---- 3: library open and extract -------------------------------------------------------------------------------------------

    IReadOnlyList<BarRow> PairRows(IReadOnlyList<Pair> src) => src.Select(p => new BarRow(p.Corpus, null, p.Theirs, FmtUs(p.Theirs), p.Ours, FmtUs(p.Ours), Ratio(p.Theirs / p.Ours), "#E0E7FF", IndigoD)).ToList();

    void LibraryOpenPage()
    {
        var y = ContentPage("Part 1  /  Benchmarks  /  library", Indigo, "Opening and reading documents",
            "The same PDF bytes go to PortableDocument and to PdfPig 0.1.16. Before timing, both must agree on page counts and decoded text. Bars compare engines inside a row, not corpora.",
            "Library: open and extract", "Part 1: Benchmarks");
        y -= 12;
        SubHeading(y, "Open a document and read its page count");
        var openRows = PairRows(Open);
        var yb = 0.0;
        Fig("Paired bar chart of the time to open six documents with PortableDocument and with PdfPig 0.1.16. PortableDocument is faster on every document: 14.36 versus 45.66 microseconds on 03 typography (3.18 times), 28.15 versus 70.27 on 06 patterns (2.50), 11.39 versus 40.46 on 09 images (3.55), 7.36 versus 30.44 on image.pdf (4.14), 18.78 versus 53.01 on text.pdf (2.82) and 12.96 versus 39.98 on vector.pdf (3.09).",
            () => yb = PairedBars(M, y - 6, W, openRows, ("PdfPig 0.1.16", Pig), ("PortableDocument", Indigo), 33));
        FigureCaption(M, yb - 12, "Figure 1. Mean time to open and read the page count, microseconds. Pill: PdfPig mean divided by PortableDocument mean.");
        y = yb - 44;
        SubHeading(y, "Open and extract every page's text");
        Fig("Paired bar chart of the time to open a document and extract all text. PortableDocument versus PdfPig: 3.48 versus 12.40 milliseconds on 03 typography (3.56 times), 0.83 versus 2.85 on 06 patterns (3.41), 1.38 versus 7.35 on 09 images (5.33), 86.8 versus 404.0 microseconds on image.pdf (4.65), 4.72 versus 12.45 milliseconds on text.pdf (2.64) and 5.80 versus 7.65 on vector.pdf (1.32).",
            () => yb = PairedBars(M, y - 6, W, PairRows(OpenExtract), ("PdfPig 0.1.16", Pig), ("PortableDocument", Indigo), 33));
        FigureCaption(M, yb - 12, "Figure 2. Mean time for a fresh open plus all-page text extraction and materialisation. The vector.pdf gap is the smallest, 1.32\u00D7.");
        Evidence(yb - 34, "bench/FreeDF.Bench/results/2026-10-30.md (PdfOpenBench, TextExtractionBench)", "2026-10-30", BenchHw,
            BenchMode + " Same byte[] to both engines; preflight checked page counts and normalised text. Results hold for this corpus and machine only.");
    }

    // ---- 4: extract on open documents and allocation ---------------------------------------------------------------------------------------

    void LibraryReadPage()
    {
        var y = ContentPage("Part 1  /  Benchmarks  /  library", Indigo, "Reading an open document",
            "Whiskers show the 99.9% confidence half-width BenchmarkDotNet reports. Where a whisker is wide, the ranking is weaker than the mean suggests, and the chart says so.",
            "Library: extract and allocations");
        y -= 12;
        SubHeading(y, "Extract text from a document that is already open (log scale)");
        var yb = 0.0;
        (double, string)[] ticks = [(100, "100 \u00B5s"), (1000, "1 ms"), (10000, "10 ms")];
        Fig("Dot plot on a logarithmic time axis comparing text extraction from an already open document. PortableDocument versus PdfPig: 4.42 versus 15.37 milliseconds on 03 typography (3.47 times), 2.20 versus 4.68 on 06 patterns (2.13, with a wide error of 32 percent), 2.79 versus 5.76 on 09 images (2.06), 181 versus 264 microseconds on image.pdf (1.46), 4.44 versus 10.80 milliseconds on text.pdf (2.43) and 5.68 versus 7.81 on vector.pdf (1.37).",
            () => yb = DotPlotLog(M, y - 6, W, Extract, ("PortableDocument", Indigo), ("PdfPig 0.1.16", Pig), 60, 30000, ticks, "log scale; whiskers = 99.9% CI half-width"));
        FigureCaption(M, yb - 2, "Figure 3. Mean time and confidence interval per document, 32 fresh documents per iteration. Pill: PdfPig \u00F7 PortableDocument, from the means (BenchmarkDotNet's own Ratio column can differ).");
        y = yb - 34;
        SubHeading(y, "Managed allocation for open plus extract (not total process memory)");
        var rows = 0;
        _ = rows;
        Fig("Paired bar chart of managed allocation in kilobytes for open plus extract. PortableDocument allocates less than PdfPig on five of six documents: 7567 versus 11444 on 03 typography, 2044 versus 3754 on 06, 2952 versus 5258 on 09, 6293 versus 13952 on text.pdf and 4953 versus 5674 on vector.pdf. On image.pdf PdfPig allocates less: 467 versus 557 kilobytes.",
            () => yb = AllocBars(M, y - 6, W));
        FigureCaption(M, yb - 12, "Figure 4. Rose pills mark where PdfPig allocates less than PortableDocument (image.pdf, 0.84\u00D7).");
        Evidence(yb - 34, "bench/FreeDF.Bench/results/2026-10-30.md (OpenDocumentTextBench, TextExtractionBench)", "2026-10-30", BenchHw,
            BenchMode + " Already-open cases warn about short iterations (minimum 4.95 to 66.6 ms) and multimodal distributions; small differences are not ranked.");
    }

    double AllocBars(double x, double top, double w)
    {
        const double labelW = 150, rh = 24, valW = 58, pillW = 52;
        var barW = w - labelW - valW - pillW - 10;
        Legend(x + labelW, top - 8, ("PdfPig 0.1.16", Pig, false), ("PortableDocument", Indigo, false));
        var y = top - 20;
        for (var i = 0; i < OpenExtract.Length; i++)
        {
            var r = OpenExtract[i];
            if (i % 2 == 0) Box(_pg, x, y - rh, w, rh, fill: White);
            var mid = y - rh / 2;
            Tx(_t.SansB, 8.6, x + 8, mid - 3, r.Corpus, Ink);
            var max = Math.Max(r.OursAlloc, r.TheirsAlloc);
            var bx = x + labelW;
            var tl = r.TheirsAlloc / max * barW;
            var ol = r.OursAlloc / max * barW;
            Box(_pg, bx, mid + 0.5, tl, 6.5, fill: Pig, r: 1.5);
            Box(_pg, bx, mid - 7.5, ol, 6.5, fill: Indigo, r: 1.5);
            Tx(_t.Sans, 7.9, bx + tl + 5, mid + 0.8, Num(r.TheirsAlloc, "#,0") + " KB", Slate);
            Tx(_t.SansB, 7.9, bx + ol + 5, mid - 7.2, Num(r.OursAlloc, "#,0") + " KB", Ink);
            var ratio = r.TheirsAlloc / r.OursAlloc;
            Pill(x + w - pillW - 4, mid - 8, pillW, 16, Ratio(ratio), ratio >= 1 ? "#E0E7FF" : "#FFE4E6", ratio >= 1 ? IndigoD : "#881337", 8.2);
            y -= rh;
        }
        return y;
    }

    // ---- 5: render stages -----------------------------------------------------------------------------------------------------------

    void LibraryRenderPage()
    {
        var y = ContentPage("Part 1  /  Benchmarks  /  library", Indigo, "Where rendering time goes",
            "The legacy render benchmark has no second engine, so this page makes no speed comparison. It shows stage costs, and why a rasteriser dominates a text page.",
            "Library: render stages");
        y -= 12;
        SubHeading(y, "Stage costs on a log scale");
        var yb = 0.0;
        (double, string)[] ticks = [(10, "10 \u00B5s"), (100, "100 \u00B5s"), (1000, "1 ms"), (10000, "10 ms"), (100000, "100 ms"), (1000000, "1 s")];
        Fig("Lollipop chart on a logarithmic axis of seven stage costs. Parse text.pdf 18.8 microseconds; JPEG baseline decode 8.56 milliseconds; JPEG progressive decode 17.16 milliseconds; interpret text with no raster 3.48 milliseconds; render image.pdf at 96 dpi 23.40 milliseconds; render text.pdf 115.92 milliseconds; render vector.pdf 260.20 milliseconds.",
            () => yb = Lollipops(M, y - 8, W, Stages, 10, 1000000, ticks));
        FigureCaption(M, yb - 4, "Figure 5. Mean per operation; whiskers are confidence half-widths (mostly smaller than the marker). Right: mean, \u00B1error and managed KB.");
        y = yb - 40;

        var mid = W / 2 - 8;
        var interp = Stages.Single(s => s.Name.StartsWith("Interpret"));
        var text = Stages.Single(s => s.Name.StartsWith("Render text"));
        var vec = Stages.Single(s => s.Name.StartsWith("Render vector"));
        var prog = Stages.Single(s => s.Name.StartsWith("JPEG prog"));
        var basel = Stages.Single(s => s.Name.StartsWith("JPEG base"));
        string[] reads =
        [
            $"Interpreting text.pdf with a null backend takes {Num(interp.Mean / 1000, "0.00")} ms; rendering it takes {Num(text.Mean / 1000, "0.0")} ms. Interpretation is {Num(interp.Mean / text.Mean * 100, "0.0")}% of the render.",
            $"Vector rendering is {Num(vec.Mean / text.Mean, "0.00")}\u00D7 the text render and allocates {Num(vec.AllocKb / 1024, "0.0")} MB per operation.",
            $"Progressive JPEG decodes {Num(prog.Mean / basel.Mean, "0.00")}\u00D7 slower than baseline on these photo fixtures.",
        ];
        string[] limits =
        [
            "No renderer comparison: PdfPig has no raster renderer in this harness.",
            "The job label reads ShortRun, but the report shows 6 warm-ups and 15 one-second iterations.",
            "No checked-in historical run exists, so these are a new baseline, not a speed-up.",
            "The fixtures are small: results do not generalise to arbitrary pages.",
        ];
        var h = 24 + Math.Max(reads.Sum(r => ParaH(_t.Sans, 9, mid - 28, r, 12.4) + 6), limits.Sum(r => ParaH(_t.Sans, 9, mid - 28, r, 12.4) + 6)) + 10;
        Art(() =>
        {
            Card(M, y - h, mid, h, White, 10); Box(_pg, M, y - h + 8, 3, h - 16, fill: Indigo, r: 1.5);
            Card(M + mid + 16, y - h, mid, h, "#FFF1F2", 10); Box(_pg, M + mid + 16, y - h + 8, 3, h - 16, fill: Rose, r: 1.5);
        });
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + 16, y - 15, "WHAT THE NUMBERS SAY", IndigoD, tracking: 1.4);
            var yy = y - 30;
            foreach (var r in reads) yy = Para(_t.Sans, 9, M + 16, yy, mid - 28, r, Ink, 12.4) - 6;
        });
        T("P", () =>
        {
            Tx(_t.SansB, 8, M + mid + 32, y - 15, "WHAT THEY DO NOT SAY", Rose, tracking: 1.4);
            var yy = y - 30;
            foreach (var r in limits) yy = Para(_t.Sans, 9, M + mid + 32, yy, mid - 28, r, Ink, 12.4) - 6;
        });
        Evidence(y - h - 16, "bench/FreeDF.Bench/results/2026-10-30.md (RenderBench) and bench/FreeDF.Bench/Program.cs", "2026-10-30", BenchHw,
            BenchMode + " Text/Vector/Image render every page of bench/fixtures/{text,vector,image}.pdf at 96 dpi including document load.");
    }
}
