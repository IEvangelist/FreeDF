using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen21
{
    public const string Out = "21-brochure.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Brochure: Harbour & Hearth tri-fold", "Pdf sample generator",
            "US Letter landscape tri-fold: outside and inside spreads, vector illustration, clipping, shadings, TrimBox/BleedBox", "", "en-US");
        var t = new Faces(d);
        const double W = 792, H = 612, pw = W / 3, BL = 9;
        var (outside, inside) = (d.Page(W, H), d.Page(W, H));
        foreach (var pg in new[] { outside, inside })
        {
            pg.Boxes["BleedBox"] = [0, 0, W, H];
            pg.Boxes["TrimBox"] = [BL, BL, W - BL, H - BL];
        }
        const string Sea = "#0E4C5A", Sand = "#F6EFE4", Coral = "#E4572E", Deep = "#0A2E36";

        void FoldGuides(Page pg)
        {
            pg.Save(); pg.Stroke("#CBD5E1"); pg.Width(0.4); pg.Dash([3, 3]);
            foreach (var k in new[] { 1, 2 })
            {
                pg.Move(pw * k, 0); pg.Line(pw * k, 6); pg.StrokePath();
                pg.Move(pw * k, H - 6); pg.Line(pw * k, H); pg.StrokePath();
            }
            pg.Restore();
        }

        // outside: [inside flap | back | front]
        var o = outside;
        Background(o, Sand);
        const double fx = 2 * pw;
        o.Save(); o.Rect(fx, 0, pw, H); o.Clip();
        o.Sh(Axial(fx, H, fx + pw, 0, [(0, "#0E7490"), (0.6, Sea), (1, Deep)]));
        o.Fill("#FDBA74"); o.Circle(fx + pw * 0.66, H - 270, 52); o.FillPath();
        (string Col, double Amp, double Y)[] waves = [("#0891B2", 14, 250), ("#0E7490", 18, 205), (Sea, 22, 160), (Deep, 26, 115)];
        for (var i = 0; i < waves.Length; i++)
        {
            var (col, amp, yy) = waves[i];
            o.Save(); o.Fill(col); o.Gs(("ca", 0.9));
            o.Move(fx, 0); o.Line(fx, yy);
            const int steps = 6;
            for (var s = 0; s < steps; s++)
            {
                var x0 = fx + s * pw / steps;
                var x1 = x0 + pw / steps;
                var sign = (s + i) % 2 == 0 ? 1 : -1;
                o.Curve(x0 + pw / steps * 0.35, yy + sign * amp, x0 + pw / steps * 0.65, yy + sign * amp, x1, yy);
            }
            o.Line(fx + pw, 0); o.Close(); o.FillPath(); o.Restore();
        }
        o.Restore();
        o.Text(t.SansB, 9, fx + 30, H - 56, "BRISTOL  ·  EST. 1987", color: "#FED7AA", tracking: 2.2, kern: false);
        o.Text(t.SerifB, 44, fx + 30, H - 112, "Harbour", color: "#FFFFFF");
        o.Text(t.SerifB, 44, fx + 30, H - 158, "& Hearth", color: "#FFFFFF");
        o.Text(t.SerifI, 14, fx + 30, 62, "Seafood, fire & good company", color: "#FFEDD5");

        const double bx = pw;
        Eyebrow(o, t, bx + 30, H - 60, "Find us", color: Coral);
        o.Text(t.SerifB, 20, bx + 30, H - 92, "Visit the Quay", color: Sea);
        var y = Paragraph(o, t.Sans, 10, bx + 30, H - 122, pw - 60, "Our kitchen sits on the waterside, five minutes from the ferry landing. Reservations are recommended at weekends and during festival weeks.", leading: 15, color: "#334155");
        y -= 14;
        foreach (var (lab, val) in new[] { ("ADDRESS", "12 Harbour Row, Bristol BS1 4RB"), ("OPEN", "Tue–Sun, 12:00 – 22:30"), ("PHONE", "+44 117 496 0123"), ("WEB", "harbourandhearth.example") })
        {
            o.Text(t.SansB, 7.5, bx + 30, y, lab, color: Coral, tracking: 1.6, kern: false);
            o.Text(t.Sans, 10, bx + 30, y - 14, val, color: Sea);
            y -= 40;
        }
        double mx = bx + 30, my = 52, mw = pw - 60, mh = 120;
        o.Save(); o.RRect(mx, my, mw, mh, 8); o.Clip();
        o.Fill("#E7DFD0"); o.Rect(mx, my, mw, mh); o.FillPath();
        o.Fill("#A5D8E6"); o.Move(mx, my + 20); o.Curve(mx + 60, my + 60, mx + 90, my + 10, mx + mw, my + 50); o.Line(mx + mw, my); o.Line(mx, my); o.Close(); o.FillPath();
        o.Stroke("#FFFFFF"); o.Width(5); o.Move(mx, my + 85); o.Line(mx + mw, my + 95); o.StrokePath(); o.Move(mx + 70, my + mh); o.Line(mx + 90, my + 30); o.StrokePath();
        o.Stroke("#D6CBB6"); o.Width(1); o.Dash([2, 2]); o.Move(mx + 20, my + mh); o.Line(mx + 40, my); o.StrokePath();
        o.Restore();
        o.Save(); o.Fill(Coral); o.Circle(mx + 90, my + 80, 6); o.FillPath(); o.Fill("#FFFFFF"); o.Circle(mx + 90, my + 80, 2.2); o.FillPath(); o.Restore();

        const double lx = 0;
        Box(o, lx + 30, 70, pw - 60, H - 150, fill: "#FFFFFF", r: 10);
        Eyebrow(o, t, lx + 50, H - 108, "Good to know", color: Coral);
        var ty = H - 138;
        foreach (var (h, b) in new[] { ("Allergies", "Ask us about any allergen; our menu marks the common ones."), ("Private dining", "The Lantern Room seats up to 24 for celebrations."), ("Sustainable", "Every fish is sourced from certified day-boat fisheries."), ("Gift cards", "Available in any amount at the counter or online.") })
        {
            o.Text(t.SerifB, 13, lx + 50, ty, h, color: Sea);
            ty = Paragraph(o, t.Sans, 9, lx + 50, ty - 16, pw - 100, b, leading: 13, color: "#475569") - 14;
        }
        FoldGuides(o);

        // inside: three panels
        var n = inside;
        Background(n, "#FFFFFF");
        Box(n, 0, 0, W, 90, fill: Sand);
        (string Title, string Col, (string, string, string)[] Items)[] menus =
        [
            ("To start", Coral, [("Smoked mackerel pâté", "toasted sourdough, pickled cucumber", "7.5"), ("Charred octopus", "romesco, chorizo crumb, lemon", "11"), ("Harbour chowder", "clams, potato, cream, chives", "9"), ("Burrata & peach", "basil oil, black pepper", "8.5")]),
            ("From the fire", Sea, [("Whole sea bream", "fennel, salsa verde, new potatoes", "26"), ("Wood-fired prawns", "garlic butter, chilli, flatbread", "22"), ("Dry-aged ribeye", "bone marrow butter, watercress", "34"), ("Hearth cauliflower", "tahini, pomegranate, dukkah", "17")]),
            ("To finish", "#B45309", [("Burnt honey tart", "crème fraîche, sea salt", "8"), ("Chocolate pot", "olive oil, flaked salt", "8"), ("Lemon posset", "shortbread, thyme", "7.5"), ("Local cheeses", "quince, oatcakes", "12")]),
        ];
        for (var i = 0; i < menus.Length; i++)
        {
            var (title, col, items) = menus[i];
            var x = i * pw;
            Eyebrow(n, t, x + 36, H - 56, $"0{i + 1}", color: col);
            n.Text(t.SerifB, 24, x + 36, H - 88, title, color: Sea);
            n.Save(); n.Fill(col); n.Rect(x + 36, H - 100, 28, 2.5); n.FillPath(); n.Restore();
            y = H - 138;
            foreach (var (name, desc, price) in items)
            {
                n.Text(t.SansB, 11, x + 36, y, name, color: "#0F172A");
                n.Text(t.SansB, 11, x + pw - 36 - t.SansB.Width(price, 11), y, price, color: col);
                n.Text(t.SansI, 9, x + 36, y - 14, desc, color: "#64748B");
                y -= 70;
            }
            if (i < 2)
            {
                n.Save(); n.Stroke("#E2E8F0"); n.Width(0.6); n.Move(x + pw, 100); n.Line(x + pw, H - 40); n.StrokePath(); n.Restore();
            }
        }
        n.Text(t.SerifI, 12, 36, 48, "Prices in pounds sterling, service not included. Please tell us about allergies.", color: Sea);
        LogoMark(n, W - 36 - 28, 38, 28, a: Sea, b: Coral);
        FoldGuides(n);
        d.Save(path);
    }
}
