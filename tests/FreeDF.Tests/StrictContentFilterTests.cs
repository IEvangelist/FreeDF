using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class StrictContentFilterTests
{
    [Theory]
    [InlineData(">", "")]
    [InlineData("61 62\t63\r\n>", "abc")]
    [InlineData("61\0\f62>", "ab")]
    [InlineData("610062>", "a\0b")]
    [InlineData("6162F>", "ab\u00f0")]
    [InlineData("61>ignored", "a")]
    public void StrictHexAcceptsCompleteData(string encoded, string expected)
    {
        Assert.Equal(Encoding.Latin1.GetBytes(expected), Decode(encoded, "ASCIIHexDecode"));
    }

    [Theory]
    [InlineData("61x62>")]
    [InlineData("61")]
    [InlineData("<61>")]
    [InlineData("61\u000b62>")]
    public void StrictHexRejectsInvalidOrUnterminatedData(string encoded) =>
        Assert.Throws<InvalidDataException>(() => Decode(encoded, "ASCIIHexDecode"));

    [Theory]
    [InlineData("~>", "")]
    [InlineData("z~>", "\0\0\0\0")]
    [InlineData("!!!!!~>", "\0\0\0\0")]
    [InlineData("!!~>", "\0")]
    [InlineData("!!!~>", "\0\0")]
    [InlineData("!!!!~>", "\0\0\0")]
    [InlineData("s8W-!~>", "\u00ff\u00ff\u00ff\u00ff")]
    [InlineData("rr~>", "\u00ff")]
    [InlineData("s8N~>", "\u00ff\u00ff")]
    [InlineData("s8W*~>", "\u00ff\u00ff\u00ff")]
    [InlineData("BOu!rDZ~>", "hello")]
    [InlineData("BOu!r\nD Z~>ignored", "hello")]
    [InlineData("BOu!r\0\fDZ~>", "hello")]
    public void Strict85AcceptsCompleteGroupsAndPartialGroups(string encoded, string expected) =>
        Assert.Equal(Encoding.Latin1.GetBytes(expected), Decode(encoded, "ASCII85Decode"));

    [Theory]
    [InlineData("!~>")]
    [InlineData("!z~>")]
    [InlineData("uuuuu~>")]
    [InlineData("s8W-\"~>")]
    [InlineData("uu~>")]
    [InlineData("v~>")]
    [InlineData("!!!!!~")]
    [InlineData("!!!!!~x")]
    [InlineData("!!!!!")]
    [InlineData("<~!!!!!~>")]
    [InlineData("!!!!!\u000b~>")]
    public void Strict85RejectsImpossibleOrUnterminatedData(string encoded) =>
        Assert.Throws<InvalidDataException>(() => Decode(encoded, "ASCII85Decode"));

    [Theory]
    [InlineData("61x62", "ASCIIHexDecode", "ab")]
    [InlineData("<~BOu!rDZ~>", "ASCII85Decode", "hello")]
    [InlineData("BOu!rDZ", "ASCII85Decode", "hello")]
    public void ExistingReaderRecoveryIsUnchanged(string encoded, string filter, string expected)
    {
        var result = PdfFilters.Decode(Encoding.ASCII.GetBytes(encoded), [(filter, null)], x => x);
        Assert.Equal(Encoding.ASCII.GetBytes(expected), result.Data);
    }

    [Fact]
    public void StrictAsciiMayPrecedeStrictFlate()
    {
        var expected = Encoding.ASCII.GetBytes("BT /F1 12 Tf 3 Tr (A) Tj ET");
        var compressed = PdfFilters.DeflateEncode(expected);
        var encoded = Encoding.ASCII.GetBytes(Convert.ToHexString(compressed) + ">");
        Assert.Equal(expected, PdfFilters.Decode(encoded, [("ASCIIHexDecode", null), ("FlateDecode", null)],
            x => x, strictContentFilters: true).Data);
    }

    static byte[] Decode(string encoded, string filter) =>
        PdfFilters.Decode(Encoding.Latin1.GetBytes(encoded), [(filter, null)], x => x, strictContentFilters: true).Data;
}
