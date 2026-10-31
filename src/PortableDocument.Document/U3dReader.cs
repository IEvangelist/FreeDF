using System.Buffers.Binary;
using System.Text;

namespace PortableDocument.Document;

/// <summary>Inert U3D file header (ECMA-363 6th ed. section 9.2, File Header block type 0x00443355).</summary>
public sealed record U3dFileHeader(
    int MajorVersion,
    int MinorVersion,
    uint ProfileIdentifier,
    uint DeclarationSize,
    ulong FileSize,
    uint CharacterEncoding)
{
    public bool NoCompression => (ProfileIdentifier & 0x1) != 0;
    public bool Extensible => (ProfileIdentifier & 0x2) != 0;
}

/// <summary>One U3D block envelope (ECMA-363 section 9.3): type, payload size and metadata size. The payload is not retained.</summary>
public sealed record U3dBlock(uint Type, string TypeName, long Offset, uint DataSize, uint MetaDataSize, string? Name);

/// <summary>A node declaration (Group 0xFFFFFF21, Model 0xFFFFFF22, Light 0xFFFFFF23, View 0xFFFFFF24) with parent names.</summary>
public sealed record U3dNode(string Kind, string Name, IReadOnlyList<string> Parents, string? ResourceName);

/// <summary>Counts of block categories seen in the file, by type name.</summary>
public sealed record U3dSummary(
    int Blocks,
    int Nodes,
    int Models,
    int Lights,
    int Views,
    int Groups,
    int Textures,
    int Shaders,
    int Materials,
    int Meshes,
    int Continuations);

/// <summary>
/// Metadata-only U3D model: header, block table, node hierarchy and category counts. Geometry, textures and animation are never
/// decoded or rendered. <see cref="Truncated"/> reports that a block, node or recursion budget was reached, and
/// <see cref="Malformed"/> that the stream ended or went inconsistent; whatever was read before is still returned.
/// </summary>
public sealed record U3dModel(
    U3dFileHeader? Header,
    IReadOnlyList<U3dBlock> Blocks,
    IReadOnlyList<U3dNode> Nodes,
    U3dSummary Summary,
    bool Truncated,
    bool Malformed);

/// <summary>Bounded, allocation-light U3D (ECMA-363) block walker.</summary>
public static class U3dReader
{
    public const int MaxBlocks = 65_536;
    public const int MaxNodes = 8_192;
    public const int MaxParents = 64;
    public const int MaxNameBytes = 1024;
    public const int MaxBytes = 256 * 1024 * 1024;

    const uint FileHeaderType = 0x00443355;

    static string TypeName(uint t) => t switch
    {
        FileHeaderType => "FileHeader",
        0xFFFFFF12 => "FileReference",
        0xFFFFFF14 => "ModifierChain",
        0xFFFFFF15 => "PriorityUpdate",
        0xFFFFFF16 => "NewObjectType",
        0xFFFFFF21 => "GroupNode",
        0xFFFFFF22 => "ModelNode",
        0xFFFFFF23 => "LightNode",
        0xFFFFFF24 => "ViewNode",
        0xFFFFFF31 => "CLODMeshGenerator",
        0xFFFFFF36 => "PointSet",
        0xFFFFFF37 => "LineSet",
        0xFFFFFF3B => "CLODBaseMeshContinuation",
        0xFFFFFF3C => "CLODProgressiveMeshContinuation",
        0xFFFFFF3E => "PointSetContinuation",
        0xFFFFFF3F => "LineSetContinuation",
        0xFFFFFF42 => "AnimationModifier",
        0xFFFFFF43 => "BoneWeightModifier",
        0xFFFFFF44 => "CLODModifier",
        0xFFFFFF45 => "ShadingModifier",
        0xFFFFFF46 => "SubdivisionModifier",
        0xFFFFFF47 => "GlyphModifier",
        0xFFFFFF51 => "LightResource",
        0xFFFFFF52 => "ViewResource",
        0xFFFFFF53 => "LitTextureShader",
        0xFFFFFF54 => "MaterialResource",
        0xFFFFFF55 => "TextureResource",
        0xFFFFFF56 => "MotionResource",
        0xFFFFFF5C => "TextureContinuation",
        _ => "Unknown",
    };

    public static bool LooksLikeU3d(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data) == FileHeaderType;

    public static U3dModel Read(ReadOnlySpan<byte> data)
    {
        var blocks = new List<U3dBlock>();
        var nodes = new List<U3dNode>();
        U3dFileHeader? header = null;
        var truncated = false;
        var malformed = false;
        int models = 0, lights = 0, views = 0, groups = 0, textures = 0, shaders = 0, materials = 0, meshes = 0, cont = 0;
        if (data.Length > MaxBytes) { data = data[..MaxBytes]; truncated = true; }
        long pos = 0;
        while (pos < data.Length)
        {
            if (blocks.Count >= MaxBlocks) { truncated = true; break; }
            if (data.Length - pos < 12) { malformed = true; break; }
            var span = data[(int)pos..];
            var type = BinaryPrimitives.ReadUInt32LittleEndian(span);
            var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            var metaSize = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
            var payloadStart = pos + 12;
            var padded = (long)dataSize + ((4 - (dataSize & 3)) & 3);
            var metaPadded = (long)metaSize + ((4 - (metaSize & 3)) & 3);
            if (payloadStart + padded + metaPadded > data.Length) { malformed = true; break; }
            var payload = data.Slice((int)payloadStart, (int)dataSize);
            string? name = null;
            switch (type)
            {
                case FileHeaderType:
                    header ??= ReadHeader(payload);
                    break;
                case 0xFFFFFF21 or 0xFFFFFF22 or 0xFFFFFF23 or 0xFFFFFF24:
                    if (nodes.Count >= MaxNodes) { truncated = true; break; }
                    var node = ReadNode(type, payload);
                    if (node is null) { malformed = true; break; }
                    name = node.Name;
                    nodes.Add(node);
                    break;
                case 0xFFFFFF51 or 0xFFFFFF52 or 0xFFFFFF53 or 0xFFFFFF54 or 0xFFFFFF55
                    or 0xFFFFFF31 or 0xFFFFFF36 or 0xFFFFFF37 or 0xFFFFFF16 or 0xFFFFFF14:
                    name = ReadLeadingString(payload);
                    break;
            }
            switch (type)
            {
                case 0xFFFFFF22: models++; break;
                case 0xFFFFFF23: lights++; break;
                case 0xFFFFFF24: views++; break;
                case 0xFFFFFF21: groups++; break;
                case 0xFFFFFF55: textures++; break;
                case 0xFFFFFF53 or 0xFFFFFF45: shaders++; break;
                case 0xFFFFFF54: materials++; break;
                case 0xFFFFFF31 or 0xFFFFFF36 or 0xFFFFFF37: meshes++; break;
                case 0xFFFFFF3B or 0xFFFFFF3C or 0xFFFFFF3E or 0xFFFFFF3F or 0xFFFFFF5C: cont++; break;
            }
            blocks.Add(new U3dBlock(type, TypeName(type), pos, dataSize, metaSize, name));
            pos = payloadStart + padded + metaPadded;
            if (malformed) break;
        }
        if (blocks.Count == 0 || blocks[0].Type != FileHeaderType) malformed = true;
        return new U3dModel(header, blocks, nodes,
            new U3dSummary(blocks.Count, nodes.Count, models, lights, views, groups, textures, shaders, materials, meshes, cont),
            truncated, malformed);
    }

    static U3dFileHeader? ReadHeader(ReadOnlySpan<byte> p)
    {
        if (p.Length < 24) return null;
        return new U3dFileHeader(
            BinaryPrimitives.ReadInt16LittleEndian(p), BinaryPrimitives.ReadInt16LittleEndian(p[2..]),
            BinaryPrimitives.ReadUInt32LittleEndian(p[4..]), BinaryPrimitives.ReadUInt32LittleEndian(p[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(p[12..]), p.Length >= 24 ? BinaryPrimitives.ReadUInt32LittleEndian(p[20..]) : 0);
    }

    // U3D strings are a U16 byte count followed by UTF-8 bytes (ECMA-363 section 9.2.1).
    static bool TryString(ReadOnlySpan<byte> p, ref int o, out string value)
    {
        value = "";
        if (p.Length - o < 2) return false;
        var len = BinaryPrimitives.ReadUInt16LittleEndian(p[o..]);
        if (len > MaxNameBytes || p.Length - o - 2 < len) return false;
        value = Encoding.UTF8.GetString(p.Slice(o + 2, len));
        o += 2 + len;
        return true;
    }

    static string? ReadLeadingString(ReadOnlySpan<byte> p)
    {
        var o = 0;
        return TryString(p, ref o, out var s) ? s : null;
    }

    static U3dNode? ReadNode(uint type, ReadOnlySpan<byte> p)
    {
        var o = 0;
        if (!TryString(p, ref o, out var name)) return null;
        if (p.Length - o < 4) return null;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(p[o..]);
        o += 4;
        if (count > MaxParents) return null;
        var parents = new List<string>((int)count);
        for (var i = 0; i < count; i++)
        {
            if (!TryString(p, ref o, out var parent)) return null;
            parents.Add(parent);
            o += 64; // 4x4 float transform
            if (o > p.Length) return null;
        }
        string? resource = null;
        if (type != 0xFFFFFF21 && TryString(p, ref o, out var r)) resource = r;
        var kind = type switch { 0xFFFFFF21 => "Group", 0xFFFFFF22 => "Model", 0xFFFFFF23 => "Light", _ => "View" };
        return new U3dNode(kind, name, parents, resource);
    }
}
