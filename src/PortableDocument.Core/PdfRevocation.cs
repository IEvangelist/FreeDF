using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PortableDocument.Core;

/// <summary>Validation material (certificates, CRLs, OCSP responses) supplied by the caller, read from a /DSS, or taken from a signature's revocation-info attribute.</summary>
public sealed class PdfValidationData
{
    public List<X509Certificate2> Certificates { get; } = [];
    /// <summary>DER CertificateList (RFC 5280 §5).</summary>
    public List<byte[]> Crls { get; } = [];
    /// <summary>DER OCSPResponse (RFC 6960 §4.2.1).</summary>
    public List<byte[]> OcspResponses { get; } = [];

    public bool IsEmpty => Certificates.Count == 0 && Crls.Count == 0 && OcspResponses.Count == 0;

    internal void Merge(PdfValidationData other)
    {
        foreach (var c in other.Certificates) if (!Certificates.Any(x => x.RawData.AsSpan().SequenceEqual(c.RawData))) Certificates.Add(c);
        foreach (var c in other.Crls) if (!Crls.Any(x => x.AsSpan().SequenceEqual(c))) Crls.Add(c);
        foreach (var o in other.OcspResponses) if (!OcspResponses.Any(x => x.AsSpan().SequenceEqual(o))) OcspResponses.Add(o);
    }
}

/// <summary>Outcome of the revocation check for one certificate.</summary>
public enum PdfRevocationOutcome
{
    /// <summary>Authenticated evidence shows the certificate was not revoked at the validation time.</summary>
    Good,
    /// <summary>Authenticated evidence shows the certificate was revoked at or before the validation time.</summary>
    Revoked,
    /// <summary>No usable evidence covers the validation time (absent, unverifiable, stale, out of scope, or the responder answered "unknown").</summary>
    Indeterminate,
    /// <summary>Not checked: trust anchor, or revocation checking disabled by the policy.</summary>
    NotChecked,
}

public sealed record PdfRevocationStatus(string Subject, PdfRevocationOutcome Outcome, string Source, DateTimeOffset? EvidenceTime, DateTimeOffset? RevocationTime, string Detail);

/// <summary>Strict, bounded parsers for CRLs and OCSP responses and signature verification for them. Every failure is reported as "unusable", never as an exception.</summary>
static class PdfRevocationParser
{
    public const int MaxBlobBytes = 32 * 1024 * 1024;
    const int MaxCrlEntries = 2_000_000;

    internal sealed record Crl(byte[] IssuerDer, DateTimeOffset ThisUpdate, DateTimeOffset? NextUpdate, Dictionary<string, (DateTimeOffset Date, int Reason)> Revoked,
        byte[] Tbs, string SigOid, byte[]? SigParams, byte[] Signature, string? OutOfScope);

    internal sealed record SingleResponse(string HashOid, byte[] NameHash, byte[] KeyHash, byte[] Serial, int Status, DateTimeOffset? RevokedAt, DateTimeOffset ThisUpdate, DateTimeOffset? NextUpdate);

    internal sealed record Ocsp(byte[] Tbs, string SigOid, byte[]? SigParams, byte[] Signature, List<X509Certificate2> Certs, byte[]? ResponderName, byte[]? ResponderKeyHash,
        DateTimeOffset ProducedAt, List<SingleResponse> Responses);

    public static string SerialKey(ReadOnlySpan<byte> serial)
    {
        var i = 0;
        while (i < serial.Length - 1 && serial[i] == 0) i++;
        return Convert.ToHexString(serial[i..]);
    }

    static DateTimeOffset ReadTime(AsnReader r)
        => r.PeekTag().TagValue == 23 ? r.ReadUtcTime() : r.ReadGeneralizedTime();

    static (string Oid, byte[]? Params) ReadAlg(AsnReader r)
    {
        var s = r.ReadSequence();
        var oid = s.ReadObjectIdentifier();
        return (oid, s.HasData ? s.ReadEncodedValue().ToArray() : null);
    }

    public static Crl? ParseCrl(byte[] der)
    {
        if (der.Length == 0 || der.Length > MaxBlobBytes) return null;
        try
        {
            var outer = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
            var tbsRaw = outer.ReadEncodedValue().ToArray();
            var (sigOid, sigParams) = ReadAlg(outer);
            var sig = outer.ReadBitString(out var unused);
            if (unused != 0) return null;
            var tbs = new AsnReader(tbsRaw, AsnEncodingRules.DER).ReadSequence();
            if (tbs.PeekTag().TagValue == 2) tbs.ReadInteger();
            tbs.ReadSequence();
            var issuer = tbs.ReadEncodedValue().ToArray();
            var thisUpdate = ReadTime(tbs);
            DateTimeOffset? next = null;
            if (tbs.HasData && tbs.PeekTag().TagValue is 23 or 24 && tbs.PeekTag().TagClass == TagClass.Universal) next = ReadTime(tbs);
            var revoked = new Dictionary<string, (DateTimeOffset, int)>();
            if (tbs.HasData && tbs.PeekTag().TagValue == 16 && tbs.PeekTag().TagClass == TagClass.Universal)
            {
                var list = tbs.ReadSequence();
                while (list.HasData)
                {
                    if (revoked.Count >= MaxCrlEntries) return null;
                    var e = list.ReadSequence();
                    var serial = SerialKey(e.ReadIntegerBytes().Span);
                    var date = ReadTime(e);
                    var reason = 0;
                    if (e.HasData)
                    {
                        var exts = e.ReadSequence();
                        while (exts.HasData)
                        {
                            var x = exts.ReadSequence();
                            var oid = x.ReadObjectIdentifier();
                            if (x.PeekTag().TagValue == 1) x.ReadBoolean();
                            var val = x.ReadOctetString();
                            if (oid == "2.5.29.21") reason = (int)new AsnReader(val, AsnEncodingRules.DER).ReadEnumeratedValue<CrlReason>();
                        }
                    }
                    revoked[serial] = (date, reason);
                }
            }
            string? scope = null;
            if (tbs.HasData && tbs.PeekTag().TagClass == TagClass.ContextSpecific && tbs.PeekTag().TagValue == 0)
            {
                var exts = tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
                while (exts.HasData)
                {
                    var x = exts.ReadSequence();
                    var oid = x.ReadObjectIdentifier();
                    var critical = x.PeekTag().TagValue == 1 && x.ReadBoolean();
                    var val = x.ReadOctetString();
                    switch (oid)
                    {
                        case "2.5.29.27": scope = "delta CRL"; break;
                        case "2.5.29.28":
                            var idp = new AsnReader(val, AsnEncodingRules.DER).ReadSequence();
                            while (idp.HasData)
                            {
                                var t = idp.PeekTag();
                                if (t.TagClass != TagClass.ContextSpecific || t.TagValue != 0) scope = "partitioned CRL (issuing distribution point restricts its scope)";
                                idp.ReadEncodedValue();
                            }
                            break;
                        case "2.5.29.20" or "2.5.29.35": break;
                        default: if (critical) scope = "critical extension " + oid; break;
                    }
                }
            }
            return new Crl(issuer, thisUpdate, next, revoked, tbsRaw, sigOid, sigParams, sig, scope);
        }
        catch (Exception e) when (e is AsnContentException or CryptographicException or InvalidOperationException or ArgumentException) { return null; }
    }

    enum CrlReason { Unspecified, KeyCompromise, CaCompromise, AffiliationChanged, Superseded, CessationOfOperation, CertificateHold, Unused7, RemoveFromCrl, PrivilegeWithdrawn, AaCompromise }

    public static Ocsp? ParseOcsp(byte[] der)
    {
        if (der.Length == 0 || der.Length > MaxBlobBytes) return null;
        try
        {
            var resp = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
            if (resp.ReadEnumeratedValue<OcspStatus>() != OcspStatus.Successful) return null;
            var rb = resp.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
            if (rb.ReadObjectIdentifier() != "1.3.6.1.5.5.7.48.1.1") return null;
            var basic = new AsnReader(rb.ReadOctetString(), AsnEncodingRules.DER).ReadSequence();
            var tbsRaw = basic.ReadEncodedValue().ToArray();
            var (sigOid, sigParams) = ReadAlg(basic);
            var sig = basic.ReadBitString(out var unused);
            if (unused != 0) return null;
            var certs = new List<X509Certificate2>();
            if (basic.HasData)
            {
                var cs = basic.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
                while (cs.HasData && certs.Count < 32) certs.Add(X509CertificateLoader.LoadCertificate(cs.ReadEncodedValue().ToArray()));
            }
            var tbs = new AsnReader(tbsRaw, AsnEncodingRules.DER).ReadSequence();
            if (tbs.PeekTag() == new Asn1Tag(TagClass.ContextSpecific, 0, true)) tbs.ReadEncodedValue();
            byte[]? name = null, keyHash = null;
            var rid = tbs.PeekTag();
            if (rid.TagClass == TagClass.ContextSpecific && rid.TagValue == 1) name = tbs.ReadSequence(rid).ReadEncodedValue().ToArray();
            else if (rid.TagClass == TagClass.ContextSpecific && rid.TagValue == 2) keyHash = new AsnReader(tbs.ReadSequence(rid).ReadEncodedValue(), AsnEncodingRules.DER).ReadOctetString();
            else return null;
            var produced = tbs.ReadGeneralizedTime();
            var singles = new List<SingleResponse>();
            var list = tbs.ReadSequence();
            while (list.HasData && singles.Count < 10_000)
            {
                var s = list.ReadSequence();
                var id = s.ReadSequence();
                var (hashOid, _) = ReadAlg(id);
                var nh = id.ReadOctetString();
                var kh = id.ReadOctetString();
                var serial = id.ReadIntegerBytes().ToArray();
                var tag = s.PeekTag();
                int status; DateTimeOffset? revAt = null;
                if (tag.TagClass != TagClass.ContextSpecific || tag.TagValue > 2) return null;
                status = tag.TagValue;
                if (status == 1) { var ri = s.ReadSequence(tag); revAt = ri.ReadGeneralizedTime(); }
                else s.ReadEncodedValue();
                var thisUpdate = s.ReadGeneralizedTime();
                DateTimeOffset? next = null;
                if (s.HasData && s.PeekTag() == new Asn1Tag(TagClass.ContextSpecific, 0, true)) next = s.ReadSequence(s.PeekTag()).ReadGeneralizedTime();
                singles.Add(new SingleResponse(hashOid, nh, kh, serial, status, revAt, thisUpdate, next));
            }
            return new Ocsp(tbsRaw, sigOid, sigParams, sig, certs, name, keyHash, produced, singles);
        }
        catch (Exception e) when (e is AsnContentException or CryptographicException or InvalidOperationException or ArgumentException) { return null; }
    }

    enum OcspStatus { Successful, MalformedRequest, InternalError, TryLater, Unused4, SigRequired, Unauthorized }

    /// <summary>Verifies <paramref name="sig"/> over <paramref name="data"/> with the public key of <paramref name="signer"/>. SHA-1 and MD5 signatures are rejected.</summary>
    public static bool Verify(X509Certificate2 signer, byte[] data, string oid, byte[]? algParams, byte[] sig, out string error)
    {
        error = "";
        try
        {
            HashAlgorithmName? h = oid switch
            {
                "1.2.840.113549.1.1.11" or "1.2.840.10045.4.3.2" => HashAlgorithmName.SHA256,
                "1.2.840.113549.1.1.12" or "1.2.840.10045.4.3.3" => HashAlgorithmName.SHA384,
                "1.2.840.113549.1.1.13" or "1.2.840.10045.4.3.4" => HashAlgorithmName.SHA512,
                _ => null,
            };
            if (oid == "1.2.840.113549.1.1.10")
            {
                if (!PssHash(algParams, out var ph, out var salt) || salt != HashSize(ph)) { error = "Unsupported RSASSA-PSS parameters"; return false; }
                using var rsaPss = signer.GetRSAPublicKey();
                return rsaPss is not null && rsaPss.VerifyData(data, sig, ph, RSASignaturePadding.Pss);
            }
            if (h is null) { error = "Unsupported or weak signature algorithm " + oid; return false; }
            if (oid.StartsWith("1.2.840.113549.1.1.", StringComparison.Ordinal))
            {
                using var rsa = signer.GetRSAPublicKey();
                return rsa is not null && rsa.VerifyData(data, sig, h.Value, RSASignaturePadding.Pkcs1);
            }
            using var ec = signer.GetECDsaPublicKey();
            return ec is not null && ec.VerifyData(data, sig, h.Value, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception e) when (e is CryptographicException or AsnContentException or ArgumentException) { error = "Signature could not be verified: " + e.GetType().Name; return false; }
    }

    static int HashSize(HashAlgorithmName h) => h.Name switch { "SHA256" => 32, "SHA384" => 48, _ => 64 };

    static bool PssHash(byte[]? p, out HashAlgorithmName hash, out int salt)
    {
        hash = HashAlgorithmName.SHA1; salt = 20;
        if (p is null) return false;
        try
        {
            var s = new AsnReader(p, AsnEncodingRules.DER).ReadSequence();
            while (s.HasData)
            {
                var t = s.PeekTag();
                if (t.TagValue == 0) hash = HashFromOid(ReadAlg(new AsnReader(s.ReadSequence(t).ReadEncodedValue(), AsnEncodingRules.DER)).Oid) ?? HashAlgorithmName.MD5;
                else if (t.TagValue == 2) salt = (int)s.ReadSequence(t).ReadInteger();
                else s.ReadEncodedValue();
            }
            return hash != HashAlgorithmName.SHA1 && hash != HashAlgorithmName.MD5;
        }
        catch (Exception e) when (e is AsnContentException or OverflowException) { return false; }
    }

    public static HashAlgorithmName? HashFromOid(string oid) => oid switch
    {
        "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
        "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384,
        "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
        "1.3.14.3.2.26" => HashAlgorithmName.SHA1,
        _ => null,
    };

    /// <summary>Verifies that <paramref name="cert"/> carries a valid signature by <paramref name="issuer"/>.</summary>
    public static bool IsIssuedBy(X509Certificate2 cert, X509Certificate2 issuer)
    {
        try
        {
            if (!cert.IssuerName.RawData.AsSpan().SequenceEqual(issuer.SubjectName.RawData)) return false;
            var outer = new AsnReader(cert.RawData, AsnEncodingRules.DER).ReadSequence();
            var tbs = outer.ReadEncodedValue().ToArray();
            var (oid, p) = ReadAlg(outer);
            var sig = outer.ReadBitString(out _);
            return Verify(issuer, tbs, oid, p, sig, out _);
        }
        catch (Exception e) when (e is AsnContentException or CryptographicException) { return false; }
    }
}
