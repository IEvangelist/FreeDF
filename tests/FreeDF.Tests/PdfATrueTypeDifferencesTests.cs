using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfATrueTypeDifferencesTests
{
    public static IEnumerable<object[]> Names()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var indirect in new[] { false, true })
                foreach (var symbolic in new[] { false, true })
                    foreach (var embedded in new[] { false, true })
                        foreach (var name in new[] { "grave", "A", "afii10017", "Asmall", ".notdef", "gravee", "uni0060", "u0060", "grave.alt", "grave_grave" })
                            yield return [profile, indirect, symbolic, embedded, name,
                                symbolic || profile == PdfValidationProfile.PdfA1b
                                    || name is "gravee" or "uni0060" or "u0060" or "grave.alt" or "grave_grave"];
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void LiteralGlyphNamesAreAllowedOnlyInPartsTwoAndThree(PdfValidationProfile profile,
        bool indirect, bool symbolic, bool embedded, string name, bool invalid)
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
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-ENCODING-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-TRUETYPE-ENCODING"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FONT-TRUETYPE-ENCODING"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.3.7",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.2.11.6",
                _ => "ISO 19005-3 6.2.11.6",
            }, finding.Clause);
        });
    }

    public static IEnumerable<object[]> Arrays()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var (value, incomplete) in new (string, bool)[]
            {
                ("null", false), ("[]", false), ("[0 /A]", false), ("[255 /.notdef]", false),
                ("[0 /A 0 /A]", false), ("true", true), ("9 0 R", false), ("[0.5 /A]", true),
                ("[-1 /A]", true), ("[256 /A]", true), ("[/A]", true), ("[255 /A /A]", true),
                ("[0 (A)]", true), ("[" + string.Concat(Enumerable.Repeat("0 /A ", 5000)) + "]", false),
                ("[" + string.Concat(Enumerable.Repeat("0 /A ", 5000)) + "0]", true),
            })
                yield return [profile, value, incomplete];
    }

    public static IEnumerable<object[]> BaseEncodings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var encoding in new[] { "/WinAnsiEncoding", "/MacRomanEncoding", "/StandardEncoding", "null", "()", "10 0 R" })
                foreach (var glyph in new[] { "A", "uni0041" })
                    yield return [profile, encoding, glyph,
                        encoding is not ("/WinAnsiEncoding" or "/MacRomanEncoding") || glyph != "A"];
    }

    [Theory]
    [MemberData(nameof(BaseEncodings))]
    public void DifferencesDoNotRelaxRequiredBaseEncoding(PdfValidationProfile profile,
        string encoding, string glyph, bool invalid)
    {
        var doc = PdfDocument.Load(TestPdf.Build("", "/Font << /F1 << /Type /Font /Subtype /TrueType /BaseFont /Foo "
            + "/FontDescriptor << /Flags 32 >> /Encoding << /BaseEncoding " + encoding
            + " /Differences [96 /" + glyph + "] >> >> >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-ENCODING-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-TRUETYPE-ENCODING"));
    }

    [Theory]
    [MemberData(nameof(Arrays))]
    public void UninspectableDifferencesProduceExplicitIncompleteError(PdfValidationProfile profile,
        string value, bool incomplete)
    {
        var doc = PdfDocument.Load(TestPdf.Build("", "/Font << /F1 << /Type /Font /Subtype /TrueType /BaseFont /Foo "
            + "/FontDescriptor << /Flags 32 >> /Encoding << /BaseEncoding /WinAnsiEncoding /Differences " + value + " >> >> >>"));
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-TRUETYPE-ENCODING"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-ENCODING-INCOMPLETE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FONT-ENCODING-INCOMPLETE"),
            f => Assert.Equal(ValidationSeverity.Error, f.Severity));
    }
}
