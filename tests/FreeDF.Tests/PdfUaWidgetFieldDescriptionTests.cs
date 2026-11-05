using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaWidgetFieldDescriptionTests
{
    static PdfDocument Load(string fieldTooltip, string widgetTooltip, int alternatives, bool secondHidden,
        string parentTooltip = "", bool duplicateOwner = false) => PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /AcroForm << /Fields [13 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [9 0 R 10 0 R] /Tabs /S >>",
            ObjPdf.Stream(""),
            "<< /Type /StructTreeRoot /K 6 0 R /ParentTree << /Nums [1 7 0 R 2 8 0 R] >> >>",
            "<< /Type /StructElem /S /Document /P 5 0 R /K [7 0 R 8 0 R] >>",
            "<< /Type /StructElem /S /Form /P 6 0 R /K << /Type /OBJR /Obj 9 0 R /Pg 3 0 R >> " + ((alternatives & 1) != 0 ? "/Alt (first)" : "") + " >>",
            "<< /Type /StructElem /S /Form /P 6 0 R /K << /Type /OBJR /Obj 10 0 R /Pg 3 0 R >> " + ((alternatives & 2) != 0 ? "/Alt (second)" : "") + " >>",
            "<< /Type /Annot /Subtype /Widget /Parent 11 0 R /Rect [0 0 10 10] /StructParent 1 " + widgetTooltip + " >>",
            "<< /Type /Annot /Subtype /Widget /Parent 11 0 R /Rect [20 0 30 10] /StructParent 2 " + widgetTooltip + (secondHidden ? " /F 2" : "") + " >>",
            "<< /FT /Btn /T (buttons) /Parent 13 0 R /Kids [9 0 R 10 0 R] " + fieldTooltip + " >>",
            "(indirect tooltip)",
            "<< /T (group) /Kids [11 0 R " + (duplicateOwner ? "14 0 R" : "") + "] " + parentTooltip + " >>",
            "<< /FT /Btn /T (other) /Kids [9 0 R] /TU (other) >>"));

    public static IEnumerable<object[]> Descriptions()
    {
        foreach (var fieldTooltip in new[] { "", "/TU ()", "/TU null", "/TU (field)", "/TU 12 0 R" })
            foreach (var widgetTooltip in new[] { "", "/TU (widget-only)" })
                foreach (var alternatives in new[] { 0, 1, 3 })
                    foreach (var hidden in new[] { false, true })
                        yield return [fieldTooltip, widgetTooltip, alternatives, hidden];
    }

    [Theory]
    [MemberData(nameof(Descriptions))]
    public void TerminalFieldTooltipOrEachVisibleWidgetAltSuppliesTheDescription(string fieldTooltip, string widgetTooltip, int alternatives, bool hidden)
    {
        var report = PdfValidator.ValidatePdfUA(Load(fieldTooltip, widgetTooltip, alternatives, hidden), 1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        var hasFieldTooltip = fieldTooltip is "/TU (field)" or "/TU 12 0 R";
        var expected = hasFieldTooltip ? 0 : ((alternatives & 1) == 0 ? 1 : 0) + (!hidden && (alternatives & 2) == 0 ? 1 : 0);
        var findings = report.Findings.Where(f => f.RuleId == "PDFUA-FORM-TU").ToArray();
        Assert.Equal(expected, findings.Length);
        Assert.All(findings, f =>
        {
            Assert.Equal(ValidationSeverity.Error, f.Severity);
            Assert.Equal("ISO 14289-1 7.18.1", f.Clause);
        });
    }

    [Fact]
    public void NonterminalParentTooltipIsNotTheTerminalFieldsTooltip()
    {
        var report = PdfValidator.ValidatePdfUA(Load("", "", 0, false, "/TU (group)"), 1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(2, report.Findings.Count(f => f.RuleId == "PDFUA-FORM-TU"));
    }

    [Fact]
    public void AmbiguousWidgetOwnershipReportsIncompleteValidation()
    {
        var report = PdfValidator.ValidatePdfUA(Load("/TU (field)", "", 3, false, duplicateOwner: true), 1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Contains(report.Findings, f => f.RuleId == "PDFUA-FORM-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void FieldDepthBoundaryReportsIncompleteValidation(int depth, bool incomplete)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 3 0 R /AcroForm << /Fields [5 0 R] >> >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
            "<< /Type /StructTreeRoot /K 4 0 R >>",
            "<< /Type /StructElem /S /Document /P 3 0 R >>",
        };
        for (var level = 0; level <= depth; level++)
            objects.Add(level == depth ? "<< /T (terminal) /FT /Tx /TU (field) >>"
                : $"<< /T (group) /Kids [{6 + level} 0 R] >>");
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(ObjPdf.Build(objects.ToArray())), 1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(incomplete, report.Has("PDFUA-FORM-INCOMPLETE"));
    }
}
