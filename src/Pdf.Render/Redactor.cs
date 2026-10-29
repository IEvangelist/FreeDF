using Pdf.Content;
using Pdf.Document;

namespace Pdf.Render;

/// <summary>
/// True redaction: content that intersects a redaction rectangle is removed from the content stream (not merely covered),
/// then an opaque black box is painted over the area. Text runs, paths, images and form XObjects are removed whole
/// when their bounds intersect, so no hidden data survives in the saved file.
/// </summary>
public static class Redactor
{
    /// <summary>Rectangle in page box space (origin top-left of the CropBox, y down), as used by text spans and objects.</summary>
    public readonly record struct BoxRect(double X, double Y, double W, double H);

    /// <summary>Returns the redacted page content for <paramref name="page"/>; set it with <see cref="PdfEditor.SetContentOverride"/>.</summary>
    public static byte[] Redact(PdfPage page, IReadOnlyList<BoxRect> rects)
    {
        var content = page.GetContents();
        if (rects.Count == 0) return content;
        var (spans, objects) = PdfRenderer.Analyze(page);

        bool Hit(double x0, double y0, double x1, double y1)
        {
            foreach (var r in rects)
                if (x0 < r.X + r.W && x1 > r.X && y0 < r.Y + r.H && y1 > r.Y) return true;
            return false;
        }

        // Span boxes are padded vertically because the span origin convention is the top of the line.
        var removals = spans
            .Where(s => Hit(s.X, s.Y - s.Size * 0.25, s.X + Math.Max(s.Width, s.Size * 0.1), s.Y + s.Size * 1.25))
            .ToDictionary(s => s.Index, s => s.Advance);
        var edits = objects.Where(o => Hit(o.X0, o.Y0, o.X1, o.Y1))
            .Select(o => new ContentRewriter.ObjectEdit(o.Start, o.End, true, null, null, null));
        content = ContentRewriter.EditObjects(content, edits);
        // Object edits only shrink byte ranges after the text-operator indices are computed, which are unaffected.
        content = ContentRewriter.RemoveText(content, removals);

        var box = page.CropBox;
        var sb = new System.Text.StringBuilder("\nq 0 g\n");
        foreach (var r in rects)
            sb.Append(FormattableString.Invariant($"{box.Left + r.X:0.###} {box.Top - r.Y - r.H:0.###} {r.W:0.###} {r.H:0.###} re f\n"));
        sb.Append("Q\n");
        var tail = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[content.Length + tail.Length];
        content.CopyTo(result, 0);
        tail.CopyTo(result, content.Length);
        return result;
    }
}
