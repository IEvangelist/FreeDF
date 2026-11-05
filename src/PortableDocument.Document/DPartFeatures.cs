using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Why a DPart value or its children were not expanded. None does not imply PDF conformance.</summary>
public enum PdfDPartBoundary
{
    None,
    InvalidReference,
    InvalidDictionary,
    InvalidType,
    InvalidChildren,
    Cycle,
    RepeatedReference,
    DepthLimit,
    NodeLimit,
    GroupLimit,
}

/// <summary>
/// Inert catalog /DPartRoot metadata (ISO 32000-2:2020, 14.12, Tables 408/409).
/// Dictionary retains all original entries, including malformed values and raw references.
/// The typed view does not validate record levels, names, page ranges, parent links, or tree conformance.
/// Exposed collections are read-only snapshots; original PDF objects remain accessible, not cloned.
/// </summary>
public sealed record PdfDPartRoot(
    PdfObject RawValue,
    PdfDictionary? Dictionary,
    PdfDPartBoundary Boundary,
    PdfDPartNode? RootNode,
    PdfInteger? RecordLevel,
    PdfArray? NodeNameList)
{
    /// <summary>
    /// Maximum expanded dictionaries on a root-to-leaf path, including the root DPart.
    /// One additional unexpanded DepthLimit occurrence can be exposed and counts toward MaxNodeVisits.
    /// </summary>
    public const int MaxDepth = 64;

    /// <summary>Maximum node slots visited, including the root, malformed entries, and repeated references.</summary>
    public const int MaxNodeVisits = 4096;

    /// <summary>Maximum child-group slots visited across the entire hierarchy, including empty or malformed groups.</summary>
    public const int MaxGroupVisits = 4096;
}

/// <summary>
/// An inert DPart occurrence. Boundary identifies an unexpanded value; Dictionary, when available,
/// is preserved even at a boundary. Children preserve /DParts array-of-arrays order and grouping.
/// ChildrenBoundary reports malformed /DParts or omitted trailing groups when a budget is exhausted.
/// Start, End, and Parent are raw indirect references, not resolved or validated.
/// DPM, Metadata, and AF are raw values; no metadata or associated-file payload is decoded or opened.
/// RawValue is null only when the required /DPartRootNode entry is missing.
/// </summary>
public sealed record PdfDPartNode(
    PdfObject? RawValue,
    PdfDictionary? Dictionary,
    PdfDPartBoundary Boundary,
    IReadOnlyList<PdfDPartGroup> Children,
    PdfDPartBoundary ChildrenBoundary)
{
    public PdfRef? Reference => RawValue as PdfRef;
    public PdfRef? Start => Dictionary?.Get("Start") as PdfRef;
    public PdfRef? End => Dictionary?.Get("End") as PdfRef;
    public PdfRef? Parent => Dictionary?.Get("Parent") as PdfRef;
    public PdfObject? DPM => Dictionary?.Get("DPM");
    public PdfObject? Metadata => Dictionary?.Get("Metadata");
    public PdfObject? AF => Dictionary?.Get("AF");
}

/// <summary>
/// One original /DParts group slot. RawValue preserves its array/reference or malformed value.
/// Boundary reports a malformed group or an omitted tail of child slots due to the node budget.
/// Unvisited slots always remain available through the original array and node dictionary.
/// </summary>
public sealed record PdfDPartGroup(
    PdfObject RawValue,
    IReadOnlyList<PdfDPartNode> Nodes,
    PdfDPartBoundary Boundary);

public static class DPartFeatures
{
    /// <summary>
    /// Reads bounded, depth-first local DPart metadata, or null when catalog /DPartRoot is absent.
    /// Catalog /DPartRoot, /DPartRootNode, and child nodes must be indirect references.
    /// Optional /Type may be absent; present wrong names or types produce InvalidType.
    /// No files are opened, payloads decoded, links repaired, actions executed, or navigation performed.
    /// Page order, non-overlap, coverage, and /Start-/End-/DParts constraints are not validated.
    /// Use ValidateDParts for a separate, bounded tree-link and page-range conformance report.
    /// </summary>
    public static PdfDPartRoot? GetDPartRoot(this PdfDocument doc)
    {
        var raw = doc.File.Catalog.Get("DPartRoot");
        if (raw is null) return null;
        var dictionary = (raw is PdfRef ? doc.File.Resolve(raw) : raw) as PdfDictionary;
        var boundary = raw is not PdfRef || HasWrongGeneration(doc.File, raw) ? PdfDPartBoundary.InvalidReference
            : dictionary is null ? PdfDPartBoundary.InvalidDictionary
            : !HasType(doc.File, dictionary, "DPartRoot") ? PdfDPartBoundary.InvalidType
            : PdfDPartBoundary.None;
        if (boundary != PdfDPartBoundary.None)
            return new(raw, dictionary, boundary, null, null, null);

        var reader = new Reader(doc.File);
        var node = reader.ReadNode(dictionary!.Get("DPartRootNode"), 0);
        return new(raw, dictionary, boundary, node,
            doc.File.Resolve(dictionary.Get("RecordLevel")) as PdfInteger,
            doc.File.Resolve(dictionary.Get("NodeNameList")) as PdfArray);
    }

    static bool HasType(PdfFile file, PdfDictionary dictionary, string type) =>
        dictionary.Get("Type") is not { } raw || file.Resolve(raw).AsName() == type;

    static bool HasWrongGeneration(PdfFile file, PdfObject? raw) =>
        raw is PdfRef reference && file.Xref.TryGetValue(reference.Number, out var entry)
            && reference.Generation != (entry.Compressed ? 0 : entry.GenOrIndex);

    sealed class Reader(PdfFile file)
    {
        readonly HashSet<PdfDictionary> _active = new(ReferenceEqualityComparer.Instance);
        readonly HashSet<PdfDictionary> _seen = new(ReferenceEqualityComparer.Instance);
        int _nodeVisits;
        int _groupVisits;

        public PdfDPartNode ReadNode(PdfObject? raw, int depth)
        {
            _nodeVisits++;
            var dictionary = (raw is PdfRef ? file.Resolve(raw) : raw) as PdfDictionary;
            PdfDPartNode Stop(PdfDPartBoundary boundary) => new(raw, dictionary, boundary, [], PdfDPartBoundary.None);
            if (raw is not PdfRef || HasWrongGeneration(file, raw)) return Stop(PdfDPartBoundary.InvalidReference);
            if (dictionary is null) return Stop(PdfDPartBoundary.InvalidDictionary);
            if (!HasType(file, dictionary, "DPart")) return Stop(PdfDPartBoundary.InvalidType);
            if (_active.Contains(dictionary)) return Stop(PdfDPartBoundary.Cycle);
            if (!_seen.Add(dictionary)) return Stop(PdfDPartBoundary.RepeatedReference);
            if (depth >= PdfDPartRoot.MaxDepth) return Stop(PdfDPartBoundary.DepthLimit);

            _active.Add(dictionary);
            var groups = new List<PdfDPartGroup>();
            var childrenBoundary = PdfDPartBoundary.None;
            if (dictionary.Get("DParts") is { } rawChildren)
            {
                if (file.Resolve(rawChildren) is PdfArray children)
                {
                    foreach (var rawGroup in children.Items)
                    {
                        if (_nodeVisits >= PdfDPartRoot.MaxNodeVisits)
                        {
                            childrenBoundary = PdfDPartBoundary.NodeLimit;
                            break;
                        }
                        if (_groupVisits >= PdfDPartRoot.MaxGroupVisits)
                        {
                            childrenBoundary = PdfDPartBoundary.GroupLimit;
                            break;
                        }
                        _groupVisits++;
                        if (file.Resolve(rawGroup) is not PdfArray group)
                        {
                            groups.Add(new(rawGroup, [], PdfDPartBoundary.InvalidChildren));
                            continue;
                        }

                        var nodes = new List<PdfDPartNode>();
                        var groupBoundary = PdfDPartBoundary.None;
                        foreach (var child in group.Items)
                        {
                            if (_nodeVisits >= PdfDPartRoot.MaxNodeVisits)
                            {
                                groupBoundary = PdfDPartBoundary.NodeLimit;
                                break;
                            }
                            nodes.Add(ReadNode(child, depth + 1));
                        }
                        groups.Add(new(rawGroup, nodes.AsReadOnly(), groupBoundary));
                    }
                }
                else childrenBoundary = PdfDPartBoundary.InvalidChildren;
            }
            _active.Remove(dictionary);
            return new(raw, dictionary, PdfDPartBoundary.None, groups.AsReadOnly(), childrenBoundary);
        }
    }
}
