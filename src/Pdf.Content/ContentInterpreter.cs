using Pdf.Core;
using Pdf.Document;

namespace Pdf.Content;

sealed class GState
{
    public Matrix Ctm = Matrix.Identity;
    public PdfColorSpace FillCs = PdfColorSpace.Gray, StrokeCs = PdfColorSpace.Gray;
    public double[] FillColor = [0], StrokeColor = [0];
    public PdfObject? FillPattern, StrokePattern;
    public double LineWidth = 1, MiterLimit = 10, DashPhase;
    public LineCap Cap; public LineJoin Join;
    public double[] Dash = [];
    public double FillAlpha = 1, StrokeAlpha = 1;
    public string BlendMode = "Normal";
    public double CharSpace, WordSpace, HScale = 1, Leading, Rise;
    public PdfFont? Font; public double FontSize;
    public int RenderMode;

    public GState Clone() => (GState)MemberwiseClone();
}

public sealed class ContentInterpreter
{
    readonly PdfFile _f;
    IRenderBackend _be;
    readonly IFontFallback _fallback;
    readonly Dictionary<PdfDictionary, PdfFont> _fonts = new(ReferenceEqualityComparer.Instance);
    readonly Stack<GState> _stack = new();
    readonly HashSet<PdfDictionary> _activeForms = new(ReferenceEqualityComparer.Instance);
    GState _gs = new();
    PathData _path = new();
    int _pendingClip; // 0 none, 1 nonzero, 2 even-odd
    Matrix _tm = Matrix.Identity, _tlm = Matrix.Identity;
    PathData? _textClip;
    readonly List<string> _hiddenStack = [];
    int _hiddenDepth;
    int _type3Depth;
    public List<string> Warnings { get; } = [];

    public ContentInterpreter(PdfFile f, IRenderBackend backend, IFontFallback? fallback = null)
    {
        _f = f; _be = backend; _fallback = fallback ?? new SystemFontFallback();
    }

    public void Render(PdfPage page, Matrix baseMatrix)
    {
        SetBase(baseMatrix);
        _gs = new GState { Ctm = baseMatrix };
        Run(page.GetContents(), page.Resources);
    }

    void Warn(string s) { if (!Warnings.Contains(s)) Warnings.Add(s); }

    PdfObject? R(PdfObject? o) => _f.Resolve(o);

    void Run(byte[] content, PdfDictionary res)
    {
        var lx = new PdfLexer(content);
        var ops = new List<PdfObject>();
        var stackDepth = _stack.Count;
        PdfObject? o;
        while ((o = lx.ReadObject(false)) is not null)
        {
            if (o is not PdfKeyword k) { ops.Add(o); if (ops.Count > 512) ops.RemoveAt(0); continue; }
            if (k.Value == "BI") { InlineImage(lx, res); ops.Clear(); continue; }
            try { Exec(k.Value, ops, res); }
            catch (Exception e) when (e is InvalidCastException or ArgumentOutOfRangeException or IndexOutOfRangeException or NotSupportedException or KeyNotFoundException or InvalidOperationException)
            { Warn($"operator {k.Value}: {e.GetType().Name}"); }
            ops.Clear();
        }
        while (_stack.Count > stackDepth) { _gs = _stack.Pop(); _be.Restore(); }
    }

    static double N(List<PdfObject> a, int i) => i < a.Count ? a[i].AsNumber() ?? 0 : 0;

    void Exec(string op, List<PdfObject> a, PdfDictionary res)
    {
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
            case "ri": case "i": break;
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
                if (_textClip is { IsEmpty: false } tc) _be.Clip(tc, Matrix.Identity, false);
                _textClip = null; break;
            case "Tc": _gs.CharSpace = N(a, 0); break;
            case "Tw": _gs.WordSpace = N(a, 0); break;
            case "Tz": _gs.HScale = N(a, 0) / 100; break;
            case "TL": _gs.Leading = N(a, 0); break;
            case "Tr": _gs.RenderMode = (int)N(a, 0); break;
            case "Ts": _gs.Rise = N(a, 0); break;
            case "Tf":
                if (a.Count >= 2 && a[0] is PdfName fn) { _gs.Font = LoadFont(res, fn.Value); _gs.FontSize = N(a, 1); }
                break;
            case "Td": _tlm = Matrix.Translate(N(a, 0), N(a, 1)).Then(_tlm); _tm = _tlm; break;
            case "TD": _gs.Leading = -N(a, 1); _tlm = Matrix.Translate(N(a, 0), N(a, 1)).Then(_tlm); _tm = _tlm; break;
            case "Tm": _tm = _tlm = new Matrix(N(a, 0), N(a, 1), N(a, 2), N(a, 3), N(a, 4), N(a, 5)); break;
            case "T*": NextLine(); break;
            case "Tj": if (a.Count > 0 && a[0] is PdfString s0) ShowText(s0.Bytes, res); break;
            case "'": NextLine(); if (a.Count > 0 && a[0] is PdfString s1) ShowText(s1.Bytes, res); break;
            case "\"":
                _gs.WordSpace = N(a, 0); _gs.CharSpace = N(a, 1); NextLine();
                if (a.Count > 2 && a[2] is PdfString s2) ShowText(s2.Bytes, res);
                break;
            case "TJ":
                if (a.Count > 0 && a[0] is PdfArray arr)
                    foreach (var item in arr.Items)
                    {
                        if (item is PdfString ts) ShowText(ts.Bytes, res);
                        else if (item.AsNumber() is { } adj)
                        {
                            var d = -adj / 1000 * _gs.FontSize;
                            _tm = (_gs.Font?.Vertical == true ? Matrix.Translate(0, d) : Matrix.Translate(d * _gs.HScale, 0)).Then(_tm);
                        }
                    }
                break;

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
            case "Do": if (a.Count > 0 && a[0] is PdfName xn && R(res.Get("XObject")) is PdfDictionary xd) DoXObject(R(xd.Get(xn.Value)), res); break;

            case "BMC": _hiddenStack.Add(""); break;
            case "BDC":
                {
                    var hidden = false;
                    if (a.Count >= 2 && a[0].AsName() == "OC")
                    {
                        PdfObject? prop = a[1] is PdfName pn && R(res.Get("Properties")) is PdfDictionary pd ? R(pd.Get(pn.Value)) : a[1];
                        hidden = prop is PdfDictionary ocd && IsHidden(ocd);
                    }
                    _hiddenStack.Add(hidden ? "H" : "");
                    if (hidden) _hiddenDepth++;
                    break;
                }
            case "EMC":
                if (_hiddenStack.Count > 0) { if (_hiddenStack[^1] == "H") _hiddenDepth--; _hiddenStack.RemoveAt(_hiddenStack.Count - 1); }
                break;
            case "d0": case "d1": case "MP": case "DP": case "BX": case "EX": break;
            default: Warn($"unknown operator {op}"); break;
        }
    }

    bool IsHidden(PdfDictionary oc)
    {
        var cfg = R(R(_f.Catalog.Get("OCProperties")) is PdfDictionary ocp ? ocp.Get("D") : null) as PdfDictionary;
        var type = R(oc.Get("Type")).AsName();
        if (type == "OCMD")
        {
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

    bool OcgOff(PdfDictionary ocg, PdfDictionary? cfg)
    {
        if (cfg is null) return false;
        var baseOff = R(cfg.Get("BaseState")).AsName() == "OFF";
        bool Contains(string key) => R(cfg.Get(key)) is PdfArray arr && arr.Items.Any(x => ReferenceEquals(R(x), ocg));
        if (Contains("OFF")) return true;
        if (Contains("ON")) return false;
        return baseOff;
    }

    void NextLine() { _tlm = Matrix.Translate(0, -_gs.Leading).Then(_tlm); _tm = _tlm; }

    void ApplyExtGState(PdfDictionary g, PdfDictionary res)
    {
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
                case "BM": _gs.BlendMode = (v is PdfArray ba && ba.Count > 0 ? R(ba[0]).AsName() : v.AsName()) ?? "Normal"; if (_gs.BlendMode == "Compatible") _gs.BlendMode = "Normal"; break;
                case "Font": if (v is PdfArray { Count: 2 } fa && R(fa[0]) is PdfDictionary fd) { _gs.Font = GetFont(fd); _gs.FontSize = R(fa[1]).AsNumber() ?? 0; } break;
                case "SMask": ApplySoftMask(v, res); break;
            }
        }
    }

    void ApplySoftMask(PdfObject? v, PdfDictionary res)
    {
        if (v is not PdfDictionary sm) { _be.SetSoftMask(null, false, default); return; }
        if (R(sm.Get("G")) is not PdfStream g) return;
        var lum = R(sm.Get("S")).AsName() == "Luminosity";
        var bc = RgbColor.Black;
        if (lum && R(sm.Get("BC")) is PdfArray ba)
        {
            var c = ba.Items.Select(x => R(x).AsNumber() ?? 0).ToArray();
            bc = c.Length switch
            {
                1 => new RgbColor(c[0], c[0], c[0]),
                3 => new RgbColor(c[0], c[1], c[2]),
                4 => new RgbColor((1 - c[0]) * (1 - c[3]), (1 - c[1]) * (1 - c[3]), (1 - c[2]) * (1 - c[3])),
                _ => bc
            };
        }
        var ctm = _gs.Ctm;
        _be.SetSoftMask(off =>
        {
            var sb = _be; var saved = _gs;
            _be = off; _gs = new GState { Ctm = ctm };
            try { DoForm(g, res); }
            finally { _be = sb; _gs = saved; }
        }, lum, bc);
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
        return cs.Components == 0 ? RgbColor.Black : cs.ToRgb(c);
    }

    void Paint(bool fill, bool evenOdd, bool stroke)
    {
        var p = _path; _path = new PathData();
        var clip = _pendingClip; _pendingClip = 0;
        if (_hiddenDepth == 0 && !p.IsEmpty)
        {
            if (fill) FillPath(p, evenOdd);
            if (stroke) _be.Stroke(p, _gs.Ctm, Style(), Color(true), _gs.StrokeAlpha, _gs.BlendMode);
        }
        if (clip != 0) _be.Clip(p, _gs.Ctm, clip == 2);
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
        _be.Fill(p, _gs.Ctm, evenOdd, Color(false), _gs.FillAlpha, _gs.BlendMode);
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
        var color = Color(false);
        _be.Save();
        _be.Clip(p, _gs.Ctm, evenOdd);
        var savedGs = _gs; var savedStack = new Stack<GState>(_stack.Reverse()); var savedPath = _path;
        for (var j = j0; j <= j1; j++)
            for (var i = i0; i <= i1; i++)
            {
                _be.Save();
                _gs = new GState { Ctm = Matrix.Translate(i * xs, j * ys).Then(pm) };
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

    void ShowText(byte[] s, PdfDictionary res)
    {
        var font = _gs.Font;
        if (font is null || _hiddenDepth > 0 && false) return;
        var hidden = _hiddenDepth > 0;
        foreach (var run in font.Decode(s))
        {
            var fs = _gs.FontSize;
            var trm = new Matrix(fs * _gs.HScale, 0, 0, fs, 0, _gs.Rise).Then(_tm).Then(_gs.Ctm);
            if (!hidden)
            {
                if (font.IsType3) DrawType3(font, run.Code, trm);
                else if (font.GetGlyph(run.Code) is { } outline)
                {
                    var m = Matrix.Scale(0.001, 0.001).Then(trm);
                    var mode = _gs.RenderMode;
                    if (mode is 0 or 2 or 4 or 6) FillGlyph(outline, m);
                    if (mode is 1 or 2 or 5 or 6)
                    {
                        // Stroke width is in user space, so stroke with the text-space transform folded into the path.
                        var up = outline.Transform(Matrix.Scale(0.001, 0.001).Then(new Matrix(fs * _gs.HScale, 0, 0, fs, 0, _gs.Rise).Then(_tm)));
                        _be.Stroke(up, _gs.Ctm, Style(), Color(true), _gs.StrokeAlpha, _gs.BlendMode);
                    }
                    if (mode >= 4) { _textClip ??= new PathData(); _textClip.Append(outline.Transform(m)); }
                }
            }
            double tx;
            if (font.Vertical) { var ty = -run.Width * 0 - 1.0 * fs + _gs.CharSpace + (font.IsSpace(run) ? _gs.WordSpace : 0); _tm = Matrix.Translate(0, ty).Then(_tm); continue; }
            tx = (run.Width * fs + _gs.CharSpace + (font.IsSpace(run) ? _gs.WordSpace : 0)) * _gs.HScale;
            _tm = Matrix.Translate(tx, 0).Then(_tm);
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
        _be.Fill(outline, m, false, Color(false), _gs.FillAlpha, _gs.BlendMode);
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

    void DoXObject(PdfObject? obj, PdfDictionary res)
    {
        if (obj is not PdfStream xs || _hiddenDepth > 0) return;
        if (R(xs.Dict.Get("OC")) is PdfDictionary oc && IsHidden(oc)) return;
        var sub = R(xs.Dict.Get("Subtype")).AsName();
        if (sub == "Image") DrawImage(xs, res);
        else if (sub == "Form") DoForm(xs, res);
    }

    void DoForm(PdfStream xs, PdfDictionary parentRes)
    {
        if (!_activeForms.Add(xs.Dict)) return;
        var saved = _gs; var savedPath = _path; var savedTm = _tm; var savedTlm = _tlm;
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
        Run(_f.DecodeStream(xs), res);
        _gs = _stack.Pop();
        _be.Restore();
        _gs = saved; _path = savedPath; _tm = savedTm; _tlm = savedTlm;
        _activeForms.Remove(xs.Dict);
    }

    // ---- Shadings ----
    void Shade(PdfObject? shObj, PdfDictionary res, Matrix? shadingToDevice)
    {
        var d = shObj as PdfDictionary ?? (shObj as PdfStream)?.Dict;
        if (d is null) return;
        var m = shadingToDevice ?? _gs.Ctm;
        var inv = m.Invert();
        var type = (int)(R(d.Get("ShadingType")).AsNumber() ?? 0);
        var cs = PdfColorSpace.Parse(_f, d.Get("ColorSpace"), res);
        PdfFunction[]? fns = null;
        var fo = R(d.Get("Function"));
        if (fo is PdfArray fa) fns = fa.Items.Select(x => PdfFunction.Parse(_f, x)).Where(x => x is not null).Select(x => x!).ToArray();
        else if (fo is not null && PdfFunction.Parse(_f, fo) is { } one) fns = [one];

        RgbColor EvalT(double t)
        {
            if (fns is null) return RgbColor.Black;
            double[] comps = fns.Length == 1 ? fns[0].Evaluate([t]) : fns.Select(fn => fn.Evaluate([t])[0]).ToArray();
            if (comps.Length < cs.Components) comps = [.. comps, .. new double[cs.Components - comps.Length]];
            return cs.ToRgb(comps);
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
                _be.PaintShader((px, py) =>
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
                _be.PaintShader((px, py) =>
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
            _be.PaintShader((px, py) =>
            {
                var (ux, uy) = inv.Apply(px, py);
                var (sx, sy) = minv.Apply(ux, uy);
                if (sx < dom[0] || sx > dom[1] || sy < dom[2] || sy > dom[3] || fns is null) return null;
                double[] comps = fns.Length == 1 ? fns[0].Evaluate([sx, sy]) : fns.Select(fn => fn.Evaluate([sx, sy])[0]).ToArray();
                if (comps.Length < cs.Components) comps = [.. comps, .. new double[cs.Components - comps.Length]];
                return cs.ToRgb(comps);
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
                ? cs.ToRgb(comps.Length < cs.Components ? [.. comps, .. new double[cs.Components - comps.Length]] : comps)
                : EvalT(comps[0]));
            _be.PaintShader(shader, _gs.FillAlpha, _gs.BlendMode);
        }
        else Warn($"shading type {type} not rendered");
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
        var img = ImageDecoder.Decode(_f, xs, res, out var isMask, out var warn);
        if (warn is not null) Warn(warn);
        if (img is null) return;
        _be.DrawImage(img, _gs.Ctm, _gs.FillAlpha, isMask ? Color(false) : RgbColor.Black, _gs.BlendMode);
    }
}
