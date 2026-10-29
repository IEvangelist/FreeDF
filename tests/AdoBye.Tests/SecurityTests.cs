using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AdoBye.Core;
using AdoBye.Document;
using Xunit;

namespace AdoBye.Tests;

public class SecurityTests
{
    static byte[] Base(string title = "Secret Title")
    {
        var e = PdfEditor.Open(TestPdf.Build("1 0 0 rg 50 50 100 100 re f"));
        e.SetInfo("Title", title);
        return e.Save();
    }

    internal static X509Certificate2 Cert(string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=" + cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.NonRepudiation, true));
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx, "pw"), "pw", X509KeyStorageFlags.Exportable);
    }

    public static IEnumerable<object[]> Methods() =>
        from m in Enum.GetValues<PdfEncryptionMethod>() from os in new[] { false, true } select new object[] { m, os };

    [Theory, MemberData(nameof(Methods))]
    public void Standard_handler_roundtrip(PdfEncryptionMethod method, bool objStreams)
    {
        var e = PdfEditor.Open(Base());
        var bytes = e.Save(options: new PdfWriteOptions
        {
            Compress = true, ObjectStreams = objStreams,
            Encryption = new PdfEncryption { UserPassword = "u", OwnerPassword = "o", Method = method, Permissions = PdfPermissionHelper.ToP(PdfPermissions.Print) },
        });
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Load(bytes));
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Load(bytes, "wrong"));
        var user = PdfDocument.Load(bytes, "u");
        Assert.Equal("Secret Title", user.Info("Title"));
        Assert.Equal(1, user.Pages.Count);
        var dec = user.File.Decryptor!;
        Assert.Equal(method, dec.EncryptionMethod);
        Assert.False(dec.IsOwner);
        Assert.Equal(PdfPermissions.Print, dec.PermissionFlags);
        var owner = PdfDocument.Load(bytes, "o");
        Assert.True(owner.File.Decryptor!.IsOwner);
        Assert.True(owner.File.Decryptor.IsAllowed(PdfPermissions.Modify));
    }

    [Fact]
    public void Empty_user_password_opens_and_rc4_dictionary_values()
    {
        var bytes = PdfEditor.Open(Base()).Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { OwnerPassword = "o", Method = PdfEncryptionMethod.Rc4_40 } });
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/V 1", text); Assert.Contains("/R 2", text);
        Assert.Equal("Secret Title", PdfDocument.Load(bytes).Info("Title"));
    }

    [Fact]
    public void Permissions_are_enforced_by_the_editor_for_non_owners()
    {
        var bytes = PdfEditor.Open(Base()).Save(options: new PdfWriteOptions
        {
            Encryption = new PdfEncryption { UserPassword = "u", OwnerPassword = "o", Permissions = PdfPermissionHelper.ToP(PdfPermissions.Print | PdfPermissions.Copy) },
        });
        var user = PdfEditor.Open(bytes, "u");
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Copy, user.Permissions);
        Assert.True(user.IsAllowed(PdfPermissions.Copy));
        Assert.False(user.IsAllowed(PdfPermissions.Modify));
        user.SetInfo("Title", "free while not enforced");
        user.EnforcePermissions = true;
        Assert.Throws<PdfPermissionException>(() => user.SetInfo("Title", "x"));
        Assert.Throws<PdfPermissionException>(() => user.DeletePage(0));
        var owner = PdfEditor.Open(bytes, "o");
        owner.EnforcePermissions = true;
        owner.SetInfo("Title", "ok");
    }

    public static IEnumerable<object[]> PubMethods() =>
        [[PdfEncryptionMethod.Aes256], [PdfEncryptionMethod.Aes128], [PdfEncryptionMethod.Rc4_128]];

    [Theory, MemberData(nameof(PubMethods))]
    public void Public_key_handler_roundtrip(PdfEncryptionMethod method)
    {
        using var alice = Cert("alice"); using var bob = Cert("bob"); using var eve = Cert("eve");
        var bytes = PdfEditor.Open(Base()).Save(options: new PdfWriteOptions
        {
            Compress = true, ObjectStreams = method == PdfEncryptionMethod.Aes256,
            Encryption = new PdfEncryption
            {
                Method = method,
                Recipients = [new PdfRecipient(alice, PdfPermissionHelper.ToP(PdfPermissions.Print)), new PdfRecipient(bob)],
            },
        });
        Assert.Contains("/Adobe.PubSec", Encoding.Latin1.GetString(bytes));
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Load(bytes));
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Load(bytes, "", eve));
        var a = PdfDocument.Load(bytes, "", alice);
        Assert.Equal("Secret Title", a.Info("Title"));
        Assert.True(a.File.Decryptor!.IsPublicKey);
        Assert.Equal(PdfPermissions.Print, a.File.Decryptor.PermissionFlags);
        var b = PdfDocument.Load(bytes, "", bob);
        Assert.Equal("Secret Title", b.Info("Title"));
        Assert.Equal(PdfPermissions.All, b.File.Decryptor!.PermissionFlags);
    }

    [Fact]
    public void Signing_produces_valid_detached_cms_signature()
    {
        using var cert = Cert("Test Signer");
        var signed = PdfEditor.Open(Base()).Sign(new PdfSignOptions
        {
            Certificate = cert, Reason = "Approval", Location = "Lab",
            Rect = new PdfSignatureRect(20, 20, 160, 50),
        });
        var sigs = PdfEditor.VerifySignatures(signed);
        var s = Assert.Single(sigs);
        Assert.Equal("Signature1", s.FieldName);
        Assert.Equal("adbe.pkcs7.detached", s.SubFilter);
        Assert.True(s.ByteRangeValid); Assert.True(s.DigestValid); Assert.True(s.SignatureValid, s.Error);
        Assert.True(s.CoversWholeDocument); Assert.False(s.ModifiedAfterSigning);
        Assert.Equal("Test Signer", s.SignerName);
        Assert.Equal("Approval", s.Reason);
        Assert.NotNull(s.SigningTime);
        Assert.Equal("Secret Title", PdfDocument.Load(signed).Info("Title"));
        // Visible appearance is attached to the page.
        Assert.Contains("/Widget", Encoding.Latin1.GetString(signed));
    }

    [Fact]
    public void Tampering_and_later_updates_are_detected()
    {
        using var cert = Cert("Test Signer");
        var signed = PdfEditor.Open(Base()).Sign(new PdfSignOptions { Certificate = cert });
        var s0 = PdfEditor.VerifySignatures(signed)[0];

        var tampered = (byte[])signed.Clone();
        var i = Encoding.Latin1.GetString(tampered).IndexOf("/Type /Catalog", StringComparison.Ordinal);
        if (i < 0) i = Encoding.Latin1.GetString(tampered).IndexOf("/Catalog", StringComparison.Ordinal);
        Assert.True(i > 0 && i < s0.ByteRange[1]);
        tampered[i + 2] ^= 0x01;
        var t = PdfSignatures.Verify(tampered);
        Assert.Single(t);
        Assert.False(t[0].DigestValid); Assert.False(t[0].SignatureValid);

        var edited = PdfEditor.Open(signed);
        edited.SetInfo("Title", "Changed after signing");
        var updated = edited.SaveIncremental();
        var u = Assert.Single(PdfEditor.VerifySignatures(updated));
        Assert.True(u.SignatureValid, u.Error);
        Assert.False(u.CoversWholeDocument);
        Assert.True(u.ModifiedAfterSigning);
    }

    [Fact]
    public void Second_signature_keeps_first_cryptographically_valid_but_not_whole_document()
    {
        using var c1 = Cert("First"); using var c2 = Cert("Second");
        var one = PdfEditor.Open(Base()).Sign(new PdfSignOptions { Certificate = c1, FieldName = "A" });
        var two = PdfEditor.SignFile(one, new PdfSignOptions { Certificate = c2, FieldName = "B", Rect = new PdfSignatureRect(10, 100, 120, 40) });
        var sigs = PdfEditor.VerifySignatures(two);
        Assert.Equal(2, sigs.Count);
        var a = sigs.Single(x => x.FieldName == "A"); var b = sigs.Single(x => x.FieldName == "B");
        Assert.True(a.SignatureValid); Assert.False(a.CoversWholeDocument);
        Assert.True(b.SignatureValid); Assert.True(b.CoversWholeDocument);
        Assert.Equal("Second", b.SignerName);
    }

    /// <summary>Writes samples for validation with qpdf/pikepdf/pyHanko/openssl when PDF_SECURITY_OUT names a directory; otherwise a no-op.</summary>
    [Fact]
    public void Export_samples_for_external_validation()
    {
        var dir = Environment.GetEnvironmentVariable("PDF_SECURITY_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var m in Enum.GetValues<PdfEncryptionMethod>())
            foreach (var os in new[] { false, true })
                File.WriteAllBytes(Path.Combine(dir, $"enc-{m}-{(os ? "objstm" : "classic")}.pdf"), PdfEditor.Open(Base()).Save(options: new PdfWriteOptions
                {
                    Compress = true, ObjectStreams = os,
                    Encryption = new PdfEncryption { UserPassword = "user", OwnerPassword = "owner", Method = m },
                }));
        using var cert = Cert("Sample Signer");
        File.WriteAllBytes(Path.Combine(dir, "signer.pfx"), cert.Export(X509ContentType.Pfx, "pw"));
        File.WriteAllBytes(Path.Combine(dir, "signer.cer"), cert.Export(X509ContentType.Cert));
        File.WriteAllBytes(Path.Combine(dir, "signed.pdf"), PdfEditor.Open(Base()).Sign(new PdfSignOptions { Certificate = cert, Reason = "Test", Rect = new PdfSignatureRect(20, 20, 160, 50) }));
        File.WriteAllBytes(Path.Combine(dir, "signed-twice.pdf"), PdfEditor.SignFile(File.ReadAllBytes(Path.Combine(dir, "signed.pdf")), new PdfSignOptions { Certificate = cert, FieldName = "Second", Rect = new PdfSignatureRect(20, 100, 160, 50) }));
        var edited = PdfEditor.Open(File.ReadAllBytes(Path.Combine(dir, "signed.pdf")));
        edited.SetInfo("Title", "after");
        File.WriteAllBytes(Path.Combine(dir, "signed-then-edited.pdf"), edited.SaveIncremental());
        foreach (var m in new[] { PdfEncryptionMethod.Aes256, PdfEncryptionMethod.Aes128, PdfEncryptionMethod.Rc4_128 })
            File.WriteAllBytes(Path.Combine(dir, $"pubsec-{m}.pdf"), PdfEditor.Open(Base()).Save(options: new PdfWriteOptions
            {
                Compress = true, Encryption = new PdfEncryption { Method = m, Recipients = [new PdfRecipient(cert)] },
            }));
    }

    [Fact]
    public void Unsigned_document_has_no_signatures_and_encrypted_sources_are_saved_first()
    {
        Assert.Empty(PdfEditor.VerifySignatures(Base()));
        var enc = PdfEditor.Open(Base()).Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { UserPassword = "u" } });
        using var cert = Cert("S");
        var signed = PdfEditor.Open(enc, "u").Sign(new PdfSignOptions { Certificate = cert });
        Assert.True(Assert.Single(PdfEditor.VerifySignatures(signed)).SignatureValid);
    }
}
