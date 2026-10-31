using System.Buffers.Binary;
using System.IO.Compression;

namespace PortableDocument.Document;

/// <summary>A PRC (ISO 14739-1 section 7.2) file-structure section: its role, byte range and, when it is a valid zlib stream, its bounded inflated length.</summary>
public sealed record PrcSection(string Name, long Offset, long Length, bool ZlibHeader, long? InflatedLength, bool InflateTruncated);

/// <summary>One PRC file structure: versions, UUIDs, uncompressed-file sizes and the five section ranges (globals, tree, tessellation, geometry, extra geometry).</summary>
public sealed record PrcFileStructure(
    long Offset,
    uint MinimalVersionForRead,
    uint AuthoringVersion,
    string FileStructureUuid,
    string ApplicationUuid,
    IReadOnlyList<long> UncompressedFileSizes,
    IReadOnlyList<PrcSection> Sections);

/// <summary>
/// Metadata-only PRC model: file header and per-structure section table. Section payloads (the product-occurrence tree, tessellation and
/// B-rep geometry bitstreams) are neither decoded nor rendered. Truncated means a count/size budget was reached; Malformed that
/// the layout was inconsistent (everything read before the inconsistency is returned).
/// </summary>
public sealed record PrcModel(
    uint MinimalVersionForRead,
    uint AuthoringVersion,
    string FileStructureUuid,
    string ApplicationUuid,
    IReadOnlyList<PrcFileStructure> FileStructures,
    long ModelFileOffset,
    bool Truncated,
    bool Malformed);

public static class PrcReader
{
    public const int MaxFileStructures = 64;
    public const int MaxUncompressedFiles = 1024;
    public const long MaxInflatedBytes = 64L * 1024 * 1024;
    public const long MaxTotalInflatedBytes = 256L * 1024 * 1024;
    public const long MaxTotalInputBytes = 64L * 1024 * 1024;

    static readonly string[] SectionNames = ["Globals", "Tree", "Tessellation", "Geometry", "ExtraGeometry"];

    public static bool LooksLikePrc(ReadOnlySpan<byte> d) => d.Length >= 11 && d[0] == 'P' && d[1] == 'R' && d[2] == 'C';

    public static PrcModel Read(ReadOnlySpan<byte> data) => Read(data.ToArray());

    public static PrcModel Read(byte[] array)
    {
        ReadOnlySpan<byte> data = array;
        var budget = new Budget();
        var truncated = false;
        var malformed = false;
        var structures = new List<PrcFileStructure>();
        if (!LooksLikePrc(data) || data.Length < 3 + 8 + 32 + 4) return new PrcModel(0, 0, "", "", structures, 0, false, true);
        var o = 3;
        var minimal = U32(data, ref o);
        var authoring = U32(data, ref o);
        var fsUuid = Uuid(data, ref o);
        var appUuid = Uuid(data, ref o);
        var count = U32(data, ref o);
        long modelOffset = 0;
        var offsets = new List<long>();
        if (count > MaxFileStructures) { truncated = true; count = MaxFileStructures; }
        for (var i = 0; i < count; i++)
        {
            if (data.Length - o < 4) { malformed = true; break; }
            offsets.Add(U32(data, ref o));
        }
        if (!malformed)
        {
            if (data.Length - o < 4) malformed = true;
            else modelOffset = U32(data, ref o);
        }
        foreach (var start in offsets.Distinct())
        {
            if (start < 0 || start >= data.Length) { malformed = true; continue; }
            var fs = ReadStructure(array, start, budget, ref truncated);
            if (fs is null) { malformed = true; continue; }
            structures.Add(fs);
        }
        return new PrcModel(minimal, authoring, fsUuid, appUuid, structures, modelOffset, truncated, malformed);
    }

    sealed class Budget
    {
        public long Inflated;
        public long Input;
        public readonly Dictionary<(long, long), (long? Length, bool Cut)> Cache = new();
    }

    static PrcFileStructure? ReadStructure(byte[] array, long start, Budget budget, ref bool truncated)
    {
        ReadOnlySpan<byte> data = array;
        var o = (int)start;
        if (data.Length - o < 3 + 8 + 32 + 4 || data[o] != 'P' || data[o + 1] != 'R' || data[o + 2] != 'C') return null;
        o += 3;
        var minimal = U32(data, ref o);
        var authoring = U32(data, ref o);
        var fsUuid = Uuid(data, ref o);
        var appUuid = Uuid(data, ref o);
        var files = U32(data, ref o);
        var sizes = new List<long>();
        if (files > MaxUncompressedFiles) { truncated = true; return null; }
        for (var i = 0; i < files; i++)
        {
            if (data.Length - o < 4) return null;
            var size = U32(data, ref o);
            if (size > data.Length - o) return null;
            sizes.Add(size);
            o += (int)size;
        }
        if (data.Length - o < 5 * 4) return null;
        var offs = new long[5];
        for (var i = 0; i < 5; i++) offs[i] = U32(data, ref o);
        var sections = new List<PrcSection>(5);
        for (var i = 0; i < 5; i++)
        {
            var begin = offs[i];
            var end = i + 1 < 5 ? offs[i + 1] : data.Length;
            if (begin < 0 || begin > data.Length || end < begin || end > data.Length) return null;
            var slice = data.Slice((int)begin, (int)(end - begin));
            var zlib = slice.Length >= 2 && (slice[0] & 0x0F) == 8 && ((slice[0] << 8) | slice[1]) % 31 == 0;
            long? inflated = null;
            var cut = false;
            if (zlib)
            {
                if (budget.Cache.TryGetValue((begin, end), out var hit)) { inflated = hit.Length; cut = hit.Cut; }
                else if (budget.Input + slice.Length > MaxTotalInputBytes) { cut = true; truncated = true; }
                else
                {
                    budget.Input += slice.Length;
                    inflated = Inflate(array, (int)begin, (int)(end - begin), MaxInflatedBytes, MaxTotalInflatedBytes - budget.Inflated, out cut, out var produced);
                    budget.Inflated += produced;
                    budget.Cache[(begin, end)] = (inflated, cut);
                    if (cut) truncated = true;
                }
            }
            sections.Add(new PrcSection(SectionNames[i], begin, end - begin, zlib, inflated, cut));
        }
        return new PrcFileStructure(start, minimal, authoring, fsUuid, appUuid, sizes, sections);
    }

    static long? Inflate(byte[] array, int offset, int count, long maxOne, long maxRemaining, out bool truncated, out long produced)
    {
        truncated = false;
        produced = 0;
        var limit = Math.Min(maxOne, maxRemaining);
        if (limit <= 0) { truncated = true; return null; }
        try
        {
            using var input = new MemoryStream(array, offset, count, writable: false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            var buffer = new byte[16384];
            int n;
            while ((n = z.Read(buffer, 0, buffer.Length)) > 0)
            {
                produced += n;
                if (produced > limit) { truncated = true; produced = limit; return limit; }
            }
            return produced;
        }
        catch (InvalidDataException) { return null; }
    }

    static uint U32(ReadOnlySpan<byte> d, ref int o)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(d[o..]);
        o += 4;
        return v;
    }

    static string Uuid(ReadOnlySpan<byte> d, ref int o)
    {
        var s = Convert.ToHexString(d.Slice(o, 16));
        o += 16;
        return s;
    }
}
