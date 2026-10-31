using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Conservative auto-tagging of an untagged page content stream. Every text object (BT..ET) and every Form XObject that paints text becomes marked content
/// with an MCID (role P); every image (Image XObject or inline) becomes marked content with role Figure and no alternate text, so validators keep flagging it;
/// only vector paths and shadings are marked as Artifact. No other semantics are inferred. The result always needs human review.
/// </summary>
internal static class ContentAutoTagger
{
    internal enum DoKind { Artifact, Text, Figure }

    /// <param name="Blocks">Marked-content blocks in content order; MCIDs are the indexes.</param>
    internal sealed record Result(byte[] Content, IReadOnlyList<string> Blocks, int ArtifactBlocks, bool AlreadyMarked);

    static readonly HashSet<string> PathOps = new(StringComparer.Ordinal) { "m", "l", "c", "v", "y", "h", "re" };
    static readonly HashSet<string> PaintOps = new(StringComparer.Ordinal) { "S", "s", "f", "F", "f*", "B", "B*", "b", "b*" };

    const int MaxTokens = 20_000_000;

    public static Result Tag(byte[] content, Func<string, DoKind> classify)
    {
        var lex = new PdfLexer(content);
        var wraps = new List<(int Start, int End, string? Role)>();
        int textStart = -1, pathStart = -1, imageStart = -1, opStart = -1, tokens = 0;
        string? lastName = null;
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
            if (o is PdfName n) { lastName = n.Value; continue; }
            if (o is not PdfKeyword kw) continue;
            var op = kw.Value;
            var start = opStart;
            var name = lastName;
            opStart = -1;
            lastName = null;
            switch (op)
            {
                case "BDC" or "BMC":
                    marked = true;
                    break;
                case "BT":
                    if (textStart < 0 && imageStart < 0) textStart = start;
                    break;
                case "ET":
                    if (textStart >= 0) { wraps.Add((textStart, lex.Pos, "P")); textStart = -1; }
                    break;
                case "BI":
                    if (textStart < 0) imageStart = start;
                    break;
                case "ID":
                    PdfUADeepChecks.SkipInlineImage(lex);
                    if (imageStart >= 0) { wraps.Add((imageStart, lex.Pos, "Figure")); imageStart = -1; }
                    break;
                case "sh":
                    if (textStart < 0) wraps.Add((start, lex.Pos, null));
                    break;
                case "Do":
                    if (textStart < 0)
                        wraps.Add((start, lex.Pos, name is null ? null : classify(name) switch { DoKind.Text => "P", DoKind.Figure => "Figure", _ => null }));
                    break;
                default:
                    if (textStart >= 0) break;
                    if (PathOps.Contains(op)) { if (pathStart < 0) pathStart = start; }
                    else if (PaintOps.Contains(op)) { if (pathStart >= 0) wraps.Add((pathStart, lex.Pos, null)); pathStart = -1; }
                    else if (op == "n") pathStart = -1;
                    break;
            }
        }
        if (marked) return new Result(content, [], 0, true);

        var output = new MemoryStream(content.Length + wraps.Count * 40);
        var at = 0;
        var blocks = new List<string>();
        var artifacts = 0;
        foreach (var (s, e, role) in wraps.OrderBy(w => w.Start))
        {
            if (s < at || e > content.Length) continue;
            output.Write(content, at, s - at);
            if (role is null) { output.Write("/Artifact BMC\n"u8); artifacts++; }
            else { output.Write(System.Text.Encoding.ASCII.GetBytes($"/{role} <</MCID {blocks.Count}>> BDC\n")); blocks.Add(role); }
            output.Write(content, s, e - s);
            output.Write("\nEMC\n"u8);
            at = e;
        }
        output.Write(content, at, content.Length - at);
        return new Result(output.ToArray(), blocks, artifacts, false);
    }
}
