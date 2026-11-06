using System.Buffers.Binary;
using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaTrueTypeWidthTests
{
    public static IEnumerable<object[]> Widths()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            foreach (var mode in Enumerable.Range(0, 8))
                foreach (var (width, mismatch) in new[] { (250.0, false), (249.0, false), (251.0, false),
                    (248.99, true), (251.01, true), (192.0, true) })
                    yield return [profile, mode, width, mismatch && mode != 3];
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void ComparesShownRenderingCodesWithinOneUnit(PdfValidationProfile profile, int mode, double width, bool mismatch)
    {
        var doc = Document(Program(), $"BT /F 12 Tf {mode} Tr ( ) Tj ET");
        Font(doc).Entries["Widths"] = new PdfArray([new PdfReal(width)]);
        AssertWidth(doc, profile, mismatch);
    }

    [Theory]
    [InlineData("BT /F 12 Tf ( ) ' ET", true)]
    [InlineData("BT /F 12 Tf 0 0 ( ) \" ET", true)]
    [InlineData("BT /F 12 Tf [( ) 32 ()] TJ ET", true)]
    [InlineData("BT /F 12 Tf 3 Tr ( ) Tj 0 Tr ( ) Tj ET", true)]
    [InlineData("BT /F 12 Tf 0 Tr ( ) Tj 3 Tr ( ) Tj ET", true)]
    [InlineData("BT /F 12 Tf 3 Tr q 0 Tr ( ) Tj Q ET", true)]
    [InlineData("BT /F 12 Tf q 3 Tr ( ) Tj Q ET", false)]
    [InlineData("BT /F 12 Tf () Tj ET", false)]
    [InlineData("BT /F 12 Tf [32 ()] TJ ET", false)]
    [InlineData("BT /F 12 Tf (A) Tj ET", false)]
    [InlineData("BT /F 12 Tf ET % ( ) Tj", false)]
    public void UsesActualRenderingBytesNotSelectedOrInvisibleFonts(string content, bool mismatch)
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertWidth(Document(Program(), content), profile, mismatch);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void UsesFormAppearanceAndExtGStateFontState(int surface, bool invisible)
    {
        var doc = Document(Program(), "");
        var content = $"BT /F 12 Tf {(invisible ? 3 : 7)} Tr ( ) Tj ET";
        var page = doc.Pages[0];
        if (surface < 2)
        {
            var form = Stream(surface == 0 ? content : $"BT {(invisible ? 3 : 7)} Tr ( ) Tj ET");
            form.Dict.Entries["Subtype"] = new PdfName("Form");
            page.Resources.Entries["XObject"] = new PdfDictionary { Entries = { ["Fm"] = form } };
            page.Dict.Entries["Contents"] = Stream("BT /F 12 Tf ET /Fm Do");
        }
        else if (surface == 2)
            page.Dict.Entries["Annots"] = new PdfArray([new PdfDictionary { Entries =
                { ["Subtype"] = new PdfName("Stamp"), ["AP"] = new PdfDictionary { Entries = { ["N"] = Stream(content) } } } }]);
        else
        {
            page.Resources.Entries["ExtGState"] = new PdfDictionary { Entries = { ["GS"] = new PdfDictionary
                { Entries = { ["Font"] = new PdfArray([Font(doc), new PdfInteger(12)]) } } } };
            page.Dict.Entries["Contents"] = Stream($"/GS gs BT {(invisible ? 3 : 7)} Tr ( ) Tj ET");
        }
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertWidth(doc, profile, !invisible);
    }

    [Theory]
    [InlineData("encoding")]
    [InlineData("differences")]
    [InlineData("symbolic")]
    [InlineData("not-embedded")]
    public void DoesNotGuessUnimplementedFontMappings(string scope)
    {
        var doc = Document(Program(), "BT /F 12 Tf ( ) Tj ET");
        var font = Font(doc);
        var descriptor = (PdfDictionary)doc.File.Resolve(font.Get("FontDescriptor"))!;
        if (scope == "encoding") font.Entries["Encoding"] = new PdfName("MacRomanEncoding");
        if (scope == "differences") font.Entries["Encoding"] = new PdfDictionary { Entries =
            { ["BaseEncoding"] = new PdfName("WinAnsiEncoding"), ["Differences"] = new PdfArray([new PdfInteger(32), new PdfName("A")]) } };
        if (scope == "symbolic") descriptor.Entries["Flags"] = new PdfInteger(4);
        if (scope == "not-embedded") descriptor.Entries.Remove("FontFile2");
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertWidth(doc, profile, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormatFourRangeOffsetsAndRepeatedLastAdvanceAreSupported(bool rangeOffset)
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertWidth(Document(Program(rangeOffset), "BT /F 12 Tf ( ) Tj ET"), profile, true);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("head")]
    [InlineData("hhea")]
    [InlineData("hmtx")]
    [InlineData("cmap-format")]
    [InlineData("cmap-offset")]
    [InlineData("cmap-gid")]
    public void MalformedOrUnsupportedSelectedMetadataIsExplicitlyIncomplete(string variant)
    {
        var program = Program();
        if (variant == "truncated") program = program[..^1];
        if (variant == "head") BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(126), 0);
        if (variant == "hhea") BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(198), 0);
        if (variant == "hmtx") BinaryPrimitives.WriteUInt32BigEndian(program.AsSpan(56), 2);
        if (variant == "cmap-format") BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(252), 12);
        if (variant == "cmap-offset") BinaryPrimitives.WriteUInt32BigEndian(program.AsSpan(248), uint.MaxValue);
        if (variant == "cmap-gid") BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(276), 100);
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertWidth(Document(program, "BT /F 12 Tf ( ) Tj ET"), profile, false, true);
    }

    [Fact]
    public void EveryTruncatedMetadataPrefixIsRejectedWithoutIndexExceptions()
    {
        var program = Program();
        for (var length = 0; length < program.Length; length++)
            Assert.Throws<InvalidDataException>(() => FontChecks.TrueTypeAsciiWidths(program[..length]));
        Assert.Equal(250, FontChecks.TrueTypeAsciiWidths(program)[32]);
    }

    [Theory]
    [InlineData(120, 0)]
    [InlineData(126, 15)]
    [InlineData(126, 16385)]
    [InlineData(198, 3)]
    [InlineData(242, 65535)]
    [InlineData(258, 3)]
    [InlineData(258, 0)]
    [InlineData(268, 65534)]
    [InlineData(270, 1)]
    [InlineData(272, 33)]
    [InlineData(274, 65534)]
    [InlineData(280, 1)]
    [InlineData(280, 2)]
    [InlineData(280, 65534)]
    public void InvalidTableAndSegmentMetadataDoesNotProduceGuessedAdvances(int offset, int value)
    {
        var program = Program();
        BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(offset), (ushort)value);
        Assert.Throws<InvalidDataException>(() => FontChecks.TrueTypeAsciiWidths(program));
    }

    [Fact]
    public void RangeOffsetZeroGlyphIsNotAdjustedByDeltaAndOtherGlyphsAre()
    {
        var program = Program(true);
        BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(276), 1);
        BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(284), 0);
        Assert.Null(FontChecks.TrueTypeAsciiWidths(program)[32]);
        BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(276), 65535);
        BinaryPrimitives.WriteUInt16BigEndian(program.AsSpan(284), 2);
        Assert.Equal(250, FontChecks.TrueTypeAsciiWidths(program)[32]);
    }

    [Fact]
    public void DecodedProgramBoundIsExplicit()
    {
        var program = new byte[16_777_217];
        Program().CopyTo(program, 0);
        Assert.Throws<InvalidDataException>(() => FontChecks.TrueTypeAsciiWidths(program));
    }

    [Theory]
    [InlineData("FirstChar", true)]
    [InlineData("LastChar", true)]
    [InlineData("Widths", true)]
    [InlineData("absent", false)]
    [InlineData("null-differences", false)]
    public void DictionaryWidthsAndUnmodifiedBaseEncodingHaveExplicitOutcomes(string variant, bool incomplete)
    {
        var doc = Document(Program(), "BT /F 12 Tf ( ) Tj ET");
        var font = Font(doc);
        if (variant is "FirstChar" or "LastChar") font.Entries[variant] = new PdfReal(32.5);
        if (variant == "Widths") font.Entries["Widths"] = new PdfArray([new PdfName("Bad")]);
        if (variant == "absent") font.Entries.Remove("Widths");
        if (variant == "null-differences") font.Entries["Encoding"] = new PdfDictionary { Entries =
            { ["BaseEncoding"] = new PdfName("WinAnsiEncoding"), ["Differences"] = PdfNull.Instance } };
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertWidth(doc, profile, variant == "null-differences", incomplete);
    }

    internal static byte[] Program(bool rangeOffset = false)
    {
        var data = new byte[rangeOffset ? 286 : 284];
        void U16(int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), (ushort)value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
        U32(0, 0x00010000);
        U16(4, 6);
        var tables = new[] { ("head", 108, 54), ("hhea", 164, 36), ("hmtx", 200, 6),
            ("maxp", 208, 32), ("cmap", 240, data.Length - 240), ("glyf", 284, 0) };
        for (var i = 0; i < tables.Length; i++)
        {
            var (tag, offset, length) = tables[i];
            Encoding.ASCII.GetBytes(tag).CopyTo(data, 12 + i * 16);
            U32(20 + i * 16, (uint)offset); U32(24 + i * 16, (uint)length);
        }
        U32(108, 0x00010000); U32(120, 0x5F0F3CF5); U16(126, 2048);
        U32(164, 0x00010000); U16(198, 1);
        U16(200, 512);
        U32(208, 0x00010000); U16(212, 2);
        U16(242, 1); U16(244, 3); U16(246, 1); U32(248, 12);
        U16(252, 4); U16(254, data.Length - 252); U16(258, 4);
        U16(266, 32); U16(268, 65535); U16(272, 32); U16(274, 65535);
        U16(276, rangeOffset ? 0 : 65505); U16(278, 1);
        if (rangeOffset) { U16(280, 4); U16(284, 1); }
        return data;
    }

    internal static PdfDocument Document(byte[] program, string content) => PdfDocument.Load(ObjPdf.Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F 5 0 R >> >> /Contents 4 0 R >>",
        ObjPdf.Stream(content),
        "<< /Type /Font /Subtype /TrueType /BaseFont /Test /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 32 /Widths [192] /FontDescriptor 6 0 R >>",
        "<< /Type /FontDescriptor /FontName /Test /Flags 32 /FontFile2 7 0 R >>",
        ObjPdf.Stream(Encoding.Latin1.GetString(program))));

    static PdfDictionary Font(PdfDocument doc) => (PdfDictionary)doc.File.Resolve(
        ((PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("Font"))!).Get("F"))!;

    static PdfStream Stream(string text) => new(new PdfDictionary(), Encoding.Latin1.GetBytes(text));

    static void AssertWidth(PdfDocument doc, PdfValidationProfile profile, bool mismatch, bool incomplete = false)
    {
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(mismatch, report.Has("PDFUA-FONT-WIDTH"));
        Assert.Equal(incomplete, report.Has("PDFUA-FONT-WIDTH-INCOMPLETE"));
        Assert.All(report.Findings.Where(f => f.RuleId.StartsWith("PDFUA-FONT-WIDTH", StringComparison.Ordinal)), f =>
        {
            Assert.Equal(ValidationSeverity.Error, f.Severity);
            Assert.Equal(profile == PdfValidationProfile.PdfUA1 ? "ISO 14289-1 7.21.5" : "ISO 14289-2 8.4.5.6", f.Clause);
        });
    }
}
