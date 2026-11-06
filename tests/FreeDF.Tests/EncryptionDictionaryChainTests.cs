using PortableDocument.Core;

namespace FreeDF.Tests;

public class EncryptionDictionaryChainTests
{
    [Theory]
    [InlineData(1, "", "")]
    [InlineData(2, "", "")]
    [InlineData(4, "", "")]
    [InlineData(1, "secret", "secret")]
    [InlineData(2, "secret", "owner")]
    [InlineData(4, "secret", "wrong")]
    public void Compressed_terminal_dictionary_is_malformed_even_through_uncompressed_aliases(int hops, string user, string supplied)
    {
        foreach (var repair in new[] { false, true })
        {
            var bytes = ObjectStreamEncryptionDictionaryTests.Build(user, true, repair, hops);
            var error = Assert.Throws<PdfParseException>(() => PdfFile.Load(bytes, supplied));
            Assert.IsNotType<PdfPasswordException>(error);
        }
    }

    [Theory]
    [InlineData(1, "", "")]
    [InlineData(2, "secret", "secret")]
    [InlineData(4, "secret", "owner")]
    [InlineData(31, "", "")]
    public void Valid_uncompressed_aliases_preserve_passwords_and_terminal_dictionary_bytes(int hops, string user, string supplied)
    {
        foreach (var repair in new[] { false, true })
        {
            var baseline = PdfFile.Load(ObjectStreamEncryptionDictionaryTests.Build(user, false, repair), supplied);
            var file = PdfFile.Load(ObjectStreamEncryptionDictionaryTests.Build(user, false, repair, hops), supplied);
            Assert.True(file.IsEncrypted);
            Assert.Equal(repair, file.Repaired);
            Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
            var original = Assert.IsType<PdfDictionary>(baseline.GetObject(2));
            var terminal = Assert.IsType<PdfDictionary>(file.GetObject(2));
            foreach (var key in new[] { "O", "U" })
                Assert.Equal(Assert.IsType<PdfString>(original.Get(key)).Bytes, Assert.IsType<PdfString>(terminal.Get(key)).Bytes);
            Assert.Same(terminal, file.Resolve(file.Trailer.Get("Encrypt")));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Valid_aliases_preserve_incorrect_password_errors(int hops)
    {
        foreach (var repair in new[] { false, true })
            Assert.Throws<PdfPasswordException>(() => PdfFile.Load(
                ObjectStreamEncryptionDictionaryTests.Build("secret", false, repair, hops), "wrong"));
    }

    [Fact]
    public void Existing_reference_depth_limit_is_not_extended()
    {
        foreach (var repair in new[] { false, true })
            Assert.Throws<PdfParseException>(() => PdfFile.Load(
                ObjectStreamEncryptionDictionaryTests.Build("", false, repair, 32)));
    }
}
