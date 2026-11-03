using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAImageInterpolationTests
{
    public static IEnumerable<object[]> Flags()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var flag in new[] { "", "/Interpolate false", "/Interpolate true", "/Interpolate 6 0 R", "/Interpolate null" })
                yield return [profile, flag, flag is "/Interpolate true" or "/Interpolate 6 0 R"];
    }

    static PdfDocument Load(string flag, string subtype = "Image", bool mask = false) => PdfDocument.Load(ObjPdf.Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /XObject << /Im 5 0 R >> >> >>",
        ObjPdf.Stream("q 10 0 0 10 0 0 cm /Im Do Q"),
        ObjPdf.Stream(mask ? "x" : "abc", $"/Type /XObject /Subtype /{subtype} /Width 1 /Height 1 {flag} " +
            (mask ? "/ImageMask true /BitsPerComponent 1" : "/BitsPerComponent 8 /ColorSpace [/CalRGB << /WhitePoint [0.9505 1 1.089] >>]")),
        "true"));

    [Theory]
    [MemberData(nameof(Flags))]
    public void ExplicitTrueImageInterpolationIsNotPermittedInArchivalProfiles(PdfValidationProfile profile, string flag, bool invalid)
    {
        var report = PdfValidator.Validate(Load(flag), profile);

        Assert.False(report.Has("VALIDATION-INTERNAL"));
        var findings = report.Errors.Where(f => f.RuleId == "PDFA-IMAGE-INTERPOLATE").ToList();
        Assert.Equal(invalid ? 1 : 0, findings.Count);
        if (invalid)
            Assert.Equal(profile == PdfValidationProfile.PdfA1b ? "ISO 19005-1 6.2.4"
                : profile == PdfValidationProfile.PdfA2b ? "ISO 19005-2 6.2.8" : "ISO 19005-3 6.2.8", findings[0].Clause);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void ImageMasksAreSubjectToTheSamePolicy(PdfValidationProfile profile)
    {
        Assert.True(PdfValidator.Validate(Load("/Interpolate true", mask: true), profile).Has("PDFA-IMAGE-INTERPOLATE"));
    }

    [Fact]
    public void OtherXObjectSubtypesDoNotAcquireAnImagePolicy()
    {
        Assert.False(PdfValidator.Validate(Load("/Interpolate true", "Form"), PdfValidationProfile.PdfA2b).Has("PDFA-IMAGE-INTERPOLATE"));
    }

    [Fact]
    public void PrivateNonStreamDictionariesAreNotImageXObjects()
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /Private 3 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
            "<< /Subtype /Image /Interpolate true >>"));

        Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfA2b).Has("PDFA-IMAGE-INTERPOLATE"));
    }
}
