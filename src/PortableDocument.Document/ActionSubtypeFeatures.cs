using PortableDocument.Core;

namespace PortableDocument.Document;

// Inert typed views of the remaining action subtypes (ISO 32000-2:2020 12.6.4). Clause numbers follow the project brief;
// table numbers are intentionally not cited where the primary text could not be consulted. Nothing here launches,
// plays, navigates, hides, toggles, runs script, or touches any external target. Local indirect references are resolved
// to their objects only; missing or wrong-typed entries are null and stay available in Dictionary, which is always
// the original, unmodified action dictionary. Documented defaults are exposed as Effective* members so null still
// means "absent or malformed".

/// <summary>Trigger-event group (ISO 32000-2 12.6.3, additional-actions dictionaries; 12.5.6.5 for a link's /PA).</summary>
public enum PdfTriggerEventGroup { Annotation, Page, FormField, Catalog, LinkUri }

/// <summary>A recognised additional-actions event key with its spec name. Unknown keys classify to null and remain in the raw dictionary.</summary>
public sealed record PdfTriggerEvent(PdfTriggerEventGroup Group, string Key, string Name);

public static class PdfTriggerEvents
{
    static readonly PdfTriggerEvent[] All =
    [
        new(PdfTriggerEventGroup.Annotation, "E", "Cursor enters annotation"),
        new(PdfTriggerEventGroup.Annotation, "X", "Cursor exits annotation"),
        new(PdfTriggerEventGroup.Annotation, "D", "Mouse button pressed"),
        new(PdfTriggerEventGroup.Annotation, "U", "Mouse button released"),
        new(PdfTriggerEventGroup.Annotation, "Fo", "Annotation receives input focus"),
        new(PdfTriggerEventGroup.Annotation, "Bl", "Annotation loses input focus"),
        new(PdfTriggerEventGroup.Annotation, "PO", "Page containing annotation opened"),
        new(PdfTriggerEventGroup.Annotation, "PC", "Page containing annotation closed"),
        new(PdfTriggerEventGroup.Annotation, "PV", "Page containing annotation becomes visible"),
        new(PdfTriggerEventGroup.Annotation, "PI", "Page containing annotation no longer visible"),
        new(PdfTriggerEventGroup.Page, "O", "Page opened"),
        new(PdfTriggerEventGroup.Page, "C", "Page closed"),
        new(PdfTriggerEventGroup.FormField, "K", "Keystroke"),
        new(PdfTriggerEventGroup.FormField, "F", "Format"),
        new(PdfTriggerEventGroup.FormField, "V", "Validate"),
        new(PdfTriggerEventGroup.FormField, "C", "Calculate"),
        new(PdfTriggerEventGroup.Catalog, "WC", "Before closing document"),
        new(PdfTriggerEventGroup.Catalog, "WS", "Before saving document"),
        new(PdfTriggerEventGroup.Catalog, "DS", "After saving document"),
        new(PdfTriggerEventGroup.Catalog, "WP", "Before printing document"),
        new(PdfTriggerEventGroup.Catalog, "DP", "After printing document"),
        new(PdfTriggerEventGroup.LinkUri, "PA", "Link annotation URI action (/PA)"),
    ];

    /// <summary>Classifies an /AA event key (or /PA) for the scope that owns it. Widget annotations may also carry the form-field keys.</summary>
    public static PdfTriggerEvent? Classify(PdfActionScope scope, string? key)
    {
        if (key is null) return null;
        foreach (var e in All)
        {
            if (!string.Equals(e.Key, key, StringComparison.Ordinal)) continue;
            var ok = scope switch
            {
                PdfActionScope.Catalog => e.Group == PdfTriggerEventGroup.Catalog,
                PdfActionScope.Page => e.Group == PdfTriggerEventGroup.Page,
                PdfActionScope.FormField => e.Group == PdfTriggerEventGroup.FormField,
                PdfActionScope.Annotation => e.Group is PdfTriggerEventGroup.Annotation or PdfTriggerEventGroup.FormField or PdfTriggerEventGroup.LinkUri,
                _ => false,
            };
            if (ok) return e;
        }
        return null;
    }
}

/// <summary>Windows launch parameters (12.6.4.6 /Win, deprecated in 2.0). Strings are preserved as bytes-backed objects, never executed.</summary>
public sealed record PdfWindowsLaunchParameters(PdfString? FileName, PdfString? Directory, PdfString? Operation, PdfString? Parameters, PdfDictionary Dictionary);

/// <summary>Inert /Launch (12.6.4.6). Nothing is launched or opened.</summary>
public sealed record PdfLaunchAction(
    PdfObject? FileSpecification,
    PdfWindowsLaunchParameters? Windows,
    PdfObject? Mac,
    PdfObject? Unix,
    bool? NewWindow,
    PdfDictionary Dictionary);

/// <summary>Inert /Thread (12.6.4.7). The thread/bead is not located. /D is a thread dictionary, index or title; /B a bead dictionary or index.</summary>
public sealed record PdfThreadAction(PdfObject? FileSpecification, PdfObject? Thread, PdfObject? Bead, PdfDictionary Dictionary);

/// <summary>Inert /Sound (12.6.4.9, deprecated in 2.0). The sound stream is retained undecoded and never played.</summary>
public sealed record PdfSoundAction(PdfStream? Sound, double? Volume, bool? Synchronous, bool? Repeat, bool? Mix, PdfDictionary Dictionary)
{
    /// <summary>Spec default 1.0 when /Volume is absent or not a number.</summary>
    public double EffectiveVolume => Volume ?? 1.0;
    public bool EffectiveSynchronous => Synchronous ?? false;
    public bool EffectiveRepeat => Repeat ?? false;
    public bool EffectiveMix => Mix ?? false;
}

/// <summary>Inert /Movie (12.6.4.10, deprecated in 2.0). Nothing is played.</summary>
public sealed record PdfMovieAction(PdfDictionary? Annotation, PdfString? Title, PdfName? Operation, PdfDictionary Dictionary)
{
    /// <summary>Spec default /Play when /Operation is absent or not a name.</summary>
    public string EffectiveOperation => Operation?.Value ?? "Play";
}

/// <summary>Inert /Hide (12.6.4.12). Targets (a field/annotation dictionary, a field name string, or an array of those) are not looked up or modified.</summary>
public sealed record PdfHideAction(PdfObject? Target, bool? Hide, PdfDictionary Dictionary)
{
    /// <summary>Spec default true when /H is absent or not a boolean.</summary>
    public bool EffectiveHide => Hide ?? true;
}

/// <summary>One /State group: the raw operation name (ON, OFF, Toggle or unknown) and the optional-content group dictionaries it names.</summary>
public sealed record PdfOcgStateChange(string Operation, IReadOnlyList<PdfDictionary> Groups);

/// <summary>Inert /SetOCGState (12.6.4.13). Optional-content state is never changed.</summary>
public sealed record PdfSetOcgStateAction(PdfArray? State, bool? PreserveRadioButtons, IReadOnlyList<PdfOcgStateChange> Changes, bool Truncated, PdfDictionary Dictionary)
{
    /// <summary>Spec default true when /PreserveRB is absent or not a boolean.</summary>
    public bool EffectivePreserveRadioButtons => PreserveRadioButtons ?? true;
}

/// <summary>Transition dictionary values (12.4.4.2). Raw dictionary is retained.</summary>
public sealed record PdfTransition(PdfName? Style, double? Duration, PdfName? Dimension, PdfName? Motion, PdfObject? Direction, double? Scale, bool? Base, PdfDictionary Dictionary)
{
    public string EffectiveStyle => Style?.Value ?? "R";
    public double EffectiveDuration => Duration ?? 1.0;
    public string EffectiveDimension => Dimension?.Value ?? "H";
    public string EffectiveMotion => Motion?.Value ?? "I";
    public double EffectiveScale => Scale ?? 1.0;
    public bool EffectiveBase => Base ?? false;
}

/// <summary>Inert /Trans action (12.6.4.14). No transition is performed.</summary>
public sealed record PdfTransAction(PdfDictionary? TransitionDictionary, PdfTransition? Transition, PdfDictionary Dictionary);

/// <summary>Inert /GoTo3DView (12.6.4.15). /TA is a 3D annotation dictionary, /V a view dictionary, index, name or string; neither is navigated.</summary>
public sealed record PdfGoTo3DViewAction(PdfDictionary? Target, PdfObject? View, PdfDictionary Dictionary);

/// <summary>Inert /RichMediaExecute (13.7 RichMedia). The command and arguments are retained and never sent to any content.</summary>
public sealed record PdfRichMediaExecuteAction(
    PdfDictionary? Target,
    PdfDictionary? TargetInstance,
    PdfDictionary? Command,
    PdfString? CommandName,
    PdfArray? CommandArguments,
    PdfDictionary Dictionary);

/// <summary>Opaque /JavaScript (12.6.4.17). /JS is a text string or stream retained undecoded; it is never parsed or run.</summary>
public sealed record PdfJavaScriptAction(PdfObject? Script, PdfDictionary Dictionary);

public static partial class SafeContentFeatures
{
    const int MaxOcgStateElements = 8192;

    static PdfLaunchAction ReadLaunchAction(PdfFile f, PdfDictionary action)
    {
        var file = f.Resolve(action.Get("F"));
        var win = f.Resolve(action.Get("Win")) as PdfDictionary;
        return new PdfLaunchAction(
            file is PdfString or PdfDictionary ? file : null,
            win is null ? null : new PdfWindowsLaunchParameters(
                f.Resolve(win.Get("F")) as PdfString,
                f.Resolve(win.Get("D")) as PdfString,
                f.Resolve(win.Get("O")) as PdfString,
                f.Resolve(win.Get("P")) as PdfString,
                win),
            f.Resolve(action.Get("Mac")),
            f.Resolve(action.Get("Unix")),
            Bool(f, action.Get("NewWindow")),
            action);
    }

    static PdfThreadAction ReadThreadAction(PdfFile f, PdfDictionary action)
    {
        var file = f.Resolve(action.Get("F"));
        var thread = f.Resolve(action.Get("D"));
        var bead = f.Resolve(action.Get("B"));
        return new PdfThreadAction(
            file is PdfString or PdfDictionary ? file : null,
            thread is PdfDictionary or PdfInteger or PdfString ? thread : null,
            bead is PdfDictionary or PdfInteger ? bead : null,
            action);
    }

    static PdfSoundAction ReadSoundAction(PdfFile f, PdfDictionary action) =>
        new(f.Resolve(action.Get("Sound")) as PdfStream,
            f.Resolve(action.Get("Volume")) is PdfInteger or PdfReal ? f.Resolve(action.Get("Volume")).AsNumber() : null,
            Bool(f, action.Get("Synchronous")), Bool(f, action.Get("Repeat")), Bool(f, action.Get("Mix")), action);

    static PdfMovieAction ReadMovieAction(PdfFile f, PdfDictionary action) =>
        new(f.Resolve(action.Get("Annotation")) as PdfDictionary,
            f.Resolve(action.Get("T")) as PdfString,
            f.Resolve(action.Get("Operation")) as PdfName,
            action);

    static PdfHideAction ReadHideAction(PdfFile f, PdfDictionary action)
    {
        var target = f.Resolve(action.Get("T"));
        return new PdfHideAction(target is PdfDictionary or PdfString or PdfArray ? target : null, Bool(f, action.Get("H")), action);
    }

    static PdfSetOcgStateAction ReadSetOcgStateAction(PdfFile f, PdfDictionary action)
    {
        var state = f.Resolve(action.Get("State")) as PdfArray;
        var changes = new List<PdfOcgStateChange>();
        var truncated = false;
        if (state is not null)
        {
            var count = state.Count;
            if (count > MaxOcgStateElements) { count = MaxOcgStateElements; truncated = true; }
            string? op = null;
            var groups = new List<PdfDictionary>();
            for (var i = 0; i < count; i++)
            {
                var item = f.Resolve(state[i]);
                if (item is PdfName name)
                {
                    if (op is not null) changes.Add(new PdfOcgStateChange(op, groups));
                    op = name.Value;
                    groups = [];
                }
                else if (item is PdfDictionary group && op is not null)
                    groups.Add(group);
            }
            if (op is not null) changes.Add(new PdfOcgStateChange(op, groups));
        }
        return new PdfSetOcgStateAction(state, Bool(f, action.Get("PreserveRB")), changes, truncated, action);
    }

    static PdfTransAction ReadTransAction(PdfFile f, PdfDictionary action)
    {
        var trans = f.Resolve(action.Get("Trans")) as PdfDictionary;
        var direction = trans is null ? null : f.Resolve(trans.Get("Di"));
        return new PdfTransAction(
            trans,
            trans is null ? null : new PdfTransition(
                f.Resolve(trans.Get("S")) as PdfName,
                f.Resolve(trans.Get("D")) is PdfInteger or PdfReal ? f.Resolve(trans.Get("D")).AsNumber() : null,
                f.Resolve(trans.Get("Dm")) as PdfName,
                f.Resolve(trans.Get("M")) as PdfName,
                direction is PdfInteger or PdfReal or PdfName ? direction : null,
                f.Resolve(trans.Get("SS")) is PdfInteger or PdfReal ? f.Resolve(trans.Get("SS")).AsNumber() : null,
                Bool(f, trans.Get("B")),
                trans),
            action);
    }

    static PdfGoTo3DViewAction ReadGoTo3DViewAction(PdfFile f, PdfDictionary action)
    {
        var view = f.Resolve(action.Get("V"));
        return new PdfGoTo3DViewAction(
            f.Resolve(action.Get("TA")) as PdfDictionary,
            view is PdfDictionary or PdfInteger or PdfName or PdfString ? view : null,
            action);
    }

    static PdfRichMediaExecuteAction ReadRichMediaExecuteAction(PdfFile f, PdfDictionary action)
    {
        var cmd = f.Resolve(action.Get("CMD")) as PdfDictionary;
        return new PdfRichMediaExecuteAction(
            f.Resolve(action.Get("TA")) as PdfDictionary,
            f.Resolve(action.Get("TI")) as PdfDictionary,
            cmd,
            cmd is null ? null : f.Resolve(cmd.Get("C")) as PdfString,
            cmd is null ? null : f.Resolve(cmd.Get("A")) as PdfArray,
            action);
    }

    static bool? Bool(PdfFile f, PdfObject? value) => f.Resolve(value) is PdfBoolean b ? b.Value : null;
}
