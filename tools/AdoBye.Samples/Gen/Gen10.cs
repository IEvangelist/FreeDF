using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.K;
using static AdoBye.Samples.Kit.Theme;
using AdoBye.Samples.Kit;

namespace AdoBye.Samples.Gen;

static class Gen10
{
    public const string Out = "10-annotations.pdf";

    /// <summary>Appearance stream whose BBox equals the annotation rectangle (identity mapping).</summary>
    static int Ap(Doc d, double[] rect, Action<XForm> draw)
    {
        var xf = new XForm(d, rect);
        draw(xf);
        return xf.Build();
    }

    public static void Build(string path)
    {
        var d = new Doc("Annotations", subject: "Link, Highlight, Underline, StrikeOut, Squiggly, Text, FreeText, Stamp, Ink, Line, Square, Circle, Polygon with appearance streams", lang: "en-US");
        var t = new Faces(d);
        var helv = d.Std("Helvetica");
        var helvB = d.Std("Helvetica-Bold");
        var (w, h) = A4;
        var pg = d.Page(w, h);
        var pg2 = d.Page(w, h);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Annotations", "Interactive  /  01", 1, 2);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Every annotation carries an appearance stream, so it looks identical in any viewer.", color: P["slate"]);

        const string body = "Portable documents keep their meaning when content is reviewed. Reviewers mark passages with highlights, " +
            "underlines and strike-outs, attach sticky notes, draw freehand ink, and stamp approvals without touching the " +
            "page content beneath. Each markup is a separate object that can be listed, edited or removed.";
        var colW = pg.W - 2 * M;
        var y = pg.H - 150;
        var lineY = new List<(double Y, string Text)>();
        foreach (var ln in Wrap(t.Serif, 13.5, body, colW))
        {
            pg.Text(t.Serif, 13.5, M, y, ln, color: P["ink"]);
            lineY.Add((y, ln));
            y -= 24;
        }

        void Markup(string sub, string s, Color color)
        {
            var (yy, ln) = lineY.First(l => l.Text.Contains(s));
            var pre = t.Serif.Width(ln[..ln.IndexOf(s, StringComparison.Ordinal)], 13.5);
            double x0 = M + pre, y0 = yy - 4.5, x1 = M + pre + t.Serif.Width(s, 13.5), y1 = yy + 12.5;
            var quad = $"[{N(x0)} {N(y1)} {N(x1)} {N(y1)} {N(x0)} {N(y0)} {N(x1)} {N(y0)}]";
            var a = Ap(d, [x0, y0, x1, y1], c =>
            {
                switch (sub)
                {
                    case "Highlight":
                        c.Gs(("BM", "/Multiply"));
                        c.Fill(color); c.Rect(x0, y0, x1 - x0, y1 - y0); c.FillPath();
                        break;
                    case "Underline":
                        c.Stroke(color); c.Width(1.2); c.Move(x0, y0 + 1.5); c.Line(x1, y0 + 1.5); c.StrokePath();
                        break;
                    case "StrikeOut":
                        c.Stroke(color); c.Width(1.2); c.Move(x0, (y0 + y1) / 2); c.Line(x1, (y0 + y1) / 2); c.StrokePath();
                        break;
                    default:
                        c.Stroke(color); c.Width(1); c.Move(x0, y0 + 2);
                        int k = 0;
                        for (var xx = x0; xx < x1; k++)
                        {
                            xx = Math.Min(xx + 2.5, x1);
                            c.Line(xx, y0 + 2 + (k % 2 == 0 ? 2 : 0));
                        }
                        c.StrokePath();
                        break;
                }
            });
            pg.AddAnnot($"<< /Type /Annot /Subtype /{sub} /Rect [{N(x0)} {N(y0)} {N(x1)} {N(y1)}] /QuadPoints {quad} " +
                        $"/C [{color.Ops}] /T (Reviewer) /Contents ({sub}) /F 4 /AP << /N {a} 0 R >> >>");
        }

        Markup("Highlight", "meaning when content", "#FDE047");
        Markup("Underline", "highlights,", P["indigo"]);
        Markup("StrikeOut", "separate object", P["rose"]);
        Markup("Squiggly", "removed.", P["teal"]);

        // sticky note
        var ny = y - 14;
        var nx = M + 6;
        var a0 = Ap(d, [nx, ny - 20, nx + 20, ny], c =>
        {
            c.Fill("#FACC15"); c.Stroke("#A16207"); c.Width(1);
            c.RRect(nx + 1, ny - 19, 18, 18, 3); c.FillStroke();
            c.Stroke("#A16207"); c.Width(1);
            for (var k = 0; k < 3; k++) { c.Move(nx + 4, ny - 6 - k * 4.5); c.Line(nx + 16, ny - 6 - k * 4.5); c.StrokePath(); }
        });
        pg.AddAnnot($"<< /Type /Annot /Subtype /Text /Rect [{N(nx)} {N(ny - 20)} {N(nx + 20)} {N(ny)}] /Name /Comment /Contents (Sticky note: remember to cite the source.) /T (Reviewer) /C [1 0.85 0] /Open false /F 28 /AP << /N {a0} 0 R >> >>");
        pg.Text(t.Sans, 9, nx + 30, ny - 13, "Text annotation (sticky note)", color: P["slate"]);

        // link annotations
        void Card(double x, double ytop, double cw, double ch, string title, string sub, Color color)
        {
            Box(pg, x, ytop - ch, cw, ch, fill: P["white"], stroke: P["mist"], r: 8);
            Box(pg, x, ytop - ch, 4, ch, fill: color);
            pg.Text(t.SansB, 11, x + 16, ytop - 24, title, color: P["ink"]);
            pg.Text(t.Sans, 9, x + 16, ytop - 40, sub, color: P["slate"]);
        }
        var cw = (colW - 20) / 2;
        var top = ny - 46;
        Card(M, top, cw, 66, "URI link", "Opens https://github.com in a browser", P["indigo"]);
        pg.AddAnnot($"<< /Type /Annot /Subtype /Link /Rect [{N(M)} {N(top - 66)} {N(M + cw)} {N(top)}] /Border [0 0 0] /A << /S /URI /URI (https://github.com) >> /F 4 >>");
        Card(M + cw + 20, top, cw, 66, "GoTo link", "Jumps to page 2 at an explicit /XYZ", P["teal"]);
        pg.AddAnnot($"<< /Type /Annot /Subtype /Link /Rect [{N(M + cw + 20)} {N(top - 66)} {N(M + 2 * cw + 20)} {N(top)}] /Border [0 0 0] " +
                    $"/Dest [{pg2.Ref} 0 R /XYZ 54 780 null] /F 4 >>");

        // FreeText
        var fy = top - 96;
        double fx = M, fw = 250, fh = 50;
        var fa = Ap(d, [fx, fy - fh, fx + fw, fy], c =>
        {
            c.Fill("#FEF3C7"); c.Stroke(P["amber"]); c.Width(1.2); c.RRect(fx + 0.6, fy - fh + 0.6, fw - 1.2, fh - 1.2, 6); c.FillStroke();
            c.Text(helv, 11, fx + 10, fy - 20, "Free text: typeset in the", color: P["ink"], kern: false);
            c.Text(helv, 11, fx + 10, fy - 34, "annotation's own font.", color: P["ink"], kern: false);
        });
        pg.AddAnnot($"<< /Type /Annot /Subtype /FreeText /Rect [{N(fx)} {N(fy - fh)} {N(fx + fw)} {N(fy)}] /DA (0 g /Helv 11 Tf) /Contents (Free text) /F 4 /AP << /N {fa} 0 R >> >>");

        // Stamp
        double sx = M + 290, sy = fy - 52, sw = 180, sh = 52;
        var sa = Ap(d, [sx, sy, sx + sw, sy + sh], c =>
        {
            c.Gs(("CA", 0.9), ("ca", 0.9));
            c.Stroke(P["rose"]); c.Width(3); c.RRect(sx + 2, sy + 2, sw - 4, sh - 4, 8); c.StrokePath();
            c.Width(1); c.RRect(sx + 6, sy + 6, sw - 12, sh - 12, 5); c.StrokePath();
            var tw = helvB.Width("APPROVED", 24);
            c.Text(helvB, 24, sx + (sw - tw) / 2, sy + 17, "APPROVED", color: P["rose"], kern: false);
        });
        pg.AddAnnot($"<< /Type /Annot /Subtype /Stamp /Rect [{N(sx)} {N(sy)} {N(sx + sw)} {N(sy + sh)}] /Name /Approved /Contents (Approved) /F 4 /AP << /N {sa} 0 R >> >>");

        // Ink
        var iy = fy - 90;
        var pts = new List<(double X, double Y)>();
        for (var k = 0; k <= 120; k++)
        {
            var u = k / 120.0;
            pts.Add((M + 20 + u * (colW - 40), iy - 40 + Math.Sin(u * 14) * 22 * Math.Sin(u * Math.PI)));
        }
        var ia = Ap(d, [M, iy - 80, M + colW, iy], c =>
        {
            c.Stroke(P["violet"]); c.Width(2.4); c.Cap(1); c.Join(1);
            c.Move(pts[0].X, pts[0].Y);
            foreach (var p in pts.Skip(1)) c.Line(p.X, p.Y);
            c.StrokePath();
        });
        var flat = string.Join(" ", pts.Select(p => $"{N(p.X)} {N(p.Y)}"));
        pg.AddAnnot($"<< /Type /Annot /Subtype /Ink /Rect [{N(M)} {N(iy - 80)} {N(M + colW)} {N(iy)}] /InkList [[{flat}]] /C [{((Color)P["violet"]).Ops}] /BS << /W 2.4 >> /F 4 /AP << /N {ia} 0 R >> >>");
        pg.Text(t.Sans, 9, M, iy - 94, "Ink annotation: freehand stroke stored as an /InkList polyline.", color: P["slate"]);

        // Square / Circle / Line / Polygon
        var shapesY = iy - 124;
        (string Sub, Color Colr)[] shapes = [("Square", P["sky"]), ("Circle", P["emerald"]), ("Line", P["orange"]), ("Polygon", P["violet"])];
        var sw2 = (colW - 3 * 14) / 4;
        for (var k = 0; k < shapes.Length; k++)
        {
            var (sub, colr) = shapes[k];
            var x = M + k * (sw2 + 14);
            double cx0 = x + 8, cy0 = shapesY - 62, cx1 = x + sw2 - 8, cy1 = shapesY - 8;
            var light = Lighten(colr, 0.7);
            var a = Ap(d, [x, shapesY - 70, x + sw2, shapesY], c =>
            {
                c.Fill(light); c.Stroke(colr); c.Width(2);
                switch (sub)
                {
                    case "Square":
                        c.Rect(cx0, cy0, cx1 - cx0, cy1 - cy0); c.FillStroke();
                        break;
                    case "Circle":
                        c.Ellipse((cx0 + cx1) / 2, (cy0 + cy1) / 2, (cx1 - cx0) / 2, (cy1 - cy0) / 2); c.FillStroke();
                        break;
                    case "Line":
                        c.Move(cx0, cy0); c.Line(cx1, cy1); c.StrokePath();
                        c.Fill(colr); c.Move(cx1, cy1); c.Line(cx1 - 12, cy1 - 3); c.Line(cx1 - 3, cy1 - 12); c.Close(); c.FillPath();
                        break;
                    default:
                        c.Move(cx0, cy0); c.Line(cx1, cy0 + 8); c.Line(cx1 - 14, cy1); c.Line(cx0 + 10, cy1 - 12); c.Close(); c.FillStroke();
                        break;
                }
            });
            var extra = sub switch
            {
                "Line" => $"/L [{N(cx0)} {N(cy0)} {N(cx1)} {N(cy1)}] /LE [/None /ClosedArrow]",
                "Polygon" => $"/Vertices [{N(cx0)} {N(cy0)} {N(cx1)} {N(cy0 + 8)} {N(cx1 - 14)} {N(cy1)} {N(cx0 + 10)} {N(cy1 - 12)}]",
                _ => "",
            };
            pg.AddAnnot($"<< /Type /Annot /Subtype /{sub} /Rect [{N(x)} {N(shapesY - 70)} {N(x + sw2)} {N(shapesY)}] {extra} /C [{colr.Ops}] /IC [{((Color)light).Ops}] /F 4 /AP << /N {a} 0 R >> >>");
            pg.Text(t.SansB, 9, x, shapesY - 84, sub, color: P["ink"]);
        }

        // page 2 (link target)
        Background(pg2, P["paper"]);
        PageHeader(pg2, t, "Link target", "Interactive  /  01", 2, 2);
        Paragraph(pg2, t.Sans, 11, M, pg2.H - 120, pg2.W - 2 * M,
                  "You followed the GoTo link on page 1. The destination is an explicit /XYZ array, [page /XYZ left top zoom], " +
                  "with zoom null so the viewer keeps its current magnification.", color: P["slate"], leading: 16);
        d.Save(path);
    }
}
