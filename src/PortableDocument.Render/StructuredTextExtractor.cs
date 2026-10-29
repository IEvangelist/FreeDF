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
    readonly Dictionary<PdfStream, int> _streamNumbers = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<PdfStream> _structuredStreams = new(ReferenceEqualityComparer.Instance);

    /// <summary>Emit /Alt for elements (typically figures) that carry no text of their own.</summary>
    public bool IncludeAlt { get; init; }

    public StructuredTextExtractor(PdfDocument doc)
    {
        _doc = doc;
        if (doc.Structure is { } tree)
            foreach (var e in tree.Elements())
                foreach (var mc in e.MarkedContent)
                    if (mc.Stream is { } stream) _structuredStreams.Add(stream);
    }

    /// <summary>Text of the whole document in structure order, or null when the document is untagged.</summary>
    public static string? Extract(PdfDocument doc, bool includeAlt = false) => new StructuredTextExtractor(doc) { IncludeAlt = includeAlt }.Extract();

    public string? Extract()
    {
        var tree = _doc.Structure;
        if (tree is null) return null;
        var sb = new StringBuilder();
        foreach (var c in tree.Children) Walk(c, sb, 0);
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Text of one structure element and its descendants.</summary>
    public string GetText(StructElement element)
    {
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

    Dictionary<(int Form, int Mcid), string> PageText(int pageIndex)
    {
        if (_pageText.TryGetValue(pageIndex, out var d)) return d;
        d = [];
        _pageText[pageIndex] = d;
        if (pageIndex < 0 || pageIndex >= _doc.Pages.Count) return d;
        List<global::PortableDocument.Content.ContentInterpreter.TextSpan> spans;
        try { spans = PortableDocument.ExtractText(_doc.Pages[pageIndex]); }
        catch (Exception e) when (e is global::PortableDocument.Core.PdfParseException or InvalidCastException or InvalidDataException) { return d; }
        var sbs = new Dictionary<(int Form, int Mcid), StringBuilder>();
        var last = new Dictionary<(int Form, int Mcid), global::PortableDocument.Content.ContentInterpreter.TextSpan>();
        var seen = new HashSet<(int Form, int Index)>();
        foreach (var s in spans)
        {
            if (s.Mcid < 0) continue;
            var form = s.Form;
            if (form > 0 && _doc.File.GetObject(form) is PdfStream stream)
            {
                _streamNumbers[stream] = form;
                // Untagged forms invoked inside page marked content retain the caller's page MCID.
                if (stream.Dict.Get("StructParents") is null && !_structuredStreams.Contains(stream)) form = 0;
            }
            if (form != 0 && !seen.Add((form, s.Index))) continue;
            var key = (form, s.Mcid);
            if (!sbs.TryGetValue(key, out var sb)) sbs[key] = sb = new StringBuilder();
            if (last.TryGetValue(key, out var p) && NeedsSpace(p, s) && sb.Length > 0 && !char.IsWhiteSpace(sb[^1]) && s.Text.Length > 0 && !char.IsWhiteSpace(s.Text[0])) sb.Append(' ');
            sb.Append(s.Text);
            last[key] = s;
        }
        foreach (var (k, v) in sbs) d[k] = v.ToString();
        return d;
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
                    case StructMarkedContent mc when mc.PageIndex is { } p:
                        var t = mc.Stream is { } stream ? GetMarkedContentText(p, stream, mc.Mcid) : GetMarkedContentText(p, mc.Mcid);
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
