using System.Globalization;
using FreeDF.Samples.Kit;
using static FreeDF.Samples.Kit.Helpers;
using static FreeDF.Samples.Kit.Theme;

namespace FreeDF.Samples.Gen;

static class Gen22
{
    public const string Out = "22-invoice.pdf";

    static readonly (string Name, string Desc, int Qty, double Rate)[] Items =
    [
        ("Brand identity system", "Logo suite, colour and type guidelines", 1, 4800.00),
        ("Website design", "12 templates, responsive, design tokens", 1, 7200.00),
        ("Front-end build", "Static site, accessibility audit (WCAG 2.2 AA)", 64, 95.00),
        ("Illustration set", "24 spot illustrations, vector + PNG", 24, 120.00),
        ("Content strategy workshop", "Two half-day sessions", 2, 850.00),
        ("Print collateral", "Brochure, business cards, letterhead", 1, 1950.00),
    ];

    static string Money(double v) => v.ToString("N2", CultureInfo.InvariantCulture);

    public static void Build(string path)
    {
        var d = new Doc("Invoice INV-2026-0142", author: "Northlight Studio", subject: "Invoice with line-item table, totals and payment details", lang: "en-US");
        var t = new Faces(d);
        var pg = d.Page(A4.W, A4.H);
        var W = pg.W - 2 * M;
        Background(pg, P["white"]);
        Box(pg, 0, pg.H - 14, pg.W, 14, fill: P["indigo"]);
        LogoMark(pg, M, pg.H - 92, 30);
        pg.Text(t.SerifB, 18, M + 42, pg.H - 79, "Northlight Studio", color: P["ink"]);
        pg.Text(t.Sans, 9, M + 42, pg.H - 92, "Design & engineering for the web", color: P["muted"]);
        pg.Text(t.SerifB, 34, pg.W - M - t.SerifB.Width("Invoice", 34), pg.H - 84, "Invoice", color: P["indigo_d"]);

        var y = pg.H - 150;
        (string, string)[] meta = [("Invoice no.", "INV-2026-0142"), ("Issued", "15 January 2026"), ("Due", "14 February 2026"), ("PO reference", "PO-88213")];
        var cw = W / 4;
        for (var i = 0; i < meta.Length; i++)
        {
            var x = M + i * cw;
            Eyebrow(pg, t, x, y, meta[i].Item1, P["muted"]);
            pg.Text(t.SansB, 11, x, y - 17, meta[i].Item2, color: P["ink"]);
        }
        Rule(pg, M, y - 32, M + W);

        var y2 = y - 62;
        (string, string[])[] parties =
        [
            ("From", ["Northlight Studio Ltd", "14 Harbour Row", "Bristol BS1 4RB", "United Kingdom", "VAT GB 123 4567 89"]),
            ("Billed to", ["Meridian Analytics Inc.", "Attn: Priya Raman", "500 Market Street, Suite 12", "San Francisco, CA 94105", "United States"]),
        ];
        for (var i = 0; i < parties.Length; i++)
        {
            var x = M + i * (W / 2);
            Eyebrow(pg, t, x, y2, parties[i].Item1);
            for (var j = 0; j < parties[i].Item2.Length; j++)
                pg.Text(j == 0 ? t.SansB : t.Sans, 10.5, x, y2 - 18 - j * 14.5, parties[i].Item2[j], color: j == 0 ? P["ink"] : P["slate"]);
        }

        var ty = y2 - 120;
        (double X, string H, bool Left)[] cols = [(0, "Description", true), (W - 250, "Qty", false), (W - 170, "Rate", false), (W, "Amount", false)];
        Box(pg, M, ty - 6, W, 26, fill: P["ink"], r: 4);
        foreach (var (x, h, left) in cols)
        {
            var xx = left ? M + 12 : M + x - t.SansB.Width(h.ToUpperInvariant(), 8, tracking: 1.2) - 12;
            pg.Text(t.SansB, 8, xx, ty + 4, h.ToUpperInvariant(), color: "#FFFFFF", tracking: 1.2, kern: false);
        }
        var ry = ty - 6;
        double sub = 0;
        for (var i = 0; i < Items.Length; i++)
        {
            var (name, desc, q, rate) = Items[i];
            const double rh = 42;
            ry -= rh;
            if (i % 2 == 0) Box(pg, M, ry, W, rh, fill: P["paper"]);
            var amt = q * rate;
            sub += amt;
            pg.Text(t.SansB, 10.5, M + 12, ry + 24, name, color: P["ink"]);
            pg.Text(t.Sans, 9, M + 12, ry + 10, desc, color: P["muted"]);
            foreach (var (x, s, f) in new[] { (W - 250, $"{q}", t.Sans), (W - 170, Money(rate), t.Sans), (W, Money(amt), t.SansB) })
                pg.Text(f, 10.5, M + x - f.Width(s, 10.5) - 12, ry + 17, s, color: P["ink"]);
        }
        Rule(pg, M, ry, M + W);

        var tax = Math.Round(sub * 0.2, 2);
        var total = sub + tax;
        var tx = M + W - 240;
        y = ry - 30;
        foreach (var (k, v) in new[] { ("Subtotal", sub), ("VAT 20 %", tax) })
        {
            pg.Text(t.Sans, 10.5, tx, y, k, color: P["slate"]);
            pg.Text(t.Sans, 10.5, M + W - 12 - t.Sans.Width(Money(v), 10.5), y, Money(v), color: P["ink"]);
            y -= 22;
        }
        y -= 8;
        Box(pg, tx - 12, y - 18, 252, 38, fill: P["indigo"], r: 8);
        pg.Text(t.SansB, 11, tx, y - 4, "Total due (USD)", color: "#E0E7FF");
        pg.Text(t.SerifB, 17, M + W - 12 - t.SerifB.Width("$" + Money(total), 17), y - 6, "$" + Money(total), color: "#FFFFFF");

        var py = ry - 30;
        Eyebrow(pg, t, M, py, "Payment");
        var pay = new[] { ("Bank", "Northlight Studio Ltd, Monzo Business"), ("IBAN", "GB29 MONZ 0400 0412 3456 78"), ("BIC", "MONZGB2L"), ("Reference", "INV-2026-0142") };
        for (var j = 0; j < pay.Length; j++)
        {
            var (k, v) = pay[j];
            pg.Text(t.Sans, 9.5, M, py - 18 - j * 15, k, color: P["muted"]);
            pg.Text(k is "IBAN" or "BIC" ? t.Mono : t.Sans, 9.5, M + 62, py - 18 - j * 15, v, color: P["ink"], kern: false);
        }
        Paragraph(pg, t.Sans, 9, M, py - 100, W * 0.62,
            "Payment is due within 30 days. Late payments incur 1.5 % monthly interest. Thank you for your business; it was a pleasure working with the Meridian team.",
            color: P["muted"], leading: 13);
        Rule(pg, M, 56, M + W);
        pg.Text(t.Sans, 8, M, 40, "Northlight Studio Ltd · Registered in England No. 09871234 · hello@northlight.example", color: P["muted"], kern: false);
        pg.Text(t.Sans, 8, M + W - t.Sans.Width("Page 1 of 1", 8), 40, "Page 1 of 1", color: P["muted"]);
        d.Save(path);
    }
}
