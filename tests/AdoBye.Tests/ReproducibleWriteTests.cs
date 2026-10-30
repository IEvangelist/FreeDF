using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

public class ReproducibleWriteTests
{
    static PdfEncryption Encryption(Func<int, byte[]>? random) => new() { UserPassword = "u", OwnerPassword = "o", RandomBytes = random };

    static byte[] Constant(int n) => Enumerable.Repeat((byte)7, n).ToArray();

    [Fact]
    public void ConstantRandomBytesAndFileIdMakeEncryptedOutputReproducible()
    {
        byte[] Write() => PdfEditor.Create().Save(options: new PdfWriteOptions { Encryption = Encryption(Constant), FileId = new byte[16] });
        Assert.Equal(Write(), Write());
        Assert.NotEqual(PdfEditor.Create().Save(options: new PdfWriteOptions { Encryption = Encryption(null) }), PdfEditor.Create().Save(options: new PdfWriteOptions { Encryption = Encryption(null) }));
    }

    [Fact]
    public void RandomBytesMustReturnTheRequestedLength() =>
        Assert.Throws<ArgumentException>(() => PdfEditor.Create().Save(options: new PdfWriteOptions { Encryption = Encryption(n => new byte[n - 1]) }));

    [Fact]
    public void FileIdMustBeSixteenBytesAndIsCopied()
    {
        Assert.Throws<ArgumentException>(() => new PdfWriteOptions { FileId = new byte[8] });
        var id = new byte[16];
        var options = new PdfWriteOptions { FileId = id };
        id[0] = 9;
        Assert.Equal(0, options.FileId![0]);
    }

    [Fact]
    public void CatalogEntryRejectsManagedKeysAndSetObjectOnlyAcceptsReservedObjects()
    {
        var editor = PdfEditor.Create();
        Assert.Throws<ArgumentException>(() => editor.SetCatalogEntry("Pages", new PdfName("x")));
        Assert.Throws<ArgumentException>(() => editor.SetCatalogEntry("Type", new PdfName("x")));
        editor.SetCatalogEntry("PageMode", new PdfName("UseOutlines"));

        Assert.Throws<ArgumentException>(() => editor.SetObject(new PdfRef(1, 0), PdfNull.Instance));
        var added = editor.AddObject(new PdfDictionary());
        Assert.Throws<ArgumentException>(() => editor.SetObject(added, PdfNull.Instance));
        var reserved = editor.ReserveObject();
        Assert.Throws<ArgumentException>(() => editor.SetObject(new PdfRef(reserved.Number, 1), PdfNull.Instance));
        editor.SetObject(reserved, new PdfInteger(5));
    }
}
