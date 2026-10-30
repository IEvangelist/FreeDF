using PortableDocument.Core;

namespace PortableDocument.Document;

// Inert typed views of annotation dictionaries (ISO 32000-2:2020 12.5.2 common entries and 12.5.6 subtype entries).
// Citations are by clause; table numbers are intentionally not cited because the primary text could not be consulted
// while writing this. No action, popup, media, attachment, trap, redaction, or appearance is ever executed or applied.
// Missing or wrong-typed entries are null; documented defaults are exposed as Effective* members. The original
// dictionary is always retained unchanged on PdfTypedAnnotation.Dictionary.

/// <summary>A bounded numeric array (QuadPoints, Vertices, L, RD, ...). Non-numeric elements are skipped and flagged; the raw array stays in the dictionary.</summary>
public sealed record PdfNumberList(IReadOnlyList<double> Values, bool Truncated, bool HasNonNumeric);

[Flags]
public enum PdfAnnotationFlags : long
{
    None = 0, Invisible = 1, Hidden = 2, Print = 4, NoZoom = 8, NoRotate = 16, NoView = 32,
    ReadOnly = 64, Locked = 128, ToggleNoView = 256, LockedContents = 512,
}

/// <summary>Entries common to all annotations (12.5.2).</summary>
public sealed record PdfAnnotationCommon(
    PdfName? Subtype,
    PdfRect? Rect,
    PdfString? Contents,
    PdfString? Name,
    PdfObject? Modified,
    long Flags,
    PdfDictionary? AppearanceDictionary,
    PdfName? AppearanceState,
    PdfArray? Border,
    PdfArray? Color,
    PdfDictionary? BorderStyle,
    long? StructParent,
    PdfDictionary? OptionalContent,
    PdfDictionary? Page)
{
    public PdfAnnotationFlags FlagSet => (PdfAnnotationFlags)Flags;
}

/// <summary>Entries common to markup annotations (12.5.6.2). /IRT is only resolved, never followed.</summary>
public sealed record PdfMarkupInfo(
    PdfString? Title, PdfDictionary? Popup, double? Opacity, PdfObject? RichContents, PdfObject? CreationDate,
    PdfDictionary? InReplyTo, PdfString? Subject, PdfName? ReplyType, PdfName? Intent, PdfDictionary? ExData)
{
    public double EffectiveOpacity => Opacity ?? 1.0;
    public string EffectiveReplyType => ReplyType?.Value ?? "R";
}

public sealed record PdfTextAnnotation(bool? Open, PdfName? IconName, PdfString? State, PdfString? StateModel)
{
    public bool EffectiveOpen => Open ?? false;
    public string EffectiveIconName => IconName?.Value ?? "Note";
}

/// <summary>Link (12.5.6.5). /A and /PA are exposed raw; their typed forms come from GetActions and are never followed.</summary>
public sealed record PdfLinkAnnotation(PdfObject? Destination, PdfName? Highlight, PdfObject? Action, PdfObject? UriAction, PdfNumberList? QuadPoints, PdfDictionary? BorderStyle)
{
    public string EffectiveHighlight => Highlight?.Value ?? "I";
}

public sealed record PdfFreeTextAnnotation(
    PdfString? DefaultAppearance, long? Quadding, PdfString? DefaultStyle, PdfNumberList? CalloutLine,
    PdfDictionary? BorderEffect, PdfNumberList? Differences, PdfDictionary? BorderStyle, PdfName? LineEnding)
{
    public long EffectiveQuadding => Quadding ?? 0;
    public string EffectiveLineEnding => LineEnding?.Value ?? "None";
}

public sealed record PdfLineAnnotation(
    PdfNumberList? Line, PdfDictionary? BorderStyle, PdfArray? LineEndings, PdfArray? InteriorColor, double? LeaderLength,
    double? LeaderExtension, bool? Caption, double? LeaderOffset, PdfName? CaptionPosition, PdfDictionary? Measure, PdfNumberList? CaptionOffset)
{
    public double EffectiveLeaderLength => LeaderLength ?? 0;
    public double EffectiveLeaderExtension => LeaderExtension ?? 0;
    public bool EffectiveCaption => Caption ?? false;
    public double EffectiveLeaderOffset => LeaderOffset ?? 0;
    public string EffectiveCaptionPosition => CaptionPosition?.Value ?? "Inline";
}

/// <summary>Square and Circle (12.5.6.8).</summary>
public sealed record PdfShapeAnnotation(PdfDictionary? BorderStyle, PdfArray? InteriorColor, PdfDictionary? BorderEffect, PdfNumberList? Differences);

/// <summary>Polygon and PolyLine (12.5.6.9). /Path (PDF 2.0) is retained as a raw array.</summary>
public sealed record PdfPolyAnnotation(
    PdfNumberList? Vertices, PdfArray? LineEndings, PdfDictionary? BorderStyle, PdfArray? InteriorColor,
    PdfDictionary? BorderEffect, PdfDictionary? Measure, PdfArray? Path);

/// <summary>Highlight, Underline, Squiggly and StrikeOut (12.5.6.10).</summary>
public sealed record PdfTextMarkupAnnotation(PdfNumberList? QuadPoints);

public sealed record PdfCaretAnnotation(PdfNumberList? Differences, PdfName? Symbol)
{
    public string EffectiveSymbol => Symbol?.Value ?? "None";
}

public sealed record PdfStampAnnotation(PdfName? IconName)
{
    public string EffectiveIconName => IconName?.Value ?? "Draft";
}

/// <summary>Ink (12.5.6.13). The ink list is bounded; <see cref="Truncated"/> reports a cut.</summary>
public sealed record PdfInkAnnotation(IReadOnlyList<PdfNumberList> InkList, bool Truncated, PdfDictionary? BorderStyle);

public sealed record PdfPopupAnnotation(PdfDictionary? Parent, bool? Open)
{
    public bool EffectiveOpen => Open ?? false;
}

public sealed record PdfFileAttachmentAnnotation(PdfObject? FileSpecification, PdfName? IconName)
{
    public string EffectiveIconName => IconName?.Value ?? "PushPin";
}

public sealed record PdfSoundAnnotationKeys(PdfStream? Sound, PdfName? IconName)
{
    public string EffectiveIconName => IconName?.Value ?? "Speaker";
}

/// <summary>Movie (12.5.6.17, deprecated in 2.0). /A is a boolean or activation dictionary; absent means true.</summary>
public sealed record PdfMovieAnnotationKeys(PdfString? Title, PdfDictionary? Movie, PdfObject? Activation);

public sealed record PdfScreenAnnotationKeys(PdfString? Title, PdfDictionary? AppearanceCharacteristics, PdfObject? Action, PdfDictionary? AdditionalActions);

public sealed record PdfWidgetAnnotation(PdfName? Highlight, PdfDictionary? AppearanceCharacteristics, PdfObject? Action, PdfDictionary? AdditionalActions, PdfDictionary? BorderStyle, PdfDictionary? Parent)
{
    public string EffectiveHighlight => Highlight?.Value ?? "I";
}

public sealed record PdfPrinterMarkAnnotation(PdfName? MarkName);

public sealed record PdfTrapNetAnnotation(PdfObject? LastModified, PdfArray? Version, PdfArray? AnnotStates, PdfArray? FontFauxing);

/// <summary>Watermark (12.5.6.22). Fixed-print matrix and translations are exposed typed.</summary>
public sealed record PdfWatermarkAnnotation(PdfDictionary? FixedPrint, PdfNumberList? Matrix, double? HorizontalTranslation, double? VerticalTranslation)
{
    public double EffectiveHorizontalTranslation => HorizontalTranslation ?? 0;
    public double EffectiveVerticalTranslation => VerticalTranslation ?? 0;
}

/// <summary>Redact (12.5.6.23). Nothing is redacted; the overlay stream is only referenced.</summary>
public sealed record PdfRedactAnnotation(
    PdfNumberList? QuadPoints, PdfArray? InteriorColor, PdfStream? OverlayAppearance, PdfString? OverlayText,
    bool? Repeat, PdfString? DefaultAppearance, long? Quadding)
{
    public bool EffectiveRepeat => Repeat ?? false;
    public long EffectiveQuadding => Quadding ?? 0;
}

public sealed record PdfTypedAnnotation(PdfAnnotationCommon Common, PdfMarkupInfo? Markup, PdfDictionary Dictionary)
{
    public PdfTextAnnotation? Text { get; init; }
    public PdfLinkAnnotation? Link { get; init; }
    public PdfFreeTextAnnotation? FreeText { get; init; }
    public PdfLineAnnotation? Line { get; init; }
    public PdfShapeAnnotation? Shape { get; init; }
    public PdfPolyAnnotation? Poly { get; init; }
    public PdfTextMarkupAnnotation? TextMarkup { get; init; }
    public PdfCaretAnnotation? Caret { get; init; }
    public PdfStampAnnotation? Stamp { get; init; }
    public PdfInkAnnotation? Ink { get; init; }
    public PdfPopupAnnotation? Popup { get; init; }
    public PdfFileAttachmentAnnotation? FileAttachment { get; init; }
    public PdfSoundAnnotationKeys? Sound { get; init; }
    public PdfMovieAnnotationKeys? Movie { get; init; }
    public PdfScreenAnnotationKeys? Screen { get; init; }
    public PdfWidgetAnnotation? Widget { get; init; }
    public PdfPrinterMarkAnnotation? PrinterMark { get; init; }
    public PdfTrapNetAnnotation? TrapNet { get; init; }
    public PdfWatermarkAnnotation? Watermark { get; init; }
    public PdfRedactAnnotation? Redact { get; init; }
}

/// <summary>Typed annotations of a page; Truncated is true when the annotation or numeric work budget stopped the read early.</summary>
public sealed record PdfAnnotationDetailSet(IReadOnlyList<PdfTypedAnnotation> Annotations, bool Truncated);

public static class AnnotationDetailFeatures
{
    public const int MaxAnnotations = 100_000;
    public const int MaxNumbersPerArray = 65_536;
    public const int MaxTotalNumbers = 1_000_000;
    const int MaxInkStrokes = 4096;

    sealed class Ctx { public int Numbers; public bool Truncated; }

    /// <summary>Typed, inert views of a page's /Annots entries (12.5). Nothing referenced by an annotation is followed or run.</summary>
    public static PdfAnnotationDetailSet GetAnnotationDetails(this PdfPage page)
    {
        var f = page.Document.File;
        var ctx = new Ctx();
        var result = new List<PdfTypedAnnotation>();
        if (f.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return new(result, false);
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var o in annots.Items)
        {
            if (result.Count >= MaxAnnotations) { ctx.Truncated = true; break; }
            if (f.Resolve(o) is PdfDictionary d && seen.Add(d)) result.Add(Read(f, d, ctx));
        }
        return new(result, ctx.Truncated);
    }

    static PdfTypedAnnotation Read(PdfFile f, PdfDictionary d, Ctx ctx)
    {
        var subtype = f.Resolve(d.Get("Subtype")) as PdfName;
        var common = new PdfAnnotationCommon(
            subtype, ReadRect(f, d.Get("Rect")), Str(f, d, "Contents"), Str(f, d, "NM"), f.Resolve(d.Get("M")) is PdfString or PdfName ? f.Resolve(d.Get("M")) : null,
            Int(f, d, "F") ?? 0, Dict(f, d, "AP"), Name(f, d, "AS"), Arr(f, d, "Border"), Arr(f, d, "C"), Dict(f, d, "BS"),
            Int(f, d, "StructParent"), Dict(f, d, "OC"), Dict(f, d, "P"));
        var s = subtype?.Value;
        var markup = s is "Text" or "FreeText" or "Line" or "Square" or "Circle" or "Polygon" or "PolyLine" or "Highlight" or "Underline"
            or "Squiggly" or "StrikeOut" or "Caret" or "Stamp" or "Ink" or "FileAttachment" or "Sound" or "Redact" or "Projection"
            ? new PdfMarkupInfo(Str(f, d, "T"), Dict(f, d, "Popup"), Num(f, d, "CA"), f.Resolve(d.Get("RC")) is PdfString or PdfStream ? f.Resolve(d.Get("RC")) : null,
                f.Resolve(d.Get("CreationDate")) as PdfString, Dict(f, d, "IRT"), Str(f, d, "Subj"), Name(f, d, "RT"), Name(f, d, "IT"), Dict(f, d, "ExData"))
            : null;
        var a = new PdfTypedAnnotation(common, markup, d);
        switch (s)
        {
            case "Text":
                return a with { Text = new(Bool(f, d, "Open"), Name(f, d, "Name"), Str(f, d, "State"), Str(f, d, "StateModel")) };
            case "Link":
                return a with { Link = new(Dest(f, d), Name(f, d, "H"), f.Resolve(d.Get("A")), f.Resolve(d.Get("PA")), Nums(f, d, "QuadPoints", ctx), Dict(f, d, "BS")) };
            case "FreeText":
                return a with { FreeText = new(Str(f, d, "DA"), Int(f, d, "Q"), Str(f, d, "DS"), Nums(f, d, "CL", ctx), Dict(f, d, "BE"), Nums(f, d, "RD", ctx), Dict(f, d, "BS"), Name(f, d, "LE")) };
            case "Line":
                return a with { Line = new(Nums(f, d, "L", ctx), Dict(f, d, "BS"), Arr(f, d, "LE"), Arr(f, d, "IC"), Num(f, d, "LL"), Num(f, d, "LLE"), Bool(f, d, "Cap"), Num(f, d, "LLO"), Name(f, d, "CP"), Dict(f, d, "Measure"), Nums(f, d, "CO", ctx)) };
            case "Square" or "Circle":
                return a with { Shape = new(Dict(f, d, "BS"), Arr(f, d, "IC"), Dict(f, d, "BE"), Nums(f, d, "RD", ctx)) };
            case "Polygon" or "PolyLine":
                return a with { Poly = new(Nums(f, d, "Vertices", ctx), Arr(f, d, "LE"), Dict(f, d, "BS"), Arr(f, d, "IC"), Dict(f, d, "BE"), Dict(f, d, "Measure"), Arr(f, d, "Path")) };
            case "Highlight" or "Underline" or "Squiggly" or "StrikeOut":
                return a with { TextMarkup = new(Nums(f, d, "QuadPoints", ctx)) };
            case "Caret":
                return a with { Caret = new(Nums(f, d, "RD", ctx), Name(f, d, "Sy")) };
            case "Stamp":
                return a with { Stamp = new(Name(f, d, "Name")) };
            case "Ink":
                return a with { Ink = ReadInk(f, d, ctx) };
            case "Popup":
                return a with { Popup = new(Dict(f, d, "Parent"), Bool(f, d, "Open")) };
            case "FileAttachment":
                return a with { FileAttachment = new(f.Resolve(d.Get("FS")) is PdfString or PdfDictionary ? f.Resolve(d.Get("FS")) : null, Name(f, d, "Name")) };
            case "Sound":
                return a with { Sound = new(f.Resolve(d.Get("Sound")) as PdfStream, Name(f, d, "Name")) };
            case "Movie":
                return a with { Movie = new(Str(f, d, "T"), Dict(f, d, "Movie"), f.Resolve(d.Get("A")) is PdfBoolean or PdfDictionary ? f.Resolve(d.Get("A")) : null) };
            case "Screen":
                return a with { Screen = new(Str(f, d, "T"), Dict(f, d, "MK"), f.Resolve(d.Get("A")), Dict(f, d, "AA")) };
            case "Widget":
                return a with { Widget = new(Name(f, d, "H"), Dict(f, d, "MK"), f.Resolve(d.Get("A")), Dict(f, d, "AA"), Dict(f, d, "BS"), Dict(f, d, "Parent")) };
            case "PrinterMark":
                return a with { PrinterMark = new(Name(f, d, "MN")) };
            case "TrapNet":
                return a with { TrapNet = new(f.Resolve(d.Get("LastModified")) as PdfString, Arr(f, d, "Version"), Arr(f, d, "AnnotStates"), Arr(f, d, "FontFauxing")) };
            case "Watermark":
                var fp = Dict(f, d, "FixedPrint");
                return a with { Watermark = new(fp, fp is null ? null : Nums(f, fp, "Matrix", ctx), fp is null ? null : Num(f, fp, "H"), fp is null ? null : Num(f, fp, "V")) };
            case "Redact":
                return a with { Redact = new(Nums(f, d, "QuadPoints", ctx), Arr(f, d, "IC"), f.Resolve(d.Get("RO")) as PdfStream, Str(f, d, "OverlayText"), Bool(f, d, "Repeat"), Str(f, d, "DA"), Int(f, d, "Q")) };
            default:
                return a;
        }
    }

    static PdfInkAnnotation ReadInk(PdfFile f, PdfDictionary d, Ctx ctx)
    {
        var strokes = new List<PdfNumberList>();
        var truncated = false;
        if (f.Resolve(d.Get("InkList")) is PdfArray list)
        {
            foreach (var item in list.Items)
            {
                if (strokes.Count >= MaxInkStrokes) { truncated = true; ctx.Truncated = true; break; }
                if (f.Resolve(item) is PdfArray stroke) strokes.Add(ReadNumbers(f, stroke, ctx));
            }
        }
        return new PdfInkAnnotation(strokes, truncated, Dict(f, d, "BS"));
    }

    static PdfNumberList? Nums(PdfFile f, PdfDictionary d, string key, Ctx ctx) =>
        f.Resolve(d.Get(key)) is PdfArray a ? ReadNumbers(f, a, ctx) : null;

    static PdfNumberList ReadNumbers(PdfFile f, PdfArray a, Ctx ctx)
    {
        var values = new List<double>();
        var truncated = false; var bad = false;
        foreach (var item in a.Items)
        {
            if (values.Count >= MaxNumbersPerArray || ctx.Numbers >= MaxTotalNumbers) { truncated = true; ctx.Truncated = true; break; }
            var r = f.Resolve(item);
            if (r is PdfInteger or PdfReal) { values.Add(r.AsNumber()!.Value); ctx.Numbers++; }
            else bad = true;
        }
        return new PdfNumberList(values, truncated, bad);
    }

    static PdfRect? ReadRect(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfArray r || r.Count != 4) return null;
        var v = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (f.Resolve(r[i]) is not (PdfInteger or PdfReal)) return null;
            v[i] = f.Resolve(r[i]).AsNumber()!.Value;
        }
        return new PdfRect(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]));
    }

    static PdfObject? Dest(PdfFile f, PdfDictionary d) =>
        f.Resolve(d.Get("Dest")) is PdfName or PdfString or PdfArray ? f.Resolve(d.Get("Dest")) : null;

    static PdfString? Str(PdfFile f, PdfDictionary d, string k) => f.Resolve(d.Get(k)) as PdfString;
    static PdfName? Name(PdfFile f, PdfDictionary d, string k) => f.Resolve(d.Get(k)) as PdfName;
    static PdfDictionary? Dict(PdfFile f, PdfDictionary d, string k) => f.Resolve(d.Get(k)) as PdfDictionary;
    static PdfArray? Arr(PdfFile f, PdfDictionary d, string k) => f.Resolve(d.Get(k)) as PdfArray;
    static bool? Bool(PdfFile f, PdfDictionary d, string k) => f.Resolve(d.Get(k)) is PdfBoolean b ? b.Value : null;
    static long? Int(PdfFile f, PdfDictionary d, string k) => f.Resolve(d.Get(k)) is PdfInteger i ? i.Value : null;
    static double? Num(PdfFile f, PdfDictionary d, string k) => f.Resolve(d.Get(k)) is PdfInteger or PdfReal ? f.Resolve(d.Get(k)).AsNumber() : null;
}
