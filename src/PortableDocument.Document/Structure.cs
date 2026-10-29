using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>A node of the logical structure tree (ISO 32000-2 14.7): a structure element, a marked-content reference or an object reference.</summary>
public abstract class StructNode
{
    public StructElement? Parent { get; internal set; }
    /// <summary>Index into <see cref="PdfDocument.Pages"/> of the page this node's content lives on, when known.</summary>
    public int? PageIndex { get; internal set; }
}

/// <summary>Marked-content reference (an integer kid or an /MCR dictionary): content sequence <see cref="Mcid"/> on a page or form XObject.</summary>
public sealed class StructMarkedContent : StructNode
{
    public int Mcid { get; internal set; }
    /// <summary>Content stream (form XObject) holding the marked content, when /Stm was given; null for the page's own content.</summary>
    public PdfStream? Stream { get; internal set; }
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
    int _nodes;

    public PdfDictionary Root { get; }
    public IReadOnlyDictionary<string, string> RoleMap { get; }
    public List<StructElement> Children { get; } = [];
    /// <summary>True when parsing stopped early because a depth or node limit was hit.</summary>
    public bool Truncated { get; private set; }

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
            case PdfInteger or PdfReal when parent is not null:
                node = new StructMarkedContent { Mcid = (int)(o.AsNumber() ?? 0), PageIndex = inheritedPage };
                break;
            case PdfDictionary d:
                var type = F.Resolve(d.Get("Type")).AsName();
                if (type == "MCR")
                {
                    if (parent is null) return;
                    node = new StructMarkedContent
                    {
                        Mcid = (int)(F.Resolve(d.Get("MCID")).AsNumber() ?? 0),
                        PageIndex = PageOf(d.Get("Pg")) ?? inheritedPage,
                        Stream = F.Resolve(d.Get("Stm")) as PdfStream,
                    };
                }
                else if (type == "OBJR")
                {
                    if (parent is null) return;
                    node = new StructObjectRef { Object = F.Resolve(d.Get("Obj")), PageIndex = PageOf(d.Get("Pg")) ?? inheritedPage };
                }
                else
                {
                    if (ReferenceEquals(d, Root) || _byDict.ContainsKey(d)) return;
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
        if (_mcidMap is null)
        {
            var m = new Dictionary<(int, int), StructMarkedContent>();
            foreach (var e in Elements())
                foreach (var mc in e.MarkedContent)
                    if (mc.PageIndex is { } p && mc.Stream is null) m.TryAdd((p, mc.Mcid), mc);
            _mcidMap = m;
        }
        return _mcidMap.GetValueOrDefault((pageIndex, mcid));
    }

    /// <summary>Looks an integer key up in the ParentTree number tree.</summary>
    public PdfObject? ParentTreeLookup(int key)
    {
        var node = F.Resolve(Root.Get("ParentTree")) as PdfDictionary;
        for (var depth = 0; node is not null && depth < 32; depth++)
        {
            if (F.Resolve(node.Get("Nums")) is PdfArray nums)
            {
                for (var i = 0; i + 1 < nums.Count; i += 2)
                    if (F.Resolve(nums[i]).AsNumber() is { } n && (int)n == key) return F.Resolve(nums[i + 1]);
                return null;
            }
            if (F.Resolve(node.Get("Kids")) is not PdfArray kids) return null;
            PdfDictionary? next = null;
            foreach (var kid in kids.Items)
            {
                if (F.Resolve(kid) is not PdfDictionary kd) continue;
                if (F.Resolve(kd.Get("Limits")) is PdfArray { Count: >= 2 } lim
                    && F.Resolve(lim[0]).AsNumber() is { } lo && F.Resolve(lim[1]).AsNumber() is { } hi)
                {
                    if (key < lo || key > hi) continue;
                }
                next = kd; break;
            }
            node = next;
        }
        return null;
    }

    /// <summary>The structure element that owns marked-content sequence <paramref name="mcid"/> on a page, via the page's /StructParents and the ParentTree.</summary>
    public StructElement? ElementFromParentTree(int pageIndex, int mcid)
    {
        if (pageIndex < 0 || pageIndex >= _doc.Pages.Count || mcid < 0) return null;
        if (F.Resolve(_doc.Pages[pageIndex].Dict.Get("StructParents")).AsNumber() is not { } key) return null;
        if (ParentTreeLookup((int)key) is PdfArray a && mcid < a.Count && F.Resolve(a[mcid]) is PdfDictionary d) return ElementFor(d);
        return null;
    }
}
