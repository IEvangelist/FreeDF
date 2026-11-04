using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaToUnicodeValueTests
{
    public static IEnumerable<object[]> Values()
    {
        (string Body, bool Forbidden)[] cases =
        [
            ("1 beginbfchar <01> <0000> endbfchar", true),
            ("1 beginbfchar <01> <FEFF> endbfchar", true),
            ("1 beginbfchar <01> <FFFE> endbfchar", true),
            ("1 beginbfchar <01> <004100000042> endbfchar", true),
            ("1 beginbfchar <01> <0041FEFF0042> endbfchar", true),
            ("1 beginbfchar <01> <0041FFFE0042> endbfchar", true),
            ("1 beginbfchar <01> <0041> endbfchar", false),
            ("1 beginbfchar <01> <004> endbfchar", false),
            ("1 beginbfchar <01> <FEF> endbfchar", false),
            ("1 beginbfchar <01> <000> endbfchar", true),
            ("1 beginbfchar <0000> <0041> endbfchar", false),
            ("1 beginbfchar <FEFF> <0041> endbfchar", false),
            ("1 beginbfchar <01> <D83DDE00> endbfchar", false),
            ("1 beginbfchar <01> <00410042> endbfchar", false),
            ("% <01> <0000>\n1 beginbfchar <01> <0041> endbfchar", false),
            ("1 beginbfrange <01> <02> <FEFE> endbfrange", true),
            ("1 beginbfrange <01> <02> <FFFD> endbfrange", true),
            ("1 beginbfrange <01> <02> <00410000> endbfrange", true),
            ("1 beginbfrange <01> <02> [<0041> <0000>] endbfrange", true),
            ("1 beginbfrange <01> <02> [<0041> <FEFF>] endbfrange", true),
            ("1 beginbfrange <01> <02> [<0041> <FFFE>] endbfrange", true),
            ("1 beginbfrange <01> <02> <0041> endbfrange", false),
            ("1 beginbfrange <01> <02> [<00410042> <D83DDE00>] endbfrange", false),
            ("1 beginbfrange <01> <02> <00FD> endbfrange", false),
            ("1 beginbfchar <01> <0000> endbfchar 1 beginbfchar <01> <0041> endbfchar", false),
            ("1 beginbfchar <01> <0041> endbfchar 1 beginbfchar <01> <0000> endbfchar", true),
            ("1 beginbfrange <01> <02> <FEFE> endbfrange 1 beginbfchar <02> <0041> endbfchar", false),
        ];
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            foreach (var row in cases) yield return [profile, row.Body, row.Forbidden];
    }

    [Theory]
    [MemberData(nameof(Values))]
    public void ChecksEffectiveUtf16DestinationsNotSourceCodes(PdfValidationProfile profile, string body, bool forbidden)
    {
        var report = PdfValidator.Validate(Document(body), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-TOUNICODE-INCOMPLETE"));
        Assert.Equal(forbidden, report.Has("PDFUA-FONT-TOUNICODE-VALUE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFUA-FONT-TOUNICODE-VALUE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(profile == PdfValidationProfile.PdfUA1 ? "ISO 14289-1 7.21.7" : "ISO 14289-2 8.4.5.8", finding.Clause);
            Assert.Equal(0, finding.PageIndex);
        });
    }

    [Theory]
    [InlineData("1 beginbfchar <01> <0041>")]
    [InlineData("2 beginbfchar <01> <0041> endbfchar")]
    [InlineData("1 beginbfchar <01> <00xz> endbfchar")]
    [InlineData("1 beginbfchar <01> <> endbfchar")]
    [InlineData("1 beginbfrange <02> <01> <0041> endbfrange")]
    [InlineData("1 beginbfrange <01> <02> [<0041>] endbfrange")]
    [InlineData("1 beginbfrange <01> <02> <FFFF> endbfrange")]
    [InlineData("1 beginbfrange <01> <02> <00FF> endbfrange")]
    [InlineData("++1 beginbfchar <01> <0041> endbfchar")]
    [InlineData("1.0 beginbfchar <01> <0041> endbfchar")]
    [InlineData("1 beginbfchar <01> <D83D> endbfchar extra")]
    [InlineData("/Other usecmap")]
    [InlineData("{ 1 beginbfchar <01> <0000> endbfchar }")]
    public void UnsupportedOrMalformedProgramsAreExplicitlyIncomplete(string body)
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
        {
            var report = PdfValidator.Validate(Document(body), profile);
            Assert.Contains(report.Findings, f => f.RuleId == "PDFUA-FONT-TOUNICODE-INCOMPLETE"
                && f.Severity == ValidationSeverity.Error);
            Assert.False(report.Has("PDFUA-FONT-TOUNICODE-VALUE"));
            Assert.False(report.Has("VALIDATION-INTERNAL"));
        }
    }

    [Fact]
    public void DictionaryParentIsNotSilentlyIgnored()
    {
        var doc = Document("1 beginbfchar <01> <0041> endbfchar");
        var font = Font(doc);
        Assert.IsType<PdfStream>(font.Get("ToUnicode")).Dict.Entries["UseCMap"] = new PdfName("Parent");
        Assert.True(PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1).Has("PDFUA-FONT-TOUNICODE-INCOMPLETE"));
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("flate")]
    [InlineData("hex-flate")]
    public void StrictStreamDecodingPreservesLiteralMappings(string encoding)
    {
        var doc = Document("1 beginbfchar <01> <FEFF> endbfchar");
        var original = Assert.IsType<PdfStream>(Font(doc).Get("ToUnicode"));
        var data = encoding == "raw" ? original.RawData : PdfFilters.DeflateEncode(original.RawData);
        var dictionary = new PdfDictionary();
        if (encoding != "raw") dictionary.Entries["Filter"] = new PdfName("FlateDecode");
        if (encoding == "hex-flate")
        {
            dictionary.Entries["Filter"] = new PdfArray([new PdfName("ASCIIHexDecode"), new PdfName("FlateDecode")]);
            data = Encoding.ASCII.GetBytes(Convert.ToHexString(data) + ">");
        }
        Font(doc).Entries["ToUnicode"] = new PdfStream(dictionary, data);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.True(report.Has("PDFUA-FONT-TOUNICODE-VALUE"));
        Assert.False(report.Has("PDFUA-FONT-TOUNICODE-INCOMPLETE"));
    }

    [Theory]
    [InlineData("corrupt-flate")]
    [InlineData("bad-filter")]
    [InlineData("dropped-filter")]
    [InlineData("bad-parameters")]
    public void UnprovenFilterDecodingDoesNotBecomeAUnicodeVerdict(string kind)
    {
        var doc = Document("1 beginbfchar <01> <FEFF> endbfchar");
        var stream = Assert.IsType<PdfStream>(Font(doc).Get("ToUnicode"));
        if (kind == "corrupt-flate")
        {
            var data = PdfFilters.DeflateEncode(stream.RawData);
            data[^1] ^= 1;
            Font(doc).Entries["ToUnicode"] = new PdfStream(
                new PdfDictionary { Entries = { ["Filter"] = new PdfName("FlateDecode") } }, data);
        }
        else if (kind == "bad-filter") stream.Dict.Entries["Filter"] = new PdfInteger(1);
        else if (kind == "dropped-filter") stream.Dict.Entries["Filter"] = new PdfArray([PdfNull.Instance]);
        else stream.Dict.Entries["DecodeParms"] = new PdfName("Invalid");
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.True(report.Has("PDFUA-FONT-TOUNICODE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-FONT-TOUNICODE-VALUE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("tokens")]
    [InlineData("mappings")]
    [InlineData("destination")]
    public void InspectionLimitsAreExplicit(string limit)
    {
        var body = limit switch
        {
            "bytes" => new string(' ', 16 * 1024 * 1024),
            "tokens" => string.Concat(Enumerable.Repeat("pop ", 1_000_001)),
            "mappings" => string.Concat(Enumerable.Repeat("1 beginbfchar <01> <0041> endbfchar\n", 65537)),
            _ => "1 beginbfchar <01> <" + new string('0', 1028) + "> endbfchar",
        };
        var report = PdfValidator.Validate(Document(body), PdfValidationProfile.PdfUA1);
        Assert.True(report.Has("PDFUA-FONT-TOUNICODE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-FONT-TOUNICODE-VALUE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    static PdfDictionary Font(PdfDocument doc) =>
        Assert.IsType<PdfDictionary>(doc.File.Resolve(Assert.IsType<PdfDictionary>(
            doc.File.Resolve(doc.Pages[0].Resources.Get("Font"))).Get("F1")));

    static PdfDocument Document(string body)
    {
        var doc = PdfACidToGidTests.Document(true, "CIDFontType2", 0, "/Identity", "null");
        Font(doc).Entries["ToUnicode"] = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("/CIDInit /ProcSet findresource begin 12 dict begin\nbegincmap\n"
                + "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n"
                + "/CMapName /Test def /CMapType 2 def\n1 begincodespacerange <00> <FF> endcodespacerange\n"
                + body + "\nendcmap\nCMapName currentdict /CMap defineresource pop end end"));
        return doc;
    }
}
