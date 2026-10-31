using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

/// <summary>
/// Sample 28: a designed, tagged showcase of what the repository has measured, what it might look like, and how much of ISO 32000-2 it covers.
/// Three parts (benchmarks, brand directions, spec parity) drawn entirely with this repo's writer: vector charts, shadings, transparency groups,
/// embedded OpenType subsets, a logical structure tree, outlines and alternative text. It is deliberately kept out of the 27 collection (01-26).
/// </summary>
sealed partial class Gen28
{
    public const string Out = "28-benchmarks-brand-spec.pdf";

    // Palette. Text colours are chosen so every label clears WCAG AA (4.5:1) on the surface it sits on; see Contrast().
    static readonly Color Ink = "#0F172A", Slate = "#475569", Paper = "#F8FAFC", White = "#FFFFFF", Mist = "#E2E8F0";
    static readonly Color Indigo = "#4F46E5", IndigoD = "#312E81", Violet = "#7C3AED", Teal = "#0F766E", TealL = "#14B8A6";
    static readonly Color Amber = "#B45309", AmberL = "#F59E0B", Rose = "#BE123C", Emerald = "#047857", Pig = "#64748B";
    static readonly Color Night = "#0B1020";

    const double PW = 595.276, PH = 841.89, M = 48, W = PW - 2 * M;

    sealed class Node
    {
        public string Role { get; }
        public string? Alt { get; }
        public string Attrs { get; }
        public List<object> Kids { get; } = [];
        public int Ref { get; set; }

        public Node(string role, Node? parent = null, string? alt = null, string attrs = "")
        {
            Role = role; Alt = alt; Attrs = attrs;
            parent?.Kids.Add(this);
        }
    }

    sealed record OutlineItem(string Title, Page Page, int Level);

    readonly Doc _d;
    readonly Faces _t;
    readonly List<Page> _pages = [];
    readonly Dictionary<Page, int> _mc = [];
    readonly Dictionary<Page, Dictionary<int, Node>> _parent = [];
    readonly Node _root = new("Document");
    readonly List<OutlineItem> _outline = [];
    readonly EmbFont _vera, _veraB;
    Page _pg = null!;
    Node _sec = null!;

    Gen28()
    {
        _d = new Doc("Measured, named, mapped: csharp-pdf benchmarks, brand directions and ISO 32000-2 parity",
            author: "csharp-pdf contributors",
            subject: "Benchmark charts from the checked-in 2026-10-30 reports, exploratory brand directions and the current ISO 32000-2 coverage matrix. Parity is not complete.",
            keywords: "benchmarks, branding, ISO 32000-2, PDF/UA, tagged PDF, csharp-pdf, PortableDocument",
            lang: "en-US");
        _t = new Faces(_d);
        _vera = _d.Font("Vera.ttf");
        _veraB = _d.Font("VeraBd.ttf");
    }

    public static void Build(string path)
    {
        var g = new Gen28();
        g.Compose();
        g.Finish(path);
    }

    void Compose()
    {
        CoverPage();
        ReadFirstPage();
        LibraryOpenPage();
        LibraryReadPage();
        LibraryRenderPage();
        EditorDeltaPage();
        AotPage();
        GoalsPage();
        PoolPage();
        BrandNamesPage();
        BrandCaretPage();
        BrandPalettePage();
        BrandTypePage();
        BrandApplicationsPage();
        SpecOverviewPage();
        SpecParserPage();
        SpecRendererPage();
        MethodPage();
    }

    // ---- tagging -----------------------------------------------------------------------------------------------------------------

    Page NewPage(string sectionTitle)
    {
        _pg = _d.Page(PW, PH);
        _pages.Add(_pg);
        _sec = new Node("Sect", _root);
        return _pg;
    }

    void Mc(Node node, Action draw)
    {
        var m = _mc.GetValueOrDefault(_pg);
        _mc[_pg] = m + 1;
        _pg.Raw($"/{node.Role} << /MCID {m} >> BDC");
        draw();
        _pg.Raw("EMC");
        node.Kids.Add((_pg, m));
        if (!_parent.TryGetValue(_pg, out var pm)) _parent[_pg] = pm = [];
        pm[m] = node;
    }

    /// <summary>Marked-content leaf under the current section (or under <paramref name="parent"/>).</summary>
    Node T(string role, Action draw, Node? parent = null, string? alt = null, string attrs = "")
    {
        var n = new Node(role, parent ?? _sec, alt, attrs);
        Mc(n, draw);
        return n;
    }

    void Art(Action draw, string type = "Layout")
    {
        _pg.Raw($"/Artifact << /Type /{type} >> BDC");
        draw();
        _pg.Raw("EMC");
    }

    void Outline(string title, int level) => _outline.Add(new OutlineItem(title, _pg, level));

    void Finish(string path)
    {
        // structure tree
        var strRoot = _d.New();
        Alloc(_root);
        Emit(_root, strRoot);
        var nums = new List<string>();
        for (var i = 0; i < _pages.Count; i++)
        {
            var pg = _pages[i];
            pg.Extra += $" /StructParents {i} /Tabs /S";
            var pm = _parent.GetValueOrDefault(pg) ?? [];
            var arr = string.Join(" ", Enumerable.Range(0, _mc.GetValueOrDefault(pg)).Select(m => $"{pm[m].Ref} 0 R"));
            nums.Add($"{i} [{arr}]");
        }
        _d.Put(strRoot, $"<< /Type /StructTreeRoot /K {_root.Ref} 0 R /ParentTree << /Nums [{string.Join(" ", nums)}] >> /ParentTreeNextKey {_pages.Count} >>");

        // outline (two levels)
        var items = _outline.Select(o => (Item: o, Ref: _d.New())).ToList();
        var outlines = _d.New();
        var top = new List<int>();
        for (var i = 0; i < items.Count; i++) if (items[i].Item.Level == 0) top.Add(i);
        for (var ti = 0; ti < top.Count; ti++)
        {
            var i = top[ti];
            var end = ti + 1 < top.Count ? top[ti + 1] : items.Count;
            var kids = Enumerable.Range(i + 1, end - i - 1).ToList();
            var parts = new List<string>
            {
                $"/Title {K.Utf16(items[i].Item.Title)}", $"/Parent {outlines} 0 R", Dest(items[i].Item.Page),
            };
            if (ti > 0) parts.Add($"/Prev {items[top[ti - 1]].Ref} 0 R");
            if (ti + 1 < top.Count) parts.Add($"/Next {items[top[ti + 1]].Ref} 0 R");
            if (kids.Count > 0) parts.Add($"/First {items[kids[0]].Ref} 0 R /Last {items[kids[^1]].Ref} 0 R /Count {kids.Count}");
            _d.Put(items[i].Ref, "<< " + string.Join(" ", parts) + " >>");
            for (var k = 0; k < kids.Count; k++)
            {
                var c = items[kids[k]];
                var cp = new List<string> { $"/Title {K.Utf16(c.Item.Title)}", $"/Parent {items[i].Ref} 0 R", Dest(c.Item.Page) };
                if (k > 0) cp.Add($"/Prev {items[kids[k - 1]].Ref} 0 R");
                if (k + 1 < kids.Count) cp.Add($"/Next {items[kids[k + 1]].Ref} 0 R");
                _d.Put(c.Ref, "<< " + string.Join(" ", cp) + " >>");
            }
        }
        _d.Put(outlines, $"<< /Type /Outlines /First {items[top[0]].Ref} 0 R /Last {items[top[^1]].Ref} 0 R /Count {items.Count} >>");

        var xmpTitle = System.Security.SecurityElement.Escape(_d.Title);
        var xmp = "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
            "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\">" +
            $"<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">{xmpTitle}</rdf:li></rdf:Alt></dc:title>" +
            "<dc:language><rdf:Bag><rdf:li>en-US</rdf:li></rdf:Bag></dc:language>" +
            "<dc:creator><rdf:Seq><rdf:li>csharp-pdf contributors</rdf:li></rdf:Seq></dc:creator>" +
            "<pdfuaid:part>1</pdfuaid:part></rdf:Description></rdf:RDF></x:xmpmeta>\n<?xpacket end=\"w\"?>";
        var xmpRef = _d.Stream("/Type /Metadata /Subtype /XML", System.Text.Encoding.UTF8.GetBytes(xmp), compress: false);

        _d.Cat.Add($"/StructTreeRoot {strRoot} 0 R /MarkInfo << /Marked true /Suspects false >> /ViewerPreferences << /DisplayDocTitle true >> /Outlines {outlines} 0 R /PageMode /UseOutlines /Metadata {xmpRef} 0 R");
        _d.Save(path, version: "1.7");
    }

    static string Dest(Page p) => $"/Dest [{p.Ref} 0 R /XYZ 0 {K.N(p.H)} 0]";

    void Alloc(Node nd)
    {
        nd.Ref = _d.New();
        foreach (var k in nd.Kids.OfType<Node>()) Alloc(k);
    }

    void Emit(Node nd, int parentRef)
    {
        var kids = new List<string>();
        Page? pg = null;
        foreach (var k in nd.Kids)
        {
            if (k is Node child) { Emit(child, nd.Ref); kids.Add($"{child.Ref} 0 R"); }
            else
            {
                var (p, m) = ((Page, int))k;
                kids.Add($"<< /Type /MCR /Pg {p.Ref} 0 R /MCID {m} >>");
                pg = p;
            }
        }
        var extra = "";
        if (nd.Alt != null) extra += $" /Alt {K.Utf16(nd.Alt)}";
        if (nd.Attrs != "") extra += $" /A {nd.Attrs}";
        var pgRef = pg != null ? $" /Pg {pg.Ref} 0 R" : "";
        _d.Put(nd.Ref, $"<< /Type /StructElem /S /{nd.Role} /P {parentRef} 0 R{pgRef}{extra} /K [{string.Join(" ", kids)}] >>");
    }

    // ---- drawing primitives -----------------------------------------------------------------------------------------------------

    static string Num(double v, string fmt = "0.##") => v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

    static double Lum(Color c)
    {
        static double L(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    /// <summary>WCAG 2.x contrast ratio between two colours.</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static void CheckGlyphs(IFont f, string s)
    {
        if (f is not EmbFont e) return;
        foreach (var ch in s)
            if (ch != ' ' && e.File.GlyphForCodePoint(ch) == 0) throw new InvalidOperationException($"U+{(int)ch:X4} '{ch}' is missing from the font used for \"{s}\"");
    }

    /// <summary>Single line of text anchored left, right or centre of <paramref name="x"/>.</summary>
    void Tx(IFont f, double size, double x, double y, string s, Color c, char anchor = 'l', double tracking = 0, Content? on = null)
    {
        CheckGlyphs(f, s);
        var w = f.Width(s, size, tracking == 0, tracking);
        var xx = anchor == 'r' ? x - w : anchor == 'c' ? x - w / 2 : x;
        (on ?? _pg).Text(f, size, xx, y, s, color: c, tracking: tracking, kern: tracking == 0);
    }

    double Para(IFont f, double size, double x, double y, double width, string text, Color c, double leading = 0, string align = "left")
    {
        CheckGlyphs(f, text.Replace("\n", " "));
        return Paragraph(_pg, f, size, x, y, width, text, leading == 0 ? size * 1.42 : leading, align, c);
    }

    double ParaH(IFont f, double size, double width, string text, double leading = 0) =>
        Wrap(f, size, text, width).Count * (leading == 0 ? size * 1.42 : leading);

    void Pill(double x, double y, double w, double h, string text, Color fill, Color fg, double size = 8, IFont? f = null, Color? stroke = null)
    {
        Box(_pg, x, y, w, h, fill: fill, stroke: stroke, r: h / 2);
        Tx(f ?? _t.SansB, size, x + w / 2, y + h / 2 - size * 0.34, text, fg, 'c');
    }

    static void Box(Content c, double x, double y, double w, double h, Color? fill = null, Color? stroke = null, double r = 0, double lw = 0.75) =>
        Theme.Box(c, x, y, w, h, fill, stroke, r, lw);

    void Shadow(double x, double y, double w, double h, double r, double alpha = 0.10, double dy = 3, double spread = 6)
    {
        for (var i = 3; i >= 1; i--)
        {
            _pg.Save();
            _pg.Gs(("ca", alpha / 3));
            Box(_pg, x - spread * i / 3, y - dy - spread * i / 3, w + 2 * spread * i / 3, h + 2 * spread * i / 3, fill: "#0F172A", r: r + spread * i / 3);
            _pg.Restore();
        }
    }

    void Card(double x, double y, double w, double h, Color? fill = null, double r = 10) =>
        Box(_pg, x, y, w, h, fill: fill ?? White, stroke: Mist, r: r);

    void Line(double x0, double y0, double x1, double y1, Color c, double w = 0.75, double[]? dash = null)
    {
        _pg.Save(); _pg.Stroke(c); _pg.Width(w);
        if (dash != null) _pg.Dash(dash);
        _pg.Move(x0, y0); _pg.Line(x1, y1); _pg.StrokePath(); _pg.Restore();
    }

    void Dot(double cx, double cy, double r, Color fill, Color? stroke = null)
    {
        _pg.Save(); _pg.Circle(cx, cy, r);
        if (stroke is { } s) { _pg.Fill(fill); _pg.Stroke(s); _pg.Width(1.2); _pg.FillStroke(); } else { _pg.Fill(fill); _pg.FillPath(); }
        _pg.Restore();
    }

    /// <summary>Clips to a rounded rectangle and paints an axial gradient through it.</summary>
    void GradRect(double x, double y, double w, double h, double r, Color a, Color b, bool diagonal = true)
    {
        _pg.Save();
        _pg.RRect(x, y, w, h, r); _pg.Clip();
        _pg.Sh(diagonal ? Axial(x, y + h, x + w, y, [(0, a), (1, b)]) : Axial(x, y, x, y + h, [(0, a), (1, b)]));
        _pg.Restore();
    }

    // ---- page chrome ------------------------------------------------------------------------------------------------------------

    /// <summary>Light content page: header strip, running head, title, standfirst, folio and footer. Returns the y at which content may start.</summary>
    double ContentPage(string part, Color partColour, string title, string lede, string? outlineTitle = null, string? partOutline = null)
    {
        NewPage(title);
        var num = _pages.Count;
        Art(() =>
        {
            Background(_pg, Paper);
            _pg.Save();
            _pg.Rect(0, PH - 6, PW, 6); _pg.Clip();
            _pg.Sh(Axial(0, 0, PW, 0, [(0, Indigo), (0.5, Violet), (1, TealL)]));
            _pg.Restore();
            Tx(_t.SansB, 8, M, PH - 34, part.ToUpperInvariant(), partColour, tracking: 1.6);
            Tx(_t.Sans, 8, PW - M, PH - 34, "csharp-pdf  /  sample 28", Slate, 'r');
            Rule(_pg, M, PH - 44, PW - M, Line_);
            Rule(_pg, M, 40, PW - M, Line_);
            Tx(_t.Sans, 8, M, 26, "Exploratory edition. Figures are copied from checked-in reports; nothing here is a marketing claim.", Slate);
            Tx(_t.SansB, 8, PW - M, 26, num.ToString(), Ink, 'r');
        }, "Pagination");
        if (partOutline != null) Outline(partOutline, 0);
        if (outlineTitle != null) Outline(outlineTitle, partOutline != null ? 1 : 1);
        T("H1", () => Tx(_t.SerifB, 25, M, PH - 80, title, Ink));
        var y = 0.0;
        T("P", () => y = Para(_t.SerifI, 12, M, PH - 102, W * 0.92, lede, Slate, 17));
        return y;
    }

    /// <summary>Evidence strip that labels the source, date, hardware and build mode of the figure above it. Returns the next free y.</summary>
    double Evidence(double y, string source, string date, string hardware, string mode)
    {
        double c0 = W * 0.40, c1 = W * 0.13, c2 = W * 0.47;
        var hText = Math.Max(ParaH(_t.Sans, 7.8, c0 - 14, source, 10.5), ParaH(_t.Sans, 7.8, c2 - 14, hardware, 10.5));
        var hMode = ParaH(_t.Sans, 7.8, W - 66, mode, 10.5);
        var h = 12 + 11 + hText + 5 + hMode + 7;
        Art(() => Box(_pg, M, y - h, W, h, fill: "#EEF2FF", stroke: "#C7D2FE", r: 7));
        T("P", () =>
        {
            var x = M + 10;
            string[] labels = ["SOURCE", "RUN", "HARDWARE"], vals = [source, date, hardware];
            double[] cw = [c0, c1, c2];
            for (var i = 0; i < 3; i++)
            {
                Tx(_t.SansB, 7, x, y - 14, labels[i], IndigoD, tracking: 1.2);
                Para(_t.Sans, 7.8, x, y - 25, cw[i] - 14, vals[i], Ink, 10.5);
                x += cw[i];
            }
            var my = y - 25 - hText - 5;
            Tx(_t.SansB, 7, M + 10, my - 8, "MODE", IndigoD, tracking: 1.2);
            Para(_t.Sans, 7.8, M + 46, my - 8, W - 66, mode, Ink, 10.5);
        });
        return y - h - 10;
    }
    void FigureCaption(double x, double y, string text, double width = W) =>
        T("Caption", () => Para(_t.SansI, 9, x, y, width, text, Slate, 12.5));

    void SubHeading(double y, string text, Color? c = null) => T("H2", () => Tx(_t.SansB, 13, M, y, text, c ?? IndigoD));
}
