using AdoBye.Samples.Kit;
using static AdoBye.Samples.Kit.Helpers;
using static AdoBye.Samples.Kit.Theme;

namespace AdoBye.Samples.Gen;

sealed partial class Gen28
{
    sealed record Col(string Head, double W, bool Bold = false, bool Pill = false, double Size = 8.4);

    static (Color Bg, Color Fg) StatusColours(string s) => s switch
    {
        "Done" => ("#D1FAE5", "#064E3B"),
        "Done*" => ("#ECFCCB", "#365314"),
        "Partial" => ("#FEF3C7", "#78350F"),
        "Todo" => ("#FFE4E6", "#881337"),
        "Working name" => ("#E0E7FF", "#312E81"),
        "In the API" => ("#E0E7FF", "#312E81"),
        "Legacy" => ("#F1F5F9", "#334155"),
        "Cancelled" => ("#FFE4E6", "#881337"),
        _ => ("#E0E7FF", "#312E81"),
    };

    /// <summary>Tagged table (Table / TR / TH scope Column / TD) with wrapped cells and optional status pills. Returns the y below the last row.</summary>
    double DataTable(double x, double top, IReadOnlyList<Col> cols, IReadOnlyList<string[]> rows, string summary, double minRow = 24)
    {
        var total = cols.Sum(c => c.W);
        var table = new Node("Table", _sec, attrs: $"<< /O /Table /Summary {K.PStr(summary)} >>");
        const double hh = 22;
        Art(() => Box(_pg, x, top - hh, total, hh, fill: Ink, r: 5));
        var th = new Node("TR", table);
        var cx = x;
        foreach (var c in cols)
        {
            var px = cx;
            var cell = new Node("TH", th, attrs: "<< /O /Table /Scope /Column >>");
            Mc(cell, () => Tx(_t.SansB, 7.6, px + 8, top - hh / 2 - 2.6, c.Head.ToUpperInvariant(), White, tracking: 1.1));
            cx += c.W;
        }
        var y = top - hh;
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            double h = minRow;
            for (var ci = 0; ci < cols.Count; ci++)
            {
                if (cols[ci].Pill) continue;
                var f = cols[ci].Bold ? _t.SansB : _t.Sans;
                h = Math.Max(h, Wrap(f, cols[ci].Size, row[ci], cols[ci].W - 16).Count * cols[ci].Size * 1.36 + 13);
            }
            var rowTop = y;
            var rh = h;
            if (ri % 2 == 0) Art(() => Box(_pg, x, rowTop - rh, total, rh, fill: White));
            Art(() => Rule(_pg, x, rowTop - rh, x + total, Mist, 0.5));
            var tr = new Node("TR", table);
            cx = x;
            for (var ci = 0; ci < cols.Count; ci++)
            {
                var col = cols[ci];
                var px = cx;
                var text = row[ci];
                var cell = new Node("TD", tr);
                Mc(cell, () =>
                {
                    if (col.Pill)
                    {
                        var (bg, fg) = StatusColours(text);
                        var pw = _t.SansB.Width(text, 7.8) + 16;
                        Pill(px + 8, rowTop - 19, pw, 15, text, bg, fg, 7.8);
                    }
                    else
                    {
                        var f = col.Bold ? _t.SansB : _t.Sans;
                        Para(f, col.Size, px + 8, rowTop - 7 - col.Size * 0.78, col.W - 16, text, col.Bold ? Ink : (Color)"#1E293B", col.Size * 1.36);
                    }
                });
                cx += col.W;
            }
            y -= rh;
        }
        return y;
    }
}
