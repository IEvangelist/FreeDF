using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaCMapReferenceTests
{
    public static IEnumerable<object[]> References()
    {
        foreach (var part in new[] { 1, 2 })
            foreach (var indirect in new[] { false, true })
                foreach (var name in new[] { "Identity-H", "Identity-V", "H", "V", "UniGB-UTF16-H", "UniCNS-UCS2-V", "UniJIS-UCS2-HW-H", "KSCpc-EUC-H", "Adobe-Korea1-2", "Custom", "identity-H", "" })
                    yield return [part, indirect, name, name is "Adobe-Korea1-2" or "Custom" or "identity-H" or ""];
    }

    [Theory]
    [MemberData(nameof(References))]
    public void NamedEncodingCMapReferencesMustBePredefined(int part, bool indirect, string name, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            ObjPdf.Stream("BT /F1 12 Tf <0001> Tj ET"),
            "<< /Type /Font /Subtype /Type0 /BaseFont /Foo /Encoding 6 0 R /DescendantFonts [7 0 R] >>",
            ObjPdf.Stream("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Local def /WMode 0 def endcmap end end",
                "/Type /CMap /CMapName /Local /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /UseCMap " + (indirect ? "8 0 R" : "/" + name)),
            "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Foo /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> >>",
            "/" + name));
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFUA-FONT-CMAP-REFERENCE"));
        foreach (var finding in report.Findings.Where(f => f.RuleId == "PDFUA-FONT-CMAP-REFERENCE"))
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(part == 1 ? "ISO 14289-1 7.21.3.3" : "ISO 14289-2 8.4.5.4", finding.Clause);
        }
    }
}
