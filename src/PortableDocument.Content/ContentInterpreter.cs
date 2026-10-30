using PortableDocument.Core;
using PortableDocument.Document;

namespace PortableDocument.Content;

sealed class GState
{
    public Matrix Ctm = Matrix.Identity;
    public PdfColorSpace FillCs = PdfColorSpace.Gray, StrokeCs = PdfColorSpace.Gray;
    public double[] FillColor = [0], StrokeColor = [0];
    public RenderingIntent Intent = RenderingIntent.RelativeColorimetric;
    public BlackPointCompensation BlackPointCompensation = BlackPointCompensation.Default;
    public PdfObject? FillPattern, StrokePattern;
    public double LineWidth = 1, MiterLimit = 10, DashPhase;
    public LineCap Cap; public LineJoin Join;
    public double[] Dash = [];
    public double FillAlpha = 1, StrokeAlpha = 1;
    public string BlendMode = "Normal";
    public double CharSpace, WordSpace, HScale = 1, Leading, Rise;
    public PdfFont? Font; public double FontSize; public int FontRef;
    public int RenderMode;
    public bool SoftMaskActive;
    public bool FillOverprint, StrokeOverprint;
    public int OverprintMode;
    public TransferFunctions? Transfer;
    public bool AlphaIsShape, TextKnockout = true;

    public GState Clone() => (GState)MemberwiseClone();
}

public sealed class ContentInterpreter
{
    public enum OptionalContentEvent
    {
        View,
        Print,
        Export,
    }

    readonly PdfFile _f;
    IRenderBackend _be;
    readonly IFontFallback _fallback;
    readonly Dictionary<PdfDictionary, PdfFont> _fonts = new(ReferenceEqualityComparer.Instance);
    readonly Stack<GState> _stack = new();
    readonly HashSet<PdfDictionary> _activeForms = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<PdfDictionary, bool> _optionalContentOff = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<PdfObject, TransferFunctions?> _transferCache = new(ReferenceEqualityComparer.Instance);
    GState _gs = new();
    PathData _path = new();
    int _pendingClip; // 0 none, 1 nonzero, 2 even-odd
    Matrix _tm = Matrix.Identity, _tlm = Matrix.Identity;
    PathData? _textClip;
    List<string> _hiddenStack = [];
    List<int> _mcidStack = [];
    int _hiddenDepth;
    int _pageMcid = -1;
    int _type3Depth;
    public List<string> Warnings { get; } = [];
    public PdfDictionary? OptionalContentConfiguration { get; set; }
    public PdfDictionary? DefaultOptionalContentConfiguration { get; set; }
    public OptionalContentState? OptionalContentState { get; set; }
    public OptionalContentEvent OptionalContentDisplayEvent { get; set; } = OptionalContentEvent.View;
    public double OptionalContentZoom { get; set; } = 1;
    public OptionalContentContext? OptionalContentContext { get; set; }
    /// <summary>
    /// Simulates the ExtGState overprint parameters (/OP, /op, /OPM; ISO 32000-2 8.6.7, 11.7.4.3) on the RGB output; off by default. The standard leaves
    /// overprint device-dependent and nonzero overprint mode does not apply on a non-CMYK device, so ordinary screen output ignores it.
    /// </summary>
    public bool OverprintSimulation { get; set; }

    public ContentInterpreter(PdfFile f, IRenderBackend backend, IFontFallback? fallback = null)
    {
        _f = f; _be = backend; _fallback = fallback ?? new SystemFontFallback();
    }

    public bool DrawAnnotations { get; set; } = true;
    /// <summary>Allow annotation appearances without /AP /N to use generated fallback appearances.</summary>
    public bool SynthesizeAnnotationAppearances { get; set; } = true;
    /// <summary>Associate captured text spans with the selected annotation appearance stream.</summary>
    public bool CaptureAnnotationAppearanceText { get; set; }

    public void Render(PdfPage page, Matrix baseMatrix) => Render(page, baseMatrix, asPageElement: false);

    /// <summary>Renders a page. When placed as an element of another page, /I controls isolation and a non-isolated page inherits its backdrop's colour space (11.4.7, 11.6.6).</summary>
    public void Render(PdfPage page, Matrix baseMatrix, bool asPageElement)
    {
        _optionalContentOff.Clear();
        if (OptionalContentState is { } state)
        {
            if (!ReferenceEquals(state.File, _f))
                throw new ArgumentException("Optional-content state belongs to another document.");
            foreach (var warning in state.Warnings) Warn(warning);
        }
        SetBase(baseMatrix);
        _gs = new GState { Ctm = baseMatrix };
        var group = R(page.Dict.Get("Group")) is PdfDictionary pg && R(pg.Get("S")).AsName() == "Transparency" ? pg : null;
        // Output pages are always isolated, including pages without /Group (11.4.7).
        var isolated = !asPageElement || R(group?.Get("I")) is PdfBoolean { Value: true };
        var knockout = R(group?.Get("K")) is PdfBoolean { Value: true };
        var colorSpace = DrawGraphics && isolated ? PageGroupColorSpace(group?.Get("CS"), page.Resources) : null;
        _be.Save();
        try
        {
            if (asPageElement)
            {
                var box = page.CropBox;
                var clip = new PathData(); clip.Rect(box.Left, box.Bottom, box.Width, box.Height);
                _be.Clip(clip, baseMatrix, false);
            }
            if (DrawGraphics) _be.BeginGroup(isolated, knockout, 1, "Normal", colorSpace);
            try
            {
                var stackDepth = _stack.Count;
                _be.Save();
                try { Run(page.GetContents(), page.Resources); }
                finally
                {
                    while (_stack.Count > stackDepth) { _gs = _stack.Pop(); _be.Restore(); }
                    _be.Restore();
                }
                if (DrawAnnotations) RenderAnnotations(page, baseMatrix);
            }
            finally { if (DrawGraphics) _be.EndGroup(); }
        }
        finally { _be.Restore(); }
        if (OptionalContentState is { } evaluatedState)
            foreach (var warning in evaluatedState.Warnings) Warn(warning);
    }

    PdfColorSpace? PageGroupColorSpace(PdfObject? obj, PdfDictionary resources, int depth = 0, bool useDefaults = true)
    {
        obj = R(obj);
        if (obj is null or PdfNull) return null;
        if (depth > 8) throw new PdfParseException("Cyclic page transparency group colour space.");
        var spaces = R(resources.Get("ColorSpace")) as PdfDictionary;
        if (obj is PdfName name)
        {
            var device = name.Value switch
            {
                "DeviceGray" => PdfColorSpace.Gray,
                "DeviceRGB" => PdfColorSpace.Rgb,
                "DeviceCMYK" => PdfColorSpace.Cmyk,
                _ => null,
            };
            if (device is not null)
            {
                var key = name.Value switch { "DeviceGray" => "DefaultGray", "DeviceRGB" => "DefaultRGB", _ => "DefaultCMYK" };
                if (useDefaults && R(spaces?.Get(key)) is { } replacement and not PdfNull)
                {
                    var mapped = PageGroupColorSpace(replacement, resources, depth + 1, useDefaults: false);
                    if (mapped is null || mapped.Components != device.Components)
                        throw new PdfParseException($"Page transparency group /{key} has an invalid component count.");
                    return mapped;
                }
                return device != PdfColorSpace.Cmyk ? device : CmykGroupSpace;
            }
            else if (spaces?.Get(name.Value) is { } named)
            {
                var resolved = PageGroupColorSpace(named, resources, depth + 1, useDefaults);
                return resolved ?? throw new PdfParseException($"Invalid page transparency group colour space /{name.Value}.");
            }
        }
        // The general colour parser deliberately falls back for unsupported spaces/profiles.
        // A blending space needs an inverse transform, so that fallback is not valid here.
        var kind = obj is PdfArray { Count: > 0 } a ? R(a[0]).AsName() : obj.AsName();
        if (obj is PdfArray { Count: >= 2 } arr)
        {
            switch (kind)
            {
                case "CalGray" when R(arr[1]) is PdfDictionary gd && GroupNumbers(gd.Get("WhitePoint"), 3) is { } gw:
                    return new GroupBlendColorSpace(GroupBlendSpace.CalGray(R(gd.Get("Gamma")).AsNumber() ?? 1));
                case "CalRGB" when R(arr[1]) is PdfDictionary cd && GroupNumbers(cd.Get("WhitePoint"), 3) is { } cw:
                    {
                        var calibrated = GroupBlendSpace.CalRgb(cw, GroupNumbers(cd.Get("Gamma"), 3) ?? [1, 1, 1],
                            GroupNumbers(cd.Get("Matrix"), 9) ?? [1, 0, 0, 0, 1, 0, 0, 0, 1]);
                        if (calibrated is not null) return new GroupBlendColorSpace(calibrated);
                        break;
                    }
                case "ICCBased" when R(arr[1]) is PdfStream icc:
                    {
                        // Only the component count selects the model; profile transfer curves/primaries are not inverted.
                        var n = (int)(R(icc.Dict.Get("N")).AsNumber() ?? 0);
                        byte[]? data = null;
                        try { data = _f.DecodeStream(icc); } catch (Exception) { }
                        if (data is not null && IccProfile.TryParse(data) is { } profile && profile.Components == n && n is 1 or 3 or 4)
                            return n == 1 ? PdfColorSpace.Gray : n == 3 ? PdfColorSpace.Rgb : CmykGroupSpace;
                        break;
                    }
            }
        }
        throw new NotSupportedException($"Page transparency group colour space /{kind ?? "invalid"} is not supported; only DeviceGray, DeviceRGB, DeviceCMYK, CalGray, CalRGB and ICCBased blending are implemented.");
    }

    static readonly PdfColorSpace CmykGroupSpace = new GroupBlendColorSpace(GroupBlendSpace.Cmyk);

    double[]? GroupNumbers(PdfObject? o, int n)
    {
        if (R(o) is not PdfArray a || a.Count < n) return null;
        var v = new double[n];
        for (var i = 0; i < n; i++) { if (R(a[i]).AsNumber() is not { } x) return null; v[i] = x; }
        return v;
    }

    // ISO 32000-2 12.5.5: map the transformed appearance BBox onto the annotation Rect.
    void RenderAnnotations(PdfPage page, Matrix baseMatrix)
    {
        if (R(page.Dict.Get("Annots")) is not PdfArray annots) return;
        foreach (var ao in annots.Items)
        {
            if (R(ao) is not PdfDictionary a) continue;
            var flags = (int)(R(a.Get("F")).AsNumber() ?? 0);
            if ((flags & (2 | 32)) != 0 || R(a.Get("Subtype")).AsName() == "Popup") continue;
            if (R(a.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var ap = R(a.Get("AP")) as PdfDictionary;
            var appearance = ap?.Get("N");
            var n = R(appearance);
            if (n is PdfDictionary states)
            {
                appearance = R(a.Get("AS")).AsName() is { } s ? states.Get(s) : null;
                n = R(appearance);
            }
            if (n is not PdfStream xs && (!SynthesizeAnnotationAppearances || (xs = SynthAppearance(a, ra)!) is null)) continue;
            var r = ra.Items.Select(x => R(x).AsNumber() ?? 0).ToArray();
            double rx0 = Math.Min(r[0], r[2]), ry0 = Math.Min(r[1], r[3]), rw = Math.Abs(r[2] - r[0]), rh = Math.Abs(r[3] - r[1]);
            var bb = R(xs.Dict.Get("BBox")) is PdfArray ba && ba.Count == 4 ? ba.Items.Select(x => R(x).AsNumber() ?? 0).ToArray() : [0, 0, rw, rh];
            var fm = R(xs.Dict.Get("Matrix")) is PdfArray ma && ma.Count == 6 ? ToMatrix(ma) : new Matrix(1, 0, 0, 1, 0, 0);
            var pts = new[] { fm.Apply(bb[0], bb[1]), fm.Apply(bb[2], bb[1]), fm.Apply(bb[0], bb[3]), fm.Apply(bb[2], bb[3]) };
            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            if (x1 - x0 <= 0 || y1 - y0 <= 0) continue;
            double sx = rw / (x1 - x0), sy = rh / (y1 - y0);
            var amap = new Matrix(sx, 0, 0, sy, rx0 - x0 * sx, ry0 - y0 * sy);
            _gs = new GState { Ctm = amap.Then(baseMatrix) };
            _path = new PathData();
            DoForm(xs, page.Resources, appearance is PdfRef { Number: > 0 } appearanceRef ? appearanceRef.Number : 0,
                annotationAppearance: CaptureAnnotationAppearanceText);
        }
    }

    void Warn(string s) { if (!Warnings.Contains(s)) Warnings.Add(s); }

    double[] Nums(PdfObject? o) => R(o) is PdfArray a ? a.Items.Select(x => R(x).AsNumber() ?? 0).ToArray() : [];

    static string ColorOps(double[] c, bool stroke) => c.Length switch
    {
        1 => $"{c[0]:0.###} {(stroke ? "G" : "g")} ",
        3 => $"{c[0]:0.###} {c[1]:0.###} {c[2]:0.###} {(stroke ? "RG" : "rg")} ",
        4 => $"{c[0]:0.###} {c[1]:0.###} {c[2]:0.###} {c[3]:0.###} {(stroke ? "K" : "k")} ",
        _ => "",
    };

    /// <summary>Default appearance (12.5.5 fallback) for markup annotations lacking /AP: highlight, underline, strikeout, squiggly, square, circle, line, polygon/polyline, ink and free text. Content is in page space; BBox is the annotation Rect.</summary>
    PdfStream? SynthAppearance(PdfDictionary a, PdfArray rectArr)
    {
        var sub = R(a.Get("Subtype")).AsName();
        var rect = Nums(rectArr);
        var c = Nums(a.Get("C"));
        var ic = Nums(a.Get("IC"));
        var ca = R(a.Get("CA")).AsNumber() ?? 1;
        double bw = 1;
        var dash = "";
        if (R(a.Get("BS")) is PdfDictionary bs)
        {
            bw = R(bs.Get("W")).AsNumber() ?? 1;
            if (R(bs.Get("S")).AsName() == "D") dash = "[3] 0 d ";
        }
        else if (Nums(a.Get("Border")) is { Length: >= 3 } bo) bw = bo[2];
        var sb = new System.Text.StringBuilder();
        var res = new Dictionary<string, PdfObject>();
        var gs = new Dictionary<string, PdfObject> { ["CA"] = new PdfReal(ca), ["ca"] = new PdfReal(ca) };
        double x0 = Math.Min(rect[0], rect[2]), y0 = Math.Min(rect[1], rect[3]), x1 = Math.Max(rect[0], rect[2]), y1 = Math.Max(rect[1], rect[3]);
        var q = Nums(a.Get("QuadPoints"));
        string P(double x, double y, string op) => $"{x:0.###} {y:0.###} {op}\n";
        switch (sub)
        {
            case "Highlight" when q.Length >= 8:
                gs["BM"] = new PdfName("Multiply");
                sb.Append(ColorOps(c.Length == 0 ? [1, 1, 0] : c, false));
                for (var i = 0; i + 8 <= q.Length; i += 8)
                    sb.Append(P(q[i], q[i + 1], "m")).Append(P(q[i + 2], q[i + 3], "l")).Append(P(q[i + 6], q[i + 7], "l")).Append(P(q[i + 4], q[i + 5], "l")).Append("h f\n");
                break;
            case "Underline" or "StrikeOut" or "Squiggly" when q.Length >= 8:
                sb.Append(ColorOps(c.Length == 0 ? [0] : c, true)).Append($"{Math.Max(bw, 1):0.##} w\n");
                for (var i = 0; i + 8 <= q.Length; i += 8)
                {
                    double ax = q[i + 4], ay = q[i + 5], bx = q[i + 6], by = q[i + 7];
                    if (sub == "StrikeOut") { ax = (q[i] + q[i + 4]) / 2; ay = (q[i + 1] + q[i + 5]) / 2; bx = (q[i + 2] + q[i + 6]) / 2; by = (q[i + 3] + q[i + 7]) / 2; }
                    if (sub == "Squiggly")
                    {
                        var len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                        var steps = Math.Max(1, (int)(len / 2));
                        double dx = (bx - ax) / len, dy = (by - ay) / len;
                        sb.Append(P(ax, ay, "m"));
                        for (var s = 1; s <= steps; s++)
                        {
                            var t = s * 2.0; var off = s % 2 == 0 ? 0 : 2;
                            sb.Append(P(ax + dx * t - dy * off, ay + dy * t + dx * off, "l"));
                        }
                        sb.Append("S\n");
                    }
                    else sb.Append(P(ax, ay, "m")).Append(P(bx, by, "l")).Append("S\n");
                }
                break;
            case "Square" or "Circle":
            {
                var h = bw / 2;
                double rx = x0 + h, ry = y0 + h, rw = x1 - x0 - bw, rh = y1 - y0 - bw;
                if (rw <= 0 || rh <= 0) return null;
                var fill = ic.Length > 0; var stroke = bw > 0;
                if (fill) sb.Append(ColorOps(ic, false));
                if (stroke) sb.Append(ColorOps(c.Length == 0 ? [0] : c, true)).Append($"{bw:0.##} w {dash}\n");
                if (sub == "Square") sb.Append($"{rx:0.###} {ry:0.###} {rw:0.###} {rh:0.###} re\n");
                else
                {
                    const double k = 0.5522847498; double cx = rx + rw / 2, cy = ry + rh / 2, ox = rw / 2 * k, oy = rh / 2 * k;
                    sb.Append(P(rx, cy, "m"))
                      .Append($"{rx:0.###} {cy + oy:0.###} {cx - ox:0.###} {ry + rh:0.###} {cx:0.###} {ry + rh:0.###} c\n")
                      .Append($"{cx + ox:0.###} {ry + rh:0.###} {rx + rw:0.###} {cy + oy:0.###} {rx + rw:0.###} {cy:0.###} c\n")
                      .Append($"{rx + rw:0.###} {cy - oy:0.###} {cx + ox:0.###} {ry:0.###} {cx:0.###} {ry:0.###} c\n")
                      .Append($"{cx - ox:0.###} {ry:0.###} {rx:0.###} {cy - oy:0.###} {rx:0.###} {cy:0.###} c h\n");
                }
                sb.Append(fill && stroke ? "B\n" : fill ? "f\n" : stroke ? "S\n" : "n\n");
                break;
            }
            case "Line" when Nums(a.Get("L")) is { Length: >= 4 } l:
                sb.Append(ColorOps(c.Length == 0 ? [0] : c, true)).Append($"{bw:0.##} w {dash}\n").Append(P(l[0], l[1], "m")).Append(P(l[2], l[3], "l")).Append("S\n");
                break;
            case "Polygon" or "PolyLine" when Nums(a.Get("Vertices")) is { Length: >= 4 } v:
            {
                var fill = sub == "Polygon" && ic.Length > 0;
                if (fill) sb.Append(ColorOps(ic, false));
                sb.Append(ColorOps(c.Length == 0 ? [0] : c, true)).Append($"{bw:0.##} w {dash}\n");
                for (var i = 0; i + 1 < v.Length; i += 2) sb.Append(P(v[i], v[i + 1], i == 0 ? "m" : "l"));
                sb.Append(sub == "Polygon" ? (fill ? "h B\n" : "s\n") : "S\n");
                break;
            }
            case "Ink" when R(a.Get("InkList")) is PdfArray ink:
                sb.Append(ColorOps(c.Length == 0 ? [0] : c, true)).Append($"{bw:0.##} w 1 J 1 j\n");
                foreach (var stroke in ink.Items)
                {
                    var pts = Nums(stroke);
                    for (var i = 0; i + 1 < pts.Length; i += 2) sb.Append(P(pts[i], pts[i + 1], i == 0 ? "m" : "l"));
                    sb.Append("S\n");
                }
                break;
            case "FreeText" when R(a.Get("Contents")) is PdfString txt:
            {
                var da = R(a.Get("DA")) is PdfString ds ? ds.Text : "0 g";
                var size = 12.0;
                var m = System.Text.RegularExpressions.Regex.Match(da, @"([\d.]+)\s+Tf");
                if (m.Success) double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out size);
                if (size <= 0) size = 12;
                var colorOps = System.Text.RegularExpressions.Regex.Replace(da, @"/\S+\s+[\d.]+\s+Tf", "");
                if (ic.Length > 0) sb.Append("q ").Append(ColorOps(ic, false)).Append($"{x0:0.###} {y0:0.###} {x1 - x0:0.###} {y1 - y0:0.###} re f Q\n");
                if (c.Length > 0 && bw > 0) sb.Append("q ").Append(ColorOps(c, true)).Append($"{bw:0.##} w {x0 + bw / 2:0.###} {y0 + bw / 2:0.###} {x1 - x0 - bw:0.###} {y1 - y0 - bw:0.###} re S Q\n");
                sb.Append($"q {x0:0.###} {y0:0.###} {x1 - x0:0.###} {y1 - y0:0.###} re W n BT {colorOps} /Helv {size:0.##} Tf {size * 1.15:0.##} TL {x0 + 2:0.###} {y1 - 2 - size:0.###} Td\n");
                foreach (var line in txt.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    sb.Append($"({line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")}) Tj T*\n");
                sb.Append("ET Q\n");
                res["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Helv"] = new PdfDictionary(new Dictionary<string, PdfObject>
                    { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName("Helvetica"), ["Encoding"] = new PdfName("WinAnsiEncoding") }) });
                break;
            }
            default: return null;
        }
        if (sb.Length == 0) return null;
        res["ExtGState"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["G0"] = new PdfDictionary(gs) });
        var body = System.Text.Encoding.Latin1.GetBytes("/G0 gs\n" + sb);
        return new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["BBox"] = new PdfArray([new PdfReal(x0), new PdfReal(y0), new PdfReal(x1), new PdfReal(y1)]),
            ["Resources"] = new PdfDictionary(res),
            ["Length"] = new PdfInteger(body.Length),
        }), body);
    }

    PdfObject? R(PdfObject? o) => _f.Resolve(o);

    void Run(byte[] content, PdfDictionary res)
    {
        var lx = new PdfLexer(content);
        var ops = new List<PdfObject>();
        var stackDepth = _stack.Count;
        PdfObject? o;
        _runDepth++;
        var collect = _runDepth == _collectDepth && Objects is not null;
        int stmtStart = 0, pathStart = -1;
        try
        {
            while ((o = lx.ReadObject(false)) is not null)
            {
                if (o is not PdfKeyword k) { ops.Add(o); if (ops.Count > 512) ops.RemoveAt(0); continue; }
                if (k.Value == "BI")
                {
                    if (collect && _hiddenDepth == 0) { var s0 = SkipWhite(content, stmtStart); var c0 = _gs.Ctm; InlineImage(lx, res); AddObject("image", s0, lx.Pos, c0, UnitSquare(c0)); }
                    else InlineImage(lx, res);
                    ops.Clear(); stmtStart = lx.Pos; continue;
                }
                if (collect) Observe(k.Value, ops, res, content, stmtStart, lx.Pos, ref pathStart);
                try { Exec(k.Value, ops, res); }
                catch (Exception e) when (e is InvalidCastException or ArgumentException or IndexOutOfRangeException or NotSupportedException or KeyNotFoundException or InvalidOperationException or OverflowException or NullReferenceException or FormatException or InvalidDataException or AggregateException)
                { Warn($"operator {k.Value}: {e.GetType().Name}"); }
                ops.Clear(); stmtStart = lx.Pos;
            }
        }
        finally { EndTextKnockout(); _runDepth--; }
        while (_stack.Count > stackDepth) { _gs = _stack.Pop(); _be.Restore(); }
    }

    /// <summary>A painted graphics object or clipping path located by byte range in its owning stream: the page content (<paramref name="Form"/> 0) or a form XObject (its object number).</summary>
    public sealed record PageObject(int Index, string Kind, int Start, int End, Matrix Ctm, double X0, double Y0, double X1, double Y1,
        bool HasFill, bool HasStroke, RgbColor Fill, RgbColor Stroke, double LineWidth, int Form = 0, PdfStream? Stream = null, int Ref = 0);

    /// <summary>When set, page-content paint objects and clipping paths are recorded here.</summary>
    public List<PageObject>? Objects { get; set; }

    static int SkipWhite(byte[] d, int p) { while (p < d.Length && PdfLexer.IsWhite(d[p])) p++; return p; }

    static (double, double, double, double) UnitSquare(Matrix m) => Bounds(m, [(0, 0), (1, 0), (0, 1), (1, 1)], 0);

    static (double, double, double, double) Bounds(Matrix m, ReadOnlySpan<(double X, double Y)> pts, double pad)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (px, py) in pts) { var (x, y) = m.Apply(px, py); x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        return (x0 - pad, y0 - pad, x1 + pad, y1 + pad);
    }

    void AddObject(string kind, int start, int end, Matrix ctm, (double X0, double Y0, double X1, double Y1) b, bool fill = false, bool stroke = false, PdfStream? stream = null, int xref = 0)
        => Objects!.Add(new PageObject(Objects.Count, kind, start, end, ctm, b.X0, b.Y0, b.X1, b.Y1, fill, stroke, fill ? Color(false) : default, stroke ? Color(true) : default, _gs.LineWidth, _curForm, stream, xref));

    void Observe(string op, List<PdfObject> a, PdfDictionary res, byte[] content, int stmtStart, int end, ref int pathStart)
    {
        switch (op)
        {
            case "m": case "l": case "c": case "v": case "y": case "h": case "re":
                if (pathStart < 0) pathStart = SkipWhite(content, stmtStart);
                break;
            case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n":
                if ((op != "n" || _pendingClip != 0) && pathStart >= 0 && _hiddenDepth == 0 && !_path.IsEmpty)
                {
                    var stroke = op is "S" or "s" or "B" or "B*" or "b" or "b*";
                    var fill = op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
                    var pts = new List<(double, double)>();
                    for (var i = 0; i + 1 < _path.Pts.Count; i += 2) pts.Add((_path.Pts[i], _path.Pts[i + 1]));
                    var sc = Math.Sqrt(Math.Abs(_gs.Ctm.Det));
                    var kind = op == "n" ? "clip-path" : _pendingClip != 0 ? "clipped-path" : "path";
                    AddObject(kind, pathStart, end, _gs.Ctm,
                        Bounds(_gs.Ctm, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pts), stroke ? _gs.LineWidth * sc / 2 : 0), fill, stroke);
                }
                pathStart = -1;
                break;
            case "Do":
                if (_hiddenDepth == 0 && a.Count > 0 && a[0] is PdfName xn && R(res.Get("XObject")) is PdfDictionary xd && R(xd.Get(xn.Value)) is PdfStream xs
                    && !(R(xs.Dict.Get("OC")) is PdfDictionary oc && IsHidden(oc)))
                {
                    var sub = R(xs.Dict.Get("Subtype")).AsName();
                    var xnum = xd.Get(xn.Value) is PdfRef xr ? xr.Number : 0;
                    if (sub == "Image") AddObject("image", SkipWhite(content, stmtStart), end, _gs.Ctm, UnitSquare(_gs.Ctm), stream: xs, xref: xnum);
                    else if (sub == "Form")
                    {
                        var fm = R(xs.Dict.Get("Matrix")) is PdfArray ma && ma.Count == 6 ? ToMatrix(ma) : Matrix.Identity;
                        var bb = R(xs.Dict.Get("BBox")) is PdfArray ba && ba.Count == 4 ? ba.Items.Select(x => R(x).AsNumber() ?? 0).ToArray() : [0, 0, 1, 1];
                        AddObject("form", SkipWhite(content, stmtStart), end, _gs.Ctm, Bounds(fm.Then(_gs.Ctm), [(bb[0], bb[1]), (bb[2], bb[1]), (bb[0], bb[3]), (bb[2], bb[3])], 0), xref: xnum);
                    }
                }
                break;
        }
    }

    static double N(List<PdfObject> a, int i) => i < a.Count ? a[i].AsNumber() ?? 0 : 0;

    void Exec(string op, List<PdfObject> a, PdfDictionary res)
    {
        if (!DrawGraphics && Objects is null && op is
            "m" or "l" or "c" or "v" or "y" or "h" or "re" or
            "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n" or "W" or "W*")
            return;
        switch (op)
        {
            case "q": _stack.Push(_gs); _gs = _gs.Clone(); _be.Save(); break;
            case "Q": if (_stack.Count > 0) { _gs = _stack.Pop(); _be.Restore(); } break;
            case "cm": _gs.Ctm = new Matrix(N(a, 0), N(a, 1), N(a, 2), N(a, 3), N(a, 4), N(a, 5)).Then(_gs.Ctm); break;
            case "w": _gs.LineWidth = N(a, 0); break;
            case "J": _gs.Cap = (LineCap)Math.Clamp((int)N(a, 0), 0, 2); break;
            case "j": _gs.Join = (LineJoin)Math.Clamp((int)N(a, 0), 0, 2); break;
            case "M": _gs.MiterLimit = N(a, 0); break;
            case "d": if (a.Count >= 2 && a[0] is PdfArray da) { _gs.Dash = da.Items.Select(x => x.AsNumber() ?? 0).ToArray(); _gs.DashPhase = N(a, 1); } break;
            case "ri": if (a.Count > 0 && a[0] is PdfName intent) _gs.Intent = RenderingIntentNames.Parse(intent.Value); break;
            case "i": break;
            case "gs": if (a.Count > 0 && a[0] is PdfName gn && R(res.Get("ExtGState")) is PdfDictionary egd && R(egd.Get(gn.Value)) is PdfDictionary g) ApplyExtGState(g, res); break;

            case "m": _path.MoveTo(N(a, 0), N(a, 1)); break;
            case "l": _path.LineTo(N(a, 0), N(a, 1)); break;
            case "c": _path.CurveTo(N(a, 0), N(a, 1), N(a, 2), N(a, 3), N(a, 4), N(a, 5)); break;
            case "v": { var (cx, cy) = _path.Current; _path.CurveTo(cx, cy, N(a, 0), N(a, 1), N(a, 2), N(a, 3)); break; }
            case "y": _path.CurveTo(N(a, 0), N(a, 1), N(a, 2), N(a, 3), N(a, 2), N(a, 3)); break;
            case "h": _path.Close(); break;
            case "re": _path.Rect(N(a, 0), N(a, 1), N(a, 2), N(a, 3)); break;

            case "S": Paint(false, false, true); break;
            case "s": _path.Close(); Paint(false, false, true); break;
            case "f": case "F": Paint(true, false, false); break;
            case "f*": Paint(true, true, false); break;
            case "B": Paint(true, false, true); break;
            case "B*": Paint(true, true, true); break;
            case "b": _path.Close(); Paint(true, false, true); break;
            case "b*": _path.Close(); Paint(true, true, true); break;
            case "n": Paint(false, false, false); break;
            case "W": _pendingClip = 1; break;
            case "W*": _pendingClip = 2; break;

            case "BT": _tm = _tlm = Matrix.Identity; _textClip = null; break;
            case "ET":
                EndTextKnockout();
                if (_textClip is { IsEmpty: false } tc) _be.Clip(tc, Matrix.Identity, false);
                _textClip = null; break;
            case "Tc": _gs.CharSpace = N(a, 0); break;
            case "Tw": _gs.WordSpace = N(a, 0); break;
            case "Tz": _gs.HScale = N(a, 0) / 100; break;
            case "TL": _gs.Leading = N(a, 0); break;
            case "Tr": _gs.RenderMode = (int)N(a, 0); break;
            case "Ts": _gs.Rise = N(a, 0); break;
            case "Tf":
                if (a.Count >= 2 && a[0] is PdfName fn)
                {
                    _gs.Font = LoadFont(res, fn.Value); _gs.FontSize = N(a, 1);
                    _gs.FontRef = R(res.Get("Font")) is PdfDictionary fdr && fdr.Get(fn.Value) is PdfRef frr ? frr.Number : 0;
                }
                break;
            case "Td": _tlm = Matrix.Translate(N(a, 0), N(a, 1)).Then(_tlm); _tm = _tlm; break;
            case "TD": _gs.Leading = -N(a, 1); _tlm = Matrix.Translate(N(a, 0), N(a, 1)).Then(_tlm); _tm = _tlm; break;
            case "Tm": _tm = _tlm = new Matrix(N(a, 0), N(a, 1), N(a, 2), N(a, 3), N(a, 4), N(a, 5)); break;
            case "T*": NextLine(); break;
            case "Tj": case "'": case "\"": case "TJ": ShowOp(op, a, res); break;

            case "CS": _gs.StrokeCs = PdfColorSpace.Parse(_f, a[0], res); _gs.StrokeColor = _gs.StrokeCs.InitialColor; _gs.StrokePattern = null; break;
            case "cs": _gs.FillCs = PdfColorSpace.Parse(_f, a[0], res); _gs.FillColor = _gs.FillCs.InitialColor; _gs.FillPattern = null; break;
            case "SC": case "SCN": SetColor(a, res, true); break;
            case "sc": case "scn": SetColor(a, res, false); break;
            case "G": _gs.StrokeCs = PdfColorSpace.Gray; _gs.StrokeColor = [N(a, 0)]; _gs.StrokePattern = null; break;
            case "g": _gs.FillCs = PdfColorSpace.Gray; _gs.FillColor = [N(a, 0)]; _gs.FillPattern = null; break;
            case "RG": _gs.StrokeCs = PdfColorSpace.Rgb; _gs.StrokeColor = [N(a, 0), N(a, 1), N(a, 2)]; _gs.StrokePattern = null; break;
            case "rg": _gs.FillCs = PdfColorSpace.Rgb; _gs.FillColor = [N(a, 0), N(a, 1), N(a, 2)]; _gs.FillPattern = null; break;
            case "K": _gs.StrokeCs = PdfColorSpace.Cmyk; _gs.StrokeColor = [N(a, 0), N(a, 1), N(a, 2), N(a, 3)]; _gs.StrokePattern = null; break;
            case "k": _gs.FillCs = PdfColorSpace.Cmyk; _gs.FillColor = [N(a, 0), N(a, 1), N(a, 2), N(a, 3)]; _gs.FillPattern = null; break;

            case "sh": if (_hiddenDepth == 0 && a.Count > 0 && a[0] is PdfName sn && R(res.Get("Shading")) is PdfDictionary shd) Shade(R(shd.Get(sn.Value)), res, null); break;
            case "Do": if (a.Count > 0 && a[0] is PdfName xn && R(res.Get("XObject")) is PdfDictionary xd) DoXObject(R(xd.Get(xn.Value)), res, xd.Get(xn.Value) is PdfRef xrf ? xrf.Number : 0); break;

            case "BMC": _hiddenStack.Add(a.Count > 0 && a[0].AsName() == "Artifact" ? "A" : ""); _mcidStack.Add(-1); break;
            case "BDC":
                {
                    var hidden = false;
                    var mcid = -1;
                    if (a.Count >= 2 && (a[1] is PdfName mpn && R(res.Get("Properties")) is PdfDictionary mpd ? R(mpd.Get(mpn.Value)) : a[1]) is PdfDictionary mdict
                        && mdict.Get("MCID") is { } rawMcid)
                    {
                        if (R(rawMcid) is PdfInteger { Value: >= 0 and <= int.MaxValue } mv) mcid = (int)mv.Value;
                        else Warn("invalid marked-content MCID");
                    }
                    _mcidStack.Add(mcid);
                    if (a.Count >= 2 && a[0].AsName() == "OC")
                    {
                        PdfObject? prop = a[1] is PdfName pn && R(res.Get("Properties")) is PdfDictionary pd ? R(pd.Get(pn.Value)) : a[1];
                        hidden = prop is PdfDictionary ocd && IsHidden(ocd);
                    }
                    _hiddenStack.Add(hidden ? "H" : a.Count > 0 && a[0].AsName() == "Artifact" ? "A" : "");
                    if (hidden) _hiddenDepth++;
                    break;
                }
            case "EMC":
                if (_mcidStack.Count == 0) { Warn("unmatched EMC"); break; }
                _mcidStack.RemoveAt(_mcidStack.Count - 1);
                if (_hiddenStack.Count > 0) { if (_hiddenStack[^1] == "H") _hiddenDepth--; _hiddenStack.RemoveAt(_hiddenStack.Count - 1); }
                break;
            case "d0": case "d1": case "MP": case "DP": case "BX": case "EX": break;
            default: Warn($"unknown operator {op}"); break;
        }
    }

    bool IsHidden(PdfDictionary oc)
    {
        var cfg = OptionalContentConfiguration ?? DefaultOptionalContentConfiguration
            ?? R(R(_f.Catalog.Get("OCProperties")) is PdfDictionary ocp ? ocp.Get("D") : null) as PdfDictionary;
        var type = R(oc.Get("Type")).AsName();
        if (type == "OCMD")
        {
            if (R(oc.Get("VE")) is { } expression)
            {
                var budget = 0;
                var expressionVisible = EvaluateVisibilityExpression(expression, cfg, 0, ref budget, out var valid);
                if (!valid)
                {
                    Warn("invalid optional-content visibility expression");
                    return true;
                }
                return !expressionVisible;
            }
            // Visibility policy AnyOn (default) / AllOn / AnyOff / AllOff over OCGs.
            var ocgs = R(oc.Get("OCGs"));
            var list = ocgs is PdfArray oa ? oa.Items.Select(R).OfType<PdfDictionary>().ToList() : ocgs is PdfDictionary od ? [od] : [];
            if (list.Count == 0) return false;
            var pol = R(oc.Get("P")).AsName() ?? "AnyOn";
            var states = list.Select(g => !OcgOff(g, cfg)).ToList();
            var visible = pol switch { "AllOn" => states.All(x => x), "AnyOff" => states.Any(x => !x), "AllOff" => states.All(x => !x), _ => states.Any(x => x) };
            return !visible;
        }
        return OcgOff(oc, cfg);
    }

    bool EvaluateVisibilityExpression(PdfObject? value, PdfDictionary? cfg, int depth, ref int budget, out bool valid)
    {
        valid = true;
        if (depth > 64 || ++budget > 10_000)
        {
            valid = false;
            return false;
        }

        var expression = R(value);
        if (expression is PdfDictionary ocg)
        {
            if (R(ocg.Get("Type")).AsName() != "OCG")
            {
                valid = false;
                return false;
            }
            return !OcgOff(ocg, cfg);
        }
        if (expression is not PdfArray array || array.Count < 2 || R(array[0]).AsName() is not { } op)
        {
            valid = false;
            return false;
        }

        if (op == "Not")
        {
            if (array.Count != 2)
            {
                valid = false;
                return false;
            }
            var child = EvaluateVisibilityExpression(array[1], cfg, depth + 1, ref budget, out valid);
            return valid && !child;
        }
        if (op is not ("And" or "Or") || array.Count < 3)
        {
            valid = false;
            return false;
        }

        var result = op == "And";
        for (var i = 1; i < array.Count; i++)
        {
            var child = EvaluateVisibilityExpression(array[i], cfg, depth + 1, ref budget, out var childValid);
            if (!childValid) valid = false;
            result = op == "And" ? result && child : result || child;
        }
        return valid && result;
    }

    bool OcgOff(PdfDictionary ocg, PdfDictionary? cfg)
    {
        if (_optionalContentOff.TryGetValue(ocg, out var off)) return off;
        off = OptionalContentState is { } state
            ? !state.IsVisibleCore(ocg, OptionalContentDisplayEvent, OptionalContentZoom, OptionalContentContext)
            : global::PortableDocument.Content.OptionalContentState.IsOff(
                _f, ocg, cfg, DefaultOptionalContentConfiguration, OptionalContentDisplayEvent, OptionalContentZoom,
                OptionalContentContext, Warn);
        _optionalContentOff[ocg] = off;
        return off;
    }

    void NextLine() { _tlm = Matrix.Translate(0, -_gs.Leading).Then(_tlm); _tm = _tlm; }

    void ApplyExtGState(PdfDictionary g, PdfDictionary res)
    {
        // Apply before /SMask regardless of dictionary entry order.
        if (R(g.Get("RI")) is PdfName intent) _gs.Intent = RenderingIntentNames.Parse(intent.Value);
        if (g.Get("UseBlackPtComp") is { } bpc)
        {
            switch (R(bpc))
            {
                case PdfName { Value: "ON" }: _gs.BlackPointCompensation = BlackPointCompensation.On; break;
                case PdfName { Value: "OFF" }: _gs.BlackPointCompensation = BlackPointCompensation.Off; break;
                case PdfName { Value: "Default" }: _gs.BlackPointCompensation = BlackPointCompensation.Default; break;
                default: Warn("invalid ExtGState /UseBlackPtComp: expected /ON, /OFF or /Default; retaining current state"); break;
            }
        }
        if (R(g.Get("OP")) is PdfBoolean opStroke)
        {
            _gs.StrokeOverprint = opStroke.Value;
            if (g.Get("op") is null) _gs.FillOverprint = opStroke.Value;
        }
        if (R(g.Get("op")) is PdfBoolean opFill) _gs.FillOverprint = opFill.Value;
        if (R(g.Get("OPM")) is { } opm)
        {
            if (opm.AsNumber() is { } mode && (mode == 0 || mode == 1)) _gs.OverprintMode = (int)mode;
            else Warn("invalid ExtGState /OPM: expected 0 or 1; retaining current state");
        }
        // TR2 takes precedence over TR; /Default and /Identity both select the (identity) page default here.
        var trKey = g.Get("TR2") is not null ? "TR2" : g.Get("TR") is not null ? "TR" : null;
        if (trKey is not null)
        {
            var trObj = R(g.Get(trKey));
            if (trObj is not null && _transferCache.TryGetValue(trObj, out var cached)) _gs.Transfer = cached;
            else if (TransferFunctions.TryParse(_f, trObj, trKey == "TR2", out var parsed))
            {
                _gs.Transfer = parsed;
                if (trObj is not null) _transferCache[trObj] = parsed;
            }
            else Warn($"invalid ExtGState /{trKey}: expected a function, an array of four functions or /Identity; retaining current state");
        }
        foreach (var (k, v0) in g.Entries)
        {
            var v = R(v0);
            switch (k)
            {
                case "LW": _gs.LineWidth = v.AsNumber() ?? 1; break;
                case "LC": _gs.Cap = (LineCap)Math.Clamp((int)(v.AsNumber() ?? 0), 0, 2); break;
                case "LJ": _gs.Join = (LineJoin)Math.Clamp((int)(v.AsNumber() ?? 0), 0, 2); break;
                case "ML": _gs.MiterLimit = v.AsNumber() ?? 10; break;
                case "D": if (v is PdfArray { Count: 2 } d && R(d[0]) is PdfArray da) { _gs.Dash = da.Items.Select(x => R(x).AsNumber() ?? 0).ToArray(); _gs.DashPhase = R(d[1]).AsNumber() ?? 0; } break;
                case "CA": _gs.StrokeAlpha = Math.Clamp(v.AsNumber() ?? 1, 0, 1); break;
                case "ca": _gs.FillAlpha = Math.Clamp(v.AsNumber() ?? 1, 0, 1); break;
                case "BM": _gs.BlendMode = FirstKnownBlend(v) ?? "Normal"; break;
                case "Font": if (v is PdfArray { Count: 2 } fa && R(fa[0]) is PdfDictionary fd) { _gs.Font = GetFont(fd); _gs.FontSize = R(fa[1]).AsNumber() ?? 0; } break;
                case "SMask": ApplySoftMask(v, res); break;
                case "AIS": _gs.AlphaIsShape = v is PdfBoolean { Value: true }; if (DrawGraphics) _be.SetAlphaIsShape(_gs.AlphaIsShape); break;
                case "TK": _gs.TextKnockout = v is not PdfBoolean { Value: false }; break;
            }
        }
    }

    static readonly HashSet<string> KnownBlends = ["Normal", "Compatible", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight", "SoftLight", "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity"];

    // /BM may be an array: the first name the consumer recognises wins (11.6.3).
    string? FirstKnownBlend(PdfObject v)
    {
        var names = new List<string?>();
        if (v is PdfArray a) { for (var i = 0; i < a.Count; i++) names.Add(R(a[i]).AsName()); } else names.Add(v.AsName());
        foreach (var n in names)
            if (n is not null && KnownBlends.Contains(n)) return n == "Compatible" ? "Normal" : n;
        return null;
    }

    void ApplySoftMask(PdfObject? v, PdfDictionary res)
    {
        if (!DrawGraphics) return;
        if (v is not PdfDictionary sm) { _be.SetSoftMask(null, false, default); _gs.SoftMaskActive = false; return; }
        if (R(sm.Get("G")) is not PdfStream g) return;
        var lum = R(sm.Get("S")).AsName() == "Luminosity";
        var bc = RgbColor.Black;
        if (lum && R(sm.Get("BC")) is PdfArray ba)
        {
            var c = ba.Items.Select(x => R(x).AsNumber() ?? 0).ToArray();
            var gcs = R(R(g.Dict.Get("Group")) is PdfDictionary gd ? gd.Get("CS") : null) is { } cso ? PdfColorSpace.Parse(_f, cso, res) : null;
            if (gcs is not null && gcs.Components == c.Length) bc = gcs.ToRgb(c, _gs.Intent, _gs.BlackPointCompensation);
            else bc = c.Length switch
            {
                1 => new RgbColor(c[0], c[0], c[0]),
                3 => new RgbColor(c[0], c[1], c[2]),
                4 => PdfColorSpace.Cmyk.ToRgb(c, _gs.Intent, _gs.BlackPointCompensation),
                _ => bc
            };
        }
        Func<double, double>? transfer = null;
        if (R(sm.Get("TR")) is { } tro && tro is not PdfName && PdfFunction.Parse(_f, tro) is { } trf)
            transfer = x => { var o = trf.Evaluate([x]); return o.Length > 0 ? o[0] : x; };
        var ctm = _gs.Ctm;
        var intent = _gs.Intent;
        var blackPointCompensation = _gs.BlackPointCompensation;
        _gs.SoftMaskActive = true;
        _be.SetSoftMask(off =>
        {
            var sb = _be; var saved = _gs;
            _be = off; _gs = new GState { Ctm = ctm, Intent = intent, BlackPointCompensation = blackPointCompensation };
            try { DoForm(g, res); }
            finally { _be = sb; _gs = saved; }
        }, lum, bc, transfer);
    }

    PdfFont? LoadFont(PdfDictionary res, string name) => R(res.Get("Font")) is PdfDictionary fd && R(fd.Get(name)) is PdfDictionary d ? GetFont(d) : null;

    PdfFont GetFont(PdfDictionary d)
    {
        if (!_fonts.TryGetValue(d, out var f)) _fonts[d] = f = new PdfFont(_f, d, _fallback);
        return f;
    }

    void SetColor(List<PdfObject> a, PdfDictionary res, bool stroke)
    {
        var cs = stroke ? _gs.StrokeCs : _gs.FillCs;
        var nums = a.Select(x => x.AsNumber()).Where(x => x is not null).Select(x => x!.Value).ToArray();
        PdfObject? pattern = null;
        if (cs is PdfColorSpace.PatternCs && a.Count > 0 && a[^1] is PdfName pn && R(res.Get("Pattern")) is PdfDictionary pd) pattern = R(pd.Get(pn.Value));
        if (stroke) { _gs.StrokeColor = nums; _gs.StrokePattern = pattern; }
        else { _gs.FillColor = nums; _gs.FillPattern = pattern; }
    }

    RgbColor Color(bool stroke)
    {
        var cs = stroke ? _gs.StrokeCs : _gs.FillCs;
        var c = stroke ? _gs.StrokeColor : _gs.FillColor;
        if (c.Length < cs.Components) c = [.. c, .. new double[cs.Components - c.Length]];
        return cs.Components == 0 ? RgbColor.Black : cs.ToRgb(c, _gs.Intent, _gs.BlackPointCompensation);
    }

    void Paint(bool fill, bool evenOdd, bool stroke)
    {
        var p = _path; _path = new PathData();
        var clip = _pendingClip; _pendingClip = 0;
        if (DrawGraphics && _hiddenDepth == 0 && !p.IsEmpty)
        {
            // 11.7.4.4: fill and stroke of one painting operator form a knockout group, so the stroke replaces the fill where they overlap.
            var knockout = fill && stroke && NeedsObjectGroup() && BeginPathKnockout(p);
            try
            {
                if (fill) FillPath(p, evenOdd);
                if (stroke) StrokeBackend(p, _gs.Ctm, Style());
            }
            finally { if (knockout) { _be.EndGroup(); _be.Restore(); } }
        }
        if (DrawGraphics && clip != 0) _be.Clip(p, _gs.Ctm, clip == 2);
    }

    // Constant alpha, a non-Normal blend mode or a soft mask is what makes overlapping elements of one object observable.
    bool NeedsObjectGroup() => _gs.FillAlpha < 1 || _gs.StrokeAlpha < 1 || _gs.BlendMode != "Normal" || _gs.SoftMaskActive;

    /// <summary>Opens a non-isolated knockout group clipped to the stroked bounds of <paramref name="p"/> so the layer stays small.</summary>
    bool BeginPathKnockout(PathData p)
    {
        var pts = p.Pts;
        if (pts.Count < 2) return false;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        for (var i = 0; i + 1 < pts.Count; i += 2)
        {
            x0 = Math.Min(x0, pts[i]); x1 = Math.Max(x1, pts[i]); y0 = Math.Min(y0, pts[i + 1]); y1 = Math.Max(y1, pts[i + 1]);
        }
        var ctm = _gs.Ctm;
        var scale = Math.Sqrt(Math.Abs(ctm.A * ctm.D - ctm.B * ctm.C));
        if (!(scale > 1e-9) || double.IsNaN(x0 + x1 + y0 + y1)) return false;
        var pad = Math.Max(_gs.LineWidth, 0) / 2 * (_gs.Join == LineJoin.Miter ? Math.Max(_gs.MiterLimit, 1) : 1) + 2 / scale;
        var box = new PathData(); box.Rect(x0 - pad, y0 - pad, x1 - x0 + 2 * pad, y1 - y0 + 2 * pad);
        _be.Save();
        _be.Clip(box, ctm, false);
        _be.BeginGroup(false, true, 1, "Normal");
        return true;
    }

    bool _textKnockout;
    int _textKnockoutDepth;

    /// <summary>11.7.4.5: with /TK true the glyphs of one text object are elements of a knockout group (opened lazily at the first painted glyph).</summary>
    void BeginTextKnockout()
    {
        if (_textKnockout || !_gs.TextKnockout || !NeedsObjectGroup()) return;
        _be.Save();
        _be.BeginGroup(false, true, 1, "Normal");
        _textKnockout = true; _textKnockoutDepth = _runDepth;
    }

    void EndTextKnockout()
    {
        if (!_textKnockout || _textKnockoutDepth != _runDepth) return;
        _textKnockout = false;
        _be.EndGroup(); _be.Restore();
    }

    StrokeStyle Style() => new(_gs.LineWidth, _gs.Cap, _gs.Join, _gs.MiterLimit, _gs.Dash, _gs.DashPhase);

    void FillPath(PathData p, bool evenOdd)
    {
        if (_gs.FillCs is PdfColorSpace.PatternCs && _gs.FillPattern is { } pat)
        {
            var pd = pat as PdfDictionary ?? (pat as PdfStream)?.Dict;
            var type = (int)(R(pd?.Get("PatternType")).AsNumber() ?? 0);
            if (type == 2 && pd is not null)
            {
                _be.Save();
                _be.Clip(p, _gs.Ctm, evenOdd);
                var pm = R(pd.Get("Matrix")) is PdfArray ma && ma.Count == 6 ? ToMatrix(ma) : Matrix.Identity;
                var saved = _gs.Ctm;
                Shade(R(pd.Get("Shading")), new PdfDictionary(), pm.Then(BaseCtm));
                _gs.Ctm = saved;
                _be.Restore();
            }
            else if (type == 1 && pat is PdfStream ps) TilingFill(p, evenOdd, ps);
            return;
        }
        FillBackend(p, _gs.Ctm, evenOdd);
    }

    bool IsFullyOpaque(bool stroke) => (stroke ? _gs.StrokeAlpha : _gs.FillAlpha) >= 1 && _gs.BlendMode == "Normal" && !_gs.SoftMaskActive;

    /// <summary>The colour sent to the backend: the converted colour mapped through /TR or /TR2 when the object is fully opaque (10.4, 11.7.5.2).</summary>
    RgbColor PaintColor(bool stroke)
    {
        var c = Color(stroke);
        return _gs.Transfer is { } tr && IsFullyOpaque(stroke) ? tr.Apply(c) : c;
    }

    void FillBackend(PathData p, Matrix m, bool evenOdd)
    {
        var op = OverprintFor(false, out var skip);
        if (skip) return;
        if (op is null) _be.Fill(p, m, evenOdd, PaintColor(false), _gs.FillAlpha, _gs.BlendMode);
        else _be.Fill(p, m, evenOdd, PaintColor(false), _gs.FillAlpha, _gs.BlendMode, op);
    }

    void StrokeBackend(PathData p, Matrix m, StrokeStyle style)
    {
        var op = OverprintFor(true, out var skip);
        if (skip) return;
        if (op is null) _be.Stroke(p, m, style, PaintColor(true), _gs.StrokeAlpha, _gs.BlendMode);
        else _be.Stroke(p, m, style, PaintColor(true), _gs.StrokeAlpha, _gs.BlendMode, op);
    }

    static readonly string[] ProcessColorants = ["Cyan", "Magenta", "Yellow", "Black"];

    /// <summary>
    /// Overprint simulation for the next fill or stroke (8.6.7, Tables 148/149) when <see cref="OverprintSimulation"/> is on, the applicable
    /// overprint parameter is true and the blend mode is Normal. <paramref name="skip"/> means the paint marks no colorant at all.
    /// Returns null for ordinary painting.
    /// </summary>
    OverprintPaint? OverprintFor(bool stroke, out bool skip)
    {
        skip = false;
        if (!OverprintSimulation || !(stroke ? _gs.StrokeOverprint : _gs.FillOverprint) || _gs.BlendMode != "Normal") return null;
        var cs = stroke ? _gs.StrokeCs : _gs.FillCs;
        if (cs is PdfColorSpace.PatternCs) return null;
        var c = stroke ? _gs.StrokeColor : _gs.FillColor;
        if (cs == PdfColorSpace.Cmyk)
        {
            if (c.Length < 4) return null;
            byte mask = 15;
            if (_gs.OverprintMode == 1) { mask = 0; for (var i = 0; i < 4; i++) if (c[i] != 0) mask |= (byte)(1 << i); }
            return Process(mask, c, out skip);
        }
        if (cs.Colorants is not { Count: > 0 } names) return null;
        if (names.All(n => n == "None")) { skip = true; return null; }
        if (names.Contains("All")) return null;
        if (names.All(n => Array.IndexOf(ProcessColorants, n) >= 0))
        {
            byte mask = 0;
            var map = new int[names.Count];
            for (var i = 0; i < names.Count; i++) { map[i] = Array.IndexOf(ProcessColorants, names[i]); mask |= (byte)(1 << map[i]); }
            var cmyk = new double[4];
            for (var i = 0; i < names.Count && i < c.Length; i++) cmyk[map[i]] = c[i];
            return Process(mask, cmyk, out skip);
        }
        return new OverprintPaint(0, 0, 0, 0, 0, Spot: true);

        static OverprintPaint? Process(byte mask, double[] v, out bool skip)
        {
            skip = mask == 0;
            return mask is 0 or 15 ? null : new OverprintPaint(mask, (float)Math.Clamp(v[0], 0, 1), (float)Math.Clamp(v[1], 0, 1), (float)Math.Clamp(v[2], 0, 1), (float)Math.Clamp(v[3], 0, 1));
        }
    }

    Matrix ToMatrix(PdfArray a) { var v = a.Items.Select(x => R(x).AsNumber() ?? 0).ToArray(); return new Matrix(v[0], v[1], v[2], v[3], v[4], v[5]); }

    /// <summary>The page's base transform (default user space → device), used as parent of pattern matrices.</summary>
    Matrix BaseCtm { get; set; } = Matrix.Identity;
    public void SetBase(Matrix m) => BaseCtm = m;

    void TilingFill(PathData p, bool evenOdd, PdfStream pat)
    {
        var d = pat.Dict;
        var bbox = R(d.Get("BBox")) as PdfArray;
        if (bbox is null || bbox.Count < 4) return;
        var bb = bbox.Items.Select(x => R(x).AsNumber() ?? 0).ToArray();
        double xs = R(d.Get("XStep")).AsNumber() ?? (bb[2] - bb[0]), ys = R(d.Get("YStep")).AsNumber() ?? (bb[3] - bb[1]);
        if (xs == 0) xs = bb[2] - bb[0];
        if (ys == 0) ys = bb[3] - bb[1];
        if (xs == 0 || ys == 0) return;
        var pm = (R(d.Get("Matrix")) is PdfArray ma && ma.Count == 6 ? ToMatrix(ma) : Matrix.Identity).Then(BaseCtm);
        var inv = pm.Invert();
        // Bounds of the filled path in pattern space determine the tile range.
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var dev = p.Transform(_gs.Ctm);
        for (var i = 0; i < dev.Pts.Count; i += 2)
        {
            var (px, py) = inv.Apply(dev.Pts[i], dev.Pts[i + 1]);
            minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
        }
        var i0 = (int)Math.Floor((minX - bb[2]) / xs); var i1 = (int)Math.Ceiling((maxX - bb[0]) / xs);
        var j0 = (int)Math.Floor((minY - bb[3]) / ys); var j1 = (int)Math.Ceiling((maxY - bb[1]) / ys);
        if ((long)(i1 - i0 + 1) * (j1 - j0 + 1) > 20000) { Warn("tiling pattern with too many tiles skipped"); return; }
        var paintType = (int)(R(d.Get("PaintType")).AsNumber() ?? 1);
        var uncolored = paintType == 2;
        var content = _f.DecodeStream(pat);
        var res = R(d.Get("Resources")) as PdfDictionary ?? new PdfDictionary();
        var color = PaintColor(false);
        _be.Save();
        _be.Clip(p, _gs.Ctm, evenOdd);
        var savedGs = _gs; var savedStack = new Stack<GState>(_stack.Reverse()); var savedPath = _path;
        for (var j = j0; j <= j1; j++)
            for (var i = i0; i <= i1; i++)
            {
                _be.Save();
                _gs = new GState { Ctm = Matrix.Translate(i * xs, j * ys).Then(pm), Intent = savedGs.Intent, BlackPointCompensation = savedGs.BlackPointCompensation };
                if (uncolored) { _gs.FillCs = PdfColorSpace.Rgb; _gs.FillColor = [color.R, color.G, color.B]; _gs.StrokeCs = PdfColorSpace.Rgb; _gs.StrokeColor = _gs.FillColor; }
                var r = new PathData(); r.Rect(bb[0], bb[1], bb[2] - bb[0], bb[3] - bb[1]);
                _be.Clip(r, _gs.Ctm, false);
                _path = new PathData();
                Run(content, res);
                _be.Restore();
            }
        _gs = savedGs; _path = savedPath;
        _stack.Clear(); foreach (var s in savedStack.Reverse()) _stack.Push(s);
        _be.Restore();
    }

    /// <summary>A text-showing operator (Tj, TJ, ' or ") in the page's own content stream, with its device-space geometry.</summary>
    /// <remarks>Advance is a TJ adjustment along the font's writing axis. Pieces contain glyph boxes for both writing modes.
    /// Mcid is local to Form (0 for page content); enclosing form MCIDs are never inherited.</remarks>
    public sealed record TextSpan(int Index, string Op, string Text, double X, double Y, double Width, double Size, double Angle,
        string FontName, RgbColor Color, double Advance, bool Vertical, int Mcid = -1,
        int Form = 0, int FontRef = 0, PdfFont? Font = null, IReadOnlyList<TextPiece>? Pieces = null)
    {
        /// <summary>One-em glyph space to the same device space as X/Y, including text scale, rise and CTM.</summary>
        public Matrix RenderingMatrix { get; init; }
        public double CharacterSpacing { get; init; }
        public double WordSpacing { get; init; }
        /// <summary>The page sequence surrounding an untagged form invocation; not an MCID in this span's form.</summary>
        public int EnclosingPageMcid { get; init; } = -1;
        /// <summary>Whether this text is inside an explicit /Artifact marked-content sequence.</summary>
        public bool IsArtifact { get; init; }
    }

    /// <summary>One shown glyph: its raw string bytes, device-space box and writing-axis advance as a TJ number.</summary>
    public sealed record TextGlyph(byte[] Bytes, double X0, double Y0, double X1, double Y1, double Adv);

    /// <summary>Either a TJ adjustment number or a run of glyphs from one string operand.</summary>
    public sealed record TextPiece(double? Adj, IReadOnlyList<TextGlyph>? Glyphs);

    /// <summary>When set, text operators in the page content and in form XObjects reached by indirect reference are recorded here; <see cref="TextSpan.Form"/> is 0 for the page.</summary>
    public List<TextSpan>? TextSpans { get; set; }
    /// <summary>Disable painting and image/glyph-outline decoding when collecting text only.</summary>
    public bool DrawGraphics { get; set; } = true;
    int _runDepth, _spanIndex, _collectDepth = 1, _curForm;

    int CurrentMcid()
    {
        for (var i = _mcidStack.Count - 1; i >= 0; i--) if (_mcidStack[i] >= 0) return _mcidStack[i];
        return -1;
    }

    (double X, double Y) TextOrigin() =>
        new Matrix(_gs.FontSize * _gs.HScale, 0, 0, _gs.FontSize, 0, _gs.Rise).Then(_tm).Then(_gs.Ctm).Apply(0, 0);

    void ShowOp(string op, List<PdfObject> a, PdfDictionary res)
    {
        if (op == "'") NextLine();
        else if (op == "\"") { _gs.WordSpace = N(a, 0); _gs.CharSpace = N(a, 1); NextLine(); }

        var idx = _runDepth == _collectDepth ? _spanIndex++ : -1;
        var collect = idx >= 0 && TextSpans is not null && _hiddenDepth == 0 && _gs.Font is not null;
        var tmBefore = _tm; var start = collect ? TextOrigin() : default;
        var sb = collect ? new System.Text.StringBuilder() : null;
        var pieces = collect ? new List<TextPiece>() : null;
        var font = _gs.Font;

        void Show(byte[] bytes)
        {
            List<TextGlyph>? rec = pieces is null ? null : [];
            ShowText(bytes, res, rec, sb);
            if (rec is not null) pieces!.Add(new TextPiece(null, rec));
        }

        if (op == "TJ")
        {
            if (a.Count > 0 && a[0] is PdfArray arr)
                foreach (var item in arr.Items)
                {
                    if (item is PdfString ts) Show(ts.Bytes);
                    else if (item.AsNumber() is { } adj)
                    {
                        var d = -adj / 1000 * _gs.FontSize;
                        _tm = (_gs.Font?.Vertical == true ? Matrix.Translate(0, d) : Matrix.Translate(d * _gs.HScale, 0)).Then(_tm);
                        if (sb is not null && adj <= -200) sb.Append(' ');
                        pieces?.Add(new TextPiece(adj, null));
                    }
                }
        }
        else if (a.Count > (op == "\"" ? 2 : 0) && a[op == "\"" ? 2 : 0] is PdfString s) Show(s.Bytes);

        if (!collect || font is null) return;
        var end = TextOrigin();
        var trm = new Matrix(_gs.FontSize * _gs.HScale, 0, 0, _gs.FontSize, 0, _gs.Rise).Then(tmBefore).Then(_gs.Ctm);
        var displacement = tmBefore.Invert().Apply(_tm.E, _tm.F);
        var local = font.Vertical ? displacement.Y : displacement.X;
        var scale = _gs.FontSize * (font.Vertical ? 1 : _gs.HScale);
        TextSpans!.Add(new TextSpan(idx, op, sb!.ToString(), start.X, start.Y, Math.Sqrt((end.X - start.X) * (end.X - start.X) + (end.Y - start.Y) * (end.Y - start.Y)),
            Math.Sqrt(trm.C * trm.C + trm.D * trm.D), Math.Atan2(trm.B, trm.A) * 180 / Math.PI,
            font.Dict.Get("BaseFont") is PdfName bn ? bn.Value : "", Color(false), scale == 0 ? 0 : -local * 1000 / scale, font.Vertical, CurrentMcid(),
            _curForm, _gs.FontRef, font, pieces)
        {
            EnclosingPageMcid = _pageMcid, IsArtifact = _hiddenStack.Contains("A"), RenderingMatrix = trm,
            CharacterSpacing = _gs.FontSize == 0 ? 0 : _gs.CharSpace * Math.Sqrt(trm.C * trm.C + trm.D * trm.D) / _gs.FontSize,
            WordSpacing = _gs.FontSize == 0 ? 0 : _gs.WordSpace * Math.Sqrt(trm.C * trm.C + trm.D * trm.D) / _gs.FontSize
        });
    }

    void ShowText(byte[] s, PdfDictionary res, List<TextGlyph>? rec = null, System.Text.StringBuilder? text = null)
    {
        var font = _gs.Font;
        if (font is null || _hiddenDepth > 0 && false) return;
        var hidden = _hiddenDepth > 0;
        var off = 0;
        foreach (var run in font.Decode(s))
        {
            text?.Append(font.ToUnicode(run));
            var fs = _gs.FontSize;
            var vertical = font.Vertical ? font.VerticalMetrics(run) : default;
            // v points from the horizontal origin to the vertical origin; outlines use the former.
            var origin = font.Vertical ? Matrix.Translate(-vertical.X, -vertical.Y) : Matrix.Identity;
            var glyphToUser = origin.Then(new Matrix(fs * _gs.HScale, 0, 0, fs, 0, _gs.Rise)).Then(_tm);
            var trm = glyphToUser.Then(_gs.Ctm);
            var advance = (font.Vertical ? vertical.Advance : run.Width) * fs
                + _gs.CharSpace + (font.IsSpace(run) ? _gs.WordSpace : 0);
            if (rec is not null && fs != 0)
            {
                var (bx0, by0, bx1, by1) = Bounds(trm, [(0, -0.25), (run.Width, -0.25), (0, 0.95), (run.Width, 0.95)], 0);
                var adv = -advance * 1000 / fs;
                rec.Add(new TextGlyph(s[off..Math.Min(s.Length, off + run.ByteLength)], bx0, by0, bx1, by1, adv));
            }
            off += run.ByteLength;
            if (DrawGraphics && !hidden)
            {
                if (font.IsType3) DrawType3(font, run.Code, trm);
                else
                {
                    var outline = font.GetGlyph(run.Code, font.ToUnicode(run));
                    foreach (var warning in font.Warnings) Warn(warning);
                    if (outline is not null)
                    {
                        var m = Matrix.Scale(0.001, 0.001).Then(trm);
                        var mode = _gs.RenderMode;
                        if (mode is 0 or 1 or 2 or 4 or 5 or 6) BeginTextKnockout();
                        if (mode is 0 or 2 or 4 or 6) FillGlyph(outline, m);
                        if (mode is 1 or 2 or 5 or 6)
                        {
                            // Stroke width is in user space, so stroke with the text-space transform folded into the path.
                            var up = outline.Transform(Matrix.Scale(0.001, 0.001).Then(glyphToUser));
                            StrokeBackend(up, _gs.Ctm, Style());
                        }
                        if (mode >= 4) { _textClip ??= new PathData(); _textClip.Append(outline.Transform(m)); }
                    }
                }
            }
            _tm = (font.Vertical ? Matrix.Translate(0, advance) : Matrix.Translate(advance * _gs.HScale, 0)).Then(_tm);
        }
    }

    void FillGlyph(PathData outline, Matrix m)
    {
        if (_gs.FillCs is PdfColorSpace.PatternCs && _gs.FillPattern is not null)
        {
            // Pattern fills of text: transform to the current user space so FillPath's pattern logic applies.
            var userPath = outline.Transform(m.Then(_gs.Ctm.Invert()));
            FillPath(userPath, false);
            return;
        }
        FillBackend(outline, m, false);
    }

    void DrawType3(PdfFont font, int code, Matrix trm)
    {
        if (font.GetCharProc(code) is not { } proc || _type3Depth > 4) return;
        var tm = font.Type3Matrix;
        var fm = new Matrix(tm[0], tm[1], tm[2], tm[3], tm[4], tm[5]);
        var data = _f.DecodeStream(proc);
        var saved = _gs; var savedPath = _path; var savedTm = _tm; var savedTlm = _tlm;
        _be.Save();
        _stack.Push(_gs);
        _gs = _gs.Clone();
        _gs.Ctm = fm.Then(trm);
        _type3Depth++;
        // d1 glyphs inherit the current colour; d0 glyphs set their own, so the fill colour is simply left in place.
        Run(data, font.Type3Resources ?? new PdfDictionary());
        _type3Depth--;
        _gs = _stack.Pop();
        _be.Restore();
        _gs = saved; _path = savedPath; _tm = savedTm; _tlm = savedTlm;
    }

    void DoXObject(PdfObject? obj, PdfDictionary res, int objNum = 0)
    {
        if (obj is not PdfStream xs || _hiddenDepth > 0) return;
        if (R(xs.Dict.Get("OC")) is PdfDictionary oc && IsHidden(oc)) return;
        var sub = R(xs.Dict.Get("Subtype")).AsName();
        if (sub == "Image") DrawImage(xs, res);
        else if (sub == "Form") DoForm(xs, res, objNum);
    }

    void DoForm(PdfStream xs, PdfDictionary parentRes, int objNum = 0, bool annotationAppearance = false)
    {
        if (!_activeForms.Add(xs.Dict)) return;
        var saved = _gs; var savedPath = _path; var savedTm = _tm; var savedTlm = _tlm;
        var sCollect = _collectDepth; var sForm = _curForm; var sIdx = _spanIndex;
        var savedHidden = _hiddenStack; var savedMcids = _mcidStack;
        var savedHiddenDepth = _hiddenDepth; var savedPageMcid = _pageMcid;
        var stackDepth = _stack.Count;
        var grouped = false;
        // MCIDs belong to the stream declaring them, never to a descendant form.
        // Preserve only the explicit page-owned Do association for legacy page extraction.
        _pageMcid = _runDepth == 1 ? CurrentMcid() : CurrentMcid() >= 0 ? -1 : _pageMcid;
        _hiddenStack = []; _mcidStack = [];
        try
        {
            // Text and objects of a form are recorded only when the form is an indirect object (so it can be rewritten) reached from a recorded stream.
            if ((_runDepth == _collectDepth || annotationAppearance && _runDepth == 0) && objNum > 0) { _collectDepth = _runDepth + 1; _curForm = objNum; _spanIndex = 0; }
            else if (_runDepth == _collectDepth) _collectDepth = -1;
            _be.Save();
            _stack.Push(_gs);
            _gs = _gs.Clone();
            if (R(xs.Dict.Get("Matrix")) is PdfArray ma && ma.Count == 6) _gs.Ctm = ToMatrix(ma).Then(_gs.Ctm);
            if (R(xs.Dict.Get("BBox")) is PdfArray bb && bb.Count == 4)
            {
                var v = bb.Items.Select(x => R(x).AsNumber() ?? 0).ToArray();
                var r = new PathData(); r.Rect(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Abs(v[2] - v[0]), Math.Abs(v[3] - v[1]));
                _be.Clip(r, _gs.Ctm, false);
            }
            _path = new PathData();
            var res = R(xs.Dict.Get("Resources")) as PdfDictionary ?? parentRes;
            if (R(xs.Dict.Get("Group")) is PdfDictionary grp && R(grp.Get("S")).AsName() == "Transparency")
            {
                var isolated = R(grp.Get("I")) is PdfBoolean { Value: true };
                var knockout = R(grp.Get("K")) is PdfBoolean { Value: true };
                var alpha = _gs.FillAlpha; var bm = _gs.BlendMode;
                if (knockout || alpha < 1 || bm != "Normal" || _gs.SoftMaskActive || (isolated && TransparencyScan.UsesBlendMode(_f, res)))
                {
                    _be.BeginGroup(isolated, knockout, alpha, bm);
                    _gs.FillAlpha = _gs.StrokeAlpha = 1; _gs.BlendMode = "Normal"; _gs.SoftMaskActive = false;
                    grouped = true;
                }
            }
            Run(_f.DecodeStream(xs), res);
        }
        finally
        {
            if (_mcidStack.Count > 0) Warn("unclosed marked content in form");
            _hiddenStack = savedHidden; _mcidStack = savedMcids;
            _hiddenDepth = savedHiddenDepth; _pageMcid = savedPageMcid;
            _collectDepth = sCollect; _curForm = sForm; _spanIndex = sIdx;
            _activeForms.Remove(xs.Dict);
            if (grouped) _be.EndGroup();
            while (_stack.Count > stackDepth) { _gs = _stack.Pop(); _be.Restore(); }
            _gs = saved; _path = savedPath; _tm = savedTm; _tlm = savedTlm;
        }
    }

    // ---- Shadings ----
    void Shade(PdfObject? shObj, PdfDictionary res, Matrix? shadingToDevice)
    {
        if (!DrawGraphics) return;
        var intent = _gs.Intent;
        var blackPointCompensation = _gs.BlackPointCompensation;
        var d = shObj as PdfDictionary ?? (shObj as PdfStream)?.Dict;
        if (d is null) return;
        var m = shadingToDevice ?? _gs.Ctm;
        var inv = m.Invert();
        var type = (int)(R(d.Get("ShadingType")).AsNumber() ?? 0);
        var cs = PdfColorSpace.Parse(_f, d.Get("ColorSpace"), res);
        PdfFunction[]? fns = null;
        var fo = R(d.Get("Function"));
        if (type is >= 4 and <= 7 && fo is not null)
        {
            var functions = fo is PdfArray array ? array.Items.ToArray() : [fo];
            if (fo is PdfArray && functions.Length != cs.Components)
            {
                Warn("mesh shading Function array must contain one function per colour component");
                return;
            }
            fns = new PdfFunction[functions.Length];
            for (var i = 0; i < functions.Length; i++)
            {
                var fn = PdfFunction.Parse(_f, functions[i]);
                if (fn is null)
                {
                    Warn($"mesh shading Function {i}: unsupported or invalid function");
                    return;
                }
                if (fn.Domain.Length != 2 || fn.Outputs != (fo is PdfArray ? 1 : cs.Components))
                {
                    Warn($"mesh shading Function {i}: expected one input and {(fo is PdfArray ? 1 : cs.Components)} output component(s)");
                    return;
                }
                fns[i] = fn;
            }
        }
        else if (fo is PdfArray fa) fns = fa.Items.Select(x => PdfFunction.Parse(_f, x)).Where(x => x is not null).Select(x => x!).ToArray();
        else if (fo is not null && PdfFunction.Parse(_f, fo) is { } one) fns = [one];

        RgbColor EvalT(double t)
        {
            if (fns is null) return RgbColor.Black;
            double[] comps = fns.Length == 1 ? fns[0].Evaluate([t]) : fns.Select(fn => fn.Evaluate([t])[0]).ToArray();
            if (comps.Length < cs.Components) comps = [.. comps, .. new double[cs.Components - comps.Length]];
            return cs.ToRgb(comps, intent, blackPointCompensation);
        }

        double[] Arr(string k) => R(d.Get(k)) is PdfArray a ? a.Items.Select(x => R(x).AsNumber() ?? 0).ToArray() : [];
        bool[] Ext() => R(d.Get("Extend")) is PdfArray a && a.Count >= 2 ? [R(a[0]) is PdfBoolean { Value: true }, R(a[1]) is PdfBoolean { Value: true }] : [false, false];

        if (type == 2 || type == 3)
        {
            var c = Arr("Coords"); var dom = Arr("Domain") is { Length: 2 } dm ? dm : [0, 1]; var ext = Ext();
            if (fns is null) return;
            // Pre-sample the colour ramp for speed.
            var ramp = new RgbColor[257];
            for (var i = 0; i <= 256; i++) ramp[i] = EvalT(dom[0] + (dom[1] - dom[0]) * i / 256.0);
            RgbColor At(double s) => ramp[(int)Math.Round(Math.Clamp(s, 0, 1) * 256)];
            if (type == 2 && c.Length >= 4)
            {
                double x0 = c[0], y0 = c[1], dx = c[2] - c[0], dy = c[3] - c[1];
                var den = dx * dx + dy * dy;
                PaintShader((px, py) =>
                {
                    var (ux, uy) = inv.Apply(px, py);
                    var s = den == 0 ? 0 : ((ux - x0) * dx + (uy - y0) * dy) / den;
                    if (s < 0) return ext[0] ? At(0) : null;
                    if (s > 1) return ext[1] ? At(1) : null;
                    return At(s);
                }, _gs.FillAlpha, _gs.BlendMode);
            }
            else if (type == 3 && c.Length >= 6)
            {
                double x0 = c[0], y0 = c[1], r0 = c[2], x1 = c[3], y1 = c[4], r1 = c[5];
                PaintShader((px, py) =>
                {
                    var (ux, uy) = inv.Apply(px, py);
                    // Solve |p - (c0 + s(c1-c0))| = r0 + s(r1-r0) for the largest valid s.
                    double cdx = x1 - x0, cdy = y1 - y0, dr = r1 - r0;
                    double pdx = ux - x0, pdy = uy - y0;
                    var a = cdx * cdx + cdy * cdy - dr * dr;
                    var b = pdx * cdx + pdy * cdy + r0 * dr;
                    var cc = pdx * pdx + pdy * pdy - r0 * r0;
                    double? best = null;
                    void Try(double s)
                    {
                        if (r0 + s * dr < 0) return;
                        if (s < 0 && !ext[0] || s > 1 && !ext[1]) return;
                        if (best is null || s > best) best = s;
                    }
                    if (Math.Abs(a) < 1e-12) { if (Math.Abs(b) > 1e-12) Try(cc / (2 * b)); }
                    else
                    {
                        var disc = b * b - a * cc;
                        if (disc >= 0) { var sq = Math.Sqrt(disc); Try((b + sq) / a); Try((b - sq) / a); }
                    }
                    return best is { } sv ? At(sv) : null;
                }, _gs.FillAlpha, _gs.BlendMode);
            }
        }
        else if (type == 1)
        {
            var dom = Arr("Domain") is { Length: 4 } dm ? dm : [0, 1, 0, 1];
            var mm = R(d.Get("Matrix")) is PdfArray ma && ma.Count == 6 ? ToMatrix(ma) : Matrix.Identity;
            var minv = mm.Invert();
            PaintShader((px, py) =>
            {
                var (ux, uy) = inv.Apply(px, py);
                var (sx, sy) = minv.Apply(ux, uy);
                if (sx < dom[0] || sx > dom[1] || sy < dom[2] || sy > dom[3] || fns is null) return null;
                double[] comps = fns.Length == 1 ? fns[0].Evaluate([sx, sy]) : fns.Select(fn => fn.Evaluate([sx, sy])[0]).ToArray();
                if (comps.Length < cs.Components) comps = [.. comps, .. new double[cs.Components - comps.Length]];
                return cs.ToRgb(comps, intent, blackPointCompensation);
            }, _gs.FillAlpha, _gs.BlendMode);
        }
        else if (type is >= 4 and <= 7 && shObj is PdfStream ms)
        {
            var nComps = fns is null ? cs.Components : 1;
            var dec = Arr("Decode");
            int Int(string k, int def) => (int)(R(d.Get(k)).AsNumber() ?? def);
            var mesh = MeshShading.Decode(type, _f.DecodeStream(ms), dec, Int("BitsPerCoordinate", 8), Int("BitsPerComponent", 8),
                type == 5 ? 0 : Int("BitsPerFlag", 8), nComps, Int("VerticesPerRow", 0), m);
            var shader = MeshShading.ToShader(mesh, comps => fns is null
                ? cs.ToRgb(comps.Length < cs.Components ? [.. comps, .. new double[cs.Components - comps.Length]] : comps, intent, blackPointCompensation)
                : EvalT(comps[0]));
            PaintShader(shader, _gs.FillAlpha, _gs.BlendMode);
        }
        else Warn($"shading type {type} not rendered");
    }

    /// <summary>Paints a shading, mapping its colours through the current transfer function when the shading is fully opaque (11.7.5.2).</summary>
    void PaintShader(Func<double, double, RgbColor?> shader, double alpha, string blendMode)
    {
        if (_gs.Transfer is { } tr && IsFullyOpaque(false))
        {
            var inner = shader;
            shader = (x, y) => inner(x, y) is { } c ? tr.Apply(c) : null;
        }
        _be.PaintShader(shader, alpha, blendMode);
    }

    // ---- Images ----
    void InlineImage(PdfLexer lx, PdfDictionary res)
    {
        var d = new Dictionary<string, PdfObject>();
        while (true)
        {
            var k = lx.ReadObject(false);
            if (k is null) return;
            if (k is PdfKeyword { Value: "ID" }) break;
            if (k is not PdfName kn) continue;
            var v = lx.ReadObject(false);
            if (v is null) return;
            d[Expand(kn.Value)] = v;
        }
        lx.Pos++; // single whitespace after ID
        var start = lx.Pos;
        var data = lx.Data;
        int end;
        var filter = d.GetValueOrDefault("Filter");
        if (filter is null)
        {
            var w = (int)(d.GetValueOrDefault("Width").AsNumber() ?? 0); var h = (int)(d.GetValueOrDefault("Height").AsNumber() ?? 0);
            var bpc = d.ContainsKey("ImageMask") ? 1 : (int)(d.GetValueOrDefault("BitsPerComponent").AsNumber() ?? 8);
            var ncomp = d.ContainsKey("ImageMask") ? 1 : PdfColorSpace.Parse(_f, d.GetValueOrDefault("ColorSpace"), res).Components;
            end = Math.Min(data.Length, start + (w * ncomp * bpc + 7) / 8 * h);
        }
        else
        {
            end = FindEI(data, start);
        }
        var raw = data.AsSpan(start, Math.Max(0, end - start)).ToArray();
        lx.Pos = end;
        // consume EI
        var save = lx.Pos;
        var t = lx.ReadObject(false);
        if (t is not PdfKeyword { Value: "EI" }) lx.Pos = FindEI(data, end) + 2 is var p && p <= data.Length ? p : save;
        if (_hiddenDepth > 0) return;
        if (d.TryGetValue("Filter", out var fv)) d["Filter"] = ExpandNames(fv);
        var stream = new PdfStream(new PdfDictionary(d), raw);
        try { DrawImage(stream, res); } catch (Exception e) when (e is NotSupportedException or IndexOutOfRangeException or ArgumentException) { Warn("inline image: " + e.Message); }
    }

    static PdfObject ExpandNames(PdfObject o) => o is PdfArray a ? new PdfArray(a.Items.Select(x => x is PdfName n ? new PdfName(ExpandFilter(n.Value)) : x).ToList()) : o is PdfName nn ? new PdfName(ExpandFilter(nn.Value)) : o;

    static string ExpandFilter(string n) => n switch { "AHx" => "ASCIIHexDecode", "A85" => "ASCII85Decode", "LZW" => "LZWDecode", "Fl" => "FlateDecode", "RL" => "RunLengthDecode", "CCF" => "CCITTFaxDecode", "DCT" => "DCTDecode", _ => n };

    static string Expand(string k) => k switch
    {
        "BPC" => "BitsPerComponent", "CS" => "ColorSpace", "D" => "Decode", "DP" => "DecodeParms", "F" => "Filter", "H" => "Height", "IM" => "ImageMask", "I" => "Interpolate", "W" => "Width", _ => k
    };

    static int FindEI(byte[] d, int from)
    {
        for (var i = from; i + 1 < d.Length; i++)
            if (d[i] == 'E' && d[i + 1] == 'I' && (i == 0 || PdfLexer.IsWhite(d[i - 1])) && (i + 2 >= d.Length || PdfLexer.IsWhite(d[i + 2])))
            {
                // Heuristic: following bytes should look like content stream text, not binary.
                var ok = true;
                for (var k = i + 2; k < Math.Min(d.Length, i + 12); k++) if (d[k] > 127 || d[k] < 9) { ok = false; break; }
                if (ok) return i;
            }
        return d.Length;
    }

    void DrawImage(PdfStream xs, PdfDictionary res)
    {
        if (!DrawGraphics) return;
        var img = ImageDecoder.Decode(_f, xs, res, out var isMask, out var warn, _gs.Intent, _gs.BlackPointCompensation);
        if (warn is not null) Warn(warn);
        if (img is null) return;
        if (!isMask && _gs.Transfer is { } tr && IsFullyOpaque(false)) img = tr.Apply(img);
        _be.DrawImage(img, _gs.Ctm, _gs.FillAlpha, isMask ? PaintColor(false) : RgbColor.Black, _gs.BlendMode);
    }
}
