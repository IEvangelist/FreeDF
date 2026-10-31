using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

/// <summary>Certification signatures / DocMDP (ISO 32000-2 §12.8.2.2), encrypted incremental signing, document timestamps (§12.8.5) and /DSS reading (§12.8.4.3).</summary>
public class DocMdpTests
{
    [Theory]
    [InlineData("MD5")]
    [InlineData("SHA-256")]
    [InlineData(null)]
    public void TimestampRejectsUnsupportedHashBeforeCallingProvider(string? name)
    {
        var called = false;
        var ex = Assert.Throws<ArgumentException>(() => PdfEditor.AddDocumentTimeStamp([], (_, _) =>
        {
            called = true;
            return [];
        }, name is null ? default(HashAlgorithmName) : new HashAlgorithmName(name)));
        Assert.Equal("hash", ex.ParamName);
        Assert.False(called);
    }

    internal static byte[] FormPdf()
    {
        const string s = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [4 0 R] >>\nendobj\n" +
            "4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Rect [10 150 150 190] /P 3 0 R >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 5 >>\n%%EOF\n";
        return PdfEditor.Open(Encoding.Latin1.GetBytes(s)).Save();
    }

    static X509Certificate2 Cert(string cn) => SecurityTests.Cert(cn);

    static byte[] Certified(PdfDocMdpPermission p, X509Certificate2 cert, byte[]? source = null)
        => PdfEditor.SignFile(source ?? FormPdf(), new PdfSignOptions { Certificate = cert, FieldName = "Certify", Certify = p });

    /// <summary>Appends an incremental update that applies <paramref name="edit"/> to a mutable copy of the parsed objects.</summary>
    internal static byte[] Update(byte[] pdf, Action<PdfFile, Dictionary<int, PdfObject>> edit)
    {
        var f = PdfFile.Load(pdf);
        var changed = new Dictionary<int, PdfObject>();
        edit(f, changed);
        var text = Encoding.Latin1.GetString(pdf);
        var prev = long.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 9)..].Trim().Split('\n', '\r')[0]);
        var size = Math.Max(changed.Keys.Max() + 1, (int)f.Resolve(f.Trailer.Get("Size")).AsNumber()!);
        var xrefStream = !text.AsSpan((int)prev, 4).SequenceEqual("xref");
        return PdfWriter.WriteIncremental(pdf, prev, changed, size, ((PdfRef)f.Trailer.Get("Root")!).Number, (f.Trailer.Get("Info") as PdfRef)?.Number, f.Resolve(f.Trailer.Get("ID")), xrefStream);
    }

    internal static int Num(PdfFile f, Func<PdfDictionary, bool> pred) => f.Xref.Keys.Order().First(n => f.GetObject(n) is PdfDictionary d && pred(d));

    internal static PdfDictionary D(PdfFile f, int n) => (PdfDictionary)f.GetObject(n);

    internal static PdfDictionary With(PdfDictionary d, string k, PdfObject v) => new(new Dictionary<string, PdfObject>(d.Entries) { [k] = v });

    static PdfSignatureInfo Cert1(byte[] pdf) => PdfEditor.VerifySignatures(pdf)[0];

    [Theory]
    [InlineData(PdfDocMdpPermission.NoChanges)]
    [InlineData(PdfDocMdpPermission.FormFillAndSign)]
    [InlineData(PdfDocMdpPermission.FormFillSignAndAnnotate)]
    public void Certification_signature_writes_docmdp_reference_and_perms(PdfDocMdpPermission p)
    {
        using var cert = Cert("Certifier");
        var pdf = Certified(p, cert);
        var s = Cert1(pdf);
        Assert.True(s.IsCertification);
        Assert.Equal(p, s.DocMdpPermission);
        Assert.Equal(PdfDocMdpStatus.Unmodified, s.DocMdpStatus);
        Assert.Empty(s.DocMdpFindings);
        Assert.True(s.SignatureValid, s.Error);
        Assert.True(s.CoversWholeDocument);

        var f = PdfFile.Load(pdf);
        var sigNum = Num(f, d => d.Get("Type") is PdfName { Value: "Sig" });
        var perms = Assert.IsType<PdfDictionary>(f.Resolve(f.Catalog.Get("Perms")));
        Assert.Equal(sigNum, Assert.IsType<PdfRef>(perms.Get("DocMDP")).Number);
        var sig = D(f, sigNum);
        var reference = Assert.IsType<PdfDictionary>(Assert.Single(Assert.IsType<PdfArray>(sig.Get("Reference")).Items));
        Assert.Equal("SigRef", reference.Get("Type").AsName());
        Assert.Equal("DocMDP", reference.Get("TransformMethod").AsName());
        var tp = Assert.IsType<PdfDictionary>(reference.Get("TransformParams"));
        Assert.Equal((double)(int)p, tp.Get("P").AsNumber());
        Assert.Equal("1.2", tp.Get("V").AsName());
    }

    [Fact]
    public void Certification_must_be_first_and_unique()
    {
        using var cert = Cert("Certifier");
        var approved = PdfEditor.SignFile(FormPdf(), new PdfSignOptions { Certificate = cert, FieldName = "A" });
        Assert.Throws<InvalidOperationException>(() => PdfEditor.SignFile(approved, new PdfSignOptions { Certificate = cert, FieldName = "C", Certify = PdfDocMdpPermission.FormFillAndSign }));
        var certified = Certified(PdfDocMdpPermission.FormFillAndSign, cert);
        Assert.Throws<InvalidOperationException>(() => PdfEditor.SignFile(certified, new PdfSignOptions { Certificate = cert, FieldName = "C2", Certify = PdfDocMdpPermission.NoChanges }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfEditor.SignFile(FormPdf(), new PdfSignOptions { Certificate = cert, Certify = (PdfDocMdpPermission)7 }));
    }

    [Fact]
    public void Approval_signature_after_certification_is_permitted_for_p2_and_p3_but_not_p1()
    {
        using var cert = Cert("Certifier");
        using var signer = Cert("Approver");
        foreach (var (p, ok) in new[] { (PdfDocMdpPermission.NoChanges, false), (PdfDocMdpPermission.FormFillAndSign, true), (PdfDocMdpPermission.FormFillSignAndAnnotate, true) })
        {
            var two = PdfEditor.SignFile(Certified(p, cert), new PdfSignOptions { Certificate = signer, FieldName = "Approve" });
            var sigs = PdfEditor.VerifySignatures(two);
            Assert.Equal(2, sigs.Count);
            var c = sigs[0];
            Assert.True(c.SignatureValid, c.Error);
            Assert.True(c.ModifiedAfterSigning);
            Assert.Equal(ok ? PdfDocMdpStatus.ChangesPermitted : PdfDocMdpStatus.Violated, c.DocMdpStatus);
            if (!ok) Assert.Contains(c.DocMdpFindings, x => x.Contains("annotation"));
            Assert.True(sigs[1].SignatureValid && !sigs[1].IsCertification);
        }
    }

    [Fact]
    public void Editor_sign_of_a_certified_file_does_not_rewrite_untouched_objects()
    {
        using var cert = Cert("Certifier");
        using var signer = Cert("Approver");
        var certified = Certified(PdfDocMdpPermission.FormFillAndSign, cert);
        var two = PdfEditor.Open(certified).Sign(new PdfSignOptions { Certificate = signer, FieldName = "Approve", Rect = new PdfSignatureRect(10, 10, 120, 40) });
        var c = PdfEditor.VerifySignatures(two)[0];
        Assert.True(c.SignatureValid, c.Error);
        Assert.True(c.DocMdpStatus == PdfDocMdpStatus.ChangesPermitted, string.Join(" | ", c.DocMdpFindings));
        Assert.Empty(c.DocMdpFindings);
    }

    [Fact]
    public void Form_fill_is_permitted_for_p2_and_a_violation_for_p1()
    {
        using var cert = Cert("Certifier");
        foreach (var (p, status) in new[] { (PdfDocMdpPermission.NoChanges, PdfDocMdpStatus.Violated), (PdfDocMdpPermission.FormFillAndSign, PdfDocMdpStatus.ChangesPermitted) })
        {
            var filled = Update(Certified(p, cert), (f, ch) =>
            {
                var n = Num(f, d => f.Resolve(d.Get("T")) is PdfString { Text: "name" });
                ch[n] = With(D(f, n), "V", new PdfString(Encoding.Latin1.GetBytes("Grace")));
            });
            var c = Cert1(filled);
            Assert.True(c.SignatureValid, c.Error);
            Assert.Equal(status, c.DocMdpStatus);
        }
    }

    [Fact]
    public void Editor_form_fill_after_certification_is_permitted_by_p2()
    {
        using var cert = Cert("Certifier");
        var e = PdfEditor.Open(Certified(PdfDocMdpPermission.FormFillAndSign, cert));
        Assert.True(e.SetFieldValue("name", "Grace Hopper"));
        var c = Cert1(e.SaveIncremental());
        Assert.True(c.SignatureValid, c.Error);
        Assert.True(c.DocMdpStatus == PdfDocMdpStatus.ChangesPermitted, string.Join(" | ", c.DocMdpFindings));
        Assert.Empty(c.DocMdpFindings);
    }

    [Fact]
    public void Changes_outside_the_permissions_are_reported()
    {
        using var cert = Cert("Certifier");
        var certified = Certified(PdfDocMdpPermission.FormFillAndSign, cert);

        var info = PdfEditor.Open(certified);
        info.SetInfo("Title", "tampered");
        var t = Cert1(info.SaveIncremental());
        Assert.True(t.SignatureValid);
        Assert.Equal(PdfDocMdpStatus.Violated, t.DocMdpStatus);

        var content = Update(certified, (f, ch) =>
        {
            var pn = Num(f, d => f.Resolve(d.Get("Type")).AsName() == "Page");
            ch[pn] = With(D(f, pn), "MediaBox", new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10)]));
        });
        var c = Cert1(content);
        Assert.Equal(PdfDocMdpStatus.Violated, c.DocMdpStatus);
        Assert.Contains(c.DocMdpFindings, x => x.Contains("/MediaBox"));

        var locked = Update(certified, (f, ch) =>
        {
            var n = Num(f, d => f.Resolve(d.Get("T")) is PdfString { Text: "name" });
            ch[n] = With(D(f, n), "Ff", new PdfInteger(1));
        });
        Assert.Contains(Cert1(locked).DocMdpFindings, x => x.Contains("/Ff"));
    }

    [Fact]
    public void Annotations_are_permitted_only_for_p3()
    {
        using var cert = Cert("Certifier");
        foreach (var (p, status) in new[] { (PdfDocMdpPermission.FormFillAndSign, PdfDocMdpStatus.Violated), (PdfDocMdpPermission.FormFillSignAndAnnotate, PdfDocMdpStatus.ChangesPermitted) })
        {
            var noted = Update(Certified(p, cert), (f, ch) =>
            {
                var pn = Num(f, d => f.Resolve(d.Get("Type")).AsName() == "Page");
                var page = D(f, pn);
                var an = f.Xref.Keys.Max() + 1;
                ch[an] = new PdfDictionary(new Dictionary<string, PdfObject>
                {
                    ["Type"] = new PdfName("Annot"), ["Subtype"] = new PdfName("Text"), ["Contents"] = new PdfString(Encoding.Latin1.GetBytes("note")),
                    ["Rect"] = new PdfArray([new PdfInteger(1), new PdfInteger(1), new PdfInteger(20), new PdfInteger(20)]),
                });
                ch[pn] = With(page, "Annots", new PdfArray([.. ((PdfArray)f.Resolve(page.Get("Annots"))!).Items, new PdfRef(an, 0)]));
            });
            Assert.Equal(status, Cert1(noted).DocMdpStatus);
        }
    }

    [Fact]
    public void Docmdp_without_perms_is_not_an_effective_certification()
    {
        using var cert = Cert("Certifier");
        var pdf = Certified(PdfDocMdpPermission.NoChanges, cert);
        var f = PdfFile.Load(pdf);
        // Declaring DocMDP in /Reference without registering it in /Perms must not be treated as a certification.
        var raw = Encoding.Latin1.GetString(pdf).Replace("/DocMDP ", "/DocXDP ");
        Assert.NotEqual(Encoding.Latin1.GetString(pdf), raw);
        var s = Cert1(Encoding.Latin1.GetBytes(raw));
        Assert.False(s.IsCertification);
        Assert.Equal(PdfDocMdpStatus.NotCertification, s.DocMdpStatus);
        Assert.Contains(s.DocMdpFindings, x => x.Contains("/Perms"));
        Assert.NotNull(f);
    }

    [Theory]
    [InlineData(PdfEncryptionMethod.Aes256)]
    [InlineData(PdfEncryptionMethod.Aes128)]
    [InlineData(PdfEncryptionMethod.Rc4_128)]
    public void Encrypted_sources_are_signed_incrementally_and_stay_encrypted(PdfEncryptionMethod m)
    {
        using var cert = Cert("Signer");
        var enc = PdfEditor.Open(FormPdf()).Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { UserPassword = "u", OwnerPassword = "o", Method = m } });
        var editor = PdfEditor.Open(enc, "u");
        Assert.True(editor.CanSignIncrementally);
        var signed = editor.Sign(new PdfSignOptions { Certificate = cert, Reason = "Because", Rect = new PdfSignatureRect(10, 10, 120, 40) });
        Assert.True(signed.AsSpan(0, enc.Length).SequenceEqual(enc));
        var f = PdfFile.Load(signed, "u");
        Assert.True(f.IsEncrypted);
        var s = Assert.Single(PdfEditor.VerifySignatures(signed, "u"));
        Assert.True(s.SignatureValid, $"{s.Error} dig={s.DigestValid} br={s.ByteRangeValid}");
        Assert.Equal("Because", s.Reason);
        Assert.Equal("Signer", s.SignerName);
        Assert.Throws<PdfPasswordException>(() => PdfEditor.VerifySignatures(signed));

        var two = PdfEditor.SignFile(signed, new PdfSignOptions { Certificate = cert, FieldName = "Second" }, "u");
        Assert.True(two.AsSpan(0, signed.Length).SequenceEqual(signed));
        var both = PdfEditor.VerifySignatures(two, "u");
        Assert.Equal(2, both.Count);
        Assert.All(both, x => Assert.True(x.SignatureValid, x.Error));
    }

    [Fact]
    public void Redacted_or_new_documents_still_sign_after_a_full_save()
    {
        using var cert = Cert("Signer");
        var e = PdfEditor.Open(FormPdf());
        e.RequireFullRewrite();
        Assert.False(e.CanSignIncrementally);
        Assert.True(Assert.Single(PdfEditor.VerifySignatures(e.Sign(new PdfSignOptions { Certificate = cert }))).SignatureValid);
    }

    // --- RFC 3161 document timestamps (offline TSA) ---

    static byte[] Token(X509Certificate2 tsa, byte[] digest, DateTimeOffset time, string alg = "2.16.840.1.101.3.4.2.1")
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteInteger(1);
            w.WriteObjectIdentifier("1.2.3.4.1");
            using (w.PushSequence())
            {
                using (w.PushSequence()) { w.WriteObjectIdentifier(alg); w.WriteNull(); }
                w.WriteOctetString(digest);
            }
            w.WriteInteger(42);
            w.WriteGeneralizedTime(time, omitFractionalSeconds: true);
        }
        var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), w.Encode()), false);
        cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, tsa) { IncludeOption = X509IncludeOption.WholeChain });
        return cms.Encode();
    }

    static byte[] Stamp(byte[] pdf, X509Certificate2 tsa, DateTimeOffset time, string name = "DocTimeStamp1")
        => PdfEditor.AddDocumentTimeStamp(pdf, (h, digest) => Token(tsa, digest, time), null, name);

    [Fact]
    public void Document_timestamp_is_embedded_and_verified()
    {
        using var signer = Cert("Signer");
        using var tsa = Cert("Offline TSA");
        var time = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var signed = PdfEditor.Open(FormPdf()).Sign(new PdfSignOptions { Certificate = signer });
        var stamped = Stamp(signed, tsa, time);
        var sigs = PdfEditor.VerifySignatures(stamped);
        Assert.Equal(2, sigs.Count);
        Assert.True(sigs[0].SignatureValid && !sigs[0].IsDocumentTimeStamp);
        var ts = sigs[1];
        Assert.True(ts.IsDocumentTimeStamp);
        Assert.Equal("ETSI.RFC3161", ts.SubFilter);
        Assert.True(ts.ByteRangeValid && ts.DigestValid && ts.SignatureValid && ts.CoversWholeDocument, ts.Error);
        Assert.Equal(time, ts.TimeStampTime);
        Assert.Equal("Offline TSA", ts.SignerName);
        Assert.False(ts.ChainTrusted);

        var f = PdfFile.Load(stamped);
        var v = D(f, Num(f, d => f.Resolve(d.Get("Type")).AsName() == "DocTimeStamp"));
        Assert.Equal("Adobe.PPKLite", v.Get("Filter").AsName());
    }

    [Fact]
    public void Document_timestamp_detects_tampering_and_a_wrong_imprint()
    {
        using var signer = Cert("Signer");
        using var tsa = Cert("Offline TSA");
        var stamped = Stamp(PdfEditor.Open(FormPdf()).Sign(new PdfSignOptions { Certificate = signer }), tsa, DateTimeOffset.UtcNow);
        var bad = (byte[])stamped.Clone();
        bad[20] ^= 0x01;
        Assert.False(PdfEditor.VerifySignatures(bad).Last().DigestValid);

        var wrong = PdfEditor.AddDocumentTimeStamp(FormPdf(), (h, digest) => Token(tsa, new byte[digest.Length], DateTimeOffset.UtcNow));
        var ts = Assert.Single(PdfEditor.VerifySignatures(wrong));
        Assert.False(ts.DigestValid); Assert.False(ts.SignatureValid);
    }

    [Fact]
    public void Document_timestamp_after_a_p1_certification_is_not_a_docmdp_violation()
    {
        using var cert = Cert("Certifier");
        using var tsa = Cert("Offline TSA");
        var stamped = Stamp(Certified(PdfDocMdpPermission.NoChanges, cert), tsa, DateTimeOffset.UtcNow);
        var sigs = PdfEditor.VerifySignatures(stamped);
        Assert.Equal(PdfDocMdpStatus.ChangesPermitted, sigs[0].DocMdpStatus);
        Assert.Empty(sigs[0].DocMdpFindings);
        Assert.True(sigs[1].IsDocumentTimeStamp && sigs[1].SignatureValid);
    }

    // --- /DSS (read side) ---

    internal static byte[] WithDss(byte[] signed, X509Certificate2 cert, out string key)
    {
        var f0 = PdfFile.Load(signed);
        var contents = ((PdfString)D(f0, Num(f0, d => f0.Resolve(d.Get("Type")).AsName() == "Sig")).Get("Contents")!).Bytes;
        var k = key = Convert.ToHexString(SHA1.HashData(contents));
        return Update(signed, (f, ch) =>
        {
            var next = f.Xref.Keys.Max() + 1;
            ch[next] = new PdfStream(new PdfDictionary(), cert.Export(X509ContentType.Cert));
            var root = ((PdfRef)f.Trailer.Get("Root")!).Number;
            var crl = new PdfStream(new PdfDictionary(), [1, 2, 3]);
            ch[next + 1] = crl;
            ch[root] = With(D(f, root), "DSS", new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Certs"] = new PdfArray([new PdfRef(next, 0)]),
                ["CRLs"] = new PdfArray([new PdfRef(next + 1, 0)]),
                ["VRI"] = new PdfDictionary(new Dictionary<string, PdfObject>
                {
                    [k] = new PdfDictionary(new Dictionary<string, PdfObject>
                    {
                        ["Cert"] = new PdfArray([new PdfRef(next, 0)]), ["CRL"] = new PdfArray([new PdfRef(next + 1, 0)]),
                        ["TU"] = new PdfString(Encoding.ASCII.GetBytes("D:20260101000000Z")),
                    }),
                }),
            }));
        });
    }

    [Fact]
    public void Document_security_store_is_read_and_matched_to_the_signature()
    {
        using var cert = Cert("Certifier");
        var signed = Certified(PdfDocMdpPermission.NoChanges, cert);
        Assert.Null(PdfEditor.ReadDocumentSecurityStore(signed));
        Assert.False(Cert1(signed).HasValidationInformation);

        var withDss = WithDss(signed, cert, out var key);
        var dss = Assert.IsType<PdfDocumentSecurityStore>(PdfEditor.ReadDocumentSecurityStore(withDss));
        Assert.Equal((1, 1, 0), (dss.Certificates, dss.Crls, dss.Ocsps));
        var vri = Assert.Single(dss.Vri);
        Assert.Equal(key, vri.Key);
        Assert.Equal((1, 1, 0), (vri.Certificates, vri.Crls, vri.Ocsps));
        var s = Cert1(withDss);
        Assert.True(s.HasValidationInformation);
        // Adding validation material is permitted even by P = 1 (§12.8.4.3 / DocMDP note).
        Assert.Equal(PdfDocMdpStatus.ChangesPermitted, s.DocMdpStatus);
    }

    /// <summary>Writes samples for validation with pyHanko when PDF_SECURITY_OUT names a directory; otherwise a no-op.</summary>
    [Fact]
    public void Export_docmdp_samples_for_external_validation()
    {
        var dir = Environment.GetEnvironmentVariable("PDF_SECURITY_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var cert = Cert("Sample Certifier");
        using var tsa = Cert("Sample TSA");
        File.WriteAllBytes(Path.Combine(dir, "docmdp-signer.cer"), cert.Export(X509ContentType.Cert));
        File.WriteAllBytes(Path.Combine(dir, "docmdp-tsa.cer"), tsa.Export(X509ContentType.Cert));
        foreach (var p in Enum.GetValues<PdfDocMdpPermission>())
        {
            var certified = Certified(p, cert);
            File.WriteAllBytes(Path.Combine(dir, $"certified-p{(int)p}.pdf"), certified);
            File.WriteAllBytes(Path.Combine(dir, $"certified-p{(int)p}-approved.pdf"), PdfEditor.SignFile(certified, new PdfSignOptions { Certificate = cert, FieldName = "Approve" }));
            File.WriteAllBytes(Path.Combine(dir, $"certified-p{(int)p}-filled.pdf"), Update(certified, (f, ch) =>
            {
                var n = Num(f, d => f.Resolve(d.Get("T")) is PdfString { Text: "name" });
                ch[n] = With(D(f, n), "V", new PdfString(Encoding.Latin1.GetBytes("Grace")));
            }));
        }
        File.WriteAllBytes(Path.Combine(dir, "certified-p1-dss.pdf"), WithDss(Certified(PdfDocMdpPermission.NoChanges, cert), cert, out _));
        File.WriteAllBytes(Path.Combine(dir, "doctimestamp.pdf"), Stamp(PdfEditor.Open(FormPdf()).Sign(new PdfSignOptions { Certificate = cert }), tsa, DateTimeOffset.UtcNow));
        var enc = PdfEditor.Open(FormPdf()).Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { UserPassword = "u", OwnerPassword = "o" } });
        File.WriteAllBytes(Path.Combine(dir, "encrypted-certified-p2-approved.pdf"), PdfEditor.SignFile(PdfEditor.SignFile(enc, new PdfSignOptions { Certificate = cert, FieldName = "Certify", Certify = PdfDocMdpPermission.FormFillAndSign }, "u"), new PdfSignOptions { Certificate = cert, FieldName = "Approve" }, "u"));
        File.WriteAllBytes(Path.Combine(dir, "encrypted-incremental-signed.pdf"), PdfEditor.Open(enc, "u").Sign(new PdfSignOptions { Certificate = cert }));
    }
}
