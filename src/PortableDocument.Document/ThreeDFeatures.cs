using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>3D animation style (ISO 32000-1:2008 Table 301). Defaults: Subtype None, PlayCount 0, TimeMultiplier 1. Nothing is animated.</summary>
public sealed record Pdf3DAnimationStyle(string Subtype, long PlayCount, double TimeMultiplier, PdfDictionary Dictionary);

/// <summary>
/// 3D activation dictionary (Table 299). Defaults: A=XA, AIS=L, D=PI, DIS=U, TB=true, NP=false.
/// These are preserved as data only; the annotation is never activated.
/// </summary>
public sealed record Pdf3DActivation(
    string Activate,
    string ArtworkInstanceState,
    string Deactivate,
    string DeactivatedState,
    bool Toolbar,
    bool NavigationPanel,
    PdfDictionary Dictionary);

/// <summary>3D projection dictionary (Table 305). Subtype is O or P; FOV, PS, OS, OB are preserved without computing a projection.</summary>
public sealed record Pdf3DProjection(
    string? Subtype,
    string ClippingStyle,
    PdfObject? Far,
    PdfObject? Near,
    double? FieldOfView,
    PdfObject? ProjectionScale,
    double OrthographicScale,
    string? OrthographicBinding,
    PdfDictionary Dictionary);

/// <summary>3D background dictionary (Table 306). Defaults: Subtype SC, CS DeviceRGB, EA false; the colour array is preserved raw.</summary>
public sealed record Pdf3DBackground(string Subtype, string ColorSpace, PdfArray? Color, bool EntireAnnotation, PdfDictionary Dictionary);

/// <summary>3D render mode dictionary (Tables 307, 308). Defaults: opacity 0.5, crease value 45.</summary>
public sealed record Pdf3DRenderMode(
    string? Subtype,
    PdfArray? AuxiliaryColor,
    PdfObject? FaceColor,
    double Opacity,
    double CreaseValue,
    PdfDictionary Dictionary);

/// <summary>3D lighting scheme dictionary (Tables 309, 310). Only the scheme name is surfaced; lights are not computed.</summary>
public sealed record Pdf3DLightingScheme(string? Subtype, PdfDictionary Dictionary);

/// <summary>
/// 3D cross section dictionary (Table 311). Defaults: opacity 0.5, intersection invisible; centre/orientation/colour arrays
/// are preserved raw (the orientation array may legally contain exactly one null).
/// </summary>
public sealed record Pdf3DCrossSection(
    PdfArray? Center,
    PdfArray? Orientation,
    double Opacity,
    PdfArray? PlaneColor,
    bool IntersectionVisible,
    PdfArray? IntersectionColor,
    PdfDictionary Dictionary);

/// <summary>3D node dictionary (Table 312). Opacity and visibility are null when absent (inherited from the parent node or artwork).</summary>
public sealed record Pdf3DNode(string? Name, double? Opacity, bool? Visible, PdfArray? Matrix, PdfDictionary Dictionary);

/// <summary>3D measurement dictionary (ISO 32000-2 /3DMeasure). Only the subtype is surfaced; the raw dictionary is preserved and no measurement is computed.</summary>
public sealed record Pdf3DMeasure(string? Subtype, PdfDictionary Dictionary);

/// <summary>
/// 3D stream or 3D reference dictionary target (Tables 300, 303). The Subtype name is preserved verbatim (U3D, and PRC in
/// ISO 32000-2) and <see cref="IsRecognizedSubtype"/> reports only whether it is one of those; the stream bytes are never decoded.
/// /OnInstantiate is a JavaScript value preserved opaquely and never executed.
/// </summary>
public sealed record Pdf3DStream(
    string? Subtype,
    bool IsRecognizedSubtype,
    bool ViaReference,
    IReadOnlyList<Pdf3DView> Views,
    PdfObject? DefaultView,
    PdfObject? Resources,
    PdfObject? OnInstantiate,
    Pdf3DAnimationStyle? AnimationStyle,
    PdfStream Stream)
{
    /// <summary>Typed view/detail traversal hit a work limit; the raw stream dictionary is unchanged.</summary>
    public bool Truncated { get; init; }
}

/// <summary>Inert 3D feature readers shared by the 3D annotation and view APIs.</summary>
static class ThreeDFeatures
{
    internal sealed class ReadContext
    {
        int remaining = 1024;
        public bool Truncated;
        public Dictionary<PdfStream, Pdf3DStream> Streams { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<PdfDictionary, Pdf3DView> Views { get; } = new(ReferenceEqualityComparer.Instance);
        public bool Take()
        {
            if (remaining > 0) { remaining--; return true; }
            Truncated = true;
            return false;
        }
    }

    public static Pdf3DStream? ReadStream(PdfFile f, PdfObject? value, ReadContext ctx)
    {
        var resolved = f.Resolve(value);
        var viaReference = false;
        if (resolved is PdfDictionary reference && f.Resolve(reference.Get("Type")).AsName() is null or "3DRef")
        {
            resolved = f.Resolve(reference.Get("3DD"));
            viaReference = true;
        }
        if (resolved is not PdfStream stream) return null;
        if (ctx.Streams.TryGetValue(stream, out var cached))
            return cached.ViaReference == viaReference ? cached : cached with { ViaReference = viaReference };
        if (!ctx.Take()) return new Pdf3DStream(f.Resolve(stream.Dict.Get("Subtype")).AsName(), f.Resolve(stream.Dict.Get("Subtype")).AsName() is "U3D" or "PRC", viaReference, [],
            f.Resolve(stream.Dict.Get("DV")), f.Resolve(stream.Dict.Get("Resources")), f.Resolve(stream.Dict.Get("OnInstantiate")),
            null, stream) { Truncated = true };
        var d = stream.Dict;
        var views = new List<Pdf3DView>();
        if (f.Resolve(d.Get("VA")) is PdfArray array)
            for (var i = 0; i < array.Count; i++)
            {
                if (i >= 64) { ctx.Truncated = true; break; }
                if (DocumentFeatures.Read3DView(f, array[i], ctx) is { } view)
                    views.Add(view);
                else if (ctx.Truncated) break;
            }
        var subtype = f.Resolve(d.Get("Subtype")).AsName();
        var result = new Pdf3DStream(
            subtype,
            subtype is "U3D" or "PRC",
            viaReference,
            views,
            f.Resolve(d.Get("DV")),
            f.Resolve(d.Get("Resources")),
            f.Resolve(d.Get("OnInstantiate")),
            ReadAnimationStyle(f, d.Get("AN")),
            stream) { Truncated = ctx.Truncated };
        ctx.Streams[stream] = result;
        return result;
    }

    public static Pdf3DView? SelectView(PdfFile f, PdfObject? selector, Pdf3DStream? stream, ReadContext ctx)
    {
        var resolved = f.Resolve(selector);
        if (resolved is PdfDictionary) return DocumentFeatures.Read3DView(f, resolved, ctx);
        var views = stream?.Views ?? [];
        switch (resolved)
        {
            case PdfInteger index:
                return index.Value >= 0 && index.Value < views.Count ? views[(int)index.Value] : null;
            case PdfString name:
                return views.FirstOrDefault(v => v.InternalName == name.Text);
            case PdfName { Value: "F" }:
                return views.Count > 0 ? views[0] : null;
            case PdfName { Value: "L" }:
                return views.Count > 0 ? views[^1] : null;
            case PdfName { Value: "D" } when stream is not null:
                return SelectView(f, stream.DefaultView, stream with { DefaultView = null }, ctx) ?? (views.Count > 0 ? views[0] : null);
            default:
                return null;
        }
    }

    public static Pdf3DAnimationStyle? ReadAnimationStyle(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary style) return null;
        var subtype = f.Resolve(style.Get("Subtype")).AsName();
        return new Pdf3DAnimationStyle(
            subtype is "None" or "Linear" or "Oscillating" ? subtype : "None",
            f.Resolve(style.Get("PC")) is PdfInteger count ? count.Value : 0,
            f.Resolve(style.Get("TM")).AsNumber() is > 0 and var multiplier ? multiplier : 1,
            style);
    }

    public static Pdf3DActivation? ReadActivation(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary activation) return null;
        return new Pdf3DActivation(
            Choice(f, activation.Get("A"), "XA", "PO", "PV"),
            Choice(f, activation.Get("AIS"), "L", "I"),
            Choice(f, activation.Get("D"), "PI", "PC", "XD"),
            Choice(f, activation.Get("DIS"), "U", "I", "L"),
            f.Resolve(activation.Get("TB")) is PdfBoolean toolbar ? toolbar.Value : true,
            f.Resolve(activation.Get("NP")) is PdfBoolean panel ? panel.Value : false,
            activation);
    }

    public static Pdf3DProjection? ReadProjection(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary projection) return null;
        var clipping = f.Resolve(projection.Get("CS")).AsName();
        return new Pdf3DProjection(
            f.Resolve(projection.Get("Subtype")).AsName(),
            clipping is "XNF" ? clipping : "ANF",
            f.Resolve(projection.Get("F")),
            f.Resolve(projection.Get("N")),
            f.Resolve(projection.Get("FOV")).AsNumber(),
            f.Resolve(projection.Get("PS")),
            f.Resolve(projection.Get("OS")).AsNumber() is > 0 and var scale ? scale : 1,
            f.Resolve(projection.Get("OB")).AsName(),
            projection);
    }

    public static Pdf3DBackground? ReadBackground(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary background) return null;
        return new Pdf3DBackground(
            f.Resolve(background.Get("Subtype")).AsName() ?? "SC",
            f.Resolve(background.Get("CS")).AsName() ?? "DeviceRGB",
            f.Resolve(background.Get("C")) as PdfArray,
            f.Resolve(background.Get("EA")) is PdfBoolean { Value: true },
            background);
    }

    public static Pdf3DRenderMode? ReadRenderMode(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary mode) return null;
        var face = f.Resolve(mode.Get("FC"));
        return new Pdf3DRenderMode(
            f.Resolve(mode.Get("Subtype")).AsName(),
            f.Resolve(mode.Get("AC")) as PdfArray,
            face is PdfName or PdfArray ? face : null,
            f.Resolve(mode.Get("O")).AsNumber() ?? 0.5,
            f.Resolve(mode.Get("CV")).AsNumber() ?? 45,
            mode);
    }

    public static Pdf3DLightingScheme? ReadLightingScheme(PdfFile f, PdfObject? value) =>
        f.Resolve(value) is PdfDictionary scheme
            ? new Pdf3DLightingScheme(f.Resolve(scheme.Get("Subtype")).AsName(), scheme)
            : null;

    public static IReadOnlyList<Pdf3DMeasure> ReadMeasures(PdfFile f, PdfObject? value, ReadContext ctx) =>
        Each(f, value, ctx, d => new Pdf3DMeasure(f.Resolve(d.Get("Subtype")).AsName(), d));

    public static IReadOnlyList<Pdf3DCrossSection> ReadCrossSections(PdfFile f, PdfObject? value, ReadContext ctx) =>
        Each(f, value, ctx, d => new Pdf3DCrossSection(
            f.Resolve(d.Get("C")) as PdfArray,
            f.Resolve(d.Get("O")) as PdfArray,
            f.Resolve(d.Get("PO")).AsNumber() ?? 0.5,
            f.Resolve(d.Get("PC")) as PdfArray,
            f.Resolve(d.Get("IV")) is PdfBoolean { Value: true },
            f.Resolve(d.Get("IC")) as PdfArray,
            d));

    public static IReadOnlyList<Pdf3DNode> ReadNodes(PdfFile f, PdfObject? value, ReadContext ctx) =>
        Each(f, value, ctx, d => new Pdf3DNode(
            f.Resolve(d.Get("N")) is PdfString name ? name.Text : null,
            f.Resolve(d.Get("O")).AsNumber(),
            f.Resolve(d.Get("V")) is PdfBoolean visible ? visible.Value : null,
            f.Resolve(d.Get("M")) as PdfArray,
            d));

    static IReadOnlyList<T> Each<T>(PdfFile f, PdfObject? value, ReadContext ctx, Func<PdfDictionary, T> read)
    {
        var list = new List<T>();
        if (f.Resolve(value) is PdfArray array)
            for (var i = 0; i < array.Count; i++)
            {
                if (i >= 256) { ctx.Truncated = true; break; }
                if (!ctx.Take()) break;
                if (f.Resolve(array[i]) is PdfDictionary d)
                    list.Add(read(d));
            }
        return list;
    }

    static string Choice(PdfFile f, PdfObject? value, string fallback, params string[] allowed)
    {
        var name = f.Resolve(value).AsName();
        return name is not null && Array.IndexOf(allowed, name) >= 0 ? name : fallback;
    }
}
