using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Signing.Http;
using Xunit;

namespace AdoBye.Tests;

enum OcspResponseStatus { Successful }

/// <summary>Test-only PKI (random throw-away keys, nothing is trusted outside the tests): root, intermediate, signer and TSA certificates plus CRL/OCSP builders.</summary>
sealed class TestPki : IDisposable
{
    public readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    public X509Certificate2 Root = null!, Inter = null!, Leaf = null!, Tsa = null!;

    public TestPki(Action<CertificateRequest>? leafExt = null, Action<CertificateRequest>? interExt = null, DateTimeOffset? leafNotAfter = null)
    {
        Root = Make("CN=Test Root", null, 1, Now.AddDays(-30), Now.AddYears(10), null);
        Inter = Make("CN=Test Intermediate", Root, 0, Now.AddDays(-20), Now.AddYears(5), interExt);
        Leaf = Make("CN=Test Signer", Inter, -1, Now.AddDays(-10), leafNotAfter ?? Now.AddYears(1), r =>
        {
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
            leafExt?.Invoke(r);
        });
        Tsa = Make("CN=Test TSA", Inter, -1, Now.AddDays(-10), Now.AddYears(1), r =>
        {
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.8")], true));
        });
    }

    public static X509Certificate2 Make(string subject, X509Certificate2? issuer, int pathLen, DateTimeOffset nb, DateTimeOffset na, Action<CertificateRequest>? ext)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        if (pathLen >= 0)
        {
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, pathLen, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        }
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        ext?.Invoke(req);
        var serial = RandomNumberGenerator.GetBytes(8); serial[0] &= 0x7F; serial[0] |= 1;
        using var cert = issuer is null ? req.CreateSelfSigned(nb, na) : req.Create(issuer, nb, na, serial);
        var withKey = issuer is null ? cert : cert.CopyWithPrivateKey(key);
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx, "pw"), "pw", X509KeyStorageFlags.Exportable);
    }

    public byte[] Crl(X509Certificate2 issuer, DateTimeOffset thisUpdate, DateTimeOffset nextUpdate, params (X509Certificate2 Cert, DateTimeOffset At)[] revoked)
    {
        var b = new CertificateRevocationListBuilder();
        foreach (var (c, at) in revoked) b.AddEntry(c, at, X509RevocationReason.KeyCompromise);
        return b.Build(issuer, BigInteger.One, nextUpdate, HashAlgorithmName.SHA256, null, thisUpdate);
    }

    /// <summary>OCSP response for <paramref name="cert"/>; status 0 good, 1 revoked, 2 unknown. Signed by <paramref name="signer"/> (must have an EC key).</summary>
    public static byte[] Ocsp(X509Certificate2 cert, X509Certificate2 issuer, X509Certificate2 signer, int status, DateTimeOffset thisUpdate, DateTimeOffset? next, DateTimeOffset? revokedAt = null, bool embedSigner = false)
    {
        var tbs = new AsnWriter(AsnEncodingRules.DER);
        using (tbs.PushSequence())
        {
            using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1))) tbs.WriteEncodedValue(signer.SubjectName.RawData);
            tbs.WriteGeneralizedTime(thisUpdate, omitFractionalSeconds: true);
            using (tbs.PushSequence())
            using (tbs.PushSequence())
            {
                using (tbs.PushSequence())
                {
                    using (tbs.PushSequence()) { tbs.WriteObjectIdentifier("1.3.14.3.2.26"); tbs.WriteNull(); }
                    tbs.WriteOctetString(SHA1.HashData(issuer.SubjectName.RawData));
                    tbs.WriteOctetString(SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData));
                    tbs.WriteIntegerUnsigned(cert.SerialNumberBytes.Span);
                }
                if (status == 1) using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1))) tbs.WriteGeneralizedTime(revokedAt ?? thisUpdate, omitFractionalSeconds: true);
                else tbs.WriteNull(new Asn1Tag(TagClass.ContextSpecific, status));
                tbs.WriteGeneralizedTime(thisUpdate, omitFractionalSeconds: true);
                if (next is { } n) using (tbs.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0))) tbs.WriteGeneralizedTime(n, omitFractionalSeconds: true);
            }
        }
        var tbsBytes = tbs.Encode();
        using var ec = signer.GetECDsaPrivateKey()!;
        var sig = ec.SignData(tbsBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var basic = new AsnWriter(AsnEncodingRules.DER);
        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(tbsBytes);
            using (basic.PushSequence()) basic.WriteObjectIdentifier("1.2.840.10045.4.3.2");
            basic.WriteBitString(sig);
            if (embedSigner) using (basic.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0))) using (basic.PushSequence()) basic.WriteEncodedValue(signer.RawData);
        }
        var resp = new AsnWriter(AsnEncodingRules.DER);
        using (resp.PushSequence())
        {
            resp.WriteEnumeratedValue(OcspResponseStatus.Successful);
            using (resp.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (resp.PushSequence()) { resp.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1.1"); resp.WriteOctetString(basic.Encode()); }
        }
        return resp.Encode();
    }

    public PdfTrustPolicy Policy(PdfRevocationRequirement rev = PdfRevocationRequirement.Require, PdfValidationData? supplied = null, DateTimeOffset? time = null, bool embedded = true)
        => new()
        {
            TrustAnchors = [Root], AdditionalCertificates = [Inter], SuppliedData = supplied, Revocation = rev, ValidationTime = time, UseEmbeddedValidationData = embedded,
        };

    /// <summary>CRLs from the root (for the intermediate) and the intermediate (for the signer and TSA) with nothing revoked, current at <see cref="Now"/>.</summary>
    public PdfValidationData GoodCrls(params (X509Certificate2 Cert, DateTimeOffset At)[] revokedByInter)
    {
        var d = new PdfValidationData();
        d.Crls.Add(Crl(Root, Now.AddHours(-1), Now.AddDays(1)));
        d.Crls.Add(Crl(Inter, Now.AddHours(-1), Now.AddDays(1), revokedByInter));
        return d;
    }

    public void Dispose() { Root.Dispose(); Inter.Dispose(); Leaf.Dispose(); Tsa.Dispose(); }
}

public class SignatureTrustTests
{
    sealed class TestTsa(X509Certificate2 tsa, DateTimeOffset time) : ITimestampProvider
    {
        public byte[] GetTimestampToken(HashAlgorithmName hash, byte[] digest)
        {
            var w = new AsnWriter(AsnEncodingRules.DER);
            using (w.PushSequence())
            {
                w.WriteInteger(1);
                w.WriteObjectIdentifier("1.2.3.4.1");
                using (w.PushSequence())
                {
                    using (w.PushSequence()) { w.WriteObjectIdentifier(hash.Name switch { "SHA1" => "1.3.14.3.2.26", "SHA256" => "2.16.840.1.101.3.4.2.1", _ => "2.16.840.1.101.3.4.2.2" }); w.WriteNull(); }
                    w.WriteOctetString(digest);
                }
                w.WriteInteger(42);
                w.WriteGeneralizedTime(time, omitFractionalSeconds: true);
            }
            var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), w.Encode()), false);
            cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, tsa) { IncludeOption = X509IncludeOption.EndCertOnly });
            return cms.Encode();
        }
    }

    static PdfTrustResult Run(TestPki pki, PdfTrustPolicy policy, DateTimeOffset? at = null, PdfValidationData? data = null)
        => PdfCertificateValidator.Validate(pki.Leaf, policy, at ?? pki.Now, data);

    static bool Has(PdfTrustResult r, string code) => r.Findings.Any(f => f.Code == code);

    [Fact]
    public void Chain_with_anchor_and_crls_is_valid_and_reports_each_certificate()
    {
        using var pki = new TestPki();
        var r = Run(pki, pki.Policy(), data: pki.GoodCrls());
        Assert.Equal(PdfTrustStatus.Valid, r.Status);
        Assert.True(r.ChainTrusted);
        Assert.Equal(3, r.Chain.Count);
        Assert.Equal(PdfRevocationOutcome.Good, r.Revocation[0].Outcome);
        Assert.Equal(PdfRevocationOutcome.NotChecked, r.Revocation[^1].Outcome);
    }

    [Fact]
    public void Missing_revocation_is_indeterminate_unless_the_policy_allows_best_effort()
    {
        using var pki = new TestPki();
        Assert.Equal(PdfTrustStatus.Indeterminate, Run(pki, pki.Policy()).Status);
        Assert.Equal(PdfTrustStatus.Valid, Run(pki, pki.Policy(PdfRevocationRequirement.BestEffort)).Status);
        Assert.Equal(PdfTrustStatus.Valid, Run(pki, pki.Policy(PdfRevocationRequirement.Ignore)).Status);
    }

    [Fact]
    public void No_anchor_or_wrong_anchor_is_never_valid()
    {
        using var pki = new TestPki();
        using var other = new TestPki();
        Assert.Equal(PdfTrustStatus.Indeterminate, Run(pki, new PdfTrustPolicy { Revocation = PdfRevocationRequirement.Ignore }).Status);
        var wrong = new PdfTrustPolicy { TrustAnchors = [other.Root], AdditionalCertificates = [pki.Inter], Revocation = PdfRevocationRequirement.Ignore };
        var r = Run(pki, wrong);
        Assert.NotEqual(PdfTrustStatus.Valid, r.Status);
    }

    [Fact]
    public void Expired_or_not_yet_valid_certificate_is_invalid()
    {
        using var pki = new TestPki();
        var p = pki.Policy(PdfRevocationRequirement.Ignore);
        Assert.Equal(PdfTrustStatus.Invalid, Run(pki, p, pki.Now.AddYears(2)).Status);
        Assert.Equal(PdfTrustStatus.Invalid, Run(pki, p, pki.Now.AddDays(-25)).Status);
    }

    [Fact]
    public void Crl_revocation_before_the_validation_time_is_invalid_but_later_revocation_is_not()
    {
        using var pki = new TestPki();
        var revoked = pki.GoodCrls((pki.Leaf, pki.Now.AddDays(-1)));
        var r = Run(pki, pki.Policy(), data: revoked);
        Assert.Equal(PdfTrustStatus.Invalid, r.Status);
        Assert.True(Has(r, "Revoked"));
        var earlier = Run(pki, pki.Policy(), pki.Now.AddDays(-5), revoked);
        Assert.Equal(PdfTrustStatus.Valid, earlier.Status);
    }

    [Fact]
    public void Crl_with_a_bad_signature_or_stale_window_is_not_evidence()
    {
        using var pki = new TestPki();
        var forged = new PdfValidationData();
        forged.Crls.Add(pki.Crl(pki.Root, pki.Now.AddHours(-1), pki.Now.AddDays(1)));
        using var rogue = TestPki.Make("CN=Test Intermediate", null, 0, pki.Now.AddDays(-1), pki.Now.AddYears(1), null);
        forged.Crls.Add(pki.Crl(rogue, pki.Now.AddHours(-1), pki.Now.AddDays(1)));
        var r = Run(pki, pki.Policy(), data: forged);
        Assert.Equal(PdfTrustStatus.Indeterminate, r.Status);
        Assert.True(Has(r, "RevocationUnknown"));

        var stale = new PdfValidationData();
        stale.Crls.Add(pki.Crl(pki.Root, pki.Now.AddDays(-10), pki.Now.AddDays(-9)));
        stale.Crls.Add(pki.Crl(pki.Inter, pki.Now.AddDays(-10), pki.Now.AddDays(-9)));
        Assert.Equal(PdfTrustStatus.Indeterminate, Run(pki, pki.Policy(), data: stale).Status);
    }

    [Fact]
    public void Ocsp_good_revoked_unknown_and_forged_responses()
    {
        using var pki = new TestPki();
        var rootCrl = pki.Crl(pki.Root, pki.Now.AddHours(-1), pki.Now.AddDays(1));
        PdfValidationData With(byte[] ocsp) { var d = new PdfValidationData(); d.Crls.Add(rootCrl); d.OcspResponses.Add(ocsp); return d; }
        var t = pki.Now.AddMinutes(-30);

        var good = Run(pki, pki.Policy(), data: With(TestPki.Ocsp(pki.Leaf, pki.Inter, pki.Inter, 0, t, t.AddDays(1))));
        Assert.Equal(PdfTrustStatus.Valid, good.Status);
        Assert.StartsWith("OCSP", good.Revocation[0].Source);

        var revoked = Run(pki, pki.Policy(), data: With(TestPki.Ocsp(pki.Leaf, pki.Inter, pki.Inter, 1, t, t.AddDays(1), pki.Now.AddDays(-2))));
        Assert.Equal(PdfTrustStatus.Invalid, revoked.Status);

        var unknown = Run(pki, pki.Policy(), data: With(TestPki.Ocsp(pki.Leaf, pki.Inter, pki.Inter, 2, t, t.AddDays(1))));
        Assert.Equal(PdfTrustStatus.Indeterminate, unknown.Status);

        var forged = Run(pki, pki.Policy(), data: With(TestPki.Ocsp(pki.Leaf, pki.Inter, pki.Tsa, 0, t, t.AddDays(1))));
        Assert.Equal(PdfTrustStatus.Indeterminate, forged.Status);

        var expired = Run(pki, pki.Policy(), data: With(TestPki.Ocsp(pki.Leaf, pki.Inter, pki.Inter, 0, pki.Now.AddDays(-3), pki.Now.AddDays(-2))));
        Assert.Equal(PdfTrustStatus.Indeterminate, expired.Status);
    }

    [Fact]
    public void Ocsp_delegated_responder_needs_the_ocsp_signing_eku()
    {
        using var pki = new TestPki();
        using var good = TestPki.Make("CN=Responder", pki.Inter, -1, pki.Now.AddDays(-1), pki.Now.AddYears(1), r => r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.9")], false)));
        var t = pki.Now.AddMinutes(-30);
        var d = new PdfValidationData();
        d.Crls.Add(pki.Crl(pki.Root, pki.Now.AddHours(-1), pki.Now.AddDays(1)));
        d.OcspResponses.Add(TestPki.Ocsp(pki.Leaf, pki.Inter, good, 0, t, t.AddDays(1), embedSigner: true));
        Assert.Equal(PdfTrustStatus.Valid, Run(pki, pki.Policy(), data: d).Status);
        // The TSA certificate is also issued by the intermediate but lacks the OCSP signing purpose.
        var bad = new PdfValidationData();
        bad.Crls.AddRange(d.Crls);
        bad.OcspResponses.Add(TestPki.Ocsp(pki.Leaf, pki.Inter, pki.Tsa, 0, t, t.AddDays(1), embedSigner: true));
        Assert.Equal(PdfTrustStatus.Indeterminate, Run(pki, pki.Policy(), data: bad).Status);
    }

    [Fact]
    public void Key_usage_eku_and_policy_requirements_are_enforced()
    {
        using var pki = new TestPki(r =>
        {
            r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.4")], false));
        });
        var p = new PdfTrustPolicy { TrustAnchors = [pki.Root], AdditionalCertificates = [pki.Inter], Revocation = PdfRevocationRequirement.Ignore, RequiredExtendedKeyUsages = ["1.3.6.1.5.5.7.3.3"] };
        var r1 = Run(pki, p);
        Assert.Equal(PdfTrustStatus.Invalid, r1.Status);
        Assert.True(Has(r1, "ExtendedKeyUsage"));
        var ok = new PdfTrustPolicy { TrustAnchors = [pki.Root], AdditionalCertificates = [pki.Inter], Revocation = PdfRevocationRequirement.Ignore, RequiredExtendedKeyUsages = ["1.3.6.1.5.5.7.3.4"] };
        Assert.Equal(PdfTrustStatus.Valid, Run(pki, ok).Status);
        var pol = new PdfTrustPolicy { TrustAnchors = [pki.Root], AdditionalCertificates = [pki.Inter], Revocation = PdfRevocationRequirement.Ignore, RequiredCertificatePolicies = ["1.2.3.4.5"] };
        Assert.True(Has(Run(pki, pol), "CertificatePolicy"));
    }

    [Fact]
    public void Signer_without_signing_key_usage_is_invalid()
    {
        using var root = TestPki.Make("CN=R", null, 1, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), null);
        using var leaf = TestPki.Make("CN=L", root, -1, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), r =>
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true)));
        var r = PdfCertificateValidator.Validate(leaf, new PdfTrustPolicy { TrustAnchors = [root], Revocation = PdfRevocationRequirement.Ignore }, DateTimeOffset.UtcNow);
        Assert.Equal(PdfTrustStatus.Invalid, r.Status);
        Assert.True(Has(r, "KeyUsage"));
    }

    [Fact]
    public void Path_length_constraint_violation_is_invalid()
    {
        using var root = TestPki.Make("CN=R", null, 0, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), null);
        using var inter = TestPki.Make("CN=I", root, 0, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), null);
        using var leaf = TestPki.Make("CN=L", inter, -1, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), null);
        var r = PdfCertificateValidator.Validate(leaf, new PdfTrustPolicy { TrustAnchors = [root], AdditionalCertificates = [inter], Revocation = PdfRevocationRequirement.Ignore }, DateTimeOffset.UtcNow);
        Assert.Equal(PdfTrustStatus.Invalid, r.Status);
    }

    [Fact]
    public void Name_constraints_violation_is_invalid()
    {
        static void Constrain(CertificateRequest r)
        {
            var w = new AsnWriter(AsnEncodingRules.DER);
            using (w.PushSequence())
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (w.PushSequence())
                w.WriteCharacterString(UniversalTagNumber.IA5String, "example.com", new Asn1Tag(TagClass.ContextSpecific, 2));
            r.CertificateExtensions.Add(new X509Extension(new Oid("2.5.29.30"), w.Encode(), true));
        }
        static void San(string name, CertificateRequest r) { var b = new SubjectAlternativeNameBuilder(); b.AddDnsName(name); r.CertificateExtensions.Add(b.Build()); }
        using var inside = new TestPki(r => San("www.example.com", r), Constrain);
        using var outside = new TestPki(r => San("www.evil.org", r), Constrain);
        var pin = inside.Policy(PdfRevocationRequirement.Ignore);
        Assert.Equal(PdfTrustStatus.Valid, Run(inside, pin).Status);
        var rout = Run(outside, outside.Policy(PdfRevocationRequirement.Ignore));
        Assert.Equal(PdfTrustStatus.Invalid, rout.Status);
        Assert.True(Has(rout, "NameConstraintsViolated"));
    }

    [Fact]
    public void Chain_length_policy_is_enforced()
    {
        using var pki = new TestPki();
        var p = new PdfTrustPolicy { TrustAnchors = [pki.Root], AdditionalCertificates = [pki.Inter], Revocation = PdfRevocationRequirement.Ignore, MaxChainLength = 2 };
        Assert.True(Has(Run(pki, p), "ChainTooLong"));
    }

    [Fact]
    public void Malformed_revocation_data_is_ignored_without_exceptions()
    {
        using var pki = new TestPki();
        var rng = new Random(7);
        var d = new PdfValidationData();
        for (var i = 0; i < 50; i++)
        {
            var junk = new byte[rng.Next(1, 300)];
            rng.NextBytes(junk);
            d.Crls.Add(junk); d.OcspResponses.Add(junk);
        }
        d.Crls.Add([0x30, 0x84, 0xFF, 0xFF, 0xFF, 0xFF, 0x00]);
        d.OcspResponses.Add([0x30, 0x83, 0x7F, 0xFF, 0xFF, 0x0A, 0x01, 0x00]);
        var good = pki.GoodCrls();
        d.Crls.AddRange(good.Crls);
        var r = Run(pki, pki.Policy(), data: d);
        Assert.Equal(PdfTrustStatus.Valid, r.Status);
        Assert.True(Has(r, "UnusableRevocationData"));
        // Truncations of a real CRL never throw and never validate.
        var crl = good.Crls[1];
        for (var len = 0; len < crl.Length; len += 7)
        {
            var t = new PdfValidationData();
            t.Crls.Add(good.Crls[0]); t.Crls.Add(crl[..len]);
            Assert.NotEqual(PdfTrustStatus.Valid, Run(pki, pki.Policy(), data: t).Status);
        }
    }

    [Fact]
    public void Validation_is_thread_safe()
    {
        using var pki = new TestPki();
        var policy = pki.Policy();
        var data = pki.GoodCrls();
        var results = new PdfTrustStatus[24];
        Parallel.For(0, results.Length, i => results[i] = Run(pki, policy, data: data).Status);
        Assert.All(results, s => Assert.Equal(PdfTrustStatus.Valid, s));
    }

    // --- PDF level ---

    static byte[] Sign(TestPki pki, PdfSignOptions o) => PdfEditor.SignFile(DocMdpTests.FormPdf(), o);

    static PdfSignOptions Opts(TestPki pki, Func<PdfSignOptions, PdfSignOptions>? tweak = null)
    {
        var o = new PdfSignOptions { Certificate = pki.Leaf, ExtraCertificates = [pki.Inter], SigningTime = pki.Now, SubFilter = PdfSignatureSubFilter.CadesDetached };
        return tweak is null ? o : tweak(o);
    }

    [Fact]
    public void Pdf_signature_trust_without_evidence_is_indeterminate_then_valid_with_supplied_data()
    {
        using var pki = new TestPki();
        var pdf = Sign(pki, Opts(pki));
        var sig = Assert.Single(PdfEditor.VerifySignatures(pdf));
        Assert.True(sig.IsIntact, sig.Error);
        Assert.Equal("ETSI.CAdES.detached", sig.SubFilter);
        Assert.True(sig.SigningCertificateV2Valid);

        var none = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(time: pki.Now)));
        Assert.Equal(PdfTrustStatus.Indeterminate, none.Trust.Status);
        Assert.False(none.Trust.ChainTrusted);

        var ok = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: pki.GoodCrls(), time: pki.Now)));
        Assert.Equal(PdfTrustStatus.Valid, ok.Trust.Status);
        Assert.Equal(PdfValidationTimeSource.Policy, ok.Trust.ValidationTimeSource);
    }

    [Fact]
    public void Tampered_pdf_is_invalid_even_when_the_chain_is_trusted()
    {
        using var pki = new TestPki();
        var pdf = Sign(pki, Opts(pki));
        var sig = PdfEditor.VerifySignatures(pdf)[0];
        pdf[(int)sig.ByteRange[2] + 5] ^= 1;
        var r = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: pki.GoodCrls(), time: pki.Now)));
        Assert.Equal(PdfTrustStatus.Invalid, r.Trust.Status);
        Assert.True(Has(r.Trust, "SignatureNotIntact"));
    }

    [Fact]
    public void Dss_authoring_adds_vri_and_makes_the_signature_validatable_from_the_file_alone()
    {
        using var pki = new TestPki();
        var pdf = Sign(pki, Opts(pki));
        var data = pki.GoodCrls();
        data.Certificates.Add(pki.Inter);
        var ltv = PdfEditor.AddValidationInformation(pdf, data);

        var dss = PdfEditor.ReadDocumentSecurityStore(ltv)!;
        Assert.Equal(1, dss.Certificates);
        Assert.Equal(2, dss.Crls);
        var vri = Assert.Single(dss.Vri);
        Assert.Equal(1, vri.Certificates); Assert.Equal(2, vri.Crls); Assert.NotNull(vri.UpdateTime);
        var sig = Assert.Single(PdfEditor.VerifySignatures(ltv));
        Assert.True(sig.IsIntact && sig.HasValidationInformation && sig.ModifiedAfterSigning);
        var r = Assert.Single(PdfSignatureTrust.Evaluate(ltv, pki.Policy(time: pki.Now)));
        Assert.Equal(PdfTrustStatus.Valid, r.Trust.Status);
        Assert.All(r.Trust.Revocation.Where(x => x.Outcome == PdfRevocationOutcome.Good), x => Assert.Equal("CRL/DSS", x.Source));

        // A second update with the same data does not duplicate streams.
        var again = PdfEditor.ReadDocumentSecurityStore(PdfEditor.AddValidationInformation(ltv, data))!;
        Assert.Equal(2, again.Crls);
    }

    [Fact]
    public void Pades_lta_document_timestamp_supplies_a_trusted_validation_time()
    {
        using var pki = new TestPki();
        var pdf = Sign(pki, Opts(pki));
        var data = pki.GoodCrls();
        var lt = PdfEditor.AddValidationInformation(pdf, data);
        var lta = PdfEditor.AddDocumentTimeStamp(lt, new TestTsa(pki.Tsa, pki.Now));
        var sigs = PdfEditor.VerifySignatures(lta);
        Assert.Equal(2, sigs.Count);
        Assert.True(sigs[1].IsDocumentTimeStamp && sigs[1].IsIntact);

        var policy = pki.Policy();
        var results = PdfSignatureTrust.Evaluate(lta, policy);
        Assert.Equal(PdfValidationTimeSource.DocumentTimestamp, results[0].Trust.ValidationTimeSource);
        Assert.Equal(PdfTrustStatus.Valid, results[0].Trust.Status);

        // With document timestamps disabled the validation time falls back to now.
        var noTsa = PdfSignatureTrust.Evaluate(lta, new PdfTrustPolicy { TrustAnchors = [pki.Root], AdditionalCertificates = [pki.Inter], UseDocumentTimestamps = false });
        Assert.Equal(PdfValidationTimeSource.Now, noTsa[0].Trust.ValidationTimeSource);
        using var other = new TestPki();
        var wrongAnchor = PdfSignatureTrust.Evaluate(lta, new PdfTrustPolicy { TrustAnchors = [other.Root], Revocation = PdfRevocationRequirement.Ignore });
        Assert.NotEqual(PdfValidationTimeSource.DocumentTimestamp, wrongAnchor[0].Trust.ValidationTimeSource);
        Assert.NotEqual(PdfTrustStatus.Valid, wrongAnchor[0].Trust.Status);
    }

    [Fact]
    public void Signature_embedded_revocation_info_is_used()
    {
        using var pki = new TestPki();
        var pdf = Sign(pki, Opts(pki, o => new PdfSignOptions { Certificate = o.Certificate, ExtraCertificates = o.ExtraCertificates, SigningTime = o.SigningTime, SubFilter = o.SubFilter, EmbeddedRevocationInfo = pki.GoodCrls() }));
        var r = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(time: pki.Now)));
        Assert.Equal(PdfTrustStatus.Valid, r.Trust.Status);
        Assert.Contains(r.Trust.Revocation, x => x.Source == "CRL/Signature");
        var off = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(time: pki.Now, embedded: false)));
        Assert.Equal(PdfTrustStatus.Indeterminate, off.Trust.Status);
    }

    [Fact]
    public void Signature_timestamp_attribute_supplies_the_validation_time()
    {
        using var pki = new TestPki();
        var pdf = Sign(pki, Opts(pki, o => new PdfSignOptions { Certificate = o.Certificate, ExtraCertificates = o.ExtraCertificates, SigningTime = o.SigningTime, SubFilter = o.SubFilter, SignatureTimestamp = new TestTsa(pki.Tsa, pki.Now) }));
        var r = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: pki.GoodCrls()))).Trust;
        Assert.Equal(PdfValidationTimeSource.DocumentTimestamp, r.ValidationTimeSource);
        Assert.Equal(PdfTrustStatus.Valid, r.Status);
        // A timestamp from an untrusted TSA is ignored.
        using var rogue = new TestPki();
        var forged = Sign(pki, Opts(pki, o => new PdfSignOptions { Certificate = o.Certificate, ExtraCertificates = o.ExtraCertificates, SigningTime = o.SigningTime, SubFilter = o.SubFilter, SignatureTimestamp = new TestTsa(rogue.Tsa, pki.Now) }));
        Assert.Equal(PdfValidationTimeSource.Now, Assert.Single(PdfSignatureTrust.Evaluate(forged, pki.Policy(supplied: pki.GoodCrls()))).Trust.ValidationTimeSource);
    }

    static byte[] SignWithTs(TestPki pki, ITimestampProvider ts)
        => Sign(pki, Opts(pki, o => new PdfSignOptions { Certificate = o.Certificate, ExtraCertificates = o.ExtraCertificates, SigningTime = o.SigningTime, SubFilter = o.SubFilter, SignatureTimestamp = ts }));

    [Fact]
    public void Self_issued_backdated_timestamp_over_an_expired_certificate_is_not_valid()
    {
        using var pki = new TestPki(leafNotAfter: DateTimeOffset.UtcNow.AddMinutes(-30));
        var pdf = SignWithTs(pki, new TestTsa(pki.Leaf, pki.Now.AddDays(-5)));
        var r = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: pki.GoodCrls()))).Trust;
        Assert.NotEqual(PdfTrustStatus.Valid, r.Status);
        Assert.NotEqual(PdfValidationTimeSource.DocumentTimestamp, r.ValidationTimeSource);
        Assert.Contains(r.Findings, f => f.Code == "TimestampRejected" && f.Message.Contains("signer's own"));
    }

    [Fact]
    public void Self_issued_backdated_timestamp_over_a_later_revoked_certificate_is_not_valid()
    {
        using var pki = new TestPki();
        var pdf = SignWithTs(pki, new TestTsa(pki.Leaf, pki.Now.AddDays(-5)));
        var r = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: pki.GoodCrls((pki.Leaf, pki.Now.AddDays(-1)))))).Trust;
        Assert.Equal(PdfTrustStatus.Invalid, r.Status);
        Assert.NotEqual(PdfValidationTimeSource.DocumentTimestamp, r.ValidationTimeSource);
    }

    [Fact]
    public void Self_issued_document_timestamp_is_not_used()
    {
        using var pki = new TestPki();
        var pdf = Sign(pki, Opts(pki));
        var stamped = PdfEditor.AddDocumentTimeStamp(pdf, new TestTsa(pki.Leaf, pki.Now.AddDays(-5)));
        var r = PdfSignatureTrust.Evaluate(stamped, pki.Policy(supplied: pki.GoodCrls((pki.Leaf, pki.Now.AddDays(-1)))))[0].Trust;
        Assert.NotEqual(PdfValidationTimeSource.DocumentTimestamp, r.ValidationTimeSource);
        Assert.Equal(PdfTrustStatus.Invalid, r.Status);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("noncritical")]
    [InlineData("multiple")]
    [InlineData("wrong")]
    [InlineData("ok")]
    public void Tsa_certificate_must_have_a_single_critical_timestamping_eku(string kind)
    {
        using var pki = new TestPki();
        using var tsa = TestPki.Make("CN=Variant TSA", pki.Inter, -1, pki.Now.AddDays(-1), pki.Now.AddYears(1), r =>
        {
            r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            var ts = new Oid("1.3.6.1.5.5.7.3.8");
            switch (kind)
            {
                case "noncritical": r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ts], false)); break;
                case "multiple": r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ts, new Oid("1.3.6.1.5.5.7.3.4")], true)); break;
                case "wrong": r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.4")], true)); break;
                case "ok": r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ts], true)); break;
            }
        });
        var data = pki.GoodCrls();
        var pdf = SignWithTs(pki, new TestTsa(tsa, pki.Now));
        var r = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: data))).Trust;
        Assert.Equal(kind == "ok" ? PdfValidationTimeSource.DocumentTimestamp : PdfValidationTimeSource.Now, r.ValidationTimeSource);
        if (kind != "ok") Assert.Contains(r.Findings, f => f.Code == "TimestampRejected");
    }

    [Fact]
    public void Revoked_tsa_certificate_at_the_timestamp_time_is_not_used()
    {
        using var pki = new TestPki();
        var pdf = SignWithTs(pki, new TestTsa(pki.Tsa, pki.Now));
        var r = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: pki.GoodCrls((pki.Tsa, pki.Now.AddDays(-1)))))).Trust;
        Assert.NotEqual(PdfValidationTimeSource.DocumentTimestamp, r.ValidationTimeSource);
        // Without revocation evidence for the TSA it is Indeterminate, never silently accepted.
        var onlyRoot = new PdfValidationData();
        onlyRoot.Crls.Add(pki.GoodCrls().Crls[0]);
        var r2 = Assert.Single(PdfSignatureTrust.Evaluate(pdf, pki.Policy(supplied: onlyRoot))).Trust;
        Assert.NotEqual(PdfTrustStatus.Valid, r2.Status);
    }

    [Fact]
    public void Sha1_timestamp_imprint_is_not_used_for_time_trust()
    {
        using var pki = new TestPki();
        var policy = pki.Policy(supplied: pki.GoodCrls());
        var sigTs = Sign(pki, Opts(pki, o => new PdfSignOptions { Certificate = o.Certificate, ExtraCertificates = o.ExtraCertificates, SigningTime = o.SigningTime, SubFilter = o.SubFilter, SignatureTimestamp = new Sha1Tsa(pki.Tsa, pki.Now) }));
        var r1 = Assert.Single(PdfSignatureTrust.Evaluate(sigTs, policy)).Trust;
        Assert.Equal(PdfValidationTimeSource.Now, r1.ValidationTimeSource);
        Assert.Contains(r1.Findings, f => f.Code == "TimestampRejected" && f.Message.Contains("weak hash"));

        var docTs = PdfEditor.AddDocumentTimeStamp(Sign(pki, Opts(pki)), new TestTsa(pki.Tsa, pki.Now), HashAlgorithmName.SHA1);
        var sigs = PdfEditor.VerifySignatures(docTs);
        Assert.True(sigs[1].IsDocumentTimeStamp && sigs[1].IsIntact);
        var r2 = PdfSignatureTrust.Evaluate(docTs, policy)[0].Trust;
        Assert.Equal(PdfValidationTimeSource.Now, r2.ValidationTimeSource);
        Assert.Contains(r2.Findings, f => f.Code == "TimestampRejected" && f.Message.Contains("weak hash"));
    }

    sealed class Sha1Tsa(X509Certificate2 tsa, DateTimeOffset time) : ITimestampProvider
    {
        public byte[] GetTimestampToken(HashAlgorithmName hash, byte[] digest) => new TestTsa(tsa, time).GetTimestampToken(HashAlgorithmName.SHA1, digest);
    }

    [Fact]
    public void Field_lock_detects_a_duplicate_named_field_that_carries_the_original_value()
    {
        using var cert = SecurityTests.Cert("Locker2");
        var signed = PdfEditor.SignFile(DocMdpTests.FormPdf(), new PdfSignOptions { Certificate = cert, Lock = new PdfFieldLock(PdfFieldLockAction.All) });
        var attack = DocMdpTests.Update(signed, (f, ch) =>
        {
            var n = DocMdpTests.Num(f, d => f.Resolve(d.Get("T")) is PdfString { Text: "name" });
            var original = DocMdpTests.D(f, n);
            ch[n] = DocMdpTests.With(original, "V", new PdfString(System.Text.Encoding.Latin1.GetBytes("Evil")));
            var dup = f.Xref.Keys.Max() + 1;
            ch[dup] = original;
            var cat = (PdfDictionary)f.Resolve(f.Trailer.Get("Root"))!;
            var form = (PdfDictionary)f.Resolve(cat.Get("AcroForm"))!;
            var fields = (PdfArray)f.Resolve(form.Get("Fields"))!;
            var fieldsNum = (form.Get("Fields") as PdfRef)?.Number;
            var arr = new PdfArray([.. fields.Items, new PdfRef(dup, 0)]);
            if (fieldsNum is { } fnum) ch[fnum] = arr;
            else if (cat.Get("AcroForm") is PdfRef fr) ch[fr.Number] = DocMdpTests.With(form, "Fields", arr);
            else ch[((PdfRef)f.Trailer.Get("Root")!).Number] = DocMdpTests.With(cat, "AcroForm", DocMdpTests.With(form, "Fields", arr));
        });
        var s = Assert.Single(PdfEditor.VerifySignatures(attack));
        Assert.NotEmpty(s.FieldMdpFindings);
    }

    [Fact]
    public void Dss_requires_an_existing_signature_and_data()
    {
        using var pki = new TestPki();
        Assert.Throws<InvalidOperationException>(() => PdfEditor.AddValidationInformation(DocMdpTests.FormPdf(), pki.GoodCrls()));
        var pdf = Sign(pki, Opts(pki));
        Assert.Throws<ArgumentException>(() => PdfEditor.AddValidationInformation(pdf, new PdfValidationData()));
        Assert.Throws<InvalidOperationException>(() => PdfEditor.AddValidationInformation(pdf, pki.GoodCrls(), "Nope"));
    }

    public static IEnumerable<object[]> Algorithms()
    {
        foreach (var h in new[] { "SHA256", "SHA384", "SHA512" })
        foreach (var kind in new[] { "rsa", "rsa-pss", "p256", "p384", "p521" })
        foreach (var sub in new[] { PdfSignatureSubFilter.Pkcs7Detached, PdfSignatureSubFilter.CadesDetached })
            yield return [h, kind, sub];
    }

    [Theory, MemberData(nameof(Algorithms))]
    public void Signing_algorithms_produce_verifiable_signatures(string hash, string kind, PdfSignatureSubFilter sub)
    {
        X509Certificate2 cert;
        using (AsymmetricAlgorithm key = kind.StartsWith("rsa") ? RSA.Create(2048) : ECDsa.Create(kind switch { "p256" => ECCurve.NamedCurves.nistP256, "p384" => ECCurve.NamedCurves.nistP384, _ => ECCurve.NamedCurves.nistP521 }))
        {
            CertificateRequest req = key is RSA r ? new("CN=Alg", r, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) : new("CN=Alg", (ECDsa)key, HashAlgorithmName.SHA256);
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            cert = X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx, "pw"), "pw", X509KeyStorageFlags.Exportable);
        }
        using (cert)
        {
            var o = new PdfSignOptions { Certificate = cert, Hash = new HashAlgorithmName(hash), RsaPss = kind == "rsa-pss", SubFilter = sub };
            var s = Assert.Single(PdfEditor.VerifySignatures(PdfEditor.SignFile(DocMdpTests.FormPdf(), o)));
            Assert.True(s.IsIntact, s.Error);
            Assert.Equal(sub == PdfSignatureSubFilter.CadesDetached ? "ETSI.CAdES.detached" : "adbe.pkcs7.detached", s.SubFilter);
            Assert.Equal(sub == PdfSignatureSubFilter.CadesDetached ? true : null, s.SigningCertificateV2Valid);
            if (sub == PdfSignatureSubFilter.CadesDetached) Assert.Null(s.SigningTime); else Assert.NotNull(s.SigningTime);
        }
    }

    [Fact]
    public void Export_pades_samples()
    {
        var dir = Environment.GetEnvironmentVariable("PDF_SECURITY_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var pki = new TestPki();
        File.WriteAllBytes(Path.Combine(dir, "root.cer"), pki.Root.RawData);
        File.WriteAllBytes(Path.Combine(dir, "inter.cer"), pki.Inter.RawData);
        File.WriteAllBytes(Path.Combine(dir, "tsa.cer"), pki.Tsa.RawData);
        var form = DocMdpTests.FormPdf();

        var cades = PdfEditor.SignFile(form, Opts(pki));
        File.WriteAllBytes(Path.Combine(dir, "lib-cades-p256-sha256.pdf"), cades);
        var cadesT = PdfEditor.SignFile(form, Opts(pki, o => new PdfSignOptions { Certificate = o.Certificate, ExtraCertificates = o.ExtraCertificates, SigningTime = o.SigningTime, SubFilter = o.SubFilter, SignatureTimestamp = new TestTsa(pki.Tsa, DateTimeOffset.UtcNow) }));
        File.WriteAllBytes(Path.Combine(dir, "lib-cades-t.pdf"), cadesT);
        var data = pki.GoodCrls();
        data.Certificates.Add(pki.Inter);
        data.Certificates.Add(pki.Tsa);
        var lt = PdfEditor.AddValidationInformation(cadesT, data);
        File.WriteAllBytes(Path.Combine(dir, "lib-pades-dss.pdf"), lt);
        File.WriteAllBytes(Path.Combine(dir, "lib-pades-lta.pdf"), PdfEditor.AddDocumentTimeStamp(lt, new TestTsa(pki.Tsa, DateTimeOffset.UtcNow)));

        // Same flow with signer and TSA issued directly by the root (no intermediate CA).
        using var flatSigner = TestPki.Make("CN=Flat Signer", pki.Root, -1, pki.Now.AddDays(-1), pki.Now.AddYears(1), r => r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true)));
        using var flatTsa = TestPki.Make("CN=Flat TSA", pki.Root, -1, pki.Now.AddDays(-1), pki.Now.AddYears(1), r => r.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.8")], true)));
        var flat = PdfEditor.SignFile(form, new PdfSignOptions { Certificate = flatSigner, SigningTime = pki.Now, SubFilter = PdfSignatureSubFilter.CadesDetached, SignatureTimestamp = new TestTsa(flatTsa, DateTimeOffset.UtcNow) });
        var flatData = new PdfValidationData();
        flatData.Crls.Add(pki.Crl(pki.Root, pki.Now.AddHours(-1), pki.Now.AddDays(1)));
        flatData.Certificates.Add(flatTsa);
        File.WriteAllBytes(Path.Combine(dir, "lib-pades-lta-flat.pdf"), PdfEditor.AddDocumentTimeStamp(PdfEditor.AddValidationInformation(flat, flatData), new TestTsa(flatTsa, DateTimeOffset.UtcNow)));
        File.WriteAllBytes(Path.Combine(dir, "lib-pades-certified-p2.pdf"), PdfEditor.SignFile(form, Opts(pki, o => new PdfSignOptions { Certificate = o.Certificate, ExtraCertificates = o.ExtraCertificates, SigningTime = o.SigningTime, SubFilter = o.SubFilter, Certify = PdfDocMdpPermission.FormFillAndSign })));

        foreach (var (name, hash, kind) in new[] { ("rsa-pss-sha384", "SHA384", "rsa-pss"), ("rsa-pkcs1-sha512", "SHA512", "rsa"), ("p384-sha384", "SHA384", "p384"), ("p521-sha512", "SHA512", "p521") })
        {
            using AsymmetricAlgorithm key = kind.StartsWith("rsa") ? RSA.Create(2048) : ECDsa.Create(kind == "p384" ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP521);
            CertificateRequest req = key is RSA r ? new($"CN={name}", r, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) : new($"CN={name}", (ECDsa)key, HashAlgorithmName.SHA256);
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            using var withKey = X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx, "pw"), "pw", X509KeyStorageFlags.Exportable);
            File.WriteAllBytes(Path.Combine(dir, $"{name}-signer.cer"), c.RawData);
            File.WriteAllBytes(Path.Combine(dir, $"lib-{name}.pdf"), PdfEditor.SignFile(form, new PdfSignOptions { Certificate = withKey, Hash = new HashAlgorithmName(hash), RsaPss = kind == "rsa-pss", SubFilter = PdfSignatureSubFilter.CadesDetached }));
        }
    }

    [Theory]
    [InlineData(PdfFieldLockAction.All, true)]
    [InlineData(PdfFieldLockAction.Include, true)]
    [InlineData(PdfFieldLockAction.Exclude, false)]
    public void Field_locks_report_changes_to_locked_fields(PdfFieldLockAction action, bool expectFinding)
    {
        using var cert = SecurityTests.Cert("Locker");
        var signed = PdfEditor.SignFile(DocMdpTests.FormPdf(), new PdfSignOptions { Certificate = cert, Lock = new PdfFieldLock(action, ["name"]) });
        var before = Assert.Single(PdfEditor.VerifySignatures(signed));
        Assert.Equal(action.ToString(), before.FieldLockAction);
        Assert.Empty(before.FieldMdpFindings);
        var filled = DocMdpTests.Update(signed, (f, ch) =>
        {
            var n = DocMdpTests.Num(f, d => f.Resolve(d.Get("T")) is PdfString { Text: "name" });
            ch[n] = DocMdpTests.With(DocMdpTests.D(f, n), "V", new PdfString(System.Text.Encoding.Latin1.GetBytes("Grace")));
        });
        var after = Assert.Single(PdfEditor.VerifySignatures(filled));
        Assert.True(after.IsIntact);
        Assert.Equal(expectFinding, after.FieldMdpFindings.Any(m => m.Contains("Locked field name")));
    }

    [Fact]
    public void Signature_without_a_lock_reports_no_field_lock()
    {
        using var cert = SecurityTests.Cert("NoLock");
        var s = Assert.Single(PdfEditor.VerifySignatures(PdfEditor.SignFile(DocMdpTests.FormPdf(), new PdfSignOptions { Certificate = cert })));
        Assert.Null(s.FieldLockAction);
    }

    [Fact]
    public void Unsupported_digest_is_rejected()
    {
        using var cert = SecurityTests.Cert("Weak");
        Assert.Throws<ArgumentException>(() => PdfEditor.SignFile(DocMdpTests.FormPdf(), new PdfSignOptions { Certificate = cert, Hash = HashAlgorithmName.SHA1 }));
    }

    [Fact]
    public void Http_timestamp_client_is_opt_in_and_checks_the_response()
    {
        Assert.Throws<ArgumentException>(() => new Rfc3161TimestampClient(new Uri("file:///tsa")));
        using var pki = new TestPki();
        var digest = SHA256.HashData([1, 2, 3]);
        var token = new TestTsa(pki.Tsa, pki.Now).GetTimestampToken(HashAlgorithmName.SHA256, digest);
        var resp = new AsnWriter(AsnEncodingRules.DER);
        using (resp.PushSequence())
        {
            using (resp.PushSequence()) resp.WriteInteger(0);
            resp.WriteEncodedValue(token);
        }
        // The canned TSTInfo carries nonce-less content, so a nonce-checking client must refuse it.
        Assert.Throws<InvalidOperationException>(() => Rfc3161TimestampClient.ParseResponse(resp.Encode(), "2.16.840.1.101.3.4.2.1", digest, 5));
        Assert.Throws<InvalidOperationException>(() => Rfc3161TimestampClient.ParseResponse([1, 2, 3], "2.16.840.1.101.3.4.2.1", digest, 5));
    }
}
