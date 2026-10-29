using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

public class TransparencyTests
{
    static (int R, int G, int B) Px(RenderedPage p, int x, int y)
    {
        var o = (y * p.Width + x) * 4;
        return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]);
    }

    static RenderedPage Render(string content, string res, string objs = "", string box = "[0 0 200 200]")
        => DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(content, res, objs, box, brokenXref: true)).Pages[0], 72);

    static string Stream(int n, string dict, string data)
        => $"{n} 0 obj\n<< {dict} /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n";

    static void Near((int R, int G, int B) a, (int, int, int) e, int tol = 3)
    {
        Assert.True(Math.Abs(a.R - e.Item1) <= tol && Math.Abs(a.G - e.Item2) <= tol && Math.Abs(a.B - e.Item3) <= tol, $"expected {e} got {a}");
    }

    static string GroupForm(string groupFlags, string content)
        => Stream(5, $"/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency {groupFlags} >> /Resources << /ExtGState << /M << /BM /Multiply >> /A << /ca 0.5 >> >> >>", content);

    const string RedPage = "1 0 0 rg 0 0 200 200 re f ";

    static PdfPage GroupPage(string content, string? group, string resources = "", string objects = "", string extraPage = "")
    {
        var pdf = Encoding.Latin1.GetString(TestPdf.Build(content, resources, objects, brokenXref: true));
        var attributes = group is null ? "" : $"/Group << /S /Transparency {group} >>";
        pdf = pdf.Replace("/Type /Page /Parent", $"/Type /Page {attributes} {extraPage} /Parent");
        return PdfDocument.Load(Encoding.Latin1.GetBytes(pdf)).Pages[0];
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/I false")]
    [InlineData("/I true")]
    public void OutputPage_IsIsolatedRegardlessOfPageFlag(string? flags)
    {
        // 11.4.7: Difference must not see the paper. PDFium 156 also returns red.
        var page = GroupPage("/B gs 1 0 0 rg 0 0 100 200 re f", flags, "/ExtGState << /B << /BM /Difference /ca 0.5 >> >>");
        var p = DocumentRenderer.RenderPage(page, 72);
        Near(Px(p, 50, 100), (255, 128, 128), 1);
        Near(Px(p, 150, 100), (255, 255, 255), 0);
        Assert.All(Enumerable.Range(0, p.Width * p.Height), i => Assert.Equal(255, p.Rgba[i * 4 + 3]));
        Assert.Empty(p.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/I false")]
    [InlineData("/I true")]
    public void PageGroup_GrayConvertsSourcesBeforeBlending(string flags)
    {
        const string content = RedPage + "/M gs 0 0 1 rg 0 0 200 200 re f";
        const string res = "/ExtGState << /M << /BM /Multiply >> >>";
        var gray = DocumentRenderer.RenderPage(GroupPage(content, flags + " /CS /DeviceGray", res), 72);
        var rgb = DocumentRenderer.RenderPage(GroupPage(content, flags + " /CS /DeviceRGB", res), 72);
        // 10.4.2.2, 11.6.6: gray(red) * gray(blue) = .3 * .11, not gray(red * blue).
        Near(Px(gray, 100, 100), (8, 8, 8), 1);
        Near(Px(rgb, 100, 100), (0, 0, 0), 0);
        Assert.Empty(gray.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/I false")]
    [InlineData("/I true")]
    public void PageGroup_GrayConvertsBeforeFlatteningOntoPaper(string flags)
    {
        var p = DocumentRenderer.RenderPage(GroupPage("/B gs " + RedPage, flags + " /CS /DeviceGray",
            "/ExtGState << /B << /BM /Difference /ca 0.5 >> >>"), 72);
        Near(Px(p, 100, 100), (166, 166, 166), 1);
    }

    [Theory]
    [InlineData(false, 97)]
    [InlineData(true, 142)]
    public void PageGroup_GrayPreservesKnockout(bool knockout, int expected)
    {
        var page = GroupPage("/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f",
            $"/CS /DeviceGray /K {knockout.ToString().ToLowerInvariant()}", "/ExtGState << /A << /ca 0.5 >> >>");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (expected, expected, expected), 1);
    }

    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("/I false", 0, 0, 0)]
    [InlineData("/I true", 0, 255, 255)]
    public void PlacedPage_IsolationControlsWhetherItSeesBackdrop(string flags, int r, int g, int b)
    {
        using var raster = new SoftwareRasterizer(200, 200);
        var rect = new PathData(); rect.Rect(0, 0, 200, 200);
        raster.Fill(rect, Matrix.Identity, false, new RgbColor(1, 0, 0), 1, "Normal");
        var page = GroupPage("/M gs 0 1 1 rg 0 0 200 200 re f", flags, "/ExtGState << /M << /BM /Multiply >> >>");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (r, g, b), 1);
    }

    [Theory]
    [InlineData("/I false", 0, 255, 255)]
    [InlineData("/I true", 179, 179, 179)]
    public void PlacedPage_ColorSpaceIsHonouredOnlyWhenIsolated(string flags, int r, int g, int b)
    {
        using var raster = new SoftwareRasterizer(200, 200);
        var page = GroupPage("0 1 1 rg 0 0 200 200 re f", flags + " /CS /DeviceGray");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (r, g, b), 1);
    }

    [Fact]
    public void PlacedPage_NonIsolatedInheritsGrayAndIgnoresUnsupportedCS()
    {
        using var raster = new SoftwareRasterizer(200, 200);
        raster.BeginGroup(true, false, 1, "Normal", PdfColorSpace.Gray);
        var rect = new PathData(); rect.Rect(0, 0, 200, 200);
        raster.Fill(rect, Matrix.Identity, false, new RgbColor(1, 0, 0), 1, "Normal");
        var page = GroupPage("/M gs 0 1 1 rg 0 0 200 200 re f", "/CS /DeviceCMYK",
            "/ExtGState << /M << /BM /Multiply >> >>");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        raster.EndGroup();
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (54, 54, 54), 1);
    }

    [Fact]
    public void PlacedPage_ClipsToCropBoxAndRestoresParentSpaceAndClip()
    {
        using var raster = new SoftwareRasterizer(300, 300);
        var page = GroupPage("1 0 0 rg -100 -100 400 400 re f", "/I true /CS /DeviceGray");
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Translate(50, 50), asPageElement: true);
        var rect = new PathData(); rect.Rect(0, 0, 20, 20);
        raster.Fill(rect, Matrix.Identity, false, new RgbColor(1, 0, 0), 1, "Normal");
        var p = new RenderedPage(300, 300, raster.ToRgba(), []);
        Near(Px(p, 100, 100), (77, 77, 77), 1);
        Near(Px(p, 25, 100), (255, 255, 255), 0);
        Near(Px(p, 10, 10), (255, 0, 0), 0);
    }

    [Theory]
    [InlineData("/CS /Blend", "/ColorSpace << /Blend /DeviceGray >>")]
    [InlineData("/CS 5 0 R", "")]
    [InlineData("/CS /DeviceGray", "/ColorSpace << /DefaultGray /DeviceGray >>")]
    public void PageGroup_ColorSpaceResolvesNamesReferencesAndDefaults(string group, string resources)
    {
        var page = GroupPage(RedPage, group, resources, "5 0 obj /DeviceGray endobj\n");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (77, 77, 77), 1);
    }

    [Theory]
    [InlineData("/DeviceCMYK")]
    [InlineData("[/CalGray << /WhitePoint [0.9505 1 1.089] >>]")]
    [InlineData("[/CalRGB << /WhitePoint [0.9505 1 1.089] >>]")]
    [InlineData("[/ICCBased 5 0 R]")]
    [InlineData("[/Lab << /WhitePoint [0.9505 1 1.089] >>]")]
    [InlineData("[/Indexed /DeviceRGB 0 <FF0000>]")]
    [InlineData("[/Separation /Spot /DeviceRGB << >>]")]
    [InlineData("[/DeviceN [/Spot] /DeviceRGB << >>]")]
    [InlineData("/Pattern")]
    [InlineData("/Missing")]
    public void PageGroup_UnsupportedCSFailsInsteadOfFallingBack(string space)
    {
        var page = GroupPage(RedPage, "/CS " + space, objects: Stream(5, "/N 3", "invalid ICC profile"));
        var e = Assert.Throws<NotSupportedException>(() => DocumentRenderer.RenderPage(page, 72));
        Assert.Contains("Page transparency group colour space", e.Message);
    }

    [Fact]
    public void PageGroup_UnsupportedDefaultSpaceFailsInsteadOfFallingBack()
    {
        var page = GroupPage(RedPage, "/CS /DeviceRGB", "/ColorSpace << /DefaultRGB [/ICCBased 5 0 R] >>",
            Stream(5, "/N 3", "invalid ICC profile"));
        Assert.Throws<NotSupportedException>(() => DocumentRenderer.RenderPage(page, 72));
    }

    [Fact]
    public void PageGroup_CyclicColorSpaceFailsExplicitly()
    {
        var page = GroupPage(RedPage, "/CS /Loop", "/ColorSpace << /Loop /Loop >>");
        Assert.Throws<PdfParseException>(() => DocumentRenderer.RenderPage(page, 72));
    }

    [Theory]
    [InlineData("/CS null", "")]
    [InlineData("/CS 5 0 R", "")]
    [InlineData("/CS 99 0 R", "")]
    [InlineData("/CS /DeviceRGB", "/ColorSpace << /DefaultRGB 99 0 R >>")]
    public void PageGroup_NullEntriesAndReferencesToMissingObjectsAreAbsent(string group, string resources)
    {
        // 7.3.9/7.3.10: null dictionary entries and references to missing objects are absent.
        var page = GroupPage(RedPage, group, resources, "5 0 obj null endobj\n");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (255, 0, 0), 0);
    }

    [Fact]
    public void PageGroup_InvalidDefaultComponentCountFailsExplicitly()
    {
        var page = GroupPage(RedPage, "/CS /DeviceRGB", "/ColorSpace << /DefaultRGB /DeviceGray >>");
        Assert.Throws<PdfParseException>(() => DocumentRenderer.RenderPage(page, 72));
    }

    [Fact]
    public void PlacedPage_ResolvesIndirectGroupAttributes()
    {
        var page = GroupPage(RedPage, null, objects:
            "5 0 obj << /S 6 0 R /I 7 0 R /CS 8 0 R >> endobj\n" +
            "6 0 obj /Transparency endobj\n7 0 obj true endobj\n8 0 obj /DeviceGray endobj\n");
        page.Dict.Entries["Group"] = new PdfRef(5, 0);
        using var raster = new SoftwareRasterizer(200, 200);
        new ContentInterpreter(page.Document.File, raster).Render(page, Matrix.Identity, asPageElement: true);
        Near(Px(new RenderedPage(200, 200, raster.ToRgba(), []), 100, 100), (77, 77, 77), 1);
    }

    [Fact]
    public void PageGroup_IgnoresAttributesOfOtherGroupSubtypes()
    {
        var page = GroupPage(RedPage, "/CS /DeviceCMYK /K true");
        ((PdfDictionary)page.Dict.Get("Group")!).Entries["S"] = new PdfName("Other");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (255, 0, 0), 0);
    }

    [Theory]
    [InlineData("q 200 0 0 200 0 0 cm /Im Do Q",
        "/XObject << /Im 5 0 R >>", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8", "\xFF\0\0")]
    [InlineData("/Sh sh",
        "/Shading << /Sh << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [1 0 0] /N 1 >> >> >>", "", "")]
    [InlineData("/F Do", "/XObject << /F 5 0 R >>",
        "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /I true >> /Resources << /ExtGState << /M << /BM /Multiply >> >> >>",
        "/M gs 1 0 0 rg 0 0 200 200 re f")]
    public void PageGroup_GrayAppliesToImagesShadersAndNestedForms(string content, string resources, string dict, string data)
    {
        var page = GroupPage(content, "/CS /DeviceGray", resources, Stream(5, dict, data));
        var p = DocumentRenderer.RenderPage(page, 72);
        Near(Px(p, 100, 100), (77, 77, 77), 1);
        Assert.Empty(p.Warnings);
    }

    [Theory]
    [InlineData("0 0 10 10 re W n")]
    [InlineData("q 0 0 10 10 re W n")]
    public void PageGroup_AnnotationsInheritPageCSButNotContentClip(string content)
    {
        var page = GroupPage(content, "/CS /DeviceGray",
            objects: Stream(5, "/Type /XObject /Subtype /Form /BBox [0 0 200 200]", RedPage),
            extraPage: "/Annots [<< /Subtype /Stamp /Rect [0 0 200 200] /AP << /N 5 0 R >> >>]");
        Near(Px(DocumentRenderer.RenderPage(page, 72), 100, 100), (77, 77, 77), 1);
    }

    [Fact]
    public void TextExtraction_DoesNotRequireAGroupColorTransform()
    {
        var page = GroupPage("BT /F 12 Tf (Hello) Tj ET", "/CS /DeviceCMYK",
            "/Font << /F << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        Assert.Equal("Hello", string.Concat(DocumentRenderer.ExtractText(page).Select(s => s.Text)));
    }

    [Fact]
    public void NonIsolatedGroup_BlendsWithBackdrop()
        => Near(Px(Render(RedPage + "/F Do", "/XObject << /F 5 0 R >>", GroupForm("", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (0, 0, 0));

    [Fact]
    public void IsolatedGroup_DoesNotSeeBackdrop()
        => Near(Px(Render(RedPage + "/F Do", "/XObject << /F 5 0 R >>", GroupForm("/I true", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (0, 255, 255));

    [Fact]
    public void GroupAlpha_AppliesToWholeGroup()
    {
        const string res = "/XObject << /F 5 0 R >> /ExtGState << /A << /ca 0.5 >> >>";
        Near(Px(Render(RedPage + "/A gs /F Do", res, GroupForm("", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (127, 0, 0));
        Near(Px(Render(RedPage + "/A gs /F Do", res, GroupForm("/I true", "/M gs 0 1 1 rg 0 0 200 200 re f")), 100, 100), (127, 127, 127));
    }

    [Fact]
    public void KnockoutGroup_LaterObjectsReplaceEarlier()
    {
        const string c = "/A gs 1 0 0 rg 0 0 120 200 re f 0 0 1 rg 80 0 120 200 re f";
        var ko = Render("/F Do", "/XObject << /F 5 0 R >>", GroupForm("/K true", c));
        Near(Px(ko, 100, 100), (127, 127, 255));
        Near(Px(ko, 20, 100), (255, 127, 127));
        var plain = Render("/F Do", "/XObject << /F 5 0 R >>", GroupForm("", c));
        Near(Px(plain, 100, 100), (127, 63, 191));
    }

    static double Lum(double[] c) => 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2];
    static double[] ClipColor(double[] c)
    {
        var l = Lum(c); var n = c.Min(); var x = c.Max();
        if (n < 0) c = c.Select(v => l + (v - l) * l / (l - n)).ToArray();
        if (x > 1) c = c.Select(v => l + (v - l) * (1 - l) / (x - l)).ToArray();
        return c;
    }
    static double[] SetLum(double[] c, double l) { var d = l - Lum(c); return ClipColor(c.Select(v => v + d).ToArray()); }
    static double Sat(double[] c) => c.Max() - c.Min();
    static double[] SetSat(double[] c, double s)
    {
        var idx = new[] { 0, 1, 2 }.OrderBy(i => c[i]).ToArray();
        var r = new double[3];
        var (mn, md, mx) = (idx[0], idx[1], idx[2]);
        if (c[mx] > c[mn]) { r[md] = (c[md] - c[mn]) * s / (c[mx] - c[mn]); r[mx] = s; }
        return r;
    }

    static double SepRef(string m, double cb, double cs)
    {
        double Screen(double a, double b) => a + b - a * b;
        double Hard(double b, double s) => s <= 0.5 ? b * 2 * s : Screen(b, 2 * s - 1);
        switch (m)
        {
            case "Multiply": return cb * cs;
            case "Screen": return Screen(cb, cs);
            case "Overlay": return Hard(cs, cb);
            case "Darken": return Math.Min(cb, cs);
            case "Lighten": return Math.Max(cb, cs);
            case "ColorDodge": return cb == 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs));
            case "ColorBurn": return cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs);
            case "HardLight": return Hard(cb, cs);
            case "SoftLight":
                if (cs <= 0.5) return cb - (1 - 2 * cs) * cb * (1 - cb);
                var d = cb <= 0.25 ? ((16 * cb - 12) * cb + 4) * cb : Math.Sqrt(cb);
                return cb + (2 * cs - 1) * (d - cb);
            case "Difference": return Math.Abs(cb - cs);
            case "Exclusion": return cb + cs - 2 * cb * cs;
            default: return cs;
        }
    }

    [Theory]
    [InlineData("Normal")] [InlineData("Multiply")] [InlineData("Screen")] [InlineData("Overlay")] [InlineData("Darken")]
    [InlineData("Lighten")] [InlineData("ColorDodge")] [InlineData("ColorBurn")] [InlineData("HardLight")] [InlineData("SoftLight")]
    [InlineData("Difference")] [InlineData("Exclusion")] [InlineData("Hue")] [InlineData("Saturation")] [InlineData("Color")] [InlineData("Luminosity")]
    public void BlendMode_MatchesSpecFormula(string mode)
    {
        double[] cb = [0.4, 0.6, 0.2], cs = [0.7, 0.3, 0.9];
        double[] e = mode switch
        {
            "Hue" => SetLum(SetSat(cs, Sat(cb)), Lum(cb)),
            "Saturation" => SetLum(SetSat(cb, Sat(cs)), Lum(cb)),
            "Color" => SetLum(cs, Lum(cb)),
            "Luminosity" => SetLum(cb, Lum(cs)),
            _ => [SepRef(mode, cb[0], cs[0]), SepRef(mode, cb[1], cs[1]), SepRef(mode, cb[2], cs[2])],
        };
        var p = Render("0.4 0.6 0.2 rg 0 0 200 200 re f /B gs 0.7 0.3 0.9 rg 0 0 200 200 re f", $"/ExtGState << /B << /BM /{mode} >> >>");
        Near(Px(p, 100, 100), ((int)Math.Round(e[0] * 255), (int)Math.Round(e[1] * 255), (int)Math.Round(e[2] * 255)), 2);
    }

    [Fact]
    public void BlendModeArray_UsesFirstSupported()
    {
        var p = Render("1 0 0 rg 0 0 200 200 re f /B gs 0 1 1 rg 0 0 200 200 re f", "/ExtGState << /B << /BM [/Foo /Multiply] >> >>");
        Near(Px(p, 100, 100), (0, 0, 0));
    }

    const string Inv = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";

    static string Mask(string form, string extra) => "/ExtGState << /G1 << /SMask << /Type /Mask /S /Luminosity /G 5 0 R " + extra + " >> >> >>";

    static string MaskForm(string content)
        => Stream(5, "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >>", content);

    [Fact]
    public void SoftMask_TransferFunction_Inverts()
    {
        const string fill = "/G1 gs 1 0 0 rg 0 0 200 200 re f";
        Near(Px(Render(fill, Mask("", ""), MaskForm("0.25 g 0 0 200 200 re f")), 100, 100), (255, 191, 191));
        Near(Px(Render(fill, Mask("", "/TR " + Inv), MaskForm("0.25 g 0 0 200 200 re f")), 100, 100), (255, 64, 64));
    }

    [Fact]
    public void SoftMask_TransferIdentityNameIsIgnored()
        => Near(Px(Render("/G1 gs 1 0 0 rg 0 0 200 200 re f", Mask("", "/TR /Identity"), MaskForm("0.25 g 0 0 200 200 re f")), 100, 100), (255, 191, 191));

    [Fact]
    public void SoftMask_BackdropColourFillsOutsideGroup()
    {
        const string fill = "/G1 gs 1 0 0 rg 0 0 200 200 re f";
        const string half = "1 g 0 0 100 200 re f";
        var def = Render(fill, Mask("", ""), MaskForm(half));
        Near(Px(def, 50, 100), (255, 0, 0));
        Near(Px(def, 150, 100), (255, 255, 255));
        Near(Px(Render(fill, Mask("", "/BC [0.5]"), MaskForm(half)), 150, 100), (255, 128, 128));
        Near(Px(Render(fill, Mask("", "/TR " + Inv), MaskForm(half)), 150, 100), (255, 0, 0));
    }

    // ---- mesh shadings 4-7 ----

    static string MeshStream(int type, string data, string extra = "")
        => Stream(5, $"/ShadingType {type} /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1 0 1 0 1] {extra}", data);

    static RenderedPage Mesh(string obj) => Render("/M sh", "/Shading << /M 5 0 R >>", obj, "[0 0 255 255]");

    static string V(int x, int y, string rgb) => $"{(char)x}{(char)y}{rgb}";
    const string R = "\xFF\0\0", G = "\0\xFF\0", B = "\0\0\xFF", Y = "\xFF\xFF\0";

    [Fact]
    public void MeshType5_Lattice_TwoAndThreeVerticesPerRow()
    {
        var two = Mesh(MeshStream(5, V(0, 0, R) + V(255, 0, G) + V(0, 255, B) + V(255, 255, Y), "/VerticesPerRow 2"));
        var (r, g, b, y) = (Px(two, 3, 252), Px(two, 252, 252), Px(two, 3, 3), Px(two, 252, 3));
        Assert.True(r.R > 230 && r.G < 25 && r.B < 25, r.ToString());
        Assert.True(g.G > 230 && g.R < 25, g.ToString());
        Assert.True(b.B > 230 && b.R < 25, b.ToString());
        Assert.True(y.R > 230 && y.G > 230 && y.B < 25, y.ToString());
        // Type 5 is Gouraud-triangulated per cell, so the centre lies on a triangle diagonal (no bilinear average).
        Assert.True(Px(two, 127, 127).R + Px(two, 127, 127).G + Px(two, 127, 127).B > 100);

        var three = Mesh(MeshStream(5, V(0, 0, R) + V(128, 0, "\0\0\0") + V(255, 0, G) + V(0, 255, B) + V(128, 255, "\0\0\0") + V(255, 255, Y), "/VerticesPerRow 3"));
        Assert.True(Px(three, 3, 252).R > 230);
        Assert.True(Px(three, 252, 252).G > 230);
        var mid = Px(three, 128, 252); // black middle vertex on the bottom row
        Assert.True(mid.R < 40 && mid.G < 40, mid.ToString());
    }

    [Fact]
    public void MeshType5_NonByteAlignedVerticesAreAlignedPerVertex()
    {
        // 12-bit coordinates and 4-bit components: a vertex is 12+12+12 = 36 bits, padded to 40.
        static string Vtx(int x, int y, int r, int g, int b)
        {
            ulong bits = ((ulong)x << 24) | ((ulong)y << 12) | (ulong)(r << 8 | g << 4 | b);
            bits <<= 4;
            var sb = new StringBuilder();
            for (var i = 4; i >= 0; i--) sb.Append((char)((bits >> (i * 8)) & 0xFF));
            return sb.ToString();
        }
        var data = Vtx(0, 0, 15, 0, 0) + Vtx(4095, 0, 0, 15, 0) + Vtx(0, 4095, 0, 0, 15) + Vtx(4095, 4095, 15, 15, 0);
        var obj = Stream(5, "/ShadingType 5 /ColorSpace /DeviceRGB /BitsPerCoordinate 12 /BitsPerComponent 4 /VerticesPerRow 2 /Decode [0 255 0 255 0 1 0 1 0 1]", data);
        var p = Mesh(obj);
        Assert.True(Px(p, 3, 252).R > 220 && Px(p, 3, 252).G < 40);
        Assert.True(Px(p, 252, 252).G > 220 && Px(p, 252, 252).R < 40);
        Assert.True(Px(p, 3, 3).B > 220);
    }

    static string PatchPoints()
    {
        (int, int)[] order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)];
        var sb = new StringBuilder();
        foreach (var (i, j) in order) sb.Append((char)(j * 85)).Append((char)(i * 85));
        return sb.ToString();
    }

    [Fact]
    public void MeshType7_TensorPatch()
    {
        var p = Mesh(MeshStream(7, "\0" + PatchPoints() + R + G + B + Y));
        Assert.True(Px(p, 3, 252).R > 230 && Px(p, 3, 252).G < 25);
        Assert.True(Px(p, 252, 252).G > 230);
        Near(Px(p, 127, 127), (127, 127, 64), 6);
    }

    [Fact]
    public void MeshType7_EdgeContinuationSharesColoursAndEdge()
    {
        // Patch 1 spans x 0..126; patch 2 (f=1) reuses its right edge and adds the far side.
        (int, int)[] order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)];
        var sb = new StringBuilder("\0");
        foreach (var (i, j) in order) sb.Append((char)(j * 42)).Append((char)(i * 85));
        sb.Append(R + G + B + Y);
        // f=1: new patch starts from edge (p03,p13,p23,p33) of the previous patch; supply the remaining 12 points and 2 colours.
        sb.Append('\x01');
        (int, int)[] rest = [(0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (1, 1), (1, 2), (2, 2)];
        foreach (var (i, j) in rest) sb.Append((char)(126 + j * 42)).Append((char)(i * 85));
        sb.Append((char)(126 + 105)).Append((char)(170)); // p21 interior point
        sb.Append("\0\xFF\xFF" + "\xFF\0\xFF");
        var p = Mesh(MeshStream(7, sb.ToString()));
        Assert.True(Px(p, 3, 252).R > 230);                     // patch 1 c00 red
        Assert.NotEqual((255, 255, 255), Px(p, 200, 128));      // patch 2 painted
        var l = Px(p, 120, 128); var r = Px(p, 132, 128);
        Assert.True(Math.Abs(l.R - r.R) < 40 && Math.Abs(l.G - r.G) < 40 && Math.Abs(l.B - r.B) < 40, $"{l} vs {r}"); // colour continuous over shared edge
    }

    [Fact]
    public void MeshType4_EdgeFlags_ShareVertices()
    {
        // Triangle A (f=0) then triangle B (f=1) reusing vb,vc of A plus a new vertex.
        var data = "\0" + V(0, 0, R) + "\0" + V(255, 0, G) + "\0" + V(0, 255, B) + "\x01" + V(255, 255, Y);
        var obj = MeshStream(4, data);
        var p = Mesh(obj);
        Assert.True(Px(p, 3, 252).R > 230);
        Assert.True(Px(p, 252, 3).R > 230 && Px(p, 252, 3).G > 230); // yellow corner from triangle B
        Assert.True(Px(p, 252, 252).G > 230);
    }

    [Fact]
    public void MeshWithFunction_MapsParameterThroughFunction()
    {
        var fn = "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";
        var data = "\0" + "\0\0\0" + "\0" + "\xFF\0\xFF" + "\0" + "\0\xFF\0";
        var obj = Stream(5, $"/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1] {fn}", data);
        var p = Mesh(obj);
        Assert.True(Px(p, 3, 252).R > 200 && Px(p, 3, 252).B < 60);
        Assert.True(Px(p, 252, 252).B > 200 && Px(p, 252, 252).R < 60);
    }
}
