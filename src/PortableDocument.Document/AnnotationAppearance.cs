using System.Globalization;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Generates /AP /N form XObjects (12.5.5) for authored annotations so every viewer renders them. Content is written in page space and the
/// BBox equals /Rect with an identity matrix. Everything is derived from the annotation dictionary, so regenerating after an edit, move or
/// resize is just another call.
/// </summary>
public static class AnnotationAppearance
{
    static readonly int[] Helvetica = [278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584];

    public static readonly string[] StandardStamps = ["Approved", "AsIs", "Confidential", "Departmental", "Draft", "Experimental", "Expired", "Final", "ForComment",
        "ForPublicRelease", "NotApproved", "NotForPublicRelease", "Sold", "TopSecret"];

    static string F(double v) => (Math.Abs(v) < 0.0005 ? 0 : v).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Width of <paramref name="text"/> in Helvetica at 1 unit font size (non-ASCII uses an average advance).</summary>
    public static double TextWidth(string text, double size)
    {
        var w = 0;
        foreach (var ch in text) w += ch is >= ' ' and <= '~' ? Helvetica[ch - ' '] : 556;
        return w * size / 1000.0;
    }

    /// <summary>Greedy word wrap with Helvetica metrics; long words are split by character.</summary>
    public static List<string> Wrap(string text, double size, double width)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var cur = new StringBuilder();
            foreach (var word in para.Split(' '))
            {
                var candidate = cur.Length == 0 ? word : cur + " " + word;
                if (TextWidth(candidate, size) <= width || cur.Length == 0 && TextWidth(word, size) <= width) { cur.Clear(); cur.Append(candidate); continue; }
                if (cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); }
                foreach (var ch in word)
                {
                    if (cur.Length > 0 && TextWidth(cur.ToString() + ch, size) > width) { lines.Add(cur.ToString()); cur.Clear(); }
                    cur.Append(ch);
                }
            }
            lines.Add(cur.ToString());
        }
        return lines;
    }

    static string Color(double[] c, bool stroke) => c.Length switch
    {
        1 => $"{F(c[0])} {(stroke ? "G" : "g")}\n",
        3 => $"{F(c[0])} {F(c[1])} {F(c[2])} {(stroke ? "RG" : "rg")}\n",
        4 => $"{F(c[0])} {F(c[1])} {F(c[2])} {F(c[3])} {(stroke ? "K" : "k")}\n",
        _ => "",
    };

    static string Lit(string s)
    {
        var sb = new StringBuilder("(");
        foreach (var ch in s)
        {
            var b = ch <= 0xFF ? (byte)ch : (byte)'?';
            if (b is (byte)'(' or (byte)')' or (byte)'\\') sb.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.Append(')').ToString();
    }

    static PdfDictionary Font(string baseFont) => new(new Dictionary<string, PdfObject>
    {
        ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName(baseFont), ["Encoding"] = new PdfName("WinAnsiEncoding"),
    });

    /// <summary>Bounds of a set of points (x,y pairs).</summary>
    public static double[] Bounds(IEnumerable<double> xy)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        var i = 0;
        double? px = null;
        foreach (var v in xy)
        {
            if (i++ % 2 == 0) { px = v; continue; }
            x0 = Math.Min(x0, px!.Value); x1 = Math.Max(x1, px.Value); y0 = Math.Min(y0, v); y1 = Math.Max(y1, v);
        }
        return x0 > x1 ? [0, 0, 0, 0] : [x0, y0, x1, y1];
    }

    /// <summary>Builds quads (TL, TR, BL, BR per box) from axis-aligned boxes given as x0,y0,x1,y1 (y up).</summary>
    public static double[] QuadsFromBoxes(IEnumerable<(double X0, double Y0, double X1, double Y1)> boxes)
    {
        var q = new List<double>();
        foreach (var (x0, y0, x1, y1) in boxes) q.AddRange([x0, y1, x1, y1, x0, y0, x1, y0]);
        return [.. q];
    }

    static bool HasEnds(PdfAnnotationSpec s) => s.LineStart is { } a && a != "None" || s.LineEnd is { } b && b != "None";

    static double[] Inflate(double[] b, double m) => [b[0] - m, b[1] - m, b[2] + m, b[3] + m];

    /// <summary>Computes /Rect for geometry-defined subtypes (markup, line, polygon, ink); returns null when the user-supplied rectangle is authoritative.</summary>
    public static double[]? RectFromGeometry(string subtype, PdfAnnotationSpec s, double borderWidth)
    {
        var bw = Math.Max(borderWidth, 1);
        switch (subtype)
        {
            case "Highlight" or "Underline" or "StrikeOut" or "Squiggly" or "Redact" when s.QuadPoints is { Length: >= 8 }:
                return Inflate(Bounds(s.QuadPoints), subtype is "Underline" or "Squiggly" or "StrikeOut" ? bw + 1 : 0);
            case "Line" when s.Line is { Length: >= 4 }:
                return Inflate(Bounds(s.Line), bw / 2 + (HasEnds(s) ? Math.Max(6, bw * 5) : 0));
            case "Polygon" or "PolyLine" when s.Vertices is { Length: >= 4 }:
                return Inflate(Bounds(s.Vertices), bw / 2 + (s.Cloud == true ? 12 : 0) + (subtype == "PolyLine" && HasEnds(s) ? Math.Max(6, bw * 5) : 0));
            case "Ink" when s.InkList is { Count: > 0 }:
                return Inflate(Bounds(s.InkList.SelectMany(p => p)), bw / 2 + 1);
            default: return null;
        }
    }

    /// <summary>Builds the normal appearance for <paramref name="a"/>. <paramref name="add"/> registers a new indirect object (used for images). Returns null for subtypes without a drawn appearance.</summary>
    public static PdfStream? Build(PdfDictionary a, Func<PdfObject?, PdfObject?> res, Func<PdfObject, PdfRef> add, PdfStream? previous = null, PdfAnnotationImage? image = null, bool viewer = false)
    {
        double[] Nums(string k) => res(a.Get(k)) is PdfArray arr ? arr.Items.Select(x => res(x).AsNumber() ?? 0).ToArray() : [];
        var sub = res(a.Get("Subtype")).AsName() ?? "";
        if (viewer && sub is "Stamp" or "Redact") return null;
        var rect = Nums("Rect");
        if (rect.Length != 4) return null;
        double x0 = Math.Min(rect[0], rect[2]), y0 = Math.Min(rect[1], rect[3]), x1 = Math.Max(rect[0], rect[2]), y1 = Math.Max(rect[1], rect[3]);
        double w = x1 - x0, h = y1 - y0;
        var c = Nums("C"); var ic = Nums("IC");
        var ca = res(a.Get("CA")).AsNumber() ?? 1;
        double bw = 1; var dash = "";
        string Dash(PdfObject? d)
        {
            var v = res(d) is PdfArray da ? da.Items.Take(16).Select(x => Math.Max(res(x).AsNumber() ?? 3, 0)).ToArray() : [];
            return v.Length > 0 && v.Sum() > 0 ? $"[{string.Join(' ', v.Select(F))}] 0 d\n" : "[3] 0 d\n";
        }
        if (res(a.Get("BS")) is PdfDictionary bs)
        {
            bw = res(bs.Get("W")).AsNumber() ?? 1;
            if (res(bs.Get("S")).AsName() == "D") dash = Dash(bs.Get("D"));
        }
        else if (res(a.Get("Border")) is PdfArray border && border.Count >= 3)
        {
            bw = res(border[2]).AsNumber() ?? 1;
            if (border.Count >= 4 && res(border[3]) is PdfArray) dash = Dash(border[3]);
        }
        bw = Math.Max(bw, 0);
        var sb = new StringBuilder();
        var resources = new Dictionary<string, PdfObject>();
        var gs = new Dictionary<string, PdfObject> { ["CA"] = new PdfReal(ca), ["ca"] = new PdfReal(ca) };
        var q = Nums("QuadPoints");
        string Stroke(double[] def) => Color(c.Length == 0 ? def : c, true) + $"{F(bw)} w\n" + dash;

        switch (sub)
        {
            case "Highlight" when q.Length >= 8:
                gs["BM"] = new PdfName("Multiply");
                sb.Append(Color(c.Length == 0 ? [1, 1, 0] : c, false));
                for (var i = 0; i + 8 <= q.Length; i += 8)
                    sb.Append($"{F(q[i])} {F(q[i + 1])} m {F(q[i + 2])} {F(q[i + 3])} l {F(q[i + 6])} {F(q[i + 7])} l {F(q[i + 4])} {F(q[i + 5])} l h f\n");
                break;
            case "Underline" or "StrikeOut" or "Squiggly" when q.Length >= 8:
                sb.Append(Color(c.Length == 0 ? [0] : c, true)).Append($"{F(Math.Max(bw, 1))} w\n");
                for (var i = 0; i + 8 <= q.Length; i += 8)
                {
                    double ax = q[i + 4], ay = q[i + 5], bx = q[i + 6], by = q[i + 7];
                    if (viewer && sub != "StrikeOut") { var lift = Math.Max(bw, 1); ay += lift; by += lift; }
                    if (sub == "StrikeOut") { ax = (q[i] + q[i + 4]) / 2; ay = (q[i + 1] + q[i + 5]) / 2; bx = (q[i + 2] + q[i + 6]) / 2; by = (q[i + 3] + q[i + 7]) / 2; }
                    if (sub != "Squiggly") { sb.Append($"{F(ax)} {F(ay)} m {F(bx)} {F(by)} l S\n"); continue; }
                    var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    if (len < 0.1) continue;
                    double dx = (bx - ax) / len, dy = (by - ay) / len;
                    var steps = Math.Max(1, (int)(len / 2));
                    sb.Append($"{F(ax)} {F(ay)} m\n");
                    for (var s = 1; s <= steps; s++)
                    {
                        var t = Math.Min(s * 2.0, len); var off = s % 2 == 0 ? 0 : viewer ? 1 : 2;
                        sb.Append($"{F(ax + dx * t - dy * off)} {F(ay + dy * t + dx * off)} l\n");
                    }
                    sb.Append("S\n");
                }
                break;
            case "Square" or "Circle":
            {
                var cloud = CloudIntensity(a, res) > 0;
                var rd = Nums("RD");
                double dl = 0, db = 0, dr = 0, dt = 0;
                if (rd.Length >= 4 && rd.All(v => v >= 0)) (dl, db, dr, dt) = (rd[0], rd[1], rd[2], rd[3]);
                var inset = bw / 2 + (cloud ? CloudRadius(Math.Min(w - dl - dr, h - db - dt)) : 0);
                double rx = x0 + dl + inset, ry = y0 + db + inset, rw = w - dl - dr - 2 * inset, rh = h - db - dt - 2 * inset;
                if (rw <= 0 || rh <= 0) return null;
                var fill = ic.Length > 0; var stroke = bw > 0;
                if (fill) sb.Append(Color(ic, false));
                if (stroke) sb.Append(Stroke([0]));
                if (cloud) AppendCloud(sb, sub == "Square" ? [rx, ry, rx + rw, ry, rx + rw, ry + rh, rx, ry + rh] : Ellipse(rx, ry, rw, rh));
                else if (sub == "Square") sb.Append($"{F(rx)} {F(ry)} {F(rw)} {F(rh)} re\n");
                else AppendEllipse(sb, rx, ry, rw, rh);
                sb.Append(fill && stroke ? "B\n" : fill ? "f\n" : stroke ? "S\n" : "n\n");
                break;
            }
            case "Line" when Nums("L") is { Length: >= 4 } l:
            {
                sb.Append(Stroke([0]));
                var le = LineEndings(a, res);
                var (sx, sy, ex, ey) = (l[0], l[1], l[2], l[3]);
                var len = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
                if (len > 0)
                {
                    var arrow = Math.Max(6, bw * 5);
                    double ux = (ex - sx) / len, uy = (ey - sy) / len;
                    var (cx0, cy0, cx1, cy1) = (sx, sy, ex, ey);
                    if (le.Start == "ClosedArrow") { cx0 += ux * arrow * 0.8; cy0 += uy * arrow * 0.8; }
                    if (le.End == "ClosedArrow") { cx1 -= ux * arrow * 0.8; cy1 -= uy * arrow * 0.8; }
                    sb.Append($"{F(cx0)} {F(cy0)} m {F(cx1)} {F(cy1)} l S\n");
                    AppendLineEnd(sb, le.Start, sx, sy, -ux, -uy, arrow, ic, c);
                    AppendLineEnd(sb, le.End, ex, ey, ux, uy, arrow, ic, c);
                }
                break;
            }
            case "Polygon" or "PolyLine" when Nums("Vertices") is { Length: >= 4 } v:
            {
                var fill = sub == "Polygon" && ic.Length > 0;
                if (fill) sb.Append(Color(ic, false));
                sb.Append(Stroke([0]));
                if (sub == "Polygon" && CloudIntensity(a, res) > 0) AppendCloud(sb, v);
                else
                    for (var i = 0; i + 1 < v.Length; i += 2) sb.Append($"{F(v[i])} {F(v[i + 1])} {(i == 0 ? "m" : "l")}\n");
                sb.Append(sub == "Polygon" ? (fill ? "h B\n" : "h S\n") : "S\n");
                if (sub == "PolyLine" && v.Length >= 4)
                {
                    var le = LineEndings(a, res);
                    var n = v.Length;
                    var arrow = Math.Max(6, bw * 5);
                    void End(string style, double px, double py, double qx, double qy)
                    {
                        var len = Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
                        if (len > 0) AppendLineEnd(sb, style, px, py, (px - qx) / len, (py - qy) / len, arrow, ic, c);
                    }
                    End(le.Start, v[0], v[1], v[2], v[3]);
                    End(le.End, v[n - 2], v[n - 1], v[n - 4], v[n - 3]);
                }
                break;
            }
            case "Ink" when res(a.Get("InkList")) is PdfArray ink:
                sb.Append(Color(c.Length == 0 ? [0] : c, true)).Append($"{F(bw)} w 1 J 1 j\n").Append(dash);
                foreach (var stroke in ink.Items)
                {
                    var pts = res(stroke) is PdfArray pa ? pa.Items.Take(65_536).Select(x => res(x).AsNumber() ?? 0).ToArray() : [];
                    if (!viewer) { AppendSmooth(sb, pts); continue; }
                    for (var i = 0; i + 1 < pts.Length; i += 2) sb.Append($"{F(pts[i])} {F(pts[i + 1])} {(i == 0 ? "m" : "l")}\n");
                    if (pts.Length >= 2) sb.Append(pts.Length == 2 ? $"{F(pts[0])} {F(pts[1])} l S\n" : "S\n");
                }
                break;
            case "FreeText" when res(a.Get("Contents")) is PdfString txt:
            {
                var da = res(a.Get("DA")) is PdfString ds ? ds.Text : "0 g";
                var size = 12.0;
                var m = System.Text.RegularExpressions.Regex.Match(da, @"([\d.]+)\s+Tf");
                if (m.Success) double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out size);
                if (size <= 0) size = 12;
                var colorOps = System.Text.RegularExpressions.Regex.Replace(da, @"/\S+\s+[\d.]+\s+Tf", "").Trim();
                var explicitBorder = res(a.Get("BS")) is PdfDictionary || res(a.Get("Border")) is PdfArray;
                var fillColor = ic.Length > 0 ? ic : viewer ? c : [];
                var borderColor = !viewer ? c : ic.Length > 0 && c.Length > 0 ? c : DaColor(da);
                if (fillColor.Length > 0) sb.Append("q\n").Append(Color(fillColor, false)).Append($"{F(x0)} {F(y0)} {F(w)} {F(h)} re f\nQ\n");
                if (borderColor.Length > 0 && bw > 0 && (!viewer || explicitBorder)) sb.Append("q\n").Append(Color(borderColor, true)).Append($"{F(bw)} w\n{dash}{F(x0 + bw / 2)} {F(y0 + bw / 2)} {F(w - bw)} {F(h - bw)} re S\nQ\n");
                var pad = 2 + bw;
                var lines = Wrap(txt.Text, size, Math.Max(w - 2 * pad, size));
                var lead = size * 1.15;
                var quad = (int)(res(a.Get("Q")).AsNumber() ?? 0);
                sb.Append($"q {F(x0)} {F(y0)} {F(w)} {F(h)} re W n BT\n{colorOps}\n/Helv {F(size)} Tf {F(lead)} TL\n");
                var y = y1 - pad - size;
                foreach (var line in lines)
                {
                    var lw = TextWidth(line, size);
                    var lx = x0 + pad + (quad == 1 ? (w - 2 * pad - lw) / 2 : quad == 2 ? w - 2 * pad - lw : 0);
                    sb.Append($"1 0 0 1 {F(lx)} {F(y)} Tm\n{Lit(line)} Tj\n");
                    y -= lead;
                }
                sb.Append("ET Q\n");
                resources["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Helv"] = Font("Helvetica") });
                break;
            }
            case "Text":
                DrawNote(sb, resources, res(a.Get("Name")).AsName() ?? "Note", x0, y0, w, h, c.Length == 0 ? [1, 0.9, 0.2] : c);
                break;
            case "Caret":
                sb.Append(Color(c.Length == 0 ? [0, 0, 1] : c, false));
                sb.Append($"{F(x0)} {F(y0)} m {F(x0 + w * 0.4)} {F(y0)} {F(x0 + w / 2)} {F(y0 + h * 0.3)} {F(x0 + w / 2)} {F(y1)} c ")
                  .Append($"{F(x0 + w / 2)} {F(y0 + h * 0.3)} {F(x1 - w * 0.4)} {F(y0)} {F(x1)} {F(y0)} c h f\n");
                break;
            case "Stamp":
                DrawStamp(sb, resources, a, res, add, previous, image, x0, y0, w, h);
                break;
            case "FileAttachment":
                DrawAttachment(sb, res(a.Get("Name")).AsName() ?? "PushPin", x0, y0, w, h, c.Length == 0 ? [0.3, 0.3, 0.3] : c);
                break;
            case "Redact":
            {
                sb.Append("1 0 0 RG\n1 w\n[3 2] 0 d\n");
                if (q.Length >= 8)
                    for (var i = 0; i + 8 <= q.Length; i += 8)
                        sb.Append($"{F(q[i])} {F(q[i + 1])} m {F(q[i + 2])} {F(q[i + 3])} l {F(q[i + 6])} {F(q[i + 7])} l {F(q[i + 4])} {F(q[i + 5])} l h S\n");
                else sb.Append($"{F(x0 + 0.5)} {F(y0 + 0.5)} {F(w - 1)} {F(h - 1)} re S\n");
                break;
            }
            default: return null;
        }
        if (sb.Length == 0) return null;
        resources["ExtGState"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["G0"] = new PdfDictionary(gs) });
        var body = Encoding.Latin1.GetBytes("/G0 gs\n" + sb);
        return new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Form"),
            ["BBox"] = new PdfArray([new PdfReal(x0), new PdfReal(y0), new PdfReal(x1), new PdfReal(y1)]),
            ["Resources"] = new PdfDictionary(resources),
            ["Length"] = new PdfInteger(body.Length),
        }), body);
    }

    /// <summary>Fill colour operands of a /DA string (g, rg or k operator), or empty when none.</summary>
    static double[] DaColor(string da)
    {
        var m = System.Text.RegularExpressions.Regex.Match(da, @"((?:[-+]?[\d.]+\s+){1,4})(rg|g|k)\b");
        if (!m.Success) return [];
        var v = m.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToArray();
        return m.Groups[2].Value switch { "g" when v.Length >= 1 => [v[^1]], "rg" when v.Length >= 3 => v[^3..], "k" when v.Length >= 4 => v[^4..], _ => [] };
    }

    static double CloudIntensity(PdfDictionary a, Func<PdfObject?, PdfObject?> res) =>
        res(a.Get("BE")) is PdfDictionary be && res(be.Get("S")).AsName() == "C" ? Math.Max(res(be.Get("I")).AsNumber() ?? 1, 0) : 0;

    static double CloudRadius(double shortSide) => Math.Clamp(shortSide / 8, 3, 8);

    static double[] Ellipse(double x, double y, double w, double h)
    {
        var p = new List<double>();
        for (var i = 0; i < 32; i++) { var t = 2 * Math.PI * i / 32; p.Add(x + w / 2 + w / 2 * Math.Cos(t)); p.Add(y + h / 2 + h / 2 * Math.Sin(t)); }
        return [.. p];
    }

    static void AppendEllipse(StringBuilder sb, double rx, double ry, double rw, double rh)
    {
        const double k = 0.5522847498;
        double cx = rx + rw / 2, cy = ry + rh / 2, ox = rw / 2 * k, oy = rh / 2 * k;
        sb.Append($"{F(rx)} {F(cy)} m\n")
          .Append($"{F(rx)} {F(cy + oy)} {F(cx - ox)} {F(ry + rh)} {F(cx)} {F(ry + rh)} c\n")
          .Append($"{F(cx + ox)} {F(ry + rh)} {F(rx + rw)} {F(cy + oy)} {F(rx + rw)} {F(cy)} c\n")
          .Append($"{F(rx + rw)} {F(cy - oy)} {F(cx + ox)} {F(ry)} {F(cx)} {F(ry)} c\n")
          .Append($"{F(cx - ox)} {F(ry)} {F(rx)} {F(cy - oy)} {F(rx)} {F(cy)} c h\n");
    }

    /// <summary>Scalloped outline: each edge becomes semicircular bulges bowing outward.</summary>
    static void AppendCloud(StringBuilder sb, double[] pts)
    {
        var n = pts.Length / 2;
        double area = 0;
        for (var i = 0; i < n; i++) { var j = (i + 1) % n; area += pts[2 * i] * pts[2 * j + 1] - pts[2 * j] * pts[2 * i + 1]; }
        var sign = area >= 0 ? 1.0 : -1.0;
        sb.Append($"{F(pts[0])} {F(pts[1])} m\n");
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            double ax = pts[2 * i], ay = pts[2 * i + 1], bx = pts[2 * j], by = pts[2 * j + 1];
            var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            if (len < 0.01) continue;
            double ux = (bx - ax) / len, uy = (by - ay) / len, nx = uy * sign, ny = -ux * sign;
            var arcs = Math.Max(1, (int)Math.Round(len / 12));
            var step = len / arcs;
            var bulge = step / 2 * 4.0 / 3.0 * 0.9;
            for (var k = 0; k < arcs; k++)
            {
                double sx = ax + ux * step * k, sy = ay + uy * step * k, ex = ax + ux * step * (k + 1), ey = ay + uy * step * (k + 1);
                sb.Append($"{F(sx + nx * bulge)} {F(sy + ny * bulge)} {F(ex + nx * bulge)} {F(ey + ny * bulge)} {F(ex)} {F(ey)} c\n");
            }
        }
    }

    /// <summary>Catmull-Rom through the points, emitted as cubic Béziers; short strokes become dots.</summary>
    static void AppendSmooth(StringBuilder sb, double[] p)
    {
        var n = p.Length / 2;
        if (n == 0) return;
        if (n == 1) { sb.Append($"{F(p[0])} {F(p[1])} m {F(p[0])} {F(p[1])} l S\n"); return; }
        sb.Append($"{F(p[0])} {F(p[1])} m\n");
        for (var i = 0; i < n - 1; i++)
        {
            int i0 = Math.Max(i - 1, 0), i3 = Math.Min(i + 2, n - 1);
            double c1x = p[2 * i] + (p[2 * (i + 1)] - p[2 * i0]) / 6, c1y = p[2 * i + 1] + (p[2 * (i + 1) + 1] - p[2 * i0 + 1]) / 6;
            double c2x = p[2 * (i + 1)] - (p[2 * i3] - p[2 * i]) / 6, c2y = p[2 * (i + 1) + 1] - (p[2 * i3 + 1] - p[2 * i + 1]) / 6;
            sb.Append($"{F(c1x)} {F(c1y)} {F(c2x)} {F(c2y)} {F(p[2 * (i + 1)])} {F(p[2 * (i + 1) + 1])} c\n");
        }
        sb.Append("S\n");
    }

    static (string Start, string End) LineEndings(PdfDictionary a, Func<PdfObject?, PdfObject?> res) =>
        res(a.Get("LE")) is PdfArray { Count: >= 2 } le ? (res(le[0]).AsName() ?? "None", res(le[1]).AsName() ?? "None") : ("None", "None");

    static void AppendLineEnd(StringBuilder sb, string style, double x, double y, double ux, double uy, double size, double[] ic, double[] c)
    {
        double nx = -uy, ny = ux;
        switch (style)
        {
            case "OpenArrow" or "ClosedArrow":
            {
                double bx = x - ux * size, by = y - uy * size, hw = size * 0.4;
                sb.Append($"{F(bx + nx * hw)} {F(by + ny * hw)} m {F(x)} {F(y)} l {F(bx - nx * hw)} {F(by - ny * hw)} l ");
                if (style == "ClosedArrow") { sb.Append("h "); sb.Append(Color(ic.Length > 0 ? ic : c.Length > 0 ? c : [0], false).Replace("\n", " ")).Append("B\n"); }
                else sb.Append("S\n");
                break;
            }
            case "Square" or "Circle" or "Diamond":
            {
                var r = size * 0.4;
                if (style == "Circle") AppendEllipse(sb, x - r, y - r, 2 * r, 2 * r);
                else if (style == "Square") sb.Append($"{F(x - r)} {F(y - r)} {F(2 * r)} {F(2 * r)} re\n");
                else sb.Append($"{F(x - r)} {F(y)} m {F(x)} {F(y + r)} l {F(x + r)} {F(y)} l {F(x)} {F(y - r)} l h\n");
                sb.Append("S\n");
                break;
            }
            case "Butt":
                sb.Append($"{F(x + nx * size * 0.4)} {F(y + ny * size * 0.4)} m {F(x - nx * size * 0.4)} {F(y - ny * size * 0.4)} l S\n");
                break;
        }
    }

    static void DrawNote(StringBuilder sb, Dictionary<string, PdfObject> resources, string icon, double x, double y, double w, double h, double[] c)
    {
        sb.Append(Color(c, false)).Append("0 G\n0.7 w\n");
        double sx = w / 20, sy = h / 20;
        string X(double v) => F(x + v * sx);
        string Y(double v) => F(y + v * sy);
        switch (icon)
        {
            case "Comment":
                sb.Append($"{X(1)} {Y(6)} m {X(1)} {Y(19)} l {X(19)} {Y(19)} l {X(19)} {Y(6)} l {X(8)} {Y(6)} l {X(4)} {Y(1)} l {X(5)} {Y(6)} l h B\n");
                sb.Append($"0 g {X(4)} {Y(15)} {F(12 * sx)} {F(1.2 * sy)} re f {X(4)} {Y(11)} {F(12 * sx)} {F(1.2 * sy)} re f\n");
                break;
            case "Insert":
                sb.Append($"{X(10)} {Y(19)} m {X(1)} {Y(2)} l {X(19)} {Y(2)} l h B\n");
                break;
            case "Key":
                AppendEllipse(sb, x + 2 * sx, y + 11 * sy, 7 * sx, 7 * sy);
                sb.Append("B\n").Append($"{X(8)} {Y(11)} m {X(18)} {Y(2)} l S\n");
                break;
            case "Help" or "Paragraph" or "NewParagraph":
                AppendEllipse(sb, x + sx, y + sy, 18 * sx, 18 * sy);
                sb.Append("B\n0 g\n");
                var glyph = icon == "Help" ? "?" : "\\266";
                sb.Append($"BT /Helv {F(14 * sy)} Tf {X(6.2)} {Y(5.5)} Td ({glyph}) Tj ET\n");
                resources["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Helv"] = Font("Helvetica-Bold") });
                break;
            default:
                sb.Append($"{X(2)} {Y(1)} m {X(2)} {Y(19)} l {X(14)} {Y(19)} l {X(18)} {Y(15)} l {X(18)} {Y(1)} l h B\n");
                sb.Append($"{X(14)} {Y(19)} m {X(14)} {Y(15)} l {X(18)} {Y(15)} l S\n");
                for (var i = 0; i < 3; i++) sb.Append($"{X(5)} {Y(12 - i * 3.5)} m {X(15)} {Y(12 - i * 3.5)} l S\n");
                break;
        }
    }

    static void DrawAttachment(StringBuilder sb, string icon, double x, double y, double w, double h, double[] c)
    {
        sb.Append(Color(c, true)).Append("1.2 w 1 J 1 j\n");
        double sx = w / 20, sy = h / 20;
        string P(double px, double py) => $"{F(x + px * sx)} {F(y + py * sy)}";
        if (icon == "Paperclip")
            sb.Append($"{P(6, 3)} m {P(6, 14)} l {P(9, 18)} l {P(13, 14)} l {P(13, 5)} l {P(10, 5)} l {P(10, 12)} l S\n");
        else
        {
            sb.Append(Color(c, false)).Append($"{P(10, 1)} m {P(10, 8)} l S\n");
            sb.Append($"{P(5, 8)} m {P(15, 8)} l {P(13, 18)} l {P(7, 18)} l h f\n");
        }
    }

    static string StampLabel(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name) { if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' '); sb.Append(char.ToUpperInvariant(ch)); }
        return sb.ToString();
    }

    static void DrawStamp(StringBuilder sb, Dictionary<string, PdfObject> resources, PdfDictionary a, Func<PdfObject?, PdfObject?> res, Func<PdfObject, PdfRef> add,
        PdfStream? previous, PdfAnnotationImage? image, double x, double y, double w, double h)
    {
        PdfObject? imageObj = null;
        if (image is not null)
        {
            var rgb = Deflate(image.Rgb);
            var dict = new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Image"), ["Width"] = new PdfInteger(image.Width), ["Height"] = new PdfInteger(image.Height),
                ["ColorSpace"] = new PdfName("DeviceRGB"), ["BitsPerComponent"] = new PdfInteger(8), ["Filter"] = new PdfName("FlateDecode"), ["Length"] = new PdfInteger(rgb.Length),
            };
            if (image.Alpha is { Length: > 0 } alpha && alpha.Any(v => v != 255))
            {
                var ad = Deflate(alpha);
                dict["SMask"] = add(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
                {
                    ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Image"), ["Width"] = new PdfInteger(image.Width), ["Height"] = new PdfInteger(image.Height),
                    ["ColorSpace"] = new PdfName("DeviceGray"), ["BitsPerComponent"] = new PdfInteger(8), ["Filter"] = new PdfName("FlateDecode"), ["Length"] = new PdfInteger(ad.Length),
                }), ad));
            }
            imageObj = add(new PdfStream(new PdfDictionary(dict), rgb));
        }
        else if (previous is not null && res(previous.Dict.Get("Resources")) is PdfDictionary pr && res(pr.Get("XObject")) is PdfDictionary px && px.Get("Im0") is { } old)
            imageObj = old;
        if (imageObj is not null)
        {
            resources["XObject"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Im0"] = imageObj });
            sb.Append($"q {F(w)} 0 0 {F(h)} {F(x)} {F(y)} cm /Im0 Do Q\n");
            return;
        }
        var name = res(a.Get("Name")).AsName() ?? "Draft";
        var label = StampLabel(name);
        double[] col = name is "Approved" or "Final" or "ForPublicRelease" ? [0, 0.5, 0] : name is "NotApproved" or "Expired" or "Confidential" or "TopSecret" ? [0.75, 0, 0] : [0, 0, 0.7];
        var size = 100.0;
        size = Math.Min(h * 0.55, (w - 12) / Math.Max(TextWidth(label, 1) * 1.06, 0.01));
        if (size < 1) size = 1;
        var tw = TextWidth(label, size) * 1.06;
        sb.Append(Color(col, true)).Append(Color(col, false)).Append("2 w\n");
        sb.Append($"{F(x + 1.5)} {F(y + 1.5)} {F(w - 3)} {F(h - 3)} re S\n");
        sb.Append($"BT /Helv {F(size)} Tf {F(x + (w - tw) / 2)} {F(y + (h - size * 0.72) / 2)} Td {Lit(label)} Tj ET\n");
        resources["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Helv"] = Font("Helvetica-Bold") });
    }

    /// <summary>zlib (RFC 1950) compression for FlateDecode.</summary>
    public static byte[] Deflate(byte[] data) => PdfFilters.DeflateEncode(data);
}
