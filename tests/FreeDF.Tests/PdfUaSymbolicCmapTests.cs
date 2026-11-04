using System.Buffers.Binary;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaSymbolicCmapTests
{
    public static IEnumerable<object[]> Encodings()
    {
        foreach (var part in new[] { 1, 2 })
            foreach (var records in new[]
            {
                Array.Empty<(ushort, ushort)>(),
                new (ushort, ushort)[] { (3, 0) },
                [(1, 0)],
                [(3, 1)],
                [(3, 10)],
                [(3, 1), (3, 10)],
                [(1, 0), (3, 1)],
                [(3, 0), (3, 1)],
            })
                yield return [part, records,
                    part == 1 ? records.Length != 1 && !records.Contains(((ushort)3, (ushort)0))
                        : !records.Contains(((ushort)3, (ushort)0)) && !records.Contains(((ushort)1, (ushort)0))];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void SymbolicEncodingRequirementsAreProfileSpecific(int part, (ushort, ushort)[] records, bool invalid)
    {
        var report = Validate(part, Program(records));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-CMAP-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFUA-FONT-SYMBOLIC-CMAP"));
        foreach (var finding in report.Findings.Where(f => f.RuleId == "PDFUA-FONT-SYMBOLIC-CMAP"))
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(part == 1 ? "ISO 14289-1 7.21.6" : "ISO 14289-2 8.4.5.7", finding.Clause);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TruncatedOrUnsupportedHeadersAreExplicitlyIncomplete(int part)
    {
        var valid = Program([(3, 0)]);
        foreach (var end in new[] { 0, 4, 11, 12, 27, 28, 31, 35, 39, 41 })
        {
            var report = Validate(part, valid[..end]);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFUA-FONT-CMAP-INCOMPLETE"), $"length {end}");
            Assert.False(report.Has("PDFUA-FONT-SYMBOLIC-CMAP"));
        }
        foreach (var offset in new[] { 0, 4, 20, 24, 28, 30, 36 })
        {
            var malformed = (byte[])valid.Clone();
            malformed[offset] = 0xff;
            var report = Validate(part, malformed);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFUA-FONT-CMAP-INCOMPLETE"), $"offset {offset}");
            Assert.False(report.Has("PDFUA-FONT-SYMBOLIC-CMAP"));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingCmapTableFailsTheEncodingRequirement(int part)
    {
        var program = Program([(3, 0)]);
        program[12] = (byte)'x';
        Assert.True(Validate(part, program).Has("PDFUA-FONT-SYMBOLIC-CMAP"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NonSymbolicAndUnembeddedFontsAreOutsideThisSlice(int part)
    {
        foreach (var program in new byte[]?[] { null, [] })
        {
            var report = Validate(part, program, symbolic: false);
            Assert.False(report.Has("PDFUA-FONT-SYMBOLIC-CMAP"));
            Assert.False(report.Has("PDFUA-FONT-CMAP-INCOMPLETE"));
        }
        var unembedded = Validate(part, null);
        Assert.False(unembedded.Has("PDFUA-FONT-SYMBOLIC-CMAP"));
        Assert.False(unembedded.Has("PDFUA-FONT-CMAP-INCOMPLETE"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnsupportedStreamFiltersCannotSilentlySkipTheCheck(int part)
    {
        var report = Validate(part, Program([(3, 0)]), filter: "UnknownFilter");
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-SYMBOLIC-CMAP"));
        Assert.True(report.Has("PDFUA-FONT-CMAP-INCOMPLETE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFUA-FONT-CMAP-INCOMPLETE"),
            f => Assert.Equal(ValidationSeverity.Error, f.Severity));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FontFile3WithoutOpenTypeSubtypeIsOutsideThisSlice(int part)
    {
        var report = Validate(part, [], programKey: "FontFile3");
        Assert.False(report.Has("PDFUA-FONT-SYMBOLIC-CMAP"));
        Assert.False(report.Has("PDFUA-FONT-CMAP-INCOMPLETE"));
    }

    internal static ValidationReport Validate(int part, byte[]? program, bool symbolic = true, string programKey = "FontFile2", string? filter = null, bool openType = false)
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F1 4 0 R >> >> >>",
            "<< /Type /Font /Subtype /TrueType /BaseFont /Foo /FontDescriptor 5 0 R >>",
            $"<< /Type /FontDescriptor /FontName /Foo /Flags {(symbolic ? 4 : 32)} >>"));
        var font = (PdfDictionary)doc.File.Resolve(((PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources!.Get("Font"))!).Get("F1"))!;
        var descriptor = (PdfDictionary)doc.File.Resolve(font.Get("FontDescriptor"))!;
        if (program is not null)
        {
            var dictionary = new PdfDictionary();
            if (filter is not null) dictionary.Entries["Filter"] = new PdfName(filter);
            if (openType) dictionary.Entries["Subtype"] = new PdfName("OpenType");
            descriptor.Entries[openType ? "FontFile3" : programKey] = new PdfStream(dictionary, program);
        }
        return PdfValidator.ValidatePdfUA(doc, part);
    }

    static byte[] Program((ushort Platform, ushort Encoding)[] records)
    {
        var size = 4 + records.Length * 8 + 10;
        var bytes = new byte[28 + size];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 1);
        "cmap"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 28);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24), (uint)size);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(30), (ushort)records.Length);
        for (var i = 0; i < records.Length; i++)
        {
            var offset = 32 + 8 * i;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), records[i].Platform);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 2), records[i].Encoding);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 4), (uint)(4 + records.Length * 8));
        }
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(bytes.Length - 10), 6);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(bytes.Length - 8), 10);
        return bytes;
    }
}
