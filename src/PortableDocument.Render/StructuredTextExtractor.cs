using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace PortableDocument.Render;

/// <summary>Reading-order text extraction that follows the logical structure tree instead of content-stream order (ISO 32000-2 14.8.2.3).</summary>
public sealed class StructuredTextExtractor
{
    static readonly HashSet<string> Inline = new(StringComparer.Ordinal)
        { "Span", "Quote", "Code", "Link", "Reference", "Note", "Sub", "Annot", "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP", "Lbl", "BibEntry" };

    readonly PdfDocument _doc;
    readonly Dictionary<int, Dictionary<(int Form, int Mcid), string>> _pageText = [];
    readonly Dictionary<int, Dictionary<PdfStream, HashSet<PdfDictionary>>> _annotationAppearances = [];
    readonly Dictionary<PdfStream, int> _streamNumbers = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<int, Dictionary<PdfStream, string>> _objectText = [];
    readonly HashSet<(int Page, PdfDictionary Annotation)> _visitedAnnotations = [];

    /// <summary>Emit /Alt for elements (typically figures) that carry no text of their own.</summary>
    public bool IncludeAlt { get; init; }

    public StructuredTextExtractor(PdfDocument doc) => _doc = doc;

    /// <summary>Text of the whole document in structure order, or null when the document is untagged.</summary>
    public static string? Extract(PdfDocument doc, bool includeAlt = false) => new StructuredTextExtractor(doc) { IncludeAlt = includeAlt }.Extract();

    public string? Extract()
    {
        var tree = _doc.Structure;
        if (tree is null) return null;
        _visitedAnnotations.Clear();
        var sb = new StringBuilder();
        foreach (var c in tree.Children) Walk(c, sb, 0);
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Text of one structure element and its descendants.</summary>
    public string GetText(StructElement element)
    {
        _visitedAnnotations.Clear();
        var sb = new StringBuilder();
        Walk(element, sb, 0);
        return sb.ToString().Trim('\n');
    }

    /// <summary>Text of one marked-content sequence on a page, or null if absent.</summary>
    public string? GetMarkedContentText(int pageIndex, int mcid) =>
        PageText(pageIndex).TryGetValue((0, mcid), out var t) ? t : null;

    /// <summary>Text of a form's marked-content sequence as rendered on a page. Repeated placements contribute only once.</summary>
    public string? GetMarkedContentText(int pageIndex, PdfStream stream, int mcid)
    {
        var text = PageText(pageIndex);
        return _streamNumbers.TryGetValue(stream, out var form) && text.TryGetValue((form, mcid), out var t) ? t : null;
    }

    string? GetMarkedContentText(int pageIndex, StructMarkedContent content)
    {
        if (content.Stream is { } stream && content.StreamOwner is { } owner
            && AnnotationAppearances(pageIndex).TryGetValue(stream, out var owners)
            && (owner is not PdfDictionary annotation
                || !owners.Contains(annotation)))
            return null;
        return content.Stream is { } form ? GetMarkedContentText(pageIndex, form, content.Mcid) : GetMarkedContentText(pageIndex, content.Mcid);
    }

    Dictionary<PdfStream, HashSet<PdfDictionary>> AnnotationAppearances(int pageIndex)
    {
        if (_annotationAppearances.TryGetValue(pageIndex, out var appearances)) return appearances;
        appearances = new(ReferenceEqualityComparer.Instance);
        _annotationAppearances[pageIndex] = appearances;
        if (pageIndex < 0 || pageIndex >= _doc.Pages.Count) return appearances;

        var file = _doc.File;
        if (file.Resolve(_doc.Pages[pageIndex].Dict.Get("Annots")) is not PdfArray annots) return appearances;
        foreach (var item in annots.Items)
        {
            if (file.Resolve(item) is not PdfDictionary annotation) continue;
            var flags = (int)(file.Resolve(annotation.Get("F")).AsNumber() ?? 0);
            if ((flags & (2 | 32)) != 0 || file.Resolve(annotation.Get("Subtype")).AsName() == "Popup") continue;
            if (file.Resolve(annotation.Get("Rect")) is not PdfArray { Count: >= 4 }) continue;
            var ap = file.Resolve(annotation.Get("AP")) as PdfDictionary;
            var appearance = ap?.Get("N");
            var normal = file.Resolve(appearance);
            if (normal is PdfDictionary states)
            {
                appearance = file.Resolve(annotation.Get("AS")).AsName() is { } state ? states.Get(state) : null;
                normal = file.Resolve(appearance);
            }
            if (normal is not PdfStream stream) continue;
            if (!appearances.TryGetValue(stream, out var owners)) appearances[stream] = owners = new(ReferenceEqualityComparer.Instance);
            owners.Add(annotation);
        }
        return appearances;
    }

    Dictionary<(int Form, int Mcid), string> PageText(int pageIndex)
    {
        if (_pageText.TryGetValue(pageIndex, out var d)) return d;
        d = [];
        _pageText[pageIndex] = d;
        if (pageIndex < 0 || pageIndex >= _doc.Pages.Count) return d;
        List<global::PortableDocument.Content.ContentInterpreter.TextSpan> spans;
        try { spans = PortableDocument.ExtractText(_doc.Pages[pageIndex], includeAnnotationAppearances: true); }
        catch (Exception e) when (e is global::PortableDocument.Core.PdfParseException or InvalidCastException or InvalidDataException) { return d; }
        var sbs = new Dictionary<(int Form, int Mcid), StringBuilder>();
        var last = new Dictionary<(int Form, int Mcid), global::PortableDocument.Content.ContentInterpreter.TextSpan>();
        var seen = new HashSet<(int Form, int Index)>();
        var objectBuilders = new Dictionary<PdfStream, StringBuilder>(ReferenceEqualityComparer.Instance);
        var objectLast = new Dictionary<PdfStream, global::PortableDocument.Content.ContentInterpreter.TextSpan>(ReferenceEqualityComparer.Instance);
        var objectSeen = new HashSet<(int Form, int Index)>();
        foreach (var s in spans)
        {
            var form = s.Form;
            var mcid = s.Mcid;
            if (form > 0 && _doc.File.GetObject(form) is PdfStream stream)
            {
                _streamNumbers[stream] = form;
                if (!s.IsArtifact && (mcid < 0 || _doc.Structure?.FindMarkedContent(stream, mcid) is null)
                    && AnnotationAppearances(pageIndex).ContainsKey(stream)
                    && objectSeen.Add((form, s.Index)))
                {
                    if (!objectBuilders.TryGetValue(stream, out var builder)) objectBuilders[stream] = builder = new StringBuilder();
                    if (objectLast.TryGetValue(stream, out var previous) && NeedsSpace(previous, s)
                        && builder.Length > 0 && !char.IsWhiteSpace(builder[^1])
                        && s.Text.Length > 0 && !char.IsWhiteSpace(s.Text[0])) builder.Append(' ');
                    builder.Append(s.Text);
                    objectLast[stream] = s;
                }
            }
            if (mcid < 0) { form = 0; mcid = s.EnclosingPageMcid; }
            if (mcid < 0) continue;
            if (form != 0 && !seen.Add((form, s.Index))) continue;
            var key = (form, mcid);
            if (!sbs.TryGetValue(key, out var sb)) sbs[key] = sb = new StringBuilder();
            if (last.TryGetValue(key, out var p) && NeedsSpace(p, s) && sb.Length > 0 && !char.IsWhiteSpace(sb[^1]) && s.Text.Length > 0 && !char.IsWhiteSpace(s.Text[0])) sb.Append(' ');
            sb.Append(s.Text);
            last[key] = s;
        }
        foreach (var (k, v) in sbs) d[k] = v.ToString();
        var objectText = new Dictionary<PdfStream, string>(ReferenceEqualityComparer.Instance);
        foreach (var (stream, builder) in objectBuilders) objectText[stream] = builder.ToString();
        _objectText[pageIndex] = objectText;
        return d;
    }

    // Explicit MCRs retain their text; OBJR contributes the remaining selected appearance text.
    string? GetObjectText(StructObjectRef reference)
    {
        if (reference.PageIndex is not { } page || reference.Object is not PdfDictionary annotation
            || reference.Parent is not { } parent
            || _doc.File.Resolve(annotation.Get("StructParent")) is not PdfInteger { Value: >= 0 and <= int.MaxValue } key
            || _doc.Structure?.ParentTreeLookup((int)key.Value) is not PdfDictionary owner
            || !ReferenceEquals(owner, parent.Dict))
            return null;
        PageText(page);
        if (!_objectText.TryGetValue(page, out var texts)) return null;
        foreach (var (stream, owners) in AnnotationAppearances(page))
            if (owners.Contains(annotation) && texts.TryGetValue(stream, out var text)
                && _visitedAnnotations.Add((page, annotation)))
                return text;
        return null;
    }

    static bool NeedsSpace(global::PortableDocument.Content.ContentInterpreter.TextSpan prev, global::PortableDocument.Content.ContentInterpreter.TextSpan cur)
    {
        var size = Math.Max(prev.Size, 1);
        if (Math.Abs(cur.Y - prev.Y) > size * 0.5 || Math.Abs(cur.Angle - prev.Angle) > 1) return true;
        return cur.X - (prev.X + prev.Width) > size * 0.15 || cur.X < prev.X;
    }

    static void Break(StringBuilder sb) { if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n'); }
    static void Space(StringBuilder sb) { if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1])) sb.Append(' '); }

    void Walk(StructElement e, StringBuilder sb, int depth)
    {
        if (depth > StructureTree.MaxDepth || e.StandardType == "Artifact") return;
        var type = e.StandardType;
        var isCell = type is "TD" or "TH";
        var isInline = Inline.Contains(type);
        if (isCell) { if (sb.Length > 0 && sb[^1] is not ('\n' or '\t')) sb.Append('\t'); }
        else if (!isInline) Break(sb);

        var start = sb.Length;
        if (!string.IsNullOrEmpty(e.ActualText)) sb.Append(e.ActualText);
        else
        {
            var first = true;
            foreach (var c in e.Children)
            {
                switch (c)
                {
                    case StructElement ce: Walk(ce, sb, depth + 1); break;
                    case StructObjectRef reference:
                        var objectText = GetObjectText(reference);
                        if (!string.IsNullOrEmpty(objectText))
                        {
                            if (!first) Space(sb);
                            sb.Append(objectText);
                            first = false;
                        }
                        break;
                    case StructMarkedContent mc when mc.PageIndex is { } p:
                        var t = GetMarkedContentText(p, mc);
                        if (!string.IsNullOrEmpty(t))
                        {
                            if (!first) Space(sb);
                            sb.Append(t);
                            first = false;
                        }
                        break;
                }
            }
            if (IncludeAlt && sb.Length == start && !string.IsNullOrEmpty(e.Alt)) sb.Append(e.Alt);
        }

        if (type == "Lbl") Space(sb);
        else if (!isInline && !isCell) Break(sb);
    }
}
