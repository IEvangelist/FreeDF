using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// Document-level features carried along by <see cref="PdfEditor.ImportPages"/> when merging whole files.
/// Plain page copies (duplicate, blank) leave every option off.
/// </summary>
/// <param name="Forms">Register AcroForm fields whose widgets landed on imported pages (renaming on name collisions) and merge /DA, /Q, /DR and /NeedAppearances.</param>
/// <param name="PageLabels">Keep each imported page's source label and each existing page's current label.</param>
/// <param name="Outlines">Append outline items that point at imported pages (including named destinations); items for omitted pages are dropped.</param>
/// <param name="OutlineTitle">When set, imported outline items are nested under one new bookmark with this title.</param>
public sealed record PdfImportOptions(bool Forms = false, bool PageLabels = false, bool Outlines = false, string? OutlineTitle = null)
{
    public static PdfImportOptions Merge(string? outlineTitle = null) => new(true, true, true, outlineTitle);
}

public sealed partial class PdfEditor
{
    /// <summary>Imports several documents in order starting at <paramref name="insertAt"/>; if any import fails the editor is restored to its previous state.</summary>
    public void ImportBatch(IReadOnlyList<(PdfDocument Source, List<int> Pages, PdfImportOptions Options)> files, int insertAt)
    {
        var objs = _objs.ToDictionary(kv => kv.Key, kv => kv.Value);
        var pages = _pages.ToList();
        var next = _next;
        var importMaps = files.Select(f => f.Source).Distinct()
            .ToDictionary(source => source, source => _importMaps.TryGetValue(source, out var map) ? map : null);
        try
        {
            var at = insertAt;
            foreach (var (source, chosen, options) in files) { ImportPages(source, chosen, at, options); at += chosen.Count; }
        }
        catch
        {
            _objs.Clear();
            foreach (var (k, v) in objs) _objs[k] = v;
            _pages.Clear(); _pages.AddRange(pages); _next = next;
            foreach (var (source, map) in importMaps)
            {
                _importMaps.Remove(source);
                if (map is not null) _importMaps.Add(source, map);
            }
            throw;
        }
    }
    void MergeForms(PdfDocument src, List<int> newPages, PdfObject? drCopy)
    {
        var f = src.File;
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary srcForm) return;
        var landed = new HashSet<int>();
        foreach (var pn in newPages)
            if (_objs[pn] is PdfDictionary pd && Res(pd.Get("Annots")) is PdfArray annots)
                foreach (var a in annots.Items)
                    if (a is PdfRef ar && _objs.TryGetValue(ar.Number, out var ao) && ao is PdfDictionary ad && Res(ad.Get("Subtype")).AsName() == "Widget") landed.Add(ar.Number);

        var roots = new List<int>();
        foreach (var w in landed)
        {
            var cur = w; var seen = new HashSet<int>();
            while (seen.Add(cur) && _objs[cur] is PdfDictionary d && d.Get("Parent") is PdfRef pr && _objs.TryGetValue(pr.Number, out var po) && po is PdfDictionary) cur = pr.Number;
            if (!roots.Contains(cur)) roots.Add(cur);
        }
        roots.Sort();

        // Drop sibling widgets that stayed behind in the source (their pages were not imported).
        bool Prune(int num, int depth)
        {
            if (depth > 32 || _objs[num] is not PdfDictionary d) return false;
            if (Res(d.Get("Kids")) is not PdfArray kids) return Res(d.Get("Subtype")).AsName() != "Widget" || landed.Contains(num);
            var keep = kids.Items.Where(k => k is PdfRef kr && _objs.ContainsKey(kr.Number) && Prune(kr.Number, depth + 1)).ToList();
            if (keep.Count == 0) return false;
            if (keep.Count != kids.Count) _objs[num] = new PdfDictionary(new Dictionary<string, PdfObject>(d.Entries) { ["Kids"] = new PdfArray(keep) });
            return true;
        }
        roots = roots.Where(r => Prune(r, 0)).ToList();
        if (roots.Count == 0) return;

        var catalog = (PdfDictionary)_objs[_catalog];
        var formRaw = catalog.Get("AcroForm");
        var destForm = Res(formRaw) as PdfDictionary;
        var fieldsRaw = destForm?.Get("Fields");
        var fields = (Res(fieldsRaw) as PdfArray)?.Items.ToList() ?? [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in fields) if (Res(it) is PdfDictionary fd && Res(fd.Get("T")) is PdfString ts) names.Add(ts.Text);

        var srcDa = f.Resolve(srcForm.Get("DA")) as PdfString;
        var srcQ = f.Resolve(srcForm.Get("Q")) as PdfInteger;
        var destDa = destForm is null ? null : Res(destForm.Get("DA")) as PdfString;
        var stamp = fields.Count > 0;
        var added = 0;
        foreach (var root in roots)
        {
            var d = (PdfDictionary)_objs[root];
            if (Res(d.Get("T")) is not PdfString t) continue;
            var unique = t.Text;
            for (var n = 2; !names.Add(unique); n++) unique = $"{t.Text}_{n}";
            var e = new Dictionary<string, PdfObject>(d.Entries);
            if (unique != t.Text) e["T"] = new PdfString(EncodeText(unique));
            if (stamp)
            {
                if (srcDa is not null && !e.ContainsKey("DA") && destDa?.Text != srcDa.Text) e["DA"] = srcDa;
                if (srcQ is not null && !e.ContainsKey("Q") && (Res(destForm?.Get("Q")) as PdfInteger)?.Value != srcQ.Value) e["Q"] = srcQ;
            }
            _objs[root] = new PdfDictionary(e);
            fields.Add(new PdfRef(root, 0)); added++;
        }
        if (added == 0) return;

        var form = new Dictionary<string, PdfObject>(destForm?.Entries ?? []);
        if (!stamp)
        {
            if (srcDa is not null) form["DA"] = srcDa;
            if (srcQ is not null) form["Q"] = srcQ;
        }
        if (Res(srcForm.Get("NeedAppearances")) is PdfBoolean { Value: true }) form["NeedAppearances"] = new PdfBoolean(true);
        if (drCopy is PdfDictionary srcDr) form["DR"] = MergeResources(Res(form.GetValueOrDefault("DR")) as PdfDictionary, form.GetValueOrDefault("DR"), srcDr);
        if (fieldsRaw is PdfRef fr) { _objs[fr.Number] = new PdfArray(fields); form["Fields"] = fr; }
        else form["Fields"] = new PdfArray(fields);
        if (formRaw is PdfRef formRef) _objs[formRef.Number] = new PdfDictionary(form);
        else _objs[_catalog] = new PdfDictionary(new Dictionary<string, PdfObject>(catalog.Entries) { ["AcroForm"] = new PdfDictionary(form) });
    }

    // Existing resources win on a name clash; fonts such as /Helv are normally identical standard-font entries.
    PdfObject MergeResources(PdfDictionary? dest, PdfObject? destRaw, PdfDictionary src)
    {
        if (dest is null) return src;
        var result = new Dictionary<string, PdfObject>(dest.Entries);
        foreach (var (category, value) in src.Entries)
        {
            if (!result.TryGetValue(category, out var existing)) { result[category] = value; continue; }
            if (Res(existing) is not PdfDictionary ed || Res(value) is not PdfDictionary sd) continue;
            var merged = new Dictionary<string, PdfObject>(ed.Entries);
            foreach (var (name, v) in sd.Entries) merged.TryAdd(name, v);
            if (existing is PdfRef er) _objs[er.Number] = new PdfDictionary(merged);
            else result[category] = new PdfDictionary(merged);
        }
        if (destRaw is PdfRef dr) { _objs[dr.Number] = new PdfDictionary(result); return dr; }
        return new PdfDictionary(result);
    }

    static PdfArray? ImportedDestination(PdfFile f, PdfDictionary item, Dictionary<int, int> pageMap)
    {
        var raw = f.Resolve(item.Get("Dest"));
        if (raw is null && f.Resolve(item.Get("A")) is PdfDictionary action && f.Resolve(action.Get("S")).AsName() == "GoTo") raw = f.Resolve(action.Get("D"));
        raw = raw switch { PdfString s => DocumentFeatures.LookupNamedDest(f, s.Text), PdfName n => DocumentFeatures.LookupNamedDest(f, n.Value), _ => raw };
        raw = f.Resolve(raw);
        if (raw is PdfDictionary wrapper) raw = f.Resolve(wrapper.Get("D"));
        return raw is PdfArray { Count: > 0 } arr && arr[0] is PdfRef pr && pageMap.TryGetValue(pr.Number, out var dst)
            ? new PdfArray([new PdfRef(dst, 0), .. arr.Items.Skip(1)]) : null;
    }

    sealed class OutlineNode(int number, Dictionary<string, PdfObject> entries, bool open)
    {
        public readonly int Number = number;
        public readonly Dictionary<string, PdfObject> Entries = entries;
        public readonly bool Open = open;
        public readonly List<OutlineNode> Kids = [];
        public int Visible => Kids.Sum(k => 1 + (k.Open ? k.Visible : 0));
    }

    void MergeOutlines(PdfDocument src, Dictionary<int, int> pageMap, string? title, int firstPage)
    {
        var f = src.File;
        if (f.Resolve(f.Catalog.Get("Outlines")) is not PdfDictionary srcRoot) return;
        var seen = new HashSet<int>();
        List<OutlineNode> Build(PdfObject? first, int depth)
        {
            var result = new List<OutlineNode>();
            var cur = first;
            while (cur is PdfRef r && seen.Add(r.Number) && f.Resolve(cur) is PdfDictionary d)
            {
                var kids = depth < 32 ? Build(d.Get("First"), depth + 1) : [];
                var dest = ImportedDestination(f, d, pageMap);
                if (dest is not null || kids.Count > 0)
                {
                    var e = new Dictionary<string, PdfObject> { ["Title"] = f.Resolve(d.Get("Title")) as PdfString ?? new PdfString(EncodeText("Untitled")) };
                    if (dest is not null) e["Dest"] = dest;
                    if (f.Resolve(d.Get("C")) is PdfArray c) e["C"] = c;
                    if (f.Resolve(d.Get("F")) is PdfInteger fl) e["F"] = fl;
                    var node = new OutlineNode(_next++, e, (f.Resolve(d.Get("Count")).AsNumber() ?? 0) >= 0);
                    node.Kids.AddRange(kids);
                    result.Add(node);
                }
                cur = d.Get("Next");
            }
            return result;
        }
        var top = Build(srcRoot.Get("First"), 0);
        if (top.Count == 0) return;
        if (!string.IsNullOrEmpty(title))
        {
            var wrapper = new OutlineNode(_next++, new() { ["Title"] = new PdfString(EncodeText(title)), ["Dest"] = new PdfArray([new PdfRef(firstPage, 0), new PdfName("Fit")]) }, true);
            wrapper.Kids.AddRange(top);
            top = [wrapper];
        }

        void Link(List<OutlineNode> siblings, int parent)
        {
            for (var i = 0; i < siblings.Count; i++)
            {
                var n = siblings[i];
                n.Entries["Parent"] = new PdfRef(parent, 0);
                if (i > 0) n.Entries["Prev"] = new PdfRef(siblings[i - 1].Number, 0);
                if (i < siblings.Count - 1) n.Entries["Next"] = new PdfRef(siblings[i + 1].Number, 0);
                if (n.Kids.Count > 0)
                {
                    n.Entries["First"] = new PdfRef(n.Kids[0].Number, 0);
                    n.Entries["Last"] = new PdfRef(n.Kids[^1].Number, 0);
                    n.Entries["Count"] = new PdfInteger(n.Open ? n.Visible : -n.Visible);
                    Link(n.Kids, n.Number);
                }
                _objs[n.Number] = new PdfDictionary(n.Entries);
            }
        }

        var catalog = (PdfDictionary)_objs[_catalog];
        var rootRaw = catalog.Get("Outlines");
        var destRoot = Res(rootRaw) as PdfDictionary;
        int rootNum;
        Dictionary<string, PdfObject> root;
        if (destRoot is not null && rootRaw is PdfRef rr) { rootNum = rr.Number; root = new(destRoot.Entries); }
        else { rootNum = _next++; root = new() { ["Type"] = new PdfName("Outlines") }; }

        Link(top, rootNum);
        var visible = top.Count + top.Where(t => t.Open).Sum(t => t.Visible);
        if (root.GetValueOrDefault("Last") is PdfRef lastRef && _objs.TryGetValue(lastRef.Number, out var lastObj) && lastObj is PdfDictionary lastDict)
        {
            _objs[lastRef.Number] = new PdfDictionary(new Dictionary<string, PdfObject>(lastDict.Entries) { ["Next"] = new PdfRef(top[0].Number, 0) });
            _objs[top[0].Number] = new PdfDictionary(new Dictionary<string, PdfObject>(((PdfDictionary)_objs[top[0].Number]).Entries) { ["Prev"] = lastRef });
            root["Count"] = new PdfInteger(Math.Abs((long)(Res(root.GetValueOrDefault("Count")).AsNumber() ?? 0)) + visible);
        }
        else
        {
            root["First"] = new PdfRef(top[0].Number, 0);
            root["Count"] = new PdfInteger(visible);
        }
        root["Last"] = new PdfRef(top[^1].Number, 0);
        _objs[rootNum] = new PdfDictionary(root);
        if (rootRaw is not PdfRef) _objs[_catalog] = new PdfDictionary(new Dictionary<string, PdfObject>(catalog.Entries) { ["Outlines"] = new PdfRef(rootNum, 0) });
    }

    sealed record PageLabel(string? Style, PdfString? Prefix, long Number);

    static List<(int Start, PdfDictionary Spec)> LabelRanges(PdfObject? root, Func<PdfObject?, PdfObject?> res)
    {
        var ranges = new List<(int, PdfDictionary)>();
        void Walk(PdfObject? node, int depth)
        {
            if (depth > 16 || res(node) is not PdfDictionary d) return;
            if (res(d.Get("Nums")) is PdfArray nums)
                for (var i = 0; i + 1 < nums.Count; i += 2)
                    if (res(nums[i]).AsNumber() is { } start && res(nums[i + 1]) is PdfDictionary spec) ranges.Add(((int)start, spec));
            if (res(d.Get("Kids")) is PdfArray kids) foreach (var k in kids.Items) Walk(k, depth + 1);
        }
        Walk(root, 0);
        ranges.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return ranges;
    }

    static PageLabel LabelOf(List<(int Start, PdfDictionary Spec)> ranges, int index, Func<PdfObject?, PdfObject?> res)
    {
        var at = ranges.FindLastIndex(r => r.Start <= index);
        if (at < 0) return new("D", null, index + 1L);
        var (start, spec) = ranges[at];
        var number = (long)(res(spec.Get("St")).AsNumber() ?? 1) + index - start;
        return new(res(spec.Get("S")).AsName(), res(spec.Get("P")) as PdfString, number);
    }

    List<(int Start, PdfDictionary Spec)>? CurrentLabelRanges() => LabelRanges(Res(((PdfDictionary)_objs[_catalog]).Get("PageLabels")), Res);

    void MergePageLabels(PdfDocument src, List<int> chosen, List<(int Start, PdfDictionary Spec)> destRanges, int oldCount, int at)
    {
        var f = src.File;
        var srcRanges = LabelRanges(f.Catalog.Get("PageLabels"), f.Resolve);
        if (srcRanges.Count == 0 && destRanges.Count == 0) return;
        var labels = new List<PageLabel>();
        for (var i = 0; i < at; i++) labels.Add(LabelOf(destRanges, i, Res));
        foreach (var index in chosen) labels.Add(LabelOf(srcRanges, index, f.Resolve));
        for (var i = at; i < oldCount; i++) labels.Add(LabelOf(destRanges, i, Res));
        var nums = new List<PdfObject>();
        PageLabel? prev = null;
        for (var i = 0; i < labels.Count; i++)
        {
            var l = labels[i];
            var continues = prev is not null && l.Style == prev.Style && l.Prefix?.Text == prev.Prefix?.Text && (l.Style is null || l.Number == prev.Number + 1);
            prev = l;
            if (continues) continue;
            var spec = new Dictionary<string, PdfObject>();
            if (l.Style is not null) spec["S"] = new PdfName(l.Style);
            if (l.Prefix is { Bytes.Length: > 0 }) spec["P"] = l.Prefix;
            if (l.Style is not null && l.Number != 1) spec["St"] = new PdfInteger(l.Number);
            nums.Add(new PdfInteger(i)); nums.Add(new PdfDictionary(spec));
        }
        var catalog = (PdfDictionary)_objs[_catalog];
        _objs[_catalog] = new PdfDictionary(new Dictionary<string, PdfObject>(catalog.Entries) { ["PageLabels"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Nums"] = new PdfArray(nums) }) });
    }
}
