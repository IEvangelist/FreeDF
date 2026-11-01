using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Samples.Gen;

/// <summary>
/// The whole collection in one navigable file: every other sample (01-26) merged page-for-page with PdfEditor.ImportPages, plus a rebuilt
/// outline (one bookmark per sample, each carrying that sample's own bookmarks), page labels ("NN · label") and the catalog structures
/// (form, layers, attachments, named destinations, structure tree) that belong to exactly one source.
/// The two encrypted samples are opened with their documented passwords and merged as ordinary pages; the result is NOT encrypted and
/// carries neither their passwords nor their permission restrictions (see samples/README.md).
/// </summary>
static class Gen27
{
    public const string Out = "27-full-collection.pdf";

    static readonly Dictionary<string, string> Passwords = new() { ["17-"] = "user123", ["18-"] = "owner123" };

    // Catalog entries copied (remapped) from the single source that defines them.
    static readonly string[] SingleOwner = ["AcroForm", "OCProperties", "StructTreeRoot", "MarkInfo", "AF"];

    sealed record Part(string File, string Number, string Title, PdfDocument Doc, int Start);

    public static void Build(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var editor = PdfEditor.Create();
        var parts = new List<Part>();
        var sources = Directory.GetFiles(dir, "*.pdf").Select(f => Path.GetFileName(f)).Where(n => n != Out && Regex.IsMatch(n, @"^\d\d-") && string.CompareOrdinal(n[..2], "27") < 0).Order(StringComparer.Ordinal).ToList();
        using var hash = MD5.Create();
        foreach (var name in sources)
        {
            var bytes = File.ReadAllBytes(Path.Combine(dir, name));
            hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
            var password = Passwords.FirstOrDefault(kv => name.StartsWith(kv.Key, StringComparison.Ordinal)).Value ?? "";
            var doc = PdfDocument.Load(bytes, password);
            var start = editor.PageCount;
            editor.ImportPages(doc);
            parts.Add(new Part(name, name[..2], doc.Info("Title") ?? Path.GetFileNameWithoutExtension(name), doc, start));
        }
        hash.TransformFinalBlock([], 0, 0);

        RetargetSiblingLinks(editor, parts);
        MergeCatalogStructures(editor, parts);
        editor.SetCatalogEntry("Outlines", BuildOutline(editor, parts));
        editor.SetCatalogEntry("PageLabels", BuildPageLabels(parts));
        editor.SetCatalogEntry("PageMode", new PdfName("UseOutlines"));
        editor.SetCatalogEntry("Lang", new PdfString(Encoding.ASCII.GetBytes("en-US")));
        editor.SetInfo("Title", "FreeDF sample collection");
        editor.SetInfo("Subject", $"All {parts.Count} sample documents merged into one file ({editor.PageCount} pages)");
        editor.SetInfo("Producer", "FreeDF (PdfEditor)");
        File.WriteAllBytes(path, editor.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true, FileId = hash.Hash }));
    }

    // Sample 01 links to its siblings with GoToR actions; inside the merged file they become plain GoTo destinations.
    static void RetargetSiblingLinks(PdfEditor editor, List<Part> parts)
    {
        var first = parts.ToDictionary(p => p.File, p => editor.PageId(p.Start));
        foreach (var p in parts)
            for (var i = 0; i < p.Doc.Pages.Count; i++)
                editor.RewriteAnnotations(p.Start + i, a =>
                {
                    if (a.Get("A") is not PdfDictionary action || (action.Get("S") as PdfName)?.Value != "GoToR") return null;
                    if (action.Get("F") is not PdfString f || !first.TryGetValue(f.Text, out var page)) return null;
                    var e = new Dictionary<string, PdfObject>(a.Entries) { ["Dest"] = new PdfArray([new PdfRef(page, 0), new PdfName("Fit")]) };
                    e.Remove("A");
                    return new PdfDictionary(e);
                });
    }

    static void MergeCatalogStructures(PdfEditor editor, List<Part> parts)
    {
        var owners = new Dictionary<string, Part>();
        var nameTrees = new Dictionary<string, Part>();
        foreach (var p in parts)
        {
            var f = p.Doc.File;
            foreach (var key in SingleOwner)
            {
                if (f.Catalog.Get(key) is null) continue;
                if (!owners.TryAdd(key, p)) throw new InvalidOperationException($"/{key} is defined by both {owners[key].File} and {p.File}; it cannot be merged unambiguously");
                editor.SetCatalogEntry(key, editor.ImportValue(p.Doc, f.Catalog.Get(key)));
            }
            if (f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names)
                foreach (var key in names.Entries.Keys)
                    if (!nameTrees.TryAdd(key, p)) throw new InvalidOperationException($"/Names /{key} is defined by both {nameTrees[key].File} and {p.File}");
        }
        var merged = new Dictionary<string, PdfObject>();
        foreach (var (key, p) in nameTrees)
            merged[key] = editor.ImportValue(p.Doc, ((PdfDictionary)p.Doc.File.Resolve(p.Doc.File.Catalog.Get("Names"))!).Get(key));
        if (merged.Count > 0) editor.SetCatalogEntry("Names", new PdfDictionary(merged));
    }

    static PdfString Text(string s) => new([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(s)]);

    static PdfObject Dest(PdfEditor editor, int page) => new PdfArray([new PdfRef(editor.PageId(page), 0), new PdfName("Fit")]);

    static int Visible(PdfOutlineItem item) => item.Children.Sum(c => 1 + (c.Open ? Visible(c) : 0));

    // One collapsed bookmark per sample, each holding that sample's own outline.
    static PdfObject BuildOutline(PdfEditor editor, List<Part> parts)
    {
        var root = editor.ReserveObject();

        PdfRef Level(IReadOnlyList<PdfOutlineItem> items, PdfRef parent, out PdfRef last)
        {
            var refs = items.Select(_ => editor.ReserveObject()).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var e = new Dictionary<string, PdfObject> { ["Title"] = Text(item.Title), ["Parent"] = parent };
                if (i > 0) e["Prev"] = refs[i - 1];
                if (i < refs.Count - 1) e["Next"] = refs[i + 1];
                if (item.PageIndex is { } pg) e["Dest"] = Dest(editor, pg);
                else if (item.ActionUri is not null)
                    e["A"] = new PdfDictionary(new() { ["S"] = new PdfName("URI"), ["URI"] = new PdfString(Encoding.Latin1.GetBytes(item.ActionUri)) });
                if (item.Children.Count > 0)
                {
                    e["First"] = Level(item.Children, refs[i], out var kidsLast);
                    e["Last"] = kidsLast;
                    var visible = Visible(item);
                    e["Count"] = new PdfInteger(item.Open ? visible : -visible);
                }
                editor.SetObject(refs[i], new PdfDictionary(e));
            }
            last = refs[^1];
            return refs[0];
        }

        var top = parts.Select(p => new PdfOutlineItem($"{p.Number}  {p.Title}", p.Start, null, false, p.Doc.GetOutlines().Select(o => Shift(o, p.Start)).ToList())).ToList();
        var first = Level(top, root, out var last);
        editor.SetObject(root, new PdfDictionary(new()
        {
            ["Type"] = new PdfName("Outlines"), ["First"] = first, ["Last"] = last, ["Count"] = new PdfInteger(Visible(new PdfOutlineItem("", null, null, false, top))),
        }));
        return root;
    }

    // Source outline destinations are relative to the source; move them to merged page indices.
    static PdfOutlineItem Shift(PdfOutlineItem o, int start) =>
        o with { PageIndex = o.PageIndex + start, Children = o.Children.Select(c => Shift(c, start)).ToList() };

    // "NN · label": samples that define their own labels keep them, the rest number their pages from 1.
    static PdfObject BuildPageLabels(List<Part> parts)
    {
        var nums = new List<PdfObject>();
        foreach (var p in parts)
        {
            if (p.Doc.File.Catalog.Get("PageLabels") is not null)
                for (var i = 0; i < p.Doc.Pages.Count; i++)
                    nums.AddRange([new PdfInteger(p.Start + i), new PdfDictionary(new() { ["P"] = Text($"{p.Number} · {p.Doc.GetPageLabel(i)}") })]);
            else
                nums.AddRange([new PdfInteger(p.Start), new PdfDictionary(new() { ["S"] = new PdfName("D"), ["St"] = new PdfInteger(1), ["P"] = Text($"{p.Number} · ") })]);
        }
        return new PdfDictionary(new() { ["Nums"] = new PdfArray(nums) });
    }
}
