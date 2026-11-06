using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamNestedValueTests
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
    [InlineData("[q]")]
    [InlineData("[obj]")]
    [InlineData("[endobj]")]
    [InlineData("[true false null q]")]
    [InlineData("[>>]")]
    [InlineData("<< /Key q >>")]
    [InlineData("<< /Key endobj >>")]
    [InlineData("[<< /Key [q] >>]")]
    [InlineData("<< /Key [<< /Inner endstream >>] >>")]
    [InlineData("[42 R]")]
    public void Keywords_inside_containers_are_not_pdf_object_values(string value)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            Assert.Equal(forceRepair, file.Repaired);
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Throws<PdfParseException>(() => file.GetObject(2));
            Assert.True(file.Repaired);
        }
    }

    [Theory]
    [InlineData("[1 1.5 true false null /q (endobj)]", typeof(PdfArray))]
    [InlineData("[<< /Key [1 0 R /obj (q)] >>]", typeof(PdfArray))]
    [InlineData("<< /Key [<< /Inner 1 0 R >>] >>", typeof(PdfDictionary))]
    [InlineData("<< /q /obj /Value (endstream) >>", typeof(PdfDictionary))]
    [InlineData("<< /Type /Target /Nested [null % q endobj\n /endobj] >>", typeof(PdfDictionary))]
    public void Nested_objects_references_names_strings_and_comments_remain_supported(string value, Type expectedType)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = FileWithValue(value, forceRepair);
            var member = file.GetObject(2);
            Assert.IsType(expectedType, member);
            Assert.Same(member, file.GetObject(2));
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrequested_invalid_container_does_not_prevent_valid_member_lookup(bool forceRepair)
    {
        var file = PdfFile.Load(ObjectStreamOffsetOrderTests.Build(0,
            ObjectStreamOffsetOrderTests.SecondOffset, forceRepair, secondBody: "[<< /Key q >>]"));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
        Assert.Equal(forceRepair, file.Repaired);
        Assert.Throws<PdfParseException>(() => file.GetObject(5));
        Assert.Equal("First", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
    }
}
