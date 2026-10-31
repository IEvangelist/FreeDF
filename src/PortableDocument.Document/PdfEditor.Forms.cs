using System.Globalization;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

public sealed record PdfFormFieldEntry(PdfFormFieldSpec Spec, PdfFormFieldHandle Handle, int WidgetIndex, int AnnotOrder = 0);

public sealed partial class PdfEditor
{
    const long FfReadOnly = 1, FfRequired = 2, FfMultiline = 1L << 12, FfPassword = 1L << 13, FfNoToggleToOff = 1L << 14, FfRadio = 1L << 15,
        FfPushButton = 1L << 16, FfCombo = 1L << 17, FfEdit = 1L << 18, FfMultiSelect = 1L << 21, FfComb = 1L << 24;

    static readonly Dictionary<string, string> StandardFormFonts = new(StringComparer.Ordinal)
    {
        ["Helv"] = "Helvetica", ["HeBo"] = "Helvetica-Bold", ["HeOb"] = "Helvetica-Oblique", ["HeBO"] = "Helvetica-BoldOblique",
        ["TiRo"] = "Times-Roman", ["TiBo"] = "Times-Bold", ["TiIt"] = "Times-Italic", ["TiBI"] = "Times-BoldItalic",
        ["Cour"] = "Courier", ["CoBo"] = "Courier-Bold", ["CoOb"] = "Courier-Oblique", ["CoBO"] = "Courier-BoldOblique",
        ["Symb"] = "Symbol", ["ZaDb"] = "ZapfDingbats",
    };

    static readonly short[] HelveticaWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, 1015,
        667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611,
        278, 278, 278, 469, 556, 333,
        556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 500, 333, 500, 278, 556, 500, 722, 500, 500, 500,
        334, 260, 334, 584,
    ];

    static string Num(double v) => (Math.Abs(v) < 0.00005 ? 0 : v).ToString("0.####", CultureInfo.InvariantCulture);

    static double TextWidth(string s, string fontResource, double size)
    {
        if (fontResource.StartsWith("Co", StringComparison.Ordinal) || fontResource is "Symb" or "ZaDb") return s.Length * 600 * size / 1000;
        double w = 0;
        foreach (var ch in s) w += ch is >= ' ' and <= '~' ? HelveticaWidths[ch - ' '] : 556;
        return w * size / 1000 * (fontResource.StartsWith("Ti", StringComparison.Ordinal) ? 0.93 : 1);
    }

    /// <summary>A node of the field tree. <see cref="Dict"/> is read live so edits that replace the dictionary never leave the cached index stale.</summary>
    sealed class FormNode(PdfEditor owner, PdfRef reference, PdfRef? parent, string fullName, bool terminal)
    {
        public PdfRef Ref { get; } = reference;
        public PdfRef? ParentRef { get; } = parent;
        public string FullName { get; } = fullName;
        public bool Terminal { get; } = terminal;
        public PdfDictionary Dict => (PdfDictionary)owner._objs[Ref.Number];
    }

    sealed class FormIndex
    {
        public readonly List<FormNode> All = [];
        public readonly Dictionary<string, FormNode> ByName = new(StringComparer.Ordinal);
        public readonly Dictionary<int, FormNode> ByRef = [];
        public PdfDictionary? Form;
        public void Add(FormNode n) { All.Add(n); ByName.TryAdd(n.FullName, n); ByRef[n.Ref.Number] = n; }
    }

    sealed record DaInfo(string Font, double Size, double[] Color);

    FormIndex? _formIndex;

    /// <summary>Number of times the whole field tree was walked; a diagnostic that stays linear in the number of operations, not fields.</summary>
    public int FormTreeWalkCount { get; private set; }

    PdfDictionary? AcroFormDict => Res(((PdfDictionary)_objs[_catalog]).Get("AcroForm")) as PdfDictionary;

    void InvalidateFormIndex() => _formIndex = null;

    /// <summary>The field tree index, rebuilt only when /AcroForm was replaced behind its back or a structural edit invalidated it.</summary>
    FormIndex Index()
    {
        var form = AcroFormDict;
        if (_formIndex is { } cached && ReferenceEquals(cached.Form, form)) return cached;
        FormTreeWalkCount++;
        var index = new FormIndex { Form = form };
        _formIndex = index;
        if (form is null || Res(form.Get("Fields")) is not PdfArray roots) return index;
        var seen = new HashSet<int>();
        void Walk(PdfObject? o, PdfRef? parent, string prefix, int depth)
        {
            if (depth > 32 || o is not PdfRef r || !seen.Add(r.Number) || Res(r) is not PdfDictionary d) return;
            var t = Res(d.Get("T")) is PdfString s ? s.Text : null;
            var full = t is null ? prefix : prefix.Length == 0 ? t : prefix + "." + t;
            var kids = Res(d.Get("Kids")) as PdfArray;
            var terminal = true;
            if (kids is not null)
                foreach (var k in kids.Items)
                    if (Res(k) is PdfDictionary kd && (kd.Get("T") is not null || kd.Get("Kids") is not null)) { terminal = false; break; }
            index.Add(new FormNode(this, r, parent, full, terminal));
            if (!terminal) foreach (var k in kids!.Items) Walk(k, r, full, depth + 1);
        }
        foreach (var f in roots.Items) Walk(f, null, "", 0);
        return index;
    }

    List<FormNode> FormNodes() => Index().All;

    List<PdfRef> WidgetRefs(FormNode n)
    {
        if (Res(n.Dict.Get("Kids")) is PdfArray kids)
            return kids.Items.OfType<PdfRef>().Where(k => Res(k) is PdfDictionary).ToList();
        return [n.Ref];
    }

    PdfObject? Inherited(PdfDictionary d, string key)
    {
        PdfDictionary? cur = d;
        for (var i = 0; i < 32 && cur is not null; i++)
        {
            if (cur.Get(key) is { } v) return Res(v);
            cur = Res(cur.Get("Parent")) as PdfDictionary;
        }
        return null;
    }

    string FullNameOf(PdfDictionary d)
    {
        var parts = new List<string>();
        PdfDictionary? cur = d;
        for (var i = 0; i < 32 && cur is not null; i++)
        {
            if (Res(cur.Get("T")) is PdfString s) parts.Add(s.Text);
            cur = Res(cur.Get("Parent")) as PdfDictionary;
        }
        parts.Reverse();
        return string.Join('.', parts);
    }

    PdfObject EditArray(PdfObject? raw, Func<List<PdfObject>, List<PdfObject>> edit)
    {
        var items = Res(raw) is PdfArray a ? new List<PdfObject>(a.Items) : [];
        var next = new PdfArray(edit(items));
        if (raw is PdfRef r && _objs.ContainsKey(r.Number)) { _objs[r.Number] = next; return raw; }
        return next;
    }

    void SetEntries(int objNumber, Action<Dictionary<string, PdfObject>> mutate)
    {
        var e = new Dictionary<string, PdfObject>(((PdfDictionary)_objs[objNumber]).Entries);
        mutate(e);
        _objs[objNumber] = new PdfDictionary(e);
    }

    void EditAcroForm(Action<Dictionary<string, PdfObject>> mutate)
    {
        var cat = (PdfDictionary)_objs[_catalog];
        var raw = cat.Get("AcroForm");
        var current = Res(raw) as PdfDictionary;
        var indexIsCurrent = _formIndex is { } ix && ReferenceEquals(ix.Form, current);
        var e = current is not null ? new Dictionary<string, PdfObject>(current.Entries) : [];
        mutate(e);
        var nd = new PdfDictionary(e);
        if (raw is PdfRef r && _objs.ContainsKey(r.Number)) _objs[r.Number] = nd;
        else _objs[_catalog] = With(cat, "AcroForm", nd);
        // Structural edits made through the form helpers keep the index in step; anything else forces a rebuild.
        if (indexIsCurrent) _formIndex!.Form = nd;
    }

    void EnsureAcroForm()
    {
        if (AcroFormDict is not null)
        {
            EditAcroForm(e =>
            {
                if (!e.ContainsKey("Fields")) e["Fields"] = new PdfArray([]);
                if (!e.ContainsKey("DA")) e["DA"] = new PdfString(Encoding.Latin1.GetBytes("/Helv 0 Tf 0 g"));
            });
            return;
        }
        EditAcroForm(e =>
        {
            e["Fields"] = new PdfArray([]);
            e["DA"] = new PdfString(Encoding.Latin1.GetBytes("/Helv 0 Tf 0 g"));
            e["NeedAppearances"] = new PdfBoolean(false);
        });
    }

    /// <summary>Returns the /DR font object registered under <paramref name="name"/>, creating a standard-14 font (or registering <paramref name="custom"/>) when missing.</summary>
    PdfObject FormFont(string name, PdfRef? custom = null)
    {
        PdfObject? found = null;
        if (AcroFormDict is { } form && Res(form.Get("DR")) is PdfDictionary dr && Res(dr.Get("Font")) is PdfDictionary fonts && fonts.Get(name) is { } f) found = f;
        if (found is not null && custom is null) return found;
        PdfObject font = custom ?? (StandardFormFonts.TryGetValue(name, out var baseFont)
            ? AddObject(new PdfDictionary(new Dictionary<string, PdfObject>(baseFont is "Symbol" or "ZapfDingbats"
                ? new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName(baseFont) }
                : new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName(baseFont), ["Encoding"] = new PdfName("WinAnsiEncoding") })))
            : throw new ArgumentException($"Unknown form font '{name}'."));
        EditAcroForm(e =>
        {
            var drd = Res(e.GetValueOrDefault("DR")) is PdfDictionary x ? new Dictionary<string, PdfObject>(x.Entries) : [];
            var fd = Res(drd.GetValueOrDefault("Font")) is PdfDictionary y ? new Dictionary<string, PdfObject>(y.Entries) : [];
            fd[name] = font;
            drd["Font"] = new PdfDictionary(fd);
            e["DR"] = new PdfDictionary(drd);
        });
        return font;
    }

    static string[] SplitFieldName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 512) throw new ArgumentException("A field name must be 1 to 512 characters.");
        var parts = name.Split('.');
        if (parts.Any(p => p.Length == 0 || p.Any(char.IsControl))) throw new ArgumentException("Field name parts must be non-empty and contain no control characters.");
        return parts;
    }

    static PdfRect NormalizeRect(PdfRect r)
    {
        var n = new PdfRect(Math.Min(r.Left, r.Right), Math.Min(r.Bottom, r.Top), Math.Max(r.Left, r.Right), Math.Max(r.Bottom, r.Top));
        if (!double.IsFinite(n.Left + n.Bottom + n.Right + n.Top) || n.Width < 1 || n.Height < 1) throw new ArgumentException("A field needs a finite rectangle of at least 1 x 1 points.");
        return n;
    }

    static void ValidateColor(IReadOnlyList<double>? c, string what)
    {
        if (c is null || c.Count == 0) return;
        if (c.Count is not (1 or 3 or 4) || c.Any(v => !(v >= 0 && v <= 1))) throw new ArgumentException($"{what} must be 1, 3 or 4 components in 0..1.");
    }

    static void ValidateSpec(PdfFormFieldSpec s)
    {
        SplitFieldName(s.Name);
        NormalizeRect(s.Rect);
        ValidateColor(s.TextColor, "Text colour"); ValidateColor(s.BorderColor, "Border colour"); ValidateColor(s.BackgroundColor, "Background colour");
        if (!(s.BorderWidth >= 0 && s.BorderWidth <= 20)) throw new ArgumentException("Border width must be 0..20.");
        if (s.BorderStyle is not ("S" or "D" or "B" or "I" or "U")) throw new ArgumentException("Unknown border style.");
        if (s.Alignment is < 0 or > 2) throw new ArgumentException("Alignment must be 0, 1 or 2.");
        if (!(s.FontSize >= 0 && s.FontSize <= 200)) throw new ArgumentException("Font size must be 0 (auto) to 200.");
        if (s.MaxLength < 0) throw new ArgumentException("Maximum length cannot be negative.");
        if (s.FontObject is null && !StandardFormFonts.ContainsKey(s.FontName)) throw new ArgumentException($"Unknown form font '{s.FontName}'.");
        if (s.Kind == PdfFormFieldKind.CombText && s.MaxLength < 1) throw new ArgumentException("A comb field needs a maximum length.");
        if (s.Kind is PdfFormFieldKind.Checkbox or PdfFormFieldKind.Radio && (string.IsNullOrWhiteSpace(s.ExportValue) || s.ExportValue == "Off" || s.ExportValue.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))))
            throw new ArgumentException("The export value must be a non-empty name without spaces and not 'Off'.");
        if (s.Kind is PdfFormFieldKind.ComboBox or PdfFormFieldKind.ListBox)
        {
            if (s.Options is not { Count: > 0 } || s.Options.Any(o => string.IsNullOrEmpty(o.Export))) throw new ArgumentException("A choice field needs at least one option with an export value.");
            if (s.Options.Select(o => o.Export).Distinct().Count() != s.Options.Count) throw new ArgumentException("Option export values must be unique.");
            if (s.Kind == PdfFormFieldKind.ComboBox && !s.Editable && s.Value is { Length: > 0 } v && !s.Options.Any(o => o.Export == v)) throw new ArgumentException("The value is not one of the options.");
        }
        if (s.Kind is PdfFormFieldKind.Text or PdfFormFieldKind.CombText or PdfFormFieldKind.PasswordText && s.MaxLength > 0 && s.Value is { } tv && tv.Length > s.MaxLength)
            throw new ArgumentException("The value is longer than the maximum length.");
    }

    static string FieldType(PdfFormFieldKind k) => k switch
    {
        PdfFormFieldKind.Text or PdfFormFieldKind.MultilineText or PdfFormFieldKind.CombText or PdfFormFieldKind.PasswordText => "Tx",
        PdfFormFieldKind.Checkbox or PdfFormFieldKind.Radio or PdfFormFieldKind.PushButton => "Btn",
        PdfFormFieldKind.ComboBox or PdfFormFieldKind.ListBox => "Ch",
        _ => "Sig",
    };

    static PdfObject ColorArray(IReadOnlyList<double> c) => new PdfArray(c.Select(v => (PdfObject)new PdfReal(v)).ToList());
    static PdfString Text(string s) => new(EncodeText(s));
    static string ColorOps(IReadOnlyList<double>? c, bool stroke)
    {
        if (c is null || c.Count == 0) return "";
        var op = c.Count switch { 1 => stroke ? "G" : "g", 3 => stroke ? "RG" : "rg", _ => stroke ? "K" : "k" };
        return string.Join(' ', c.Select(Num)) + " " + op + " ";
    }

    static string DaString(PdfFormFieldSpec s) =>
        $"/{s.FontName} {Num(s.FontSize)} Tf " + (s.TextColor is { Count: > 0 } ? ColorOps(s.TextColor, false) : "0 g ").TrimEnd();

    static IEnumerable<string> SelectedValues(PdfFormFieldSpec s) =>
        s.Value is { Length: > 0 } v ? (s.MultiSelect ? v.Replace("\r", "").Split('\n').Where(x => x.Length > 0) : [v]) : [];

    Dictionary<string, PdfObject> FieldEntries(PdfFormFieldSpec s)
    {
        var e = new Dictionary<string, PdfObject>();
        long ff = 0;
        if (s.ReadOnly) ff |= FfReadOnly;
        if (s.Required) ff |= FfRequired;
        switch (s.Kind)
        {
            case PdfFormFieldKind.MultilineText: ff |= FfMultiline; break;
            case PdfFormFieldKind.PasswordText: ff |= FfPassword; break;
            case PdfFormFieldKind.CombText: ff |= FfComb; break;
            case PdfFormFieldKind.Radio: ff |= FfRadio | FfNoToggleToOff; break;
            case PdfFormFieldKind.PushButton: ff |= FfPushButton; break;
            case PdfFormFieldKind.ComboBox: ff |= FfCombo | (s.Editable ? FfEdit : 0); break;
            case PdfFormFieldKind.ListBox: ff |= s.MultiSelect ? FfMultiSelect : 0; break;
        }
        var ft = FieldType(s.Kind);
        e["FT"] = new PdfName(ft);
        if (ff != 0) e["Ff"] = new PdfInteger(ff);
        if (s.Tooltip is { Length: > 0 } tu) e["TU"] = Text(tu);
        if (ft != "Sig") e["DA"] = new PdfString(Encoding.Latin1.GetBytes(DaString(s)));
        if (ft is "Tx" or "Ch" && s.Alignment != 0) e["Q"] = new PdfInteger(s.Alignment);
        if (ft == "Tx" && s.MaxLength > 0) e["MaxLen"] = new PdfInteger(s.MaxLength);
        switch (s.Kind)
        {
            case PdfFormFieldKind.Text or PdfFormFieldKind.MultilineText or PdfFormFieldKind.CombText or PdfFormFieldKind.PasswordText:
                if (s.Value is { Length: > 0 } tv) e["V"] = Text(tv);
                if (s.DefaultValue is { Length: > 0 } dv) e["DV"] = Text(dv);
                break;
            case PdfFormFieldKind.ComboBox or PdfFormFieldKind.ListBox:
                e["Opt"] = new PdfArray(s.Options!.Select(o => o.Display is { Length: > 0 } d && d != o.Export
                    ? (PdfObject)new PdfArray([Text(o.Export), Text(d)]) : Text(o.Export)).ToList());
                var sel = SelectedValues(s).ToList();
                if (sel.Count == 1) e["V"] = Text(sel[0]);
                else if (sel.Count > 1) e["V"] = new PdfArray(sel.Select(x => (PdfObject)Text(x)).ToList());
                if (s.Kind == PdfFormFieldKind.ListBox && sel.Count > 0)
                    e["I"] = new PdfArray(s.Options!.Select((o, i) => (o, i)).Where(t => sel.Contains(t.o.Export)).Select(t => (PdfObject)new PdfInteger(t.i)).ToList());
                if (s.DefaultValue is { Length: > 0 } cdv) e["DV"] = Text(cdv);
                break;
            case PdfFormFieldKind.Checkbox:
                e["V"] = new PdfName(s.Value == s.ExportValue ? s.ExportValue : "Off");
                break;
        }
        return e;
    }

    Dictionary<string, PdfObject> WidgetEntries(PdfFormFieldSpec s)
    {
        var r = NormalizeRect(s.Rect);
        var e = new Dictionary<string, PdfObject>
        {
            ["Rect"] = new PdfArray([new PdfReal(r.Left), new PdfReal(r.Bottom), new PdfReal(r.Right), new PdfReal(r.Top)]),
        };
        var mk = new Dictionary<string, PdfObject>();
        if (s.BorderColor is { Count: > 0 }) mk["BC"] = ColorArray(s.BorderColor);
        if (s.BackgroundColor is { Count: > 0 }) mk["BG"] = ColorArray(s.BackgroundColor);
        if (s.Kind == PdfFormFieldKind.Checkbox) mk["CA"] = new PdfString("4"u8.ToArray());
        else if (s.Kind == PdfFormFieldKind.Radio) mk["CA"] = new PdfString("l"u8.ToArray());
        else if (s.Kind == PdfFormFieldKind.PushButton && s.Caption is { Length: > 0 } cap) mk["CA"] = Text(cap);
        if (mk.Count > 0) e["MK"] = new PdfDictionary(mk);
        var bs = new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Border"), ["W"] = new PdfReal(s.BorderWidth), ["S"] = new PdfName(s.BorderStyle) };
        if (s.BorderStyle == "D") bs["D"] = new PdfArray([new PdfInteger(3)]);
        e["BS"] = new PdfDictionary(bs);
        return e;
    }

    static readonly string[] FieldManaged = ["FT", "Ff", "TU", "DA", "Q", "MaxLen", "Opt", "I", "V", "DV"];
    static readonly string[] WidgetManaged = ["Rect", "MK", "BS"];

    Dictionary<int, int> WidgetPages()
    {
        var map = new Dictionary<int, int>();
        for (var i = 0; i < _pages.Count; i++)
            if (Res(((PdfDictionary)_objs[_pages[i]]).Get("Annots")) is PdfArray a)
                foreach (var it in a.Items) if (it is PdfRef r) map[r.Number] = i;
        return map;
    }

    void AppendAnnot(int pageIndex, PdfRef widget)
    {
        var n = _pages[pageIndex];
        var page = (PdfDictionary)_objs[n];
        _objs[n] = With(page, "Annots", EditArray(page.Get("Annots"), l => { if (!l.Any(x => x is PdfRef r && r.Number == widget.Number)) l.Add(widget); return l; }));
    }

    void RemoveAnnots(ISet<int> widgetNumbers)
    {
        foreach (var n in _pages.ToList())
        {
            var page = (PdfDictionary)_objs[n];
            if (Res(page.Get("Annots")) is not PdfArray a || !a.Items.Any(x => x is PdfRef r && widgetNumbers.Contains(r.Number))) continue;
            _objs[n] = With(page, "Annots", EditArray(page.Get("Annots"), l => l.Where(x => !(x is PdfRef r && widgetNumbers.Contains(r.Number))).ToList()));
        }
    }

    void AttachChild(PdfRef? parent, PdfRef child)
    {
        if (parent is null) { EditAcroForm(e => e["Fields"] = EditArray(e.GetValueOrDefault("Fields"), l => { l.Add(child); return l; })); return; }
        SetEntries(parent.Number, e => e["Kids"] = EditArray(e.GetValueOrDefault("Kids"), l => { l.Add(child); return l; }));
    }

    void DetachChild(PdfRef? parent, PdfRef child)
    {
        List<PdfObject> Drop(List<PdfObject> l) => l.Where(x => !(x is PdfRef r && r.Number == child.Number)).ToList();
        if (parent is null) EditAcroForm(e => e["Fields"] = EditArray(e.GetValueOrDefault("Fields"), Drop));
        else SetEntries(parent.Number, e => e["Kids"] = EditArray(e.GetValueOrDefault("Kids"), Drop));
    }

    /// <summary>Creates (or finds) the non-terminal ancestors for a hierarchical name and returns the reference of the immediate parent.</summary>
    PdfRef? EnsureParents(string[] parts)
    {
        PdfRef? parent = null;
        var index = Index();
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var prefix = string.Join('.', parts.Take(i + 1));
            if (index.ByName.TryGetValue(prefix, out var node))
            {
                if (node.Terminal) throw new ArgumentException($"'{prefix}' is already a field and cannot contain other fields.");
                parent = node.Ref;
                continue;
            }
            var d = new Dictionary<string, PdfObject> { ["T"] = Text(parts[i]), ["Kids"] = new PdfArray([]) };
            if (parent is not null) d["Parent"] = parent;
            var r = AddObject(new PdfDictionary(d));
            AttachChild(parent, r);
            index.Add(new FormNode(this, r, parent, prefix, false));
            parent = r;
        }
        return parent;
    }

    /// <summary>Creates a form field and its widget on a page, builds the appearance streams and creates or updates /AcroForm. A radio spec whose name matches an existing radio group adds a kid to it.</summary>
    public PdfFormFieldHandle AddFormField(PdfFormFieldSpec spec)
    {
        Require(PdfPermissions.Modify);
        ArgumentNullException.ThrowIfNull(spec);
        Check(spec.Page);
        ValidateSpec(spec);
        var parts = SplitFieldName(spec.Name);
        var index = Index();
        index.ByName.TryGetValue(spec.Name, out var clash);
        PdfRef widget, field;
        var widgetIndex = 0;
        if (clash is not null)
        {
            if (spec.Kind != PdfFormFieldKind.Radio || clash.Terminal is false || Inherited(clash.Dict, "FT") is not PdfName { Value: "Btn" }
                || ((long)(Inherited(clash.Dict, "Ff").AsNumber() ?? 0) & FfRadio) == 0 || clash.Dict.Get("Kids") is null)
                throw new ArgumentException($"A form field named '{spec.Name}' already exists.");
            if (RadioExports(clash).Contains(spec.ExportValue)) throw new ArgumentException($"The radio group '{spec.Name}' already has the export value '{spec.ExportValue}'.");
            field = clash.Ref;
            var kid = new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Annot"), ["Subtype"] = new PdfName("Widget"), ["F"] = new PdfInteger(4), ["Parent"] = field, ["P"] = new PdfRef(_pages[spec.Page], 0) };
            foreach (var (k, v) in WidgetEntries(spec)) kid[k] = v;
            widget = AddObject(new PdfDictionary(kid));
            SetEntries(field.Number, e => e["Kids"] = EditArray(e.GetValueOrDefault("Kids"), l => { l.Add(widget); return l; }));
            if (spec.Value == spec.ExportValue) SetEntries(field.Number, e => e["V"] = new PdfName(spec.ExportValue));
            widgetIndex = (Res(((PdfDictionary)_objs[field.Number]).Get("Kids")) as PdfArray)?.Count - 1 ?? 0;
        }
        else
        {
            for (var i = 1; i < parts.Length; i++)
                if (index.ByName.TryGetValue(string.Join('.', parts.Take(i)), out var prior) && prior.Terminal)
                    throw new ArgumentException($"'{string.Join('.', parts.Take(i))}' is already a field and cannot contain other fields.");
            EnsureAcroForm();
            var parent = EnsureParents(parts);
            var pageRef = new PdfRef(_pages[spec.Page], 0);
            var leaf = new Dictionary<string, PdfObject> { ["T"] = Text(parts[^1]) };
            if (parent is not null) leaf["Parent"] = parent;
            if (spec.Kind == PdfFormFieldKind.Radio)
            {
                foreach (var (k, v) in FieldEntries(spec)) leaf[k] = v;
                leaf["V"] = new PdfName(spec.Value == spec.ExportValue ? spec.ExportValue : "Off");
                leaf["Kids"] = new PdfArray([]);
                field = AddObject(new PdfDictionary(leaf));
                var kid = new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Annot"), ["Subtype"] = new PdfName("Widget"), ["F"] = new PdfInteger(4), ["Parent"] = field, ["P"] = pageRef };
                foreach (var (k, v) in WidgetEntries(spec)) kid[k] = v;
                widget = AddObject(new PdfDictionary(kid));
                SetEntries(field.Number, e => e["Kids"] = new PdfArray([widget]));
            }
            else
            {
                leaf["Type"] = new PdfName("Annot"); leaf["Subtype"] = new PdfName("Widget"); leaf["F"] = new PdfInteger(4); leaf["P"] = pageRef;
                foreach (var (k, v) in FieldEntries(spec)) leaf[k] = v;
                foreach (var (k, v) in WidgetEntries(spec)) leaf[k] = v;
                field = widget = AddObject(new PdfDictionary(leaf));
            }
            AttachChild(parent, field);
            index.Add(new FormNode(this, field, parent, spec.Name, true));
        }
        AppendAnnot(spec.Page, widget);
        FormFont(spec.FontName, spec.FontObject);
        RebuildAppearance(widget, spec.ExportValue);
        if (spec.Kind == PdfFormFieldKind.Radio)         foreach (var k in WidgetRefs(index.ByRef[field.Number])) RebuildAppearance(k);
                return new PdfFormFieldHandle(spec.Name, field, widget, widgetIndex);
    }

    HashSet<string> RadioExports(FormNode group)
    {
        var set = new HashSet<string>();
        foreach (var k in WidgetRefs(group))
            if (Res(k) is PdfDictionary kd && ExistingOnState(kd) is { } on) set.Add(on);
        return set;
    }

    string? ExistingOnState(PdfDictionary widget) =>
        Res(widget.Get("AP")) is PdfDictionary ap && Res(ap.Get("N")) is PdfDictionary n ? n.Entries.Keys.FirstOrDefault(k => k != "Off") : null;

    /// <summary>Reads every authored or foreign Tx/Btn/Ch/Sig widget back into editable specs (one per widget).</summary>
    public IReadOnlyList<PdfFormFieldEntry> GetFormFieldSpecs()
    {
        var result = new List<PdfFormFieldEntry>();
        var pages = WidgetPages();
        var order = new Dictionary<int, int>();
        foreach (var pn in _pages)
            if (Res(((PdfDictionary)_objs[pn]).Get("Annots")) is PdfArray an)
                for (var ai = 0; ai < an.Items.Count; ai++) if (an.Items[ai] is PdfRef ar) order[ar.Number] = ai;
        foreach (var n in FormNodes().Where(n => n.Terminal))
        {
            var widgets = WidgetRefs(n);
            for (var i = 0; i < widgets.Count; i++)
            {
                if (Res(widgets[i]) is not PdfDictionary w) continue;
                var page = pages.TryGetValue(widgets[i].Number, out var pi) ? pi : Res(w.Get("P")) is { } && w.Get("P") is PdfRef pr ? _pages.IndexOf(pr.Number) : -1;
                if (page < 0 || ReadFormSpec(n, widgets[i], w, page) is not { } spec) continue;
                result.Add(new PdfFormFieldEntry(spec, new PdfFormFieldHandle(n.FullName, n.Ref, widgets[i]), i, order.GetValueOrDefault(widgets[i].Number)));
            }
        }
        return result;
    }

    static double[] ColorOf(PdfObject? o) => o is PdfArray a ? a.Items.Select(x => x.AsNumber() ?? 0).ToArray() : [];

    PdfFormFieldSpec? ReadFormSpec(FormNode node, PdfRef widgetRef, PdfDictionary w, int page)
    {
        var ft = (Inherited(w, "FT") as PdfName)?.Value;
        var ff = (long)(Inherited(w, "Ff").AsNumber() ?? 0);
        var kind = ft switch
        {
            "Tx" => (ff & FfPassword) != 0 ? PdfFormFieldKind.PasswordText : (ff & FfComb) != 0 && (Inherited(w, "MaxLen").AsNumber() ?? 0) > 0 ? PdfFormFieldKind.CombText
                : (ff & FfMultiline) != 0 ? PdfFormFieldKind.MultilineText : PdfFormFieldKind.Text,
            "Btn" => (ff & FfRadio) != 0 ? PdfFormFieldKind.Radio : (ff & FfPushButton) != 0 ? PdfFormFieldKind.PushButton : PdfFormFieldKind.Checkbox,
            "Ch" => (ff & FfCombo) != 0 ? PdfFormFieldKind.ComboBox : PdfFormFieldKind.ListBox,
            "Sig" => PdfFormFieldKind.Signature,
            _ => (PdfFormFieldKind?)null,
        };
        if (kind is not { } k || Res(w.Get("Rect")) is not PdfArray ra || ra.Count < 4) return null;
        double R(int i) => Res(ra[i]).AsNumber() ?? 0;
        var rect = new PdfRect(Math.Min(R(0), R(2)), Math.Min(R(1), R(3)), Math.Max(R(0), R(2)), Math.Max(R(1), R(3)));
        var da = ParseDa(Inherited(w, "DA") is PdfString ds ? Encoding.Latin1.GetString(ds.Bytes) : "");
        var mk = Res(w.Get("MK")) as PdfDictionary;
        var bs = Res(w.Get("BS")) as PdfDictionary;
        var opts = Inherited(w, "Opt") is PdfArray oa
            ? oa.Items.Select(o => Res(o) switch
            {
                PdfString s => new PdfFormOption(s.Text),
                PdfArray { Count: >= 2 } p when Res(p[0]) is PdfString ex && Res(p[1]) is PdfString di => new PdfFormOption(ex.Text, di.Text),
                _ => null,
            }).OfType<PdfFormOption>().ToList() : null;
        string? Val(PdfObject? v) => v switch
        {
            PdfString s => s.Text,
            PdfName nm => nm.Value,
            PdfArray arr => string.Join('\n', arr.Items.Select(x => Res(x) is PdfString s ? s.Text : "")),
            _ => null,
        };
        var export = ExistingOnState(w) ?? "Yes";
        return new PdfFormFieldSpec(k, node.FullName, page, rect)
        {
            Tooltip = Inherited(w, "TU") is PdfString tu ? tu.Text : null,
            Required = (ff & FfRequired) != 0,
            ReadOnly = (ff & FfReadOnly) != 0,
            Value = Val(Inherited(w, "V")),
            DefaultValue = Val(Inherited(w, "DV")),
            MaxLength = (int)(Inherited(w, "MaxLen").AsNumber() ?? 0),
            FontName = da.Font,
            FontSize = da.Size,
            TextColor = da.Color,
            BorderColor = mk?.Get("BC") is { } bc && ColorOf(Res(bc)) is { Length: > 0 } bca ? bca : null,
            BackgroundColor = mk?.Get("BG") is { } bg && ColorOf(Res(bg)) is { Length: > 0 } bga ? bga : null,
            BorderWidth = bs is not null && Res(bs.Get("W")).AsNumber() is { } bw ? bw : 1,
            BorderStyle = bs is not null && Res(bs.Get("S")).AsName() is { Length: 1 } st && "SDBIU".Contains(st) ? st : "S",
            Alignment = (int)(Inherited(w, "Q").AsNumber() ?? 0) is var q && q is 1 or 2 ? q : 0,
            Options = opts,
            ExportValue = export,
            Caption = Res(mk?.Get("CA")) is PdfString ca ? ca.Text : null,
            Editable = (ff & FfEdit) != 0,
            MultiSelect = (ff & FfMultiSelect) != 0,
        };
    }

    static DaInfo ParseDa(string da)
    {
        var t = da.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var font = "Helv"; var size = 0.0; double[] color = [0];
        double P(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] == "Tf" && i >= 2) { font = t[i - 2].TrimStart('/'); size = P(t[i - 1]); }
            else if (t[i] == "g" && i >= 1) color = [P(t[i - 1])];
            else if (t[i] == "rg" && i >= 3) color = [P(t[i - 3]), P(t[i - 2]), P(t[i - 1])];
            else if (t[i] == "k" && i >= 4) color = [P(t[i - 4]), P(t[i - 3]), P(t[i - 2]), P(t[i - 1])];
        }
        return new DaInfo(font, size, color);
    }

    FormNode FindTerminal(string fullName) =>
        Index().ByName.TryGetValue(fullName, out var n) && n.Terminal ? n : throw new ArgumentException($"No form field named '{fullName}'.");

    /// <summary>Applies a full spec to an existing widget (renaming, moving between pages and regenerating appearances). Check boxes, push buttons and text kinds may change kind among themselves; radio kids cannot change to or from other kinds.</summary>
    public void UpdateFormField(string fullName, PdfFormFieldSpec spec, int widgetIndex = 0)
    {
        Require(PdfPermissions.Modify);
        ArgumentNullException.ThrowIfNull(spec);
        Check(spec.Page);
        ValidateSpec(spec);
        var node = FindTerminal(fullName);
        var widgets = WidgetRefs(node);
        if ((uint)widgetIndex >= (uint)widgets.Count) throw new ArgumentOutOfRangeException(nameof(widgetIndex));
        var wref = widgets[widgetIndex];
        var w = (PdfDictionary)_objs[wref.Number];
        var currentPage = w.Get("P") is PdfRef pageRef ? _pages.IndexOf(pageRef.Number) : -1;
        if (currentPage < 0) currentPage = WidgetPages().TryGetValue(wref.Number, out var cp) ? cp : -1;
        var current = ReadFormSpec(node, wref, w, Math.Max(currentPage, 0)) ?? throw new ArgumentException("This field type cannot be edited.");
        var radio = current.Kind == PdfFormFieldKind.Radio;
        if (radio != (spec.Kind == PdfFormFieldKind.Radio)) throw new ArgumentException("A field cannot be converted to or from a radio button.");
        if (radio && spec.ExportValue != current.ExportValue && widgets.Select(x => Res(x)).OfType<PdfDictionary>().Any(d => ExistingOnState(d) == spec.ExportValue))
            throw new ArgumentException($"The radio group already has the export value '{spec.ExportValue}'.");
        if (spec.Name != fullName) RenameFormField(fullName, spec.Name);
        node = FindTerminal(spec.Name);
        wref = WidgetRefs(node)[widgetIndex];
        var fieldNumber = radio ? node.Ref.Number : wref.Number;
        var fieldEntries = FieldEntries(spec);
        if (radio)
        {
            var v = spec.Value is { Length: > 0 } sv && sv != "Off" ? sv : "Off";
            if (v == current.ExportValue && v != spec.ExportValue) v = spec.ExportValue;
            fieldEntries["V"] = new PdfName(v);
        }
        SetEntries(fieldNumber, e =>
        {
            foreach (var key in FieldManaged) e.Remove(key);
            foreach (var (k, v) in fieldEntries) e[k] = v;
        });
        var widgetEntries = WidgetEntries(spec);
        SetEntries(wref.Number, e =>
        {
            foreach (var key in WidgetManaged) e.Remove(key);
            foreach (var (k, v) in widgetEntries) e[k] = v;
            e["P"] = new PdfRef(_pages[spec.Page], 0);
        });
        if (currentPage != spec.Page)
        {
            RemoveAnnots(new HashSet<int> { wref.Number });
            AppendAnnot(spec.Page, wref);
        }
        FormFont(spec.FontName, spec.FontObject);
        RebuildAppearance(wref, spec.ExportValue);
        if (radio) foreach (var k in WidgetRefs(FindTerminal(spec.Name)).Where(x => x.Number != wref.Number)) RebuildAppearance(k);
    }

    /// <summary>Changes the fully qualified name of a field, re-parenting it as needed and refusing duplicates.</summary>
    public void RenameFormField(string fullName, string newName)
    {
        Require(PdfPermissions.Modify);
        if (fullName == newName) return;
        var parts = SplitFieldName(newName);
        var index = Index();
        var node = index.ByName.TryGetValue(fullName, out var found) ? found : throw new ArgumentException($"No form field named '{fullName}'.");
        if (index.ByName.ContainsKey(newName)) throw new ArgumentException($"A form field named '{newName}' already exists.");
        for (var i = 1; i < parts.Length; i++)
            if (index.ByName.TryGetValue(string.Join('.', parts.Take(i)), out var t) && t.Terminal && t.Ref.Number != node.Ref.Number)
                throw new ArgumentException($"'{t.FullName}' is already a field and cannot contain other fields.");
        var oldPrefix = fullName.Contains('.') ? fullName[..fullName.LastIndexOf('.')] : "";
        var newPrefix = newName.Contains('.') ? newName[..newName.LastIndexOf('.')] : "";
        if (oldPrefix == newPrefix) { SetEntries(node.Ref.Number, e => e["T"] = Text(parts[^1])); InvalidateFormIndex(); return; }
        if (!node.Terminal) throw new NotSupportedException("Only the last name part of a field group can be renamed.");
        DetachChild(node.ParentRef, node.Ref);
        PruneEmptyAncestors(node.ParentRef);
        InvalidateFormIndex();
        var parent = EnsureParents(parts);
        SetEntries(node.Ref.Number, e => { e["T"] = Text(parts[^1]); if (parent is null) e.Remove("Parent"); else e["Parent"] = parent; });
        AttachChild(parent, node.Ref);
        InvalidateFormIndex();
    }

    void PruneEmptyAncestors(PdfRef? parent)
    {
        for (var i = 0; i < 32 && parent is not null; i++)
        {
            var d = (PdfDictionary)_objs[parent.Number];
            if (Res(d.Get("Kids")) is PdfArray { Count: > 0 }) return;
            var up = d.Get("Parent") as PdfRef;
            DetachChild(up, parent);
            _deleted.Add(parent.Number);
            parent = up;
        }
    }

    /// <summary>Removes a field (all its widgets) or, with <paramref name="widgetIndex"/>, a single widget of a field group. Returns false when no such field exists.</summary>
    public bool RemoveFormField(string fullName, int widgetIndex = -1)
    {
        Require(PdfPermissions.Modify);
        var index = Index();
        List<FormNode> targets;
        if (index.ByName.TryGetValue(fullName, out var exact) && exact.Terminal) targets = [exact];
        else
        {
            var prefix = fullName + ".";
            targets = index.All.Where(n => n.Terminal && n.FullName.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }
        if (targets.Count == 0) return false;
        if (widgetIndex >= 0 && targets.Count == 1 && targets[0].FullName == fullName)
        {
            var t = targets[0];
            var widgets = WidgetRefs(t);
            if (widgets.Count > 1 && widgets[0].Number != t.Ref.Number)
            {
                if (widgetIndex >= widgets.Count) throw new ArgumentOutOfRangeException(nameof(widgetIndex));
                var victim = widgets[widgetIndex];
                _deleted.Add(victim.Number);
                SetEntries(t.Ref.Number, e => e["Kids"] = EditArray(e.GetValueOrDefault("Kids"), l => l.Where(x => !(x is PdfRef r && r.Number == victim.Number)).ToList()));
                RemoveAnnots(new HashSet<int> { victim.Number });
                InvalidateFormIndex();
                return true;
            }
        }
        RemoveTerminalNodes(targets);
        return true;
    }

    /// <summary>
    /// Removes terminal fields in one pass: every field is detached from its parent once, emptied ancestors are pruned deepest-first,
    /// and the page /Annots arrays and /CO are rewritten once, so the cost is linear in the number of removed fields.
    /// </summary>
    void RemoveTerminalNodes(IReadOnlyCollection<FormNode> targets)
    {
        var index = Index();
        var gone = new HashSet<int>();
        var removedKids = new Dictionary<int, HashSet<int>>();
        var queue = new PriorityQueue<int, int>();
        void Mark(PdfRef? parent, PdfRef child)
        {
            var key = parent?.Number ?? 0;
            if (!removedKids.TryGetValue(key, out var set))
            {
                removedKids[key] = set = [];
                if (key != 0) queue.Enqueue(key, -index.ByRef[key].FullName.Count(c => c == '.'));
            }
            set.Add(child.Number);
        }
        foreach (var t in targets)
        {
            foreach (var wr in WidgetRefs(t)) gone.Add(wr.Number);
            gone.Add(t.Ref.Number);
            Mark(t.ParentRef, t.Ref);
        }
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var set = removedKids[p];
            SetEntries(p, e => e["Kids"] = EditArray(e.GetValueOrDefault("Kids"), l => l.Where(x => !(x is PdfRef r && set.Contains(r.Number))).ToList()));
            if (Res(((PdfDictionary)_objs[p]).Get("Kids")) is PdfArray { Count: 0 } && gone.Add(p)) Mark(index.ByRef[p].ParentRef, new PdfRef(p, 0));
        }
        if (removedKids.TryGetValue(0, out var roots))
            EditAcroForm(e => e["Fields"] = EditArray(e.GetValueOrDefault("Fields"), l => l.Where(x => !(x is PdfRef r && roots.Contains(r.Number))).ToList()));
        _deleted.UnionWith(gone);
        RemoveAnnots(gone);
        if (AcroFormDict is { } f && Res(f.Get("CO")) is PdfArray)
            EditAcroForm(e => e["CO"] = EditArray(e.GetValueOrDefault("CO"), l => l.Where(x => !(x is PdfRef r && gone.Contains(r.Number))).ToList()));
        InvalidateFormIndex();
    }

    /// <summary>Sets /Tabs on a page (row, column or structure order; Unspecified removes it).</summary>
    public void SetPageTabOrder(int page, PdfFormTabOrder order)
    {
        Require(PdfPermissions.Modify);
        Check(page);
        SetEntries(_pages[page], e =>
        {
            if (order == PdfFormTabOrder.Unspecified) e.Remove("Tabs");
            else e["Tabs"] = new PdfName(order switch { PdfFormTabOrder.Row => "R", PdfFormTabOrder.Column => "C", _ => "S" });
        });
    }

    /// <summary>Reorders the page's widget annotations so the named fields appear in the given order (the slots they occupy are reused; other annotations keep their positions).</summary>
    public void ReorderFormWidgets(int page, IReadOnlyList<string> orderedNames)
    {
        Require(PdfPermissions.Modify);
        Check(page);
        var index = Index();
        var pageNumber = _pages[page];
        var pageDict = (PdfDictionary)_objs[pageNumber];
        if (Res(pageDict.Get("Annots")) is not PdfArray annots) return;
        var onPage = new HashSet<int>(annots.Items.OfType<PdfRef>().Select(r => r.Number));
        var ordered = new List<PdfRef>();
        var chosen = new HashSet<int>();
        foreach (var name in orderedNames)
            if (index.ByName.TryGetValue(name, out var n) && n.Terminal)
                foreach (var wr in WidgetRefs(n))
                    if (onPage.Contains(wr.Number) && chosen.Add(wr.Number)) ordered.Add(wr);
        var slots = annots.Items.Select((x, i) => (x, i)).Where(t => t.x is PdfRef r && chosen.Contains(r.Number)).Select(t => t.i).ToList();
        var items = new List<PdfObject>(annots.Items);
        for (var i = 0; i < slots.Count; i++) items[slots[i]] = ordered[i];
        _objs[pageNumber] = With(pageDict, "Annots", EditArray(pageDict.Get("Annots"), _ => items));
    }
}
