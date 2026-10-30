using System.Buffers.Binary;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Content;

/// <summary>
/// Reader for ICC named-colour profiles (class 'nmcl', tag 'ncl2' = namedColor2Type, ICC.1:2022 10.17).
/// ISO 32000-2 8.6.5.5 does not allow named-colour profiles in ICCBased colour spaces, so this is a standalone helper
/// (for example for tooling or Separation/DeviceN colorant lookup by name) and is not used by the PDF renderer.
/// PCS values are relative colorimetric D50 XYZ or legacy 16-bit Lab; sRGB is computed with the profile-conversion adaptation.
/// </summary>
public sealed class IccNamedColorProfile
{
    public sealed record NamedColor(string Name, double[] PcsXyz, ushort[] DeviceCoordinates, RgbColor Srgb);

    readonly Dictionary<string, NamedColor> _byName = new(StringComparer.Ordinal);

    public string Prefix { get; }
    public string Suffix { get; }
    public int DeviceCoordinateCount { get; }
    public IReadOnlyList<NamedColor> Colors { get; }

    IccNamedColorProfile(string prefix, string suffix, int deviceCount, List<NamedColor> colors)
    {
        Prefix = prefix; Suffix = suffix; DeviceCoordinateCount = deviceCount; Colors = colors;
        foreach (var c in colors) _byName.TryAdd(c.Name, c);
    }

    /// <summary>Looks up a full name (prefix + root + suffix) or, if absent, a bare root name.</summary>
    public bool TryGetColor(string name, out NamedColor color)
    {
        if (_byName.TryGetValue(name, out color!)) return true;
        return _byName.TryGetValue(Prefix + name + Suffix, out color!);
    }

    public static IccNamedColorProfile? TryParse(byte[] data)
    {
        try { return Parse(data); } catch (Exception) { return null; }
    }

    static string Ascii(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    static IccNamedColorProfile? Parse(byte[] data)
    {
        ReadOnlySpan<byte> d = data;
        if (d.Length < 132 || BinaryPrimitives.ReadUInt32BigEndian(d[36..]) != 0x61637370) return null; // 'acsp'
        if (BinaryPrimitives.ReadUInt32BigEndian(d[12..]) != 0x6E6D636C) return null; // 'nmcl'
        var pcsSig = BinaryPrimitives.ReadUInt32BigEndian(d[20..]);
        if (pcsSig != 0x58595A20 && pcsSig != 0x4C616220) return null;
        var pcsLab = pcsSig == 0x4C616220;
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(d[128..]);
        if (count < 0 || count > 1000 || 132 + (long)count * 12 > d.Length) return null;
        for (var i = 0; i < count; i++)
        {
            var e = 132 + i * 12;
            if (BinaryPrimitives.ReadUInt32BigEndian(d[e..]) != 0x6E636C32) continue; // 'ncl2'
            var off = (long)BinaryPrimitives.ReadUInt32BigEndian(d[(e + 4)..]);
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(d[(e + 8)..]);
            if (off < 0 || size < 84 || off + size > d.Length) return null;
            var t = d.Slice((int)off, (int)size);
            if (BinaryPrimitives.ReadUInt32BigEndian(t) != 0x6E636C32) return null;
            var n = BinaryPrimitives.ReadUInt32BigEndian(t[12..]);
            var m = BinaryPrimitives.ReadUInt32BigEndian(t[16..]);
            if (m > 15) return null;
            var stride = 38L + 2 * m;
            if (84 + n * stride > size) return null;
            var prefix = Ascii(t.Slice(20, 32)); var suffix = Ascii(t.Slice(52, 32));
            var colors = new List<NamedColor>((int)n);
            for (var k = 0; k < n; k++)
            {
                var p = 84 + (int)(k * stride);
                var name = prefix + Ascii(t.Slice(p, 32)) + suffix;
                double X, Y, Z;
                var v0 = BinaryPrimitives.ReadUInt16BigEndian(t[(p + 32)..]);
                var v1 = BinaryPrimitives.ReadUInt16BigEndian(t[(p + 34)..]);
                var v2 = BinaryPrimitives.ReadUInt16BigEndian(t[(p + 36)..]);
                if (pcsLab)
                {
                    // Legacy 16-bit PCSLAB (ICC.1:2022 10.8 Tables 42/43).
                    double L = v0 * 100.0 / 65280.0, a = v1 / 256.0 - 128, b = v2 / 256.0 - 128;
                    var fy = (L + 16) / 116; var fx = fy + a / 500; var fz = fy - b / 200;
                    static double Inv(double u) => u > 6.0 / 29 ? u * u * u : 3 * (6.0 / 29) * (6.0 / 29) * (u - 4.0 / 29);
                    X = 0.9642 * Inv(fx); Y = Inv(fy); Z = 0.8249 * Inv(fz);
                }
                else { X = v0 / 32768.0; Y = v1 / 32768.0; Z = v2 / 32768.0; }
                var dev = new ushort[m];
                for (var j = 0; j < m; j++) dev[j] = BinaryPrimitives.ReadUInt16BigEndian(t[(p + 38 + 2 * j)..]);
                colors.Add(new NamedColor(name, [X, Y, Z], dev, IccProfile.XyzD50ToSrgb(X, Y, Z)));
            }
            return new IccNamedColorProfile(prefix, suffix, (int)m, colors);
        }
        return null;
    }
}
