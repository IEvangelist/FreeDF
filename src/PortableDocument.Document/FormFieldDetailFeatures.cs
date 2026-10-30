using PortableDocument.Core;

namespace PortableDocument.Document;

// Inert typed form-field semantics (ISO 32000-2:2020 12.7 interactive forms; field flags 12.7.4.x, signature
// fields 12.7.5.5). Citations are by clause; table numbers are not cited because the primary text could not be consulted.
// Nothing is filled, calculated, validated, formatted, submitted or signed, and no XFA content is interpreted.
// Inheritance is applied top-down through /Kids (cycle-safe) for FT, Ff, V, DV, DA, Q, MaxLen and Opt; /TI and /I are
// read from the field's own dictionary only. AcroForm /DA and /Q are used as documented fallbacks and flagged.

/// <summary>Field-flag bits (12.7.4.x). Bit-position-specific accessors are meaningful only for the matching field type.</summary>
public readonly record struct PdfFieldFlagSet(long Raw, string? FieldType)
{
    bool Bit(int position) => (Raw & (1L << (position - 1))) != 0;
    bool Is(string type) => FieldType == type;
    public bool ReadOnly => Bit(1);
    public bool Required => Bit(2);
    public bool NoExport => Bit(3);
    public bool NoToggleToOff => Is("Btn") && Bit(15);
    public bool Radio => Is("Btn") && Bit(16);
    public bool Pushbutton => Is("Btn") && Bit(17);
    public bool RadiosInUnison => Is("Btn") && Bit(26);
    public bool Multiline => Is("Tx") && Bit(13);
    public bool Password => Is("Tx") && Bit(14);
    public bool FileSelect => Is("Tx") && Bit(21);
    public bool DoNotScroll => Is("Tx") && Bit(24);
    public bool Comb => Is("Tx") && Bit(25);
    public bool RichText => Is("Tx") && Bit(26);
    public bool Combo => Is("Ch") && Bit(18);
    public bool Edit => Is("Ch") && Bit(19);
    public bool Sort => Is("Ch") && Bit(20);
    public bool MultiSelect => Is("Ch") && Bit(22);
    public bool DoNotSpellCheck => (Is("Tx") || Is("Ch")) && Bit(23);
    public bool CommitOnSelectionChange => Is("Ch") && Bit(27);
}

/// <summary>One /Opt entry: a text string (export = display) or a two-element [export display] array. Raw keeps the original element.</summary>
public sealed record PdfFieldOption(PdfString? Export, PdfString? Display, PdfObject Raw);

/// <summary>Signature field lock dictionary (12.7.5.5). Field names are not resolved and nothing is locked.</summary>
public sealed record PdfSignatureFieldLock(PdfName? Action, PdfArray? Fields, long? Permissions, PdfDictionary Dictionary);

/// <summary>Signature field seed value (12.7.5.5). Certificate seed and other nested dictionaries stay raw; no constraint is enforced.</summary>
public sealed record PdfSignatureSeedValue(
    long? Flags, PdfName? Filter, PdfArray? SubFilter, PdfArray? DigestMethod, double? Version, PdfDictionary? Certificate,
    PdfArray? Reasons, PdfDictionary? Mdp, PdfDictionary? TimeStamp, PdfArray? LegalAttestation, bool? AddRevInfo, PdfDictionary Dictionary);

public sealed record PdfFormFieldDetails(
    string FullName,
    PdfString? PartialName,
    PdfName? FieldType,
    PdfObject? Value,
    PdfObject? DefaultValue,
    long Flags,
    PdfString? DefaultAppearance,
    bool DefaultAppearanceFromAcroForm,
    long? Quadding,
    bool QuadingFromAcroForm,
    long? MaxLength,
    IReadOnlyList<PdfFieldOption> Options,
    long? TopIndex,
    IReadOnlyList<long> SelectedIndices,
    PdfSignatureSeedValue? SeedValue,
    PdfSignatureFieldLock? Lock,
    IReadOnlyList<PdfDictionary> Widgets,
    PdfDictionary Dictionary)
{
    public PdfFieldFlagSet FlagSet => new(Flags, FieldType?.Value);
    /// <summary>Quadding with the spec default 0 (left) applied.</summary>
    public long EffectiveQuadding => Quadding ?? 0;
}

/// <summary>AcroForm-level state plus every terminal field. XFA is only reported as present; its packets stay opaque (see GetXfaPackets).</summary>
public sealed record PdfFormFieldDetailSet(
    IReadOnlyList<PdfFormFieldDetails> Fields,
    bool Truncated,
    bool HasXfa,
    bool? NeedAppearances,
    long? SignatureFlags,
    PdfString? DefaultAppearance,
    long? Quadding,
    PdfArray? CalculationOrder);

public static class FormFieldDetailFeatures
{
    public const int MaxFields = 100_000;
    public const int MaxDepth = 64;
    const int MaxOptions = 65_536;

    readonly record struct Inherited(PdfName? Ft, PdfObject? V, PdfObject? Dv, long Ff, PdfString? Da, long? Q, long? MaxLen, PdfArray? Opt);

    sealed class Ctx
    {
        public int Work; public bool Truncated;
        public PdfString? FormDa; public long? FormQ;
        public readonly HashSet<PdfDictionary> Seen = new(ReferenceEqualityComparer.Instance);
        public readonly List<PdfFormFieldDetails> Result = [];
    }

    /// <summary>Terminal fields with typed flags, inherited attributes, choice options, text limits and signature metadata (12.7).</summary>
    public static PdfFormFieldDetailSet GetFormFieldDetails(this PdfDocument doc)
    {
        var f = doc.File;
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary form)
            return new([], false, false, null, null, null, null, null);
        var ctx = new Ctx
        {
            FormDa = f.Resolve(form.Get("DA")) as PdfString,
            FormQ = f.Resolve(form.Get("Q")) is PdfInteger fq ? fq.Value : null,
        };
        if (f.Resolve(form.Get("Fields")) is PdfArray fields)
            foreach (var o in fields.Items)
                Walk(f, o, "", default, 0, ctx);
        return new(
            ctx.Result, ctx.Truncated, f.Resolve(form.Get("XFA")) is not null,
            f.Resolve(form.Get("NeedAppearances")) is PdfBoolean na ? na.Value : null,
            f.Resolve(form.Get("SigFlags")) is PdfInteger sf ? sf.Value : null,
            ctx.FormDa, ctx.FormQ, f.Resolve(form.Get("CO")) as PdfArray);
    }

    static void Walk(PdfFile f, PdfObject? o, string prefix, Inherited inh, int depth, Ctx ctx)
    {
        if (depth > MaxDepth || ++ctx.Work > MaxFields) { ctx.Truncated = true; return; }
        if (f.Resolve(o) is not PdfDictionary d || !ctx.Seen.Add(d)) return;
        var partial = f.Resolve(d.Get("T")) as PdfString;
        var name = partial is null ? prefix : prefix.Length == 0 ? partial.Text : prefix + "." + partial.Text;
        inh = new Inherited(
            f.Resolve(d.Get("FT")) as PdfName ?? inh.Ft,
            d.Get("V") is { } v ? f.Resolve(v) : inh.V,
            d.Get("DV") is { } dv ? f.Resolve(dv) : inh.Dv,
            f.Resolve(d.Get("Ff")) is PdfInteger ff ? ff.Value : inh.Ff,
            f.Resolve(d.Get("DA")) as PdfString ?? inh.Da,
            f.Resolve(d.Get("Q")) is PdfInteger q ? q.Value : inh.Q,
            f.Resolve(d.Get("MaxLen")) is PdfInteger ml ? ml.Value : inh.MaxLen,
            f.Resolve(d.Get("Opt")) as PdfArray ?? inh.Opt);
        var kids = f.Resolve(d.Get("Kids")) as PdfArray;
        if (kids is not null && kids.Items.Any(k => f.Resolve(k) is PdfDictionary kd && kd.Get("T") is not null))
        {
            foreach (var k in kids.Items) Walk(f, k, name, inh, depth + 1, ctx);
            return;
        }
        var widgets = new List<PdfDictionary>();
        if (kids is null) widgets.Add(d);
        else foreach (var k in kids.Items) if (f.Resolve(k) is PdfDictionary w) widgets.Add(w);
        ctx.Result.Add(Build(f, d, name, partial, inh, widgets, ctx));
    }

    static PdfFormFieldDetails Build(PdfFile f, PdfDictionary d, string name, PdfString? partial, Inherited inh, List<PdfDictionary> widgets, Ctx ctx)
    {
        var selected = new List<long>();
        if (f.Resolve(d.Get("I")) is PdfArray ia)
            foreach (var item in ia.Items.Take(MaxOptions))
                if (f.Resolve(item) is PdfInteger n) selected.Add(n.Value);
        var sv = f.Resolve(d.Get("SV")) as PdfDictionary;
        var lk = f.Resolve(d.Get("Lock")) as PdfDictionary;
        var isSig = inh.Ft?.Value == "Sig";
        return new PdfFormFieldDetails(
            name, partial, inh.Ft, inh.V, inh.Dv, inh.Ff,
            inh.Da ?? ctx.FormDa, inh.Da is null && ctx.FormDa is not null,
            inh.Q ?? ctx.FormQ, inh.Q is null && ctx.FormQ is not null,
            inh.MaxLen, ReadOptions(f, inh.Opt),
            f.Resolve(d.Get("TI")) is PdfInteger ti ? ti.Value : null, selected,
            isSig && sv is not null ? ReadSeed(f, sv) : null,
            isSig && lk is not null ? new PdfSignatureFieldLock(f.Resolve(lk.Get("Action")) as PdfName, f.Resolve(lk.Get("Fields")) as PdfArray,
                f.Resolve(lk.Get("P")) is PdfInteger p ? p.Value : null, lk) : null,
            widgets, d);
    }

    static List<PdfFieldOption> ReadOptions(PdfFile f, PdfArray? opt)
    {
        var list = new List<PdfFieldOption>();
        if (opt is null) return list;
        foreach (var item in opt.Items.Take(MaxOptions))
        {
            var r = f.Resolve(item);
            if (r is PdfString s) list.Add(new(s, s, r));
            else if (r is PdfArray pair && pair.Count >= 2)
                list.Add(new(f.Resolve(pair[0]) as PdfString, f.Resolve(pair[1]) as PdfString, r));
            else if (r is not null) list.Add(new(null, null, r));
        }
        return list;
    }

    static PdfSignatureSeedValue ReadSeed(PdfFile f, PdfDictionary sv) => new(
        f.Resolve(sv.Get("Ff")) is PdfInteger ff ? ff.Value : null,
        f.Resolve(sv.Get("Filter")) as PdfName,
        f.Resolve(sv.Get("SubFilter")) as PdfArray,
        f.Resolve(sv.Get("DigestMethod")) as PdfArray,
        f.Resolve(sv.Get("V")) is PdfInteger or PdfReal ? f.Resolve(sv.Get("V")).AsNumber() : null,
        f.Resolve(sv.Get("Cert")) as PdfDictionary,
        f.Resolve(sv.Get("Reasons")) as PdfArray,
        f.Resolve(sv.Get("MDP")) as PdfDictionary,
        f.Resolve(sv.Get("TimeStamp")) as PdfDictionary,
        f.Resolve(sv.Get("LegalAttestation")) as PdfArray,
        f.Resolve(sv.Get("AddRevInfo")) is PdfBoolean b ? b.Value : null,
        sv);
}
