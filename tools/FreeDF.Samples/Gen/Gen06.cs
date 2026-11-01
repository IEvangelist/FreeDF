using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.K;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen06
{
    public const string Out = "06-patterns-shadings.pdf";

    static int Coord(double v, double lo, double hi) => (int)Math.Round(Math.Min(Math.Max((v - lo) / (hi - lo), 0), 1) * 65535);

    static void U16(List<byte> o, int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }

    static void Rgb(List<byte> o, Color c) { o.Add((byte)(int)(c.R * 255)); o.Add((byte)(int)(c.G * 255)); o.Add((byte)(int)(c.B * 255)); }

    static void Vertex(List<byte> o, int flag, double x, double y, Color col, double w, double h)
    {
        o.Add((byte)flag); U16(o, Coord(x, 0, w)); U16(o, Coord(y, 0, h)); Rgb(o, col);
    }

    static byte[] Patch((double X, double Y)[] points, Color[] colors, double w, double h)
    {
        var o = new List<byte> { 0 };
        foreach (var (x, y) in points) { U16(o, Coord(x, 0, w)); U16(o, Coord(y, 0, h)); }
        foreach (var c in colors) Rgb(o, c);
        return [.. o];
    }

    public static void Build(string path)
    {
        var d = new Doc("Patterns and shadings", subject: "Tiling patterns and shading types 1-7", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Patterns and shadings", "Graphics  /  02", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Tiling patterns (coloured and uncoloured) and all seven shading types, including Coons and tensor-product meshes.", color: P["slate"]);
        const int cols = 3;
        const double th = 128, gap = 16;
        var tw = (pg.W - 2 * M - 2 * 16) / 3;
        var top = pg.H - 132;
        double ox = 0, oy = 0;

        void Tile(int i, string title, string sub, Action<double, double> draw)
        {
            int c = i % cols, r = i / cols;
            double x = M + c * (tw + gap), y = top - th - r * (th + 36);
            (ox, oy) = (x, y);
            pg.Save();
            pg.RRect(x, y, tw, th, 8);
            pg.Clip();
            pg.Translate(x, y);
            draw(tw, th);
            pg.Restore();
            Box(pg, x, y, tw, th, stroke: P["mist"], r: 8, lw: 0.8);
            pg.Text(t.SansB, 8.5, x, y - 13, title, color: P["ink"]);
            pg.Text(t.Sans, 7.5, x, y - 23, sub, color: P["muted"], kern: false);
        }

        var cp = new XForm(d, [0, 0, 20, 20]);
        cp.Fill(P["indigo"]); cp.Circle(5, 5, 4); cp.FillPath();
        cp.Fill(P["amber"]); cp.Circle(15, 15, 4); cp.FillPath();
        cp.Stroke(P["mist"]); cp.Width(0.6); cp.Move(0, 10); cp.Line(20, 10); cp.StrokePath();
        var cref = cp.Build("pattern", 20, 20);
        Tile(0, "Tiling, PaintType 1", "coloured cell with its own colours", (w, h) => { pg.PatternFill(cref); pg.Rect(0, 0, w, h); pg.FillPath(); });

        var up = new XForm(d, [0, 0, 12, 12]);
        up.Width(1.2); up.Move(0, 0); up.Line(12, 12); up.Move(-2, 10); up.Line(2, 14); up.Move(10, -2); up.Line(14, 2); up.StrokePath();
        var uref = up.Build("pattern", 12, 12, paint: 2);
        Tile(1, "Tiling, PaintType 2", "uncoloured cell; colour supplied at fill time", (w, h) =>
        {
            pg.Fill(P["paper"]); pg.Rect(0, 0, w, h); pg.FillPath();
            Color rose = P["rose"], teal = P["teal"];
            pg.PatternFill(uref, cs: "[/Pattern /DeviceRGB]", comps: rose.Ops); pg.Rect(0, 0, w / 2, h); pg.FillPath();
            pg.PatternFill(uref, cs: "[/Pattern /DeviceRGB]", comps: teal.Ops); pg.Rect(w / 2, 0, w / 2, h); pg.FillPath();
        });

        var mp = new XForm(d, [0, 0, 16, 16], [0.866, 0.5, -0.5, 0.866, 0, 0]);
        mp.Fill(P["violet"]); mp.Rect(0, 0, 8, 8); mp.FillPath(); mp.Fill(P["sky"]); mp.Rect(8, 8, 8, 8); mp.FillPath();
        mp.Stroke(P["white"]); mp.Width(0.8); mp.Rect(0, 0, 16, 16); mp.StrokePath();
        var mref = mp.Build("pattern", 16, 16);
        Tile(2, "Tiling with /Matrix", "pattern space rotated by 30 degrees", (w, h) => { pg.PatternFill(mref); pg.Rect(0, 0, w, h); pg.FillPath(); });

        Tile(3, "Axial (type 2)", "three-stop stitching function", (w, h) =>
            pg.Sh(Axial(0, 0, w, h, [(0, P["indigo_d"]), (0.5, P["violet"]), (1, P["amber"])])));

        Tile(4, "Radial (type 3)", "concentric circles, extended", (w, h) =>
            pg.Sh(Radial(w / 2, h / 2, 0, w / 2, h / 2, 70, [(0, "#FFFFFF"), (0.35, P["sky"]), (1, P["indigo_d"])])));

        Tile(5, "Radial, cone", "two circles of different radii, no extension", (w, h) =>
        {
            pg.Fill(P["ink"]); pg.Rect(0, 0, w, h); pg.FillPath();
            pg.Sh(Radial(40, 64, 6, w - 40, 64, 40, [(0, P["amber"]), (1, P["rose"])], extend: false));
        });

        var fn = d.Stream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]", Latin1("{ 2 copy mul 1 exch sub 3 1 roll }"), compress: false);
        Tile(6, "Function-based (type 1)", "PostScript calculator function f(x, y)", (w, h) =>
            pg.Sh($"<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] /Matrix [{N(w)} 0 0 {N(h)} 0 0] /Function {fn} 0 R >>"));

        int Mesh(string dict, List<byte> data, double w, double h) =>
            d.Stream($"{dict} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Decode [0 {N(w)} 0 {N(h)} 0 1 0 1 0 1]", [.. data]);

        Tile(7, "Free-form triangle mesh (type 4)", "Gouraud-shaded triangles with edge flags", (w, h) =>
        {
            Color[] cs = [P["rose"], P["amber"], P["indigo"], P["teal"], P["violet"]];
            (double X, double Y)[] v = [(8, 8), (w - 8, 14), (w / 2, h - 8), (w - 8, h - 10), (w * 0.15, h - 20)];
            var data = new List<byte>();
            for (var i = 0; i < 3; i++) Vertex(data, 0, v[i].X, v[i].Y, cs[i], w, h);
            Vertex(data, 1, v[3].X, v[3].Y, cs[3], w, h);
            Vertex(data, 2, v[4].X, v[4].Y, cs[4], w, h);
            pg.Sh($"{Mesh("/ShadingType 4 /BitsPerCoordinate 16 /BitsPerFlag 8", data, w, h)} 0 R");
        });

        Tile(8, "Lattice-form mesh (type 5)", "grid of vertices with bilinear colour", (w, h) =>
        {
            const int rows = 5, cols_ = 6;
            var data = new List<byte>();
            string[] pal = [P["indigo"], P["sky"], P["emerald"], P["amber"], P["rose"], P["violet"]];
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols_; c++)
                {
                    var x = 6 + c * (w - 12) / (cols_ - 1);
                    var y = 6 + r * (h - 12) / (rows - 1) + 10 * Math.Sin(c * 1.1 + r);
                    U16(data, Coord(x, 0, w)); U16(data, Coord(Math.Min(Math.Max(y, 0), h), 0, h));
                    Rgb(data, Mix(pal[c], pal[(c + r) % 6], 0.5));
                }
            pg.Sh($"{Mesh($"/ShadingType 5 /BitsPerCoordinate 16 /VerticesPerRow {cols_}", data, w, h)} 0 R");
        });

        Tile(9, "Coons patch mesh (type 6)", "one patch, four curved boundary edges", (w, h) =>
        {
            double x0 = 10, y0 = 10, x1 = w - 10, y1 = h - 10;
            (double, double)[] pts =
            [
                (x0, y0), (x0 - 6, y0 + 40), (x0 + 14, y0 + 70), (x0, y1),
                (x0 + 40, y1 + 8), (x1 - 40, y1 - 24), (x1, y1),
                (x1 + 8, y1 - 40), (x1 - 12, y0 + 40), (x1, y0),
                (x1 - 40, y0 - 14), (x0 + 40, y0 + 22),
            ];
            var data = Patch(pts, [P["rose"], P["amber"], P["emerald"], P["indigo"]], w, h);
            pg.Sh($"{Mesh("/ShadingType 6 /BitsPerCoordinate 16 /BitsPerFlag 8", [.. data], w, h)} 0 R");
        });

        Tile(10, "Tensor-product patch (type 7)", "sixteen control points incl. interior", (w, h) =>
        {
            double x0 = 10, y0 = 10, x1 = w - 10, y1 = h - 10;
            (double, double)[] pts =
            [
                (x0, y0), (x0, y0 + 30), (x0, y0 + 60), (x0, y1),
                (x0 + 40, y1), (x1 - 40, y1), (x1, y1),
                (x1, y1 - 30), (x1, y0 + 30), (x1, y0),
                (x1 - 40, y0), (x0 + 40, y0),
                (x0 + 30, y0 + 30), (x0 + 30, y1 - 30), (x1 - 30, y1 - 30), (x1 - 30, y0 + 70),
            ];
            var data = Patch(pts, [P["indigo"], P["sky"], P["emerald"], P["amber"]], w, h);
            pg.Sh($"{Mesh("/ShadingType 7 /BitsPerCoordinate 16 /BitsPerFlag 8", [.. data], w, h)} 0 R");
        });

        Tile(11, "Shading pattern (PatternType 2)", "a shading used as the stroke colour", (w, h) =>
        {
            var shp = d.Add($"<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 {N(ox)} {N(oy)}] /Shading {Axial(0, 0, 150, 120, [(0, P["teal"]), (1, P["violet"])])} >>");
            pg.Fill(P["ink"]); pg.Rect(0, 0, w, h); pg.FillPath();
            pg.Save(); pg.PatternFill(shp); pg.PatternFill(shp, stroke: true);
            pg.Width(10); pg.Join(1);
            pg.Move(20, 30); pg.Curve(40, 110, 70, 110, 80, 60); pg.Curve(90, 20, 110, 20, w - 20, 100); pg.StrokePath(); pg.Restore();
        });
        d.Save(path);
    }
}
