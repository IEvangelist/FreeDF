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
/// <item>Form XObjects: text, paths and images inside forms are redacted per invocation. Every invocation that needed an edit is re-pointed at a copy-on-write variant of the form (nested forms are re-pointed through their parents), so other invocations and other pages keep the original form.</item>
/// <item>Painted paths and clips in page and form content are rewritten with exact polygon subtraction (see <c>PathRedaction</c>): any CTM, curves flattened with a 0.05 unit error bound, even-odd and non-zero fills, and strokes converted to filled outlines first. Anything not understood is removed whole (clip paths become empty clips).</item>
/// <item>Inline images, image XObjects and stencil masks are re-encoded with every pixel whose footprint touches a rectangle blanked; fully covered ones are removed. Optional-content-hidden content is redacted like visible content.</item>
/// <item>With a <see cref="PdfEditor"/>: annotations under a rectangle are removed (with their AcroForm field entries), matching redacted phrases in outline titles are replaced, and XMP/Info/thumbnail metadata is scrubbed.</item>
/// </list>
/// </summary>
public static class Redactor
{
    /// <summary>Rectangle in page box space (origin top-left of the CropBox, y down), as used by text spans and objects.</summary>
    public readonly record struct BoxRect(double X, double Y, double W, double H);

    sealed record NewImage(string Name, byte[] Data, int W, int H, byte[]? Alpha, bool Mask = false);

    sealed class Variant(string name, int source, byte[] data)
    {
        public string Name { get; } = name;
        public int Source { get; } = source;
        public byte[] Data { get; } = data;
        public List<NewImage> Images { get; } = [];
        public List<Variant> Children { get; } = [];
        public HashSet<int> Drop { get; } = [];
    }

    sealed record Plan(byte[] Page, List<NewImage> PageImages, List<Variant> PageChildren, List<string> HitText, HashSet<int> PageDrop);

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

        PdfRef AddImage(NewImage im)
        {
            var dict = new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Image"), ["Width"] = new PdfInteger(im.W), ["Height"] = new PdfInteger(im.H),
                ["Filter"] = new PdfName("FlateDecode"),
            };
            if (im.Mask) { dict["ImageMask"] = new PdfBoolean(true); dict["BitsPerComponent"] = new PdfInteger(1); }
            else { dict["ColorSpace"] = new PdfName("DeviceRGB"); dict["BitsPerComponent"] = new PdfInteger(8); }
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
            return editor.AddObject(new PdfStream(new PdfDictionary(dict), z));
        }

        // A form XObject is never edited in place: each invocation that needed redaction gets its own copy, so other
        // invocations and other pages keep the original form untouched.
        PdfRef AddVariant(Variant v)
        {
            var source = back.GetValueOrDefault(v.Source);
            if (source == 0) throw new InvalidOperationException("Cannot map a form XObject back to the editor; redaction aborted.");
            var names = new Dictionary<string, PdfObject>();
            foreach (var im in v.Images) names[im.Name] = AddImage(im);
            foreach (var child in v.Children) names[child.Name] = AddVariant(child);
            return editor.AddStreamCopy(source, v.Data, names, MapDrop(v.Drop));
        }

        HashSet<int> MapDrop(IEnumerable<int> docNumbers) => docNumbers.Select(n => back.GetValueOrDefault(n)).Where(n => n != 0).ToHashSet();

        editor.DropPageXObjects(pageIndex, MapDrop(plan.PageDrop));

        foreach (var im in plan.PageImages) editor.AddPageXObject(pageIndex, im.Name, AddImage(im));
        foreach (var child in plan.PageChildren) editor.AddPageXObject(pageIndex, child.Name, AddVariant(child));
        editor.SetContentOverride(pageIndex, Tail(plan.Page, page, rects));

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
        var (spans, objects) = PortableDocument.Analyze(page, collectHidden: true);

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
        // Miter joins and square caps reach beyond the half line width the analyzer pads with.
        bool HitObject(ContentInterpreter.PageObject o)
        {
            var pad = 0d;
            if (o.HasStroke && o.Style is { } st)
            {
                var factor = Math.Max(st.Join == LineJoin.Miter ? Math.Max(st.MiterLimit, 1) : 1, st.Cap == LineCap.Square ? Math.Sqrt(2) : 1);
                pad = Math.Max(st.Width * Math.Sqrt(Math.Abs(o.Ctm.Det)) / 2 * (factor - 1), 1);
            }
            return Hit(o.X0 - pad, o.Y0 - pad, o.X1 + pad, o.Y1 + pad);
        }

        // One context per invocation of a form (inv 0 is the page); children have larger invocation numbers than their parents.
        var contexts = spans.Select(s => (Form: s.Form, Inv: s.Inv)).Concat(objects.Select(o => (Form: o.Form, Inv: o.Inv))).Append((Form: 0, Inv: 0)).Distinct()
            .OrderByDescending(c => c.Inv).ToList();
        var variants = new Dictionary<(int Form, int Inv), Variant>();
        var hitText = new List<string>();
        var pageContent = page.GetContents();
        var pageImages = new List<NewImage>(); var pageChildren = new List<Variant>();
        var pageDrop = new HashSet<int>(); var keptRefs = new HashSet<int>();
        var imgCounter = 0; var variantCounter = 0; var prefix = Guid.NewGuid().ToString("N")[..8];

        foreach (var (form, inv) in contexts)
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
            var canName = form == 0 || file.Resolve(fs!.Dict.Get("Resources")) is PdfDictionary;
            var content = form == 0 ? pageContent : file.DecodeStream(fs!);
            var images = new List<NewImage>(); var children = new List<Variant>();

            var removals = new Dictionary<int, string>();
            foreach (var sp in spans.Where(x => x.Form == form && x.Inv == inv))
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
            foreach (var o in objects.Where(x => x.Form == form && x.Inv == inv))
            {
                if (!seen.Add((o.Start, o.End))) continue;
                if (o.Kind == "form" && o.Ref > 0 && editorMode && o.ChildInv != 0)
                {
                    // The invocation was analysed: re-point the Do at its redacted copy, or leave it when nothing inside was touched.
                    if (!variants.TryGetValue((o.Ref, o.ChildInv), out var child)) continue;
                    if (canName)
                    {
                        children.Add(child);
                        edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, null, "/" + child.Name + " Do"));
                    }
                    else edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, true, null, null, null));
                    continue;
                }
                if (!HitObject(o)) continue;
                if (o.Kind is "path" or "clip-path" or "clipped-path")
                {
                    if (TryClipRectangleFill(content, o, rects, out var clipped))
                        edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, Replacement: clipped));
                    else if (PathRedaction.TryRewrite(content, o, rects, out var rewritten))
                    {
                        if (rewritten is not null) edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, Replacement: rewritten));
                    }
                    else if (o.Kind is "clip-path" or "clipped-path")
                        edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, Replacement: "0 0 m 0 0 l W n"));
                    else
                        edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, true, null, null, null));
                    continue;
                }
                if (o.Kind == "image" && editorMode && o.Stream is { } img && !Covered(o.X0, o.Y0, o.X1, o.Y1) && canName
                    && ImageDecoder.Decode(file, img, res, out var isMask, out _) is { } data)
                {
                    var name = $"RdIm{prefix}{imgCounter++}";
                    if (isMask) images.Add(new NewImage(name, BlankMask(data, o.Ctm, rects), data.Width, data.Height, null, true));
                    else
                    {
                        var (rgb, alpha) = Blank(data, o.Ctm, rects);
                        images.Add(new NewImage(name, rgb, data.Width, data.Height, alpha));
                    }
                    edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, false, null, null, null, null, "/" + name + " Do"));
                    continue;
                }
                edits.Add(new ContentRewriter.ObjectEdit(o.Start, o.End, true, null, null, null));
            }
            content = ContentRewriter.EditObjects(content, edits);
            content = ContentRewriter.ReplaceText(content, removals);
            // Resource entries of XObjects that this context replaced or removed are dropped (below) unless an unedited use remains anywhere.
            var editedStarts = edits.Select(e => e.Start).ToHashSet();
            var droppable = new HashSet<int>();
            foreach (var o in objects.Where(x => x.Form == form && x.Inv == inv && x.Ref > 0))
                if (editedStarts.Contains(o.Start) && o.Kind is "image" or "form") droppable.Add(o.Ref); else keptRefs.Add(o.Ref);
            if (form == 0) { pageContent = content; pageImages = images; pageChildren = children; pageDrop = droppable; }
            else if (edits.Count > 0 || removals.Count > 0)
            {
                var variant = new Variant($"RdFm{prefix}{variantCounter++}", form, content);
                variant.Images.AddRange(images);
                variant.Children.AddRange(children);
                variant.Drop.UnionWith(droppable);
                variants[(form, inv)] = variant;
            }
        }
        pageDrop.ExceptWith(keptRefs);
        foreach (var v in variants.Values) v.Drop.ExceptWith(keptRefs);
        return new Plan(pageContent, pageImages, pageChildren, hitText, pageDrop);
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

    // A pixel is blanked when any part of its footprint (the pixel's device-space bounding box, which over-covers for rotated or skewed CTMs) touches a rectangle.
    static bool[] CoveredPixels(int w, int h, Matrix ctm, IReadOnlyList<BoxRect> rects)
    {
        var covered = new bool[w * h];
        double hx = (Math.Abs(ctm.A) / w + Math.Abs(ctm.C) / h) / 2, hy = (Math.Abs(ctm.B) / w + Math.Abs(ctm.D) / h) / 2;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (dx, dy) = ctm.Apply((x + 0.5) / w, 1 - (y + 0.5) / h);
                foreach (var r in rects)
                    if (dx + hx > r.X && dx - hx < r.X + r.W && dy + hy > r.Y && dy - hy < r.Y + r.H) { covered[y * w + x] = true; break; }
            }
        return covered;
    }

    // Blanks covered pixels to opaque black; the alpha plane is null when everything is opaque.
    static (byte[] Rgb, byte[]? Alpha) Blank(ImageData d, Matrix ctm, IReadOnlyList<BoxRect> rects)
    {
        var rgb = new byte[d.Width * d.Height * 3];
        var alpha = new byte[d.Width * d.Height];
        var covered = CoveredPixels(d.Width, d.Height, ctm, rects);
        var translucent = false;
        for (var i = 0; i < covered.Length; i++)
        {
            if (covered[i]) { alpha[i] = 255; continue; }
            rgb[i * 3] = d.Rgba[i * 4]; rgb[i * 3 + 1] = d.Rgba[i * 4 + 1]; rgb[i * 3 + 2] = d.Rgba[i * 4 + 2];
            alpha[i] = d.Rgba[i * 4 + 3];
            if (alpha[i] != 255) translucent = true;
        }
        return (rgb, translucent ? alpha : null);
    }

    // Re-encodes a stencil mask as a 1-bit /ImageMask (sample 0 paints) with covered pixels left unpainted.
    static byte[] BlankMask(ImageData d, Matrix ctm, IReadOnlyList<BoxRect> rects)
    {
        var covered = CoveredPixels(d.Width, d.Height, ctm, rects);
        var rowBytes = (d.Width + 7) / 8;
        var bits = new byte[rowBytes * d.Height];
        for (var y = 0; y < d.Height; y++)
            for (var x = 0; x < d.Width; x++)
            {
                var i = y * d.Width + x;
                var paint = !covered[i] && d.Rgba[i * 4 + 3] != 0;
                if (!paint) bits[y * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        return bits;
    }
}
