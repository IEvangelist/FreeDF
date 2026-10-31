using PortableDocument.Content;
using PortableDocument.Core;
using Xunit;

namespace AdoBye.Tests;

/// <summary>
/// ICC.1:2010 D2Bx multiProcessElements pipelines. The fixtures are hand-assembled by tools/make_mpet_fixtures.py (self-derived,
/// not vendor profiles); expected values come from Little CMS 2.17 through Pillow 12.1.1 ImageCms (NOOPTIMIZE, relative
/// colorimetric, no BPC) and are matched within two 8-bit levels.
/// </summary>
public class IccMultiProcessTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "icc", name));

    static int[] Srgb8(IccProfile p, double[] v)
    {
        var c = p.ToSrgb(v, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off);
        return [(int)Math.Round(c.R * 255), (int)Math.Round(c.G * 255), (int)Math.Round(c.B * 255)];
    }

    static void Check(IccProfile p, (int[] In, int[] Out)[] cases)
    {
        foreach (var (i, o) in cases)
        {
            var got = Srgb8(p, i.Select(v => v / 255.0).ToArray());
            for (var k = 0; k < 3; k++) Assert.InRange(got[k], o[k] - 2, o[k] + 2);
        }
    }

    [Fact]
    public void RgbCurveSetAndMatrixMatchesLittleCms()
    {
        var p = IccProfile.TryParse(Fixture("mpet-rgb-d2b.icc"));
        Assert.NotNull(p);
        Check(p, [
            ([0, 0, 0], [0, 0, 0]), ([255, 255, 255], [255, 255, 255]), ([128, 128, 128], [128, 128, 128]),
            ([64, 128, 192], [64, 128, 192]), ([255, 0, 0], [255, 0, 0]), ([10, 200, 40], [10, 200, 40]), ([12, 12, 12], [12, 12, 12])]);
    }

    [Fact]
    public void CmykSampledSegmentAndFloatClutMatchLittleCms()
    {
        var p = IccProfile.TryParse(Fixture("mpet-cmyk-d2b.icc"));
        Assert.NotNull(p);
        Check(p, [
            ([0, 0, 0, 0], [255, 255, 255]), ([0, 0, 0, 255], [94, 94, 94]), ([255, 0, 0, 0], [0, 238, 255]),
            ([0, 255, 0, 0], [255, 158, 211]), ([0, 0, 255, 0], [233, 206, 89]), ([51, 102, 153, 26], [221, 178, 131]),
            ([128, 64, 32, 200], [72, 117, 121])]);
    }

    [Fact]
    public void CorruptD2BWithoutA2BFallbackIsRejected()
    {
        var good = Fixture("mpet-rgb-d2b.icc");
        // Corrupt the mpet element count in every D2Bx tag: the profile has no other tables, so parsing must fail safely.
        var bad = (byte[])good.Clone();
        var tagCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bad.AsSpan(128));
        for (var t = 0; t < tagCount; t++)
        {
            var e = 132 + 12 * t;
            var off = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bad.AsSpan(e + 4));
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bad.AsSpan(off)) == 0x6D706574)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(off + 12), 0xFFFFFFFF);
        }
        Assert.Null(IccProfile.TryParse(bad));
    }

    [Fact]
    public void TruncatedAndHostileMpetNeverThrows()
    {
        var good = Fixture("mpet-cmyk-d2b.icc");
        var rng = new Random(1234);
        for (var n = 0; n < 300; n++)
        {
            var b = (byte[])good.Clone();
            for (var k = 0; k < 1 + n % 6; k++) b[rng.Next(132, b.Length)] = (byte)rng.Next(256);
            var p = IccProfile.TryParse(b);
            if (p is null) continue;
            var c = p.ToSrgb([0.3, 0.6, 0.1, 0.2], RenderingIntent.RelativeColorimetric);
            Assert.InRange(c.R, 0f, 1f);
        }
        for (var len = 130; len < good.Length; len += 97) Assert.Null(Record.Exception(() => IccProfile.TryParse(good[..len])));
    }
}
