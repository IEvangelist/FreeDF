using PortableDocument.Core;

namespace PortableDocument.Document;

public enum PdfActionScope
{
    Catalog,
    Page,
    Annotation,
    AcroForm,
    FormField,
    Outline,
    NamedJavaScript,
}

public sealed record PdfAction(
    PdfActionScope Scope,
    string Trigger,
    string? Name,
    string? ActionType,
    PdfDictionary Dictionary,
    PdfObject? JavaScript,
    int? PageIndex = null,
    int? AnnotationIndex = null,
    string? FieldName = null)
{
    public PdfGoToAction? GoToDetails { get; init; }
    public PdfRemoteGoToAction? RemoteGoToDetails { get; init; }
    public PdfEmbeddedGoToAction? EmbeddedGoToDetails { get; init; }
    public PdfGoToDpAction? GoToDpDetails { get; init; }
    public PdfUriAction? UriDetails { get; init; }
    public PdfNamedAction? NamedDetails { get; init; }
}

/// <summary>
/// Inert /GoTo metadata (ISO 32000-2:2020, 12.6.4.2, Table 202). Only references
/// within the current PDF are dereferenced; no navigation or named destination lookup occurs.
/// Missing or wrong-type entries are null and remain available in Dictionary.
/// Array contents are retained without conformance validation or resolving their elements.
/// </summary>
/// <param name="Destination">The required /D name, byte string, or explicit destination array (12.3.2). Local explicit destinations use page references.</param>
/// <param name="Dictionary">The original action dictionary, including unrecognised or malformed entries.</param>
public sealed record PdfGoToAction(
    PdfObject? Destination,
    PdfDictionary Dictionary);

/// <summary>
/// Inert /URI metadata (ISO 32000-2:2020, 12.6.4.8, Table 210). Only references
/// within the current PDF are dereferenced; no URI is accessed or resolved against a base URI.
/// Missing or wrong-type entries are null and remain available in Dictionary.
/// String contents are retained without conformance validation.
/// </summary>
/// <param name="Uri">The required /URI ASCII string containing UTF-8 bytes, preserved without decoding, parsing, or navigating.</param>
/// <param name="IsMap">The optional /IsMap flag. Null preserves absence or a wrong type; the PDF default is false.</param>
/// <param name="Dictionary">The original action dictionary, including unrecognised or malformed entries.</param>
public sealed record PdfUriAction(
    PdfString? Uri,
    bool? IsMap,
    PdfDictionary Dictionary);

/// <summary>
/// Inert /Named metadata (ISO 32000-2:2020, 12.6.4.11, Table 216). Only references
/// within the current PDF are dereferenced; no named action is executed or dispatched.
/// Missing or wrong-type entries are null and remain available in Dictionary.
/// Names are retained without restricting them to the standard names in Table 215.
/// </summary>
/// <param name="Name">The required /N name, including unrecognised names, without interpretation.</param>
/// <param name="Dictionary">The original action dictionary, including unrecognised or malformed entries.</param>
public sealed record PdfNamedAction(
    PdfName? Name,
    PdfDictionary Dictionary);

/// <summary>
/// Inert /GoToR metadata (ISO 32000-2:2020, 12.6.4.3, Table 203). Only references
/// within the current PDF are dereferenced; no target file or destination is opened or resolved.
/// Missing or wrong-type entries are null and remain available in Dictionary.
/// Dictionary and array contents are retained without conformance validation.
/// </summary>
/// <param name="FileSpecification">The /F string or file specification dictionary, without reading embedded data.</param>
/// <param name="Destination">The /D name, byte string, or explicit destination array. Remote page numbers are zero-based.</param>
/// <param name="StructureDestination">The optional /SD array, retained without interpreting its remote structure element ID.</param>
/// <param name="NewWindow">The optional /NewWindow flag. Null does not imply false; a viewer would use its preference.</param>
/// <param name="Dictionary">The original action dictionary, including unrecognised or malformed entries.</param>
public sealed record PdfRemoteGoToAction(
    PdfObject? FileSpecification,
    PdfObject? Destination,
    PdfArray? StructureDestination,
    bool? NewWindow,
    PdfDictionary Dictionary);

/// <summary>
/// Inert /GoToE metadata (ISO 32000-2:2020, 12.6.4.4, Table 204). Only references
/// within the current PDF are dereferenced; no embedded content, target file, or destination is accessed.
/// Missing or wrong-type entries are null and remain available in Dictionary.
/// Dictionary and array contents are retained without conformance validation.
/// TargetPath exposes bounded local target-path metadata without following the path into other documents.
/// </summary>
/// <param name="FileSpecification">The optional /F string or file specification dictionary identifying the target's root document.</param>
/// <param name="Destination">The required /D name, byte string, or explicit destination array in the target document.</param>
/// <param name="Target">The original /T target dictionary (Table 205), required when /F is absent, retained unchanged.</param>
/// <param name="NewWindow">The optional /NewWindow flag. Null does not imply false; a viewer would use its preference.</param>
/// <param name="Dictionary">The original action dictionary, including unrecognised or malformed entries.</param>
public sealed record PdfEmbeddedGoToAction(
    PdfObject? FileSpecification,
    PdfObject? Destination,
    PdfDictionary? Target,
    bool? NewWindow,
    PdfDictionary Dictionary)
{
    /// <summary>Bounded inert Table 205 metadata for Target; the original dictionary remains unchanged.</summary>
    public PdfEmbeddedGoToTarget? TargetPath { get; init; }
}

/// <summary>
/// Inert /GoToDp metadata (ISO 32000-2:2020, 12.6.4.5, Table 206).
/// Only a local indirect /Dp reference resolving to a /Type /DPart dictionary is exposed;
/// the DPart tree and its Start/End pages are not resolved, validated, or navigated.
/// Missing or malformed values remain available in Dictionary.
/// </summary>
/// <param name="DPart">The referenced /Type /DPart dictionary, or null when /Dp is missing, direct, or malformed.</param>
/// <param name="Dictionary">The original action dictionary, including unrecognised or malformed entries.</param>
public sealed record PdfGoToDpAction(
    PdfDictionary? DPart,
    PdfDictionary Dictionary);

/// <summary>Why a dictionary-valued nested /T was not expanded in the typed target path.</summary>
public enum PdfEmbeddedGoToTargetBoundary
{
    None,
    Cycle,
    DepthLimit,
}

/// <summary>
/// Inert target-path metadata (ISO 32000-2:2020, 12.6.4.4, Table 205).
/// Only current-PDF indirect values are resolved; no document, name-tree, page, annotation,
/// or destination lookup occurs. Missing or wrong-type fields are null in the typed view.
/// Conditional requiredness and attachment/tree semantics are not validated.
/// Original dictionaries retain all raw references, malformed values, and unknown entries.
/// </summary>
/// <param name="Relationship">The /R name: /P denotes a parent and /C a child. Unknown names are retained without interpretation.</param>
/// <param name="Name">The /N byte string identifying an EmbeddedFiles name-tree target for /R /C, without decoding its bytes.</param>
/// <param name="Page">The /P integer or byte string identifying an attachment's page for /R /C, without lookup.</param>
/// <param name="Annotation">The /A integer or text string identifying the file-attachment annotation, without lookup or decoding.</param>
/// <param name="NestedTarget">The optional nested /T path element, if it is a dictionary and expansion is within bounds.</param>
/// <param name="NestedTargetBoundary">Cycle or depth boundary for an unexpanded dictionary-valued /T; otherwise None. The raw /T remains in Dictionary.</param>
/// <param name="Dictionary">The original target dictionary, including the raw /T even when expansion stops.</param>
public sealed record PdfEmbeddedGoToTarget(
    PdfName? Relationship,
    PdfString? Name,
    PdfObject? Page,
    PdfObject? Annotation,
    PdfEmbeddedGoToTarget? NestedTarget,
    PdfEmbeddedGoToTargetBoundary NestedTargetBoundary,
    PdfDictionary Dictionary)
{
    /// <summary>Maximum number of target dictionaries exposed in a typed path, including its root.</summary>
    public const int MaxPathLength = 64;
}

public sealed record PdfXfaPacket(string? Name, PdfStream Stream);

public static class SafeContentFeatures
{
    const int MaxDepth = 64;

    /// <summary>
    /// Returns action dictionaries for inspection, with inert typed metadata for local, remote, embedded, and DPart go-to, URI and named actions.
    /// Payloads are never interpreted or executed, and external files, destinations, and URIs are never accessed.
    /// </summary>
    public static IReadOnlyList<PdfAction> GetActions(this PdfDocument doc)
    {
        var f = doc.File;
        var actions = new List<PdfAction>();
        if (f.Resolve(f.Catalog.Get("OpenAction")) is PdfDictionary openAction)
            AddActionChain(f, openAction, PdfActionScope.Catalog, "OpenAction", null, null, null, null, 0, [], actions);
        AddActionDictionary(f, f.Catalog.Get("AA"), PdfActionScope.Catalog, "AA", null, null, null, null, actions);
        if (f.Resolve(f.Catalog.Get("Outlines")) is PdfDictionary outlines)
            CollectOutlineActions(f, outlines.Get("First"), 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), actions);

        if (f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names
            && f.Resolve(names.Get("JavaScript")) is PdfDictionary javaScriptTree)
            CollectJavaScriptNames(f, javaScriptTree, 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), actions);

        if (f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary form)
        {
            AddActionDictionary(f, form.Get("AA"), PdfActionScope.AcroForm, "AA", null, null, null, null, actions);
            if (f.Resolve(form.Get("Fields")) is PdfArray fields)
            {
                var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
                foreach (var field in fields.Items)
                    CollectFieldActions(f, field, "", 0, seen, actions);
            }
        }

        for (var pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            var page = doc.Pages[pageIndex];
            AddActionDictionary(f, page.Dict.Get("AA"), PdfActionScope.Page, "AA", pageIndex, null, null, null, actions);
            if (f.Resolve(page.Dict.Get("Annots")) is not PdfArray annotations) continue;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                if (f.Resolve(annotations[annotationIndex]) is not PdfDictionary annotation) continue;
                AddActionChain(f, annotation.Get("A"), PdfActionScope.Annotation, "A", null, pageIndex, annotationIndex, null, 0, [], actions);
                AddActionDictionary(f, annotation.Get("AA"), PdfActionScope.Annotation, "AA", pageIndex, annotationIndex, null, null, actions);
            }
        }
        return actions;
    }

    static void CollectOutlineActions(
        PdfFile f,
        PdfObject? first,
        int depth,
        HashSet<PdfDictionary> seen,
        List<PdfAction> actions)
    {
        var current = f.Resolve(first) as PdfDictionary;
        while (current is not null && depth <= MaxDepth && seen.Add(current))
        {
            var title = StringValue(f, current.Get("Title"));
            AddActionChain(f, current.Get("A"), PdfActionScope.Outline, "A", title, null, null, null, 0, [], actions);
            if (f.Resolve(current.Get("First")) is { } child)
                CollectOutlineActions(f, child, depth + 1, seen, actions);
            current = f.Resolve(current.Get("Next")) as PdfDictionary;
        }
    }

    /// <summary>Returns the AcroForm /XFA value without interpreting or executing its contents.</summary>
    public static PdfObject? GetXfaData(this PdfDocument doc)
    {
        var f = doc.File;
        return f.Resolve(f.Catalog.Get("AcroForm")) is PdfDictionary form
            ? f.Resolve(form.Get("XFA"))
            : null;
    }

    /// <summary>Returns XFA packet names and streams without interpreting or executing packet contents.</summary>
    public static IReadOnlyList<PdfXfaPacket> GetXfaPackets(this PdfDocument doc)
    {
        var f = doc.File;
        var xfa = doc.GetXfaData();
        if (f.Resolve(xfa) is PdfStream stream) return [new PdfXfaPacket(null, stream)];
        if (f.Resolve(xfa) is not PdfArray packets) return [];

        var result = new List<PdfXfaPacket>();
        for (var i = 0; i + 1 < packets.Count; i += 2)
            if (f.Resolve(packets[i]) is PdfString name && f.Resolve(packets[i + 1]) is PdfStream packet)
                result.Add(new PdfXfaPacket(name.Text, packet));
        return result;
    }

    static void CollectFieldActions(
        PdfFile f,
        PdfObject? value,
        string prefix,
        int depth,
        HashSet<PdfDictionary> seen,
        List<PdfAction> actions)
    {
        if (depth > MaxDepth || f.Resolve(value) is not PdfDictionary field || !seen.Add(field)) return;
        var partialName = StringValue(f, field.Get("T"));
        var name = partialName is null ? prefix : prefix.Length == 0 ? partialName : prefix + "." + partialName;
        AddActionChain(f, field.Get("A"), PdfActionScope.FormField, "A", null, null, null, name, 0, [], actions);
        AddActionDictionary(f, field.Get("AA"), PdfActionScope.FormField, "AA", null, null, name, null, actions);
        if (f.Resolve(field.Get("Kids")) is PdfArray kids)
            foreach (var child in kids.Items)
                CollectFieldActions(f, child, name, depth + 1, seen, actions);
    }

    static void AddActionDictionary(
        PdfFile f,
        PdfObject? value,
        PdfActionScope scope,
        string trigger,
        int? pageIndex,
        int? annotationIndex,
        string? fieldName,
        string? name,
        List<PdfAction> actions)
    {
        if (f.Resolve(value) is not PdfDictionary additionalActions) return;
        foreach (var (eventName, action) in additionalActions.Entries)
            AddActionChain(f, action, scope, $"{trigger}/{eventName}", name, pageIndex, annotationIndex, fieldName, 0, [], actions);
    }

    static void CollectJavaScriptNames(
        PdfFile f,
        PdfDictionary node,
        int depth,
        HashSet<PdfDictionary> seen,
        List<PdfAction> actions)
    {
        if (depth > MaxDepth || !seen.Add(node)) return;
        if (f.Resolve(node.Get("Names")) is PdfArray pairs)
        {
            for (var i = 0; i + 1 < pairs.Count; i += 2)
            {
                var name = StringValue(f, pairs[i]);
                AddActionChain(f, pairs[i + 1], PdfActionScope.NamedJavaScript, "Names/JavaScript", name, null, null, null, 0, [], actions);
            }
        }
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var child in kids.Items)
                if (f.Resolve(child) is PdfDictionary kid)
                    CollectJavaScriptNames(f, kid, depth + 1, seen, actions);
    }

    static void AddActionChain(
        PdfFile f,
        PdfObject? value,
        PdfActionScope scope,
        string trigger,
        string? name,
        int? pageIndex,
        int? annotationIndex,
        string? fieldName,
        int depth,
        HashSet<PdfDictionary> ancestors,
        List<PdfAction> actions)
    {
        if (depth > MaxDepth) return;
        if (f.Resolve(value) is PdfArray array)
        {
            for (var i = 0; i < array.Count; i++)
                AddActionChain(f, array[i], scope, $"{trigger}[{i}]", name, pageIndex, annotationIndex, fieldName, depth + 1, ancestors, actions);
            return;
        }
        if (f.Resolve(value) is not PdfDictionary action || !ancestors.Add(action)) return;
        var type = f.Resolve(action.Get("S")).AsName();
        actions.Add(new PdfAction(
            scope,
            trigger,
            name,
            type,
            action,
            type == "JavaScript" ? f.Resolve(action.Get("JS")) : null,
            pageIndex,
            annotationIndex,
            fieldName)
        {
            GoToDetails = type == "GoTo" ? ReadGoToAction(f, action) : null,
            RemoteGoToDetails = type == "GoToR" ? ReadRemoteGoToAction(f, action) : null,
            EmbeddedGoToDetails = type == "GoToE" ? ReadEmbeddedGoToAction(f, action) : null,
            GoToDpDetails = type == "GoToDp" ? ReadGoToDpAction(f, action) : null,
            UriDetails = type == "URI" ? ReadUriAction(f, action) : null,
            NamedDetails = type == "Named" ? ReadNamedAction(f, action) : null,
        });
        if (action.Get("Next") is { } next)
            AddActionChain(f, next, scope, $"{trigger}/Next", name, pageIndex, annotationIndex, fieldName, depth + 1, ancestors, actions);
        ancestors.Remove(action);
    }

    static PdfGoToAction ReadGoToAction(PdfFile f, PdfDictionary action)
    {
        var destination = f.Resolve(action.Get("D"));
        return new PdfGoToAction(
            destination is PdfName or PdfString or PdfArray ? destination : null,
            action);
    }

    static PdfRemoteGoToAction ReadRemoteGoToAction(PdfFile f, PdfDictionary action)
    {
        var file = f.Resolve(action.Get("F"));
        var destination = f.Resolve(action.Get("D"));
        return new PdfRemoteGoToAction(
            file is PdfString or PdfDictionary ? file : null,
            destination is PdfName or PdfString or PdfArray ? destination : null,
            f.Resolve(action.Get("SD")) as PdfArray,
            f.Resolve(action.Get("NewWindow")) is PdfBoolean newWindow ? newWindow.Value : null,
            action);
    }

    static PdfUriAction ReadUriAction(PdfFile f, PdfDictionary action) =>
        new(
            f.Resolve(action.Get("URI")) as PdfString,
            f.Resolve(action.Get("IsMap")) is PdfBoolean isMap ? isMap.Value : null,
            action);

    static PdfEmbeddedGoToAction ReadEmbeddedGoToAction(PdfFile f, PdfDictionary action)
    {
        var file = f.Resolve(action.Get("F"));
        var destination = f.Resolve(action.Get("D"));
        var target = f.Resolve(action.Get("T")) as PdfDictionary;
        return new PdfEmbeddedGoToAction(
            file is PdfString or PdfDictionary ? file : null,
            destination is PdfName or PdfString or PdfArray ? destination : null,
            target,
            f.Resolve(action.Get("NewWindow")) is PdfBoolean newWindow ? newWindow.Value : null,
            action)
        {
            TargetPath = target is null ? null : ReadEmbeddedGoToTarget(
                f, target, 1, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance)),
        };
    }

    static PdfGoToDpAction ReadGoToDpAction(PdfFile f, PdfDictionary action)
    {
        var dPart = action.Get("Dp") is PdfRef reference
            ? f.Resolve(reference) as PdfDictionary
            : null;
        if (dPart?.Get("Type") is { } type && f.Resolve(type).AsName() != "DPart")
            dPart = null;
        return new PdfGoToDpAction(dPart, action);
    }

    static PdfEmbeddedGoToTarget ReadEmbeddedGoToTarget(
        PdfFile f,
        PdfDictionary target,
        int length,
        HashSet<PdfDictionary> seen)
    {
        seen.Add(target);
        var page = f.Resolve(target.Get("P"));
        var annotation = f.Resolve(target.Get("A"));
        PdfEmbeddedGoToTarget? nested = null;
        var boundary = PdfEmbeddedGoToTargetBoundary.None;
        if (f.Resolve(target.Get("T")) is PdfDictionary next)
        {
            if (seen.Contains(next))
                boundary = PdfEmbeddedGoToTargetBoundary.Cycle;
            else if (length >= PdfEmbeddedGoToTarget.MaxPathLength)
                boundary = PdfEmbeddedGoToTargetBoundary.DepthLimit;
            else
                nested = ReadEmbeddedGoToTarget(f, next, length + 1, seen);
        }
        return new PdfEmbeddedGoToTarget(
            f.Resolve(target.Get("R")) as PdfName,
            f.Resolve(target.Get("N")) as PdfString,
            page is PdfInteger or PdfString ? page : null,
            annotation is PdfInteger or PdfString ? annotation : null,
            nested,
            boundary,
            target);
    }

    static PdfNamedAction ReadNamedAction(PdfFile f, PdfDictionary action) =>
        new(
            f.Resolve(action.Get("N")) as PdfName,
            action);

    static string? StringValue(PdfFile f, PdfObject? value) =>
        f.Resolve(value) is PdfString text ? text.Text : null;
}
