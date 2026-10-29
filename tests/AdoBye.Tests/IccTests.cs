using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Buffers.Binary;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

public class IccTests
{
    // ---- minimal synthetic ICC profile builder (no third-party profiles are shipped) ----
    static void U32(List<byte> b, uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(s, v); b.AddRange(s.ToArray()); }
    static void U16(List<byte> b, int v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(s, (ushort)v); b.AddRange(s.ToArray()); }
    static void S15(List<byte> b, double v) => U32(b, (uint)(int)Math.Round(v * 65536));
    static uint Sig(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);

    static byte[] Xyz(double x, double y, double z) { var b = new List<byte>(); U32(b, Sig("XYZ ")); U32(b, 0); S15(b, x); S15(b, y); S15(b, z); return [.. b]; }
    static byte[] Gamma(double g) { var b = new List<byte>(); U32(b, Sig("curv")); U32(b, 0); U32(b, 1); U16(b, (int)Math.Round(g * 256)); return [.. b]; }
    static byte[] Identity() { var b = new List<byte>(); U32(b, Sig("curv")); U32(b, 0); U32(b, 0); return [.. b]; }

    static byte[] Profile(string space, string pcs, params (string Tag, byte[] Data)[] tags)
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
        Encoding.ASCII.GetBytes("mntr").CopyTo(h, 12);
        Encoding.ASCII.GetBytes(space).CopyTo(h, 16);
        Encoding.ASCII.GetBytes(pcs).CopyTo(h, 20);
        Encoding.ASCII.GetBytes("acsp").CopyTo(h, 36);
        h[8] = 2; h[9] = 0x40;
        var all = h.Concat(table).Concat(body).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(all, (uint)all.Length);
        return all;
    }

    static byte[] MatrixRgb(byte[] curve) => Profile("RGB ", "XYZ ",
        ("rXYZ", Xyz(0.4360747, 0.2225045, 0.0139322)), ("gXYZ", Xyz(0.3850649, 0.7168786, 0.0971045)), ("bXYZ", Xyz(0.1430804, 0.0606169, 0.7141733)),
        ("rTRC", curve), ("gTRC", curve), ("bTRC", curve), ("wtpt", Xyz(0.9642, 1.0, 0.8249)));

    // 2-point-grid CMYK mft2 -> Lab (legacy 16-bit): L = 100 * (1 - K), a = b = 0
    static byte[] CmykLut()
    {
        var b = new List<byte>();
        U32(b, Sig("mft2")); U32(b, 0);
        b.Add(4); b.Add(3); b.Add(2); b.Add(0);
        foreach (var v in new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) S15(b, v);
        U16(b, 2); U16(b, 2);
        for (var i = 0; i < 4; i++) { U16(b, 0); U16(b, 65535); }
        for (var n = 0; n < 16; n++)
        {
            var k = n & 1; // last input (K) varies fastest
            U16(b, k == 1 ? 0 : 0xFF00); U16(b, 0x8000); U16(b, 0x8000);
        }
        for (var i = 0; i < 3; i++) { U16(b, 0); U16(b, 65535); }
        return Profile("CMYK", "Lab ", ("A2B0", [.. b]));
    }

    static (int R, int G, int B) Round(RgbColor c) => ((int)Math.Round(c.R * 255), (int)Math.Round(c.G * 255), (int)Math.Round(c.B * 255));

    [Fact]
    public void MatrixRgb_IdentityCurves_AreLinearLight()
    {
        var p = IccProfile.TryParse(MatrixRgb(Identity()));
        Assert.NotNull(p);
        Assert.Equal(3, p!.Components);
        var (r, g, b) = Round(p.ToSrgb([0.5, 0.5, 0.5]));
        Assert.InRange(r, 185, 191); Assert.InRange(g, 185, 191); Assert.InRange(b, 185, 191); // 0.5 linear -> sRGB 0.735
        Assert.Equal((255, 255, 255), Round(p.ToSrgb([1, 1, 1])));
        Assert.Equal((0, 0, 0), Round(p.ToSrgb([0, 0, 0])));
    }

    [Fact]
    public void MatrixRgb_Gamma22_IsNearSrgb()
    {
        var p = IccProfile.TryParse(MatrixRgb(Gamma(2.2)))!;
        var (r, g, b) = Round(p.ToSrgb([0.5, 0.5, 0.5]));
        Assert.InRange(r, 124, 132); Assert.InRange(g, 124, 132); Assert.InRange(b, 124, 132);
        var red = Round(p.ToSrgb([1, 0, 0]));
        Assert.True(red.R > 250 && red.G < 5 && red.B < 5, red.ToString());
    }

    [Fact]
    public void Gray_KTRC()
    {
        var p = IccProfile.TryParse(Profile("GRAY", "XYZ ", ("kTRC", Gamma(1.0)), ("wtpt", Xyz(0.9642, 1.0, 0.8249))))!;
        Assert.Equal(1, p.Components);
        var (r, g, b) = Round(p.ToSrgb([0.5]));
        Assert.InRange(r, 185, 191); Assert.Equal(r, g); Assert.Equal(r, b);
    }

    [Fact]
    public void Cmyk_Lut_UsesProfileNotNaiveFormula()
    {
        var p = IccProfile.TryParse(CmykLut())!;
        Assert.Equal(4, p.Components);
        Assert.Equal((255, 255, 255), Round(p.ToSrgb([0, 0, 0, 0])));
        Assert.Equal((0, 0, 0), Round(p.ToSrgb([0, 0, 0, 1])));
        var (r, g, b) = Round(p.ToSrgb([0, 0, 0, 0.5])); // L=50 -> sRGB ~119
        Assert.InRange(r, 114, 124); Assert.InRange(g, 114, 124); Assert.InRange(b, 114, 124);
        // CMY channels are ignored by this profile, unlike the 1-c-k device formula.
        var cy = Round(p.ToSrgb([1, 0, 0, 0]));
        Assert.True(cy.R > 250, cy.ToString());
    }

    [Fact]
    public void Lut_GridBakeMatchesExactEvaluation()
    {
        var p = IccProfile.TryParse(CmykLut())!;
        var exact = Round(p.ToSrgb([0.2, 0.7, 0.1, 0.33]));
        for (var i = 0; i < 6000; i++) p.ToSrgb([0.1, 0.2, 0.3, (i % 100) / 100.0]);
        var baked = Round(p.ToSrgb([0.2, 0.7, 0.1, 0.33]));
        Assert.True(Math.Abs(exact.R - baked.R) <= 3 && Math.Abs(exact.G - baked.G) <= 3 && Math.Abs(exact.B - baked.B) <= 3, $"{exact} vs {baked}");
    }

    [Fact]
    public void GarbageProfile_IsRejected()
    {
        Assert.Null(IccProfile.TryParse([1, 2, 3]));
        var bad = MatrixRgb(Identity()); bad[36] = (byte)'x'; // breaks 'acsp'
        Assert.Null(IccProfile.TryParse(bad));
    }

    static RenderedPage RenderIcc(byte[] icc, int n, string content, string alternate = "")
    {
        var data = Encoding.Latin1.GetString(icc);
        var obj = $"5 0 obj\n<< /N {n} /Length {data.Length} {alternate} >>\nstream\n{data}\nendstream\nendobj\n";
        var pdf = TestPdf.Build(content, "/ColorSpace << /CS0 [/ICCBased 5 0 R] >>", obj, brokenXref: true);
        return DocumentRenderer.RenderPage(PdfDocument.Load(pdf).Pages[0], 72);
    }

    static (int R, int G, int B) Px(RenderedPage p, int x, int y) { var o = (y * p.Width + x) * 4; return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]); }

    [Fact]
    public void IccBasedFill_UsesProfile()
    {
        var p = RenderIcc(MatrixRgb(Identity()), 3, "/CS0 cs 0.5 0.5 0.5 sc 0 0 200 200 re f");
        var c = Px(p, 100, 100);
        Assert.InRange(c.R, 185, 191); // a device-RGB approximation would give 128
    }

    [Fact]
    public void IccBasedCmykFill_UsesLut()
    {
        var p = RenderIcc(CmykLut(), 4, "/CS0 cs 0 0 0 0.5 sc 0 0 200 200 re f");
        Assert.InRange(Px(p, 100, 100).R, 114, 124);
    }

    [Fact]
    public void IccBased_MismatchedOrCorruptProfile_FallsBackToDevice()
    {
        var p = RenderIcc(MatrixRgb(Identity()), 4, "/CS0 cs 0 0 0 1 sc 0 0 200 200 re f"); // /N says 4, profile is RGB
        Assert.True(Px(p, 100, 100).R < 80, Px(p, 100, 100).ToString());
        var q = RenderIcc([9, 9, 9, 9], 3, "/CS0 cs 1 0 0 sc 0 0 200 200 re f");
        Assert.Equal((255, 0, 0), Px(q, 100, 100));
    }

    static PdfObject Object(string value) => new PdfLexer(Encoding.ASCII.GetBytes(value)).ReadObject()!;

    static (PdfFile File, PdfArray Space) IccSpace(byte[] data, int n, string alternate = "")
    {
        var dict = (PdfDictionary)Object($"<< /N {n} {alternate} >>");
        return (PdfFile.Load(TestPdf.Build("")), new PdfArray([new PdfName("ICCBased"), new PdfStream(dict, data)]));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void IccBased_SupportedProfile_TakesPrecedenceOverAlternate(int version)
    {
        var profile = MatrixRgb(Identity());
        profile[8] = (byte)version;
        var page = RenderIcc(profile, 3, "/CS0 cs 0.5 0.5 0.5 sc 0 0 200 200 re f",
            "/Alternate /DeviceRGB");
        Assert.InRange(Px(page, 100, 100).R, 185, 191);
    }

    [Fact]
    public void IccBased_UnsupportedProfile_UsesAlternateWithoutSourceTransform()
    {
        var unsupported = Profile("Lab ", "Lab ");
        Assert.Null(IccProfile.TryParse(unsupported));
        var page = RenderIcc(unsupported, 3, "/CS0 cs 50 0 0 sc 0 0 200 200 re f",
            "/Range [0 100 -128 127 -128 127] /Alternate [/Lab << /WhitePoint [0.9642 1 0.8249] >>]");
        var color = Px(page, 100, 100);
        Assert.InRange(color.R, 118, 120);
        Assert.InRange(color.G, 118, 120);
        Assert.InRange(color.B, 118, 120);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IccBased_CorruptOrMismatchedProfile_UsesValidAlternate(bool mismatched)
    {
        var (file, space) = IccSpace(mismatched ? MatrixRgb(Identity()) : [9, 9, 9], 1,
            "/Alternate [/CalGray << /WhitePoint [0.9642 1 0.8249] >>]");
        var color = Round(PdfColorSpace.Parse(file, space).ToRgb([0.5]));
        Assert.InRange(color.R, 185, 191);
        Assert.InRange(color.G, 185, 191);
        Assert.InRange(color.B, 185, 191);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Filter /Unsupported")]
    public void IccBased_UnusableProfile_CanUseIndexedAlternate(string filter)
    {
        var (file, space) = IccSpace([9, 9, 9], 1,
            $"{filter} /Alternate [/Indexed /DeviceRGB 1 <FF00000000FF>]");
        if (filter.Length > 0) Assert.Throws<NotSupportedException>(() => file.DecodeStream((PdfStream)space[1]));
        var alternate = PdfColorSpace.Parse(file, space);
        Assert.Equal(1, alternate.Components);
        Assert.Equal((255, 0, 0), Round(alternate.ToRgb([0])));
        Assert.Equal((0, 0, 255), Round(alternate.ToRgb([1])));
    }

    [Fact]
    public void IccBased_SupportedProfile_RemainsCachedAcrossResourceContexts()
    {
        var (file, space) = IccSpace(MatrixRgb(Identity()), 3, "/Alternate /Alt");
        var resources = (PdfDictionary)Object("<< /ColorSpace << /Alt /DeviceRGB >> >>");
        var profile = PdfColorSpace.Parse(file, space, resources);
        Assert.InRange(Round(profile.ToRgb([0.5, 0.5, 0.5])).R, 185, 191);
        Assert.Same(profile, PdfColorSpace.Parse(file, space));
    }

    [Theory]
    [InlineData(1, "", "DeviceGray")]
    [InlineData(3, "", "DeviceRGB")]
    [InlineData(4, "", "DeviceCMYK")]
    [InlineData(1, "/Alternate /Unknown", "DeviceGray")]
    [InlineData(3, "/Alternate /Unknown", "DeviceRGB")]
    [InlineData(3, "/Alternate 17", "DeviceRGB")]
    [InlineData(3, "/Alternate null", "DeviceRGB")]
    [InlineData(3, "/Alternate []", "DeviceRGB")]
    [InlineData(1, "/Alternate [/Unknown]", "DeviceGray")]
    [InlineData(3, "/Alternate [/Indexed]", "DeviceRGB")]
    [InlineData(1, "/Alternate [/Separation /Spot /DeviceGray null]", "DeviceGray")]
    [InlineData(3, "/Alternate /Pattern", "DeviceRGB")]
    [InlineData(1, "/Alternate [/Pattern /DeviceGray]", "DeviceGray")]
    [InlineData(3, "/Alternate [/Pattern /DeviceRGB]", "DeviceRGB")]
    [InlineData(1, "/Alternate /DeviceRGB", "DeviceGray")]
    [InlineData(3, "/Alternate /DeviceGray", "DeviceRGB")]
    [InlineData(4, "/Alternate /DeviceRGB", "DeviceCMYK")]
    public void IccBased_AbsentInvalidOrMismatchedAlternate_UsesDevice(int n, string alternate, string device)
    {
        var (file, space) = IccSpace([9, 9, 9], n, alternate);
        Assert.Same(PdfColorSpace.Parse(file, new PdfName(device)), PdfColorSpace.Parse(file, space));
    }

    [Fact]
    public void IccBased_NamedAlternate_IsResolvedForEachResourceContext()
    {
        var (file, space) = IccSpace([9, 9, 9], 3, "/Range [0 100 -128 127 -128 127] /Alternate /Alt");
        var labResources = (PdfDictionary)Object("<< /ColorSpace << /Alt /Other /Other [/Lab << /WhitePoint [0.9642 1 0.8249] >>] >> >>");
        var rgbResources = (PdfDictionary)Object("<< /ColorSpace << /Alt /DeviceRGB >> >>");
        var patternResources = (PdfDictionary)Object("<< /ColorSpace << /Alt [/Pattern /DeviceRGB] >> >>");
        var first = PdfColorSpace.Parse(file, space, labResources);
        Assert.InRange(Round(first.ToRgb([50, 0, 0])).R, 118, 120);
        Assert.Same(PdfColorSpace.Rgb, PdfColorSpace.Parse(file, space, rgbResources));
        Assert.Same(PdfColorSpace.Rgb, PdfColorSpace.Parse(file, space, patternResources));
        Assert.Same(PdfColorSpace.Rgb, PdfColorSpace.Parse(file, space));
        Assert.Equal(Round(first.ToRgb([50, 0, 0])),
            Round(PdfColorSpace.Parse(file, space, labResources).ToRgb([50, 0, 0])));
    }

    [Fact]
    public void IccBased_CyclicAlternate_TerminatesWithDeviceFallback()
    {
        var (file, space) = IccSpace([9, 9, 9], 3, "/Alternate /Alt");
        var resources = (PdfDictionary)Object("<< /ColorSpace << /Alt /Alt >> >>");
        Assert.Same(PdfColorSpace.Rgb, PdfColorSpace.Parse(file, space, resources));
        resources = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["ColorSpace"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Alt"] = space })
        });
        Assert.Same(PdfColorSpace.Rgb, PdfColorSpace.Parse(file, space, resources));
    }
}
