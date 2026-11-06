using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamRecoveredLengthTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("22")]
    [InlineData("25")]
    [InlineData("1000")]
    [InlineData("9223372036854775807")]
    public void Recovered_bytes_cannot_silently_replace_a_different_declared_Length(string length)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamLengthSchemaTests.Build(length, repair));
            if (repair) Assert.IsType<PdfNull>(file.GetObject(2));
            else Assert.Equal("object stream recovered byte count disagrees with Length",
                Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.IsType<PdfNull>(file.GetObject(2));
        }
    }

    [Theory]
    [InlineData("23")]
    [InlineData("24")]
    public void Exact_lengths_with_optional_counted_line_endings_remain_supported(string length)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamLengthSchemaTests.Build(length, repair));
            Assert.Equal("Target", Assert.IsType<PdfDictionary>(file.GetObject(2)).Get("Type").AsName());
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("22", false)]
    [InlineData("25", false)]
    [InlineData("22", true)]
    [InlineData("25", true)]
    public void Indirect_Length_and_Type_cannot_bypass_recovered_byte_validation(string length, bool indirectType)
    {
        foreach (var repair in new[] { false, true })
        {
            var source = ObjectStreamLengthTests.Build(false, repair, indirectType);
            var text = Encoding.Latin1.GetString(source).Replace("5 0 obj\n23\n", $"5 0 obj\n{length}\n", StringComparison.Ordinal);
            Assert.NotEqual(Encoding.Latin1.GetString(source), text);
            var file = PdfFile.Load(Encoding.Latin1.GetBytes(text));
            if (repair) Assert.IsType<PdfNull>(file.GetObject(2));
            else Assert.Equal("object stream recovered byte count disagrees with Length",
                Assert.Throws<PdfParseException>(() => file.GetObject(2)).Message);
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.IsType<PdfNull>(file.GetObject(2));
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("1000")]
    public void Ordinary_stream_mismatched_Length_recovery_is_explicit(string length)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(ObjectStreamLengthSchemaTests.Build(length, repair, ordinary: true));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), Assert.IsType<PdfStream>(file.GetObject(3)).RawData);
            Assert.True(file.Repaired);
        }
    }

    [Theory]
    [InlineData(PdfEncryptionMethod.Aes128)]
    [InlineData(PdfEncryptionMethod.Aes256)]
    [InlineData(PdfEncryptionMethod.Rc4_128)]
    public void Encrypted_object_streams_compare_physical_not_decrypted_byte_lengths(PdfEncryptionMethod method)
    {
        var bytes = PdfWriter.Write(new Dictionary<int, PdfObject>
        {
            [1] = new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("Catalog"), ["Probe"] = new PdfRef(2, 0)
            }),
            [2] = new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("Target"), ["Value"] = new PdfString("payload"u8.ToArray())
            })
        }, 1, null, options: new PdfWriteOptions
        {
            ObjectStreams = true,
            Encryption = new PdfEncryption { Method = method, UserPassword = "secret", OwnerPassword = "owner" }
        });
        foreach (var repair in new[] { false, true })
        {
            var text = Encoding.Latin1.GetString(bytes);
            var input = repair
                ? Encoding.Latin1.GetBytes(text[..(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)] + "-1\n%%EOF\n")
                : bytes;
            var file = PdfFile.Load(input, "secret");
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Equal("payload", Assert.IsType<PdfString>(target.Get("Value")).Text);
            var container = Assert.IsType<PdfStream>(file.GetObject((int)file.Xref.Values.First(e => e.Compressed).OffsetOrStream));
            var declared = Assert.IsType<PdfInteger>(file.Resolve(container.Dict.Get("Length"))).Value;
            if (method == PdfEncryptionMethod.Rc4_128) Assert.Equal(declared, container.RawData.LongLength);
            else Assert.NotEqual(declared, container.RawData.LongLength);
            Assert.Equal(repair, file.Repaired);
        }
    }
}
