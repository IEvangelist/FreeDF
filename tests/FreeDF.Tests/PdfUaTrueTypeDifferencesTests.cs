using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaTrueTypeDifferencesTests
{
    public static IEnumerable<object[]> Arrays()
    {
        foreach (var part in new[] { 1, 2 })
            foreach (var (value, incomplete) in new (string, bool)[]
            {
                ("null", false), ("[]", false), ("[0 /A]", false), ("[255 /.notdef]", false),
                ("[0 /A 0 /A]", false), ("true", true), ("9 0 R", false), ("[0.5 /A]", true),
                ("[-1 /A]", true), ("[256 /A]", true), ("[/A]", true), ("[255 /A /A]", true),
                ("[0 (A)]", true), ("[" + string.Concat(Enumerable.Repeat("0 /A ", 5000)) + "]", false),
                ("[" + string.Concat(Enumerable.Repeat("0 /A ", 5000)) + "0]", true),
            })
                yield return [part, value, incomplete];
    }

    [Theory]
    [MemberData(nameof(Arrays))]
    public void MalformedOrOverBudgetDifferencesAreIncomplete(int part, string value, bool incomplete)
    {
        var doc = PdfDocument.Load(TestPdf.Build("", "/Font << /F1 << /Type /Font /Subtype /TrueType /BaseFont /Foo "
            + "/FontDescriptor << /Flags 32 >> /Encoding << /BaseEncoding /WinAnsiEncoding /Differences " + value + " >> >> >>"));
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-TRUETYPE-DIFFERENCES"));
        Assert.Equal(incomplete, report.Has("PDFUA-FONT-ENCODING-INCOMPLETE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFUA-FONT-ENCODING-INCOMPLETE"),
            f => Assert.Equal(ValidationSeverity.Error, f.Severity));
    }

    public static IEnumerable<object[]> Names()
    {
        foreach (var part in new[] { 1, 2 })
            foreach (var indirect in new[] { false, true })
                foreach (var symbolic in new[] { false, true })
                    foreach (var embedded in new[] { false, true })
                        foreach (var name in new[] { "grave", "A", "afii10017", "Asmall", ".notdef", "gravee", "uni0060", "u0060", "grave.alt", "grave_grave" })
                            yield return [part, indirect, symbolic, embedded, name,
                                !symbolic && name is "gravee" or "uni0060" or "u0060" or "grave.alt" or "grave_grave"];
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void NonSymbolicDifferencesRequireLiteralGlyphListNames(int part, bool indirect, bool symbolic, bool embedded, string name, bool invalid)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F1 4 0 R >> >> >>",
            "<< /Type /Font /Subtype /TrueType /BaseFont /Foo /Encoding << /BaseEncoding /WinAnsiEncoding /Differences "
                + (indirect ? "7 0 R" : $"[96 /{name}]") + " >> /FontDescriptor 5 0 R >>",
            $"<< /Type /FontDescriptor /FontName /Foo /Flags {(symbolic ? 4 : 32)} " + (embedded ? "/FontFile2 6 0 R" : "") + " >>",
            ObjPdf.Stream("synthetic font program"),
            "[96 8 0 R]",
            "/" + name));
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalid, report.Has("PDFUA-FONT-TRUETYPE-DIFFERENCES"));
        foreach (var finding in report.Findings.Where(f => f.RuleId == "PDFUA-FONT-TRUETYPE-DIFFERENCES"))
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(part == 1 ? "ISO 14289-1 7.21.6" : "ISO 14289-2 8.4.5.7", finding.Clause);
        }
    }
}
