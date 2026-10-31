using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

static class Gen19
{
    public const string Out = "19-tagged-accessible.pdf";

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

    sealed class Tagger(Doc doc, List<Page> pages)
    {
        readonly Dictionary<Page, int> _counts = [];
        readonly Dictionary<Page, Dictionary<int, Node>> _parents = [];

        public void Mc(Page pg, Node node, Action draw)
        {
            var m = _counts.GetValueOrDefault(pg);
            _counts[pg] = m + 1;
            pg.Raw($"/{node.Role} << /MCID {m} >> BDC");
            draw();
            pg.Raw("EMC");
            node.Kids.Add((pg, m));
            if (!_parents.TryGetValue(pg, out var pm)) _parents[pg] = pm = [];
            pm[m] = node;
        }

        public static void Artifact(Page pg, Action draw, string type = "Pagination")
        {
            pg.Raw($"/Artifact << /Type /{type} >> BDC");
            draw();
            pg.Raw("EMC");
        }

        void Alloc(Node nd)
        {
            nd.Ref = doc.New();
            foreach (var k in nd.Kids.OfType<Node>()) Alloc(k);
        }

        void Emit(Node nd, int? parentRef)
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
            var par = parentRef != null ? $" /P {parentRef} 0 R" : "";
            doc.Put(nd.Ref, $"<< /Type /StructElem /S /{nd.Role}{par}{pgRef}{extra} /K [{string.Join(" ", kids)}] >>");
        }

        public void Build(Node root)
        {
            Alloc(root);
            Emit(root, null);
            var nums = new List<string>();
            for (var i = 0; i < pages.Count; i++)
            {
                var pg = pages[i];
                pg.Extra += $" /StructParents {i} /Tabs /S";
                var pm = _parents.GetValueOrDefault(pg) ?? [];
                var arr = string.Join(" ", Enumerable.Range(0, _counts.GetValueOrDefault(pg)).Select(m => $"{pm[m].Ref} 0 R"));
                nums.Add($"{i} [{arr}]");
            }
            var strRoot = doc.New();
            doc.Put(strRoot, $"<< /Type /StructTreeRoot /K {root.Ref} 0 R /ParentTree << /Nums [{string.Join(" ", nums)}] >> /RoleMap << /Callout /P >> /ParentTreeNextKey {pages.Count} >>");
            var s = doc.Body(root.Ref);
            var at = s.IndexOf($"/S /{root.Role}", StringComparison.Ordinal) + $"/S /{root.Role}".Length;
            doc.Put(root.Ref, s.Insert(at, $" /P {strRoot} 0 R"));
            doc.Cat.Add($"/StructTreeRoot {strRoot} 0 R /MarkInfo << /Marked true /Suspects false >> /ViewerPreferences << /DisplayDocTitle true >>");
            // PDF/UA-1 (ISO 14289-1 5, 7.1) identification and dc:title in an uncompressed XMP packet; checked with veraPDF.
            const string xmp = "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
                + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\" pdfuaid:part=\"1\">"
                + "<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Writing accessible PDFs</rdf:li></rdf:Alt></dc:title><dc:language><rdf:Bag><rdf:li>en-US</rdf:li></rdf:Bag></dc:language>"
                + "</rdf:Description></rdf:RDF></x:xmpmeta>\n<?xpacket end=\"w\"?>";
            doc.Cat.Add($"/Metadata {doc.Stream("/Type /Metadata /Subtype /XML", System.Text.Encoding.UTF8.GetBytes(xmp), compress: false)} 0 R");
        }
    }

    public static void Build(string path)
    {
        var d = new Doc("Writing accessible PDFs", author: "Pdf sample generator", subject: "Tagged PDF: structure tree, reading order, headings, list, table, figure with alt text, artifacts, document language",
            keywords: "accessibility, PDF/UA, tagged", lang: "en-US");
        var t = new Faces(d);
        var p1 = d.Page(A4.W, A4.H);
        var p2 = d.Page(A4.W, A4.H);
        var tg = new Tagger(d, [p1, p2]);
        var doc = new Node("Document");
        var W = p1.W - 2 * M;

        // page 1
        Tagger.Artifact(p1, () => Background(p1, P["paper"]), "Background");
        Tagger.Artifact(p1, () => { Eyebrow(p1, t, M, p1.H - 36, "Accessibility  /  Tagged PDF"); Rule(p1, M, p1.H - 44, p1.W - M); });
        tg.Mc(p1, new Node("H1", doc), () => p1.Text(t.SerifB, 34, M, p1.H - 100, "Writing accessible PDFs", color: P["ink"]));
        tg.Mc(p1, new Node("P", doc), () => Paragraph(p1, t.SerifI, 15, M, p1.H - 135, W - 40,
            "A tagged PDF carries a logical structure beside its visual one, so assistive technology can read it in the right order.", color: P["slate"], leading: 22));
        tg.Mc(p1, new Node("H2", doc), () => p1.Text(t.SansB, 15, M, p1.H - 215, "What makes a document accessible", color: P["indigo_d"]));
        tg.Mc(p1, new Node("P", doc), () => Paragraph(p1, t.Sans, 11, M, p1.H - 240, W - 20,
            "Marked content in the page stream is linked to structure elements through the parent tree. Reading order comes from the structure tree rather than from drawing order, and decoration such as rules and page furniture is marked as an artifact so it is skipped.", color: P["ink"], leading: 16.5));
        var lst = new Node("L", doc);
        string[] items =
        [
            "Set the document language and title (/Lang, /Title, DisplayDocTitle).",
            "Mark every heading with H1 to H6 and keep the levels in order.",
            "Give each figure alternative text with /Alt.",
            "Use real table structure: Table, TR, TH with scope, TD.",
        ];
        var yy = p1.H - 330;
        foreach (var it in items)
        {
            var li = new Node("LI", lst);
            var lbl = new Node("Lbl", li);
            var body = new Node("LBody", li);
            var by = yy;
            tg.Mc(p1, lbl, () => { p1.Save(); p1.Fill(P["indigo"]); p1.Circle(M + 4, by + 3.5, 2.6); p1.FillPath(); p1.Restore(); });
            tg.Mc(p1, body, () => p1.Text(t.Sans, 11, M + 18, by, it, color: P["ink"], kern: true));
            yy -= 24;
        }
        var fig = new Node("Figure", doc, alt: "Bar chart of the share of tagged PDFs by year: 18 percent in 2018, 27 in 2020, 41 in 2022 and 58 percent in 2024.");
        double fy = yy - 40, fh = 200;
        tg.Mc(p1, fig, () =>
        {
            Box(p1, M, fy - fh, W, fh, fill: P["white"], stroke: P["mist"], r: 10);
            (int Yr, int V)[] vals = [(2018, 18), (2020, 27), (2022, 41), (2024, 58)];
            const double bw = 60;
            var gap = (W - 80 - 4 * bw) / 3;
            for (var i = 0; i < vals.Length; i++)
            {
                var (yr, v) = vals[i];
                var x = M + 40 + i * (bw + gap);
                var h = v / 60.0 * (fh - 70);
                p1.Save(); p1.Fill(Mix(P["indigo"], P["violet"], i / 3.0)); p1.RRect(x, fy - fh + 34, bw, h, 5); p1.FillPath(); p1.Restore();
                p1.Text(t.SansB, 11, x + bw / 2 - t.SansB.Width($"{v}%", 11) / 2, fy - fh + 34 + h - 18, $"{v}%", color: h > 30 ? "#FFFFFF" : P["ink"]);
                p1.Text(t.Sans, 9.5, x + bw / 2 - t.Sans.Width(yr.ToString(), 9.5) / 2, fy - fh + 14, yr.ToString(), color: P["slate"]);
            }
        });
        tg.Mc(p1, new Node("Caption", doc), () => p1.Text(t.SansI, 9.5, M, fy - fh - 16, "Figure 1. Illustrative share of tagged PDFs by year (sample data).", color: P["slate"]));
        Tagger.Artifact(p1, () => p1.Text(t.Sans, 8, p1.W - M - 6, 30, "1", color: P["muted"]));

        // page 2: table
        Tagger.Artifact(p2, () => Background(p2, P["paper"]), "Background");
        Tagger.Artifact(p2, () => { Eyebrow(p2, t, M, p2.H - 36, "Accessibility  /  Tagged PDF"); Rule(p2, M, p2.H - 44, p2.W - M); });
        tg.Mc(p2, new Node("H2", doc), () => p2.Text(t.SansB, 20, M, p2.H - 90, "Structure types at a glance", color: P["indigo_d"]));
        var tab = new Node("Table", doc, attrs: "<< /O /Table /Summary (Structure element types, their purpose and an example) >>");
        (double W, string Name)[] cols = [(160, "Element"), (200, "Purpose"), (W - 360, "Example")];
        string[][] rows =
        [
            ["Element", "Purpose", "Example"],
            ["Document", "Root of the tree", "Whole file"], ["H1 – H6", "Headings", "Chapter title"], ["P", "Paragraph", "Body text"],
            ["L / LI / LBody", "Lists", "Bullets"], ["Figure", "Images, charts", "Chart with /Alt"], ["Table / TR / TH / TD", "Tabular data", "This table"],
            ["Artifact", "Not content", "Running header"],
        ];
        var ry = p2.H - 130;
        const double rh = 30;
        for (var ri = 0; ri < rows.Length; ri++)
        {
            var tr = new Node("TR", tab);
            var x = M;
            var head = ri == 0;
            var rowY = ry;
            if (head) Tagger.Artifact(p2, () => Box(p2, M, rowY - rh + 8, W, rh, fill: P["ink"]), "Layout");
            else if (ri % 2 == 0) Tagger.Artifact(p2, () => Box(p2, M, rowY - rh + 8, W, rh, fill: P["white"]), "Layout");
            for (var ci = 0; ci < cols.Length; ci++)
            {
                var cell = new Node(head ? "TH" : "TD", tr, attrs: head ? "<< /O /Table /Scope /Column >>" : "");
                var txt = rows[ri][ci];
                var cx = x;
                var isCol0 = ci == 0;
                tg.Mc(p2, cell, () => p2.Text(head || isCol0 ? t.SansB : t.Sans, 10.5, cx + 12, rowY - 12, txt, color: head ? "#FFFFFF" : P["ink"]));
                x += cols[ci].W;
            }
            ry -= rh;
        }
        tg.Mc(p2, new Node("P", doc), () => Paragraph(p2, t.Sans, 10.5, M, ry - 24, W - 40,
            "Role mapping (/RoleMap) can alias custom tags onto the standard ones. Here, Callout maps to P.", color: P["slate"], leading: 15));
        Tagger.Artifact(p2, () => p2.Text(t.Sans, 8, p2.W - M - 6, 30, "2", color: P["muted"]));
        tg.Build(doc);
        d.Save(path, version: "1.7");
    }
}
