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
    string? FieldName = null);

public static class SafeContentFeatures
{
    const int MaxDepth = 64;

    /// <summary>
    /// Returns action dictionaries for inspection. JavaScript payloads are returned as PDF objects
    /// and are never interpreted or executed.
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
            fieldName));
        if (action.Get("Next") is { } next)
            AddActionChain(f, next, scope, $"{trigger}/Next", name, pageIndex, annotationIndex, fieldName, depth + 1, ancestors, actions);
        ancestors.Remove(action);
    }

    static string? StringValue(PdfFile f, PdfObject? value) =>
        f.Resolve(value) is PdfString text ? text.Text : null;
}
