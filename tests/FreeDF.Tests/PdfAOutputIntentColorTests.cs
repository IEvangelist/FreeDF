using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAOutputIntentColorTests
{
    static PdfDocument Load(string content, string signature, string resources = "", params string[] extras)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /OutputIntents [5 0 R] >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << {resources} >> >>",
            ObjPdf.Stream(content),
            "<< /Type /OutputIntent /S /GTS_PDFA1 /DestOutputProfile 6 0 R >>",
            ObjPdf.Stream(PdfADeviceColorTests.Icc(signature), $"/N {(signature == "GRAY" ? 1 : signature == "RGB " ? 3 : 4)}"),
        };
        objects.AddRange(extras);
        return PdfDocument.Load(ObjPdf.Build(objects.ToArray()));
    }

    public static IEnumerable<object[]> Colors()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var paint in new[] { "GRAY", "RGB ", "CMYK" })
                foreach (var output in new[] { "GRAY", "RGB ", "CMYK" })
                    yield return [profile, paint, output, paint != "GRAY" && paint != output];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedFormsAreRescannedUnderDifferentInheritedResourceContexts(bool mappedFirst)
    {
        const string mapped = "/ColorSpace << /DefaultRGB [/CalRGB << /WhitePoint [0.9505 1 1.089] >>] >>";
        const string children = "/XObject << /Inner 9 0 R /Im 10 0 R >>";
        var doc = Load("/A Do /B Do", "CMYK", "/XObject << /A 7 0 R /B 8 0 R >>",
            ObjPdf.Stream("/Inner Do", $"/Subtype /Form /BBox [0 0 10 10] /Resources << {(mappedFirst ? mapped : "")} {children} >>"),
            ObjPdf.Stream("/Inner Do", $"/Subtype /Form /BBox [0 0 10 10] /Resources << {(mappedFirst ? "" : mapped)} {children} >>"),
            ObjPdf.Stream("/Im Do", "/Subtype /Form /BBox [0 0 10 10]"),
            ObjPdf.Stream("abc", "/Subtype /Image /ColorSpace /DeviceRGB /Width 1 /Height 1 /BitsPerComponent 8"));
        Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-COLORSPACE"));
    }

    [Theory]
    [MemberData(nameof(Colors))]
    public void GrayAcceptsAnyPdfAIntentButRgbAndCmykRequireMatchingProfiles(PdfValidationProfile profile, string paint, string output, bool mismatch)
    {
        var op = paint switch { "GRAY" => "0 g", "RGB " => "1 0 0 rg", _ => "0 1 0 0 k" };
        var report = PdfValidator.Validate(Load(op + " 0 0 10 10 re f", output), profile);

        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-OUTPUTINTENT-MISSING"));
        Assert.Equal(mismatch, report.Has("PDFA-OUTPUTINTENT-COLORSPACE"));
        foreach (var finding in report.Findings.Where(f => f.RuleId == "PDFA-OUTPUTINTENT-COLORSPACE"))
            Assert.Equal(profile == PdfValidationProfile.PdfA1b ? "ISO 19005-1 6.2.3.3"
                : profile == PdfValidationProfile.PdfA2b ? "ISO 19005-2 6.2.4.3" : "ISO 19005-3 6.2.4.3", finding.Clause);
    }

    [Theory]
    [InlineData("/ColorSpace << /Unused /DeviceRGB >>", "0 1 0 0 k 0 0 10 10 re f")]
    [InlineData("", "1 0 0 rg")]
    [InlineData("", "1 0 0 rg BT 3 Tr (invisible) Tj ET")]
    public void UnusedResourcesAndNonpaintingColorsDoNotCreateProfileMismatches(string resources, string content)
    {
        Assert.False(PdfValidator.Validate(Load(content, "CMYK", resources), PdfValidationProfile.PdfA2b)
            .Has("PDFA-OUTPUTINTENT-COLORSPACE"));
    }

    [Theory]
    [InlineData("0 1 0 0 k q 1 0 0 rg Q 0 0 10 10 re f", "CMYK", false)]
    [InlineData("0 1 0 0 k q 1 0 0 rg Q 0 0 10 10 re f", "RGB ", true)]
    [InlineData("1 0 0 rg 0 1 0 0 K BT 2 Tr (both) Tj ET", "RGB ", true)]
    [InlineData("1 0 0 rg 0 1 0 0 K BT 0 Tr (fill) Tj ET", "RGB ", false)]
    public void PaintingUsesRestoredColorAndTheTextRenderingMode(string content, string output, bool mismatch)
    {
        Assert.Equal(mismatch, PdfValidator.Validate(Load(content, output), PdfValidationProfile.PdfA2b)
            .Has("PDFA-OUTPUTINTENT-COLORSPACE"));
    }

    [Fact]
    public void FormInvocationMemoizationDistinguishesRgbAndCmyk()
    {
        var doc = Load("1 0 0 rg /Fm Do 0 1 0 0 k /Fm Do", "RGB ", "/XObject << /Fm 7 0 R >>",
            ObjPdf.Stream("0 0 10 10 re f", "/Subtype /Form /BBox [0 0 10 10]"));
        Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-COLORSPACE"));
    }

    [Theory]
    [InlineData("/Subtype /Image /ColorSpace /DeviceCMYK /BitsPerComponent 8", false)]
    [InlineData("/Subtype /Image /ImageMask true /BitsPerComponent 1", true)]
    public void ImagesUseTheirOwnSpaceAndStencilsUseTheCurrentFill(string image, bool stencil)
    {
        var doc = Load("0 1 0 0 k /Im Do", "RGB ", "/XObject << /Im 7 0 R >>",
            ObjPdf.Stream(stencil ? "x" : "abcd", image + " /Width 1 /Height 1"));
        Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-COLORSPACE"));
    }

    [Fact]
    public void ValidReplacementMakesRgbIndependentOfTheOutputProfile()
    {
        var doc = Load("1 0 0 rg 0 0 10 10 re f", "CMYK",
            "/ColorSpace << /DefaultRGB [/CalRGB << /WhitePoint [0.9505 1 1.089] >>] >>");
        Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-COLORSPACE"));
    }
}
