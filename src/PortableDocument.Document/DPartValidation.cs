using System.Xml;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Diagnostics for the bounded hierarchy, root-level metadata, and page-range subset of ISO 32000-2, 14.12.</summary>
public enum PdfDPartIssueCode
{
    HierarchyBoundary,
    PageTreeBoundary,
    InvalidParent,
    EmptyChildren,
    EmptyChildGroup,
    RangeOnNonLeaf,
    EndWithoutStart,
    InvalidStart,
    InvalidEnd,
    RedundantEnd,
    ReversedRange,
    PageOrder,
    OverlappingRanges,
    UncoveredPage,
    InvalidPageAssociation,
    InvalidRecordLevel,
    RecordLevelOutOfRange,
    InvalidNodeNameList,
    NodeNameCountMismatch,
    InvalidNodeNameType,
    InvalidNodeName,
    NodeNameListBoundary,
}

/// <summary>
/// Node identifies the original DPart occurrence, when applicable. PageIndex is zero-based.
/// Boundary preserves the reason for incomplete hierarchy, page-tree, or name-list inspection.
/// RawValue retains the boundary or invalid root metadata value, or the containing array when
/// its tail was not visited. Invalid name items retain their original array entry.
/// </summary>
public sealed record PdfDPartIssue(
    PdfDPartIssueCode Code,
    PdfDPartNode? Node = null,
    int? PageIndex = null,
    PdfDPartBoundary Boundary = PdfDPartBoundary.None,
    PdfObject? RawValue = null);

/// <summary>
/// A report for tree links, child arrays, RecordLevel, NodeNameList, page ranges, coverage,
/// order, and page /DPart links only. IsConformant does not assert overall DPart or PDF
/// conformance: DPM, Metadata, and AF semantics are outside this slice. IsComplete is false when a boundary
/// prevents inspection. Raw dictionaries and references remain available through Root.
/// For incomplete reports, range diagnostics describe only the inspected page-tree subset.
/// Coverage and page back-links are checked only after complete traversals.
/// RootNode is level zero; depth is the maximum number of DPart nodes on a root-to-leaf path.
/// RecordLevel must identify an existing level. NodeNameList must name exactly those levels,
/// using XML Names (not the narrower NCNames). Depth comparisons require a complete hierarchy.
/// </summary>
public sealed record PdfDPartValidationReport(
    PdfDPartRoot Root,
    IReadOnlyList<PdfDPartIssue> Issues,
    bool IsComplete)
{
    public bool IsConformant => IsComplete && Issues.Count == 0;
}

public static class DPartValidation
{
    /// <summary>
    /// Validates a bounded, additive subset of 14.12.2-14.12.4, Tables 408/409.
    /// Returns null if catalog /DPartRoot is absent. Does not change the inert GetDPartRoot API.
    /// Uses its hierarchy budgets and independently walks the page tree with MaxDepth and
    /// MaxNodeVisits from PdfDPartRoot (including internal, malformed, and repeated page slots).
    /// Reference checks honor xref generations and the parser's 32-hop resolution limit.
    /// Work and report size are linear in these budgets, not the sum of overlapping range lengths.
    /// NodeNameList item inspection has an independent MaxNodeVisits budget; unvisited tails
    /// produce a NodeNameListBoundary diagnostic. XML name checking is linear in name length.
    /// Does not navigate, repair links, decode payloads, or mutate PDF dictionaries.
    /// </summary>
    public static PdfDPartValidationReport? ValidateDParts(this PdfDocument doc)
    {
        var root = doc.GetDPartRoot();
        return root is null ? null : new Validator(doc.File, root).Validate();
    }

    sealed class Validator(PdfFile file, PdfDPartRoot root)
    {
        readonly List<PdfDPartIssue> _issues = [];
        readonly List<PdfDictionary> _pages = [];
        readonly Dictionary<PdfDictionary, int> _pageIndices = new(ReferenceEqualityComparer.Instance);
        readonly HashSet<PdfDictionary> _pageNodes = new(ReferenceEqualityComparer.Instance);
        readonly List<PdfDPartNode> _owners = [];
        int[] _coverage = [];
        int[] _ownerChanges = [];
        int _pageVisits;
        int _previousEnd = -1;
        bool _complete = true;
        bool _hierarchyComplete = true;
        int _levelCount;

        void Boundary(PdfDPartIssueCode code, PdfDPartBoundary boundary,
            PdfDPartNode? node = null, PdfObject? raw = null)
        {
            if (boundary == PdfDPartBoundary.None) return;
            _complete = false;
            if (code == PdfDPartIssueCode.HierarchyBoundary) _hierarchyComplete = false;
            _issues.Add(new(code, node, Boundary: boundary, RawValue: raw));
        }

        public PdfDPartValidationReport Validate()
        {
            var boundary = root.Boundary;
            if (boundary == PdfDPartBoundary.None && !PointsTo(root.RawValue, root.Dictionary!))
                boundary = PdfDPartBoundary.InvalidReference;
            if (boundary == PdfDPartBoundary.None && !HasType(root.Dictionary!, "DPartRoot"))
                boundary = PdfDPartBoundary.InvalidType;
            Boundary(PdfDPartIssueCode.HierarchyBoundary, boundary, raw: root.RawValue);
            if (boundary == PdfDPartBoundary.None && root.RootNode is { } node)
            {
                ReadPages(file.Catalog.Get("Pages"), 0);
                _coverage = new int[_pages.Count + 1];
                _ownerChanges = new int[_pages.Count + 1];
                ReadNode(node, root.Dictionary!, 0);
                if (_complete) CheckPages();
            }
            CheckRootLevels();
            return new(root, _issues.AsReadOnly(), _complete);
        }

        void CheckRootLevels()
        {
            if (root.Dictionary is not { } dictionary) return;
            if (dictionary.Get("RecordLevel") is { } rawLevel)
            {
                if (Resolve(rawLevel) is not PdfInteger level)
                    _issues.Add(new(PdfDPartIssueCode.InvalidRecordLevel, RawValue: rawLevel));
                // A zero-based level must exist in the actual hierarchy, not just be nonnegative.
                else if (level.Value < 0 || (_hierarchyComplete && level.Value >= _levelCount))
                    _issues.Add(new(PdfDPartIssueCode.RecordLevelOutOfRange, RawValue: rawLevel));
            }
            if (dictionary.Get("NodeNameList") is not { } rawNames) return;
            if (Resolve(rawNames) is not PdfArray names)
            {
                _issues.Add(new(PdfDPartIssueCode.InvalidNodeNameList, RawValue: rawNames));
                return;
            }
            if (_hierarchyComplete && names.Count != _levelCount)
                _issues.Add(new(PdfDPartIssueCode.NodeNameCountMismatch, RawValue: rawNames));
            for (var i = 0; i < Math.Min(names.Count, PdfDPartRoot.MaxNodeVisits); i++)
            {
                var rawName = names[i];
                if (Resolve(rawName) is not PdfName name)
                {
                    _issues.Add(new(PdfDPartIssueCode.InvalidNodeNameType, RawValue: rawName));
                    continue;
                }
                if (name.Value.Length == 0)
                {
                    _issues.Add(new(PdfDPartIssueCode.InvalidNodeName, RawValue: rawName));
                    continue;
                }
                try
                {
                    XmlConvert.VerifyName(name.Value);
                }
                catch (XmlException)
                {
                    _issues.Add(new(PdfDPartIssueCode.InvalidNodeName, RawValue: rawName));
                }
            }
            if (names.Count > PdfDPartRoot.MaxNodeVisits)
                Boundary(PdfDPartIssueCode.NodeNameListBoundary, PdfDPartBoundary.NodeLimit, raw: rawNames);
        }

        PdfObject? Resolve(PdfObject? raw)
        {
            // PdfFile.Resolve has the same hop cap, but does not check reference generations.
            for (var depth = 0; raw is PdfRef reference && depth < 32; depth++)
            {
                if (!file.Xref.TryGetValue(reference.Number, out var entry) || !entry.InUse ||
                    reference.Generation != (entry.Compressed ? 0 : entry.GenOrIndex))
                    return null;
                raw = file.GetObject(reference.Number);
            }
            return raw is PdfRef ? null : raw;
        }

        bool HasType(PdfDictionary dictionary, string type) =>
            dictionary.Get("Type") is not { } raw || Resolve(raw).AsName() == type;

        void ReadPages(PdfObject? raw, int depth)
        {
            _pageVisits++;
            var dictionary = Resolve(raw) as PdfDictionary;
            var boundary = raw is not PdfRef ? PdfDPartBoundary.InvalidReference
                : dictionary is null ? PdfDPartBoundary.InvalidDictionary
                : !_pageNodes.Add(dictionary) ? PdfDPartBoundary.RepeatedReference
                : depth >= PdfDPartRoot.MaxDepth ? PdfDPartBoundary.DepthLimit
                : PdfDPartBoundary.None;
            if (boundary != PdfDPartBoundary.None)
            {
                Boundary(PdfDPartIssueCode.PageTreeBoundary, boundary, raw: raw);
                return;
            }

            var type = Resolve(dictionary!.Get("Type")).AsName();
            if (type == "Page")
            {
                _pageIndices.Add(dictionary, _pages.Count);
                _pages.Add(dictionary);
                return;
            }
            if (type != "Pages" || Resolve(dictionary.Get("Kids")) is not PdfArray kids)
            {
                Boundary(PdfDPartIssueCode.PageTreeBoundary, PdfDPartBoundary.InvalidChildren, raw: raw);
                return;
            }
            foreach (var child in kids.Items)
            {
                if (_pageVisits >= PdfDPartRoot.MaxNodeVisits)
                {
                    Boundary(PdfDPartIssueCode.PageTreeBoundary, PdfDPartBoundary.NodeLimit, raw: kids);
                    break;
                }
                ReadPages(child, depth + 1);
            }
        }

        bool PointsTo(PdfObject? raw, PdfDictionary expected) =>
            raw is PdfRef && ReferenceEquals(Resolve(raw), expected);

        void ReadNode(PdfDPartNode node, PdfDictionary parent, int depth)
        {
            var boundary = node.Boundary;
            if (boundary == PdfDPartBoundary.None && !PointsTo(node.RawValue, node.Dictionary!))
                boundary = PdfDPartBoundary.InvalidReference;
            if (boundary == PdfDPartBoundary.None && !HasType(node.Dictionary!, "DPart"))
                boundary = PdfDPartBoundary.InvalidType;
            Boundary(PdfDPartIssueCode.HierarchyBoundary, boundary, node, node.RawValue);
            if (node.Dictionary is not { } dictionary) return;
            if (!PointsTo(dictionary.Get("Parent"), parent))
                _issues.Add(new(PdfDPartIssueCode.InvalidParent, node));
            if (boundary != PdfDPartBoundary.None) return;

            _levelCount = Math.Max(_levelCount, depth + 1);
            Boundary(PdfDPartIssueCode.HierarchyBoundary, node.ChildrenBoundary, node, dictionary.Get("DParts"));
            var hasChildren = dictionary.Get("DParts") is not null;
            var start = dictionary.Get("Start");
            var end = dictionary.Get("End");
            if (hasChildren)
            {
                if (start is not null || end is not null)
                    _issues.Add(new(PdfDPartIssueCode.RangeOnNonLeaf, node));
                if (Resolve(dictionary.Get("DParts")) is not PdfArray children)
                {
                    if (node.ChildrenBoundary == PdfDPartBoundary.None)
                        Boundary(PdfDPartIssueCode.HierarchyBoundary, PdfDPartBoundary.InvalidChildren,
                            node, dictionary.Get("DParts"));
                    return;
                }
                if (children.Count == 0)
                    _issues.Add(new(PdfDPartIssueCode.EmptyChildren, node));
                foreach (var group in node.Children)
                {
                    Boundary(PdfDPartIssueCode.HierarchyBoundary, group.Boundary, node, group.RawValue);
                    if (Resolve(group.RawValue) is not PdfArray array)
                    {
                        if (group.Boundary == PdfDPartBoundary.None)
                            Boundary(PdfDPartIssueCode.HierarchyBoundary, PdfDPartBoundary.InvalidChildren, node, group.RawValue);
                        continue;
                    }
                    if (array.Count == 0)
                        _issues.Add(new(PdfDPartIssueCode.EmptyChildGroup, node));
                    foreach (var child in group.Nodes) ReadNode(child, dictionary, depth + 1);
                }
                return;
            }
            if (start is null)
            {
                if (end is not null) _issues.Add(new(PdfDPartIssueCode.EndWithoutStart, node));
                return;
            }

            var first = PageIndex(start);
            var last = end is null ? first : PageIndex(end);
            if (first < 0) _issues.Add(new(PdfDPartIssueCode.InvalidStart, node));
            if (end is not null && last < 0) _issues.Add(new(PdfDPartIssueCode.InvalidEnd, node));
            if (first < 0 || last < 0) return;
            if (end is not null && first == last)
                _issues.Add(new(PdfDPartIssueCode.RedundantEnd, node));
            if (last < first)
            {
                _issues.Add(new(PdfDPartIssueCode.ReversedRange, node));
                return;
            }
            if (first <= _previousEnd) _issues.Add(new(PdfDPartIssueCode.PageOrder, node));
            _previousEnd = last;

            // Difference counts and XOR owner IDs avoid revisiting pages for overlapping ranges.
            _owners.Add(node);
            var owner = _owners.Count;
            _coverage[first]++;
            _coverage[last + 1]--;
            _ownerChanges[first] ^= owner;
            _ownerChanges[last + 1] ^= owner;
        }

        int PageIndex(PdfObject raw) =>
            raw is PdfRef && Resolve(raw) is PdfDictionary dictionary &&
            _pageIndices.TryGetValue(dictionary, out var index) ? index : -1;

        void CheckPages()
        {
            var count = 0;
            var owner = 0;
            for (var i = 0; i < _pages.Count; i++)
            {
                count += _coverage[i];
                owner ^= _ownerChanges[i];
                if (count == 0) _issues.Add(new(PdfDPartIssueCode.UncoveredPage, PageIndex: i));
                else if (count > 1) _issues.Add(new(PdfDPartIssueCode.OverlappingRanges, PageIndex: i));
                else
                {
                    var node = _owners[owner - 1];
                    if (!PointsTo(_pages[i].Get("DPart"), node.Dictionary!))
                        _issues.Add(new(PdfDPartIssueCode.InvalidPageAssociation, node, i));
                }
            }
        }
    }
}
