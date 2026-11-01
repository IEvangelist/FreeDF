using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.K;
using static FreeDF.Samples.Kit.Theme;
using FreeDF.Samples.Kit;

namespace FreeDF.Samples.Gen;

static class Gen12
{
    public const string Out = "12-navigation.pdf";

    static readonly (string Title, string Blurb)[] Chapters =
    [
        ("Foreword", "Why navigation matters in long documents."),
        ("Introduction", "Bookmarks, destinations and labels: three views of one idea."),
        ("Objects and syntax", "Eight object types, indirect references and the xref table."),
        ("Page content", "Content streams hold operators; resources are looked up by name."),
        ("Fonts and text", "Simple and composite fonts, encodings, CMaps and the text-state parameters."),
        ("Appendix: Glossary", "Short definitions of the terms used in this sample."),
    ];

    sealed record Node(string Title, string? Dest, int Style, Color? Color, Node[] Kids, bool Collapsed = false);

    public static void Build(string path)
    {
        var d = new Doc("Navigation: bookmarks, named destinations, page labels", subject: "Outlines (nested, coloured, collapsed), name tree of destinations, page-label ranges, GoTo links, PageMode UseOutlines", lang: "en-US");
        var t = new Faces(d);
        var (aw, ah) = A4;
        var pages = Enumerable.Range(0, Chapters.Length + 1).Select(_ => d.Page(aw, ah)).ToArray();
        var toc = pages[0];
        string[] labels = ["i", "ii", "1", "2", "3", "4", "A-1"];

        // table of contents (page 1)
        Background(toc, P["paper"]);
        toc.Text(t.SansB, 8, M, toc.H - 36, "NAVIGATION  /  SAMPLE 12", color: P["indigo"], tracking: 1.6, kern: false);
        Rule(toc, M, toc.H - 44, toc.W - M);
        toc.Text(t.SerifB, 36, M, toc.H - 110, "Contents", color: P["ink"]);
        Paragraph(toc, t.Sans, 11, M, toc.H - 138, toc.W - 2 * M - 120,
                  "Open the bookmarks panel to see the outline. Each entry below is a GoTo link to a named destination; the printed page numbers are page labels (roman, decimal, then an A- prefixed appendix).",
                  color: P["slate"], leading: 16);
        var y = toc.H - 220;
        for (var i = 0; i < Chapters.Length; i++)
        {
            var (title, blurb) = Chapters[i];
            var pidx = i + 1;
            var lab = labels[pidx];
            toc.Text(t.Serif, 16, M, y, title, color: P["ink"]);
            var sub = blurb.Split(". ")[0];
            toc.Text(t.Sans, 9, M, y - 15, sub[..Math.Min(92, sub.Length)], color: P["muted"], kern: false);
            var w = t.SansB.Width(lab, 11);
            toc.Text(t.SansB, 11, toc.W - M - w, y, lab, color: P["indigo"]);
            var x0 = M + t.Serif.Width(title, 16) + 10;
            toc.Save(); toc.Stroke(P["mist"]); toc.Width(1.2); toc.Cap(1); toc.Dash([0, 4]); toc.Move(x0, y + 1); toc.Line(toc.W - M - w - 10, y + 1); toc.StrokePath(); toc.Restore();
            toc.AddAnnot($"<< /Type /Annot /Subtype /Link /Rect [{N(M)} {N(y - 20)} {N(toc.W - M)} {N(y + 14)}] /Border [0 0 0] /Dest (chapter{pidx}) >>");
            y -= 56;
        }

        // chapter pages
        Color[] colors = [P["indigo"], P["teal"], P["violet"], P["sky"], P["orange"], P["emerald"]];
        for (var i = 0; i < Chapters.Length; i++)
        {
            var (title, blurb) = Chapters[i];
            var pg = pages[i + 1];
            Background(pg, P["white"]);
            Box(pg, 0, pg.H - 220, pg.W, 220, fill: colors[i]);
            pg.Save(); pg.Gs(("ca", 0.18)); pg.Fill("#FFFFFF");
            pg.Circle(pg.W - 60, pg.H - 60, 150); pg.FillPath(); pg.Circle(pg.W - 20, pg.H - 200, 80); pg.FillPath(); pg.Restore();
            pg.Text(t.SansB, 9, M, pg.H - 60, $"CHAPTER {i + 1}  ·  PAGE LABEL {labels[i + 1]}", color: "#FFFFFF", tracking: 1.6, kern: false);
            pg.Text(t.SerifB, 40, M, pg.H - 150, title, color: "#FFFFFF");
            Paragraph(pg, t.Serif, 15, M, pg.H - 290, pg.W - 2 * M - 40, blurb, color: P["ink"], leading: 24);
            Paragraph(pg, t.Sans, 10.5, M, pg.H - 400, (pg.W - 2 * M - 30) / 2,
                      "Navigation targets are resolved lazily: a viewer reads the name tree, finds the destination array, then scrolls to the page, honouring the fit mode in the array. Because destinations are named, pages can be reordered without breaking links elsewhere.",
                      color: P["slate"], leading: 15, align: "justify");
            pg.Text(t.Sans, 9, pg.W / 2 - 6, 36, labels[i + 1], color: P["muted"]);
            Box(pg, M, 56, 110, 26, fill: P["paper"], stroke: P["mist"], r: 13);
            pg.Text(t.SansB, 9, M + 16, 65, "← Contents", color: P["indigo"], kern: false);
            pg.AddAnnot($"<< /Type /Annot /Subtype /Link /Rect [{N(M)} 56 {N(M + 110)} 82] /Border [0 0 0] /Dest (contents) >>");
        }

        // name tree of destinations
        var dests = new List<(string Key, string Val)> { ("contents", $"[{toc.Ref} 0 R /Fit]") };
        for (var i = 0; i < Chapters.Length; i++) dests.Add(($"chapter{i + 1}", $"[{pages[i + 1].Ref} 0 R /XYZ 0 {N(pages[i + 1].H)} 0]"));
        dests.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        var names = string.Join(" ", dests.Select(e => $"{PStr(e.Key)} {e.Val}"));
        var dnode = d.Add($"<< /Names [{names}] /Limits [{PStr(dests[0].Key)} {PStr(dests[^1].Key)}] >>");
        d.Cat.Add($"/Names << /Dests << /Kids [{dnode} 0 R] >> >>");

        // outline tree
        var root = d.New();
        Node[] spec =
        [
            new("Contents", "contents", 0, null, []),
            new("Front matter", null, 0, null, [new("Foreword", "chapter1", 0, null, []), new("Introduction", "chapter2", 0, null, [])]),
            new("Main text", null, 1, null, [new("Objects and syntax", "chapter3", 0, null, []), new("Page content", "chapter4", 0, null, []), new("Fonts and text", "chapter5", 2, null, [])], true),
            new("Appendix: Glossary", "chapter6", 1, (0.88, 0.11, 0.28), []),
        ];

        (int First, int Last) Make(Node[] nodes, int parent)
        {
            var refs = nodes.Select(_ => d.New()).ToArray();
            for (var k = 0; k < nodes.Length; k++)
            {
                var n = nodes[k];
                var kidS = "";
                if (n.Kids.Length > 0)
                {
                    var (first, last) = Make(n.Kids, refs[k]);
                    kidS = $"/First {first} 0 R /Last {last} 0 R /Count {(n.Collapsed ? -n.Kids.Length : n.Kids.Length)}";
                }
                var destS = n.Dest ?? n.Kids[0].Dest;
                var col = n.Color is { } c ? $"/C [{c.Ops}]" : "";
                var prev = k > 0 ? $"/Prev {refs[k - 1]} 0 R" : "";
                var next = k + 1 < refs.Length ? $"/Next {refs[k + 1]} 0 R" : "";
                d.Put(refs[k], $"<< /Title {Utf16(n.Title)} /Parent {parent} 0 R /Dest {PStr(destS!)} /F {n.Style} {col} {prev} {next} {kidS} >>");
            }
            return (refs[0], refs[^1]);
        }
        var (rf, rl) = Make(spec, root);
        d.Put(root, $"<< /Type /Outlines /First {rf} 0 R /Last {rl} 0 R /Count {spec.Length + 2} >>");
        d.Cat.Add($"/Outlines {root} 0 R /PageMode /UseOutlines");

        // page labels
        d.Cat.Add("/PageLabels << /Nums [0 << /S /r >> 2 << /S /D >> 6 << /S /D /P (A-) /St 1 >>] >>");
        d.Cat.Add("/ViewerPreferences << /DisplayDocTitle true >>");
        d.Save(path);
    }
}
