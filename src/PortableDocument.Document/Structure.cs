using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>A malformed or inconsistent logical-structure reference found by <see cref="StructureTree.ParseIssues"/> or <see cref="StructureTree.CheckConsistency"/>.</summary>
public sealed record StructureIssue(string Code, string Message, string Clause, StructElement? Element, int? PageIndex, int? Mcid);

/// <summary>A node of the logical structure tree (ISO 32000-2 14.7): a structure element, a marked-content reference or an object reference.</summary>
public abstract class StructNode
{
    public StructElement? Parent { get; internal set; }
    /// <summary>Index into <see cref="PdfDocument.Pages"/> of the page this node's content lives on, when known.</summary>
    public int? PageIndex { get; internal set; }
}

/// <summary>Marked-content reference (an integer kid or an /MCR dictionary): content sequence <see cref="Mcid"/> on a page or non-page content stream.</summary>
public sealed class StructMarkedContent : StructNode
{
    public int Mcid { get; internal set; }
    /// <summary>Content stream (for example a form or annotation appearance) holding the marked content, when /Stm was given; null for the page's own content.</summary>
    public PdfStream? Stream { get; internal set; }
    /// <summary>Resolved /StmOwn: the object referencing <see cref="Stream"/>, commonly an annotation owning an appearance stream.
    /// This is not a substitute for the stream's /StructParents entry (ISO 32000-2 Table 357).</summary>
    public PdfObject? StreamOwner { get; internal set; }
}

/// <summary>Object reference (/OBJR), typically an annotation or XObject that belongs to the element.</summary>
public sealed class StructObjectRef : StructNode
{
    public PdfObject? Object { get; internal set; }
}

public sealed class StructElement : StructNode
{
    public PdfDictionary Dict { get; internal set; } = new();
    /// <summary>The raw /S structure type, as written in the file.</summary>
    public string Type { get; internal set; } = "";
    /// <summary>The type after RoleMap resolution (a standard type when the role map leads to one; otherwise the last name reached).</summary>
    public string StandardType { get; internal set; } = "";
    public string? Id { get; internal set; }
    public string? Title { get; internal set; }
    public string? Alt { get; internal set; }
    public string? ActualText { get; internal set; }
    public string? Expansion { get; internal set; }
    /// <summary>/Lang declared on this element itself.</summary>
    public string? Lang { get; internal set; }
    public int Depth { get; internal set; }
    public List<StructNode> Children { get; } = [];

    /// <summary>Own /Lang, else the nearest ancestor's.</summary>
    public string? EffectiveLang
    {
        get
        {
            for (StructElement? e = this; e is not null; e = e.Parent) if (!string.IsNullOrEmpty(e.Lang)) return e.Lang;
            return null;
        }
    }

    public IEnumerable<StructElement> ChildElements => Children.OfType<StructElement>();
    public IEnumerable<StructMarkedContent> MarkedContent => Children.OfType<StructMarkedContent>();

    /// <summary>True when this element is a heading (H or H1..Hn after role mapping).</summary>
    public bool IsHeading => StructureTree.HeadingLevel(StandardType) is not null;

    /// <summary>Looks a key up in the element's attribute objects (/A: a dictionary or an array of dictionaries, newest revision wins). Class maps are not consulted.</summary>
    public PdfObject? Attribute(PdfFile f, string key)
    {
        var a = f.Resolve(Dict.Get("A"));
        if (a is PdfDictionary d) return f.Resolve(d.Get(key));
        if (a is PdfArray arr)
            foreach (var item in arr.Items)
                if (f.Resolve(item) is PdfDictionary ad && ad.Get(key) is { } v) return f.Resolve(v);
        return null;
    }

    public IEnumerable<StructElement> Descendants()
    {
        var stack = new Stack<StructElement>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            yield return e;
            for (var i = e.Children.Count - 1; i >= 0; i--) if (e.Children[i] is StructElement c) stack.Push(c);
        }
    }

    public override string ToString() => Type == StandardType ? Type : $"{Type} ({StandardType})";
}

/// <summary>The parsed StructTreeRoot: element tree, RoleMap, ParentTree and marked-content lookup. Parsing is bounded (depth, node count, cycles) so hostile files cannot hang or exhaust memory.</summary>
public sealed class StructureTree
{
    public const int MaxDepth = 256;
    public const int MaxNodes = 500_000;

    static readonly HashSet<string> Standard = new(StringComparer.Ordinal)
    {
        "Document", "DocumentFragment", "Part", "Art", "Sect", "Div", "BlockQuote", "Caption", "TOC", "TOCI", "Index", "NonStruct", "Private",
        "Aside", "Title", "FENote", "Sub", "P", "H", "L", "LI", "Lbl", "LBody", "Table", "TR", "TH", "TD", "THead", "TBody", "TFoot",
        "Span", "Quote", "Note", "Reference", "BibEntry", "Code", "Link", "Annot", "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP",
        "Figure", "Formula", "Form", "Artifact",
    };

    readonly PdfDocument _doc;
    readonly Dictionary<PdfDictionary, StructElement> _byDict = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<PdfDictionary, int> _pageIndex = new(ReferenceEqualityComparer.Instance);
    Dictionary<(int Page, int Mcid), StructMarkedContent>? _mcidMap;
    readonly Dictionary<PdfStream, Dictionary<int, StructMarkedContent>> _streamMcidMap = new(ReferenceEqualityComparer.Instance);
    int _nodes;

    public PdfDictionary Root { get; }
    public IReadOnlyDictionary<string, string> RoleMap { get; }
    public List<StructElement> Children { get; } = [];
    /// <summary>True when parsing stopped early because a depth or node limit was hit.</summary>
    public bool Truncated { get; private set; }

    const int MaxIssues = 1000;
    readonly List<StructureIssue> _parseIssues = [];

    /// <summary>True when more than 1000 structure issues were found and only the first were retained.</summary>
    public bool IssuesTruncated { get; private set; }

    /// <summary>Malformed or non-local MCR/OBJR/element references encountered while parsing. Such nodes are ignored or kept inert; nothing is inferred from them.</summary>
    public IReadOnlyList<StructureIssue> ParseIssues => _parseIssues;

    void AddIssue(string code, string message, string clause, StructElement? element, int? page = null, int? mcid = null) =>
        AddIssue(_parseIssues, code, message, clause, element, page, mcid);

    void AddIssue(List<StructureIssue> issues, string code, string message, string clause, StructElement? element, int? page = null, int? mcid = null)
    {
        if (issues.Count >= MaxIssues) { IssuesTruncated = true; return; }
        issues.Add(new StructureIssue(code, message, clause, element, page ?? element?.PageIndex, mcid));
    }

    /// <summary>Parse issues plus a bounded cross-check of /P parents, duplicate marked-content claims, and /ParentTree entries against the parsed tree
    /// (ISO 32000-2 14.7.2, 14.7.5.2-14.7.5.4). Pure inspection: the tree and all raw dictionaries are unchanged.</summary>
    public IReadOnlyList<StructureIssue> CheckConsistency()
    {
        var issues = new List<StructureIssue>(_parseIssues);
        var hasParentTree = F.Resolve(Root.Get("ParentTree")) is PdfDictionary;
        var lookups = new Dictionary<int, PdfObject?>();
        PdfObject? Lookup(int key)
        {
            if (!lookups.TryGetValue(key, out var value)) lookups[key] = value = ParentTreeLookup(key);
            return value;
        }
        var pageClaims = new HashSet<(int Page, int Mcid)>();
        var streamClaims = new HashSet<(PdfStream Stream, int Mcid)>(new StreamClaimComparer());
        var objects = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (!hasParentTree && Elements().Any(e => e.Children.Any(c => c is not StructElement)))
            AddIssue(issues, "STRUCT-PARENTTREE-MISSING", "structure tree has no /ParentTree number tree", "ISO 32000-2 14.7.5.4", null);

        foreach (var e in Elements())
        {
            if (F.Resolve(e.Dict.Get("P")) is PdfDictionary declared)
            {
                if (!ReferenceEquals(declared, e.Parent?.Dict ?? Root))
                    AddIssue(issues, "STRUCT-P-MISMATCH", "element /P does not identify the element or root that lists it in /K", "ISO 32000-2 14.7.2", e);
            }
            else AddIssue(issues, "STRUCT-P-MISSING", "element has no /P parent reference", "ISO 32000-2 14.7.2", e);

            foreach (var child in e.Children)
            {
                if (child is StructMarkedContent mc)
                {
                    var owner = mc.Stream?.Dict;
                    if (mc.Stream is null && mc.PageIndex is null)
                    {
                        AddIssue(issues, "STRUCT-MCR-NOPAGE", "marked-content reference has no /Pg on itself or an ancestor, so its page is ambiguous", "ISO 32000-2 14.7.5.2", e, mcid: mc.Mcid);
                        continue;
                    }
                    var duplicate = mc.Stream is { } stream ? !streamClaims.Add((stream, mc.Mcid)) : !pageClaims.Add((mc.PageIndex!.Value, mc.Mcid));
                    if (duplicate)
                        AddIssue(issues, "STRUCT-MCID-DUPLICATE", "marked-content sequence is referenced by more than one structure content item; the first in structure order owns it", "ISO 32000-2 14.7.5.2", e, mc.PageIndex, mc.Mcid);
                    owner ??= mc.PageIndex is { } p && p < _doc.Pages.Count ? _doc.Pages[p].Dict : null;
                    if (!hasParentTree || owner is null) continue;
                    if (NonNegativeInteger(owner.Get("StructParents")) is not { } key)
                        AddIssue(issues, "STRUCT-PARENTTREE-NOKEY", "content stream or page holding the marked content has no /StructParents key", "ISO 32000-2 14.7.5.4", e, mc.PageIndex, mc.Mcid);
                    else if (Lookup(key) is not PdfArray array || mc.Mcid >= array.Count || !ReferenceEquals(F.Resolve(array[mc.Mcid]), e.Dict))
                        AddIssue(issues, "STRUCT-PARENTTREE-MISMATCH", "/ParentTree entry for this MCID does not return the referencing structure element", "ISO 32000-2 14.7.5.4", e, mc.PageIndex, mc.Mcid);
                }
                else if (child is StructObjectRef reference && reference.Object is { } obj)
                {
                    var target = obj switch { PdfStream s => s.Dict, PdfDictionary d => d, _ => null };
                    if (target is null) continue;
                    if (!objects.Add(target))
                        AddIssue(issues, "STRUCT-OBJR-DUPLICATE", "object is referenced by more than one /OBJR; the first in structure order owns it", "ISO 32000-2 14.7.5.3", e);
                    if (!hasParentTree) continue;
                    if (NonNegativeInteger(target.Get("StructParent")) is not { } key
                        || Lookup(key) is not PdfDictionary parent || !ReferenceEquals(parent, e.Dict))
                        AddIssue(issues, "STRUCT-OBJR-PARENTTREE", "object /StructParent does not map through /ParentTree to the referencing structure element", "ISO 32000-2 14.7.5.4", e);
                }
            }
        }
        return issues;
    }

    sealed class StreamClaimComparer : IEqualityComparer<(PdfStream Stream, int Mcid)>
    {
        public bool Equals((PdfStream Stream, int Mcid) x, (PdfStream Stream, int Mcid) y) => x.Mcid == y.Mcid && ReferenceEquals(x.Stream, y.Stream);
        public int GetHashCode((PdfStream Stream, int Mcid) value) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.Stream), value.Mcid);
    }

    /// <summary>Audits the /ParentTree number tree: ordering, duplicate keys, /Limits, cycles and malformed pairs (ISO 32000-2 7.9.7, Table 37).</summary>
    public PdfTreeDiagnostics? GetParentTreeDiagnostics() =>
        F.Resolve(Root.Get("ParentTree")) is PdfDictionary tree ? PdfTreeDiagnostics.Audit(F, tree, numberTree: true) : null;

    public static bool IsStandardType(string type) => Standard.Contains(type) || HeadingLevel(type) is not null;

    /// <summary>1..n for H1..Hn, 0 for the unnumbered H, null for anything else.</summary>
    public static int? HeadingLevel(string type)
    {
        if (type == "H") return 0;
        return type.Length is >= 2 and <= 9 && type[0] == 'H' && type.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0 && type[1] != '0' && int.TryParse(type.AsSpan(1), out var n) ? n : null;
    }

    StructureTree(PdfDocument doc, PdfDictionary root)
    {
        _doc = doc; Root = root;
        for (var i = 0; i < doc.Pages.Count; i++) _pageIndex.TryAdd(doc.Pages[i].Dict, i);
        var rm = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.File.Resolve(root.Get("RoleMap")) is PdfDictionary rd)
            foreach (var (k, v) in rd.Entries)
                if (doc.File.Resolve(v).AsName() is { } name) rm[k] = name;
        RoleMap = rm;
    }

    PdfFile F => _doc.File;

    public static StructureTree? Parse(PdfDocument doc)
    {
        try
        {
            if (doc.File.Resolve(doc.File.Catalog.Get("StructTreeRoot")) is not PdfDictionary root) return null;
            var t = new StructureTree(doc, root);
            t.ParseKids(null, root.Get("K"), 0, null);
            return t;
        }
        catch (Exception e) when (e is PdfParseException or InvalidCastException or NullReferenceException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Maps a structure type through the RoleMap until a standard type is reached (cycle-safe).</summary>
    public string ResolveRole(string type)
    {
        var cur = type;
        for (var i = 0; i < 32 && !IsStandardType(cur); i++)
        {
            if (!RoleMap.TryGetValue(cur, out var next) || next == cur) break;
            cur = next;
        }
        return cur;
    }

    int? PageOf(PdfObject? pg) => F.Resolve(pg) is PdfDictionary d && _pageIndex.TryGetValue(d, out var i) ? i : null;

    void ParseKids(StructElement? parent, PdfObject? k, int depth, int? inheritedPage)
    {
        var o = F.Resolve(k);
        if (o is PdfArray arr)
        {
            foreach (var item in arr.Items)
            {
                if (Truncated) return;
                ParseKid(parent, item, depth, inheritedPage);
            }
        }
        else ParseKid(parent, o, depth, inheritedPage);
    }

    void ParseKid(StructElement? parent, PdfObject? raw, int depth, int? inheritedPage)
    {
        if (_nodes >= MaxNodes || depth > MaxDepth) { Truncated = true; return; }
        var o = F.Resolve(raw);
        StructNode? node = null;
        switch (o)
        {
            case PdfInteger { Value: >= 0 and <= int.MaxValue } id when parent is not null:
                node = new StructMarkedContent { Mcid = (int)id.Value, PageIndex = inheritedPage };
                break;
            case PdfDictionary d:
                var type = F.Resolve(d.Get("Type")).AsName();
                if (type == "MCR")
                {
                    if (parent is null || NonNegativeInteger(d.Get("MCID")) is not { } mcid)
                    {
                        AddIssue("STRUCT-MCR-MCID", "marked-content reference has no parent element or no non-negative /MCID and was ignored", "ISO 32000-2 14.7.5.2", parent);
                        return;
                    }
                    var stream = F.Resolve(d.Get("Stm")) as PdfStream;
                    if (d.Get("Stm") is not null && (d.Get("Stm") is not PdfRef || stream is null))
                    {
                        AddIssue("STRUCT-MCR-STM-NONLOCAL", "marked-content reference /Stm is not an indirect reference to a stream in this file and was ignored (inert)", "ISO 32000-2 14.7.5.2", parent, mcid: mcid);
                        return;
                    }
                    PdfObject? streamOwner = null;
                    if (d.Get("StmOwn") is { } rawOwner)
                    {
                        if (stream is null || rawOwner is not PdfRef)
                        {
                            AddIssue("STRUCT-MCR-STMOWN", "marked-content reference /StmOwn requires /Stm and an indirect reference and was ignored", "ISO 32000-2 14.7.5.2", parent, mcid: mcid);
                            return;
                        }
                        streamOwner = F.Resolve(rawOwner);
                        if (streamOwner is null or PdfNull)
                        {
                            AddIssue("STRUCT-MCR-STMOWN", "marked-content reference /StmOwn does not resolve and was ignored", "ISO 32000-2 14.7.5.2", parent, mcid: mcid);
                            return;
                        }
                    }
                    node = new StructMarkedContent
                    {
                        Mcid = mcid,
                        PageIndex = PageOf(d.Get("Pg")) ?? inheritedPage,
                        Stream = stream,
                        StreamOwner = streamOwner,
                    };
                }
                else if (type == "OBJR")
                {
                    if (parent is null)
                    {
                        AddIssue("STRUCT-OBJR-ROOT", "object reference has no parent structure element and was ignored", "ISO 32000-2 14.7.5.3", null);
                        return;
                    }
                    var target = F.Resolve(d.Get("Obj"));
                    if (target is not (PdfDictionary or PdfStream))
                        AddIssue("STRUCT-OBJR-OBJ", "object reference /Obj does not resolve to a dictionary or stream in this file; no content is inferred", "ISO 32000-2 14.7.5.3", parent);
                    node = new StructObjectRef { Object = target, PageIndex = PageOf(d.Get("Pg")) ?? inheritedPage };
                }
                else
                {
                    if (ReferenceEquals(d, Root) || _byDict.ContainsKey(d))
                    {
                        AddIssue("STRUCT-ELEMENT-REVISITED", "structure element is reachable more than once (cycle or shared child); later occurrences were ignored", "ISO 32000-2 14.7.2", parent);
                        return;
                    }
                    var el = BuildElement(d, depth, inheritedPage);
                    _byDict[d] = el;
                    node = el;
                    _nodes++;
                    el.Parent = parent;
                    parent?.Children.Add(el);
                    if (parent is null) Children.Add(el);
                    ParseKids(el, d.Get("K"), depth + 1, el.PageIndex);
                    return;
                }
                break;
        }
        if (node is null || parent is null) return;
        _nodes++;
        node.Parent = parent;
        parent.Children.Add(node);
    }

    StructElement BuildElement(PdfDictionary d, int depth, int? inheritedPage)
    {
        string? Str(string key) => F.Resolve(d.Get(key)) is PdfString s ? s.Text : null;
        var type = F.Resolve(d.Get("S")).AsName() ?? "";
        return new StructElement
        {
            Dict = d,
            Type = type,
            StandardType = ResolveRole(type),
            Id = F.Resolve(d.Get("ID")) is PdfString id ? id.Text : null,
            Title = Str("T"),
            Alt = Str("Alt"),
            ActualText = Str("ActualText"),
            Expansion = Str("E"),
            Lang = Str("Lang"),
            Depth = depth,
            PageIndex = PageOf(d.Get("Pg")) ?? inheritedPage,
        };
    }

    /// <summary>All structure elements in document (depth-first) order.</summary>
    public IEnumerable<StructElement> Elements()
    {
        foreach (var c in Children)
            foreach (var e in c.Descendants()) yield return e;
    }

    public StructElement? ElementFor(PdfDictionary dict) => _byDict.GetValueOrDefault(dict);

    /// <summary>The marked-content reference for (page, MCID) found by walking the tree (not the ParentTree).</summary>
    public StructMarkedContent? FindMarkedContent(int pageIndex, int mcid)
    {
        BuildMcidMaps();
        return _mcidMap!.GetValueOrDefault((pageIndex, mcid));
    }

    /// <summary>The marked-content reference for an MCID in a particular non-page stream, independent of its page placements or /StmOwn.</summary>
    public StructMarkedContent? FindMarkedContent(PdfStream stream, int mcid)
    {
        BuildMcidMaps();
        return _streamMcidMap.TryGetValue(stream, out var map) ? map.GetValueOrDefault(mcid) : null;
    }

    void BuildMcidMaps()
    {
        if (_mcidMap is null)
        {
            var m = new Dictionary<(int, int), StructMarkedContent>();
            foreach (var e in Elements())
                foreach (var mc in e.MarkedContent)
                    if (mc.Stream is { } stream)
                    {
                        if (!_streamMcidMap.TryGetValue(stream, out var map)) _streamMcidMap[stream] = map = [];
                        map.TryAdd(mc.Mcid, mc);
                    }
                    else if (mc.PageIndex is { } p) m.TryAdd((p, mc.Mcid), mc);
            _mcidMap = m;
        }
    }

    /// <summary>Looks an integer key up in the ParentTree number tree. The tree is indexed once in a single bounded pass (first occurrence of a key wins).</summary>
    public PdfObject? ParentTreeLookup(int key) =>
        key >= 0 && ParentTreeIndex().TryGetValue(key, out var value) ? value : null;

    Dictionary<int, PdfObject?>? _parentTreeIndex;

    /// <summary>True when indexing the /ParentTree stopped at the node, entry or depth bound.</summary>
    public bool ParentTreeIndexTruncated { get; private set; }

    Dictionary<int, PdfObject?> ParentTreeIndex()
    {
        if (_parentTreeIndex is not null) return _parentTreeIndex;
        var index = new Dictionary<int, PdfObject?>();
        _parentTreeIndex = index;
        if (F.Resolve(Root.Get("ParentTree")) is not PdfDictionary root) return index;
        var pending = new Stack<(PdfDictionary Node, int Depth)>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        pending.Push((root, 0));
        var work = 0;
        while (pending.TryPop(out var entry))
        {
            var (node, depth) = entry;
            if (!seen.Add(node)) continue;
            if (depth >= 32 || ++work > MaxNodes) { ParentTreeIndexTruncated = true; continue; }
            if (F.Resolve(node.Get("Nums")) is PdfArray nums)
            {
                for (var i = 0; i + 1 < nums.Count; i += 2)
                {
                    if (++work > MaxNodes) { ParentTreeIndexTruncated = true; break; }
                    if (NonNegativeInteger(nums[i]) is { } k && !index.ContainsKey(k)) index[k] = F.Resolve(nums[i + 1]);
                }
                continue;
            }
            if (F.Resolve(node.Get("Kids")) is not PdfArray kids) continue;
            for (var i = kids.Count - 1; i >= 0; i--)
                if (F.Resolve(kids[i]) is PdfDictionary kd) pending.Push((kd, depth + 1));
        }
        return index;
    }

    /// <summary>The structure element that owns marked-content sequence <paramref name="mcid"/> on a page, via the page's /StructParents and the ParentTree.</summary>
    public StructElement? ElementFromParentTree(int pageIndex, int mcid)
    {
        if (pageIndex < 0 || pageIndex >= _doc.Pages.Count || mcid < 0) return null;
        return ElementFromParentTree(_doc.Pages[pageIndex].Dict, mcid);
    }

    /// <summary>The owning element via a non-page content stream's /StructParents key and its stream-local MCID.</summary>
    public StructElement? ElementFromParentTree(PdfStream stream, int mcid) => ElementFromParentTree(stream.Dict, mcid);

    /// <summary>The owning element via the referenced content stream's /StructParents, or the page's /StructParents when /Stm is absent.
    /// /StmOwn identifies an object referencing the stream; its /StructParent or /StructParents never replaces the content stream's key.</summary>
    public StructElement? ElementFromParentTree(StructMarkedContent content) =>
        content.Stream is { } stream ? ElementFromParentTree(stream, content.Mcid)
        : content.PageIndex is { } page ? ElementFromParentTree(page, content.Mcid) : null;

    StructElement? ElementFromParentTree(PdfDictionary owner, int mcid)
    {
        if (mcid < 0 || NonNegativeInteger(owner.Get("StructParents")) is not { } key) return null;
        if (ParentTreeLookup(key) is PdfArray a && mcid < a.Count && F.Resolve(a[mcid]) is PdfDictionary d) return ElementFor(d);
        return null;
    }

    int? NonNegativeInteger(PdfObject? value) =>
        F.Resolve(value) is PdfInteger { Value: >= 0 and <= int.MaxValue } n ? (int)n.Value : null;
}
