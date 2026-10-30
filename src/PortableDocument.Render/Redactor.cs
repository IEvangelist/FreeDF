using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

namespace PortableDocument.Render;

/// <summary>
/// True redaction: content inside a redaction rectangle is removed from the content (not merely covered), then an opaque
/// black box is painted over the area.
/// <list type="bullet">
/// <item>Text: only the glyphs whose boxes touch a rectangle are dropped (the operator becomes a <c>TJ</c> that keeps the surviving glyphs in place).</item>
/// <item>Images: a partly covered raster image is re-encoded with the covered pixels blanked; a fully covered one is removed.</item>
/// <item>Form XObjects: text and images inside forms are redacted in the form's own stream (a form shared by several pages or invocations is redacted everywhere, which over-removes but never leaks).</item>
/// <item>Single positive-size <c>re</c> paths in page content (optionally followed by <c>W</c>/<c>W*</c> and then <c>f</c>/<c>F</c>/<c>f*</c> or <c>n</c>) are clipped when their CTM is axis-aligned; other vector paints are removed whole, and unsupported clip-only paths become empty clips.</item>
/// <item>With a <see cref="PdfEditor"/>: annotations under a rectangle are removed (with their AcroForm field entries), matching redacted phrases in outline titles are replaced, and XMP/Info/thumbnail metadata is scrubbed.</item>
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
    /// partially covered images, annotations under the rectangles and document metadata. In outline titles, replaces
    /// contiguous removed text (trimmed) at word boundaries with [redacted], using canonical Unicode equivalence,
    /// whitespace-run equivalence and invariant case-insensitive matching. Punctuation remains literal.
    /// Letters, marks, numbers, connector punctuation and surrogate code units are word characters.
    /// </summary>
    public static void Apply(PdfEditor editor, int pageIndex, IReadOnlyList<BoxRect> rects)
    {
        if (rects.Count == 0) return;
        var map = new Dictionary<int, int>();
        var doc = PdfDocument.Load(editor.Save(false, true, new PdfWriteOptions { ObjectMapOut = map }));
        var back = map.ToDictionary(kv => kv.Value, kv => kv.Key);
        var page = doc.Pages[pageIndex];
        var plan = Build(page, rects, editorMode: true);
        editor.RequireFullRewrite();

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
        var phrases = plan.HitText.Select(t => NormalizeOutlineText(t).Text.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(t => t.Length).ThenBy(t => t, StringComparer.Ordinal).ToArray();
        if (phrases.Length > 0)
        {
            // Match normalized phrases, longest first, and replace only their original title ranges.
            const string word = @"[\p{L}\p{M}\p{N}\p{Pc}\p{Cs}]";
            var matches = new Regex($@"(?<!{word})(?:{string.Join("|", phrases.Select(Regex.Escape))})(?!{word})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            editor.TransformOutlineTitles(title =>
            {
                var normalized = NormalizeOutlineText(title);
                var result = new StringBuilder();
                var end = 0;
                foreach (Match match in matches.Matches(normalized.Text))
                {
                    var start = normalized.Starts[match.Index];
                    result.Append(title, end, start - end).Append("[redacted]");
                    end = normalized.Ends[match.Index + match.Length - 1];
                }
                return result.Append(title, end, title.Length - end).ToString();
            });
        }
        editor.ScrubMetadata();
    }

    static (string Text, List<int> Starts, List<int> Ends) NormalizeOutlineText(string text)
    {
        var normalized = new StringBuilder();
        var starts = new List<int>();
        var ends = new List<int>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var start = elements.ElementIndex;
            var end = start + element.Length;
            if (element.All(char.IsWhiteSpace))
            {
                if (normalized.Length > 0 && normalized[^1] == ' ')
                {
                    ends[^1] = end;
                    continue;
                }
                element = " ";
            }
            else element = element.Normalize(NormalizationForm.FormD);
            normalized.Append(element);
            // A decomposed/reordered text element still maps to its complete original range.
            foreach (var _ in element) { starts.Add(start); ends.Add(end); }
        }
        return (normalized.ToString(), starts, ends);
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
        // Isolate source graphics state so its clipping path cannot constrain the redaction mask.
        var prefix = "q\n"u8.ToArray();
        var sb = new StringBuilder("\nQ\nn\nq 0 g\n");
        foreach (var r in rects)
            sb.Append(FormattableString.Invariant($"{box.Left + r.X:0.###} {box.Top - r.Y - r.H:0.###} {r.W:0.###} {r.H:0.###} re f\n"));
        sb.Append("Q\n");
        var tail = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[prefix.Length + content.Length + tail.Length];
        prefix.CopyTo(result, 0);
        content.CopyTo(result, prefix.Length);
        tail.CopyTo(result, prefix.Length + content.Length);
        return result;
    }

    static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    static Plan Build(PdfPage page, IReadOnlyList<BoxRect> rects, bool editorMode)
    {
        var file = page.Document.File;
        var (spans, objects) = PortableDocument.Analyze(page);

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
                var any = false; var sb = new StringBuilder(); var run = new StringBuilder(); var hit = new StringBuilder();
                void Flush() { if (run.Length > 0) { sb.Append('<').Append(run).Append('>'); run.Clear(); } }
                void FlushHit() { if (hit.Length > 0) { hitText.Add(hit.ToString()); hit.Clear(); } }
                foreach (var p in sp.Pieces)
                {
                    if (p.Adj is { } adj)
                    {
                        Flush(); sb.Append(N(adj)).Append(' ');
                        if (adj <= -200 && hit.Length > 0) hit.Append(' ');
                        continue;
                    }
                    foreach (var g in p.Glyphs!)
                    {
                        var inset = Math.Abs(g.X1 - g.X0) * 0.05;
                        var lo = Math.Min(g.X0, g.X1) + inset; var hi = Math.Max(g.X0, g.X1) - inset;
                        if (Hit(lo, g.Y0, hi, g.Y1))
                        {
                            any = true; Flush(); sb.Append(N(g.Adv)).Append(' ');
                            if (sp.Font is { } font)
                                foreach (var glyph in font.Decode(g.Bytes)) hit.Append(font.ToUnicode(glyph));
                        }
                        else { FlushHit(); run.Append(Convert.ToHexString(g.Bytes)); }
                    }
                    Flush();
                }
                FlushHit();
                if (any)
                {
                    removals[sp.Index] = sb.ToString();
                    if (sp.Font is null) hitText.Add(sp.Text);
                }
            }

            var edits = new List<ContentRewriter.ObjectEdit>();
            var seen = new HashSet<(int, int)>();
            foreach (var o in objects.Where(x => x.Form == form))
            {
                if (!seen.Add((o.Start, o.End)) || !Hit(o.X0, o.Y0, o.X1, o.Y1)) continue;
                if (o.Kind == "form" && o.Ref > 0 && editorMode) continue;
                if (o.Kind is "path" or "clip-path" or "clipped-path")
                {
                    if (TryClipRectangleFill(content, o, rects, out var clipped))
                        edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, Replacement: clipped));
                    else if (o.Kind is "clip-path" or "clipped-path")
                        edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, Replacement: "0 0 m 0 0 l W n"));
                    else
                        edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, true, null, null, null));
                    continue;
                }
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

    static bool TryClipRectangleFill(byte[] content, ContentInterpreter.PageObject obj, IReadOnlyList<BoxRect> rects, out string replacement)
    {
        replacement = "";
        var clipOnly = obj.Kind == "clip-path";
        if (obj.Form != 0 || obj.HasStroke || obj.HasFill == clipOnly
            || obj.Ctm.B != 0 || obj.Ctm.C != 0 || obj.Ctm.A == 0 || obj.Ctm.D == 0
            || !double.IsFinite(obj.Ctm.A) || !double.IsFinite(obj.Ctm.D)
            || !double.IsFinite(obj.Ctm.Det) || Math.Abs(obj.Ctm.Det) < 1e-12
            || !TryReadRectangleFill(content, obj.Start, obj.End, out var x, out var y, out var w, out var h, out var clipOp, out var paintOp))
            return false;

        var ctm = obj.Ctm;
        var a = ctm.Apply(x, y);
        var b = ctm.Apply(x + w, y + h);
        var x0 = Math.Min(a.X, b.X); var x1 = Math.Max(a.X, b.X);
        var y0 = Math.Min(a.Y, b.Y); var y1 = Math.Max(a.Y, b.Y);
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1)
            || x1 <= x0 || y1 <= y0)
            return false;

        var cuts = rects.Where(r => r.W > 0 && r.H > 0
                && double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.W) && double.IsFinite(r.H)
                && r.X < x1 && r.X + r.W > x0 && r.Y < y1 && r.Y + r.H > y0)
            .ToArray();
        if (cuts.Length > 64) return false;

        var xs = new SortedSet<double> { x0, x1 };
        var ys = new SortedSet<double> { y0, y1 };
        foreach (var r in cuts)
        {
            xs.Add(Math.Clamp(r.X, x0, x1)); xs.Add(Math.Clamp(r.X + r.W, x0, x1));
            ys.Add(Math.Clamp(r.Y, y0, y1)); ys.Add(Math.Clamp(r.Y + r.H, y0, y1));
        }
        var xEdges = xs.ToArray(); var yEdges = ys.ToArray();
        if ((long)(xEdges.Length - 1) * (yEdges.Length - 1) > 4096) return false;

        var inv = ctm.Invert();
        var pieces = new StringBuilder();
        for (var yi = 0; yi + 1 < yEdges.Length; yi++)
            for (var xi = 0; xi + 1 < xEdges.Length; xi++)
            {
                var cx0 = xEdges[xi]; var cx1 = xEdges[xi + 1];
                var cy0 = yEdges[yi]; var cy1 = yEdges[yi + 1];
                var mx = cx0 + (cx1 - cx0) / 2; var my = cy0 + (cy1 - cy0) / 2;
                if (cuts.Any(r => mx >= r.X && mx < r.X + r.W && my >= r.Y && my < r.Y + r.H)) continue;

                var p0 = inv.Apply(cx0, cy0); var p1 = inv.Apply(cx1, cy1);
                var ux0 = Math.Min(p0.X, p1.X); var ux1 = Math.Max(p0.X, p1.X);
                var uy0 = Math.Min(p0.Y, p1.Y); var uy1 = Math.Max(p0.Y, p1.Y);
                if (!double.IsFinite(ux0) || !double.IsFinite(uy0) || !double.IsFinite(ux1) || !double.IsFinite(uy1)
                    || ux1 <= ux0 || uy1 <= uy0)
                    return false;
                var q0 = ctm.Apply(ux0, uy0); var q1 = ctm.Apply(ux1, uy1);
                var bx0 = Math.Min(q0.X, q1.X); var bx1 = Math.Max(q0.X, q1.X);
                var by0 = Math.Min(q0.Y, q1.Y); var by1 = Math.Max(q0.Y, q1.Y);
                if (!double.IsFinite(bx0) || !double.IsFinite(by0) || !double.IsFinite(bx1) || !double.IsFinite(by1)
                    || cuts.Any(r => bx0 < r.X + r.W && bx1 > r.X && by0 < r.Y + r.H && by1 > r.Y))
                    return false;
                pieces.Append(ux0.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(uy0.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append((ux1 - ux0).ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append((uy1 - uy0).ToString("R", CultureInfo.InvariantCulture)).Append(" re ");
            }

        if (pieces.Length == 0 && clipOp is not null)
            pieces.Append("0 0 m 0 0 l ");
        if (pieces.Length > 0)
        {
            if (clipOp is not null) pieces.Append(clipOp).Append(' ');
            pieces.Append(paintOp);
        }
        replacement = pieces.ToString();
        return true;
    }

    static bool TryReadRectangleFill(byte[] content, int start, int end,
        out double x, out double y, out double w, out double h, out string? clipOp, out string paintOp)
    {
        x = y = w = h = 0;
        clipOp = null;
        paintOp = "";
        var lexer = new PdfLexer(content, start);
        var tokens = new List<PdfObject>();
        while (lexer.Pos < end && tokens.Count < 8 && lexer.ReadObject(false) is { } token) tokens.Add(token);
        if (lexer.Pos != end || tokens.Count is not (6 or 7)) return false;
        if (tokens[0].AsNumber() is not { } tx || tokens[1].AsNumber() is not { } ty
            || tokens[2].AsNumber() is not { } tw || tokens[3].AsNumber() is not { } th
            || tokens[4] is not PdfKeyword { Value: "re" })
            return false;

        var paintIndex = 5;
        if (tokens.Count == 7)
        {
            if (tokens[5] is not PdfKeyword { Value: "W" or "W*" } clip) return false;
            clipOp = clip.Value;
            paintIndex = 6;
        }
        if (tokens[paintIndex] is not PdfKeyword paint) return false;
        if (paint.Value == "n")
        {
            if (clipOp is null) return false;
        }
        else if (paint.Value is not ("f" or "F" or "f*")) return false;
        if (!double.IsFinite(tx) || !double.IsFinite(ty) || !double.IsFinite(tw) || !double.IsFinite(th) || tw <= 0 || th <= 0)
            return false;
        x = tx; y = ty; w = tw; h = th;
        paintOp = paint.Value;
        return true;
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
