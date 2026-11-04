using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaCMapWModeTests
{
    public static IEnumerable<object[]> Definitions()
    {
        foreach (var part in new[] { 1, 2 })
            foreach (var indirect in new[] { false, true })
                foreach (var (dictionary, program, mismatch) in new (string, string, bool)[]
                {
                    ("0", "/WMode 0 def", false), ("1", "/WMode 1 def", false),
                    ("0", "/WMode 1 def", true), ("1", "/WMode 0 def", true),
                    ("", "/WMode 1 def", true), ("null", "/WMode 1 def", true),
                    ("", "/WMode 0 def", false), ("0", "", false), ("1", "", true),
                    ("0", "% /WMode 1 def\n/WMode 0 def", false),
                    ("0", "/WMode % comment\n1 def", true),
                    ("0", "(/WMode 1 def) pop /WMode 0 def", false),
                    ("0", "/W#4Dode 1 def", false),
                    ("0", "/WMode +1 def", true),
                    ("0", "/WMode 0 def /WMode 1 def", true),
                    ("0", "/WMode 1 def /WMode 0 def", false),
                    ("0", "/OtherWMode 1 def", false),
                    ("0", "[/WMode 1 def] pop", false),
                    ("0", "<2f574d6f6465203120646566> pop", false),
                })
                    yield return [part, indirect, dictionary, program, mismatch];
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void LiteralDefinitionsIgnoreCommentsStringsAndUseFinalValue(int part, bool indirect, string dictionary, string program, bool mismatch)
    {
        var report = Validate(part, dictionary, program, indirect);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-CMAP-WMODE-INCOMPLETE"));
        Assert.Equal(mismatch, report.Has("PDFUA-FONT-CMAP-WMODE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFUA-FONT-CMAP-WMODE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(part == 1 ? "ISO 14289-1 7.21.3.3" : "ISO 14289-2 8.4.5.4", finding.Clause);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UninspectableDefinitionsOrDictionaryValuesAreExplicitlyIncomplete(int part)
    {
        foreach (var (dictionary, program) in new (string, string)[]
        {
            ("0", "/WMode 1 0 add def"), ("0", "/WMode (1) def"), ("0", "/WMode 1.0 def"),
            ("0", "/WMode 2 def"), ("0", "/WMode -1 def"), ("0", "/WMode 1"),
            ("0.0", "/WMode 0 def"), ("2", "/WMode 0 def"), ("()", "/WMode 0 def"),
            ("0", "{ /WMode 1 def } exec"), ("0", "{" + new string('{', 200)),
            ("0", "/H usecmap"), ("0", "/WMode --1 def"), ("0", "/WMode 1junk.0 def"),
            ("0", "/WMode 9999999999999999999999999999 def"),
        })
        {
            var report = Validate(part, dictionary, program, false);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.False(report.Has("PDFUA-FONT-CMAP-WMODE"));
            Assert.True(report.Has("PDFUA-FONT-CMAP-WMODE-INCOMPLETE"), dictionary + " " + program);
        }
        var filtered = Validate(part, "0", "/WMode 0 def", false, "UnknownFilter");
        Assert.False(filtered.Has("VALIDATION-INTERNAL"));
        Assert.True(filtered.Has("PDFUA-FONT-CMAP-WMODE-INCOMPLETE"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ScanLimitsAreExplicitAndAnExplicitModeOverridesInheritedDefaults(int part)
    {
        foreach (var program in new[] { "%" + new string('x', 16 * 1024 * 1024), string.Concat(Enumerable.Repeat("0 ", 1_000_001)) })
        {
            var report = Validate(part, "0", program, false);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFUA-FONT-CMAP-WMODE-INCOMPLETE"));
        }
        var inherited = Validate(part, "0", "/H usecmap /WMode 0 def", false);
        Assert.False(inherited.Has("PDFUA-FONT-CMAP-WMODE-INCOMPLETE"));
        Assert.False(inherited.Has("PDFUA-FONT-CMAP-WMODE"));
    }

    static ValidationReport Validate(int part, string dictionary, string program, bool indirect, string? filter = null)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F1 4 0 R >> >> >>",
            "<< /Type /Font /Subtype /Type0 /BaseFont /Foo /Encoding 5 0 R /DescendantFonts [6 0 R] >>",
            ObjPdf.Stream("begincmap " + program + "\nendcmap", "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
                + (dictionary.Length == 0 ? "" : "/WMode " + (indirect ? "7 0 R" : dictionary))),
            "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Foo /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> >>",
            dictionary.Length == 0 ? "null" : dictionary));
        if (filter is not null)
        {
            var fonts = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources!.Get("Font"))!;
            var font = (PdfDictionary)doc.File.Resolve(fonts.Get("F1"))!;
            var cmap = (PdfStream)doc.File.Resolve(font.Get("Encoding"))!;
            cmap.Dict.Entries["Filter"] = new PdfName(filter);
        }
        return PdfValidator.ValidatePdfUA(doc, part);
    }
}
