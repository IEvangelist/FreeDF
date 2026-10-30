using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Inert minimum-bit-depth metadata (ISO 32000-1:2008 Table 269; ISO 32000-2 13.2.3 equivalent).
/// <see cref="Value"/> is null when /V is missing or not an integer; <see cref="Monitor"/> defaults to 0.
/// </summary>
public sealed record PdfMediaMinBitDepth(long? Value, long Monitor, PdfDictionary Dictionary);

/// <summary>Inert minimum-screen-size metadata (Table 270). Width/Height are null unless /V is a two-integer array.</summary>
public sealed record PdfMediaMinScreenSize(long? Width, long? Height, long Monitor, PdfDictionary Dictionary);

/// <summary>
/// Inert software identifier (Table 292). The URI, version arrays, and OS list are preserved as data;
/// no software lookup or matching is performed. Defaults: LI and HI true, H empty, OS empty.
/// </summary>
public sealed record PdfSoftwareIdentifier(
    string? Uri,
    PdfArray? LowVersion,
    bool LowInclusive,
    PdfArray? HighVersion,
    bool HighInclusive,
    PdfArray? OperatingSystems,
    PdfDictionary Dictionary);

/// <summary>
/// Inert media criteria (Table 268, /MediaCriteria). Nothing is evaluated against the host; the values
/// are preserved so callers can inspect them. Unknown or wrong-typed entries are null and remain in <see cref="Dictionary"/>.
/// </summary>
public sealed record PdfMediaCriteria(
    bool? AudioDescriptions,
    bool? Captions,
    bool? Overdubs,
    bool? Subtitles,
    long? MinBandwidth,
    PdfMediaMinBitDepth? MinBitDepth,
    PdfMediaMinScreenSize? MinScreenSize,
    IReadOnlyList<PdfSoftwareIdentifier> Viewers,
    PdfArray? PdfVersions,
    PdfArray? Languages,
    PdfDictionary Dictionary);

/// <summary>Inert media player info (Table 291): a software identifier plus the raw dictionary.</summary>
public sealed record PdfMediaPlayerInfo(PdfSoftwareIdentifier? Software, PdfDictionary Dictionary);

/// <summary>Inert media players dictionary (Table 290): MU (must use), A (alternates), NU (never use). Players are never launched.</summary>
public sealed record PdfMediaPlayers(
    IReadOnlyList<PdfMediaPlayerInfo> MustUse,
    IReadOnlyList<PdfMediaPlayerInfo> Alternates,
    IReadOnlyList<PdfMediaPlayerInfo> NotUse,
    PdfDictionary Dictionary);

/// <summary>Inert media offset (Tables 285-288): subtype T (time, in seconds), F (frame) or M (marker).</summary>
public sealed record PdfMediaOffset(string? Subtype, double? Seconds, long? Frame, string? Marker, PdfDictionary Dictionary);

/// <summary>Inert media duration (Table 281, Table 289): S is I (intrinsic), F (infinite) or T (explicit seconds).</summary>
public sealed record PdfMediaDuration(string? Subtype, double? Seconds, PdfDictionary Dictionary);

/// <summary>
/// Inert media clip (Tables 273-278). Subtype is MCD (clip data) or MCS (clip section).
/// For MCD, <see cref="Data"/> preserves the /D file specification or form XObject without opening or decoding it.
/// For MCS, <see cref="Next"/> is the next-level clip (bounded and cycle safe) and the B/E offsets are preserved per MH/BE.
/// <see cref="TemporaryFilePermission"/> is the /P /TF value, defaulting to TEMPNEVER when absent, unrecognized, or malformed (Table 275).
/// </summary>
public sealed record PdfMediaClip(
    string? Subtype,
    string? Name,
    PdfObject? Data,
    string? ContentType,
    string TemporaryFilePermission,
    PdfDictionary? Permissions,
    PdfMediaPlayers? Players,
    string? MustHonorBaseUrl,
    string? BestEffortBaseUrl,
    PdfMediaClip? Next,
    PdfMediaOffset? MustHonorBegin,
    PdfMediaOffset? MustHonorEnd,
    PdfMediaOffset? BestEffortBegin,
    PdfMediaOffset? BestEffortEnd,
    PdfDictionary Dictionary)
{
    /// <summary>True when the section chain hit the depth or node budget while this clip was read; the raw dictionaries remain inspectable.</summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Inert play behavior from a media play parameters MH or BE dictionary (Table 280), using spec defaults
/// (volume 100, no controller, fit 5, autoplay true, repeat count 1.0) when entries are absent or wrong-typed.
/// </summary>
public sealed record PdfMediaPlayBehavior(
    long Volume,
    bool ShowController,
    long Fit,
    PdfMediaDuration? Duration,
    bool AutoPlay,
    double RepeatCount,
    PdfDictionary? Dictionary);

/// <summary>Inert media play parameters (Table 279): players plus must-honor and best-effort behavior. Nothing is played.</summary>
public sealed record PdfMediaPlayParameters(
    PdfMediaPlayers? Players,
    PdfMediaPlayBehavior MustHonor,
    PdfMediaPlayBehavior BestEffort,
    PdfDictionary Dictionary);

/// <summary>
/// Inert screen behavior from a media screen parameters MH or BE dictionary (Table 283).
/// Defaults: window type 3 (annotation rectangle), opacity 1.0, monitor 0. The floating window dictionary (Table 284) is preserved raw.
/// </summary>
public sealed record PdfMediaScreenBehavior(
    long WindowType,
    PdfArray? Background,
    double Opacity,
    long Monitor,
    PdfDictionary? FloatingWindow,
    PdfDictionary? Dictionary);

/// <summary>Inert media screen parameters (Table 282). No window is created.</summary>
public sealed record PdfMediaScreenParameters(
    PdfMediaScreenBehavior MustHonor,
    PdfMediaScreenBehavior BestEffort,
    PdfDictionary Dictionary);

/// <summary>
/// Inert rendition object (Tables 266, 267, 271, 272). Subtype is MR (media) or SR (selector).
/// Selector alternatives are read depth- and cycle-bounded in <see cref="Renditions"/>. Nothing is selected, evaluated or played.
/// </summary>
public sealed record PdfRendition(
    string? Subtype,
    string? Name,
    PdfMediaCriteria? MustHonorCriteria,
    PdfMediaCriteria? BestEffortCriteria,
    PdfMediaClip? Clip,
    PdfMediaPlayParameters? PlayParameters,
    PdfMediaScreenParameters? ScreenParameters,
    IReadOnlyList<PdfRendition> Renditions,
    PdfDictionary Dictionary)
{
    /// <summary>
    /// True when a depth (64) or total-node (1024 per top-level read) budget was hit while reading this rendition or its
    /// descendants; omitted children remain available through the raw dictionaries. Shared children are read once and reused.
    /// </summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Inert rendition action (ISO 32000-1:2008 Table 214; ISO 32000-2 12.6.4.13). <see cref="Operation"/> is the raw /OP
/// integer (0 play, 1 stop, 2 pause, 3 resume, 4 play-or-resume) and is never carried out. /JS is preserved opaquely and never run.
/// </summary>
public sealed record PdfRenditionAction(
    PdfActionScope Scope,
    string Trigger,
    int? PageIndex,
    int? AnnotationIndex,
    long? Operation,
    PdfRendition? Rendition,
    PdfDictionary? ScreenAnnotation,
    PdfObject? JavaScript,
    PdfDictionary Dictionary);

/// <summary>Inert screen annotation (Table 187). The /A and /AA values are preserved raw; no action is triggered.</summary>
public sealed record PdfScreenAnnotation(
    int PageIndex,
    int AnnotationIndex,
    string? Title,
    PdfDictionary? AppearanceCharacteristics,
    PdfObject? Action,
    PdfDictionary? AdditionalActions,
    PdfDictionary Dictionary);

/// <summary>Inert legacy sound object (Table 294). Samples are never decoded. Defaults: 1 channel, 8 bits, Raw encoding.</summary>
public sealed record PdfSound(
    double? SamplingRate,
    long Channels,
    long BitsPerSample,
    string Encoding,
    string? Compression,
    PdfObject? CompressionParameters,
    PdfStream Stream);

/// <summary>Inert legacy sound annotation (Table 185); the icon name defaults to Speaker.</summary>
public sealed record PdfSoundAnnotation(
    int PageIndex,
    int AnnotationIndex,
    PdfSound? Sound,
    string IconName,
    PdfDictionary Dictionary);

/// <summary>Inert legacy movie dictionary (Table 295). The movie file is never opened.</summary>
public sealed record PdfMovie(
    PdfObject? File,
    PdfArray? Aspect,
    long Rotate,
    PdfObject? Poster,
    PdfDictionary Dictionary);

/// <summary>
/// Inert legacy movie activation (Table 296). Defaults: rate 1.0, volume 1.0, no controls, mode Once, asynchronous, position [0.5 0.5].
/// Start/Duration are preserved raw because they may be integers, byte strings or two-element arrays.
/// </summary>
public sealed record PdfMovieActivation(
    PdfObject? Start,
    PdfObject? Duration,
    double Rate,
    double Volume,
    bool ShowControls,
    string Mode,
    bool Synchronous,
    PdfArray? FloatingWindowScale,
    PdfArray? FloatingWindowPosition,
    PdfDictionary Dictionary);

/// <summary>Inert legacy movie annotation (Table 186). /A true/false is surfaced as <see cref="PlayOnActivation"/> (default true) with details only for a dictionary.</summary>
public sealed record PdfMovieAnnotation(
    int PageIndex,
    int AnnotationIndex,
    string? Title,
    PdfMovie? Movie,
    bool PlayOnActivation,
    PdfMovieActivation? Activation,
    PdfDictionary Dictionary);

/// <summary>
/// Inert typed views of multimedia structures (ISO 32000-2 clause 13.2; Table numbers cited are those of the
/// equivalent ISO 32000-1:2008 text). Traversal is bounded and cycle safe. Nothing is played, launched, streamed,
/// fetched, decoded, or evaluated: media criteria are not matched, players are not looked up, JavaScript is not run.
/// Typed values use spec defaults when entries are absent or malformed; the raw dictionary always remains available.
/// </summary>
public static class MultimediaFeatures
{
    const int MaxDepth = 64;

    /// <summary>Returns rendition actions (including chained actions) with typed rendition metadata.</summary>
    public static IReadOnlyList<PdfRenditionAction> GetRenditionActions(this PdfDocument doc)
    {
        var f = doc.File;
        var result = new List<PdfRenditionAction>();
        foreach (var action in doc.GetActions())
        {
            if (action.ActionType != "Rendition") continue;
            var d = action.Dictionary;
            result.Add(new PdfRenditionAction(
                action.Scope,
                action.Trigger,
                action.PageIndex,
                action.AnnotationIndex,
                f.Resolve(d.Get("OP")) is PdfInteger op ? op.Value : null,
                ReadRendition(f, d.Get("R")),
                f.Resolve(d.Get("AN")) as PdfDictionary,
                f.Resolve(d.Get("JS")),
                d));
        }
        return result;
    }

    /// <summary>Returns screen annotations without triggering their actions.</summary>
    public static IReadOnlyList<PdfScreenAnnotation> GetScreenAnnotations(this PdfDocument doc)
    {
        var f = doc.File;
        var result = new List<PdfScreenAnnotation>();
        foreach (var (pageIndex, annotationIndex, annotation) in Annotations(doc, "Screen"))
            result.Add(new PdfScreenAnnotation(
                pageIndex,
                annotationIndex,
                Text(f, annotation.Get("T")),
                f.Resolve(annotation.Get("MK")) as PdfDictionary,
                f.Resolve(annotation.Get("A")),
                f.Resolve(annotation.Get("AA")) as PdfDictionary,
                annotation));
        return result;
    }

    /// <summary>Returns legacy sound annotations; sample data is never decoded or played.</summary>
    public static IReadOnlyList<PdfSoundAnnotation> GetSoundAnnotations(this PdfDocument doc)
    {
        var f = doc.File;
        var result = new List<PdfSoundAnnotation>();
        foreach (var (pageIndex, annotationIndex, annotation) in Annotations(doc, "Sound"))
        {
            var sound = f.Resolve(annotation.Get("Sound")) as PdfStream;
            result.Add(new PdfSoundAnnotation(
                pageIndex,
                annotationIndex,
                sound is null ? null : ReadSound(f, sound),
                f.Resolve(annotation.Get("Name")).AsName() ?? "Speaker",
                annotation));
        }
        return result;
    }

    /// <summary>Returns legacy movie annotations; movie files are never opened or played.</summary>
    public static IReadOnlyList<PdfMovieAnnotation> GetMovieAnnotations(this PdfDocument doc)
    {
        var f = doc.File;
        var result = new List<PdfMovieAnnotation>();
        foreach (var (pageIndex, annotationIndex, annotation) in Annotations(doc, "Movie"))
        {
            var activation = f.Resolve(annotation.Get("A"));
            result.Add(new PdfMovieAnnotation(
                pageIndex,
                annotationIndex,
                Text(f, annotation.Get("T")),
                f.Resolve(annotation.Get("Movie")) is PdfDictionary movie ? ReadMovie(f, movie) : null,
                activation is not PdfBoolean { Value: false },
                activation is PdfDictionary activationDictionary ? ReadMovieActivation(f, activationDictionary) : null,
                annotation));
        }
        return result;
    }

    /// <summary>Reads a rendition dictionary (or a selector tree) as inert metadata; returns null when the value is not a dictionary.</summary>
    public static PdfRendition? ReadRendition(this PdfFile f, PdfObject? value) =>
        ReadRendition(f, value, 0, new ReadContext());

    /// <summary>Per top-level read: shared node budget plus memoization so shared (DAG) children are read once.</summary>
    sealed class ReadContext
    {
        public const int MaxNodes = 1024;
        public int Nodes;
        public bool Truncated;
        public readonly Dictionary<PdfDictionary, PdfRendition?> Renditions = new(ReferenceEqualityComparer.Instance);
        public readonly Dictionary<PdfDictionary, PdfMediaClip?> Clips = new(ReferenceEqualityComparer.Instance);

        public bool TryTake()
        {
            if (Nodes < MaxNodes) { Nodes++; return true; }
            Truncated = true;
            return false;
        }
    }

    static IEnumerable<(int Page, int Index, PdfDictionary Annotation)> Annotations(PdfDocument doc, string subtype)
    {
        var f = doc.File;
        for (var pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            if (f.Resolve(doc.Pages[pageIndex].Dict.Get("Annots")) is not PdfArray annotations) continue;
            for (var i = 0; i < annotations.Count; i++)
                if (f.Resolve(annotations[i]) is PdfDictionary annotation
                    && f.Resolve(annotation.Get("Subtype")).AsName() == subtype)
                    yield return (pageIndex, i, annotation);
        }
    }

    static PdfRendition? ReadRendition(PdfFile f, PdfObject? value, int depth, ReadContext ctx)
    {
        if (f.Resolve(value) is not PdfDictionary rendition) return null;
        if (ctx.Renditions.TryGetValue(rendition, out var cached)) return cached;
        if (depth > MaxDepth) { ctx.Truncated = true; return null; }
        if (!ctx.TryTake()) return null;
        ctx.Renditions[rendition] = null; // in-progress marker: a cycle resolves to null
        var mustHonor = f.Resolve(rendition.Get("MH")) as PdfDictionary;
        var bestEffort = f.Resolve(rendition.Get("BE")) as PdfDictionary;
        var alternatives = new List<PdfRendition>();
        if (f.Resolve(rendition.Get("R")) is PdfArray array)
            foreach (var item in array.Items)
                if (ReadRendition(f, item, depth + 1, ctx) is { } child)
                    alternatives.Add(child);
        var result = new PdfRendition(
            f.Resolve(rendition.Get("S")).AsName(),
            Text(f, rendition.Get("N")),
            ReadCriteria(f, mustHonor?.Get("C")),
            ReadCriteria(f, bestEffort?.Get("C")),
            ReadClip(f, rendition.Get("C"), 0, ctx),
            ReadPlayParameters(f, rendition.Get("P")),
            ReadScreenParameters(f, rendition.Get("SP")),
            alternatives,
            rendition)
        {
            Truncated = ctx.Truncated,
        };
        ctx.Renditions[rendition] = result;
        return result;
    }

    static PdfMediaCriteria? ReadCriteria(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary criteria) return null;
        var viewers = new List<PdfSoftwareIdentifier>();
        if (f.Resolve(criteria.Get("V")) is PdfArray array)
            foreach (var item in array.Items)
                if (ReadSoftware(f, item) is { } software)
                    viewers.Add(software);
        var depth = f.Resolve(criteria.Get("D")) as PdfDictionary;
        var size = f.Resolve(criteria.Get("Z")) as PdfDictionary;
        var dimensions = size is null ? null : f.Resolve(size.Get("V")) as PdfArray;
        return new PdfMediaCriteria(
            Bool(f, criteria.Get("A")),
            Bool(f, criteria.Get("C")),
            Bool(f, criteria.Get("O")),
            Bool(f, criteria.Get("S")),
            Int(f, criteria.Get("R")),
            depth is null ? null : new PdfMediaMinBitDepth(Int(f, depth.Get("V")), Int(f, depth.Get("M")) ?? 0, depth),
            size is null ? null : new PdfMediaMinScreenSize(
                dimensions is { Count: 2 } ? Int(f, dimensions[0]) : null,
                dimensions is { Count: 2 } ? Int(f, dimensions[1]) : null,
                Int(f, size.Get("M")) ?? 0,
                size),
            viewers,
            f.Resolve(criteria.Get("P")) as PdfArray,
            f.Resolve(criteria.Get("L")) as PdfArray,
            criteria);
    }

    static PdfSoftwareIdentifier? ReadSoftware(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary software) return null;
        return new PdfSoftwareIdentifier(
            Text(f, software.Get("U")),
            f.Resolve(software.Get("L")) as PdfArray,
            Bool(f, software.Get("LI")) ?? true,
            f.Resolve(software.Get("H")) as PdfArray,
            Bool(f, software.Get("HI")) ?? true,
            f.Resolve(software.Get("OS")) as PdfArray,
            software);
    }

    static PdfMediaPlayers? ReadPlayers(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary players) return null;
        return new PdfMediaPlayers(
            ReadPlayerList(f, players.Get("MU")),
            ReadPlayerList(f, players.Get("A")),
            ReadPlayerList(f, players.Get("NU")),
            players);
    }

    static IReadOnlyList<PdfMediaPlayerInfo> ReadPlayerList(PdfFile f, PdfObject? value)
    {
        var list = new List<PdfMediaPlayerInfo>();
        if (f.Resolve(value) is PdfArray array)
            foreach (var item in array.Items)
                if (f.Resolve(item) is PdfDictionary info)
                    list.Add(new PdfMediaPlayerInfo(ReadSoftware(f, info.Get("PID")), info));
        return list;
    }

    static PdfMediaClip? ReadClip(PdfFile f, PdfObject? value, int depth, ReadContext ctx)
    {
        if (f.Resolve(value) is not PdfDictionary clip) return null;
        if (ctx.Clips.TryGetValue(clip, out var cached)) return cached;
        if (depth > MaxDepth) { ctx.Truncated = true; return null; }
        if (!ctx.TryTake()) return null;
        ctx.Clips[clip] = null; // in-progress marker: a cycle resolves to null
        var subtype = f.Resolve(clip.Get("S")).AsName();
        var mustHonor = f.Resolve(clip.Get("MH")) as PdfDictionary;
        var bestEffort = f.Resolve(clip.Get("BE")) as PdfDictionary;
        var permissions = f.Resolve(clip.Get("P")) as PdfDictionary;
        var temp = permissions is null ? null : Text(f, permissions.Get("TF"));
        var section = subtype == "MCS";
        var result = new PdfMediaClip(
            subtype,
            Text(f, clip.Get("N")),
            section ? null : f.Resolve(clip.Get("D")),
            section ? null : Text(f, clip.Get("CT")),
            temp is "TEMPEXTRACT" or "TEMPACCESS" or "TEMPALWAYS" ? temp : "TEMPNEVER",
            permissions,
            ReadPlayers(f, clip.Get("PL")),
            Text(f, mustHonor?.Get("BU")),
            Text(f, bestEffort?.Get("BU")),
            section ? ReadClip(f, clip.Get("D"), depth + 1, ctx) : null,
            section ? ReadOffset(f, mustHonor?.Get("B")) : null,
            section ? ReadOffset(f, mustHonor?.Get("E")) : null,
            section ? ReadOffset(f, bestEffort?.Get("B")) : null,
            section ? ReadOffset(f, bestEffort?.Get("E")) : null,
            clip)
        {
            Truncated = ctx.Truncated,
        };
        ctx.Clips[clip] = result;
        return result;
    }

    static PdfMediaOffset? ReadOffset(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary offset) return null;
        var subtype = f.Resolve(offset.Get("S")).AsName();
        return new PdfMediaOffset(
            subtype,
            subtype == "T" ? ReadTimespan(f, offset.Get("T")) : null,
            subtype == "F" ? Int(f, offset.Get("F")) : null,
            subtype == "M" ? Text(f, offset.Get("M")) : null,
            offset);
    }

    static double? ReadTimespan(PdfFile f, PdfObject? value) =>
        f.Resolve(value) is PdfDictionary span && f.Resolve(span.Get("S")).AsName() == "S"
            ? f.Resolve(span.Get("V")).AsNumber()
            : null;

    static PdfMediaPlayParameters? ReadPlayParameters(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary parameters) return null;
        return new PdfMediaPlayParameters(
            ReadPlayers(f, parameters.Get("PL")),
            ReadPlayBehavior(f, parameters.Get("MH")),
            ReadPlayBehavior(f, parameters.Get("BE")),
            parameters);
    }

    static PdfMediaPlayBehavior ReadPlayBehavior(PdfFile f, PdfObject? value)
    {
        var behavior = f.Resolve(value) as PdfDictionary;
        var duration = f.Resolve(behavior?.Get("D")) as PdfDictionary;
        var durationSubtype = duration is null ? null : f.Resolve(duration.Get("S")).AsName();
        return new PdfMediaPlayBehavior(
            Int(f, behavior?.Get("V")) ?? 100,
            Bool(f, behavior?.Get("C")) ?? false,
            Int(f, behavior?.Get("F")) ?? 5,
            duration is null ? null : new PdfMediaDuration(
                durationSubtype,
                durationSubtype == "T" ? ReadTimespan(f, duration.Get("T")) : null,
                duration),
            Bool(f, behavior?.Get("A")) ?? true,
            f.Resolve(behavior?.Get("RC")).AsNumber() ?? 1.0,
            behavior);
    }

    static PdfMediaScreenParameters? ReadScreenParameters(PdfFile f, PdfObject? value)
    {
        if (f.Resolve(value) is not PdfDictionary parameters) return null;
        return new PdfMediaScreenParameters(
            ReadScreenBehavior(f, parameters.Get("MH")),
            ReadScreenBehavior(f, parameters.Get("BE")),
            parameters);
    }

    static PdfMediaScreenBehavior ReadScreenBehavior(PdfFile f, PdfObject? value)
    {
        var behavior = f.Resolve(value) as PdfDictionary;
        return new PdfMediaScreenBehavior(
            Int(f, behavior?.Get("W")) ?? 3,
            f.Resolve(behavior?.Get("B")) as PdfArray,
            f.Resolve(behavior?.Get("O")).AsNumber() ?? 1.0,
            Int(f, behavior?.Get("M")) ?? 0,
            f.Resolve(behavior?.Get("F")) as PdfDictionary,
            behavior);
    }

    static PdfSound ReadSound(PdfFile f, PdfStream sound) => new(
        f.Resolve(sound.Dict.Get("R")).AsNumber(),
        Int(f, sound.Dict.Get("C")) ?? 1,
        Int(f, sound.Dict.Get("B")) ?? 8,
        f.Resolve(sound.Dict.Get("E")).AsName() ?? "Raw",
        f.Resolve(sound.Dict.Get("CO")).AsName(),
        f.Resolve(sound.Dict.Get("CP")),
        sound);

    static PdfMovie ReadMovie(PdfFile f, PdfDictionary movie) => new(
        f.Resolve(movie.Get("F")),
        f.Resolve(movie.Get("Aspect")) as PdfArray,
        Int(f, movie.Get("Rotate")) ?? 0,
        f.Resolve(movie.Get("Poster")) is PdfBoolean or PdfStream ? f.Resolve(movie.Get("Poster")) : null,
        movie);

    static PdfMovieActivation ReadMovieActivation(PdfFile f, PdfDictionary activation) => new(
        f.Resolve(activation.Get("Start")),
        f.Resolve(activation.Get("Duration")),
        f.Resolve(activation.Get("Rate")).AsNumber() ?? 1.0,
        f.Resolve(activation.Get("Volume")).AsNumber() ?? 1.0,
        Bool(f, activation.Get("ShowControls")) ?? false,
        f.Resolve(activation.Get("Mode")).AsName() ?? "Once",
        Bool(f, activation.Get("Synchronous")) ?? false,
        f.Resolve(activation.Get("FWScale")) as PdfArray,
        f.Resolve(activation.Get("FWPosition")) as PdfArray,
        activation);

    static string? Text(PdfFile f, PdfObject? value) => f.Resolve(value) is PdfString s ? s.Text : null;

    static long? Int(PdfFile f, PdfObject? value) => f.Resolve(value) is PdfInteger i ? i.Value : null;

    static bool? Bool(PdfFile f, PdfObject? value) => f.Resolve(value) is PdfBoolean b ? b.Value : null;
}
