using System.Globalization;
using System.IO.Compression;
using System.Text;
using Pdf.Content;
using Pdf.Core;
using Pdf.Document;

namespace Pdf.Render;

/// <summary>
/// True redaction: content inside a redaction rectangle is removed from the content (not merely covered), then an opaque
/// black box is painted over the area.
/// <list type="bullet">
/// <item>Text: only the glyphs whose boxes touch a rectangle are dropped (the operator becomes a <c>TJ</c> that keeps the surviving glyphs in place).</item>
/// <item>Images: a partly covered raster image is re-encoded with the covered pixels blanked; a fully covered one is removed.</item>
/// <item>Form XObjects: text and images inside forms are redacted in the form's own stream (a form shared by several pages or invocations is redacted everywhere, which over-removes but never leaks).</item>
/// <item>Vector paths, inline images and image masks that touch a rectangle are removed whole.</item>
/// <item>With a <see cref="PdfEditor"/>: annotations under a rectangle are removed (with their AcroForm field entries), outline titles sharing a word with redacted text are blanked, and XMP/Info/thumbnail metadata is scrubbed.</item>
/// </list>
/// </summary>
public static class Redactor
{
    /// <summary>Rectangle in page box space (origin top-left of the CropBox, y down), as used by text spans and objects.</summary>
    public readonly record struct BoxRect(double X, double Y, double W, double H);

    sealed record NewImage(string Name, byte[] Data, int W, int H, byte[]? Alpha, int Form);
    sealed record Plan(byte[] Page, Dictionary<int, byte[]> Forms, List<NewImage> Images, List<string> HitText);

    /// <summary>Returns the redacted page content for <paramref name="page"/> (form XObjects and images are removed whole in this overload); set it with <see cref="PdfEditor.SetContentOverride"/>.</summary>
    public static byte[] Redact(PdfPage page, IReadOnlyList<BoxRect> rects)
    {
        if (rects.Count == 0) return page.GetContents();
        return Tail(Build(page, rects, editorMode: false).Page, page, rects);
    }

    /// <summary>
    /// Redacts page <paramref name="pageIndex"/> of <paramref name="editor"/> in place: page content, form XObject internals,
    /// partially covered images, annotations under the rectangles and document metadata.
    /// </summary>
    public static void Apply(PdfEditor editor, int pageIndex, IReadOnlyList<BoxRect> rects)
    {
        if (rects.Count == 0) return;
        var map = new Dictionary<int, int>();
        var doc = PdfDocument.Load(editor.Save(false, true, new PdfWriteOptions { ObjectMapOut = map }));
        var back = map.ToDictionary(kv => kv.Value, kv => kv.Key);
        var page = doc.Pages[pageIndex];
        var plan = Build(page, rects, editorMode: true);

        var formNames = new Dictionary<int, Dictionary<string, PdfObject>>();
        foreach (var im in plan.Images)
        {
            var dict = new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Image"), ["Width"] = new PdfInteger(im.W), ["Height"] = new PdfInteger(im.H),
                ["ColorSpace"] = new PdfName("DeviceRGB"), ["BitsPerComponent"] = new PdfInteger(8), ["Filter"] = new PdfName("FlateDecode"),
            };
            if (im.Alpha is not null)
            {
                var ad = Deflate(im.Alpha);
                dict["SMask"] = editor.AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
                {
                    ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Image"), ["Width"] = new PdfInteger(im.W), ["Height"] = new PdfInteger(im.H),
                    ["ColorSpace"] = new PdfName("DeviceGray"), ["BitsPerComponent"] = new PdfInteger(8), ["Filter"] = new PdfName("FlateDecode"), ["Length"] = new PdfInteger(ad.Length),
                }), ad));
            }
            var z = Deflate(im.Data);
            dict["Length"] = new PdfInteger(z.Length);
            var r = editor.AddObject(new PdfStream(new PdfDictionary(dict), z));
            if (im.Form == 0) editor.AddPageXObject(pageIndex, im.Name, r);
            else
            {
                var en = back.GetValueOrDefault(im.Form);
                if (en == 0) continue;
                if (!formNames.TryGetValue(en, out var fd)) formNames[en] = fd = [];
                fd[im.Name] = r;
            }
        }

        editor.SetContentOverride(pageIndex, Tail(plan.Page, page, rects));
        foreach (var (num, data) in plan.Forms)
        {
            var en = back.GetValueOrDefault(num);
            if (en != 0) editor.SetStreamOverride(en, data, formNames.GetValueOrDefault(en));
        }

        var box = page.CropBox;
        editor.RemoveAnnotations(pageIndex, a =>
        {
            if (doc.File.Resolve(a.Get("Rect")) is not PdfArray ra || ra.Count < 4) return false;
            var v = ra.Items.Select(x => doc.File.Resolve(x).AsNumber() ?? 0).ToArray();
            double x0 = Math.Min(v[0], v[2]) - box.Left, x1 = Math.Max(v[0], v[2]) - box.Left, y0 = box.Top - Math.Max(v[1], v[3]), y1 = box.Top - Math.Min(v[1], v[3]);
            return rects.Any(r => x0 < r.X + r.W && x1 > r.X && y0 < r.Y + r.H && y1 > r.Y);
        });
        editor.PruneRemovedFormFields();
        // Outline titles that share a word with redacted text are blanked (conservative: may rewrite a title that merely shares a word).
        var words = plan.HitText.SelectMany(t => t.Split([' ', '\t', '\n', ',', '.', ';', ':', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length >= 3).Select(w => w.ToLowerInvariant()).ToHashSet();
        if (words.Count > 0)
            editor.RedactOutlineTitles(title => title.Split([' ', '\t', '\n', ',', '.', ';', ':', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Any(w => words.Contains(w.ToLowerInvariant())));
        editor.ScrubMetadata();
    }

    static byte[] Deflate(byte[] d)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(d);
        return ms.ToArray();
    }

    static byte[] Tail(byte[] content, PdfPage page, IReadOnlyList<BoxRect> rects)
    {
        var box = page.CropBox;
        var sb = new StringBuilder("\nq 0 g\n");
        foreach (var r in rects)
            sb.Append(FormattableString.Invariant($"{box.Left + r.X:0.###} {box.Top - r.Y - r.H:0.###} {r.W:0.###} {r.H:0.###} re f\n"));
        sb.Append("Q\n");
        var tail = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[content.Length + tail.Length];
        content.CopyTo(result, 0);
        tail.CopyTo(result, content.Length);
        return result;
    }

    static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    static Plan Build(PdfPage page, IReadOnlyList<BoxRect> rects, bool editorMode)
    {
        var file = page.Document.File;
        var (spans, objects) = PdfRenderer.Analyze(page);

        bool Hit(double x0, double y0, double x1, double y1)
        {
            if (x0 > x1) (x0, x1) = (x1, x0);
            if (y0 > y1) (y0, y1) = (y1, y0);
            foreach (var r in rects)
                if (x0 < r.X + r.W && x1 > r.X && y0 < r.Y + r.H && y1 > r.Y) return true;
            return false;
        }
        bool Covered(double x0, double y0, double x1, double y1) =>
            rects.Any(r => x0 >= r.X - 0.5 && x1 <= r.X + r.W + 0.5 && y0 >= r.Y - 0.5 && y1 <= r.Y + r.H + 0.5);

        var streams = spans.Select(s => s.Form).Concat(objects.Select(o => o.Form)).Append(0).Distinct().ToList();
        var forms = new Dictionary<int, byte[]>();
        var images = new List<NewImage>(); var hitText = new List<string>();
        var pageContent = page.GetContents();
        var imgCounter = 0; var prefix = Guid.NewGuid().ToString("N")[..8];

        foreach (var form in streams)
        {
            if (form != 0 && !editorMode) continue;
            PdfStream? fs = null;
            var res = page.Resources;
            if (form != 0)
            {
                if (file.GetObject(form) is not PdfStream s) continue;
                fs = s;
                res = file.Resolve(s.Dict.Get("Resources")) as PdfDictionary ?? page.Resources;
            }
            var content = form == 0 ? pageContent : file.DecodeStream(fs!);

            var removals = new Dictionary<int, string>();
            foreach (var sp in spans.Where(x => x.Form == form))
            {
                if (removals.ContainsKey(sp.Index)) continue;
                if (sp.Pieces is null)
                {
                    if (Hit(sp.X, sp.Y - sp.Size * 0.25, sp.X + Math.Max(sp.Width, sp.Size * 0.1), sp.Y + sp.Size * 1.25)) { removals[sp.Index] = N(sp.Advance); hitText.Add(sp.Text); }
                    continue;
                }
                var any = false; var sb = new StringBuilder(); var run = new StringBuilder();
                void Flush() { if (run.Length > 0) { sb.Append('<').Append(run).Append('>'); run.Clear(); } }
                foreach (var p in sp.Pieces)
                {
                    if (p.Adj is { } adj) { Flush(); sb.Append(N(adj)).Append(' '); continue; }
                    foreach (var g in p.Glyphs!)
                    {
                        var inset = Math.Abs(g.X1 - g.X0) * 0.05;
                        var lo = Math.Min(g.X0, g.X1) + inset; var hi = Math.Max(g.X0, g.X1) - inset;
                        if (Hit(lo, g.Y0, hi, g.Y1)) { any = true; Flush(); sb.Append(N(g.Adv)).Append(' '); }
                        else run.Append(Convert.ToHexString(g.Bytes));
                    }
                    Flush();
                }
                if (any) { removals[sp.Index] = sb.ToString(); hitText.Add(sp.Text); }
            }

            var edits = new List<ContentRewriter.ObjectEdit>();
            var seen = new HashSet<(int, int)>();
            foreach (var o in objects.Where(x => x.Form == form))
            {
                if (!seen.Add((o.Start, o.End)) || !Hit(o.X0, o.Y0, o.X1, o.Y1)) continue;
                if (o.Kind == "form" && o.Ref > 0 && editorMode) continue;
                if (o.Kind == "image" && editorMode && o.Stream is { } img && o.Ref > 0 && !Covered(o.X0, o.Y0, o.X1, o.Y1)
                    && (form == 0 || file.Resolve(fs!.Dict.Get("Resources")) is PdfDictionary)
                    && ImageDecoder.Decode(file, img, res, out var isMask, out _) is { } data && !isMask)
                {
                    var (rgb, alpha) = Blank(data, o.Ctm, rects);
                    var name = $"RdIm{prefix}{imgCounter++}";
                    images.Add(new NewImage(name, rgb, data.Width, data.Height, alpha, form));
                    edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, null, "/" + name + " Do"));
                    continue;
                }
                edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, true, null, null, null));
            }
            content = ContentRewriter.EditObjects(content, edits);
            content = ContentRewriter.ReplaceText(content, removals);
            if (form == 0) pageContent = content; else forms[form] = content;
        }
        return new Plan(pageContent, forms, images, hitText);
    }

    // Blanks pixels whose centre maps (through the image CTM) into a redaction rectangle; the alpha plane is null when everything is opaque.
    static (byte[] Rgb, byte[]? Alpha) Blank(ImageData d, Matrix ctm, IReadOnlyList<BoxRect> rects)
    {
        var rgb = new byte[d.Width * d.Height * 3];
        var alpha = new byte[d.Width * d.Height];
        var translucent = false;
        for (var y = 0; y < d.Height; y++)
            for (var x = 0; x < d.Width; x++)
            {
                var i = y * d.Width + x;
                var (dx, dy) = ctm.Apply((x + 0.5) / d.Width, 1 - (y + 0.5) / d.Height);
                var inside = false;
                foreach (var r in rects) if (dx >= r.X && dx <= r.X + r.W && dy >= r.Y && dy <= r.Y + r.H) { inside = true; break; }
                if (inside) { alpha[i] = 255; continue; }
                rgb[i * 3] = d.Rgba[i * 4]; rgb[i * 3 + 1] = d.Rgba[i * 4 + 1]; rgb[i * 3 + 2] = d.Rgba[i * 4 + 2];
                alpha[i] = d.Rgba[i * 4 + 3];
                if (alpha[i] != 255) translucent = true;
            }
        return (rgb, translucent ? alpha : null);
    }
}
