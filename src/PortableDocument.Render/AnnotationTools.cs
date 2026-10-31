using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

namespace PortableDocument.Render;

/// <summary>Annotation helpers that need the renderer: glyph-accurate text-markup quads and applying redaction annotations.</summary>
public static class AnnotationTools
{
    /// <summary>A run of selected glyphs on one line: its box (CropBox space, y down) and the text it covers.</summary>
    public readonly record struct TextRun(double X0, double Y0, double X1, double Y1, string Text);

    /// <summary>
    /// Finds the glyphs of the page whose centres lie inside <paramref name="region"/> (CropBox space, y down) and groups consecutive glyphs of a text
    /// operator into one run each. Boxes come from the glyph geometry of the content interpreter, so they follow the font's real advances.
    /// </summary>
    public static List<TextRun> SelectText(PdfPage page, Redactor.BoxRect region)
    {
        var runs = new List<TextRun>();
        double rx1 = region.X + region.W, ry1 = region.Y + region.H;
        foreach (var span in PortableDocument.ExtractText(page))
        {
            if (span.Vertical || span.Pieces is null) continue;
            var glyphs = span.Pieces.Where(p => p.Glyphs is not null).SelectMany(p => p.Glyphs!).ToList();
            if (glyphs.Count == 0) continue;
            int? first = null; double x0 = 0, y0 = 0, x1 = 0, y1 = 0; var count = 0;
            void Flush(int last)
            {
                if (first is null) return;
                var text = span.Text;
                string slice = "";
                if (text.Length == glyphs.Count) slice = text.Substring(first.Value, last - first.Value + 1);
                else if (text.Length > 0)
                {
                    var from = (int)Math.Round((double)first.Value * text.Length / glyphs.Count);
                    var to = Math.Max(from, (int)Math.Round((double)(last + 1) * text.Length / glyphs.Count));
                    slice = text[from..Math.Min(to, text.Length)];
                }
                runs.Add(new TextRun(x0, y0, x1, y1, slice));
                first = null; count = 0;
            }
            for (var i = 0; i < glyphs.Count; i++)
            {
                var g = glyphs[i];
                double gx0 = Math.Min(g.X0, g.X1), gx1 = Math.Max(g.X0, g.X1), gy0 = Math.Min(g.Y0, g.Y1), gy1 = Math.Max(g.Y0, g.Y1);
                double cx = (gx0 + gx1) / 2, cy = (gy0 + gy1) / 2;
                var inside = cx >= region.X && cx <= rx1 && cy >= region.Y && cy <= ry1 && gx1 > gx0;
                if (!inside) { Flush(i - 1); continue; }
                if (first is null) { first = i; x0 = gx0; y0 = gy0; x1 = gx1; y1 = gy1; }
                else { x0 = Math.Min(x0, gx0); y0 = Math.Min(y0, gy0); x1 = Math.Max(x1, gx1); y1 = Math.Max(y1, gy1); }
                count++;
            }
            Flush(glyphs.Count - 1);
        }
        return runs;
    }

    /// <summary>Converts runs (CropBox space) into PDF user space quad points for a text-markup annotation.</summary>
    public static double[] QuadsFromRuns(PdfPage page, IEnumerable<TextRun> runs)
    {
        var box = page.CropBox;
        return AnnotationAppearance.QuadsFromBoxes(runs.Select(r => (box.Left + r.X0, box.Top - r.Y1, box.Left + r.X1, box.Top - r.Y0)));
    }

    /// <summary>
    /// Applies every Redact annotation of a page with the <see cref="Redactor"/>: content under the marked quads or rectangles is removed and
    /// painted over (annotations under the areas, including the marks, are removed by the redactor). Returns the number of marks applied.
    /// </summary>
    public static int ApplyRedactionAnnotations(PdfEditor editor, int pageIndex)
    {
        var marks = editor.ListAnnotations(pageIndex).Where(i => i.Spec.Subtype == "Redact").ToList();
        if (marks.Count == 0) return 0;
        var doc = PdfDocument.Load(editor.Save(false, true));
        var box = doc.Pages[pageIndex].CropBox;
        var rects = new List<Redactor.BoxRect>();
        foreach (var m in marks)
        {
            var q = m.Spec.QuadPoints;
            if (q is { Length: >= 8 })
                for (var i = 0; i + 8 <= q.Length; i += 8)
                {
                    var b = AnnotationAppearance.Bounds(q.Skip(i).Take(8));
                    rects.Add(new(b[0] - box.Left, box.Top - b[3], b[2] - b[0], b[3] - b[1]));
                }
            else if (m.Spec.Rect is { Length: 4 } r)
                rects.Add(new(Math.Min(r[0], r[2]) - box.Left, box.Top - Math.Max(r[1], r[3]), Math.Abs(r[2] - r[0]), Math.Abs(r[3] - r[1])));
        }
        Redactor.Apply(editor, pageIndex, rects);
        foreach (var m in marks) editor.DeleteAnnotation(pageIndex, m.Number);
        return marks.Count;
    }
}
