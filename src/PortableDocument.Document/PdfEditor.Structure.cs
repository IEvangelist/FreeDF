using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Optional entries for a structure element created by <see cref="PdfEditor.AddStructureElement"/>.</summary>
public sealed record StructureElementSpec(string? Alt = null, string? ActualText = null, string? Lang = null, string? Title = null, string? Id = null, PdfRef? Namespace = null);

/// <param name="Elements">Structure elements reached from the root.</param>
/// <param name="ParentTreeEntries">Number-tree entries written (page, stream and object keys).</param>
/// <param name="NextKey">The new /ParentTreeNextKey.</param>
/// <param name="Skipped">Kids that were ignored: direct (non-indirect) element dictionaries, revisited elements, or content beyond the traversal bounds.</param>
public sealed record StructureRebuildResult(int Elements, int ParentTreeEntries, int NextKey, int Skipped);

public sealed partial class PdfEditor
{
    /// <param name="PagesTagged">Pages whose content was rewritten.</param>
    /// <param name="PagesSkipped">Pages left alone because their content already contains marked content or could not be decoded.</param>
    /// <param name="TextBlocks">P elements created (text objects and Form XObjects that paint text).</param>
    /// <param name="FigureBlocks">Figure elements created for images; they have no alternate text and need an author.</param>
    /// <param name="ArtifactBlocks">Vector paths and shadings marked as Artifact; real content could be hidden here, so review them.</param>
    /// <param name="NeedsReview">Always true: the structure is flat and inferred only from content order. The catalog is marked /Suspects true until reviewed.</param>
    public sealed record AutoTagResult(int PagesTagged, int PagesSkipped, int TextBlocks, int FigureBlocks, int ArtifactBlocks, bool NeedsReview = true);

    /// <summary>Adds many structure elements under <paramref name="parent"/> in one pass (linear, unlike repeated <see cref="AddStructureElement"/> calls on a large parent).</summary>
    public IReadOnlyList<PdfRef> AddStructureElements(PdfRef parent, IEnumerable<string> types)
    {
        Require(PdfPermissions.Modify);
        var kids = StructKids(StructDict(parent, nameof(parent)));
        var added = new List<PdfRef>();
        foreach (var type in types)
        {
            ArgumentException.ThrowIfNullOrEmpty(type);
            var element = AddObject(new PdfDictionary(new Dictionary<string, PdfObject> { ["Type"] = new PdfName("StructElem"), ["S"] = new PdfName(type), ["P"] = parent }));
            kids.Add(element);
            added.Add(element);
        }
        SetStructKids(parent, kids);
        return added;
    }

    bool FormPaintsText(PdfStream form, PdfDictionary? inherited, int depth, HashSet<PdfStream> seen)
    {
        if (depth > 8 || !seen.Add(form)) return false;
        var resources = Res(form.Dict.Get("Resources")) as PdfDictionary ?? inherited;
        var lex = new PdfLexer(_src is not null ? _src.DecodeStream(form) : form.RawData);
        string? name = null;
        for (var tokens = 0; tokens++ < 5_000_000;)
        {
            lex.SkipWhite();
            if (lex.Pos >= lex.Length) break;
            var before = lex.Pos;
            PdfObject? o;
            try { o = lex.ReadObject(false); }
            catch (PdfParseException) { lex.Pos = before + 1; continue; }
            if (o is null) break;
            if (lex.Pos <= before) lex.Pos = before + 1;
            if (o is PdfName n) { name = n.Value; continue; }
            if (o is not PdfKeyword kw) continue;
            if (kw.Value is "Tj" or "TJ" or "'" or "\"") return true;
            if (kw.Value == "Do" && name is not null
                && Res((Res(resources?.Get("XObject")) as PdfDictionary)?.Get(name)) is PdfStream inner && Res(inner.Dict.Get("Subtype")).AsName() == "Form"
                && FormPaintsText(inner, resources, depth + 1, seen)) return true;
            name = null;
        }
        return false;
    }

    ContentAutoTagger.DoKind ClassifyXObject(int page, string name)
    {
        var resources = Res(((PdfDictionary)_objs[page]).Get("Resources")) as PdfDictionary;
        if (Res((Res(resources?.Get("XObject")) as PdfDictionary)?.Get(name)) is not PdfStream xo) return ContentAutoTagger.DoKind.Artifact;
        return Res(xo.Dict.Get("Subtype")).AsName() switch
        {
            "Image" => ContentAutoTagger.DoKind.Figure,
            "Form" => FormPaintsText(xo, resources, 0, new HashSet<PdfStream>(ReferenceEqualityComparer.Instance)) ? ContentAutoTagger.DoKind.Text : ContentAutoTagger.DoKind.Artifact,
            _ => ContentAutoTagger.DoKind.Artifact,
        };
    }

    byte[] PageContentBytes(int index)
    {
        var page = _pages[index];
        if (_contentOverrides.TryGetValue(page, out var overridden)) return overridden;
        var ms = new MemoryStream();
        void Add(PdfObject? o)
        {
            if (Res(o) is not PdfStream s) return;
            ms.Write(_src is not null ? _src.DecodeStream(s) : s.RawData);
            ms.WriteByte((byte)'\n');
        }
        if (Res(((PdfDictionary)_objs[page]).Get("Contents")) is PdfArray arr) foreach (var o in arr.Items) Add(o); else Add(((PdfDictionary)_objs[page]).Get("Contents"));
        return ms.ToArray();
    }

    /// <summary>
    /// Conservatively tags an untagged document in content order under one Document element: text objects and Form XObjects that paint text become P, images
    /// become Figure elements without alternate text, and only vector paths and shadings are marked as Artifact. Nothing else is inferred, so the result is
    /// structurally consistent but not accessible: /MarkInfo /Suspects is set to true until a person reviews it (clear it with <see cref="SetCatalogEntry"/>).
    /// Pages that already contain marked content are left untouched; documents with a structure tree throw.
    /// </summary>
    public AutoTagResult AutoTagUntagged()
    {
        Require(PdfPermissions.Modify);
        if (StructureRoot is not null) throw new InvalidOperationException("the document already has a structure tree");
        var tagged = new List<(int Page, ContentAutoTagger.Result Result)>();
        var skipped = 0;
        for (var i = 0; i < PageCount; i++)
        {
            ContentAutoTagger.Result r;
            try { var page = _pages[i]; r = ContentAutoTagger.Tag(PageContentBytes(i), name => ClassifyXObject(page, name)); }
            catch (Exception e) when (e is PdfParseException or NotSupportedException or InvalidDataException or InvalidOperationException) { skipped++; continue; }
            if (r.AlreadyMarked) { skipped++; continue; }
            if (r.Blocks.Count + r.ArtifactBlocks > 0) tagged.Add((i, r));
        }
        if (tagged.Count == 0) return new AutoTagResult(0, skipped, 0, 0, 0);

        var document = AddStructureElement(EnsureStructureRoot(), "Document");
        var all = tagged.SelectMany(t => t.Result.Blocks.Select((role, mcid) => (t.Page, mcid, role))).ToList();
        var elements = AddStructureElements(document, all.Select(a => a.role));
        for (var i = 0; i < all.Count; i++)
        {
            // New elements have no kids yet, so each gets its single MCR directly instead of going through the copy-on-write helpers.
            var el = elements[i];
            var d = (PdfDictionary)_objs[el.Number];
            _objs[el.Number] = new PdfDictionary(new Dictionary<string, PdfObject>(d.Entries)
            {
                ["Pg"] = new PdfRef(_pages[all[i].Page], 0),
                ["K"] = new PdfInteger(all[i].mcid),
            });
        }
        foreach (var (page, r) in tagged) SetContentOverride(page, r.Content);
        var catalog = (PdfDictionary)_objs[_catalog];
        var markInfo = Res(catalog.Get("MarkInfo")) is PdfDictionary mi ? new Dictionary<string, PdfObject>(mi.Entries) : [];
        markInfo["Suspects"] = new PdfBoolean(true);
        _objs[_catalog] = new PdfDictionary(new Dictionary<string, PdfObject>(catalog.Entries) { ["MarkInfo"] = new PdfDictionary(markInfo) });
        RebuildParentTree();
        return new AutoTagResult(tagged.Count, skipped, all.Count(a => a.role == "P"), all.Count(a => a.role == "Figure"), tagged.Sum(t => t.Result.ArtifactBlocks));
    }

    /// <summary>The indirect reference to the catalog's /StructTreeRoot, or null for an untagged document.</summary>
    public PdfRef? StructureRoot => ((PdfDictionary)_objs[_catalog]).Get("StructTreeRoot") is PdfRef r && _objs.GetValueOrDefault(r.Number) is PdfDictionary ? r : null;

    /// <summary>Creates an empty /StructTreeRoot (with an empty ParentTree) and sets /MarkInfo /Marked true when the document has no structure tree yet; returns the root.</summary>
    public PdfRef EnsureStructureRoot()
    {
        Require(PdfPermissions.Modify);
        if (StructureRoot is { } existing) return existing;
        var root = AddObject(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("StructTreeRoot"),
            ["ParentTree"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Nums"] = new PdfArray([]) }),
            ["ParentTreeNextKey"] = new PdfInteger(0),
        }));
        var cat = new Dictionary<string, PdfObject>(((PdfDictionary)_objs[_catalog]).Entries) { ["StructTreeRoot"] = root };
        var markInfo = Res(cat.GetValueOrDefault("MarkInfo")) is PdfDictionary mi ? new Dictionary<string, PdfObject>(mi.Entries) : [];
        markInfo["Marked"] = new PdfBoolean(true);
        cat["MarkInfo"] = new PdfDictionary(markInfo);
        _objs[_catalog] = new PdfDictionary(cat);
        return root;
    }

    PdfDictionary StructDict(PdfRef r, string what)
    {
        if (r.Generation != 0 || !_objs.TryGetValue(r.Number, out var o) || o is not PdfDictionary d || _deleted.Contains(r.Number))
            throw new ArgumentException($"{what} is not a structure node of this editor", nameof(r));
        return d;
    }

    List<PdfObject> StructKids(PdfDictionary d) => Res(d.Get("K")) switch
    {
        null or PdfNull => [],
        PdfArray a => [.. a.Items],
        _ => d.Get("K") is { } single ? [single] : [],
    };

    void SetStructKids(PdfRef owner, List<PdfObject> kids)
    {
        var d = (PdfDictionary)_objs[owner.Number];
        var e = new Dictionary<string, PdfObject>(d.Entries);
        if (kids.Count == 0) e.Remove("K"); else e["K"] = new PdfArray(kids);
        _objs[owner.Number] = new PdfDictionary(e);
    }

    void SetStructEntry(PdfRef node, string key, PdfObject? value)
    {
        var e = new Dictionary<string, PdfObject>(((PdfDictionary)_objs[node.Number]).Entries);
        if (value is null) e.Remove(key); else e[key] = value;
        _objs[node.Number] = new PdfDictionary(e);
    }

    bool IsStructureElement(PdfRef r) => StructDict(r, "node") is var d && Res(d.Get("Type")).AsName() != "StructTreeRoot";

    /// <summary>Adds a structure element of <paramref name="type"/> under <paramref name="parent"/> (the root or another element) at <paramref name="index"/> (default: last).</summary>
    public PdfRef AddStructureElement(PdfRef parent, string type, StructureElementSpec? spec = null, int? index = null)
    {
        Require(PdfPermissions.Modify);
        ArgumentException.ThrowIfNullOrEmpty(type);
        var pd = StructDict(parent, nameof(parent));
        var e = new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("StructElem"),
            ["S"] = new PdfName(type),
            ["P"] = parent,
        };
        if (spec?.Alt is { } alt) e["Alt"] = TextString(alt);
        if (spec?.ActualText is { } at) e["ActualText"] = TextString(at);
        if (spec?.Lang is { } lang) e["Lang"] = TextString(lang);
        if (spec?.Title is { } title) e["T"] = TextString(title);
        if (spec?.Id is { } id) e["ID"] = TextString(id);
        if (spec?.Namespace is { } ns) e["NS"] = ns;
        var element = AddObject(new PdfDictionary(e));
        var kids = StructKids(pd);
        kids.Insert(Math.Clamp(index ?? kids.Count, 0, kids.Count), element);
        SetStructKids(parent, kids);
        return element;
    }

    /// <summary>Adds a marked-content reference (ISO 32000-2 14.7.5.2) for <paramref name="mcid"/> on a page; the next <see cref="RebuildParentTree"/> maps it back.</summary>
    public void AddStructureMarkedContent(PdfRef element, int pageIndex, int mcid, PdfRef? stream = null)
    {
        Require(PdfPermissions.Modify);
        Check(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(mcid);
        var d = StructDict(element, nameof(element));
        if (!IsStructureElement(element)) throw new ArgumentException("marked content can only be added to a structure element", nameof(element));
        var mcr = new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("MCR"),
            ["Pg"] = new PdfRef(_pages[pageIndex], 0),
            ["MCID"] = new PdfInteger(mcid),
        };
        if (stream is { } s) mcr["Stm"] = s;
        var kids = StructKids(d);
        kids.Add(new PdfDictionary(mcr));
        SetStructKids(element, kids);
    }

    /// <summary>Adds an object reference (OBJR, 14.7.5.3) to an annotation or XObject so it is part of the logical structure; its /StructParent is assigned by <see cref="RebuildParentTree"/>.</summary>
    public void AddStructureObjectReference(PdfRef element, int pageIndex, PdfRef target)
    {
        Require(PdfPermissions.Modify);
        Check(pageIndex);
        var d = StructDict(element, nameof(element));
        if (!IsStructureElement(element)) throw new ArgumentException("an object reference can only be added to a structure element", nameof(element));
        if (!_objs.TryGetValue(target.Number, out var t) || t is not (PdfDictionary or PdfStream)) throw new ArgumentException("the target is not a dictionary or stream of this editor", nameof(target));
        var kids = StructKids(d);
        kids.Add(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("OBJR"),
            ["Pg"] = new PdfRef(_pages[pageIndex], 0),
            ["Obj"] = target,
        }));
        SetStructKids(element, kids);
    }

    /// <summary>Sets or clears (null) /Alt, /ActualText, /Lang or /T on a structure element.</summary>
    public void SetStructureText(PdfRef element, string key, string? value)
    {
        Require(PdfPermissions.Modify);
        if (key is not ("Alt" or "ActualText" or "Lang" or "T" or "E" or "ID")) throw new ArgumentException("unsupported structure text key", nameof(key));
        StructDict(element, nameof(element));
        SetStructEntry(element, key, value is null ? null : TextString(value));
    }

    bool DetachFromParent(PdfRef node, out PdfRef parent, out int index)
    {
        parent = default!; index = -1;
        if (Res(StructDict(node, nameof(node)).Get("P")) is not PdfDictionary || StructDict(node, nameof(node)).Get("P") is not PdfRef p) return false;
        parent = p;
        var kids = StructKids(StructDict(p, "parent"));
        index = kids.FindIndex(k => k is PdfRef r && r.Number == node.Number);
        if (index < 0) return false;
        kids.RemoveAt(index);
        SetStructKids(p, kids);
        return true;
    }

    /// <summary>Moves an element (with its subtree) under <paramref name="newParent"/>; moving an element below itself throws.</summary>
    public void MoveStructureElement(PdfRef element, PdfRef newParent, int? index = null)
    {
        Require(PdfPermissions.Modify);
        if (!IsStructureElement(element)) throw new ArgumentException("the structure root cannot be moved", nameof(element));
        StructDict(newParent, nameof(newParent));
        var depth = 0;
        for (PdfRef? cur = newParent; cur is not null && depth++ < StructureTree.MaxDepth * 4;)
        {
            if (cur.Number == element.Number) throw new ArgumentException("an element cannot be moved into its own subtree", nameof(newParent));
            cur = _objs.GetValueOrDefault(cur.Number) is PdfDictionary cd && cd.Get("P") is PdfRef pp ? pp : null;
        }
        if (!DetachFromParent(element, out _, out _)) throw new ArgumentException("the element is not attached to a parent", nameof(element));
        var kids = StructKids(StructDict(newParent, nameof(newParent)));
        kids.Insert(Math.Clamp(index ?? kids.Count, 0, kids.Count), element);
        SetStructKids(newParent, kids);
        SetStructEntry(element, "P", newParent);
    }

    PdfRef? EffectiveStructurePage(PdfRef element)
    {
        var guard = 0;
        for (PdfRef? cur = element; cur is not null && guard++ < StructureTree.MaxDepth * 4 && _objs.GetValueOrDefault(cur.Number) is PdfDictionary cd; cur = cd.Get("P") as PdfRef)
            if (cd.Get("Pg") is PdfRef pg) return pg;
        return null;
    }

    /// <summary>Deletes an element and its descendants (or, with <paramref name="keepChildren"/>, re-attaches its children to its parent in its place). Returns the number of elements deleted.</summary>
    public int RemoveStructureElement(PdfRef element, bool keepChildren = false)
    {
        Require(PdfPermissions.Modify);
        if (!IsStructureElement(element)) throw new ArgumentException("the structure root cannot be removed", nameof(element));
        var d = StructDict(element, nameof(element));
        if (!DetachFromParent(element, out var parent, out var at)) throw new ArgumentException("the element is not attached to a parent", nameof(element));
        var removed = 0;
        if (keepChildren)
        {
            // Children inherit /Pg (for bare MCIDs and page-less MCRs) and /Lang from the element being removed, so make both explicit first.
            var effectivePage = EffectiveStructurePage(element);
            var inheritedLang = Res(d.Get("Lang")) as PdfString;
            var kids = StructKids(StructDict(parent, "parent"));
            var own = StructKids(d);
            var carried = new List<PdfObject>(own.Count);
            foreach (var k in own)
            {
                var kid = Res(k);
                if (kid is PdfInteger { Value: >= 0 } n && effectivePage is { } ep)
                    carried.Add(new PdfDictionary(new Dictionary<string, PdfObject> { ["Type"] = new PdfName("MCR"), ["Pg"] = ep, ["MCID"] = n }));
                else if (kid is PdfDictionary { } md && Res(md.Get("Type")).AsName() == "MCR" && md.Get("Pg") is null && effectivePage is { } ep2)
                    carried.Add(k is PdfRef ? k : new PdfDictionary(new Dictionary<string, PdfObject>(md.Entries) { ["Pg"] = ep2 }));
                else carried.Add(k);
                if (k is PdfRef kr && kid is PdfDictionary kd && Res(kd.Get("Type")).AsName() is not ("MCR" or "OBJR"))
                {
                    SetStructEntry(kr, "P", parent);
                    if (kd.Get("Pg") is null && effectivePage is { } cp) SetStructEntry(kr, "Pg", cp);
                    if (kd.Get("Lang") is null && inheritedLang is not null) SetStructEntry(kr, "Lang", inheritedLang);
                }
                else if (k is PdfRef mr && kid is PdfDictionary mk && Res(mk.Get("Type")).AsName() == "MCR" && mk.Get("Pg") is null && effectivePage is { } ep3)
                    SetStructEntry(mr, "Pg", ep3);
            }
            kids.InsertRange(at, carried);
            SetStructKids(parent, kids);
            _objs.Remove(element.Number); _deleted.Add(element.Number);
            return 1;
        }
        var stack = new Stack<PdfRef>([element]);
        var seen = new HashSet<int>();
        while (stack.Count > 0 && removed < StructureTree.MaxNodes)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur.Number) || !_objs.TryGetValue(cur.Number, out var o) || o is not PdfDictionary cd) continue;
            foreach (var k in StructKids(cd)) if (k is PdfRef kr) stack.Push(kr);
            _objs.Remove(cur.Number); _deleted.Add(cur.Number);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// Rebuilds /ParentTree and /ParentTreeNextKey from the element tree (ISO 32000-2 14.7.5.4): page and /Stm content map MCIDs to their owning element,
    /// object references map the target's /StructParent to the element. Missing or clashing /StructParents and /StructParent keys are assigned;
    /// existing unique keys are kept. Traversal is bounded by <see cref="StructureTree.MaxNodes"/> and <see cref="StructureTree.MaxDepth"/>.
    /// </summary>
    public StructureRebuildResult RebuildParentTree()
    {
        Require(PdfPermissions.Modify);
        var rootRef = StructureRoot ?? throw new InvalidOperationException("the document has no structure tree");
        var root = (PdfDictionary)_objs[rootRef.Number];
        var pageMcids = new Dictionary<int, SortedDictionary<int, PdfRef>>();
        var streamMcids = new Dictionary<int, SortedDictionary<int, PdfRef>>();
        var objects = new Dictionary<int, PdfRef>();
        var seen = new HashSet<int>();
        int elements = 0, skipped = 0;
        var bounded = false;

        void Claim(Dictionary<int, SortedDictionary<int, PdfRef>> map, int owner, int mcid, PdfRef element)
        {
            if (!map.TryGetValue(owner, out var m)) map[owner] = m = [];
            m.TryAdd(mcid, element);
        }

        var work = new Stack<(PdfRef Element, int Depth, int? Page)>();

        void VisitKids(PdfRef owner, PdfDictionary ownerDict, int? page)
        {
            foreach (var k in StructKids(ownerDict))
            {
                var kid = Res(k);
                if (kid is PdfInteger { Value: >= 0 and <= int.MaxValue } n) { if (page is { } p) Claim(pageMcids, p, (int)n.Value, owner); else skipped++; continue; }
                if (kid is not PdfDictionary kd) { skipped++; continue; }
                switch (Res(kd.Get("Type")).AsName())
                {
                    case "MCR":
                        if (Res(kd.Get("MCID")) is not PdfInteger { Value: >= 0 and <= int.MaxValue } mc) { skipped++; break; }
                        if (kd.Get("Stm") is PdfRef stm) Claim(streamMcids, stm.Number, (int)mc.Value, owner);
                        else if (((kd.Get("Pg") as PdfRef)?.Number ?? page) is { } pg) Claim(pageMcids, pg, (int)mc.Value, owner);
                        else skipped++;
                        break;
                    case "OBJR":
                        if (kd.Get("Obj") is PdfRef target) objects.TryAdd(target.Number, owner); else skipped++;
                        break;
                }
            }
        }

        // Depth-first over indirect elements; direct element dictionaries cannot be referenced from a ParentTree and are skipped.
        var rootKids = StructKids(root);
        foreach (var k in rootKids) if (k is PdfRef kr) work.Push((kr, 1, null)); else skipped++;
        while (work.Count > 0)
        {
            var (cur, depth, inherited) = work.Pop();
            if (depth > StructureTree.MaxDepth || elements >= StructureTree.MaxNodes) { bounded = true; break; }
            if (!seen.Add(cur.Number)) { skipped++; continue; }
            if (Res(cur) is not PdfDictionary d) { skipped++; continue; }
            elements++;
            var page = (d.Get("Pg") as PdfRef)?.Number ?? inherited;
            VisitKids(cur, d, page);
            var kids = StructKids(d);
            for (var i = kids.Count - 1; i >= 0; i--)
                if (kids[i] is PdfRef kr && Res(kr) is PdfDictionary kd && Res(kd.Get("Type")).AsName() is not ("MCR" or "OBJR")) work.Push((kr, depth + 1, page));
                else if (kids[i] is PdfDictionary direct && Res(direct.Get("Type")).AsName() is not ("MCR" or "OBJR")) skipped++;
        }

        // A partial map would silently drop valid owners, so a bound hit refuses the rebuild before anything is written.
        if (bounded)
            throw new InvalidOperationException($"the structure tree exceeds the supported depth ({StructureTree.MaxDepth}) or size ({StructureTree.MaxNodes} elements); the ParentTree was not rebuilt");

        var claimed = new HashSet<int>();
        var oldNext = Res(root.Get("ParentTreeNextKey")).AsNumber() is { } nk and >= 0 ? (int)nk : 0;
        var nextKey = oldNext;
        var nums = new SortedDictionary<int, PdfObject>();
        // Keep existing unique keys first so assignment of new ones cannot steal them.
        foreach (var page in pageMcids.Keys.Where(p => _objs.TryGetValue(p, out var o) && o is PdfDictionary).OrderBy(p => _pages.IndexOf(p)))
        {
            var pd = (PdfDictionary)_objs[page];
            if (Res(pd.Get("StructParents")) is PdfInteger { Value: >= 0 and <= int.MaxValue - 1 } sp && claimed.Add((int)sp.Value)) nextKey = Math.Max(nextKey, (int)sp.Value + 1);
        }
        foreach (var (num, _) in streamMcids)
            if (_objs.TryGetValue(num, out var o) && o is PdfStream s && Res(s.Dict.Get("StructParents")) is PdfInteger { Value: >= 0 and <= int.MaxValue - 1 } sp && claimed.Add((int)sp.Value)) nextKey = Math.Max(nextKey, (int)sp.Value + 1);
        foreach (var (num, _) in objects)
        {
            var existing = _objs.TryGetValue(num, out var o) ? o switch { PdfDictionary od => od.Get("StructParent"), PdfStream os => os.Dict.Get("StructParent"), _ => null } : null;
            if (Res(existing) is PdfInteger { Value: >= 0 and <= int.MaxValue - 1 } sp && claimed.Add((int)sp.Value)) nextKey = Math.Max(nextKey, (int)sp.Value + 1);
        }

        PdfArray Array(SortedDictionary<int, PdfRef> map)
        {
            var items = new List<PdfObject>();
            var length = Math.Min(map.Keys.Max() + 1, 1_000_000);
            for (var i = 0; i < length; i++) items.Add(map.TryGetValue(i, out var el) ? el : PdfNull.Instance);
            return new PdfArray(items);
        }

        bool Owns(PdfObject? existing, out int key)
        {
            key = -1;
            if (Res(existing) is not PdfInteger { Value: >= 0 and <= int.MaxValue - 1 } k) return false;
            key = (int)k.Value;
            return true;
        }

        var usedKeys = new HashSet<int>();
        int Key(PdfObject? existing)
        {
            if (Owns(existing, out var k) && usedKeys.Add(k)) return k;
            while (claimed.Contains(nextKey)) nextKey++;
            claimed.Add(nextKey);
            usedKeys.Add(nextKey);
            return nextKey++;
        }

        foreach (var (page, map) in pageMcids.OrderBy(p => _pages.IndexOf(p.Key)))
        {
            if (!_objs.TryGetValue(page, out var po) || po is not PdfDictionary pd) { skipped++; continue; }
            var key = Key(pd.Get("StructParents"));
            nums[key] = Array(map);
            if (!(Owns(pd.Get("StructParents"), out var cur) && cur == key)) _objs[page] = new PdfDictionary(new Dictionary<string, PdfObject>(pd.Entries) { ["StructParents"] = new PdfInteger(key) });
        }
        foreach (var (num, map) in streamMcids)
        {
            if (!_objs.TryGetValue(num, out var so) || so is not PdfStream s) { skipped++; continue; }
            var key = Key(s.Dict.Get("StructParents"));
            nums[key] = Array(map);
            if (!(Owns(s.Dict.Get("StructParents"), out var cur) && cur == key))
                _objs[num] = new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>(s.Dict.Entries) { ["StructParents"] = new PdfInteger(key) }), s.RawData);
        }
        foreach (var (num, element) in objects)
        {
            if (!_objs.TryGetValue(num, out var oo)) { skipped++; continue; }
            var dict = oo switch { PdfDictionary od => od, PdfStream os => os.Dict, _ => null };
            if (dict is null) { skipped++; continue; }
            var key = Key(dict.Get("StructParent"));
            nums[key] = element;
            if (!(Owns(dict.Get("StructParent"), out var cur) && cur == key))
            {
                var ne = new Dictionary<string, PdfObject>(dict.Entries) { ["StructParent"] = new PdfInteger(key) };
                _objs[num] = oo is PdfStream st ? new PdfStream(new PdfDictionary(ne), st.RawData) : new PdfDictionary(ne);
            }
        }

        // Owners that are no longer part of the tree must not keep keys that now mean something else (or point into the dropped ParentTree).
        var pageSet = new HashSet<int>(_pages);
        foreach (var (num, o) in _objs.ToList())
        {
            switch (o)
            {
                case PdfDictionary d:
                    var dropPage = d.Get("StructParents") is not null && pageSet.Contains(num) && !pageMcids.ContainsKey(num);
                    var dropObj = d.Get("StructParent") is not null && !objects.ContainsKey(num);
                    if (dropPage || dropObj)
                    {
                        var ne = new Dictionary<string, PdfObject>(d.Entries);
                        if (dropPage) ne.Remove("StructParents");
                        if (dropObj) ne.Remove("StructParent");
                        _objs[num] = new PdfDictionary(ne);
                    }
                    break;
                case PdfStream s:
                    var dropStm = s.Dict.Get("StructParents") is not null && !streamMcids.ContainsKey(num);
                    var dropStmObj = s.Dict.Get("StructParent") is not null && !objects.ContainsKey(num);
                    if (dropStm || dropStmObj)
                    {
                        var ne = new Dictionary<string, PdfObject>(s.Dict.Entries);
                        if (dropStm) ne.Remove("StructParents");
                        if (dropStmObj) ne.Remove("StructParent");
                        _objs[num] = new PdfStream(new PdfDictionary(ne), s.RawData);
                    }
                    break;
            }
        }

        var flat = new List<PdfObject>();
        foreach (var (key, value) in nums) { flat.Add(new PdfInteger(key)); flat.Add(value); }
        var tree = new PdfDictionary(new Dictionary<string, PdfObject> { ["Nums"] = new PdfArray(flat) });
        var newRoot = new Dictionary<string, PdfObject>(root.Entries) { ["ParentTreeNextKey"] = new PdfInteger(nextKey) };
        if (root.Get("ParentTree") is PdfRef oldTree && _objs.ContainsKey(oldTree.Number)) _objs[oldTree.Number] = tree; else newRoot["ParentTree"] = tree;
        _objs[rootRef.Number] = new PdfDictionary(newRoot);
        return new StructureRebuildResult(elements, nums.Count, nextKey, skipped);
    }
}
