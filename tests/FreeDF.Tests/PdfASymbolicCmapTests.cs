using System.Buffers.Binary;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfASymbolicCmapTests
{
    public static IEnumerable<object[]> Encodings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
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
                yield return [profile, records, records.Length != 1
                    && (profile == PdfValidationProfile.PdfA1b || !records.Contains(((ushort)3, (ushort)0)))];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void SymbolicEncodingRecordsRespectEachPdfAPart(PdfValidationProfile profile,
        (ushort, ushort)[] records, bool invalid)
    {
        var report = Validate(profile, Program(records));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CMAP-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-SYMBOLIC-CMAP"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FONT-SYMBOLIC-CMAP"), finding =>
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

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MalformedHeadersAndFiltersAreExplicitlyIncomplete(PdfValidationProfile profile)
    {
        var valid = Program([(3, 0)]);
        foreach (var end in new[] { 0, 4, 11, 12, 27, 28, 31, 35, 39, 41 })
            AssertIncomplete(Validate(profile, valid[..end]));
        foreach (var offset in new[] { 0, 4, 20, 24, 28, 30, 36 })
        {
            var malformed = (byte[])valid.Clone();
            malformed[offset] = 0xff;
            AssertIncomplete(Validate(profile, malformed));
        }
        AssertIncomplete(Validate(profile, valid, filter: "UnknownFilter"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MissingCmapFailsTheEncodingRequirement(PdfValidationProfile profile)
    {
        var program = Program([(3, 0)]);
        program[12] = (byte)'x';
        Assert.True(Validate(profile, program).Has("PDFA-FONT-SYMBOLIC-CMAP"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void NonSymbolicUnembeddedAndFontFile3RemainOutsideThisCheck(PdfValidationProfile profile)
    {
        foreach (var program in new byte[]?[] { null, [] })
        {
            var report = Validate(profile, program, symbolic: false);
            Assert.False(report.Has("PDFA-FONT-SYMBOLIC-CMAP"));
            Assert.False(report.Has("PDFA-FONT-CMAP-INCOMPLETE"));
        }
        foreach (var report in new[] { Validate(profile, null), Validate(profile, [], programKey: "FontFile3") })
        {
            Assert.False(report.Has("PDFA-FONT-SYMBOLIC-CMAP"));
            Assert.False(report.Has("PDFA-FONT-CMAP-INCOMPLETE"));
        }
    }

    static void AssertIncomplete(ValidationReport report)
    {
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-SYMBOLIC-CMAP"));
        Assert.True(report.Has("PDFA-FONT-CMAP-INCOMPLETE"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FONT-CMAP-INCOMPLETE"),
            f => Assert.Equal(ValidationSeverity.Error, f.Severity));
    }

    static ValidationReport Validate(PdfValidationProfile profile, byte[]? program,
        bool symbolic = true, string programKey = "FontFile2", string? filter = null)
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
            descriptor.Entries[programKey] = new PdfStream(dictionary, program);
        }
        return PdfValidator.Validate(doc, profile);
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
