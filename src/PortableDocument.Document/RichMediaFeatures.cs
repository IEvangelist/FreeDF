using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>RichMedia instance parameters (ISO 32000-2 Table 335 /Params). Binding defaults to None; the FlashVars value is retained only as text.</summary>
public sealed record PdfRichMediaParameters(
    string Binding,
    string? BindingMaterialName,
    string? FlashVars,
    int CuePointCount,
    PdfObject? Settings,
    PdfDictionary Dictionary);

/// <summary>
/// Typed RichMedia instance (Table 334 /RichMediaInstance: 3D, Flash, Sound, Video). <see cref="IsFlash"/> flags SWF content (active content
/// that is never loaded); <see cref="AssetName"/> is the referenced asset tree key when it can be resolved.
/// </summary>
public sealed record PdfRichMediaInstanceModel(
    string? Subtype,
    string? AssetName,
    PdfDictionary? Asset,
    PdfRichMediaParameters? Parameters,
    bool IsFlash,
    PdfDictionary Dictionary);

public sealed record PdfRichMediaConfigurationModel(
    string? Name,
    string? Subtype,
    IReadOnlyList<PdfRichMediaInstanceModel> Instances,
    PdfDictionary Dictionary);

public sealed record PdfRichMediaAssetModel(string Name, PdfObject FileSpecification);

/// <summary>Typed, bounded RichMedia content (Table 333) for one annotation. Nothing is activated. <see cref="Truncated"/> reports a count/work budget.</summary>
public sealed record PdfRichMediaContentModel(
    int PageIndex,
    int AnnotationIndex,
    IReadOnlyList<PdfRichMediaAssetModel> Assets,
    IReadOnlyList<PdfRichMediaConfigurationModel> Configurations,
    IReadOnlyList<PdfObject> Views,
    bool HasFlash,
    bool Truncated,
    PdfDictionary Dictionary);

public static class RichMediaFeatures
{
    const int MaxAssets = 4096;
    const int MaxConfigurations = 256;
    const int MaxInstances = 256;
    const int MaxViews = 256;
    const int MaxTreeDepth = 32;
    const int MaxWork = 100_000;

    /// <summary>Returns typed RichMedia content for every annotation; shared annotation dictionaries are analysed once and reused.</summary>
    public static IReadOnlyList<PdfRichMediaContentModel> GetRichMediaContent(this PdfDocument doc)
    {
        var f = doc.File;
        var result = new List<PdfRichMediaContentModel>();
        var cache = new Dictionary<PdfDictionary, PdfRichMediaContentModel>(ReferenceEqualityComparer.Instance);
        var work = 0;
        for (var p = 0; p < doc.Pages.Count; p++)
        {
            if (f.Resolve(doc.Pages[p].Dict.Get("Annots")) is not PdfArray annots) continue;
            for (var a = 0; a < annots.Count; a++)
            {
                if (++work > MaxWork) return result;
                if (f.Resolve(annots[a]) is not PdfDictionary annot || f.Resolve(annot.Get("Subtype")).AsName() != "RichMedia") continue;
                if (cache.TryGetValue(annot, out var cached)) { result.Add(cached with { PageIndex = p, AnnotationIndex = a }); continue; }
                var model = Read(f, annot, p, a, ref work);
                cache[annot] = model;
                result.Add(model);
            }
        }
        return result;
    }

    static PdfRichMediaContentModel Read(PdfFile f, PdfDictionary annot, int page, int index, ref int work)
    {
        var truncated = false;
        var assets = new List<PdfRichMediaAssetModel>();
        var configs = new List<PdfRichMediaConfigurationModel>();
        var views = new List<PdfObject>();
        var hasFlash = false;
        if (f.Resolve(annot.Get("RichMediaContent")) is PdfDictionary content)
        {
            if (f.Resolve(content.Get("Assets")) is PdfDictionary tree)
                CollectAssets(f, tree, 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), assets, ref work, ref truncated);
            var assetNames = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            foreach (var a in assets)
                if (f.Resolve(a.FileSpecification) is { } resolved) assetNames.TryAdd(resolved, a.Name);
            if (f.Resolve(content.Get("Views")) is PdfArray viewArray)
                foreach (var v in viewArray.Items)
                {
                    if (views.Count >= MaxViews) { truncated = true; break; }
                    if (f.Resolve(v) is { } r) views.Add(r);
                }
            if (f.Resolve(content.Get("Configurations")) is PdfArray configArray)
                foreach (var c in configArray.Items)
                {
                    if (configs.Count >= MaxConfigurations) { truncated = true; break; }
                    if (++work > MaxWork) { truncated = true; break; }
                    if (f.Resolve(c) is not PdfDictionary config) continue;
                    var instances = new List<PdfRichMediaInstanceModel>();
                    if (f.Resolve(config.Get("Instances")) is PdfArray instArray)
                        foreach (var i in instArray.Items)
                        {
                            if (instances.Count >= MaxInstances || ++work > MaxWork) { truncated = true; break; }
                            if (f.Resolve(i) is not PdfDictionary inst) continue;
                            var model = ReadInstance(f, inst, assetNames);
                            hasFlash |= model.IsFlash;
                            instances.Add(model);
                        }
                    var subtype = f.Resolve(config.Get("Subtype")).AsName();
                    hasFlash |= subtype == "Flash";
                    configs.Add(new PdfRichMediaConfigurationModel(Text(f, config.Get("Name")), subtype, instances, config));
                }
        }
        return new PdfRichMediaContentModel(page, index, assets, configs, views, hasFlash, truncated, annot);
    }

    static PdfRichMediaInstanceModel ReadInstance(PdfFile f,     PdfDictionary inst, Dictionary<object, string> assetNames)
    {
        var subtype = f.Resolve(inst.Get("Subtype")).AsName();
        var assetRef = f.Resolve(inst.Get("Asset"));
        var assetDict = assetRef as PdfDictionary;
            var assetName = assetRef is not null && assetNames.TryGetValue(assetRef, out var n) ? n : null;
        PdfRichMediaParameters? parameters = null;
        if (f.Resolve(inst.Get("Params")) is PdfDictionary p)
        {
            var binding = f.Resolve(p.Get("Binding")).AsName();
            if (binding is not ("None" or "Foreground" or "Background" or "Material")) binding = "None";
            parameters = new PdfRichMediaParameters(
                binding,
                Text(f, p.Get("BindingMaterialName")),
                f.Resolve(p.Get("FlashVars")) switch { PdfString s => s.Text, PdfStream st => "(stream)", _ => null },
                (f.Resolve(p.Get("CuePoints")) as PdfArray)?.Count ?? 0,
                f.Resolve(p.Get("Settings")),
                p);
        }
        var swf = subtype == "Flash" || assetName?.EndsWith(".swf", StringComparison.OrdinalIgnoreCase) == true;
        return new PdfRichMediaInstanceModel(subtype, assetName, assetDict, parameters, swf, inst);
    }

    static void CollectAssets(PdfFile f, PdfDictionary node, int depth, HashSet<PdfDictionary> seen,
        List<PdfRichMediaAssetModel> assets, ref int work, ref bool truncated)
    {
        if (depth > MaxTreeDepth) { truncated = true; return; }
        if (!seen.Add(node)) return;
        if (f.Resolve(node.Get("Names")) is PdfArray names)
            for (var i = 0; i + 1 < names.Count; i += 2)
            {
                if (assets.Count >= MaxAssets || ++work > MaxWork) { truncated = true; return; }
                if (f.Resolve(names[i]) is PdfString key && f.Resolve(names[i + 1]) is { } spec)
                    assets.Add(new PdfRichMediaAssetModel(key.Text, spec));
            }
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids.Items)
            {
                if (++work > MaxWork) { truncated = true; return; }
                if (f.Resolve(k) is PdfDictionary kid)
                    CollectAssets(f, kid, depth + 1, seen, assets, ref work, ref truncated);
            }
    }

    static string? Text(PdfFile f, PdfObject? o) => f.Resolve(o) is PdfString s ? s.Text : null;
}
