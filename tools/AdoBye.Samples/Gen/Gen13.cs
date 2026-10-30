using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.Theme;
using AdoBye.Samples.Kit;

namespace AdoBye.Samples.Gen;

static class Gen13
{
    public const string Out = "13-page-boxes-rotation.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Page boxes and rotation", subject: "MediaBox, CropBox, BleedBox, TrimBox, ArtBox, trim marks, /Rotate 0/90/180/270", lang: "en-US");
        var t = new Faces(d);
        const double W = 612, H = 792;
        var boxes = new Dictionary<string, double[]>
        {
            ["CropBox"] = [18, 18, 594, 774], ["BleedBox"] = [27, 27, 585, 765], ["TrimBox"] = [36, 36, 576, 756], ["ArtBox"] = [72, 72, 540, 720],
        };
        var cols = new Dictionary<string, Color>
        {
            ["MediaBox"] = P["slate"], ["CropBox"] = P["sky"], ["BleedBox"] = P["rose"], ["TrimBox"] = P["emerald"], ["ArtBox"] = P["violet"],
        };
        var pg = d.Page(W, H);
        foreach (var (k, v) in boxes) pg.Boxes[k] = v;
        // bleed artwork reaches the bleed box, not the trim
        var bb = boxes["BleedBox"];
        pg.Save(); pg.Rect(bb[0], bb[1], bb[2] - bb[0], bb[3] - bb[1]); pg.Clip();
        pg.Fill(P["indigo_d"]); pg.Rect(0, 0, W, H); pg.FillPath();
        pg.Sh(Radial(W * 0.8, H * 0.85, 0, W * 0.8, H * 0.85, 560, [(0, P["violet"]), (0.5, P["indigo"]), (1, P["indigo_d"])]));
        pg.Restore();
        // trim marks in the margin between trim and crop
        var tb = boxes["TrimBox"];
        pg.Save(); pg.Stroke("#FFFFFF"); pg.Width(0.5);
        foreach (var (x, y, dx, dy) in new (double, double, int, int)[] { (tb[0], tb[1], -1, -1), (tb[2], tb[1], 1, -1), (tb[0], tb[3], -1, 1), (tb[2], tb[3], 1, 1) })
        {
            pg.Move(x, y + dy * 3); pg.Line(x, y + dy * 17); pg.StrokePath();
            pg.Move(x + dx * 3, y); pg.Line(x + dx * 17, y); pg.StrokePath();
        }
        pg.Restore();
        pg.Text(t.SansB, 9, 90, H - 100, "PAGE BOXES  /  SAMPLE 13", color: "#C7D2FE", tracking: 1.6, kern: false);
        pg.Text(t.SerifB, 46, 90, H - 160, "Five boxes,", color: "#FFFFFF");
        pg.Text(t.SerifB, 46, 90, H - 212, "one page.", color: "#FFFFFF");
        (string Name, string Val, string Desc)[] rows =
        [
            ("MediaBox", "0 0 612 792", "Physical sheet, the largest extent."),
            ("CropBox", "18 18 594 774", "What viewers show and print; this page opens at this size."),
            ("BleedBox", "27 27 585 765", "Where artwork must extend to survive trimming."),
            ("TrimBox", "36 36 576 756", "The finished page after cutting, marked in white."),
            ("ArtBox", "72 72 540 720", "The meaningful content, kept clear of the edge."),
        ];
        var yy = H - 300;
        foreach (var (nm, val, desc) in rows)
        {
            pg.Save(); pg.Fill(cols[nm]); pg.RRect(90, yy - 8, 10, 28, 3); pg.FillPath(); pg.Restore();
            pg.Text(t.SansB, 13, 114, yy + 8, nm, color: "#FFFFFF");
            pg.Text(t.Mono, 9.5, 220, yy + 8.5, val, color: "#C7D2FE", kern: false);
            pg.Text(t.Sans, 10, 114, yy - 6, desc, color: "#E0E7FF");
            yy -= 52;
        }
        // outlines of every box so the nesting is visible
        pg.Save(); pg.Width(0.9);
        var outlines = new List<(string, double[])> { ("MediaBox", [0.5, 0.5, W - 0.5, H - 0.5]) };
        outlines.AddRange(boxes.Select(b => (b.Key, b.Value)));
        foreach (var (nm, b) in outlines)
        {
            pg.Stroke(cols[nm]); pg.Dash(nm != "TrimBox" ? [4, 3] : []); pg.Rect(b[0], b[1], b[2] - b[0], b[3] - b[1]); pg.StrokePath();
        }
        pg.Restore();

        foreach (var deg in new[] { 90, 180, 270 })
        {
            var p = d.Page(W, H);
            p.Extra = $"/Rotate {deg}";
            Background(p, P["paper"]);
            p.Save(); p.Fill(P["indigo"]); p.Rect(0, H - 8, W, 8); p.FillPath(); p.Restore();
            p.Text(t.SansB, 9, M, H - 40, "PAGE ROTATION  /  SAMPLE 13", color: P["indigo"], tracking: 1.6, kern: false);
            p.Text(t.SerifB, 40, M, H - 100, $"/Rotate {deg}", color: P["ink"]);
            Paragraph(p, t.Sans, 11.5, M, H - 134, 420,
                      "The page dictionary stores the content upright; the viewer turns the whole page clockwise by this many degrees at display time. The arrow below always points to the content's top edge.",
                      color: P["slate"], leading: 17);
            double cx = W / 2, cy = 340;
            p.Save(); p.Fill(P["indigo"]); p.Move(cx, cy + 120); p.Line(cx + 70, cy + 10); p.Line(cx + 26, cy + 10); p.Line(cx + 26, cy - 100);
            p.Line(cx - 26, cy - 100); p.Line(cx - 26, cy + 10); p.Line(cx - 70, cy + 10); p.Close(); p.FillPath(); p.Restore();
            p.Text(t.SansB, 12, cx - t.SansB.Width("content top", 12) / 2, cy - 130, "content top", color: P["slate"]);
            foreach (var (x, y, c, lab) in new (double, double, Color, string)[] { (M, M, P["rose"], "BL"), (W - M - 40, M, P["teal"], "BR"), (M, H - 200, P["amber"], "TL"), (W - M - 40, H - 200, P["sky"], "TR") })
            {
                Box(p, x, y, 40, 40, fill: c, r: 8);
                p.Text(t.SansB, 12, x + 20 - t.SansB.Width(lab, 12) / 2, y + 15, lab, color: "#FFFFFF", kern: false);
            }
        }
        d.Save(path);
    }
}
