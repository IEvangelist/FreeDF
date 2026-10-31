using System.Text;
using System.Xml;
using System.Xml.Linq;
using PortableDocument.Core;

namespace PortableDocument.Document;

public sealed partial class PdfEditor
{
    const int MaxFormDataBytes = 16 * 1024 * 1024;
    static readonly XNamespace XfdfNs = "http://ns.adobe.com/xfdf/";

    // ---- Flatten ----

    /// <summary>
    /// Bakes widget appearances into page content (12.5.5 appearance-to-rectangle mapping) and removes the flattened widgets and fields; /AcroForm is removed once no field remains.
    /// Signed signature fields are never flattened. Hidden and no-view widgets are removed without being drawn.
    /// </summary>
    public PdfFlattenResult FlattenForm(PdfFlattenOptions? options = null)
    {
        Require(PdfPermissions.Modify);
        options ??= new PdfFlattenOptions();
        var selected = options.Names is null ? null : new HashSet<string>(options.Names, StringComparer.Ordinal);
        var nodes = FormNodes().Where(n => n.Terminal).ToList();
        var pages = WidgetPages();
        var needAppearances = AcroFormDict is { } f0 && Res(f0.Get("NeedAppearances")) is PdfBoolean { Value: true };
        var flatten = new List<FormNode>();
        foreach (var n in nodes)
        {
            var ff = (long)(Inherited(n.Dict, "Ff").AsNumber() ?? 0);
            var ft = (Inherited(n.Dict, "FT") as PdfName)?.Value;
            if (ft == "Sig" && Inherited(n.Dict, "V") is not null and not PdfNull) continue;
            var inScope = options.Scope switch
            {
                PdfFlattenScope.All => true,
                PdfFlattenScope.Selected => selected?.Contains(n.FullName) == true,
                _ => (ff & FfReadOnly) != 0,
            };
            if (inScope) flatten.Add(n);
        }

        var perPage = new Dictionary<int, (StringBuilder Ops, Dictionary<string, PdfObject> XObjects)>();
        var counter = 0;
        foreach (var n in flatten)
            foreach (var wr in WidgetRefs(n))
            {
                if (!pages.TryGetValue(wr.Number, out var pi)) continue;
                var w0 = (PdfDictionary)_objs[wr.Number];
                if ((needAppearances || w0.Get("AP") is null) && Inherited(w0, "FT") is PdfName { Value: not "Sig" }) RebuildAppearance(wr);
                var w = (PdfDictionary)_objs[wr.Number];
                var flags = (int)(Res(w.Get("F")).AsNumber() ?? 0);
                if ((flags & (2 | 32)) != 0) continue;
                if (ResolveAppearanceStream(w) is not { } stream || Res(w.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
                var (dict, _) = stream;
                if (Res(dict.Dict.Get("BBox")) is not PdfArray bb || bb.Count < 4) continue;
                double B(int i) => Res(bb[i]).AsNumber() ?? 0;
                double[] m = Res(dict.Dict.Get("Matrix")) is PdfArray ma && ma.Count >= 6 ? ma.Items.Select(x => Res(x).AsNumber() ?? 0).Take(6).ToArray() : [1, 0, 0, 1, 0, 0];
                var xs = new List<double>(); var ys = new List<double>();
                foreach (var (x, y) in new[] { (B(0), B(1)), (B(2), B(1)), (B(2), B(3)), (B(0), B(3)) })
                { xs.Add(m[0] * x + m[2] * y + m[4]); ys.Add(m[1] * x + m[3] * y + m[5]); }
                double tx0 = xs.Min(), tx1 = xs.Max(), ty0 = ys.Min(), ty1 = ys.Max();
                double R(int i) => Res(ra[i]).AsNumber() ?? 0;
                double rl = Math.Min(R(0), R(2)), rb = Math.Min(R(1), R(3)), rw = Math.Abs(R(2) - R(0)), rh = Math.Abs(R(3) - R(1));
                if (tx1 - tx0 <= 0 || ty1 - ty0 <= 0 || rw <= 0 || rh <= 0) continue;
                double sx = rw / (tx1 - tx0), sy = rh / (ty1 - ty0);
                var page = (PdfDictionary)_objs[_pages[pi]];
                var existingNames = Res(Res(page.Get("Resources")) is PdfDictionary rr ? rr.Get("XObject") : null) as PdfDictionary;
                string name;
                do { name = "FmFlat" + counter++; } while (existingNames?.Get(name) is not null);
                if (!perPage.TryGetValue(pi, out var slot)) perPage[pi] = slot = (new StringBuilder(), []);
                slot.Ops.Append($"q {Num(sx)} 0 0 {Num(sy)} {Num(rl - tx0 * sx)} {Num(rb - ty0 * sy)} cm /{name} Do Q\n");
                slot.XObjects[name] = ResolveAppearanceRef(w)!;
            }

        foreach (var (pi, slot) in perPage) AppendPageContent(pi, Encoding.Latin1.GetBytes(slot.Ops.ToString()), slot.XObjects);
        foreach (var n in flatten) RemoveFormField(n.FullName);
        var kept = FormNodes().Count(n => n.Terminal);
        if (kept == 0 && AcroFormDict is not null)
        {
            var cat = new Dictionary<string, PdfObject>(((PdfDictionary)_objs[_catalog]).Entries);
            cat.Remove("AcroForm");
            _objs[_catalog] = new PdfDictionary(cat);
        }
        return new PdfFlattenResult(flatten.Count, kept);
    }

    PdfObject? ResolveAppearanceRef(PdfDictionary w)
    {
        if (Res(w.Get("AP")) is not PdfDictionary ap || ap.Get("N") is not { } n) return null;
        if (Res(n) is PdfStream) return n;
        if (Res(n) is PdfDictionary states)
        {
            var state = Res(w.Get("AS")).AsName();
            if (state is not null && states.Get(state) is { } s && Res(s) is PdfStream) return s;
        }
        return null;
    }

    (PdfStream Stream, PdfObject Ref)? ResolveAppearanceStream(PdfDictionary w) =>
        ResolveAppearanceRef(w) is { } r && Res(r) is PdfStream st ? (st, r) : null;

    void AppendPageContent(int pageIndex, byte[] content, Dictionary<string, PdfObject> xobjects)
    {
        var n = _pages[pageIndex];
        var page = (PdfDictionary)_objs[n];
        var res = Res(page.Get("Resources")) is PdfDictionary rd ? new Dictionary<string, PdfObject>(rd.Entries) : [];
        var xo = Res(res.GetValueOrDefault("XObject")) is PdfDictionary xd ? new Dictionary<string, PdfObject>(xd.Entries) : [];
        foreach (var (k, v) in xobjects) xo[k] = v;
        res["XObject"] = new PdfDictionary(xo);
        var e = new Dictionary<string, PdfObject>(page.Entries) { ["Resources"] = new PdfDictionary(res) };
        // A pending content override replaces /Contents at save time, so it must carry the baked widgets as well.
        if (_contentOverrides.TryGetValue(n, out var ov))
            _contentOverrides[n] = [.. "q\n"u8, .. ov, .. "\nQ\n"u8, .. content];
        {
            PdfRef Stream(byte[] b) => AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject> { ["Length"] = new PdfInteger(b.Length) }), b));
            var parts = new List<PdfObject> { Stream("q\n"u8.ToArray()) };
            switch (Res(page.Get("Contents")))
            {
                case PdfArray a: parts.AddRange(a.Items); break;
                case PdfStream: parts.Add(page.Get("Contents")!); break;
            }
            parts.Add(Stream([.. "\nQ\n"u8, .. content]));
            e["Contents"] = new PdfArray(parts);
        }
        _objs[n] = new PdfDictionary(e);
    }

    // ---- Export / import (inert FDF and XFDF) ----

    sealed class FormValueNode(string name) { public string Name = name; public List<string>? Values; public bool IsName; public List<FormValueNode> Children = []; }

    List<FormValueNode> CollectFormValues()
    {
        var roots = new List<FormValueNode>();
        foreach (var n in FormNodes().Where(n => n.Terminal))
        {
            var ft = (Inherited(n.Dict, "FT") as PdfName)?.Value;
            var ff = (long)(Inherited(n.Dict, "Ff").AsNumber() ?? 0);
            if (ft is not ("Tx" or "Ch" or "Btn") || (ft == "Btn" && (ff & FfPushButton) != 0)) continue;
            var values = Inherited(n.Dict, "V") switch
            {
                PdfString s => [s.Text],
                PdfName nm => [nm.Value],
                PdfArray a => a.Items.Select(x => ValueText(Res(x))).ToList(),
                _ => null,
            };
            if (values is null) continue;
            var list = roots;
            FormValueNode? cur = null;
            foreach (var part in n.FullName.Split('.'))
            {
                cur = list.FirstOrDefault(x => x.Name == part);
                if (cur is null) list.Add(cur = new FormValueNode(part));
                list = cur.Children;
            }
            if (cur is not null) { cur.Values = values; cur.IsName = Inherited(n.Dict, "V") is PdfName; }
        }
        return roots;
    }

    /// <summary>Exports current field values as XFDF (XML) or FDF text. Only names and values are written; no actions or scripts.</summary>
    public string ExportFormData(PdfFormDataFormat format)
    {
        var roots = CollectFormValues();
        if (format == PdfFormDataFormat.Xfdf)
        {
            XElement Field(FormValueNode n)
            {
                var el = new XElement(XfdfNs + "field", new XAttribute("name", n.Name));
                if (n.Values is not null) foreach (var v in n.Values) el.Add(new XElement(XfdfNs + "value", SanitizeXml(v)));
                foreach (var c in n.Children) el.Add(Field(c));
                return el;
            }
            var root = new XElement(XfdfNs + "xfdf", new XAttribute(XNamespace.Xml + "space", "preserve"), new XElement(XfdfNs + "fields", roots.Select(Field)));
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + root.ToString();
        }
        static string Hex(string s) => "<" + Convert.ToHexString(EncodeText(s)) + ">";
        static string NameToken(string s)
        {
            var sb = new StringBuilder("/");
            foreach (var b in Encoding.UTF8.GetBytes(s))
                if (b is > 32 and < 127 && b is not ((byte)'#' or (byte)'/' or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'%')) sb.Append((char)b);
                else sb.Append('#').Append(b.ToString("X2"));
            return sb.ToString();
        }
        string Entry(FormValueNode n)
        {
            var sb = new StringBuilder("<< /T " + Hex(n.Name));
            if (n.Values is { Count: 1 } one) sb.Append(" /V ").Append(n.IsName ? NameToken(one[0]) : Hex(one[0]));
            else if (n.Values is { Count: > 1 } many) sb.Append(" /V [").Append(string.Join(' ', many.Select(Hex))).Append(']');
            if (n.Children.Count > 0) sb.Append(" /Kids [").Append(string.Join(' ', n.Children.Select(Entry))).Append(']');
            return sb.Append(" >>").ToString();
        }
        return "%FDF-1.2\n1 0 obj\n<< /FDF << /Fields [" + string.Join(' ', roots.Select(Entry)) + "] >> >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";
    }

    static string SanitizeXml(string s) => new(s.Where(c => c is '\t' or '\n' or '\r' || (c >= ' ' && !char.IsSurrogate(c) && c is not ('\uFFFE' or '\uFFFF'))).ToArray());

    /// <summary>
    /// Imports XFDF or FDF values (auto-detected). Unknown fields, push buttons, signatures, over-long text and values that are not valid for the field
    /// are skipped and reported; /AA, JavaScript and every other entry is ignored.
    /// </summary>
    public PdfFormImportResult ImportFormData(byte[] data)
    {
        Require(PdfPermissions.FillForms);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaxFormDataBytes) throw new ArgumentException("The form data file is too large.");
        var entries = new List<(string Name, List<string> Values)>();
        var head = Encoding.Latin1.GetString(data, 0, Math.Min(data.Length, 4096));
        if (head.Contains("%FDF", StringComparison.Ordinal)) ReadFdf(data, entries);
        else ReadXfdf(data, entries);

        var skipped = new List<string>();
        var applied = 0;
        var nodes = FormNodes().Where(n => n.Terminal).ToList();
        foreach (var (name, values) in entries)
        {
            var node = nodes.FirstOrDefault(n => n.FullName == name);
            if (node is null) { skipped.Add($"{name}: no such field"); continue; }
            var reason = ApplyImportedValue(node, values);
            if (reason is null) applied++; else skipped.Add($"{name}: {reason}");
        }
        return new PdfFormImportResult(applied, skipped);
    }

    string? ApplyImportedValue(FormNode node, List<string> values)
    {
        var ft = (Inherited(node.Dict, "FT") as PdfName)?.Value;
        var ff = (long)(Inherited(node.Dict, "Ff").AsNumber() ?? 0);
        var widgets = WidgetRefs(node);
        var first = values.Count > 0 ? values[0] : "";
        switch (ft)
        {
            case "Tx":
                if (values.Count > 1) return "text fields take one value";
                if ((int)(Inherited(node.Dict, "MaxLen").AsNumber() ?? 0) is > 0 and var max && first.Length > max) return "value exceeds the maximum length";
                SetEntries(node.Ref.Number, e => { if (first.Length == 0) e.Remove("V"); else e["V"] = Text(first); });
                break;
            case "Ch":
                var exports = Inherited(node.Dict, "Opt") is PdfArray opt
                    ? opt.Items.Select(o => Res(o) switch { PdfString s => s.Text, PdfArray { Count: >= 1 } p => ValueText(Res(p[0])), _ => "" }).ToList() : [];
                var multi = (ff & FfMultiSelect) != 0;
                var editable = (ff & FfCombo) != 0 && (ff & FfEdit) != 0;
                if (values.Count > 1 && !multi) return "this choice field takes one value";
                if (!editable && values.Any(v => v.Length > 0 && !exports.Contains(v))) return "value is not one of the options";
                var cleaned = values.Where(v => v.Length > 0).ToList();
                SetEntries(node.Ref.Number, e =>
                {
                    if (cleaned.Count == 0) { e.Remove("V"); e.Remove("I"); }
                    else if (cleaned.Count == 1 && !multi) e["V"] = Text(cleaned[0]);
                    else e["V"] = new PdfArray(cleaned.Select(x => (PdfObject)Text(x)).ToList());
                    if ((ff & FfCombo) == 0)
                    {
                        if (cleaned.Count == 0) e.Remove("I");
                        else e["I"] = new PdfArray(exports.Select((x, i) => (x, i)).Where(t => cleaned.Contains(t.x)).Select(t => (PdfObject)new PdfInteger(t.i)).ToList());
                    }
                });
                break;
            case "Btn":
                if ((ff & FfPushButton) != 0) return "push buttons have no value";
                if (values.Count > 1) return "buttons take one value";
                var states = widgets.Select(x => Res(x)).OfType<PdfDictionary>().Select(ExistingOnState).Where(s => s is not null).Cast<string>().ToHashSet();
                if (states.Count == 0) states.Add("Yes");
                var on = first.Length > 0 && first != "Off";
                if (on && !states.Contains(first)) return "value is not a valid state of this button";
                SetEntries(node.Ref.Number, e => e["V"] = new PdfName(on ? first : "Off"));
                foreach (var wr in widgets)
                    if (Res(wr) is PdfDictionary wd) SetEntries(wr.Number, e => e["AS"] = new PdfName(on && ExistingOnState(wd) == first ? first : "Off"));
                return null;
            default:
                return "unsupported field type";
        }
        foreach (var wr in widgets) RebuildAppearance(wr);
        return null;
    }

    static void ReadXfdf(byte[] data, List<(string, List<string>)> entries)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = MaxFormDataBytes };
            using var reader = XmlReader.Create(new MemoryStream(data), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException e) { throw new ArgumentException("The XFDF file is not valid XML: " + e.Message, e); }
        if (doc.Root is not { } root || root.Name.LocalName != "xfdf") throw new ArgumentException("This is not an XFDF file.");
        void Walk(XElement field, string prefix, int depth)
        {
            if (depth > 32 || field.Attribute("name")?.Value is not { Length: > 0 } name) return;
            var full = prefix.Length == 0 ? name : prefix + "." + name;
            var values = field.Elements().Where(x => x.Name.LocalName == "value").Select(x => x.Value).ToList();
            if (values.Count > 0) entries.Add((full, values));
            foreach (var c in field.Elements().Where(x => x.Name.LocalName == "field")) Walk(c, full, depth + 1);
        }
        foreach (var fields in root.Elements().Where(x => x.Name.LocalName == "fields"))
            foreach (var f in fields.Elements().Where(x => x.Name.LocalName == "field")) Walk(f, "", 0);
    }

    static void ReadFdf(byte[] data, List<(string, List<string>)> entries)
    {
        var text = Encoding.Latin1.GetString(data);
        var at = text.IndexOf("/Fields", StringComparison.Ordinal);
        if (at < 0) throw new ArgumentException("This FDF file has no /Fields array.");
        PdfObject? arr;
        try { arr = new PdfLexer(data, at + 7).ReadObject(false); }
        catch (PdfParseException e) { throw new ArgumentException("The FDF file could not be parsed: " + e.Message, e); }
        if (arr is not PdfArray fields) throw new ArgumentException("This FDF file has no /Fields array.");
        static string? Str(PdfObject? o) => o switch { PdfString s => s.Text, PdfName n => n.Value, _ => null };
        void Walk(PdfObject? o, string prefix, int depth)
        {
            if (depth > 32 || o is not PdfDictionary d || Str(d.Get("T")) is not { Length: > 0 } t) return;
            var full = prefix.Length == 0 ? t : prefix + "." + t;
            var values = d.Get("V") switch { PdfArray a => a.Items.Select(Str).OfType<string>().ToList(), { } v when Str(v) is { } s => new List<string> { s }, _ => new List<string>() };
            if (values.Count > 0) entries.Add((full, values));
            if (d.Get("Kids") is PdfArray kids) foreach (var k in kids.Items) Walk(k, full, depth + 1);
        }
        foreach (var f in fields.Items) Walk(f, "", 0);
    }
}
