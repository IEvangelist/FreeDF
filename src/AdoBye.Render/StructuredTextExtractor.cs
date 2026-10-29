using System.Text;
using AdoBye.Document;

namespace AdoBye.Render;

/// <summary>Reading-order text extraction that follows the logical structure tree instead of content-stream order (ISO 32000-2 14.8.2.3).</summary>
public sealed class StructuredTextExtractor
{
    static readonly HashSet<string> Inline = new(StringComparer.Ordinal)
        { "Span", "Quote", "Code", "Link", "Reference", "Note", "Sub", "Annot", "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP", "Lbl", "BibEntry" };

    readonly PdfDocument _doc;
    readonly Dictionary<int, Dictionary<int, string>> _pageText = [];

    /// <summary>Emit /Alt for elements (typically figures) that carry no text of their own.</summary>
    public bool IncludeAlt { get; init; }

    public StructuredTextExtractor(PdfDocument doc) => _doc = doc;

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
        PageText(pageIndex).TryGetValue(mcid, out var t) ? t : null;

    Dictionary<int, string> PageText(int pageIndex)
    {
        if (_pageText.TryGetValue(pageIndex, out var d)) return d;
        d = [];
        _pageText[pageIndex] = d;
        if (pageIndex < 0 || pageIndex >= _doc.Pages.Count) return d;
        List<AdoBye.Content.ContentInterpreter.TextSpan> spans;
        try { spans = PdfRenderer.ExtractText(_doc.Pages[pageIndex]); }
        catch (Exception e) when (e is AdoBye.Core.PdfParseException or InvalidCastException or InvalidDataException) { return d; }
        var sbs = new Dictionary<int, StringBuilder>();
        var last = new Dictionary<int, AdoBye.Content.ContentInterpreter.TextSpan>();
        foreach (var s in spans)
        {
            if (s.Mcid < 0) continue;
            if (!sbs.TryGetValue(s.Mcid, out var sb)) sbs[s.Mcid] = sb = new StringBuilder();
            if (last.TryGetValue(s.Mcid, out var p) && NeedsSpace(p, s) && sb.Length > 0 && !char.IsWhiteSpace(sb[^1]) && s.Text.Length > 0 && !char.IsWhiteSpace(s.Text[0])) sb.Append(' ');
            sb.Append(s.Text);
            last[s.Mcid] = s;
        }
        foreach (var (k, v) in sbs) d[k] = v.ToString();
        return d;
    }

    static bool NeedsSpace(AdoBye.Content.ContentInterpreter.TextSpan prev, AdoBye.Content.ContentInterpreter.TextSpan cur)
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
                    case StructMarkedContent mc when mc.PageIndex is { } p && mc.Stream is null:
                        if (PageText(p).TryGetValue(mc.Mcid, out var t) && t.Length > 0)
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
