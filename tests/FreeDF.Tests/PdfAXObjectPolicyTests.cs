using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAXObjectPolicyTests
{
    static PdfDocument Load(string entry, string subtype = "Image", bool mask = false, string reference = "8") =>
        PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Contents 4 0 R /Resources << /XObject << /Im 5 0 R >> /ColorSpace << /DefaultGray [/CalGray << /WhitePoint [0.9505 1 1.089] >>] /DefaultRGB [/CalRGB << /WhitePoint [0.9505 1 1.089] >>] >> >> >>",
            ObjPdf.Stream("/Im Do"),
            ObjPdf.Stream(subtype == "Form" ? "q Q" : "abcd", $"/Type /XObject /Subtype /{subtype} /Width 1 /Height 1 /BBox [0 0 1 1] {entry} "
                + (mask ? "/ImageMask true" : "/ColorSpace [/CalRGB << /WhitePoint [0.9505 1 1.089] >>]")),
            reference));

    public static IEnumerable<object[]> Bits()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var mask in new[] { false, true })
                foreach (var value in new[] { "", "null", "1", "2", "4", "8", "16", "3", "0", "-1", "8.0", "(8)", "6 0 R" })
                {
                    var resolved = value == "6 0 R" ? "8" : value;
                    var present = resolved is not ("" or "null");
                    var valid = resolved is "1" or "2" or "4" or "8" || resolved == "16" && profile != PdfValidationProfile.PdfA1b;
                    yield return [profile, mask, value, present && !valid, present && mask && resolved != "1"];
                }
    }

    [Theory]
    [MemberData(nameof(Bits))]
    public void ImageAndMaskBitDepthsFollowTheSelectedProfile(PdfValidationProfile profile, bool mask, string value, bool invalidBits, bool invalidMask)
    {
        var report = PdfValidator.Validate(Load(value == "" ? "" : "/BitsPerComponent " + value, mask: mask), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalidBits, report.Has("PDFA-IMAGE-BPC"));
        Assert.Equal(invalidMask, report.Has("PDFA-IMAGE-MASK-BPC"));
        foreach (var finding in report.Findings.Where(f => f.RuleId is "PDFA-IMAGE-BPC" or "PDFA-IMAGE-MASK-BPC"))
            Assert.Equal(ImageClause(profile), finding.Clause);
    }

    static string ImageClause(PdfValidationProfile profile) => profile == PdfValidationProfile.PdfA1b
        ? "ISO 19005-1 6.2.4" : profile == PdfValidationProfile.PdfA2b ? "ISO 19005-2 6.2.8" : "ISO 19005-3 6.2.8";

    public static IEnumerable<object[]> Entries()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subtype in new[] { "Image", "Form" })
                foreach (var key in new[] { "Alternates", "OPI", "PS", "Subtype2" })
                    foreach (var value in new[] { "null", "6 0 R", key == "Subtype2" ? "/PS" : "<< >>" })
                        yield return [profile, subtype, key, value];
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void DictionaryBansRespectSubtypeAndNullSemantics(PdfValidationProfile profile, string subtype, string key, string value)
    {
        var report = PdfValidator.Validate(Load($"/{key} {value}", subtype), profile);
        var present = value != "null";
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(present && subtype == "Image" && key == "Alternates", report.Has("PDFA-IMAGE-ALTERNATES"));
        Assert.Equal(present && subtype == "Image" && key == "OPI", report.Has("PDFA-IMAGE-OPI"));
        Assert.Equal(present && subtype == "Form" && key == "OPI", report.Has("PDFA-FORM-OPI"));
        Assert.Equal(subtype == "Form" && (key == "PS" && present || key == "Subtype2" && value == "/PS"), report.Has("PDFA-FORM-POSTSCRIPT"));
        foreach (var finding in report.Findings.Where(f => f.RuleId is "PDFA-IMAGE-ALTERNATES" or "PDFA-IMAGE-OPI" or "PDFA-FORM-OPI" or "PDFA-FORM-POSTSCRIPT"))
        {
            var clause = subtype == "Image" ? ImageClause(profile) : profile == PdfValidationProfile.PdfA1b
                ? "ISO 19005-1 " + (key == "OPI" ? "6.2.4" : "6.2.5")
                : profile == PdfValidationProfile.PdfA2b ? "ISO 19005-2 6.2.9" : "ISO 19005-3 6.2.9";
            Assert.Equal(clause, finding.Clause);
        }
    }

    [Fact]
    public void IndirectPostScriptSubtypeIsRecognized()
    {
        Assert.True(PdfValidator.Validate(Load("/Subtype2 6 0 R", "Form", reference: "/PS"), PdfValidationProfile.PdfA2b)
            .Has("PDFA-FORM-POSTSCRIPT"));
    }

    [Theory]
    [InlineData("Image", "Alternates", "PDFA-IMAGE-ALTERNATES")]
    [InlineData("Image", "OPI", "PDFA-IMAGE-OPI")]
    [InlineData("Form", "OPI", "PDFA-FORM-OPI")]
    [InlineData("Form", "PS", "PDFA-FORM-POSTSCRIPT")]
    [InlineData("Form", "Subtype2", "PDFA-FORM-POSTSCRIPT")]
    [InlineData("Image", "BitsPerComponent", "PDFA-IMAGE-BPC")]
    [InlineData("Image", "BitsPerComponent", "PDFA-IMAGE-MASK-BPC")]
    public void IndirectNullEntriesRemainAbsent(string subtype, string key, string rule)
    {
        Assert.False(PdfValidator.Validate(Load($"/{key} 6 0 R", subtype, mask: key == "BitsPerComponent", reference: "null"),
            PdfValidationProfile.PdfA2b).Has(rule));
    }
}
