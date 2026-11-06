using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAAnnotationSubtypeTests
{
    public static IEnumerable<object[]> Subtypes()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var indirect in new[] { false, true })
            {
                foreach (var name in new[]
                {
                    "Text", "Link", "FreeText", "Line", "Square", "Circle", "Highlight", "Underline",
                    "Squiggly", "StrikeOut", "Stamp", "Ink", "Popup", "Widget", "PrinterMark", "TrapNet",
                })
                    yield return [profile, indirect, "/" + name, false];
                foreach (var name in new[] { "Polygon", "PolyLine", "Caret", "FileAttachment", "Watermark", "Redact" })
                    yield return [profile, indirect, "/" + name, profile == PdfValidationProfile.PdfA1b];
                foreach (var value in new[] { "/Sound", "/Movie", "/Screen", "/3D", "/RichMedia", "/Unknown", "/text", "", "null", "(Text)", "0" })
                    yield return [profile, indirect, value, true];
            }
    }

    [Theory]
    [MemberData(nameof(Subtypes))]
    public void OnlyProfileDefinedAnnotationSubtypesArePermitted(PdfValidationProfile profile,
        bool indirect, string value, bool invalid)
    {
        var entry = value.Length == 0 ? "" : "/Subtype " + (indirect ? "6 0 R" : value);
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            $"<< /Type /Annot /Rect [10 10 20 20] /F 4 /Contents (note) {entry} >>",
            value.Length == 0 ? "null" : value));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFA-ANNOT-TYPE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-ANNOT-TYPE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.5.2",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.3.1",
                _ => "ISO 19005-3 6.3.1",
            }, finding.Clause);
        });
    }
}
