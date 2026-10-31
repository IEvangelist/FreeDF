using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Conservative auto-tagging of an untagged page content stream: every text object (BT..ET) becomes marked content with an MCID (to be tagged P),
/// and every other painted object (path, shading, image, form, inline image) is marked as an Artifact. No semantics are inferred: no headings, lists,
/// tables or figures, and no alternate text. The result still needs human review before it can be called accessible.
/// </summary>
internal static class ContentAutoTagger
{
    internal sealed record Result(byte[] Content, int TextBlocks, int ArtifactBlocks, bool AlreadyMarked);

    static readonly HashSet<string> PathOps = new(StringComparer.Ordinal) { "m", "l", "c", "v", "y", "h", "re" };
    static readonly HashSet<string> PaintOps = new(StringComparer.Ordinal) { "S", "s", "f", "F", "f*", "B", "B*", "b", "b*" };

    const int MaxTokens = 20_000_000;

    public static Result Tag(byte[] content)
    {
        var lex = new PdfLexer(content);
        var wraps = new List<(int Start, int End, int? Mcid)>();
        int textStart = -1, pathStart = -1, imageStart = -1, opStart = -1, tokens = 0;
        var marked = false;
        while (tokens++ < MaxTokens)
        {
            lex.SkipWhite();
            if (lex.Pos >= lex.Length) break;
            var before = lex.Pos;
            if (opStart < 0) opStart = before;
            PdfObject? o;
            try { o = lex.ReadObject(false); }
            catch (PdfParseException) { lex.Pos = before + 1; continue; }
            if (o is null) break;
            if (lex.Pos <= before) lex.Pos = before + 1;
            if (o is not PdfKeyword kw) continue;
            var op = kw.Value;
            var start = opStart;
            opStart = -1;
            switch (op)
            {
                case "BDC" or "BMC":
                    marked = true;
                    break;
                case "BT":
                    if (textStart < 0 && imageStart < 0) textStart = start;
                    break;
                case "ET":
                    if (textStart >= 0) { wraps.Add((textStart, lex.Pos, 0)); textStart = -1; }
                    break;
                case "BI":
                    if (textStart < 0) imageStart = start;
                    break;
                case "ID":
                    PdfUADeepChecks.SkipInlineImage(lex);
                    if (imageStart >= 0) { wraps.Add((imageStart, lex.Pos, null)); imageStart = -1; }
                    break;
                case "sh" or "Do":
                    if (textStart < 0) wraps.Add((start, lex.Pos, null));
                    break;
                default:
                    if (textStart >= 0) break;
                    if (PathOps.Contains(op)) { if (pathStart < 0) pathStart = start; }
                    else if (PaintOps.Contains(op)) { if (pathStart >= 0) wraps.Add((pathStart, lex.Pos, null)); pathStart = -1; }
                    else if (op == "n") pathStart = -1;
                    break;
            }
        }
        if (marked) return new Result(content, 0, 0, true);

        var output = new MemoryStream(content.Length + wraps.Count * 40);
        var at = 0;
        int mcid = 0, text = 0, artifacts = 0;
        foreach (var (s, e, kind) in wraps.OrderBy(w => w.Start))
        {
            if (s < at || e > content.Length) continue;
            output.Write(content, at, s - at);
            if (kind is null) { output.Write("/Artifact BMC\n"u8); artifacts++; }
            else { output.Write(System.Text.Encoding.ASCII.GetBytes($"/P <</MCID {mcid++}>> BDC\n")); text++; }
            output.Write(content, s, e - s);
            output.Write("\nEMC\n"u8);
            at = e;
        }
        output.Write(content, at, content.Length - at);
        return new Result(output.ToArray(), text, artifacts, false);
    }
}
