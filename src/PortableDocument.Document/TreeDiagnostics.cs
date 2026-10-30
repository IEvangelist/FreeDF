using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>One name-tree or number-tree problem. <paramref name="Kind"/> is a stable code such as DuplicateKey, KeyOutOfOrder, LimitsMismatch.</summary>
public sealed record PdfTreeIssue(string Kind, string Key, string Detail);

/// <summary>Read-only audit of a name tree (ISO 32000-2 7.9.6, Table 36) or number tree (7.9.7, Table 37). Parsing and lookup are unaffected;
/// the audit is bounded by depth, a total node/key budget and a visited set, and reports <see cref="Truncated"/> when a bound is hit.</summary>
public sealed record PdfTreeDiagnostics(int KeyCount, IReadOnlyList<PdfTreeIssue> Issues, bool Truncated)
{
    public const int MaxDepth = 64;
    public const int MaxWork = 200_000;
    public const int MaxIssues = 1000;

    public bool IsValid => Issues.Count == 0 && !Truncated;

    public static PdfTreeDiagnostics Audit(PdfFile file, PdfDictionary root, bool numberTree)
    {
        var audit = new Auditor(file, numberTree);
        audit.Visit(root, 0, true);
        return new PdfTreeDiagnostics(audit.Keys, audit.Issues, audit.Truncated);
    }

    readonly struct TreeKey
    {
        readonly byte[]? _bytes;
        readonly long _number;
        public TreeKey(byte[] bytes) { _bytes = bytes; _number = 0; }
        public TreeKey(long number) { _bytes = null; _number = number; }
        public int CompareTo(TreeKey other) => _bytes is null
            ? _number.CompareTo(other._number)
            : _bytes.AsSpan().SequenceCompareTo(other._bytes);
        public override string ToString() => _bytes is null ? _number.ToString() : System.Text.Encoding.Latin1.GetString(_bytes);
    }

    sealed class Auditor(PdfFile file, bool numberTree)
    {
        readonly HashSet<PdfDictionary> _seen = new(ReferenceEqualityComparer.Instance);
        TreeKey? _previous;
        int _work;
        public int Keys;
        public bool Truncated;
        public readonly List<PdfTreeIssue> Issues = [];

        void Report(string kind, string key, string detail)
        {
            if (Issues.Count < MaxIssues) Issues.Add(new PdfTreeIssue(kind, key, detail));
            else Truncated = true;
        }

        TreeKey? Parse(PdfObject? value)
        {
            var resolved = file.Resolve(value);
            if (numberTree) return resolved is PdfInteger integer ? new TreeKey(integer.Value) : null;
            return resolved is PdfString text ? new TreeKey(text.Bytes) : null;
        }

        public (TreeKey Least, TreeKey Greatest)? Visit(PdfDictionary node, int depth, bool isRoot)
        {
            if (!_seen.Add(node)) { Report("Cycle", "", "node is reachable more than once"); return null; }
            if (depth > MaxDepth || ++_work > MaxWork) { Truncated = true; return null; }
            (TreeKey Least, TreeKey Greatest)? range = null;
            void Extend(TreeKey key)
            {
                range = range is null ? (key, key)
                    : (key.CompareTo(range.Value.Least) < 0 ? key : range.Value.Least,
                       key.CompareTo(range.Value.Greatest) > 0 ? key : range.Value.Greatest);
            }

            var leaf = file.Resolve(node.Get(numberTree ? "Nums" : "Names")) as PdfArray;
            var kids = file.Resolve(node.Get("Kids")) as PdfArray;
            if (leaf is not null && kids is not null) Report("BothLeafAndKids", "", "node has both a key array and /Kids");
            if (leaf is null && kids is null && !isRoot) Report("NodeWithoutKeys", "", "intermediate or leaf node has neither keys nor /Kids");

            if (leaf is not null)
            {
                if (leaf.Count % 2 != 0) Report("MalformedPair", "", "key/value array has an odd number of items");
                for (var i = 0; i + 1 < leaf.Count; i += 2)
                {
                    if (++_work > MaxWork) { Truncated = true; break; }
                    if (Parse(leaf[i]) is not { } key) { Report("MalformedPair", "", $"entry {i / 2} has a key of the wrong type"); continue; }
                    Keys++;
                    Extend(key);
                    if (_previous is { } previous)
                    {
                        var comparison = key.CompareTo(previous);
                        if (comparison == 0) Report("DuplicateKey", key.ToString(), "key occurs more than once");
                        else if (comparison < 0) { Report("KeyOutOfOrder", key.ToString(), $"key sorts before the preceding key {previous}"); continue; }
                    }
                    _previous = key;
                }
            }
            if (kids is not null)
                foreach (var kid in kids.Items)
                {
                    if (file.Resolve(kid) is not PdfDictionary child) { Report("MalformedKid", "", "/Kids entry is not a dictionary"); continue; }
                    if (Visit(child, depth + 1, false) is { } childRange)
                    {
                        Extend(childRange.Least);
                        Extend(childRange.Greatest);
                    }
                    if (Truncated && _work > MaxWork) break;
                }

            var limits = node.Get("Limits");
            if (isRoot)
            {
                if (limits is not null) Report("LimitsOnRoot", "", "root node must not have /Limits");
            }
            else if (limits is null) Report("LimitsMissing", "", "non-root node requires /Limits");
            else if (file.Resolve(limits) is not PdfArray { Count: 2 } bounds || Parse(bounds[0]) is not { } low || Parse(bounds[1]) is not { } high)
                Report("LimitsMalformed", "", "/Limits must be an array of two keys of the tree's key type");
            else if (low.CompareTo(high) > 0) Report("LimitsInverted", low.ToString(), $"/Limits least key sorts after greatest key {high}");
            else if (range is { } actual && (low.CompareTo(actual.Least) != 0 || high.CompareTo(actual.Greatest) != 0))
                Report("LimitsMismatch", low.ToString(), $"declared [{low}, {high}] but the node's keys span [{actual.Least}, {actual.Greatest}]");
            return range;
        }
    }
}
