using System.IO.Compression;
using System.Security.Cryptography;
using PortableDocument.Core;

namespace FreeDF.Samples.Kit;

/// <summary>Resource dictionary under construction: named entries per category, de-duplicated by value.</summary>
public class Res
{
    static readonly (string Cat, string Prefix)[] Prefix =
        [("Font", "F"), ("XObject", "X"), ("ExtGState", "G"), ("Shading", "S"), ("Pattern", "P"), ("ColorSpace", "C"), ("Properties", "M")];

    readonly Dictionary<string, List<(string Key, string Val)>> _rd = Prefix.ToDictionary(p => p.Cat, _ => new List<(string, string)>());

    public string Reg(string cat, string val)
    {
        var m = _rd[cat];
        foreach (var (k, v) in m) if (v == val) return k;
        var key = Prefix.First(p => p.Cat == cat).Prefix + (m.Count + 1);
        m.Add((key, val));
        return key;
    }

    public string ResStr()
    {
        var parts = new List<string>();
        foreach (var (cat, _) in Prefix)
            if (_rd[cat] is { Count: > 0 } m) parts.Add($"/{cat} << " + string.Join(" ", m.Select(e => $"/{e.Key} {e.Val}")) + " >>");
        return "<< /ProcSet [/PDF /Text /ImageB /ImageC /ImageI] " + string.Join(" ", parts) + " >>";
    }
}

/// <summary>A content stream: graphics, path and text operators appended as lines.</summary>
public class Content(Doc doc) : Res
{
    public Doc Doc { get; } = doc;
    readonly List<string> _out = [];
    static string N(double v) => K.N(v);

    public void Raw(string s) => _out.Add(s);
    public void Save() => _out.Add("q");
    public void Restore() => _out.Add("Q");
    public void Cm(double a, double b, double c, double d, double e, double f) => _out.Add($"{N(a)} {N(b)} {N(c)} {N(d)} {N(e)} {N(f)} cm");
    public void Translate(double x, double y) => Cm(1, 0, 0, 1, x, y);
    public void Scale(double sx, double sy) => Cm(sx, 0, 0, sy, 0, 0);

    public void Rotate(double deg)
    {
        var r = deg * Math.PI / 180;
        Cm(Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0);
    }

    public void Fill(Color c) => _out.Add(c.Ops + " rg");
    public void Stroke(Color c) => _out.Add(c.Ops + " RG");
    public void Width(double w) => _out.Add($"{N(w)} w");
    public void Cap(int c) => _out.Add($"{c} J");
    public void Join(int j) => _out.Add($"{j} j");
    public void Miter(double m) => _out.Add($"{N(m)} M");
    public void Dash(double[] arr, double phase = 0) => _out.Add("[" + string.Join(" ", arr.Select(N)) + $"] {N(phase)} d");

    /// <summary>ExtGState entry: values may be bool, number, or a raw PDF string.</summary>
    public void Gs(params (string Key, object Val)[] kv)
    {
        var items = kv.Select(e => $"/{e.Key} " + e.Val switch
        {
            bool b => b ? "true" : "false",
            int i => N(i),
            double d => N(d),
            _ => e.Val.ToString(),
        });
        var name = Reg("ExtGState", "<< /Type /ExtGState " + string.Join(" ", items) + " >>");
        _out.Add($"/{name} gs");
    }

    public void Move(double x, double y) => _out.Add($"{N(x)} {N(y)} m");
    public void Line(double x, double y) => _out.Add($"{N(x)} {N(y)} l");
    public void Curve(double x1, double y1, double x2, double y2, double x3, double y3) => _out.Add($"{N(x1)} {N(y1)} {N(x2)} {N(y2)} {N(x3)} {N(y3)} c");
    public void Close() => _out.Add("h");
    public void Rect(double x, double y, double w, double h) => _out.Add($"{N(x)} {N(y)} {N(w)} {N(h)} re");

    public void RRect(double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        var k = 0.5523 * r;
        Move(x + r, y);
        Line(x + w - r, y);
        Curve(x + w - r + k, y, x + w, y + r - k, x + w, y + r);
        Line(x + w, y + h - r);
        Curve(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h);
        Line(x + r, y + h);
        Curve(x + r - k, y + h, x, y + h - r + k, x, y + h - r);
        Line(x, y + r);
        Curve(x, y + r - k, x + r - k, y, x + r, y);
        Close();
    }

    public void Ellipse(double cx, double cy, double rx, double ry)
    {
        const double k = 0.5523;
        Move(cx + rx, cy);
        Curve(cx + rx, cy + k * ry, cx + k * rx, cy + ry, cx, cy + ry);
        Curve(cx - k * rx, cy + ry, cx - rx, cy + k * ry, cx - rx, cy);
        Curve(cx - rx, cy - k * ry, cx - k * rx, cy - ry, cx, cy - ry);
        Curve(cx + k * rx, cy - ry, cx + rx, cy - k * ry, cx + rx, cy);
        Close();
    }

    public void Circle(double cx, double cy, double r) => Ellipse(cx, cy, r, r);

    /// <summary>Circular arc from a0 to a1 degrees (counter-clockwise) approximated with Béziers.</summary>
    public void ArcPath(double cx, double cy, double r, double a0, double a1, bool move = true)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(a1 - a0) / 90));
        var da = (a1 - a0) * Math.PI / 180 / steps;
        var a = a0 * Math.PI / 180;
        if (move) Move(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        var k = 4.0 / 3 * Math.Tan(da / 4);
        for (var i = 0; i < steps; i++)
        {
            var b = a + da;
            Curve(cx + r * (Math.Cos(a) - k * Math.Sin(a)), cy + r * (Math.Sin(a) + k * Math.Cos(a)),
                  cx + r * (Math.Cos(b) + k * Math.Sin(b)), cy + r * (Math.Sin(b) - k * Math.Cos(b)),
                  cx + r * Math.Cos(b), cy + r * Math.Sin(b));
            a = b;
        }
    }

    public void FillPath() => _out.Add("f");
    public void StrokePath() => _out.Add("S");
    public void FillStroke() => _out.Add("B");
    public void FillEO() => _out.Add("f*");
    public void Clip(bool eo = false) => _out.Add(eo ? "W* n" : "W n");

    public void Sh(string shadingDict) => _out.Add($"/{Reg("Shading", shadingDict)} sh");

    /// <summary>Selects a pattern as fill or stroke colour; <paramref name="pat"/> is an object number or a raw reference string.</summary>
    public void PatternFill(object pat, string cs = "/Pattern", bool stroke = false, string comps = "")
    {
        var name = Reg("Pattern", pat is int n ? $"{n} 0 R" : pat.ToString()!);
        var (setCs, setC) = stroke ? ("CS", "SCN") : ("cs", "scn");
        if (cs != "/Pattern") _out.Add($"/{Reg("ColorSpace", cs)} {setCs} {comps} /{name} {setC}");
        else _out.Add($"/Pattern {setCs} /{name} {setC}");
    }

    public void XObj(int xref) => _out.Add($"/{Reg("XObject", $"{xref} 0 R")} Do");

    public void Text(IFont font, double size, double x, double y, string s, Color? color = null, bool kern = true, double tracking = 0,
        int? mode = null, double spaceExtra = 0, Color? strokeColor = null, double? lw = null, double rise = 0)
    {
        var fname = Reg("Font", $"{font.Ref} 0 R");
        var ops = new List<string> { "BT" };
        if (color is { } c) ops.Add(c.Ops + " rg");
        if (strokeColor is { } sc) ops.Add(sc.Ops + " RG");
        if (lw is { } w) ops.Add($"{N(w)} w");
        if (mode is { } m) ops.Add($"{m} Tr");
        if (tracking != 0) ops.Add($"{N(tracking)} Tc");
        if (rise != 0) ops.Add($"{N(rise)} Ts");
        ops.Add($"/{fname} {N(size)} Tf 1 0 0 1 {N(x)} {N(y)} Tm");
        ops.Add(font.Tj(s, size, kern, spaceExtra));
        if (tracking != 0) ops.Add("0 Tc");
        if (mode != null) ops.Add("0 Tr");
        if (rise != 0) ops.Add("0 Ts");
        ops.Add("ET");
        _out.Add(string.Join(" ", ops));
    }

    public double TextW(IFont font, double size, string s, bool kern = true, double tracking = 0) => font.Width(s, size, kern, tracking);

    public byte[] Data() => K.Latin1(string.Join("\n", _out));
}

/// <summary>Form XObject, tiling pattern, soft-mask group or appearance stream.</summary>
public class XForm(Doc doc, double[] bbox, double[]? matrix = null, string? group = null) : Content(doc)
{
    public int Ref { get; private set; }

    public int Build(string kind = "form", double xstep = 0, double ystep = 0, int paint = 1, string extra = "")
    {
        var b = K.Join(bbox);
        string d;
        if (kind == "pattern")
            d = $"/Type /Pattern /PatternType 1 /PaintType {paint} /TilingType 1 /BBox [{b}] /XStep {K.N(xstep)} /YStep {K.N(ystep)} /Resources {ResStr()}";
        else
        {
            d = $"/Type /XObject /Subtype /Form /BBox [{b}] /Resources {ResStr()}";
            if (group != null) d += $" /Group {group}";
        }
        if (matrix != null) d += " /Matrix [" + K.Join(matrix) + "]";
        return Ref = Doc.Stream((d + " " + extra).Trim(), Data());
    }
}

public class Page : Content
{
    public double W { get; }
    public double H { get; }
    public int Ref { get; }
    public List<int> Annots { get; } = [];
    public string Extra { get; set; } = "";
    public Dictionary<string, double[]> Boxes { get; } = [];

    public Page(Doc doc, double w, double h) : base(doc)
    {
        W = w; H = h;
        Ref = doc.New();
    }

    public int AddAnnot(string body)
    {
        var r = Doc.Add(body);
        Annots.Add(r);
        return r;
    }
}

/// <summary>
/// A raw-object document builder: bodies are written in PDF syntax, then parsed by this repo's <see cref="PdfLexer"/> and
/// serialized (optionally AES-256 encrypted) by <see cref="PdfWriter"/>. Nothing is hand-serialized.
/// </summary>
public class Doc
{
    sealed record Obj(string Body, byte[]? Data);

    readonly List<Obj?> _objs = [null];
    readonly List<Page> _pages = [];
    readonly Dictionary<(string, string), StdFont> _std = [];
    readonly Dictionary<string, EmbFont> _emb = [];

    internal List<Action> Fin { get; } = [];
    internal int NextFontTag;

    public List<string> Cat { get; } = [];
    public string Title { get; }
    public string Author { get; }
    public string Subject { get; }
    public string Keywords { get; }
    public string? Lang { get; }

    public Doc(string title = "", string author = "Pdf sample generator", string subject = "", string keywords = "", string? lang = null)
    {
        Title = title; Author = author; Subject = subject; Keywords = keywords; Lang = lang;
    }

    public int New() { _objs.Add(null); return _objs.Count - 1; }

    public void Put(int num, string body) => _objs[num] = new Obj(body, null);

    public int Add(string body) { var n = New(); Put(n, body); return n; }

    /// <summary>Body (dictionary text for streams) of an object; assignable through <see cref="Put"/>.</summary>
    public string Body(int num) => _objs[num]?.Body ?? "null";

    public int Stream(string dict, byte[] data, bool compress = true, int? num = null)
    {
        if (compress)
        {
            using var o = new MemoryStream();
            using (var z = new ZLibStream(o, CompressionLevel.SmallestSize, true)) z.Write(data);
            data = o.ToArray();
            dict = (dict + " /Filter /FlateDecode").Trim();
        }
        var n = num ?? New();
        _objs[n] = new Obj($"<< {dict} >>", data);
        return n;
    }

    public StdFont Std(string name, string encoding = "WinAnsiEncoding")
    {
        if (!_std.TryGetValue((name, encoding), out var f)) _std[(name, encoding)] = f = new StdFont(this, name, encoding);
        return f;
    }

    public EmbFont Font(string filename)
    {
        if (!_emb.TryGetValue(filename, out var f)) _emb[filename] = f = new EmbFont(this, filename);
        return f;
    }

    public Page Page(double w = 595.276, double h = 841.89)
    {
        var p = new Page(this, w, h);
        _pages.Add(p);
        return p;
    }

    /// <summary>Builds the file. Encryption keys/IVs are derived from a fixed seed so the output is reproducible.</summary>
    public byte[] Build(string version = "1.7", string pageTreeExtra = "", PdfEncryption? encryption = null)
    {
        foreach (var f in Fin) f();
        var pagesRef = New();
        var kids = new List<string>();
        foreach (var p in _pages)
        {
            var cs = Stream("", p.Data());
            var boxes = string.Concat(p.Boxes.Select(b => $" /{b.Key} [" + K.Join(b.Value) + "]"));
            var annots = p.Annots.Count > 0 ? $" /Annots [{string.Join(" ", p.Annots.Select(a => $"{a} 0 R"))}]" : "";
            Put(p.Ref, $"<< /Type /Page /Parent {pagesRef} 0 R /MediaBox [0 0 {K.N(p.W)} {K.N(p.H)}]{boxes} /Resources {p.ResStr()} /Contents {cs} 0 R{annots} {p.Extra} >>");
            kids.Add($"{p.Ref} 0 R");
        }
        Put(pagesRef, $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {kids.Count} {pageTreeExtra} >>");
        var info = Add($"<< /Title {K.Utf16(Title)} /Author {K.Utf16(Author)} /Subject {K.Utf16(Subject)} /Keywords {K.Utf16(Keywords)} /Creator (FreeDF samples generator) /Producer (FreeDF) >>");
        var lang = Lang != null ? $" /Lang {K.PStr(Lang)}" : "";
        var root = Add($"<< /Type /Catalog /Pages {pagesRef} 0 R{lang} {string.Join(" ", Cat)} >>");

        var objs = new Dictionary<int, PdfObject>();
        using var md5 = MD5.Create();
        for (var i = 1; i < _objs.Count; i++)
        {
            var o = _objs[i];
            if (o is null) { objs[i] = PdfNull.Instance; continue; }
            var bodyBytes = K.Latin1(o.Body);
            md5.TransformBlock(bodyBytes, 0, bodyBytes.Length, null, 0);
            var parsed = new PdfLexer(bodyBytes).ReadObject(true) ?? throw new InvalidDataException($"object {i} is empty");
            if (o.Data is { } data)
            {
                md5.TransformBlock(data, 0, data.Length, null, 0);
                objs[i] = new PdfStream((PdfDictionary)parsed, data);
            }
            else objs[i] = parsed;
        }
        md5.TransformFinalBlock([], 0, 0);
        return PdfWriter.Write(objs, root, info, version, null, md5.Hash, new PdfWriteOptions { Encryption = encryption });
    }

    public void Save(string path, string version = "1.7", string pageTreeExtra = "", PdfEncryption? encryption = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Build(version, pageTreeExtra, encryption));
    }
}
