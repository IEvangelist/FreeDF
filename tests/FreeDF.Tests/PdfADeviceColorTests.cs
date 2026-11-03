using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfADeviceColorTests
{
    const string PdfAId = "http://www.aiim.org/pdfa/ns/id/";
    const string CalGray = "[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 2.2 >>]";

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "validation", "pdfa-color", name);

    static byte[] Pdf(string content, int part = 2, string resources = "", string catalogExtra = "", params string[] extras)
    {
        var bodies = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R {catalogExtra} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << {resources} >> >>",
            ObjPdf.Stream(content),
            ObjPdf.Xmp($"<rdf:Description rdf:about='' xmlns:pdfaid='{PdfAId}' pdfaid:part='{part}' pdfaid:conformance='B'/>"),
        };
        bodies.AddRange(extras);
        return ObjPdf.Build(bodies.ToArray());
    }

    static ValidationReport Validate(byte[] pdf, PdfValidationProfile profile) =>
        PdfValidator.Validate(PdfDocument.Load(pdf), profile);

    static string Icc(string colorSpace)
    {
        var bytes = new char[132];
        bytes[3] = (char)bytes.Length;
        bytes[8] = (char)2;
        bytes[9] = (char)0x20;
        "mntr".CopyTo(0, bytes, 12, 4);
        colorSpace.CopyTo(0, bytes, 16, 4);
        "XYZ ".CopyTo(0, bytes, 20, 4);
        "acsp".CopyTo(0, bytes, 36, 4);
        return new string(bytes);
    }

    static string Intent => "/OutputIntents [<< /Type /OutputIntent /S /GTS_PDFA1 /DestOutputProfile 6 0 R >>]";

    [Theory]
    [InlineData("calrgb-pdfa-1b.pdf", PdfValidationProfile.PdfA1b)]
    [InlineData("calrgb-pdfa-2b.pdf", PdfValidationProfile.PdfA2b)]
    [InlineData("calrgb-pdfa-3b.pdf", PdfValidationProfile.PdfA3b)]
    [InlineData("default-gray-pdfa-1b.pdf", PdfValidationProfile.PdfA1b)]
    [InlineData("default-gray-pdfa-2b.pdf", PdfValidationProfile.PdfA2b)]
    [InlineData("default-gray-pdfa-3b.pdf", PdfValidationProfile.PdfA3b)]
    [InlineData("text-default-gray-pdfa-1b.pdf", PdfValidationProfile.PdfA1b)]
    [InlineData("text-default-gray-pdfa-2b.pdf", PdfValidationProfile.PdfA2b)]
    [InlineData("text-default-gray-pdfa-3b.pdf", PdfValidationProfile.PdfA3b)]
    public void VeraPdfAcceptedColorFixturesPassImplementedRules(string name, PdfValidationProfile profile)
    {
        var report = Validate(File.ReadAllBytes(Fixture(name)), profile);
        Assert.True(report.IsCompliant, string.Join(Environment.NewLine, report.Findings));
    }

    [Theory]
    [InlineData("implicit-gray-pdfa-1b.pdf", PdfValidationProfile.PdfA1b)]
    [InlineData("implicit-gray-pdfa-2b.pdf", PdfValidationProfile.PdfA2b)]
    [InlineData("implicit-gray-pdfa-3b.pdf", PdfValidationProfile.PdfA3b)]
    [InlineData("explicit-gray-pdfa-1b.pdf", PdfValidationProfile.PdfA1b)]
    [InlineData("explicit-gray-pdfa-2b.pdf", PdfValidationProfile.PdfA2b)]
    [InlineData("explicit-gray-pdfa-3b.pdf", PdfValidationProfile.PdfA3b)]
    [InlineData("text-gray-pdfa-1b.pdf", PdfValidationProfile.PdfA1b)]
    [InlineData("text-gray-pdfa-2b.pdf", PdfValidationProfile.PdfA2b)]
    [InlineData("text-gray-pdfa-3b.pdf", PdfValidationProfile.PdfA3b)]
    public void UnmappedDeviceGrayPaintingRequiresAnOutputIntent(string name, PdfValidationProfile profile)
        => Assert.True(Validate(File.ReadAllBytes(Fixture(name)), profile).Has("PDFA-OUTPUTINTENT-MISSING"));

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b, 1)]
    [InlineData(PdfValidationProfile.PdfA2b, 2)]
    [InlineData(PdfValidationProfile.PdfA3b, 3)]
    public void CalibratedDefaultGrayCoversInitialAndExplicitGrayPainting(PdfValidationProfile profile, int part)
    {
        const string resources = $"/ColorSpace << /DefaultGray {CalGray} >>";
        const string path = "10 10 80 80 re f";

        Assert.False(Validate(Pdf(path, part, resources), profile).Has("PDFA-OUTPUTINTENT-MISSING"));
        Assert.False(Validate(Pdf("0 g " + path, part, resources), profile).Has("PDFA-OUTPUTINTENT-MISSING"));
        Assert.True(Validate(Pdf(path, part, "/ColorSpace << /DefaultGray /DeviceGray >>"), profile).Has("PDFA-OUTPUTINTENT-MISSING"));
    }

    [Theory]
    [InlineData("/WhitePoint [0.9505 1]")]
    [InlineData("/WhitePoint [0.9505 1 1.089 2]")]
    [InlineData("/WhitePoint [0.9505 0.99 1.089]")]
    [InlineData("/WhitePoint [0.9505 (1) 1.089]")]
    [InlineData("/WhitePoint [0.9505 1 1.089] /BlackPoint [-0.01 0 0]")]
    [InlineData("/WhitePoint [0.9505 1 1.089] /BlackPoint [0 0]")]
    [InlineData("/WhitePoint [0.9505 1 1.089] /BlackPoint [0 (0) 0]")]
    [InlineData("/WhitePoint [0.9505 1 1.089] /Gamma 0")]
    [InlineData("/WhitePoint [0.9505 1 1.089] /Gamma (2)")]
    public void InvalidCalGrayDefaultDoesNotReplaceDeviceGray(string parameters)
    {
        var resources = $"/ColorSpace << /DefaultGray [/CalGray << {parameters} >>] >>";
        Assert.True(Validate(Pdf("10 10 80 80 re f", resources: resources), PdfValidationProfile.PdfA2b)
            .Has("PDFA-OUTPUTINTENT-MISSING"));
    }

    [Fact]
    public void CalGrayDefaultAcceptsPositiveGammaBelowOneAndNonnegativeBlackPoint()
    {
        const string resources = "/ColorSpace << /DefaultGray [/CalGray << /WhitePoint [0.9505 1 1.089] /BlackPoint [0.2 0.1 0.3] /Gamma 0.5 >>] >>";
        Assert.False(Validate(Pdf("10 10 80 80 re f", resources: resources), PdfValidationProfile.PdfA2b)
            .Has("PDFA-OUTPUTINTENT-MISSING"));
    }

    [Theory]
    [InlineData("1 2", false)]
    [InlineData("1 2 3", true)]
    public void ContentScanTokenLimitReportsOnlyWhenContentRemains(string content, bool expectedIncomplete)
    {
        var context = new ValidationContext(PdfDocument.Load(Pdf(content)), PdfValidationProfile.PdfA2b, contentScanTokenLimit: 2);
        context.EnsureWalked();

        var finding = context.Findings.SingleOrDefault(f => f.RuleId == "PDFA-CONTENT-SCAN-INCOMPLETE");
        Assert.Equal(expectedIncomplete, finding is not null);
        if (finding is not null)
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void ImplicitGrayTextPaintingRequiresDeviceIndependentGrayOrGrayOutputIntent()
    {
        Assert.True(Validate(Pdf("BT (text) Tj ET"), PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));
        Assert.False(Validate(Pdf("BT 3 Tr (text) Tj ET"), PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));

        var grayIntent = Pdf("10 10 80 80 re f", catalogExtra: Intent, extras: ObjPdf.Stream(Icc("GRAY"), "/N 1"));
        Assert.False(Validate(grayIntent, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));

        var rgbIntent = Pdf("10 10 80 80 re f", catalogExtra: Intent, extras: ObjPdf.Stream(Icc("RGB "), "/N 3"));
        Assert.True(Validate(rgbIntent, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));

        var rgbPainting = Pdf("1 0 0 rg 10 10 80 80 re f", catalogExtra: Intent, extras: ObjPdf.Stream(Icc("RGB "), "/N 3"));
        Assert.False(Validate(rgbPainting, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));
    }

    [Fact]
    public void FormPaintingUsesInvocationColorAndItsOwnDefaultGray()
    {
        const string formContent = "10 10 80 80 re f";
        const string formXObject = "/Type /XObject /Subtype /Form /BBox [0 0 100 100]";
        const string formResource = "/XObject << /Fm 6 0 R >>";

        var inheritedGray = Pdf("/Fm Do", resources: formResource, extras: ObjPdf.Stream(formContent, formXObject));
        Assert.True(Validate(inheritedGray, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));

        var inheritedRgb = Pdf("1 0 0 rg /Fm Do", resources: "/XObject << /Fm 7 0 R >>", catalogExtra: Intent,
            extras: [ObjPdf.Stream(Icc("RGB "), "/N 3"), ObjPdf.Stream(formContent, formXObject)]);
        Assert.False(Validate(inheritedRgb, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));

        var calibratedGray = Pdf("/Fm Do", resources: formResource, extras: ObjPdf.Stream("0 g " + formContent,
            formXObject + $" /Resources << /ColorSpace << /DefaultGray {CalGray} >> >>"));
        Assert.False(Validate(calibratedGray, PdfValidationProfile.PdfA2b).Has("PDFA-OUTPUTINTENT-MISSING"));
    }
}
