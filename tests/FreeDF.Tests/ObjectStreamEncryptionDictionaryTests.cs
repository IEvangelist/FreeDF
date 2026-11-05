using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamEncryptionDictionaryTests
{
    internal static byte[] Build(string userPassword, bool compressed, bool forceRepair, int referenceHops = 0)
    {
        var id = new byte[16];
        var source = PdfWriter.Write(new Dictionary<int, PdfObject>
        {
            [1] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Catalog") })
        }, 1, null, options: new PdfWriteOptions
        {
            FileId = id,
            Encryption = new PdfEncryption
            {
                Method = PdfEncryptionMethod.Rc4_40, UserPassword = userPassword, OwnerPassword = "owner"
            }
        });
        var file = PdfFile.Load(source, userPassword);
        var encrypt = Assert.IsType<PdfRef>(file.Trailer.Get("Encrypt"));
        var text = Encoding.Latin1.GetString(source);
        var start = text.IndexOf('\n', (int)file.Xref[encrypt.Number].OffsetOrStream) + 1;
        var dictionary = text[start..text.IndexOf("\nendobj", start, StringComparison.Ordinal)];
        using var data = new MemoryStream();
        void Write(string value) => data.Write(Encoding.Latin1.GetBytes(value));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 1 0 R >>\nendobj\n");
        var encryption = data.Position;
        if (!compressed) Write($"2 0 obj\n{dictionary}\nendobj\n");
        var container = data.Position;
        if (compressed)
        {
            var body = "2 0 " + dictionary;
            Write($"3 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");
        }
        var aliases = new List<long>();
        for (var index = 0; index < referenceHops; index++)
        {
            aliases.Add(data.Position);
            Write($"{5 + index} 0 obj\n{(index + 1 == referenceHops ? 2 : 6 + index)} 0 R\nendobj\n");
        }
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong value, ulong index)
        {
            foreach (var field in new[] { kind, value, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)catalog, 0);
        Row(compressed ? 2UL : 1UL, compressed ? 3UL : (ulong)encryption, 0);
        Row(compressed ? 1UL : 0UL, compressed ? (ulong)container : 0, 0);
        Row(1, (ulong)xref, 0);
        foreach (var offset in aliases) Row(1, (ulong)offset, 0);
        var hex = Convert.ToHexString(id);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Encrypt {(referenceHops == 0 ? 2 : 5)} 0 R /ID [<{hex}><{hex}>] /Size {5 + referenceHops} /Index [1 {4 + referenceHops}] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(forceRepair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("", "", true)]
    [InlineData("secret", "secret", false)]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "owner", false)]
    [InlineData("secret", "owner", true)]
    [InlineData("secret", "wrong", false)]
    [InlineData("secret", "wrong", true)]
    public void Compressed_encryption_dictionary_is_malformed_not_a_password_failure(string user, string supplied, bool forceRepair)
    {
        var bytes = Build(user, true, forceRepair);
        Assert.Throws<PdfParseException>(() => PdfFile.Load(bytes, supplied));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("secret", "secret")]
    [InlineData("secret", "owner")]
    public void Valid_uncompressed_encryption_dictionary_supports_user_and_owner_passwords(string user, string supplied)
    {
        foreach (var forceRepair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(user, false, forceRepair), supplied);
            Assert.True(file.IsEncrypted);
            Assert.Equal("Catalog", file.Catalog.Get("Type").AsName());
            Assert.Equal(forceRepair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("secret")]
    public void Valid_uncompressed_encryption_dictionary_preserves_password_failures(string user)
    {
        foreach (var forceRepair in new[] { false, true })
            Assert.Throws<PdfPasswordException>(() => PdfFile.Load(Build(user, false, forceRepair), "wrong"));
    }
}
