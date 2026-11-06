using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class Pdf20ObjectIdentifierSyntaxTests
{
    [Theory]
    [InlineData("+2", "0")]
    [InlineData("0002", "0")]
    [InlineData("2", "+0")]
    [InlineData("2", "00000")]
    public void Pdf_20_object_headers_with_signs_or_padding_enter_recovery(string number, string generation)
    {
        var file = PdfFile.Load(Build("2.0", null, number, generation, "<< /Type /Target >>"));

        file.GetObject(2);
        Assert.True(file.Repaired);
    }

    [Theory]
    [InlineData("+2 0 R")]
    [InlineData("02 0 R")]
    [InlineData("2 00000 R")]
    [InlineData("0 0 R")]
    public void Pdf_20_references_with_signs_or_padding_enter_recovery(string reference)
    {
        var file = PdfFile.Load(Build("2.0", null, "2", "0", $"<< /Target {reference} >>"));

        Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.True(file.Repaired);
    }

    [Fact]
    public void Catalog_version_override_applies_to_loaded_object_headers()
    {
        var file = PdfFile.Load(Build("1.7", "2.0", "0002", "00000", "<< /Type /Target >>"));

        Assert.Equal("1.7", file.Version);
        Assert.Equal("2.0", file.Catalog.Get("Version").AsName());
        Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.True(file.Repaired);
    }

    [Fact]
    public void Catalog_version_override_applies_to_catalog_references()
    {
        var file = PdfFile.Load(Build("1.7", "2.0", "2", "0", "<< /Type /Target >>", "+2 0 R"));

        Assert.Equal("2.0", file.Catalog.Get("Version").AsName());
        Assert.True(file.Repaired);
    }

    [Theory]
    [InlineData("1.7", false)]
    [InlineData("2.0", true)]
    public void Compressed_reference_generation_sign_is_version_scoped(string version, bool shouldRepair)
    {
        var file = PdfFile.Load(BuildObjectStreamMember(version, "2 +0 R"));
        var target = file.Resolve(file.Catalog.Get("Probe"));

        Assert.Equal(shouldRepair, file.Repaired);
        if (!shouldRepair)
            Assert.Equal(new PdfRef(2, 0), Assert.IsType<PdfRef>(Assert.IsType<PdfDictionary>(target).Get("Target")));
    }

    [Theory]
    [InlineData("+2", "0", "<< /Target +2 0 R >>")]
    [InlineData("0002", "00000", "<< /Target 0002 00000 R >>")]
    public void Pdf_17_keeps_legacy_identifier_spellings(string number, string generation, string value)
    {
        var file = PdfFile.Load(Build("1.7", null, number, generation, value));

        Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.False(file.Repaired);
    }

    [Fact]
    public void Catalog_version_override_can_preserve_legacy_spellings_under_a_pdf_20_header()
    {
        var file = PdfFile.Load(Build("2.0", "1.7", "+2", "+0", "<< /Target +2 0 R >>"));

        Assert.Equal("1.7", file.Catalog.Get("Version").AsName());
        Assert.IsType<PdfDictionary>(file.GetObject(2));
        Assert.False(file.Repaired);
    }

    static byte[] Build(
        string headerVersion, string? catalogVersion, string number, string generation, string value, string? catalogProbe = null)
    {
        var text = new StringBuilder($"%PDF-{headerVersion}\n");
        var catalog = text.Length;
        var version = catalogVersion is null ? "" : $" /Version /{catalogVersion}";
        var probe = catalogProbe is null ? "" : $" /Probe {catalogProbe}";
        text.Append($"1 0 obj\n<< /Type /Catalog{version}{probe} >>\nendobj\n");
        var target = text.Length;
        text.Append($"{number} {generation} obj\n{value}\nendobj\n");
        var xref = text.Length;
        var entryGeneration = int.TryParse(generation, out var gen) ? gen : 0;
        text.Append($"xref\n0 3\n0000000000 65535 f \n{catalog:D10} 00000 n \n{target:D10} {entryGeneration:D5} n \n");
        text.Append($"trailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    static byte[] BuildObjectStreamMember(string version, string reference)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write($"%PDF-{version}\n");
        var catalog = (int)data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var container = (int)data.Position;
        const string header = "2 0 ";
        var body = $"{header}<< /Target {reference} >>";
        Write($"3 0 obj\n<< /Type /ObjStm /N 1 /First {header.Length} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        var xref = (int)data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong value, ulong index)
        {
            foreach (var field in new[] { kind, value, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)catalog, 0);
        Row(2, 3, 0);
        Row(1, (ulong)container, 0);
        Row(1, (ulong)xref, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size 5 /Index [1 4] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write("\nendstream\nendobj\n");
        Write($"startxref\n{xref}\n%%EOF\n");
        return data.ToArray();
    }
}
