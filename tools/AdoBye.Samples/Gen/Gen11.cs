using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.K;
using static AdoBye.Samples.Kit.Theme;
using AdoBye.Samples.Kit;

namespace AdoBye.Samples.Gen;

static class Gen11
{
    public const string Out = "11-acroform.pdf";

    public static void Build(string path)
    {
        var d = new Doc("Fillable form", subject: "AcroForm: text, multiline, comb, checkbox, radio, combo, list, push buttons", lang: "en-US");
        var t = new Faces(d);
        var helv = d.Std("Helvetica");
        var helvB = d.Std("Helvetica-Bold");
        var zadb = d.Std("ZapfDingbats", "");
        var (aw, ah) = A4;
        var pg = d.Page(aw, ah);
        Background(pg, P["paper"]);
        PageHeader(pg, t, "Event registration", "Forms  /  AcroForm", 1, 1);
        pg.Text(t.Sans, 11, M, pg.H - 106, "A fillable PDF form. Every widget has normal (and where relevant down) appearance streams.", color: P["slate"]);
        var fields = new List<int>();
        var W = pg.W - 2 * M;
        var fr = helv.Ref;

        const string Ink = "0.06 0.09 0.16";

        void FieldBox(Content c, double w, double h, Color? fill = null, Color? stroke = null, double r = 3)
        {
            c.Fill(fill ?? P["white"]); c.Stroke(stroke ?? P["muted"]); c.Width(1); c.RRect(0.5, 0.5, w - 1, h - 1, r); c.FillStroke();
        }

        int TxAp(double w, double h, string val, double size = 11, bool multiline = false, int comb = 0)
        {
            var xf = new XForm(d, [0, 0, w, h]);
            var helvName = xf.Reg("Font", $"{fr} 0 R");
            FieldBox(xf, w, h);
            xf.Raw("/Tx BMC q");
            xf.Raw($"BT /{helvName} {N(size)} Tf {Ink} rg");
            if (multiline)
            {
                var y = h - 6 - size;
                foreach (var ln in Wrap(helv, size, val, w - 12, kern: false))
                {
                    xf.Raw($"1 0 0 1 6 {N(y)} Tm {PStr(ln)} Tj"); y -= size * 1.3;
                }
            }
            else if (comb > 0)
            {
                var cw = (w - 2) / comb;
                for (var i = 0; i < val.Length; i++)
                {
                    var ch = val[i].ToString();
                    xf.Raw($"1 0 0 1 {N(1 + i * cw + (cw - helv.Width(ch, size, false)) / 2)} {N((h - size) / 2 + 2.5)} Tm {PStr(ch)} Tj");
                }
            }
            else xf.Raw($"1 0 0 1 6 {N((h - size) / 2 + 2.5)} Tm {PStr(val)} Tj");
            xf.Raw("ET Q EMC");
            if (comb > 0)
            {
                xf.Stroke(P["mist"]); xf.Width(0.75);
                for (var i = 1; i < comb; i++)
                {
                    var x = 1 + i * (w - 2) / comb;
                    xf.Move(x, 3); xf.Line(x, h - 3); xf.StrokePath();
                }
            }
            return xf.Build();
        }

        void Label(double x, double y, string s) =>
            pg.Text(t.SansB, 9, x, y, s.ToUpperInvariant(), color: P["slate"], tracking: 0.8, kern: false);

        void TextField(string name, double x, double ytop, double w, double h, string val = "", bool multiline = false, int comb = 0)
        {
            var ff = (multiline ? 4096 : 0) | (comb > 0 ? 1 << 24 : 0);
            var a = TxAp(w, h, val, multiline: multiline, comb: comb);
            var extra = comb > 0 ? $"/MaxLen {comb} " : "";
            var r = d.Add($"<< /Type /Annot /Subtype /Widget /FT /Tx /T {PStr(name)} /TU {PStr(name)} /Ff {ff} {extra}/V {PStr(val)} " +
                          $"/Rect [{N(x)} {N(ytop - h)} {N(x + w)} {N(ytop)}] /F 4 /DA (/Helv 11 Tf 0.06 0.09 0.16 rg) /MK << /BC [0.58 0.64 0.72] /BG [1 1 1] >> " +
                          $"/P {pg.Ref} 0 R /AP << /N {a} 0 R >> >>");
            pg.Annots.Add(r); fields.Add(r);
        }

        var half = (W - 18) / 2;
        var y = pg.H - 150;
        Label(M, y, "Full name"); Label(M + half + 18, y, "Email");
        TextField("fullName", M, y - 6, half, 26, "Ada Lovelace");
        TextField("email", M + half + 18, y - 6, half, 26, "ada@example.org");
        y -= 56;
        Label(M, y, "Organisation"); Label(M + half + 18, y, "Postal code (comb, 8 cells)");
        TextField("org", M, y - 6, half, 26, "Analytical Engines Ltd");
        TextField("postal", M + half + 18, y - 6, half, 26, "SW1A1AA", comb: 8);
        y -= 56;
        Label(M, y, "Comments (multiline)");
        TextField("comments", M, y - 6, W, 70, "Looking forward to the workshop on document engineering. Please seat me near the demo stage and send the slides afterwards.", multiline: true);
        y -= 110;

        // combo + list
        void Choice(string name, double x, double ytop, double w, double h, string[] opts, string val, bool combo)
        {
            var ff = combo ? 131072 : 0;
            var sel = Math.Max(0, Array.IndexOf(opts, val));
            const double row = 15;
            var xf = new XForm(d, [0, 0, w, h]);
            var helvName = xf.Reg("Font", $"{fr} 0 R");
            FieldBox(xf, w, h);
            xf.Raw("/Tx BMC q");
            if (combo)
            {
                xf.Raw($"BT /{helvName} 11 Tf {Ink} rg 1 0 0 1 6 {N((h - 11) / 2 + 2.5)} Tm {PStr(val)} Tj ET");
                xf.Fill(P["slate"]); xf.Move(w - 18, h / 2 + 3); xf.Line(w - 8, h / 2 + 3); xf.Line(w - 13, h / 2 - 3); xf.Close(); xf.FillPath();
            }
            else
            {
                for (var i = 0; i < opts.Length; i++)
                {
                    var yy = h - 4 - (i + 1) * row;
                    if (i == sel) { xf.Fill(P["indigo"]); xf.Rect(2, yy, w - 4, row); xf.FillPath(); }
                    xf.Raw($"BT /{helvName} 10.5 Tf {(i == sel ? "1 1 1" : Ink)} rg 1 0 0 1 8 {N(yy + 4)} Tm {PStr(opts[i])} Tj ET");
                }
            }
            xf.Raw("Q EMC");
            var a = xf.Build();
            var opt = string.Join(" ", opts.Select(PStr));
            var extra = combo ? "" : $"/I [{sel}] ";
            var r = d.Add($"<< /Type /Annot /Subtype /Widget /FT /Ch /T {PStr(name)} /Ff {ff} /Opt [{opt}] /V {PStr(val)} {extra}" +
                          $"/Rect [{N(x)} {N(ytop - h)} {N(x + w)} {N(ytop)}] /F 4 /DA (/Helv 11 Tf 0 g) /MK << /BC [0.58 0.64 0.72] >> /P {pg.Ref} 0 R /AP << /N {a} 0 R >> >>");
            pg.Annots.Add(r); fields.Add(r);
        }

        Label(M, y, "Ticket type (combo box)"); Label(M + half + 18, y, "Track (list box)");
        Choice("ticket", M, y - 6, half, 26, ["Standard", "Student", "Speaker", "Sponsor"], "Speaker", true);
        Choice("track", M + half + 18, y - 6, half, 56, ["Rendering", "Parsing", "Editing"], "Parsing", false);
        y -= 76;

        // checkboxes
        Label(M, y, "Options (check boxes)");
        (string Name, string Text, bool On)[] options = [("newsletter", "Subscribe to the newsletter", true), ("lunch", "Vegetarian lunch", false), ("badge", "Print my badge in advance", true)];
        const double s = 16;
        for (var i = 0; i < options.Length; i++)
        {
            var (nm, text, on) = options[i];
            var cy = y - 24 - i * 24;
            int BoxAp(bool checkedBox)
            {
                var xf = new XForm(d, [0, 0, s, s]);
                xf.Reg("Font", $"{zadb.Ref} 0 R");
                FieldBox(xf, s, s);
                if (checkedBox)
                {
                    xf.Stroke(P["indigo"]); xf.Width(2); xf.Cap(1); xf.Join(1); xf.Move(3.5, 8.5); xf.Line(6.8, 4.5); xf.Line(12.5, 12); xf.StrokePath();
                }
                return xf.Build();
            }
            var onA = BoxAp(true);
            var offA = BoxAp(false);
            var state = on ? "Yes" : "Off";
            var r = d.Add($"<< /Type /Annot /Subtype /Widget /FT /Btn /T {PStr(nm)} /V /{state} /AS /{state} " +
                          $"/Rect [{N(M)} {N(cy)} {N(M + s)} {N(cy + s)}] /F 4 /DA (/ZaDb 0 Tf 0 g) /MK << /CA (4) /BC [0.58 0.64 0.72] >> /P {pg.Ref} 0 R " +
                          $"/AP << /N << /Yes {onA} 0 R /Off {offA} 0 R >> /D << /Yes {onA} 0 R /Off {offA} 0 R >> >> >>");
            pg.Annots.Add(r); fields.Add(r);
            pg.Text(t.Sans, 11, M + 26, cy + 3.5, text, color: P["ink"]);
        }

        // radio group
        var rx = M + half + 18;
        Label(rx, y, "Day (radio buttons)");
        var grp = d.New();
        var kids = new List<int>();
        string[] days = ["Thursday", "Friday", "Saturday"];
        for (var i = 0; i < days.Length; i++)
        {
            var day = days[i];
            var cy = y - 24 - i * 24;
            int RadioAp(bool checkedBox)
            {
                var xf = new XForm(d, [0, 0, s, s]);
                xf.Fill(P["white"]); xf.Stroke(P["muted"]); xf.Width(1); xf.Circle(s / 2, s / 2, s / 2 - 0.5); xf.FillStroke();
                if (checkedBox) { xf.Fill(P["indigo"]); xf.Circle(s / 2, s / 2, 4.2); xf.FillPath(); }
                return xf.Build();
            }
            var onA = RadioAp(true);
            var offA = RadioAp(false);
            var k = d.Add($"<< /Type /Annot /Subtype /Widget /Parent {grp} 0 R /AS /{(i == 1 ? day : "Off")} /Rect [{N(rx)} {N(cy)} {N(rx + s)} {N(cy + s)}] /F 4 " +
                          $"/MK << /BC [0.58 0.64 0.72] /CA (l) >> /P {pg.Ref} 0 R /AP << /N << /{day} {onA} 0 R /Off {offA} 0 R >> >> >>");
            kids.Add(k);
            pg.Annots.Add(k);
            pg.Text(t.Sans, 11, rx + 26, cy + 3.5, day, color: P["ink"]);
        }
        d.Put(grp, $"<< /FT /Btn /Ff {32768 | 16384} /T (day) /V /Friday /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] >>");
        fields.Add(grp);

        // push buttons
        y -= 24 + 3 * 24 + 10;
        const double bw = 130, bh = 32;
        void Button(string name, double x, string caption, string action, bool primary)
        {
            var xf = new XForm(d, [0, 0, bw, bh]);
            xf.Fill(primary ? P["indigo"] : P["white"]); xf.Stroke(P["indigo"]); xf.Width(1.2); xf.RRect(0.6, 0.6, bw - 1.2, bh - 1.2, 6); xf.FillStroke();
            var tw = helvB.Width(caption, 11, false);
            xf.Text(helvB, 11, (bw - tw) / 2, (bh - 11) / 2 + 2.5, caption, color: primary ? P["white"] : P["indigo"], kern: false);
            var a = xf.Build();
            var r = d.Add($"<< /Type /Annot /Subtype /Widget /FT /Btn /Ff 65536 /T {PStr(name)} /Rect [{N(x)} {N(y - bh)} {N(x + bw)} {N(y)}] /F 4 " +
                          $"/MK << /CA {PStr(caption)} /BG [1 1 1] >> /DA (/Helv 11 Tf 0 g) /A {action} /P {pg.Ref} 0 R /AP << /N {a} 0 R >> >>");
            pg.Annots.Add(r); fields.Add(r);
        }
        Button("submit", M, "Submit", "<< /S /SubmitForm /F << /FS /URL /F (https://example.org/submit) >> /Flags 4 >>", true);
        Button("reset", M + bw + 14, "Reset form", "<< /S /ResetForm >>", false);
        pg.Text(t.Sans, 9, M + 2 * bw + 28, y - 20, "Submit posts FDF; Reset clears values.", color: P["muted"]);

        d.Cat.Add($"/AcroForm << /Fields [{string.Join(" ", fields.Select(f => $"{f} 0 R"))}] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv {helv.Ref} 0 R /ZaDb {zadb.Ref} 0 R >> >> /NeedAppearances false >>");
        d.Save(path);
    }
}
