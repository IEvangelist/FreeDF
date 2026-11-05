using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamExternalFileTests
{
    [Theory]
    [InlineData("(external-object-stream.bin)", "null")]
    [InlineData("<< /Type /Filespec /F (external-object-stream.bin) >>", "null")]
    [InlineData("/ASCIIHexDecode", "null")]
    [InlineData("42", "null")]
    [InlineData("true", "null")]
    [InlineData("[]", "null")]
    [InlineData("5 0 R", "(external-object-stream.bin)")]
    [InlineData("5 0 R", "<< /Type /Filespec /F (external-object-stream.bin) >>")]
    [InlineData("5 0 R", "5 0 R")]
    public void External_payload_cannot_be_substituted_by_inline_member_bytes(string external, string indirect)
    {
        foreach (var repair in new[] { false, true })
            foreach (var filtered in new[] { false, true })
            {
                var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filtered ? "/ASCIIHexDecode" : null,
                    indirect, filtered, repair, externalFile: external));
                Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
                Assert.True(file.Repaired);
                Assert.False(file.Xref.ContainsKey(2));
                Assert.IsType<PdfNull>(file.GetObject(2));
            }
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("null", "null")]
    [InlineData("7 0 R", "null")]
    [InlineData("5 0 R", "null")]
    public void Absent_or_null_external_file_preserves_inline_payload(string? external, string indirect)
    {
        foreach (var repair in new[] { false, true })
            foreach (var filtered in new[] { false, true })
            {
                var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filtered ? "/ASCIIHexDecode" : null,
                    indirect, filtered, repair, externalFile: external));
                var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
                Assert.Equal("Target", target.Get("Type").AsName());
                Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
                Assert.Equal(repair, file.Repaired);
            }
    }

    [Theory]
    [InlineData("(external-object-stream.bin)", "/ASCIIHexDecode", true)]
    [InlineData("<< /Type /Filespec /F (external-object-stream.bin) >>", "/ASCIIHexDecode", true)]
    [InlineData("/ASCIIHexDecode", null, true)]
    public void Ordinary_stream_external_metadata_recovery_is_unchanged(string external, string? filter, bool hex)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build(filter, "null", hex, repair,
                ordinary: true, externalFile: external));
            var stream = Assert.IsType<PdfStream>(file.GetObject(3));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), file.DecodeStream(stream));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    public void Null_external_aliases_preserve_the_existing_32_reference_limit(int aliases)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamFilterNameTests.Build("/ASCIIHexDecode", "null", true,
                repair, filterAliases: aliases, externalFile: "5 0 R"));
            var target = file.Resolve(file.Catalog.Get("Probe"));
            if (aliases < 32)
            {
                Assert.Equal("Target", Assert.IsType<PdfDictionary>(target).Get("Type").AsName());
                Assert.Equal(repair, file.Repaired);
            }
            else
            {
                Assert.IsType<PdfNull>(target);
                Assert.True(file.Repaired);
                Assert.False(file.Xref.ContainsKey(2));
            }
        }
    }
}
