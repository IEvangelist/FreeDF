using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamHexStringTests
{
    static PdfFile FileWithValue(string value, bool forceRepair)
    {
        var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, value: value);
        if (forceRepair)
        {
            var text = Encoding.Latin1.GetString(bytes);
            bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
        }
        return PdfFile.Load(bytes);
    }

    [Theory]
    [InlineData("<GG>")]
    [InlineData("<0G>")]
    [InlineData("<%20>")]
    [InlineData("<0% comment\n0>")]
    [InlineData("<[00]>")]
    [InlineData("<01.2>")]
    [InlineData("<00R>")]
    [InlineData("[<0G>]")]
    [InlineData("<< /Key <0G> >>")]
    public void Compressed_hex_strings_reject_nonhex_nonwhitespace_bytes(string value)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            Assert.Equal(forceRepair, file.Repaired);
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Contains("invalid hexadecimal string", Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.True(file.Repaired);
        }
    }

    [Theory]
    [InlineData("<")]
    [InlineData("<A")]
    [InlineData("<00 \t\r\n")]
    public void Compressed_hex_strings_require_closing_angle_bracket(string value)
    {
        foreach (var forceRepair in new[] { false, true })
            Assert.Throws<PdfParseException>(() => FileWithValue(value, forceRepair).GetObject(2));
    }

    [Theory]
    [InlineData("<>", "")]
    [InlineData("<0>", "00")]
    [InlineData("<A>", "A0")]
    [InlineData("<901FA>", "901FA0")]
    [InlineData("<aBcD>", "ABCD")]
    [InlineData("<00 09\t0A\n0C\f0D\r20\0FF>", "00090A0C0D20FF")]
    public void Complete_hex_strings_preserve_bytes_and_odd_nibble_padding(string value, string expected)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = Assert.IsType<PdfString>(file.GetObject(2));
            Assert.Equal(Convert.FromHexString(expected), member.Bytes);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrequested_invalid_hex_string_preserves_valid_member(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "<0G>"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }

    [Fact]
    public void Default_lexer_preserves_existing_invalid_hex_recovery()
    {
        var value = new PdfLexer(Encoding.Latin1.GetBytes("<0G>")).ReadObject();
        Assert.Equal(new byte[] { 0 }, Assert.IsType<PdfString>(value).Bytes);
    }
}
