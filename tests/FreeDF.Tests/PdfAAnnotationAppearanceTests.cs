using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationAppearanceTests
{
    public static IEnumerable<object[]> Appearances()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subtype in new[] { "Square", "Link", "Popup" })
                foreach (var rect in new[] { "[10 10 20 20]", "[10 10 10 10]", "[10 10 10 20]", "[10 10 20 10]", "[20 20 10 10]" })
                    foreach (var indirect in new[] { false, true })
                        foreach (var value in new[] { "", "null", "<< >>", "<< /N 7 0 R >>", "7 0 R" })
                            yield return [profile, subtype, rect, indirect, value,
                                profile != PdfValidationProfile.PdfA1b && subtype == "Square"
                                    && rect != "[10 10 10 10]" && value is not ("<< >>" or "<< /N 7 0 R >>")];
    }

    [Theory]
    [MemberData(nameof(Appearances))]
    public void PartsTwoAndThreeRequireAppearancesExceptForLinksPopupsAndPointRectangles(
        PdfValidationProfile profile, string subtype, string rect, bool indirect, string value, bool missing)
    {
        var entry = value.Length == 0 ? "" : "/AP " + (indirect ? "6 0 R" : value);
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Type /Annot /Subtype /{subtype} /Rect {rect} /F 4 /Contents (note) {entry} >>",
            value.Length == 0 ? "null" : value,
            ObjPdf.Stream("", "/Type /XObject /Subtype /Form /BBox [0 0 10 10]")));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(missing, report.Has("PDFA-ANNOT-APPEARANCE-MISSING"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ANNOT-APPEARANCE-MISSING"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile == PdfValidationProfile.PdfA2b
                ? "ISO 19005-2 6.3.3" : "ISO 19005-3 6.3.3", finding.Clause);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA2b, 10, 10, false)]
    [InlineData(PdfValidationProfile.PdfA2b, 10, 20, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 20, 10, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 20, 20, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 10, 10, false)]
    [InlineData(PdfValidationProfile.PdfA3b, 10, 20, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 20, 10, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 20, 20, true)]
    public void PointRectangleExceptionResolvesArrayAndCoordinates(PdfValidationProfile profile,
        int x1, int y1, bool missing)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            "<< /Type /Annot /Subtype /Square /Rect 6 0 R /F 4 >>",
            "[7 0 R 8 0 R 9 0 R 10 0 R]",
            "10", "10", x1.ToString(), y1.ToString()));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(missing, report.Has("PDFA-ANNOT-APPEARANCE-MISSING"));
    }
}
