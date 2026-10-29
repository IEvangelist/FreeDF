using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>Cheap static analysis used to decide whether an isolated group needs its own layer (it only does if content blends non-Normally).</summary>
static class TransparencyScan
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, object> Cache = new();

    public static bool UsesBlendMode(PdfFile f, PdfDictionary res)
    {
        if (Cache.TryGetValue(res, out var hit)) return (bool)hit;
        var r = Scan(f, res, 0);
        Cache.AddOrUpdate(res, r);
        return r;
    }

    static bool Scan(PdfFile f, PdfDictionary res, int depth)
    {
        if (depth > 4) return false;
        if (f.Resolve(res.Get("ExtGState")) is PdfDictionary egs)
            foreach (var (_, v0) in egs.Entries)
                if (f.Resolve(v0) is PdfDictionary gs && f.Resolve(gs.Get("BM")) is { } bm)
                {
                    var name = bm is PdfArray a && a.Count > 0 ? f.Resolve(a[0]).AsName() : bm.AsName();
                    if (name is not (null or "Normal" or "Compatible")) return true;
                }
        if (f.Resolve(res.Get("XObject")) is PdfDictionary xo)
            foreach (var (_, v0) in xo.Entries)
                if (f.Resolve(v0) is PdfStream { } s && f.Resolve(s.Dict.Get("Subtype")).AsName() == "Form"
                    && f.Resolve(s.Dict.Get("Resources")) is PdfDictionary r2 && !ReferenceEquals(r2, res) && Scan(f, r2, depth + 1)) return true;
        return false;
    }
}
