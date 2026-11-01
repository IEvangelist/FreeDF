using System.Buffers.Binary;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using Xunit;

namespace FreeDF.Tests;

/// <summary>
/// CMM completeness: perceptual reference black (ICC.1:2022 6.3.4.3), LUT black-point compensation, B2A destination
/// conversion and named-colour profiles. Synthetic profiles are built in-test; reference values come from Pillow 12.1.1 /
/// Little CMS 2.17 (see the per-test comments for flags).
/// </summary>
public class IccCmmTests
{
    static void U32(List<byte> b, uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(s, v); b.AddRange(s.ToArray()); }
    static void U16(List<byte> b, int v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(s, (ushort)v); b.AddRange(s.ToArray()); }
    static void S15(List<byte> b, double v) => U32(b, (uint)(int)Math.Round(v * 65536));
    static uint Sig(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
    static void Ascii(List<byte> b, string s, int len) { var bytes = Encoding.ASCII.GetBytes(s); for (var i = 0; i < len; i++) b.Add(i < bytes.Length ? bytes[i] : (byte)0); }

    static byte[] XyzTag(double x, double y, double z) { var b = new List<byte>(); U32(b, Sig("XYZ ")); U32(b, 0); S15(b, x); S15(b, y); S15(b, z); return [.. b]; }

    static byte[] Profile(string space, string pcs, string cls, int major, params (string Tag, byte[] Data)[] tags)
    {
        var body = new List<byte>();
        var table = new List<byte>();
        U32(table, (uint)tags.Length);
        var off = 128 + 4 + tags.Length * 12;
        foreach (var (t, d) in tags)
        {
            while ((off + body.Count) % 4 != 0) body.Add(0);
            U32(table, Sig(t)); U32(table, (uint)(off + body.Count)); U32(table, (uint)d.Length);
            body.AddRange(d);
        }
        var h = new byte[128];
        Encoding.ASCII.GetBytes(cls).CopyTo(h, 12);
        Encoding.ASCII.GetBytes(space).CopyTo(h, 16);
        Encoding.ASCII.GetBytes(pcs).CopyTo(h, 20);
        Encoding.ASCII.GetBytes("acsp").CopyTo(h, 36);
        h[8] = (byte)major; h[9] = 0x40;
        var all = h.Concat(table).Concat(body).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(all, (uint)all.Length);
        return all;
    }

    static int[] Lab16(double l, double a, double b) =>
        [(int)Math.Round(l / 100 * 65280), (int)Math.Round((a + 128) * 256), (int)Math.Round((b + 128) * 256)];

    // 2-point-per-axis mft2 (legacy 16-bit PCSLAB), identity curves; the last input varies fastest.
    static byte[] Mft2(int nIn, int nOut, IEnumerable<int[]> corners)
    {
        var b = new List<byte>();
        U32(b, Sig("mft2")); U32(b, 0);
        b.AddRange([(byte)nIn, (byte)nOut, 2, 0]);
        foreach (var v in new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) S15(b, v);
        U16(b, 2); U16(b, 2);
        for (var i = 0; i < nIn; i++) { U16(b, 0); U16(b, 65535); }
        foreach (var c in corners) foreach (var v in c) U16(b, v);
        for (var i = 0; i < nOut; i++) { U16(b, 0); U16(b, 65535); }
        return [.. b];
    }

    static IEnumerable<int[]> RgbCorners()
    {
        for (var r = 0; r < 2; r++) for (var g = 0; g < 2; g++) for (var b = 0; b < 2; b++)
                    yield return Lab16(8 + 92 * (0.3 * r + 0.59 * g + 0.11 * b), 25 * (r - g), 25 * (b - (r + g) / 2.0));
    }

    static IEnumerable<int[]> CmykCorners()
    {
        for (var c = 0; c < 2; c++) for (var m = 0; m < 2; m++) for (var y = 0; y < 2; y++) for (var k = 0; k < 2; k++)
                        yield return Lab16(95 - 80 * k - 3 * (c + m + y), 30 * (m - c), 30 * (y - (c + m) / 2.0));
    }

    static IEnumerable<int[]> B2aCorners()
    {
        for (var l = 0; l < 2; l++) for (var a = 0; a < 2; a++) for (var b = 0; b < 2; b++)
                {
                    var v = 1 - l;
                    var cmy = (int)Math.Round(0.3 * v * 65535);
                    yield return [cmy, cmy, cmy, (int)Math.Round(v * 65535.0)];
                }
    }

    static readonly byte[] D50 = XyzTag(0.9642, 1, 0.8249);

    static byte[] RgbProfile(int major)
    {
        var t = Mft2(3, 3, RgbCorners());
        return Profile("RGB ", "Lab ", "scnr", major, ("A2B0", t), ("A2B1", t), ("A2B2", t), ("wtpt", D50));
    }

    static byte[] CmykProfile(int major)
    {
        var t = Mft2(4, 3, CmykCorners()); var bt = Mft2(3, 4, B2aCorners());
        return Profile("CMYK", "Lab ", "prtr", major, ("A2B0", t), ("A2B1", t), ("A2B2", t),
            ("B2A0", bt), ("B2A1", bt), ("B2A2", bt), ("wtpt", D50));
    }

    static readonly (int, int, int)[] RgbIn = [(0, 0, 0), (255, 255, 255), (128, 128, 128), (64, 128, 192), (255, 0, 0), (0, 77, 230)];
    static readonly (int, int, int, int)[] CmykIn = [(0, 0, 0, 0), (0, 0, 0, 255), (255, 255, 255, 255), (51, 102, 153, 26), (128, 0, 0, 77), (0, 0, 0, 128), (255, 0, 0, 0)];

    static int[] Srgb8(IccProfile p, double[] v, RenderingIntent intent, BlackPointCompensation bpc)
    {
        var c = p.ToSrgb(v, intent, bpc);
        return [(int)Math.Round(c.R * 255), (int)Math.Round(c.G * 255), (int)Math.Round(c.B * 255)];
    }

    static Dictionary<string, List<(double[] Input, int[] Output)>> Reference()
    {
        var result = new Dictionary<string, List<(double[], int[])>>();
        List<(double[], int[])>? current = null;
        foreach (var raw in IccLcmsReference.Text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (!char.IsWhiteSpace(line[0])) { current = []; result[line.Trim()] = current; continue; }
            var halves = line.Split("->");
            static int[] Ints(string s) => s.Replace("(", "").Replace(")", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
            current!.Add((Ints(halves[0]).Select(v => v / 255.0).ToArray(), Ints(halves[1])));
        }
        return result;
    }

    public static IEnumerable<object[]> MatchingCases() =>
        Reference().Keys.Select(k => new object[] { k });

    static RenderingIntent ParseIntent(string s) => Enum.Parse<RenderingIntent>(s);

    // 8-bit results of the C# CMM must be within two levels of Pillow 12.1.1 / Little CMS 2.17 (NOOPTIMIZE, explicit BPC flag).
    [Theory]
    [MemberData(nameof(MatchingCases))]
    public void LittleCms_Reference_MatchesWithinTwoLevels(string key)
    {
        var parts = key.Split(' ');
        var bpc = key.EndsWith("bpc=1") ? BlackPointCompensation.On : BlackPointCompensation.Off;
        var rows = Reference()[key];
        if (parts[2] == "->")
        {
            var major = int.Parse(parts[1][1..]);
            var intent = ParseIntent(parts[5]);
            var source = IccProfile.TryParse(RgbProfile(major), true)!;
            var destination = IccProfile.TryParse(CmykProfile(major), true)!;
            foreach (var (input, expected) in rows)
            {
                var output = new double[4];
                Assert.True(IccProfile.TryConvert(source, input, destination, intent, bpc, output));
                for (var i = 0; i < 4; i++)
                    Assert.InRange((int)Math.Round(output[i] * 255), expected[i] - 2, expected[i] + 2);
            }
            return;
        }
        var profile = IccProfile.TryParse(parts[0] == "RGB" ? RgbProfile(int.Parse(parts[1][1..])) : CmykProfile(int.Parse(parts[1][1..])))!;
        foreach (var (input, expected) in rows)
        {
            var actual = Srgb8(profile, input, ParseIntent(parts[2]), bpc);
            for (var i = 0; i < 3; i++) Assert.InRange(actual[i], expected[i] - 2, expected[i] + 2);
        }
    }

    [Fact]
    public void CorruptB2A_DisablesOnlyDestinationUse_NotTheA2BRenderingPath()
    {
        var a2b = Mft2(4, 3, CmykCorners());
        var good = IccProfile.TryParse(CmykProfile(2))!;
        double[] input = [0.2, 0.4, 0.6, 0.1];
        // Truncated mft2 (declares a CLUT larger than the tag) and a mAB/mBA whose curve offsets overflow the tag.
        var truncated = Mft2(3, 4, B2aCorners()).Take(60).ToArray();
        var overflow = Mab(true, 3, 4, B2aCornersV4());
        BinaryPrimitives.WriteUInt32BigEndian(overflow.AsSpan(12), 0x7FFFFFF0);
        foreach (var bad in new[] { truncated, overflow })
        {
            var data = Profile("CMYK", "Lab ", "prtr", 2, ("A2B0", a2b), ("A2B1", a2b), ("A2B2", a2b), ("B2A0", bad), ("wtpt", D50));
            foreach (var destination in new[] { false, true })
            {
                var profile = IccProfile.TryParse(data, destination);
                Assert.NotNull(profile);
                Assert.Equal(Srgb8(good, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off),
                    Srgb8(profile, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off));
                // BPC falls back to the darkest-colorant estimate (no round trip) and still converts.
                Assert.True(profile.SupportsBlackPointCompensation(RenderingIntent.RelativeColorimetric));
                Assert.False(IccProfile.TryConvert(IccProfile.TryParse(RgbProfile(2), true)!, [0.5, 0.5, 0.5], profile,
                    RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off, new double[4]));
            }
        }
    }

    [Theory]
    [InlineData("RGB v2 Perceptual bpc=0", 0)]
    [InlineData("RGB v2 Saturation bpc=0", 0)]
    [InlineData("RGB v4 Saturation bpc=0", 16)]
    public void PerceptualAndSaturation_AreImplicitlyBlackCompensated_LikeLittleCms(string key, int lcmsBlack)
    {
        var parts = key.Split(' ');
        var profile = IccProfile.TryParse(RgbProfile(int.Parse(parts[1][1..])))!;
        var intent = ParseIntent(parts[2]);
        foreach (var bpc in new[] { BlackPointCompensation.Off, BlackPointCompensation.Default, BlackPointCompensation.On })
            Assert.InRange(Srgb8(profile, [0, 0, 0], intent, bpc)[0], lcmsBlack - 1, lcmsBlack + 1);
        // Relative colorimetric still honours the flag.
        Assert.InRange(Srgb8(profile, [0, 0, 0], RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off)[0], 23, 25);
    }

    [Fact]
    public void V4PerceptualReferenceBlack_MapsToDestinationBlack_WhitePreserved_PreV4Unchanged()
    {
        // ICC.1:2022 6.3.4.3 / Table 16: v4 perceptual PCS black Y = 0.003479 maps to sRGB black with white fixed.
        const double yb = 0.003479;
        double Expected(double y)
        {
            var lin = (y - yb) / (1 - yb);
            return Math.Round(255 * (lin <= 0.0031308 ? 12.92 * lin : 1.055 * Math.Pow(lin, 1 / 2.4) - 0.055));
        }
        static byte[] Constant(int version, int lightness)
        {
            var table = Mft2(3, 3, Enumerable.Repeat(Lab16(lightness, 0, 0), 8));
            return Profile("RGB ", "Lab ", version >= 4 ? "scnr" : "mntr", version, ("A2B0", table), ("A2B1", table), ("A2B2", table), ("wtpt", D50));
        }
        static double Y(double l) { var f = (l + 16) / 116; return f > 6.0 / 29 ? f * f * f : 3 * (6.0 / 29) * (6.0 / 29) * (f - 4.0 / 29); }
        var v4 = IccProfile.TryParse(Constant(4, 20))!;
        Assert.Equal(Expected(Y(20)), Srgb8(v4, [0.3, 0.4, 0.5], RenderingIntent.Perceptual, BlackPointCompensation.Off)[0], 1.0);
        Assert.Equal(Srgb8(v4, [0.3, 0.4, 0.5], RenderingIntent.Perceptual, BlackPointCompensation.Off),
            Srgb8(v4, [0.3, 0.4, 0.5], RenderingIntent.Perceptual, BlackPointCompensation.On));
        // Relative and saturation PCS are not perceptual-reference-black encoded.
        Assert.Equal(Expected(Y(20) + yb * (1 - Y(20))), Srgb8(v4, [0.3, 0.4, 0.5], RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off)[0], 1.0);
        var v4White = IccProfile.TryParse(Constant(4, 100))!;
        Assert.Equal(255, Srgb8(v4White, [0.3, 0.4, 0.5], RenderingIntent.Perceptual, BlackPointCompensation.Off)[0]);
        // Pre-v4 perceptual: Little CMS compensates the estimated black (L* 20 payload) to sRGB black.
        var v2 = IccProfile.TryParse(Constant(2, 20))!;
        Assert.Equal(0, Srgb8(v2, [0.3, 0.4, 0.5], RenderingIntent.Perceptual, BlackPointCompensation.Off)[0]);
    }

    [Fact]
    public void LutBpc_IsCachedAndAllocationFree_AndDoesNotLeakIntoOtherConfigurations()
    {
        var profile = IccProfile.TryParse(CmykProfile(2))!;
        Assert.True(profile.SupportsBlackPointCompensation(RenderingIntent.RelativeColorimetric));
        Assert.False(profile.SupportsBlackPointCompensation(RenderingIntent.AbsoluteColorimetric));
        double[] input = [0.2, 0.4, 0.6, 0.1];
        var off = Srgb8(profile, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off);
        var on = Srgb8(profile, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On);
        Assert.NotEqual(off, on);
        for (var i = 0; i < 4200; i++)
        {
            profile.ToSrgb(input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On);
            profile.ToSrgb(input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off);
        }
        Assert.Equal(off, Srgb8(profile, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off));
        Assert.Equal(on, Srgb8(profile, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On));
        Assert.Equal(Srgb8(profile, input, RenderingIntent.AbsoluteColorimetric, BlackPointCompensation.Off),
            Srgb8(profile, input, RenderingIntent.AbsoluteColorimetric, BlackPointCompensation.On));
        profile.ToSrgb(input, RenderingIntent.Perceptual, BlackPointCompensation.Off);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            profile.ToSrgb(input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On);
            profile.ToSrgb(input, RenderingIntent.Perceptual, BlackPointCompensation.Off);
        }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TryConvert_IsAllocationFree_AndRejectsMissingTables()
    {
        var source = IccProfile.TryParse(RgbProfile(2), true)!;
        var destination = IccProfile.TryParse(CmykProfile(2), true)!;
        var output = new double[4];
        double[] input = [0.25, 0.5, 0.75];
        for (var i = 0; i < 10; i++) IccProfile.TryConvert(source, input, destination, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            IccProfile.TryConvert(source, input, destination, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On, output);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        // Destination parsed without B2A tables, and too-short buffers, cannot be converted to.
        var noTables = IccProfile.TryParse(CmykProfile(2))!;
        Assert.False(IccProfile.TryConvert(source, input, noTables, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off, output));
        Assert.False(IccProfile.TryConvert(source, input, destination, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off, new double[3]));
    }

    static byte[] Gamma(double g) { var b = new List<byte>(); U32(b, Sig("curv")); U32(b, 0); U32(b, 1); U16(b, (int)Math.Round(g * 256)); return [.. b]; }

    static byte[] MatrixProfile(double gamma) => Profile("RGB ", "XYZ ", "mntr", 2,
        ("rXYZ", XyzTag(0.4360747, 0.2225045, 0.0139322)), ("gXYZ", XyzTag(0.3850649, 0.7168786, 0.0971045)), ("bXYZ", XyzTag(0.1430804, 0.0606169, 0.7141733)),
        ("rTRC", Gamma(gamma)), ("gTRC", Gamma(gamma)), ("bTRC", Gamma(gamma)), ("wtpt", D50));

    [Fact]
    public void TryConvert_MatrixProfiles_InvertTrc_AndRoundTrip()
    {
        var source = IccProfile.TryParse(MatrixProfile(2.2), true)!;
        var linear = IccProfile.TryParse(MatrixProfile(1.0), true)!;
        var output = new double[3];
        Assert.True(IccProfile.TryConvert(source, [0.5, 0.25, 0.75], linear, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off, output));
        Near(Math.Pow(0.5, 2.2), output[0]); Near(Math.Pow(0.25, 2.2), output[1]); Near(Math.Pow(0.75, 2.2), output[2]);
        var back = new double[3];
        Assert.True(IccProfile.TryConvert(linear, output, source, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off, back));
        Near(0.5, back[0]); Near(0.25, back[1]); Near(0.75, back[2]);
    }

    static void Near(double expected, double actual, double tolerance = 0.006) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    // lutAToBType / lutBToAType builders (ICC.1:2022 10.12 / 10.13): identity curves + one 2-point CLUT.
    static byte[] Mab(bool toDevice, int nIn, int nOut, IEnumerable<double[]> clutRows)
    {
        var b = new List<byte>();
        U32(b, Sig(toDevice ? "mBA " : "mAB ")); U32(b, 0);
        b.AddRange([(byte)nIn, (byte)nOut, 0, 0]);
        var bCount = 3; var aCount = toDevice ? nOut : nIn;
        var offB = 32; var offClut = offB + bCount * 12; var clutBytes = 20 + (1 << nIn) * nOut * 2; var offA = offClut + clutBytes;
        U32(b, (uint)offB); U32(b, 0); U32(b, 0); U32(b, (uint)offClut); U32(b, (uint)offA);
        for (var i = 0; i < bCount; i++) { U32(b, Sig("curv")); U32(b, 0); U32(b, 0); }
        for (var i = 0; i < nIn; i++) b.Add(2);
        for (var i = nIn; i < 16; i++) b.Add(0);
        b.AddRange([2, 0, 0, 0]);
        foreach (var row in clutRows) foreach (var v in row) U16(b, (int)Math.Round(v * 65535));
        for (var i = 0; i < aCount; i++) { U32(b, Sig("curv")); U32(b, 0); U32(b, 0); }
        return [.. b];
    }

    static double[] LabV4(double l, double a, double b) => [l / 100, (a + 128) / 255, (b + 128) / 255];

    [Fact]
    public void MabAndMba_ProduceTheSameConversionAsLut16_WithV4LabEncoding()
    {
        var a2b = Mab(false, 4, 3, CmykCornersV4());
        var b2a = Mab(true, 3, 4, B2aCornersV4());
        var mab = Profile("CMYK", "Lab ", "prtr", 4, ("A2B0", a2b), ("A2B1", a2b), ("A2B2", a2b), ("B2A0", b2a), ("B2A1", b2a), ("B2A2", b2a), ("wtpt", D50));
        var lut16 = IccProfile.TryParse(CmykProfile(4), true)!;
        var v4 = IccProfile.TryParse(mab, true)!;
        foreach (var (c, m, y, k) in CmykIn)
        {
            double[] input = [c / 255.0, m / 255.0, y / 255.0, k / 255.0];
            var expected = Srgb8(lut16, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off);
            var actual = Srgb8(v4, input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off);
            for (var i = 0; i < 3; i++) Assert.InRange(actual[i], expected[i] - 1, expected[i] + 1);
        }
        var source = IccProfile.TryParse(RgbProfile(4), true)!;
        foreach (var (r, g, b) in RgbIn)
        {
            double[] input = [r / 255.0, g / 255.0, b / 255.0];
            double[] expected = new double[4], actual = new double[4];
            Assert.True(IccProfile.TryConvert(source, input, lut16, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off, expected));
            Assert.True(IccProfile.TryConvert(source, input, v4, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off, actual));
            for (var i = 0; i < 4; i++) Near(expected[i], actual[i]);
        }
    }

    static IEnumerable<double[]> CmykCornersV4()
    {
        for (var c = 0; c < 2; c++) for (var m = 0; m < 2; m++) for (var y = 0; y < 2; y++) for (var k = 0; k < 2; k++)
                        yield return LabV4(95 - 80 * k - 3 * (c + m + y), 30 * (m - c), 30 * (y - (c + m) / 2.0));
    }

    static IEnumerable<double[]> B2aCornersV4()
    {
        for (var l = 0; l < 2; l++) for (var a = 0; a < 2; a++) for (var b = 0; b < 2; b++)
                {
                    var v = 1.0 - l;
                    yield return [0.3 * v, 0.3 * v, 0.3 * v, v];
                }
    }

    static byte[] NamedColorProfile(bool lab)
    {
        var t = new List<byte>();
        U32(t, Sig("ncl2")); U32(t, 0); U32(t, 0); U32(t, 2); U32(t, 2);
        Ascii(t, "PANTONE ", 32); Ascii(t, " C", 32);
        Ascii(t, "Warm Gray", 32);
        if (lab) { foreach (var v in Lab16(50, 0, 0)) U16(t, v); } else { U16(t, 5819); U16(t, 6036); U16(t, 4979); }
        U16(t, 0x1000); U16(t, 0x2000);
        Ascii(t, "White", 32);
        if (lab) { foreach (var v in Lab16(100, 0, 0)) U16(t, v); } else { U16(t, 31595); U16(t, 32768); U16(t, 27030); }
        U16(t, 0); U16(t, 0);
        return Profile("CMYK", lab ? "Lab " : "XYZ ", "nmcl", 2, ("ncl2", [.. t]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedColorProfile_ParsesNcl2_AndIccBasedRejectsNamedClass(bool lab)
    {
        var data = NamedColorProfile(lab);
        Assert.Null(IccProfile.TryParse(data));
        var named = IccNamedColorProfile.TryParse(data)!;
        Assert.NotNull(named);
        Assert.Equal(2, named.DeviceCoordinateCount);
        Assert.Equal("PANTONE ", named.Prefix);
        Assert.Equal(2, named.Colors.Count);
        Assert.True(named.TryGetColor("PANTONE Warm Gray C", out var gray));
        Assert.True(named.TryGetColor("Warm Gray", out var sameGray));
        Assert.Same(gray, sameGray);
        Assert.Equal([(ushort)0x1000, (ushort)0x2000], gray.DeviceCoordinates);
        Assert.Equal(119, Math.Round(gray.Srgb.R * 255), 1);
        Assert.True(named.TryGetColor("PANTONE White C", out var white));
        Assert.Equal(255, Math.Round(white.Srgb.G * 255), 1);
        Assert.False(named.TryGetColor("Missing", out _));
        Assert.Null(IccNamedColorProfile.TryParse(RgbProfile(4)));
        Assert.Null(IccNamedColorProfile.TryParse(data[..140]));
    }
}
