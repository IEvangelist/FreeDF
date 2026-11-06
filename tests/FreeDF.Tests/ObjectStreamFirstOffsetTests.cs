using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamFirstOffsetTests
{
    [Theory]
    [InlineData("null ")]
    [InlineData("42 ")]
    [InlineData("/Ignored ")]
    [InlineData("q ")]
    [InlineData(" ")]
    [InlineData("% skipped comment\n")]
    public void First_member_offset_must_be_zero_relative_to_First(string prefix)
    {
        var header = $"2 {prefix.Length} ";
        foreach (var forceRepair in new[] { false, true })
        {
            var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true,
                header: header, first: header.Length.ToString(), value: prefix + "<< /Type /Target >>");
            if (forceRepair)
            {
                var text = Encoding.Latin1.GetString(bytes);
                bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
            }
            var file = PdfFile.Load(bytes);
            Assert.IsType<PdfNull>(file.GetObject(2));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\0\t\r\n\f ")]
    [InlineData("% leading comment\n")]
    public void Zero_relative_offset_preserves_existing_leading_trivia_support(string prefix)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var bytes = XrefStreamEntryRangeTests.Build(2, compressed: true, value: prefix + "<< /Type /Target >>");
            if (forceRepair)
            {
                var text = Encoding.Latin1.GetString(bytes);
                bytes = Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n");
            }
            var file = PdfFile.Load(bytes);
            var target = Assert.IsType<PdfDictionary>(file.GetObject(2));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("null ")]
    [InlineData(" ")]
    public void Nonzero_first_offset_cannot_replace_uncompressed_identity_in_recovery(string prefix)
    {
        var file = PdfFile.Load(ObjectStreamRecoveryHeaderTests.Build($"2 {prefix.Length} ",
            prefix + "<< /Type /Compressed >>"));
        Assert.True(file.Repaired);
        Assert.False(file.Xref[2].Compressed);
        Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }
}
