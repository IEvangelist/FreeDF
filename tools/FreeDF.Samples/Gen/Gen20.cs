using System.Globalization;
using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen20
{
    public const string Out = "20-report.pdf";

    static readonly string[] Lorem =
    [
        "Document technology rarely makes headlines, yet it quietly carries contracts, invoices, manuals and archives for billions of people. This report reviews how a mature page-description format stays reliable across decades of devices, operating systems and software generations.",
        "Three forces explain its longevity: a small, fully specified imaging model; a conservative file structure that tolerates partial damage; and an ecosystem of independent implementations that keep each other honest. Each is examined in the sections that follow.",
        "The findings are drawn from a corpus of 4,812 public documents and from the behaviour of four independent renderers. Where results differ we say so, and where a difference is a bug we link it to the clause of the specification that decides the matter.",
    ];

    static readonly string[] Sections = ["Executive summary", "Findings", "Outlook", "Method and sources"];

    static string F(double v, int dec) => v.ToString("F" + dec, CultureInfo.InvariantCulture);

    public static void Build(string path)
    {
        var d = new Doc("Annual report: The state of the page", "Pdf sample generator",
            "Multi-page report with cover, contents, columns, tables, charts, outlines and internal links", "report, outlines, links, charts", "en-US");
        var t = new Faces(d);
        var (cover, toc, p1, p2, p3, p4) = (d.Page(A4.W, A4.H), d.Page(A4.W, A4.H), d.Page(A4.W, A4.H), d.Page(A4.W, A4.H), d.Page(A4.W, A4.H), d.Page(A4.W, A4.H));
        const int total = 6;
        Page[] secPages = [p1, p2, p3, p4];

        var c = cover;
        Background(c, P["ink"]);
        c.Save(); c.Rect(0, 0, c.W, c.H); c.Clip();
        c.Sh(Radial(c.W * 0.85, c.H * 0.78, 0, c.W * 0.85, c.H * 0.78, 520, [(0, "#6366F1"), (0.45, "#312E81"), (1, P["ink"])]));
        c.Save(); c.Gs(("CA", 0.18)); c.Stroke("#FFFFFF"); c.Width(0.8);
        for (var k = 1; k < 12; k++) { c.Circle(c.W * 0.85, c.H * 0.78, 30 + k * 38); c.StrokePath(); }
        c.Restore(); c.Restore();
        LogoMark(c, M, c.H - 110, 34, a: "#818CF8", b: "#C084FC");
        c.Text(t.SansB, 10, M + 48, c.H - 98, "NORTHLIGHT RESEARCH", color: "#C7D2FE", tracking: 2.4, kern: false);
        c.Text(t.SerifB, 66, M, 440, "The State", color: "#FFFFFF");
        c.Text(t.SerifB, 66, M, 372, "of the Page", color: "#FFFFFF");
        c.Text(t.SerifI, 20, M, 322, "How a 30-year-old format keeps getting better", color: "#C7D2FE");
        Rule(c, M, 140, c.W - M, color: "#4338CA");
        c.Text(t.Sans, 10, M, 112, "ANNUAL REPORT  ·  2026 EDITION", color: "#A5B4FC", tracking: 1.6, kern: false);
        const string notice = "Not for redistribution — a sample document";
        c.Text(t.Sans, 10, c.W - M - t.Sans.Width(notice, 10), 112, notice, color: "#818CF8");

        void Furniture(Page pg, int num, string title) => PageHeader(pg, t, title, "The state of the page", num, total);

        Furniture(toc, 2, "Contents");
        var y = toc.H - 170;
        for (var i = 0; i < 4; i++)
        {
            var (name, pg) = (Sections[i], secPages[i]);
            toc.Text(t.SerifB, 40, M, y - 6, $"0{i + 1}", color: P["mist"]);
            toc.Text(t.SerifB, 18, M + 70, y, name, color: P["ink"]);
            var s = (i + 3).ToString();
            toc.Text(t.Sans, 12, toc.W - M - t.Sans.Width(s, 12), y, s, color: P["slate"]);
            var dotsX0 = M + 78 + t.SerifB.Width(name, 18);
            toc.Save(); toc.Stroke(P["muted"]); toc.Width(0.8); toc.Cap(1); toc.Dash([0.1, 4]); toc.Move(dotsX0, y + 2); toc.Line(toc.W - M - 24, y + 2); toc.StrokePath(); toc.Restore();
            toc.AddAnnot($"<< /Type /Annot /Subtype /Link /Rect [{K.N(M)} {K.N(y - 14)} {K.N(toc.W - M)} {K.N(y + 24)}] /Border [0 0 0] /Dest [{pg.Ref} 0 R /XYZ 0 {K.N(pg.H)} 0] >>");
            y -= 78;
        }
        Box(toc, M, 110, toc.W - 2 * M, 70, fill: P["paper"], r: 8);
        Paragraph(toc, t.Sans, 10, M + 18, 156, toc.W - 2 * M - 36, "Every entry above is a link annotation with a GoTo destination, and the same four sections are listed in the document outline (bookmarks panel).", leading: 15, color: P["slate"]);

        // section 1: executive summary with drop cap and two columns
        Furniture(p1, 3, Sections[0]);
        Eyebrow(p1, t, M, p1.H - 110, "Key message");
        var colw = (p1.W - 2 * M - 24) / 2;
        var ytop = p1.H - 150;
        p1.Text(t.SerifB, 64, M - 2, ytop - 46, "D", color: P["indigo"]);
        var lines = Wrap(t.Sans, 10.5, Lorem[0][1..], colw - 50);
        y = ytop;
        for (var i = 0; i < lines.Count; i++)
        {
            p1.Text(t.Sans, 10.5, i < 4 ? M + 50 : M, y, lines[i], color: P["ink"]);
            y -= 15;
        }
        Paragraph(p1, t.Sans, 10.5, M, y - 8, colw, Lorem[1], leading: 15, align: "justify", color: P["ink"]);
        Paragraph(p1, t.Sans, 10.5, M + colw + 24, ytop, colw, Lorem[2], leading: 15, align: "justify", color: P["ink"]);
        var qy = p1.H - 400;
        p1.Save(); p1.Fill(P["indigo"]); p1.Rect(M, qy - 90, 4, 110); p1.FillPath(); p1.Restore();
        p1.Text(t.SerifI, 22, M + 22, qy, "“Longevity is a design choice,", color: P["ink"]);
        p1.Text(t.SerifI, 22, M + 22, qy - 30, "not an accident of adoption.”", color: P["ink"]);
        p1.Text(t.Sans, 9, M + 22, qy - 60, "— Dr. Imani Okafor, lead author", color: P["muted"]);
        (string, string)[] stats = [("4,812", "documents analysed"), ("97.3 %", "rendered identically"), ("12", "format clauses clarified"), ("30 yrs", "of backward compatibility")];
        var tw = (p1.W - 2 * M - 3 * 12) / 4;
        for (var i = 0; i < stats.Length; i++)
        {
            var (v, l) = stats[i];
            var x = M + i * (tw + 12);
            Box(p1, x, 190, tw, 90, fill: P["paper"], stroke: P["mist"], r: 8);
            p1.Text(t.SerifB, 24, x + 14, 238, v, color: P["indigo"]);
            Paragraph(p1, t.Sans, 9, x + 14, 218, tw - 24, l, leading: 12, color: P["slate"]);
        }

        // section 2: table and bar chart
        Furniture(p2, 4, Sections[1]);
        Paragraph(p2, t.Sans, 10.5, M, p2.H - 114, p2.W - 2 * M, "Compatibility improved across every feature family we measured. The table lists the share of corpus documents that used each feature and the share rendered without visible difference.", leading: 15, color: P["slate"]);
        (string Name, double Use, double Ok)[] rows =
            [("Text and fonts", 99.1, 98.2), ("Vector graphics", 87.4, 97.9), ("Transparency", 41.6, 94.1), ("Colour management", 33.8, 91.7), ("Forms and annotations", 22.5, 96.4), ("Encryption", 9.3, 99.0)];
        var ty = p2.H - 190;
        Box(p2, M, ty - 8, p2.W - 2 * M, 28, fill: P["ink"], r: 6);
        foreach (var (x, h) in new[] { (M + 14, "FEATURE FAMILY"), (M + 230, "USAGE"), (M + 330, "IDENTICAL") })
            p2.Text(t.SansB, 8, x, ty + 3, h, color: "#FFFFFF", tracking: 1.2, kern: false);
        for (var i = 0; i < rows.Length; i++)
        {
            var (name, use, ok) = rows[i];
            var yy = ty - 36 - i * 30;
            if (i % 2 == 0) Box(p2, M, yy - 9, p2.W - 2 * M, 28, fill: P["paper"]);
            p2.Text(t.Sans, 10.5, M + 14, yy, name, color: P["ink"]);
            p2.Text(t.Sans, 10.5, M + 230, yy, $"{F(use, 1)} %", color: P["slate"]);
            p2.Text(t.SansB, 10.5, M + 330, yy, $"{F(ok, 1)} %", color: ok > 95 ? P["emerald"] : P["amber"]);
            const double bw = 90;
            Box(p2, M + 400, yy - 1, bw, 7, fill: P["mist"], r: 3.5);
            Box(p2, M + 400, yy - 1, bw * ok / 100, 7, fill: P["indigo"], r: 3.5);
        }
        const double cy = 150;
        Eyebrow(p2, t, M, 372, "Figure 1");
        p2.Text(t.SansB, 12, M, 354, "Identical rendering by feature family, %", color: P["ink"]);
        double cx0 = M + 28, cx1 = p2.W - M;
        for (var g = 0; g <= 100; g += 25)
        {
            var yy = cy + g * 1.6;
            Rule(p2, cx0, yy, cx1, color: P["mist"], w: 0.5);
            p2.Text(t.Sans, 7.5, cx0 - 8 - t.Sans.Width(g.ToString(), 7.5), yy - 2.5, g.ToString(), color: P["muted"]);
        }
        var slot = (cx1 - cx0) / rows.Length;
        string[] cols = [P["indigo"], P["violet"], P["sky"], P["teal"], P["amber"], P["rose"]];
        for (var i = 0; i < rows.Length; i++)
        {
            var (name, _, ok) = rows[i];
            var x = cx0 + i * slot + slot * 0.2;
            Box(p2, x, cy, slot * 0.6, ok * 1.6, fill: cols[i], r: 3);
            var okS = F(ok, 0);
            p2.Text(t.SansB, 8.5, x + slot * 0.3 - t.SansB.Width(okS, 8.5) / 2, cy + ok * 1.6 + 5, okS, color: P["ink"]);
            var lab = name.Split(' ')[0];
            p2.Text(t.Sans, 8, x + slot * 0.3 - t.Sans.Width(lab, 8) / 2, cy - 14, lab, color: P["slate"]);
        }

        // section 3: line chart
        Furniture(p3, 5, Sections[2]);
        Paragraph(p3, t.Sans, 10.5, M, p3.H - 114, p3.W - 2 * M, "Adoption of tagged, accessible documents is the clearest growth story. If the current trend holds, a majority of new documents will carry a structure tree by 2029.", leading: 15, color: P["slate"]);
        double xs0 = M + 30, xs1 = p3.W - M, ys0 = 420, ys1 = p3.H - 210;
        int[] vals = [8, 12, 17, 24, 31, 39, 46, 53, 59, 64];
        for (var g = 0; g <= 80; g += 20)
        {
            var yy = ys0 + g / 80.0 * (ys1 - ys0);
            Rule(p3, xs0, yy, xs1, color: P["mist"], w: 0.5);
            p3.Text(t.Sans, 7.5, xs0 - 8 - t.Sans.Width(g.ToString(), 7.5), yy - 2.5, $"{g}", color: P["muted"]);
        }
        var pts = vals.Select((v, i) => (X: xs0 + i * (xs1 - xs0) / 9, Y: ys0 + v / 80.0 * (ys1 - ys0))).ToArray();
        p3.Save(); p3.Move(pts[0].X, ys0);
        foreach (var pt in pts) p3.Line(pt.X, pt.Y);
        p3.Line(pts[^1].X, ys0); p3.Close(); p3.Clip();
        p3.Sh(Axial(0, ys0, 0, ys1, [(0, "#FFFFFF"), (1, "#C7D2FE")]));
        p3.Restore();
        p3.Save(); p3.Stroke(P["indigo"]); p3.Width(2.2); p3.Join(1); p3.Move(pts[0].X, pts[0].Y);
        foreach (var pt in pts[1..6]) p3.Line(pt.X, pt.Y);
        p3.StrokePath(); p3.Dash([5, 4]); p3.Move(pts[5].X, pts[5].Y);
        foreach (var pt in pts[6..]) p3.Line(pt.X, pt.Y);
        p3.StrokePath(); p3.Restore();
        for (var i = 0; i < pts.Length; i++)
        {
            var (px, py) = pts[i];
            var yr = (2020 + i).ToString();
            p3.Save(); p3.Fill("#FFFFFF"); p3.Stroke(P["indigo"]); p3.Width(1.5); p3.Circle(px, py, 3); p3.FillStroke(); p3.Restore();
            p3.Text(t.Sans, 7.5, px - t.Sans.Width(yr, 7.5) / 2, ys0 - 14, yr, color: P["muted"]);
        }
        p3.Text(t.SansI, 8.5, pts[6].X + 6, pts[6].Y - 22, "dashed: projection", color: P["slate"]);
        Eyebrow(p3, t, M, ys1 + 36, "Figure 2");
        p3.Text(t.SansB, 12, M, ys1 + 18, "Share of new documents with a structure tree, %", color: P["ink"]);
        var cw = (p3.W - 2 * M - 24) / 3;
        (string, string, string)[] cards =
            [("Accessible by default", "Authoring tools tag structure automatically.", P["indigo"]), ("Smaller files", "Object streams and subsetting cut size by a third.", P["teal"]), ("Safer by design", "AES-256 is now the baseline for protected files.", P["amber"])];
        for (var i = 0; i < cards.Length; i++)
        {
            var (h, b, col) = cards[i];
            var x = M + i * (cw + 12);
            Box(p3, x, 160, cw, 150, fill: P["paper"], r: 8);
            p3.Save(); p3.Fill(col); p3.RRect(x, 160 + 146, cw, 4, 2); p3.FillPath(); p3.Restore();
            p3.Text(t.SerifB, 14, x + 14, 280, h, color: P["ink"]);
            Paragraph(p3, t.Sans, 9.5, x + 14, 258, cw - 28, b, leading: 14, color: P["slate"]);
        }

        // section 4: method
        Furniture(p4, 6, Sections[3]);
        y = Paragraph(p4, t.Sans, 10.5, M, p4.H - 114, p4.W - 2 * M, "Documents were sampled from public repositories, converted to a common baseline where necessary, and rendered at 96 dpi by four independent engines. A document counts as identical when no channel differs by more than 2 % over more than 2 % of pixels.", leading: 15, color: P["slate"]);
        (string, string)[] refs =
            [("ISO 32000-2:2020", "Document management — Portable document format — Part 2: PDF 2.0"), ("ISO 14289-1:2014", "PDF/UA-1 — accessible PDF"), ("ISO 19005-4:2020", "PDF/A-4 — archival"), ("RFC 8118", "The application/pdf media type")];
        Eyebrow(p4, t, M, y - 28, "References");
        y -= 56;
        foreach (var (code, desc) in refs)
        {
            p4.Text(t.Mono, 9.5, M, y, code, color: P["indigo"]);
            p4.Text(t.Sans, 10, M + 130, y, desc, color: P["ink"]);
            Rule(p4, M, y - 9, p4.W - M);
            y -= 28;
        }
        Box(p4, M, 110, p4.W - 2 * M, 86, fill: P["ink"], r: 10);
        p4.Text(t.SerifB, 16, M + 22, 166, "Colophon", color: "#FFFFFF");
        Paragraph(p4, t.Sans, 9.5, M + 22, 146, p4.W - 2 * M - 44, "Set in Source Serif 4 and Source Sans 3 (embedded CFF subsets). Charts are drawn with path operators and shadings; there are no raster images in this file.", leading: 14, color: "#CBD5E1");

        // outlines
        var items = new List<(string, Page)> { ("Cover", cover), ("Contents", toc) };
        for (var i = 0; i < 4; i++) items.Add(($"{i + 1}. {Sections[i]}", secPages[i]));
        var root = d.New();
        var nums = items.Select(_ => d.New()).ToArray();
        for (var i = 0; i < items.Count; i++)
        {
            var links = i > 0 ? $"/Prev {nums[i - 1]} 0 R " : "";
            if (i < items.Count - 1) links += $"/Next {nums[i + 1]} 0 R ";
            d.Put(nums[i], $"<< /Title {K.PStr(items[i].Item1)} /Parent {root} 0 R {links}/Dest [{items[i].Item2.Ref} 0 R /Fit] >>");
        }
        d.Put(root, $"<< /Type /Outlines /First {nums[0]} 0 R /Last {nums[^1]} 0 R /Count {items.Count} >>");
        d.Cat.Add($"/Outlines {root} 0 R /PageMode /UseOutlines /ViewerPreferences << /DisplayDocTitle true >>");
        d.Save(path);
    }
}
