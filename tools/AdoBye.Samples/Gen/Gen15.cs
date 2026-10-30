using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

static class Gen15
{
    public const string Out = "15-optional-content.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Optional content layers", subject: "OCGs, default visibility, layer order and groups, radio-button group, print/view usage, OCMD, /OC on XObjects and annotations", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Layers", "Optional content  /  OCG", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "Toggle layers in the viewer's Layers panel. The alternate palette starts hidden.", color: P["slate"]);

        int Ocg(string name, string usage = "") => d.Add($"<< /Type /OCG /Name {K.Utf16(name)} {usage} >>");
        var baseL = Ocg("Terrain");
        var grid = Ocg("Grid");
        var routes = Ocg("Routes");
        var labels = Ocg("Labels");
        var alt = Ocg("Alternate palette (hidden)");
        var wm = Ocg("Print-only watermark", "/Usage << /Print << /PrintState /ON >> /View << /ViewState /OFF >> >>");
        int day = Ocg("Day"), night = Ocg("Night");

        void Layer(Content c, int oc) => c.Raw($"/OC /{c.Reg("Properties", $"{oc} 0 R")} BDC");

        double mx = M, my = 150, mw = pg.W - 2 * M, mh = 520;
        pg.Save(); pg.RRect(mx, my, mw, mh, 14); pg.Clip();
        Layer(pg, day); pg.Fill("#E0F2FE"); pg.Rect(mx, my, mw, mh); pg.FillPath(); pg.Raw("EMC");
        Layer(pg, night); pg.Fill("#0F172A"); pg.Rect(mx, my, mw, mh); pg.FillPath(); pg.Raw("EMC");
        Layer(pg, baseL);
        var bands = new (string Col, double Amp, double Y)[] { (P["emerald"], 40, 470), (P["teal"], 55, 390), (P["indigo"], 45, 300), (P["indigo_d"], 60, 200) };
        for (var i = 0; i < bands.Length; i++)
        {
            var (col, amp, yy) = bands[i];
            pg.Save(); pg.Gs(("ca", 0.85)); pg.Fill(col);
            pg.Move(mx, my);
            for (var k = 0; k <= 40; k++)
                pg.Line(mx + k * mw / 40, my + yy - 150 + Math.Sin(k * 0.45 + i * 1.7) * amp + Math.Sin(k * 0.17 + i) * 25);
            pg.Line(mx + mw, my); pg.Close(); pg.FillPath(); pg.Restore();
        }
        pg.Raw("EMC");
        Layer(pg, alt);
        pg.Save(); pg.Gs(("ca", 0.6), ("BM", "/Multiply")); pg.Fill(P["rose"]); pg.Circle(mx + 360, my + 330, 150); pg.FillPath(); pg.Fill(P["amber"]); pg.Circle(mx + 200, my + 190, 110); pg.FillPath(); pg.Restore();
        pg.Raw("EMC");
        Layer(pg, grid);
        pg.Save(); pg.Stroke("#FFFFFF"); pg.Gs(("CA", 0.35)); pg.Width(0.6);
        for (var i = 0; i <= 10; i++) { pg.Move(mx + i * mw / 10, my); pg.Line(mx + i * mw / 10, my + mh); pg.StrokePath(); }
        for (var j = 0; j <= 10; j++) { pg.Move(mx, my + j * mh / 10); pg.Line(mx + mw, my + j * mh / 10); pg.StrokePath(); }
        pg.Restore(); pg.Raw("EMC");
        Layer(pg, routes);
        pg.Save(); pg.Stroke(P["amber"]); pg.Width(3.2); pg.Cap(1); pg.Join(1); pg.Dash([9, 6]);
        pg.Move(mx + 40, my + 90); pg.Curve(mx + 120, my + 260, mx + 190, my + 60, mx + 250, my + 230); pg.Curve(mx + 300, my + 350, mx + 360, my + 300, mx + 440, my + 440); pg.StrokePath();
        pg.Dash([]); pg.Stroke(P["rose"]); pg.Move(mx + 60, my + 420); pg.Curve(mx + 150, my + 330, mx + 200, my + 420, mx + 300, my + 380); pg.StrokePath(); pg.Restore();
        foreach (var (px, py) in new[] { (mx + 40, my + 90), (mx + 250, my + 230), (mx + 440, my + 440) })
        {
            pg.Save(); pg.Fill("#FFFFFF"); pg.Stroke(P["amber"]); pg.Width(2.5); pg.Circle(px, py, 8); pg.FillStroke(); pg.Restore();
        }
        pg.Raw("EMC");
        Layer(pg, labels);
        foreach (var (px, py, nm, left) in new[] { (mx + 40, my + 90, "Harbour", false), (mx + 250, my + 230, "Ridge", false), (mx + 440, my + 440, "Summit", true) })
        {
            var w = t.SansB.Width(nm, 10);
            var bx = left ? px - 12 - w - 14 : px + 12;
            Box(pg, bx, py - 8, w + 14, 20, fill: P["ink"], r: 10);
            pg.Text(t.SansB, 10, bx + 7, py - 1.5, nm, color: "#FFFFFF");
        }
        pg.Raw("EMC");
        Layer(pg, wm);
        pg.Save(); pg.Gs(("ca", 0.18)); pg.Fill("#000000"); pg.Translate(mx + mw / 2, my + mh / 2); pg.Rotate(35);
        pg.Text(t.SerifB, 70, -t.SerifB.Width("PRINT COPY", 70) / 2, -22, "PRINT COPY", color: "#000000", kern: false); pg.Restore();
        pg.Raw("EMC");
        pg.Restore();

        // form XObject with /OC and an OCMD requiring Routes AND Labels
        var ocmd = d.Add($"<< /Type /OCMD /OCGs [{routes} 0 R {labels} 0 R] /P /AllOn >>");
        var xf = new XForm(d, [0, 0, 190, 40]);
        Box(xf, 0, 0, 190, 40, fill: P["indigo"], r: 8);
        xf.Text(t.SansB, 10, 14, 15, "Visible when Routes + Labels on", color: "#FFFFFF", kern: false);
        var xr = xf.Build(extra: $"/OC {ocmd} 0 R");
        pg.Save(); pg.Cm(1, 0, 0, 1, M, 100); pg.XObj(xr); pg.Restore();

        pg.Text(t.SansB, 8, M + 210, 126, "LAYER USAGE", color: P["indigo"], tracking: 1.6, kern: false);
        pg.Text(t.Sans, 9, M + 210, 108, "On: Terrain, Grid, Routes, Labels. Off: Alternate palette.", color: P["slate"], kern: false);
        pg.Text(t.Sans, 9, M + 210, 94, "Day / Night is a radio group; the watermark prints only.", color: P["slate"], kern: false);

        var order = $"[{baseL} 0 R {grid} 0 R {routes} 0 R {labels} 0 R {alt} 0 R [(Background) {day} 0 R {night} 0 R] {wm} 0 R]";
        d.Cat.Add($"/OCProperties << /OCGs [{baseL} 0 R {grid} 0 R {routes} 0 R {labels} 0 R {alt} 0 R {wm} 0 R {day} 0 R {night} 0 R] " +
            $"/D << /Name (Default) /BaseState /ON /OFF [{alt} 0 R {night} 0 R {wm} 0 R] /Order {order} /RBGroups [[{day} 0 R {night} 0 R]] " +
            $"/AS [<< /Event /Print /Category [/Print] /OCGs [{wm} 0 R] >> << /Event /View /Category [/View] /OCGs [{wm} 0 R] >>] >> >>");
        d.Save(path, version: "1.7");
    }
}
