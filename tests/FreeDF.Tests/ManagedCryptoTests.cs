using System.Security.Cryptography;
using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

/// <summary>The pure-managed crypto layer (browser WASM) against published vectors, the platform implementation and real encrypted PDFs.</summary>
public class ManagedCryptoTests
{
    static byte[] Hex(string h) => Convert.FromHexString(h.Replace(" ", ""));
    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    const string Long = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";

    [Fact]
    public void Hash_vectors()
    {
        Assert.Equal(Hex("d41d8cd98f00b204e9800998ecf8427e"), ManagedCrypto.Md5([]));
        Assert.Equal(Hex("900150983cd24fb0d6963f7d28e17f72"), ManagedCrypto.Md5(Ascii("abc")));
        Assert.Equal(Hex("f96b697d7cb7938d525a2f31aaf161d0"), ManagedCrypto.Md5(Ascii("message digest")));
        Assert.Equal(Hex("a9993e364706816aba3e25717850c26c9cd0d89d"), ManagedCrypto.Sha1(Ascii("abc")));
        Assert.Equal(Hex("84983e441c3bd26ebaae4aa1f95129e5e54670f1"), ManagedCrypto.Sha1(Ascii(Long)));
        Assert.Equal(Hex("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"), ManagedCrypto.Sha256([]));
        Assert.Equal(Hex("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"), ManagedCrypto.Sha256(Ascii("abc")));
        Assert.Equal(Hex("248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"), ManagedCrypto.Sha256(Ascii(Long)));
        Assert.Equal(Hex("cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7"), ManagedCrypto.Sha384(Ascii("abc")));
        Assert.Equal(Hex("ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"), ManagedCrypto.Sha512(Ascii("abc")));
    }

    [Fact]
    public void Hmac_and_rc4_vectors()
    {
        Assert.Equal(Hex("b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7"), ManagedCrypto.HmacSha256(Enumerable.Repeat((byte)0x0b, 20).ToArray(), Ascii("Hi There")));
        Assert.Equal(Hex("bbf316e8d940af0ad3"), ManagedCrypto.Rc4(Ascii("Key"), Ascii("Plaintext")));
    }

    [Theory]
    [InlineData("000102030405060708090a0b0c0d0e0f", "69c4e0d86a7b0430d8cdb78070b4c55a")]
    [InlineData("000102030405060708090a0b0c0d0e0f1011121314151617", "dda97ca4864cdfe06eaf70a0ec0d7191")]
    [InlineData("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", "8ea2b7ca516745bfeafc49904b496089")]
    public void Aes_fips197_vectors(string key, string cipher)
    {
        var aes = new ManagedCrypto.Aes(Hex(key));
        var plain = Hex("00112233445566778899aabbccddeeff");
        Assert.Equal(Hex(cipher), aes.EncryptEcb(plain));
        Assert.Equal(plain, aes.DecryptEcb(Hex(cipher)));
    }

    [Fact]
    public void Aes_cbc_sp800_38a_vector()
    {
        var aes = new ManagedCrypto.Aes(Hex("2b7e151628aed2a6abf7158809cf4f3c"));
        var iv = Hex("000102030405060708090a0b0c0d0e0f");
        var plain = Hex("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51");
        var cipher = Hex("7649abac8119b246cee98e9b12e9197d5086cb9b507219ee95db113a917678b2");
        Assert.Equal(cipher, aes.EncryptCbcNoPad(plain, iv));
        Assert.Equal(plain, aes.DecryptCbcNoPad(cipher, iv));
    }

    [Fact]
    public void Pkcs7_padding_is_validated()
    {
        var key = new byte[16]; var iv = new byte[16];
        var aes = new ManagedCrypto.Aes(key);
        Assert.Equal([1, 2, 3], aes.DecryptCbcPkcs7(aes.EncryptCbcPkcs7([1, 2, 3], iv), iv));
        Assert.Empty(aes.DecryptCbcPkcs7(aes.EncryptCbcPkcs7([], iv), iv));
        foreach (var bad in new byte[][] { [], [1, 2, 3] })
            Assert.ThrowsAny<CryptographicException>(() => aes.DecryptCbcPkcs7(bad, iv));
        var forged = aes.EncryptCbcNoPad(new byte[16], iv); // block of zero bytes: padding value 0 is invalid
        Assert.Throws<CryptographicException>(() => aes.DecryptCbcPkcs7(forged, iv));
        var inconsistent = aes.EncryptCbcNoPad([.. new byte[13], 2, 2, 3], iv);
        Assert.Throws<CryptographicException>(() => aes.DecryptCbcPkcs7(inconsistent, iv));
        Assert.Throws<CryptographicException>(() => new ManagedCrypto.Aes(new byte[15]));
    }

    [Fact]
    public void Managed_matches_platform_over_random_inputs()
    {
        var rnd = new Random(32000);
        for (var n = 0; n < 150; n++)
        {
            var data = new byte[n < 130 ? n : rnd.Next(130, 600)]; rnd.NextBytes(data);
            Assert.Equal(MD5.HashData(data), ManagedCrypto.Md5(data));
            Assert.Equal(SHA1.HashData(data), ManagedCrypto.Sha1(data));
            Assert.Equal(SHA256.HashData(data), ManagedCrypto.Sha256(data));
            Assert.Equal(SHA384.HashData(data), ManagedCrypto.Sha384(data));
            Assert.Equal(SHA512.HashData(data), ManagedCrypto.Sha512(data));
            Assert.Equal(HMACSHA256.HashData(data.Take(Math.Min(n, 90)).ToArray(), data), ManagedCrypto.HmacSha256(data.Take(Math.Min(n, 90)).ToArray(), data));
            var key = new byte[(n % 3) switch { 0 => 16, 1 => 24, _ => 32 }]; rnd.NextBytes(key);
            var iv = new byte[16]; rnd.NextBytes(iv);
            var blocks = data[..(data.Length / 16 * 16)];
            using var platform = Aes.Create(); platform.Key = key;
            var managed = new ManagedCrypto.Aes(key);
            Assert.Equal(platform.EncryptEcb(blocks, PaddingMode.None), managed.EncryptEcb(blocks));
            Assert.Equal(platform.EncryptCbc(blocks, iv, PaddingMode.None), managed.EncryptCbcNoPad(blocks, iv));
            Assert.Equal(platform.DecryptCbc(blocks, iv, PaddingMode.None), managed.DecryptCbcNoPad(blocks, iv));
            var padded = platform.EncryptCbc(data, iv, PaddingMode.PKCS7);
            Assert.Equal(padded, managed.EncryptCbcPkcs7(data, iv));
            Assert.Equal(data, managed.DecryptCbcPkcs7(padded, iv));
        }
    }

    static T WithManaged<T>(bool managed, Func<T> body)
    {
        var old = PdfCrypto.UseManaged;
        PdfCrypto.UseManaged = managed;
        try { return body(); } finally { PdfCrypto.UseManaged = old; }
    }

    [Fact]
    public void Algorithm_2B_hash_is_identical_in_both_providers()
    {
        var pwd = Ascii("user123"); var salt = Hex("0102030405060708"); var udata = new byte[48];
        Assert.Equal(WithManaged(false, () => PdfDecryptor.Hash2B(pwd, salt, udata)), WithManaged(true, () => PdfDecryptor.Hash2B(pwd, salt, udata)));
    }

    public static IEnumerable<object[]> Methods() => Enum.GetValues<PdfEncryptionMethod>().Select(m => new object[] { m });

    [Theory, MemberData(nameof(Methods))]
    public void Files_written_by_one_provider_open_in_the_other(PdfEncryptionMethod method)
    {
        byte[] Write(bool managed) => WithManaged(managed, () =>
        {
            var e = PdfEditor.Open(TestPdf.Build("1 0 0 rg 50 50 100 100 re f"));
            e.SetInfo("Title", "Browser secret");
            return e.Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { UserPassword = "pässword", OwnerPassword = "own", Method = method } });
        });
        foreach (var (writer, reader) in new[] { (true, false), (false, true), (true, true) })
        {
            var bytes = Write(writer);
            Assert.Equal("Browser secret", WithManaged(reader, () => PdfDocument.Load(bytes, "pässword").Info("Title")));
            Assert.True(WithManaged(reader, () => PdfDocument.Load(bytes, "own").File.Decryptor!.IsOwner));
            Assert.Throws<PdfPasswordException>(() => WithManaged(reader, () => PdfDocument.Load(bytes, "nope")));
        }
    }

    [Theory]
    [InlineData("17-encrypted-user-password.pdf", "user123", false)]
    [InlineData("17-encrypted-user-password.pdf", "owner-user123", true)]
    [InlineData("18-encrypted-owner-restricted.pdf", "", false)]
    [InlineData("18-encrypted-owner-restricted.pdf", "owner123", true)]
    public void Encrypted_samples_open_with_managed_crypto(string file, string password, bool owner)
    {
        var path = Path.Combine(SamplesTests.SamplesDir()!, file);
        var bytes = File.ReadAllBytes(path);
        var reference = WithManaged(false, () => PdfDocument.Load(bytes, password));
        var doc = WithManaged(true, () => PdfDocument.Load(bytes, password));
        Assert.Equal(owner, doc.File.Decryptor!.IsOwner);
        Assert.Equal(reference.Pages.Count, doc.Pages.Count);
        Assert.Equal(reference.File.Decryptor!.Permissions, doc.File.Decryptor.Permissions);
        Assert.Equal(reference.Info("Title"), doc.Info("Title"));
        Assert.Throws<PdfPasswordException>(() => WithManaged(true, () => PdfDocument.Load(bytes, "definitely wrong")));
    }

    [Fact]
    public void Malformed_encrypt_dictionaries_fail_cleanly_with_managed_crypto()
    {
        var protectedBytes = WithManaged(true, () => PdfEditor.Open(TestPdf.Build("0 0 m")).Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { UserPassword = "u" } }));
        var text = Encoding.Latin1.GetString(protectedBytes);
        foreach (var key in new[] { "/UE", "/OE", "/U ", "/O ", "/Perms" })
        {
            var at = text.IndexOf(key + "<", StringComparison.Ordinal);
            if (at < 0) at = text.IndexOf(key, StringComparison.Ordinal);
            Assert.True(at >= 0, key);
            var mutated = Encoding.Latin1.GetBytes(text.Remove(at, key.Length).Insert(at, "/Zz" + new string('z', key.Length - 3)));
            var ex = Record.Exception(() => WithManaged(true, () => PdfDocument.Load(mutated, "u")));
            Assert.True(ex is null or PdfParseException or ArgumentException or InvalidDataException or CryptographicException, $"{key}: {ex}");
        }
        foreach (var length in new[] { 0, 1, 15, 16, 31, 47 })
        {
            var ex = Record.Exception(() => WithManaged(true, () => PdfDocument.Load(protectedBytes[..Math.Min(protectedBytes.Length, length * 3)], "u")));
            Assert.True(ex is null or PdfParseException or ArgumentException or InvalidDataException, ex?.ToString());
        }
    }
}
