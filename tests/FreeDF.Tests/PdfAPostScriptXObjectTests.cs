using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAPostScriptXObjectTests
{
    public static IEnumerable<object[]> Objects()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var (indirect, indirectType) in new[] { (false, false), (false, true), (true, false), (true, true) })
                foreach (var exposed in new[] { false, true })
                    foreach (var (type, subtype, stream, invalid) in new (string, string, bool, bool)[]
                    {
                        ("/XObject", "/PS", true, true),
                        ("/XObject", "/Form", true, false),
                        ("/XObject", "/Image", true, false),
                        ("/XObject", "null", true, false),
                        ("/XObject", "(PS)", true, false),
                        ("/Other", "/PS", true, false),
                        ("null", "/PS", true, false),
                        ("/XObject", "/PS", false, false),
                    })
                        yield return [profile, indirect, indirectType, exposed, type, subtype, stream, invalid];
    }

    [Theory]
    [MemberData(nameof(Objects))]
    public void StandalonePostScriptXObjectsAreForbiddenEvenWhenNotPainted(PdfValidationProfile profile,
        bool indirect, bool indirectType, bool exposed, string type, string subtype, bool stream, bool invalid)
    {
        var entries = $"/Type {(indirectType ? "7 0 R" : type)} /Subtype {(indirect ? "6 0 R" : subtype)} /BBox [0 0 1 1] /Width 1 /Height 1 /BitsPerComponent 8";
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << "
                + (exposed ? "/XObject << /PS0 5 0 R >>" : "") + " >> >>",
            ObjPdf.Stream(exposed ? "/PS0 Do" : ""),
            stream ? ObjPdf.Stream("inert", entries) : "<< " + entries + " >>",
            subtype,
            type));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA-XOBJECT-POSTSCRIPT"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-XOBJECT-POSTSCRIPT"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.2.7",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.2.9",
                _ => "ISO 19005-3 6.2.9",
            }, finding.Clause);
        });
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void RepeatedPostScriptXObjectsReportOneFinding(PdfValidationProfile profile)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /XObject << /PS0 5 0 R /PS1 6 0 R >> >> >>",
            ObjPdf.Stream("/PS0 Do /PS1 Do"),
            ObjPdf.Stream("inert", "/Type /XObject /Subtype /PS"),
            ObjPdf.Stream("inert", "/Type /XObject /Subtype /PS")));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Single(report.Findings, f => f.RuleId == "PDFA-XOBJECT-POSTSCRIPT");
    }
}
