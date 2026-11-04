using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaTableScopePresenceTests
{
    public static IEnumerable<object[]> Scopes()
    {
        foreach (var corner in new[] { "", "Row", "Column", "Both" })
            foreach (var top in new[] { "", "Row", "Column", "Both" })
                foreach (var left in new[] { "", "Row", "Column", "Both" })
                    yield return [corner, top, left, corner == "" || top == "" || left == ""];
    }

    [Theory]
    [MemberData(nameof(Scopes))]
    public void AllHeadersNeedScopeWhenNoDataCellSuppliesHeaders(string corner, string top, string left, bool disconnected)
    {
        var doc = Document(Header(corner), Header(top), Header(left), "<< /S /TD >>");
        var ua1 = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.False(ua1.Has("VALIDATION-INTERNAL"));
        Assert.Equal(disconnected, ua1.Has("PDFUA-TABLE-HEADER-CONNECTION"));
        Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfUA2).Has("PDFUA-TABLE-HEADER-CONNECTION"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitHeadersOrClassScopeProvidesAConnection(bool explicitHeaders)
    {
        var doc = Document(Header("Column", "/ID (h)"), "<< /S /TH /C /Col >>", Header(explicitHeaders ? "" : "Row"),
            explicitHeaders ? "<< /S /TD /A << /O /Table /Headers [(h)] >> >>" : "<< /S /TD >>",
            "/Col << /O /Table /Scope /Column >>");
        Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1).Has("PDFUA-TABLE-HEADER-CONNECTION"));
    }

    [Fact]
    public void PresenceRequirementDoesNotDependOnSpannedPlacement()
    {
        var doc = Document(Header("Row"), Header("Row"), Header(""), "<< /S /TD /A << /O /Table /ColSpan 2 >> >>");
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.True(report.Has("PDFUA-TABLE-HEADER-CONNECTION"));
    }

    static string Header(string scope, string extra = "") =>
        $"<< /S /TH {extra} {(scope.Length == 0 ? "" : "/A << /O /Table /Scope /" + scope + " >>")} >>";

    static PdfDocument Document(string corner, string top, string left, string data, string classes = "") =>
        PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            $"<< /Type /StructTreeRoot /K 5 0 R /ClassMap << {classes} >> >>",
            "<< /Type /StructElem /S /Document /K 6 0 R >>",
            "<< /Type /StructElem /S /Table /K [7 0 R 8 0 R] >>",
            $"<< /Type /StructElem /S /TR /K [{corner} {top}] >>",
            $"<< /Type /StructElem /S /TR /K [{left} {data}] >>"));
}
