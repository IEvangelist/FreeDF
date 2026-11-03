using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>
/// Bounded cache of decoded image XObjects for renders that draw the same document repeatedly (band renders of one page re-draw every image).
/// Entries are keyed by stream identity plus the inputs that affect decoding, so a hit returns exactly what a fresh decode would.
/// Not thread-safe; use one per render thread/worker.
/// </summary>
public sealed class ImageDecodeCache(long budgetBytes = 48L * 1024 * 1024)
{
    public readonly record struct Key(PdfStream Stream, PdfDictionary Resources, RenderingIntent Intent, BlackPointCompensation Compensation);

    readonly Dictionary<Key, (ImageData? Image, bool IsMask, string? Warning)> _entries = new(new KeyComparer());
    readonly Queue<Key> _order = new();
    long _bytes;

    public bool TryGet(Key key, out ImageData? image, out bool isMask, out string? warning)
    {
        if (_entries.TryGetValue(key, out var hit)) { (image, isMask, warning) = hit; return true; }
        image = null; isMask = false; warning = null;
        return false;
    }

    public void Add(Key key, ImageData? image, bool isMask, string? warning)
    {
        var size = image?.DecodedByteLength ?? 0;
        if (size > budgetBytes || _entries.ContainsKey(key)) return;
        while (_bytes + size > budgetBytes && _order.Count > 0)
        {
            var oldest = _order.Dequeue();
            if (_entries.Remove(oldest, out var removed)) _bytes -= removed.Image?.DecodedByteLength ?? 0;
        }
        _entries[key] = (image, isMask, warning);
        _order.Enqueue(key);
        _bytes += size;
    }

    public void Clear() { _entries.Clear(); _order.Clear(); _bytes = 0; }

    sealed class KeyComparer : IEqualityComparer<Key>
    {
        public bool Equals(Key a, Key b) => ReferenceEquals(a.Stream, b.Stream) && ReferenceEquals(a.Resources, b.Resources) && a.Intent == b.Intent && a.Compensation == b.Compensation;
        public int GetHashCode(Key k) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Stream), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Resources), k.Intent, k.Compensation);
    }
}
