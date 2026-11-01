using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Buffers.Binary;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

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

    static byte[] SampledCurve(params int[] values)
    {
        var b = new List<byte>();
        U32(b, Sig("curv")); U32(b, 0); U32(b, (uint)values.Length);
        foreach (var value in values) U16(b, value);
        return [.. b];
    }

    static byte[] BpcProfile(bool gray = false, int version = 4, byte[]? curve = null)
    {
        curve ??= SampledCurve(6554, 65535);
        var data = gray ? Profile("GRAY", "XYZ ", ("kTRC", curve), ("wtpt", Xyz(0.9642, 1, 0.8249)))
            : Profile("RGB ", "XYZ ",
                ("rXYZ", Xyz(0.4360747, 0.2225045, 0.0139322)),
                ("gXYZ", Xyz(0.3850649, 0.7168786, 0.0971045)),
                ("bXYZ", Xyz(0.1430804, 0.0606169, 0.7138633)),
                ("rTRC", curve), ("gTRC", curve), ("bTRC", curve), ("wtpt", Xyz(0.9642, 1, 0.8249)));
        data[8] = (byte)version;
        Encoding.ASCII.GetBytes("scnr").CopyTo(data, 12);
        return data;
    }

    [Theory]
    [InlineData(false, 2, false)]
    [InlineData(false, 4, false)]
    [InlineData(true, 2, false)]
    [InlineData(true, 4, false)]
    [InlineData(false, 2, true)]
    [InlineData(false, 4, true)]
    [InlineData(true, 2, true)]
    [InlineData(true, 4, true)]
    public void Bpc_MapsNonzeroBlackToSrgbBlack_AnchorsWhite_AndChangesMidtones(bool gray, int version, bool display)
    {
        var data = BpcProfile(gray, version);
        if (display) Encoding.ASCII.GetBytes("mntr").CopyTo(data, 12);
        var profile = IccProfile.TryParse(data)!;
        Assert.True(profile.SupportsBlackPointCompensation(RenderingIntent.RelativeColorimetric));
        double[] black = gray ? [0] : [0, 0, 0];
        double[] white = gray ? [1] : [1, 1, 1];
        double[] mid = gray ? [128.0 / 255] : [128.0 / 255, 128.0 / 255, 128.0 / 255];
        var intent = RenderingIntent.RelativeColorimetric;
        Assert.InRange(Round(profile.ToSrgb(black)).R, 88, 90);
        Assert.Equal((0, 0, 0), Round(profile.ToSrgb(black, intent, BlackPointCompensation.On)));
        Assert.Equal((255, 255, 255), Round(profile.ToSrgb(white, intent, BlackPointCompensation.On)));
        Assert.Equal(Round(profile.ToSrgb(white)), Round(profile.ToSrgb(white, intent, BlackPointCompensation.On)));
        // Independent Pillow/Little CMS 2.17 reference: input 128 -> OFF 196, ON 188.
        Assert.InRange(Round(profile.ToSrgb(mid, intent, BlackPointCompensation.Off)).R, 195, 197);
        Assert.InRange(Round(profile.ToSrgb(mid, intent, BlackPointCompensation.On)).R, 187, 189);
        Assert.Equal(profile.ToSrgb(mid), profile.ToSrgb(mid, intent, BlackPointCompensation.Off));
        Assert.Equal(profile.ToSrgb(mid), profile.ToSrgb(mid, intent, BlackPointCompensation.Default));
        if (!gray)
            Assert.Equal((137, 188, 225), Round(profile.ToSrgb([64.0 / 255, 128.0 / 255, 192.0 / 255], intent, BlackPointCompensation.On)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bpc_AbsoluteAlwaysDisables_AndOtherIntentEstimationIsExplicitlyUnsupported(bool gray)
    {
        var profile = IccProfile.TryParse(BpcProfile(gray))!;
        double[] input = gray ? [0] : [0, 0, 0];
        foreach (var intent in new[] { RenderingIntent.AbsoluteColorimetric, RenderingIntent.Perceptual, RenderingIntent.Saturation })
        {
            Assert.False(profile.SupportsBlackPointCompensation(intent));
            Assert.Equal(profile.ToSrgb(input, intent), profile.ToSrgb(input, intent, BlackPointCompensation.On));
        }
    }

    [Fact]
    public void Bpc_LutEstimation_UsesDarkestColorantOrV4ReferenceBlack_AndIntentGridsDoNotLeakChoice()
    {
        var profile = IccProfile.TryParse(IntentProfile())!;
        double[] input = [0.25, 0.5, 0.75];
        Assert.False(profile.SupportsBlackPointCompensation(RenderingIntent.AbsoluteColorimetric));
        // Constant-Lab tables: relative black (L* 50) equals the payload, so BPC maps everything to sRGB black;
        // v4 perceptual/saturation use the Table 16 reference black, a no-op for perceptual (already mapped) and a map for saturation.
        Assert.True(profile.SupportsBlackPointCompensation(RenderingIntent.RelativeColorimetric));
        Assert.Equal((0, 0, 0), Round(profile.ToSrgb(input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On)));
        Assert.Equal(Round(profile.ToSrgb(input, RenderingIntent.Perceptual)),
            Round(profile.ToSrgb(input, RenderingIntent.Perceptual, BlackPointCompensation.On)));
        Assert.True(profile.SupportsBlackPointCompensation(RenderingIntent.Saturation));
        foreach (var intent in Enum.GetValues<RenderingIntent>())
        {
            var off = Round(profile.ToSrgb(input, intent));
            var on = Round(profile.ToSrgb(input, intent, BlackPointCompensation.On));
            for (var i = 0; i < 4200; i++) profile.ToSrgb(input, intent, BlackPointCompensation.On);
            Assert.Equal(on, Round(profile.ToSrgb(input, intent, BlackPointCompensation.On)));
            Assert.Equal(off, Round(profile.ToSrgb(input, intent, BlackPointCompensation.Off)));
            Assert.Equal(off, Round(profile.ToSrgb(input, intent)));
        }
    }

    [Theory]
    [InlineData("nonmonotone")]
    [InlineData("nonunitwhite")]
    [InlineData("highblack")]
    [InlineData("unequalblack")]
    [InlineData("nonD50")]
    [InlineData("negativecolorant")]
    [InlineData("output")]
    [InlineData("version")]
    [InlineData("Lab")]
    public void Bpc_UnsupportedEstimatesAreObservable_AndPreserveBaseline(string condition)
    {
        var curve = condition switch
        {
            "nonmonotone" => SampledCurve(6554, 0, 65535),
            "nonunitwhite" => SampledCurve(6554, 60000),
            "highblack" => SampledCurve(32768, 65535),
            _ => SampledCurve(6554, 65535)
        };
        var data = Profile("RGB ", condition == "Lab" ? "Lab " : "XYZ ",
            ("rXYZ", Xyz(condition == "negativecolorant" ? -0.4360747 : 0.4360747, 0.2225045, 0.0139322)),
            ("gXYZ", Xyz(condition == "nonD50" ? 0.1 : 0.3850649, 0.7168786, 0.0971045)),
            ("bXYZ", Xyz(0.1430804, 0.0606169, 0.7138633)), ("rTRC", curve),
            ("gTRC", condition == "unequalblack" ? Identity() : curve), ("bTRC", curve));
        data[8] = condition == "version" ? (byte)3 : (byte)4;
        Encoding.ASCII.GetBytes(condition == "output" ? "prtr" : "scnr").CopyTo(data, 12);
        var profile = IccProfile.TryParse(data)!;
        Assert.NotNull(profile);
        Assert.False(profile.SupportsBlackPointCompensation(RenderingIntent.RelativeColorimetric));
        Assert.Equal(profile.ToSrgb([0.25, 0.5, 0.75]),
            profile.ToSrgb([0.25, 0.5, 0.75], RenderingIntent.RelativeColorimetric, BlackPointCompensation.On));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bpc_SharedProfilesAlternateConfigurations_WithoutAllocation(bool gray)
    {
        var data = BpcProfile(gray);
        var components = gray ? 1 : 3;
        var (file, space) = IccSpace(data, components);
        var first = PdfColorSpace.Parse(file, space);
        var other = IccSpace(data, components);
        var second = PdfColorSpace.Parse(other.File, other.Space);
        Assert.Same(first, PdfColorSpace.Parse(file, space));
        double[] input = gray ? [0] : [0, 0, 0];
        var intent = RenderingIntent.RelativeColorimetric;
        var off = first.ToRgb(input, intent, BlackPointCompensation.Off);
        for (var i = 0; i < 100; i++)
        {
            second.ToRgb(input, intent, BlackPointCompensation.On);
            first.ToRgb(input, intent, BlackPointCompensation.Off);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            second.ToRgb(input, intent, BlackPointCompensation.On);
            first.ToRgb(input, intent, BlackPointCompensation.Off);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
        Assert.Equal((0, 0, 0), Round(second.ToRgb(input, intent, BlackPointCompensation.On)));
        Assert.Equal(off, first.ToRgb(input, intent, BlackPointCompensation.Default));
    }

    const string BpcStates = "/ExtGState << /On << /UseBlackPtComp /ON >> /Off << /UseBlackPtComp /OFF >> /Default << /UseBlackPtComp /Default >> >>";

    [Theory]
    [InlineData("Off")]
    [InlineData("Default")]
    public void Bpc_ExtGStateQRestores_WithoutResettingColor(string state)
    {
        var page = RenderIcc(BpcProfile(), 3,
            $"/CS0 cs 0 0 0 sc /On gs q /{state} gs 0 0 100 200 re f Q 100 0 100 200 re f",
            resources: BpcStates);
        Assert.InRange(Px(page, 50, 100).R, 88, 90);
        Assert.Equal((0, 0, 0), Px(page, 150, 100));
        Assert.Empty(page.Warnings);
    }

    [Theory]
    [InlineData("/Unknown")]
    [InlineData("true")]
    [InlineData("17")]
    [InlineData("(ON)")]
    [InlineData("null")]
    public void Bpc_InvalidValuesWarnAndRetainState(string value)
    {
        var page = RenderIcc(BpcProfile(), 3,
            "/CS0 cs 0 0 0 sc /On gs /Bad gs 0 0 200 200 re f",
            resources: $"/ExtGState << /On << /UseBlackPtComp /ON >> /Bad << /UseBlackPtComp {value} >> >>");
        Assert.Equal((0, 0, 0), Px(page, 100, 100));
        Assert.Contains(page.Warnings, w => w.Contains("/UseBlackPtComp"));
    }

    [Theory]
    [InlineData("/RI /AbsoluteColorimetric /UseBlackPtComp /ON")]
    [InlineData("/UseBlackPtComp /ON /RI /AbsoluteColorimetric")]
    public void Bpc_ExtGStateAbsoluteIntentWinsRegardlessOfEntryOrder(string entries)
    {
        var page = RenderIcc(BpcProfile(), 3, "/CS0 cs 0 0 0 sc /G0 gs 0 0 200 200 re f",
            resources: $"/ExtGState << /G0 << {entries} >> >>");
        Assert.InRange(Px(page, 100, 100).R, 88, 90);
    }

    [Fact]
    public void Bpc_IntentOperatorDoesNotLatchAbsoluteOverride()
    {
        var page = RenderIcc(BpcProfile(), 3,
            "/On gs /CS0 cs 0 0 0 sc /AbsoluteColorimetric ri 0 0 100 200 re f /RelativeColorimetric ri 100 0 100 200 re f",
            resources: BpcStates);
        Assert.InRange(Px(page, 50, 100).R, 88, 90);
        Assert.Equal((0, 0, 0), Px(page, 150, 100));
    }

    [Theory]
    [InlineData("Indexed")]
    [InlineData("Separation")]
    [InlineData("DeviceN")]
    [InlineData("Pattern")]
    public void Bpc_WrappersPreserveContext(string kind)
    {
        var (file, icc) = IccSpace(BpcProfile(), 3);
        var function = Object("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >>");
        PdfObject[] args = kind switch
        {
            "Indexed" => [new PdfName(kind), icc, new PdfInteger(0), new PdfString([0, 0, 0])],
            "Separation" => [new PdfName(kind), new PdfName("Spot"), icc, function],
            "DeviceN" => [new PdfName(kind), new PdfArray([new PdfName("Spot")]), icc, function],
            _ => [new PdfName(kind), icc]
        };
        var wrapper = PdfColorSpace.Parse(file, new PdfArray([.. args]));
        double[] input = kind == "Pattern" ? [0, 0, 0] : [0];
        Assert.Equal((0, 0, 0), Round(wrapper.ToRgb(input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On)));
        Assert.InRange(Round(wrapper.ToRgb(input, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off)).R, 88, 90);
        Assert.InRange(Round(wrapper.ToRgb(input, RenderingIntent.AbsoluteColorimetric, BlackPointCompensation.On)).R, 88, 90);
    }

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("/Intent /RelativeColorimetric", "", 0)]
    [InlineData("/Intent /AbsoluteColorimetric", "", 89)]
    [InlineData("", "/AbsoluteColorimetric ri", 89)]
    [InlineData("/Intent /RelativeColorimetric", "/AbsoluteColorimetric ri", 0)]
    public void Bpc_RepeatedImageAndInlineImage_HonorImageIntentWithoutLeaking(string entry, string parentIntent, int expected)
    {
        var image = StreamObject(6, $"/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /CS0 {entry}",
            "\0\0\0");
        var page = RenderIcc(BpcProfile(), 3,
            $"{parentIntent} /On gs q 100 0 0 200 0 0 cm /Im0 Do Q /Off gs q 100 0 0 200 100 0 cm /Im0 Do Q",
            resources: BpcStates + " /XObject << /Im0 6 0 R >>", objects: image);
        Assert.InRange(Px(page, 50, 100).R, Math.Max(0, expected - 1), expected + 1);
        Assert.InRange(Px(page, 150, 100).R, 88, 90);
        var inline = RenderIcc(BpcProfile(), 3,
            $"{parentIntent} /On gs 200 0 0 200 0 0 cm BI /W 1 /H 1 /BPC 8 /CS /CS0 {entry} ID \0\0\0 EI",
            resources: BpcStates);
        Assert.InRange(Px(inline, 100, 100).R, Math.Max(0, expected - 1), expected + 1);
    }

    [Fact]
    public void Bpc_ImageSoftMask_InheritsChoice_ButKeepsItsOwnIntent()
    {
        var (file, space) = IccSpace(BpcProfile(gray: true), 1);
        var maskDict = (PdfDictionary)Object("<< /Width 1 /Height 1 /BitsPerComponent 8 >>");
        maskDict.Entries["ColorSpace"] = space;
        var dict = (PdfDictionary)Object("<< /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB >>");
        dict.Entries["SMask"] = new PdfStream(maskDict, [0]);
        var image = new PdfStream(dict, [255, 0, 0]);
        var res = (PdfDictionary)Object("<< >>");
        var on = ImageDecoder.Decode(file, image, res, out _, out _, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On)!;
        var off = ImageDecoder.Decode(file, image, res, out _, out _, RenderingIntent.RelativeColorimetric, BlackPointCompensation.Off)!;
        Assert.Equal(0, on.Rgba[3]);
        Assert.InRange(off.Rgba[3], 88, 90);
        maskDict.Entries["Intent"] = new PdfName("AbsoluteColorimetric");
        var absolute = ImageDecoder.Decode(file, image, res, out _, out _, RenderingIntent.RelativeColorimetric, BlackPointCompensation.On)!;
        Assert.Equal(off.Rgba[3], absolute.Rgba[3]);
    }

    [Theory]
    [InlineData("On", 0)]
    [InlineData("Off", 89)]
    public void Bpc_StrokeAndType3Text_InheritChoice(string state, int expected)
    {
        var stroke = RenderIcc(BpcProfile(), 3, $"/{state} gs /CS0 CS 0 0 0 SC 100 w 0 100 m 200 100 l S",
            resources: BpcStates);
        Assert.InRange(Px(stroke, 100, 100).R, Math.Max(0, expected - 1), expected + 1);
        var glyph = StreamObject(6, "", "1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f");
        const string font = "/Font << /F0 << /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /A 6 0 R >> /Encoding << /Type /Encoding /Differences [65 /A] >> /FirstChar 65 /LastChar 65 /Widths [1000] >> >>";
        var text = RenderIcc(BpcProfile(), 3, $"/{state} gs /CS0 cs 0 0 0 sc BT /F0 100 Tf 50 50 Td (A) Tj ET",
            resources: BpcStates + " " + font, objects: glyph);
        Assert.InRange(Px(text, 100, 100).R, Math.Max(0, expected - 1), expected + 1);
    }

    [Theory]
    [InlineData("On", 0)]
    [InlineData("Off", 89)]
    public void Bpc_FunctionAndMeshShading_InheritChoice(string state, int expected)
    {
        const string fn = "/Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [0 0 0] /N 1 >>";
        var axial = RenderIcc(BpcProfile(), 3, $"/{state} gs /Sh0 sh",
            resources: BpcStates + $" /Shading << /Sh0 << /ShadingType 2 /ColorSpace /CS0 /Coords [0 0 200 0] {fn} /Extend [true true] >> >>");
        Assert.InRange(Px(axial, 100, 100).R, Math.Max(0, expected - 1), expected + 1);
        var mesh = StreamObject(6,
            "/ShadingType 5 /ColorSpace /CS0 /BitsPerCoordinate 8 /BitsPerComponent 8 /VerticesPerRow 2 /Decode [0 200 0 200 0 1 0 1 0 1]",
            Encoding.Latin1.GetString([0, 0, 0, 0, 0, 255, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 0, 0]));
        var page = RenderIcc(BpcProfile(), 3, $"/{state} gs /Sh0 sh",
            resources: BpcStates + " /Shading << /Sh0 6 0 R >>", objects: mesh);
        Assert.InRange(Px(page, 100, 100).R, Math.Max(0, expected - 1), expected + 1);
    }

    [Theory]
    [InlineData(false, "ON", 255)]
    [InlineData(true, "ON", 255)]
    [InlineData(false, "OFF", 166)]
    [InlineData(true, "OFF", 166)]
    public void Bpc_SoftMaskBackdropAndContent_InheritStateBeforeMaskEvaluation(bool backdrop, string state, int expected)
    {
        var content = backdrop ? "" : "/CS0 cs 0 0 0 sc 0 0 200 200 re f";
        var mask = StreamObject(6, "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS [/ICCBased 5 0 R] >>", content);
        var bc = backdrop ? "/BC [0 0 0]" : "";
        var page = RenderIcc(BpcProfile(), 3, "/G0 gs 0 g 0 0 200 200 re f",
            resources: $"/ExtGState << /G0 << /SMask << /S /Luminosity {bc} /G 6 0 R >> /UseBlackPtComp /{state} /RI /RelativeColorimetric >> >>",
            objects: mask);
        Assert.InRange(Px(page, 100, 100).R, expected - 1, Math.Min(255, expected + 1));
    }

    [Theory]
    [InlineData(false, "On", 0)]
    [InlineData(true, "On", 0)]
    [InlineData(false, "Off", 89)]
    [InlineData(true, "Off", 89)]
    public void Bpc_ColoredAndUncoloredPatterns_InheritChoice(bool uncolored, string state, int expected)
    {
        var paint = uncolored ? "" : "/CS0 cs 0 0 0 sc ";
        var pattern = StreamObject(6,
            $"/Type /Pattern /PatternType 1 /PaintType {(uncolored ? 2 : 1)} /TilingType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 /Resources << /ColorSpace << /CS0 [/ICCBased 5 0 R] >> >>",
            paint + "0 0 20 20 re f");
        var color = uncolored ? "/CS1 cs 0 0 0 /P0 scn" : "/Pattern cs /P0 scn";
        var page = RenderIcc(BpcProfile(), 3, $"/{state} gs {color} 0 0 200 200 re f",
            resources: BpcStates + " /Pattern << /P0 6 0 R >>", objects: pattern, colorSpaces: "/CS1 [/Pattern /CS0]");
        Assert.InRange(Px(page, 110, 110).R, Math.Max(0, expected - 1), expected + 1);
    }

    [Fact]
    public void Bpc_FormInheritsState_AndRestoresCallerChoice()
    {
        var form = StreamObject(6, "/Type /XObject /Subtype /Form /BBox [0 0 200 200]",
            "/CS0 cs 0 0 0 sc 0 0 100 200 re f /Off gs");
        var page = RenderIcc(BpcProfile(), 3,
            "/On gs /F0 Do /CS0 cs 0 0 0 sc 100 0 100 200 re f",
            resources: BpcStates + " /XObject << /F0 6 0 R >>", objects: form);
        Assert.Equal((0, 0, 0), Px(page, 50, 100));
        Assert.Equal((0, 0, 0), Px(page, 150, 100));
    }

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

    static RenderedPage RenderIcc(byte[] icc, int n, string content, string alternate = "", string resources = "", string objects = "", string colorSpaces = "")
    {
        var data = Encoding.Latin1.GetString(icc);
        var obj = $"5 0 obj\n<< /N {n} /Length {data.Length} {alternate} >>\nstream\n{data}\nendstream\nendobj\n";
        var pdf = TestPdf.Build(content, $"/ColorSpace << /CS0 [/ICCBased 5 0 R] {colorSpaces} >> " + resources, obj + objects, brokenXref: true);
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

    static byte[] ConstantLabLut(int lightness)
    {
        var b = new List<byte>();
        U32(b, Sig("mft2")); U32(b, 0);
        b.AddRange([3, 3, 2, 0]);
        foreach (var v in new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) S15(b, v);
        U16(b, 2); U16(b, 2);
        for (var i = 0; i < 3; i++) { U16(b, 0); U16(b, 65535); }
        for (var i = 0; i < 8; i++)
        {
            U16(b, (int)Math.Round(lightness / 100.0 * 65280));
            U16(b, 0x8000); U16(b, 0x8000);
        }
        for (var i = 0; i < 3; i++) { U16(b, 0); U16(b, 65535); }
        return [.. b];
    }

    static byte[] IntentProfile(bool relative = true, bool saturation = true, bool dimWhite = false, int version = 4)
    {
        var tags = new List<(string, byte[])> { ("A2B0", ConstantLabLut(20)) };
        if (relative) tags.Add(("A2B1", ConstantLabLut(50)));
        if (saturation) tags.Add(("A2B2", ConstantLabLut(80)));
        var scale = dimWhite ? 0.5 : 1;
        tags.Add(("wtpt", Xyz(0.9642 * scale, scale, 0.8249 * scale)));
        var profile = Profile("RGB ", "Lab ", [.. tags]);
        profile[8] = (byte)version;
        if (version >= 4) Encoding.ASCII.GetBytes("scnr").CopyTo(profile, 12);
        return profile;
    }

    static void AssertIntentColor(int red, RenderingIntent intent, bool referenceBlack = true) => Assert.InRange(red, intent switch
    {
        RenderingIntent.Perceptual => referenceBlack ? 44 : 47,
        RenderingIntent.Saturation => 197,
        _ => 118
    }, intent switch
    {
        RenderingIntent.Perceptual => referenceBlack ? 46 : 49,
        RenderingIntent.Saturation => 199,
        _ => 120
    });

    [Theory]
    [InlineData(RenderingIntent.Perceptual)]
    [InlineData(RenderingIntent.RelativeColorimetric)]
    [InlineData(RenderingIntent.Saturation)]
    [InlineData(RenderingIntent.AbsoluteColorimetric)]
    public void IntentTables_SelectRequestedTransform(RenderingIntent intent)
    {
        var profile = IccProfile.TryParse(IntentProfile())!;
        Assert.False(profile.IsSrgb);
        // v4 perceptual A2B0: ICC.1:2022 Table 16 reference black maps to sRGB black (Little CMS gives 45; 48 before the map).
        AssertIntentColor(Round(profile.ToSrgb([0.3, 0.4, 0.5], intent)).R, intent, referenceBlack: true);
        Assert.Equal(Round(profile.ToSrgb([0.3, 0.4, 0.5], RenderingIntent.RelativeColorimetric)),
            Round(profile.ToSrgb([0.3, 0.4, 0.5])));
    }

    [Theory]
    [InlineData(RenderingIntent.RelativeColorimetric)]
    [InlineData(RenderingIntent.AbsoluteColorimetric)]
    [InlineData(RenderingIntent.Saturation)]
    public void MissingIntentTable_FallsBackToA2B0(RenderingIntent intent)
    {
        var profile = IccProfile.TryParse(IntentProfile(relative: false, saturation: false))!;
        AssertIntentColor(Round(profile.ToSrgb([0.3, 0.4, 0.5], intent)).R, RenderingIntent.Perceptual, referenceBlack: intent == RenderingIntent.Saturation);
    }

    [Fact]
    public void MissingSaturationTable_DoesNotFallBackToRelative()
    {
        var profile = IccProfile.TryParse(IntentProfile(saturation: false))!;
        AssertIntentColor(Round(profile.ToSrgb([0.3, 0.4, 0.5], RenderingIntent.Saturation)).R, RenderingIntent.Perceptual);
    }

    [Fact]
    public void A2B1OnlyProfile_PreservesExistingRelativeSupport_WithoutInventingOtherTransforms()
    {
        var data = Profile("RGB ", "Lab ", ("A2B1", ConstantLabLut(50)));
        var profile = IccProfile.TryParse(data)!;
        Assert.NotNull(profile);
        AssertIntentColor(Round(profile.ToSrgb([0.5, 0.5, 0.5])).R, RenderingIntent.RelativeColorimetric);
        AssertIntentColor(Round(profile.ToSrgb([0.5, 0.5, 0.5], RenderingIntent.AbsoluteColorimetric)).R,
            RenderingIntent.AbsoluteColorimetric);
        Assert.Throws<NotSupportedException>(() => profile.ToSrgb([0.5, 0.5, 0.5], RenderingIntent.Perceptual));
        Assert.Throws<NotSupportedException>(() => profile.ToSrgb([0.5, 0.5, 0.5], RenderingIntent.Saturation));
    }

    [Theory]
    [InlineData(2, 118, 120)]
    [InlineData(4, 84, 87)]
    public void AbsoluteIntent_UsesRelativeTableAndMediaWhite_WithV2DisplayRule(int version, int low, int high)
    {
        var profile = IccProfile.TryParse(IntentProfile(dimWhite: true, version: version))!;
        Assert.InRange(Round(profile.ToSrgb([0.5, 0.5, 0.5], RenderingIntent.AbsoluteColorimetric)).R, low, high);
        AssertIntentColor(Round(profile.ToSrgb([0.5, 0.5, 0.5])).R, RenderingIntent.RelativeColorimetric);
    }

    [Fact]
    public void IntentGrids_RemainIsolated_WhenSharedProfileAlternatesIntents()
    {
        var (file, space) = IccSpace(IntentProfile(dimWhite: true), 3);
        var first = PdfColorSpace.Parse(file, space);
        Assert.Same(first, PdfColorSpace.Parse(file, space));
        var other = IccSpace(IntentProfile(dimWhite: true), 3);
        var second = PdfColorSpace.Parse(other.File, other.Space);
        RenderingIntent[] intents = [RenderingIntent.Saturation, RenderingIntent.AbsoluteColorimetric,
            RenderingIntent.Perceptual, RenderingIntent.RelativeColorimetric];
        var exact = intents.Select(i => Round(first.ToRgb([0.25, 0.5, 0.75], i))).ToArray();
        for (var j = 0; j < intents.Length; j++)
        {
            for (var k = 0; k < 4100; k++) second.ToRgb([0.25, 0.5, 0.75], intents[j]);
            for (var k = 0; k < intents.Length; k++)
                Assert.Equal(exact[k], Round(first.ToRgb([0.25, 0.5, 0.75], intents[k])));
        }
    }

    [Theory]
    [InlineData("Perceptual", RenderingIntent.Perceptual)]
    [InlineData("RelativeColorimetric", RenderingIntent.RelativeColorimetric)]
    [InlineData("Saturation", RenderingIntent.Saturation)]
    [InlineData("AbsoluteColorimetric", RenderingIntent.AbsoluteColorimetric)]
    [InlineData("UnknownIntent", RenderingIntent.RelativeColorimetric)]
    public void RenderingIntentOperator_AffectsPainting(string name, RenderingIntent expected)
    {
        var page = RenderIcc(IntentProfile(), 3,
            $"/CS0 cs 0.5 0.5 0.5 sc /Perceptual ri /{name} ri 0 0 200 200 re f");
        AssertIntentColor(Px(page, 100, 100).R, expected);
    }

    [Theory]
    [InlineData("Saturation", RenderingIntent.Saturation)]
    [InlineData("UnknownIntent", RenderingIntent.RelativeColorimetric)]
    public void ExtGStateIntent_OverridesOperator_AndQRestoresIt(string name, RenderingIntent expected)
    {
        var page = RenderIcc(IntentProfile(), 3,
            "/CS0 cs 0.5 0.5 0.5 sc /Perceptual ri q /G0 gs 0 0 100 200 re f Q 100 0 100 200 re f",
            resources: $"/ExtGState << /G0 << /RI /{name} >> >>");
        AssertIntentColor(Px(page, 50, 100).R, expected);
        AssertIntentColor(Px(page, 150, 100).R, RenderingIntent.Perceptual);
    }

    [Fact]
    public void RenderingIntentOperator_QRestoresIt_WithoutResettingColor()
    {
        var page = RenderIcc(IntentProfile(), 3,
            "/CS0 cs 0.5 0.5 0.5 sc /Saturation ri q /Perceptual ri 0 0 100 200 re f Q 100 0 100 200 re f");
        AssertIntentColor(Px(page, 50, 100).R, RenderingIntent.Perceptual);
        AssertIntentColor(Px(page, 150, 100).R, RenderingIntent.Saturation);
    }

    [Theory]
    [InlineData("", RenderingIntent.Saturation)]
    [InlineData("/Intent /Perceptual", RenderingIntent.Perceptual)]
    [InlineData("/Intent /UnknownIntent", RenderingIntent.RelativeColorimetric)]
    public void ImageIntent_OverridesGraphicsState_WithoutLeaking(string entry, RenderingIntent expected)
    {
        var image = $"6 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /CS0 {entry} /Length 3 >>\nstream\n@@@\nendstream\nendobj\n";
        var page = RenderIcc(IntentProfile(), 3,
            "/Saturation ri q 100 0 0 200 0 0 cm /Im0 Do Q /CS0 cs 0.5 0.5 0.5 sc 100 0 100 200 re f",
            resources: "/XObject << /Im0 6 0 R >>", objects: image);
        AssertIntentColor(Px(page, 50, 100).R, expected);
        AssertIntentColor(Px(page, 150, 100).R, RenderingIntent.Saturation);
    }

    [Fact]
    public void ImageDecoder_LegacyApiDefaultsToRelative()
    {
        var (file, space) = IccSpace(IntentProfile(), 3);
        var dict = (PdfDictionary)Object("<< /Width 1 /Height 1 /BitsPerComponent 8 >>");
        dict.Entries["ColorSpace"] = space;
        var image = ImageDecoder.Decode(file, new PdfStream(dict, [128, 128, 128]),
            (PdfDictionary)Object("<< >>"), out var mask, out var warning);
        Assert.NotNull(image);
        Assert.False(mask);
        Assert.Null(warning);
        AssertIntentColor(image!.Rgba[0], RenderingIntent.RelativeColorimetric);
    }

    [Theory]
    [InlineData("Indexed")]
    [InlineData("Separation")]
    [InlineData("DeviceN")]
    [InlineData("Pattern")]
    public void WrapperColorSpaces_PropagateIntent(string kind)
    {
        var (file, icc) = IccSpace(IntentProfile(), 3);
        var function = Object("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >>");
        PdfObject[] args = kind switch
        {
            "Indexed" => [new PdfName(kind), icc, new PdfInteger(0), new PdfString([128, 128, 128])],
            "Separation" => [new PdfName(kind), new PdfName("Spot"), icc, function],
            "DeviceN" => [new PdfName(kind), new PdfArray([new PdfName("Spot")]), icc, function],
            _ => [new PdfName(kind), icc]
        };
        var wrapper = PdfColorSpace.Parse(file, new PdfArray([.. args]));
        double[] input = kind == "Pattern" ? [0.5, 0.5, 0.5] : [0];
        foreach (var intent in new[] { RenderingIntent.Perceptual, RenderingIntent.Saturation, RenderingIntent.RelativeColorimetric })
            AssertIntentColor(Round(wrapper.ToRgb(input, intent)).R, intent);
    }

    [Fact]
    public void AbsoluteIntent_MatrixAndGray_ApplyMediaWhite()
    {
        var matrixData = MatrixRgb(Identity());
        matrixData[8] = 4;
        Encoding.ASCII.GetBytes("scnr").CopyTo(matrixData, 12);
        // The last tag is wtpt in this synthetic profile.
        var whiteOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(matrixData.AsSpan(132 + 6 * 12 + 4));
        for (var k = 0; k < 3; k++)
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(matrixData.AsSpan(whiteOffset + 8 + k * 4));
            BinaryPrimitives.WriteInt32BigEndian(matrixData.AsSpan(whiteOffset + 8 + k * 4), value / 2);
        }
        var matrix = IccProfile.TryParse(matrixData)!;
        Assert.False(matrix.IsSrgb);
        Assert.InRange(Round(matrix.ToSrgb([0.5, 0.5, 0.5])).R, 187, 189);
        Assert.InRange(Round(matrix.ToSrgb([0.5, 0.5, 0.5], RenderingIntent.AbsoluteColorimetric)).R, 136, 138);

        var grayData = Profile("GRAY", "XYZ ", ("kTRC", Identity()), ("wtpt", Xyz(0.4821, 0.5, 0.41245)));
        grayData[8] = 4;
        Encoding.ASCII.GetBytes("scnr").CopyTo(grayData, 12);
        var gray = IccProfile.TryParse(grayData)!;
        Assert.InRange(Round(gray.ToSrgb([0.5])).R, 187, 189);
        Assert.InRange(Round(gray.ToSrgb([0.5], RenderingIntent.AbsoluteColorimetric)).R, 136, 138);
    }

    [Fact]
    public void IntentEvaluation_DoesNotAllocatePerSample()
    {
        var profile = IccProfile.TryParse(IntentProfile())!;
        double[] input = [0.25, 0.5, 0.75];
        foreach (var intent in Enum.GetValues<RenderingIntent>())
        {
            for (var i = 0; i < 4200; i++) profile.ToSrgb(input, intent);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) profile.ToSrgb(input, intent);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
        }
    }

    [Fact]
    public void Stroke_UsesIntent()
    {
        var page = RenderIcc(IntentProfile(), 3,
            "/CS0 CS 0.5 0.5 0.5 SC /Saturation ri 100 w 0 100 m 200 100 l S");
        AssertIntentColor(Px(page, 100, 100).R, RenderingIntent.Saturation);
    }

    static string StreamObject(int number, string dict, string content) =>
        $"{number} 0 obj\n<< {dict} /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n";

    [Fact]
    public void Type3Text_InheritsIntent()
    {
        var glyph = StreamObject(6, "", "1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f");
        const string font = "/Font << /F0 << /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /A 6 0 R >> /Encoding << /Type /Encoding /Differences [65 /A] >> /FirstChar 65 /LastChar 65 /Widths [1000] >> >>";
        var page = RenderIcc(IntentProfile(), 3,
            "/CS0 cs 0.5 0.5 0.5 sc /Saturation ri BT /F0 100 Tf 50 50 Td (A) Tj ET",
            resources: font, objects: glyph);
        AssertIntentColor(Px(page, 100, 100).R, RenderingIntent.Saturation);
    }

    [Fact]
    public void Shading_UsesIntent()
    {
        const string shading = "/Shading << /Sh0 << /ShadingType 2 /ColorSpace /CS0 /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >> /Extend [true true] >> >>";
        var page = RenderIcc(IntentProfile(), 3, "/Saturation ri /Sh0 sh", resources: shading);
        AssertIntentColor(Px(page, 100, 100).R, RenderingIntent.Saturation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshShading_UsesIntent_WithAndWithoutFunction(bool function)
    {
        var data = new List<byte>();
        foreach (var (x, y) in new (byte, byte)[] { (0, 0), (255, 0), (0, 255), (255, 255) })
        {
            data.Add(x); data.Add(y); data.Add(128);
            if (!function) data.AddRange([128, 128]);
        }
        var fn = function ? "/Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >>" : "";
        var decode = function ? "0 1" : "0 1 0 1 0 1";
        var mesh = StreamObject(6,
            $"/ShadingType 5 /ColorSpace /CS0 /BitsPerCoordinate 8 /BitsPerComponent 8 /VerticesPerRow 2 /Decode [0 200 0 200 {decode}] {fn}",
            Encoding.Latin1.GetString(data.ToArray()));
        var page = RenderIcc(IntentProfile(), 3, "/Saturation ri /Sh0 sh",
            resources: "/Shading << /Sh0 6 0 R >>", objects: mesh);
        AssertIntentColor(Px(page, 100, 100).R, RenderingIntent.Saturation);
    }

    [Fact]
    public void SoftMaskBackdrop_UsesExtGStateIntent_RegardlessOfEntryOrder()
    {
        var mask = StreamObject(6, "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS [/ICCBased 5 0 R] >>", "");
        var page = RenderIcc(IntentProfile(), 3, "/G0 gs 0 g 0 0 200 200 re f",
            resources: "/ExtGState << /G0 << /SMask << /S /Luminosity /BC [0.5 0.5 0.5] /G 6 0 R >> /RI /Saturation >> >>",
            objects: mask);
        Assert.InRange(Px(page, 100, 100).R, 55, 59);
    }

    [Fact]
    public void SoftMaskContent_InheritsIntent()
    {
        var mask = StreamObject(6, "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceRGB >>",
            "/CS0 cs 0.5 0.5 0.5 sc 0 0 200 200 re f");
        var page = RenderIcc(IntentProfile(), 3, "/Saturation ri /G0 gs 0 g 0 0 200 200 re f",
            resources: "/ExtGState << /G0 << /SMask << /S /Luminosity /G 6 0 R >> >> >>",
            objects: mask);
        Assert.InRange(Px(page, 100, 100).R, 55, 59);
    }

    [Fact]
    public void Painting_DefaultsToRelative()
    {
        var page = RenderIcc(IntentProfile(), 3, "/CS0 cs 0.5 0.5 0.5 sc 0 0 200 200 re f");
        AssertIntentColor(Px(page, 100, 100).R, RenderingIntent.RelativeColorimetric);
    }

    [Theory]
    [InlineData("", RenderingIntent.Saturation)]
    [InlineData("/Intent /Perceptual", RenderingIntent.Perceptual)]
    [InlineData("/Intent /UnknownIntent", RenderingIntent.RelativeColorimetric)]
    public void InlineImage_UsesIntent(string entry, RenderingIntent expected)
    {
        var page = RenderIcc(IntentProfile(), 3,
            $"/Saturation ri 200 0 0 200 0 0 cm BI /W 1 /H 1 /BPC 8 /CS /CS0 {entry} ID @@@ EI");
        AssertIntentColor(Px(page, 100, 100).R, expected);
    }

    [Fact]
    public void ColoredTilingPattern_InheritsIntent()
    {
        var pattern = StreamObject(6,
            "/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 /Resources << /ColorSpace << /CS0 [/ICCBased 5 0 R] >> >>",
            "/CS0 cs 0.5 0.5 0.5 sc 0 0 20 20 re f");
        var page = RenderIcc(IntentProfile(), 3,
            "/Saturation ri /Pattern cs /P0 scn 0 0 200 200 re f",
            resources: "/Pattern << /P0 6 0 R >>", objects: pattern);
        AssertIntentColor(Px(page, 110, 110).R, RenderingIntent.Saturation);
    }

    [Fact]
    public void Form_InheritsIntent_AndRestoresCallerIntent()
    {
        var form = StreamObject(6, "/Type /XObject /Subtype /Form /BBox [0 0 200 200]",
            "/CS0 cs 0.5 0.5 0.5 sc 0 0 100 200 re f /Perceptual ri");
        var page = RenderIcc(IntentProfile(), 3,
            "/Saturation ri /F0 Do /CS0 cs 0.5 0.5 0.5 sc 100 0 100 200 re f",
            resources: "/XObject << /F0 6 0 R >>", objects: form);
        AssertIntentColor(Px(page, 50, 100).R, RenderingIntent.Saturation);
        AssertIntentColor(Px(page, 150, 100).R, RenderingIntent.Saturation);
    }

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

    [Fact]
    public void IccBased_RepeatedCorruptProfile_UsesAlternateAcrossStreams()
    {
        const string alternate = "/Alternate [/CalGray << /WhitePoint [0.9642 1 0.8249] >>]";
        var first = IccSpace([9, 9, 9], 1, alternate);
        var second = IccSpace([9, 9, 9], 1, alternate);

        var firstColor = Round(PdfColorSpace.Parse(first.File, first.Space).ToRgb([0.5]));
        var secondColor = Round(PdfColorSpace.Parse(second.File, second.Space).ToRgb([0.5]));

        Assert.InRange(firstColor.R, 185, 191);
        Assert.Equal(firstColor, secondColor);
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
