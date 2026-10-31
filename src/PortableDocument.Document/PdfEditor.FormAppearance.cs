using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

public sealed partial class PdfEditor
{
    PdfObject? TryFormFont(string name)
    {
        if (AcroFormDict is { } form && Res(form.Get("DR")) is PdfDictionary dr && Res(dr.Get("Font")) is PdfDictionary fonts && fonts.Get(name) is { } f) return f;
        return StandardFormFonts.ContainsKey(name) ? FormFont(name, register: false) : null;
    }

    static string Circle(double cx, double cy, double r)
    {
        const double k = 0.5523;
        return $"{Num(cx + r)} {Num(cy)} m {Num(cx + r)} {Num(cy + k * r)} {Num(cx + k * r)} {Num(cy + r)} {Num(cx)} {Num(cy + r)} c " +
               $"{Num(cx - k * r)} {Num(cy + r)} {Num(cx - r)} {Num(cy + k * r)} {Num(cx - r)} {Num(cy)} c " +
               $"{Num(cx - r)} {Num(cy - k * r)} {Num(cx - k * r)} {Num(cy - r)} {Num(cx)} {Num(cy - r)} c " +
               $"{Num(cx + k * r)} {Num(cy - r)} {Num(cx + r)} {Num(cy - k * r)} {Num(cx + r)} {Num(cy)} c h ";
    }

    static string Decor(double w, double h, double[] bg, double[] bc, double bw, string style, bool round)
    {
        var sb = new StringBuilder();
        if (bg.Length > 0)
            sb.Append(round ? $"q {ColorOps(bg, false)}{Circle(w / 2, h / 2, Math.Min(w, h) / 2)}f Q\n" : $"q {ColorOps(bg, false)}0 0 {Num(w)} {Num(h)} re f Q\n");
        if (bw <= 0) return sb.ToString();
        var col = bc.Length > 0 ? bc : [0.0];
        if (round)
        {
            sb.Append($"q {ColorOps(col, true)}{Num(bw)} w {Circle(w / 2, h / 2, Math.Min(w, h) / 2 - bw / 2)}S Q\n");
            return sb.ToString();
        }
        if (bc.Length == 0) return sb.ToString();
        if (style == "U") sb.Append($"q {ColorOps(col, true)}{Num(bw)} w 0 {Num(bw / 2)} m {Num(w)} {Num(bw / 2)} l S Q\n");
        else
        {
            sb.Append($"q {ColorOps(col, true)}{Num(bw)} w {(style == "D" ? "[3] 0 d " : "")}{Num(bw / 2)} {Num(bw / 2)} {Num(w - bw)} {Num(h - bw)} re S Q\n");
            if (style is "B" or "I")
            {
                var lo = style == "B" ? "1 g" : "0.5 g"; var hi = style == "B" ? "0.5 g" : "0.75 g";
                sb.Append($"q {lo} {Num(bw)} {Num(bw)} m {Num(bw)} {Num(h - bw)} l {Num(w - bw)} {Num(h - bw)} l {Num(w - 2 * bw)} {Num(h - 2 * bw)} l {Num(2 * bw)} {Num(h - 2 * bw)} l {Num(2 * bw)} {Num(2 * bw)} l f Q\n");
                sb.Append($"q {hi} {Num(w - bw)} {Num(h - bw)} m {Num(w - bw)} {Num(bw)} l {Num(bw)} {Num(bw)} l {Num(2 * bw)} {Num(2 * bw)} l {Num(w - 2 * bw)} {Num(2 * bw)} l {Num(w - 2 * bw)} {Num(h - 2 * bw)} l f Q\n");
            }
        }
        return sb.ToString();
    }

    PdfRef MakeAppearanceForm(string content, double w, double h, string? fontName, PdfObject? fontObj)
    {
        var body = Encoding.Latin1.GetBytes(content);
        var res = new Dictionary<string, PdfObject>();
        if (fontName is not null && fontObj is not null) res["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { [fontName] = fontObj });
        return AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Form"),
            ["BBox"] = new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfReal(w), new PdfReal(h)]),
            ["Resources"] = new PdfDictionary(res), ["Length"] = new PdfInteger(body.Length),
        }), body));
    }

    static List<string> WrapLines(string text, string font, double size, double inner)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var cur = "";
            foreach (var word in para.Split(' '))
            {
                var cand = cur.Length == 0 ? word : cur + " " + word;
                if (TextWidth(cand, font, size) <= inner) { cur = cand; continue; }
                if (cur.Length > 0) { lines.Add(cur); cur = ""; }
                cur = word;
                while (cur.Length > 1 && TextWidth(cur, font, size) > inner)
                {
                    var take = 1;
                    while (take < cur.Length && TextWidth(cur[..(take + 1)], font, size) <= inner) take++;
                    lines.Add(cur[..take]); cur = cur[take..];
                }
            }
            lines.Add(cur);
        }
        return lines;
    }

    string OptionDisplay(PdfDictionary w, string value)
    {
        if (Inherited(w, "Opt") is PdfArray opt)
            foreach (var o in opt.Items)
                if (Res(o) is PdfArray { Count: >= 2 } pair && Res(pair[0]) is PdfString ex && ex.Text == value && Res(pair[1]) is PdfString disp) return disp.Text;
        return value;
    }

    static string ValueText(PdfObject? v) => v switch { PdfString s => s.Text, PdfName n => n.Value, _ => "" };

    /// <summary>Generates /AP (/N, plus /D for buttons) and /AS for one widget from its inherited field attributes. Returns false when the widget cannot be drawn (no rectangle or unknown font).</summary>
    bool RebuildAppearance(PdfRef widgetRef, string? onState = null)
    {
        if (!_objs.TryGetValue(widgetRef.Number, out var wo) || wo is not PdfDictionary w || Res(w.Get("Rect")) is not PdfArray ra || ra.Count < 4) return false;
        var ft = (Inherited(w, "FT") as PdfName)?.Value;
        if (ft is null) return false;
        var ff = (long)(Inherited(w, "Ff").AsNumber() ?? 0);
        double R(int i) => Res(ra[i]).AsNumber() ?? 0;
        double width = Math.Abs(R(2) - R(0)), height = Math.Abs(R(3) - R(1));
        if (width < 1 || height < 1) return false;
        var daText = Inherited(w, "DA") is PdfString ds ? Encoding.Latin1.GetString(ds.Bytes)
            : AcroFormDict is { } af && Res(af.Get("DA")) is PdfString fds ? Encoding.Latin1.GetString(fds.Bytes) : "/Helv 0 Tf 0 g";
        var da = ParseDa(daText);
        var mk = Res(w.Get("MK")) as PdfDictionary;
        var bg = ColorOf(Res(mk?.Get("BG"))); var bc = ColorOf(Res(mk?.Get("BC")));
        var bs = Res(w.Get("BS")) as PdfDictionary;
        var bw = bs is not null && Res(bs.Get("W")).AsNumber() is { } bwv ? bwv : 1;
        var style = bs is not null && Res(bs.Get("S")).AsName() is { Length: 1 } sv && "SDBIU".Contains(sv) ? sv : "S";
        var q = (int)(Inherited(w, "Q").AsNumber() ?? 0);
        var isBtn = ft == "Btn";
        var isPush = isBtn && (ff & FfPushButton) != 0;
        var isRadio = isBtn && (ff & FfRadio) != 0;
        var fontObj = TryFormFont(da.Font);
        var colorOps = da.Color.Length > 0 ? ColorOps(da.Color, false) : "0 g ";
        var pad = Math.Max(2, bw + 1) + (style is "B" or "I" ? bw : 0);
        PdfObject ap;
        string? asState = null;

        if (ft == "Sig")
        {
            ap = new PdfDictionary(new Dictionary<string, PdfObject> { ["N"] = MakeAppearanceForm(Decor(width, height, bg, bc, bw, style, false), width, height, null, null) });
        }
        else if (isBtn && !isPush)
        {
            var on = onState ?? ExistingOnState(w) ?? "Yes";
            var side = Math.Min(width, height);
            string Mark()
            {
                var strokeCol = ColorOps(da.Color.Length > 0 ? da.Color : [0.0], true);
                if (isRadio) return $"q {colorOps}{Circle(width / 2, height / 2, side * 0.25)}f Q\n";
                var lw = Math.Max(1, side * 0.14);
                return $"q {strokeCol}{Num(lw)} w 1 J 1 j {Num(width * 0.22)} {Num(height * 0.52)} m {Num(width * 0.42)} {Num(height * 0.28)} l {Num(width * 0.78)} {Num(height * 0.74)} l S Q\n";
            }
            var offN = MakeAppearanceForm(Decor(width, height, bg, bc, bw, style, isRadio), width, height, null, null);
            var onN = MakeAppearanceForm(Decor(width, height, bg, bc, bw, style, isRadio) + Mark(), width, height, null, null);
            double[] down = [0.75];
            var offD = MakeAppearanceForm(Decor(width, height, down, bc, bw, style, isRadio), width, height, null, null);
            var onD = MakeAppearanceForm(Decor(width, height, down, bc, bw, style, isRadio) + Mark(), width, height, null, null);
            ap = new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["N"] = new PdfDictionary(new Dictionary<string, PdfObject> { [on] = onN, ["Off"] = offN }),
                ["D"] = new PdfDictionary(new Dictionary<string, PdfObject> { [on] = onD, ["Off"] = offD }),
            });
            asState = ValueText(Inherited(w, "V")) == on ? on : "Off";
        }
        else
        {
            if (fontObj is null) return false;
            var sb = new StringBuilder(Decor(width, height, bg, bc, bw, style, false));
            var inner = Math.Max(1, width - 2 * pad);
            double Fit(double size, string text) => text.Length > 0 && TextWidth(text, da.Font, size) is var tw && tw > inner ? Math.Max(4, size * inner / tw) : size;
            string? downContent = null;
            if (isPush)
            {
                var cap = Res(mk?.Get("CA")) is PdfString cs ? cs.Text : "";
                var size = Fit(da.Size > 0 ? da.Size : Math.Clamp(height - 2 * pad, 4, 14), cap);
                var tw = TextWidth(cap, da.Font, size);
                var body = cap.Length == 0 ? "" : $"q BT /{da.Font} {Num(size)} Tf {colorOps}1 0 0 1 {Num(Math.Max(pad, (width - tw) / 2))} {Num((height - size) / 2 + size * 0.22)} Tm ({PdfEsc(cap)}) Tj ET Q\n";
                sb.Append(body);
                downContent = Decor(width, height, [0.75], bc, bw, style is "B" ? "I" : style, false) + body;
            }
            else
            {
                var isChoice = ft == "Ch";
                var isList = isChoice && (ff & FfCombo) == 0;
                var multiline = !isChoice && (ff & FfMultiline) != 0;
                var comb = !isChoice && (ff & FfComb) != 0 && (Inherited(w, "MaxLen").AsNumber() ?? 0) > 0;
                var password = !isChoice && (ff & FfPassword) != 0;
                var raw = Inherited(w, "V") is PdfArray va && va.Count > 0 ? ValueText(Res(va[0])) : ValueText(Inherited(w, "V"));
                var text = isChoice ? OptionDisplay(w, raw) : raw;
                if (password) text = new string('*', text.Length);
                sb.Append($"/Tx BMC q {Num(pad)} {Num(pad)} {Num(inner)} {Num(Math.Max(1, height - 2 * pad))} re W n\n");
                if (isList) AppendListBox(sb, w, da, colorOps, width, height, bw, pad);
                else if (comb)
                {
                    var max = (int)Inherited(w, "MaxLen").AsNumber()!;
                    var cell = width / max;
                    var shown = text.Length > max ? text[..max] : text;
                    var size = da.Size > 0 ? da.Size : Math.Clamp(height - 2 * pad, 4, 12);
                    var start = q == 1 ? (max - shown.Length) / 2 : q == 2 ? max - shown.Length : 0;
                    if (bc.Length > 0 && bw > 0)
                        for (var i = 1; i < max; i++) sb.Append($"q {ColorOps(bc, true)}{Num(bw / 2)} w {Num(i * cell)} {Num(bw)} m {Num(i * cell)} {Num(height - bw)} l S Q\n");
                    sb.Append($"BT /{da.Font} {Num(size)} Tf {colorOps}\n");
                    for (var i = 0; i < shown.Length; i++)
                        sb.Append($"1 0 0 1 {Num((start + i) * cell + (cell - TextWidth(shown[i].ToString(), da.Font, size)) / 2)} {Num((height - size) / 2 + size * 0.22)} Tm ({PdfEsc(shown[i].ToString())}) Tj\n");
                    sb.Append("ET\n");
                }
                else
                {
                    var size = da.Size > 0 ? da.Size : multiline ? 12 : Fit(Math.Clamp(height - 2 * pad, 4, 12), text);
                    var lines = multiline ? WrapLines(text, da.Font, size, inner) : [text.Replace("\r", " ").Replace("\n", " ")];
                    var y = multiline ? height - pad - size * 0.92 : (height - size) / 2 + size * 0.22;
                    sb.Append($"BT /{da.Font} {Num(size)} Tf {colorOps}\n");
                    foreach (var ln in lines)
                    {
                        var tw = TextWidth(ln, da.Font, size);
                        var x = q == 1 ? Math.Max(pad, (width - tw) / 2) : q == 2 ? Math.Max(pad, width - pad - tw) : pad;
                        if (ln.Length > 0) sb.Append($"1 0 0 1 {Num(x)} {Num(y)} Tm ({PdfEsc(ln)}) Tj\n");
                        y -= size * 1.15;
                    }
                    sb.Append("ET\n");
                }
                sb.Append("Q EMC");
            }
            var n = MakeAppearanceForm(sb.ToString(), width, height, da.Font, fontObj);
            var apd = new Dictionary<string, PdfObject> { ["N"] = n };
            if (downContent is not null) apd["D"] = MakeAppearanceForm(downContent, width, height, da.Font, fontObj);
            ap = new PdfDictionary(apd);
        }
        SetEntries(widgetRef.Number, e => { e["AP"] = ap; if (asState is not null) e["AS"] = new PdfName(asState); });
        return true;
    }

    void AppendListBox(StringBuilder sb, PdfDictionary w, DaInfo da, string colorOps, double width, double height, double bw, double pad)
    {
        if (Inherited(w, "Opt") is not PdfArray opt) return;
        var size = da.Size > 0 ? da.Size : 12;
        var line = size * 1.15;
        var selected = new HashSet<string>();
        if (Inherited(w, "V") is PdfString vs) selected.Add(vs.Text);
        else if (Inherited(w, "V") is PdfArray va) foreach (var x in va.Items) if (Res(x) is PdfString s) selected.Add(s.Text);
        var top = (int)(Res(w.Get("TI")).AsNumber() ?? 0);
        var y = height - pad;
        for (var i = Math.Max(0, top); i < opt.Count && y > 0; i++)
        {
            string export, display;
            switch (Res(opt[i]))
            {
                case PdfString s: export = display = s.Text; break;
                case PdfArray { Count: >= 2 } p when Res(p[0]) is PdfString a && Res(p[1]) is PdfString b: export = a.Text; display = b.Text; break;
                default: continue;
            }
            if (selected.Contains(export)) sb.Append($"q 0.6 0.75 0.85 rg {Num(bw)} {Num(y - line)} {Num(width - 2 * bw)} {Num(line)} re f Q\n");
            sb.Append($"BT /{da.Font} {Num(size)} Tf {colorOps}1 0 0 1 {Num(pad)} {Num(y - line + (line - size) / 2 + size * 0.22)} Tm ({PdfEsc(display)}) Tj ET\n");
            y -= line;
        }
    }
}
