using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace PortableDocument.Core;

/// <summary>
/// Evaluates whether each signature in a PDF is trustworthy under a <see cref="PdfTrustPolicy"/>. This is separate from the integrity result of
/// <see cref="PdfSignatures.Verify(PdfFile)"/>. Only an explicit <see cref="PdfTrustStatus.Valid"/> means the policy was satisfied; it is never inferred from partial evidence.
/// </summary>
public static class PdfSignatureTrust
{
    const string RevocationInfoArchival = "1.2.840.113583.1.1.8";

    public static IReadOnlyList<PdfSignatureTrustResult> Evaluate(byte[] pdf, PdfTrustPolicy policy, string password = "")
        => Evaluate(PdfFile.Load(pdf, password), policy);

    public static IReadOnlyList<PdfSignatureTrustResult> Evaluate(PdfFile f, PdfTrustPolicy policy)
    {
        var infos = PdfSignatures.Verify(f);
        var dss = policy.UseEmbeddedValidationData ? PdfSignatures.ReadDssData(f) : null;
        var results = new List<PdfSignatureTrustResult>();
        foreach (var info in infos)
        {
            var sources = new List<(string, PdfValidationData)>();
            if (policy.SuppliedData is not null) sources.Add(("Supplied", policy.SuppliedData));
            var embedded = policy.UseEmbeddedValidationData ? Embedded(info) : null;
            if (embedded is not null) sources.Add(("Signature", embedded));
            if (dss is not null) sources.Add(("DSS", dss));
            results.Add(new PdfSignatureTrustResult(info, Evaluate(info, infos, policy, sources)));
        }
        return results;
    }

    static PdfTrustResult Evaluate(PdfSignatureInfo info, IReadOnlyList<PdfSignatureInfo> all, PdfTrustPolicy policy, List<(string, PdfValidationData)> sources)
    {
        if (info.SignerCertificate is null)
            return new PdfTrustResult(PdfTrustStatus.Invalid, DateTimeOffset.UtcNow, PdfValidationTimeSource.Now, [], [new PdfTrustFinding(PdfTrustSeverity.Invalid, "NoSignerCertificate", info.Error ?? "The signature has no readable signer certificate.")], []);

        var findings = new List<PdfTrustFinding>();
        DateTimeOffset time = DateTimeOffset.UtcNow;
        var src = PdfValidationTimeSource.Now;
        if (policy.ValidationTime is { } fixedTime) { time = fixedTime; src = PdfValidationTimeSource.Policy; }
        else
        {
            if (policy.UseDocumentTimestamps && !info.IsDocumentTimeStamp && TrustedTimestampTime(info, all, policy, sources, findings) is { } ts)
            {
                time = ts; src = PdfValidationTimeSource.DocumentTimestamp;
                findings.Add(new PdfTrustFinding(PdfTrustSeverity.Info, "DocumentTimestamp", $"Validated as of the trusted document timestamp {ts:u}; the TSA certificate (timeStamping EKU, not the signer) chains to a trust anchor and was checked for revocation as of that time."));
            }
            else if (policy.UseClaimedSigningTime && (info.IsDocumentTimeStamp ? info.TimeStampTime : info.SigningTime) is { } claimed)
            {
                time = claimed; src = PdfValidationTimeSource.ClaimedSigningTime;
                findings.Add(new PdfTrustFinding(PdfTrustSeverity.Info, "ClaimedSigningTime", "The validation time is the signer-claimed signing time, which no third party vouches for."));
            }
        }

        var pool = new List<(string, PdfValidationData)>(sources);
        if (Certificates(info) is { Count: > 0 } extra) { var d = new PdfValidationData(); d.Certificates.AddRange(extra); pool.Add(("Signature", d)); }
        var r = PdfCertificateValidator.Validate(info.SignerCertificate, policy, time, src, pool, true, !info.IsDocumentTimeStamp);
        if (!info.IsIntact)
            findings.Add(new PdfTrustFinding(PdfTrustSeverity.Invalid, "SignatureNotIntact", info.Error ?? (info.ByteRangeValid ? "The signature does not verify over the signed bytes." : "Malformed /ByteRange.")));
        if (info.SigningCertificateV2Valid == false)
            findings.Add(new PdfTrustFinding(PdfTrustSeverity.Invalid, "SigningCertificateV2Mismatch", "The ESS signing-certificate-v2 attribute does not match the signer certificate."));
        if (findings.Count == 0) return r;
        var all2 = findings.Concat(r.Findings).ToList();
        var st = all2.Any(x => x.Severity == PdfTrustSeverity.Invalid) ? PdfTrustStatus.Invalid : all2.Any(x => x.Severity == PdfTrustSeverity.Indeterminate) ? PdfTrustStatus.Indeterminate : PdfTrustStatus.Valid;
        return r with { Status = st, Findings = all2 };
    }

    /// <summary>RFC 3161 §2.3 / RFC 5280 §4.2.1.12: a TSA certificate has exactly one EKU, id-kp-timeStamping, marked critical.</summary>
    internal static string? TsaCertificateProblem(X509Certificate2 tsa, DateTimeOffset genTime)
    {
        var eku = tsa.Extensions["2.5.29.37"] as X509EnhancedKeyUsageExtension;
        if (eku is null) return "the TSA certificate has no extendedKeyUsage extension";
        if (!eku.Critical) return "the TSA certificate's extendedKeyUsage is not critical";
        var usages = eku.EnhancedKeyUsages.Cast<Oid>().ToList();
        if (usages.Count != 1 || usages[0].Value != "1.3.6.1.5.5.7.3.8") return "the TSA certificate's extendedKeyUsage must be exactly id-kp-timeStamping";
        if (genTime.UtcDateTime < tsa.NotBefore.ToUniversalTime() || genTime.UtcDateTime > tsa.NotAfter.ToUniversalTime()) return "the timestamp time is outside the TSA certificate validity";
        return null;
    }

    static bool WeakImprint(string? oid) => oid is "1.3.14.3.2.26" or "1.2.840.113549.2.5";

    static DateTimeOffset? TrustedTimestampTime(PdfSignatureInfo info, IReadOnlyList<PdfSignatureInfo> all, PdfTrustPolicy policy, IReadOnlyList<(string, PdfValidationData)> sources, List<PdfTrustFinding> findings)
    {
        if (!info.ByteRangeValid) return null;
        var end = info.ByteRange[2] + info.ByteRange[3];
        DateTimeOffset? best = null;
        void Consider(X509Certificate2 tsa, IEnumerable<X509Certificate2> certs, DateTimeOffset t, string? imprintAlg)
        {
            string? why = null;
            if (WeakImprint(imprintAlg)) why = "the timestamp message imprint uses a weak hash algorithm (SHA-1/MD5)";
            else if (info.SignerCertificate is not null && tsa.RawData.AsSpan().SequenceEqual(info.SignerCertificate.RawData)) why = "the timestamp is signed by the signer's own certificate";
            else why = TsaCertificateProblem(tsa, t);
            if (why is null)
            {
                var d = new PdfValidationData();
                d.Certificates.AddRange(certs);
                var src = new List<(string, PdfValidationData)>(sources) { ("Timestamp", d) };
                var r = PdfCertificateValidator.Validate(tsa, policy, t, PdfValidationTimeSource.DocumentTimestamp, src, true, false);
                if (r.Status == PdfTrustStatus.Valid) { if (best is null || t < best) best = t; return; }
                why = "the TSA certificate could not be validated as of the timestamp time: " + string.Join("; ", r.Findings.Where(x => x.Severity != PdfTrustSeverity.Info).Select(x => x.Message));
            }
            findings.Add(new PdfTrustFinding(PdfTrustSeverity.Info, "TimestampRejected", "A timestamp was not used as the validation time: " + why));
        }
        if (SignatureTimestamp(info) is { } st) Consider(st.Tsa, st.Certs, st.Time, st.Alg);
        foreach (var ts in all)
        {
            if (!ts.IsDocumentTimeStamp || !ts.IsIntact || ts.TimeStampTime is not { } t || ts.SignerCertificate is null) continue;
            if (ts.ByteRange[1] < end || ts.ByteRange[0] != 0) continue;
            Consider(ts.SignerCertificate, Certificates(ts), t, ts.TimeStampImprintAlgorithm);
        }
        return best;
    }
    /// <summary>The RFC 3161 token in a signature's signature-time-stamp unsigned attribute (CAdES-T), when its imprint covers the signature value and its CMS signature verifies.</summary>
    static (DateTimeOffset Time, X509Certificate2 Tsa, List<X509Certificate2> Certs, string Alg)? SignatureTimestamp(PdfSignatureInfo info)
    {
        if (info.Cms is null || info.IsDocumentTimeStamp) return null;
        try
        {
            var cms = new SignedCms();
            cms.Decode(info.Cms);
            if (cms.SignerInfos.Count == 0) return null;
            var si = cms.SignerInfos[0];
            foreach (var attr in si.UnsignedAttributes.Cast<CryptographicAttributeObject>())
            {
                if (attr.Oid?.Value != "1.2.840.113549.1.9.16.2.14" || attr.Values.Count == 0) continue;
                var token = new SignedCms();
                token.Decode(attr.Values[0].RawData);
                if (token.ContentInfo.ContentType.Value != "1.2.840.113549.1.9.16.1.4" || token.SignerInfos.Count == 0 || token.SignerInfos[0].Certificate is not { } tsa) continue;
                var tst = new AsnReader(token.ContentInfo.Content, AsnEncodingRules.BER).ReadSequence();
                tst.ReadInteger(); tst.ReadObjectIdentifier();
                var imprint = tst.ReadSequence();
                var alg = imprint.ReadSequence().ReadObjectIdentifier();
                var hashed = imprint.ReadOctetString();
                tst.ReadInteger();
                var genTime = tst.ReadGeneralizedTime();
                var h = PdfRevocationParser.HashFromOid(alg);
                if (h is null && alg is not ("1.2.840.113549.2.5")) continue;
                if (h is null || WeakImprint(alg)) return (genTime, tsa, token.Certificates.Cast<X509Certificate2>().ToList(), alg);
                var expected = h.Value.Name switch { "SHA384" => SHA384.HashData(si.GetSignature()), "SHA512" => SHA512.HashData(si.GetSignature()), "SHA1" => SHA1.HashData(si.GetSignature()), _ => SHA256.HashData(si.GetSignature()) };
                if (!expected.AsSpan().SequenceEqual(hashed)) continue;
                token.CheckSignature(true);
                return (genTime, tsa, token.Certificates.Cast<X509Certificate2>().ToList(), alg);
            }
        }
        catch (Exception e) when (e is CryptographicException or AsnContentException or InvalidOperationException) { }
        return null;
    }

    static List<X509Certificate2> Certificates(PdfSignatureInfo info)
    {
        var list = new List<X509Certificate2>();
        if (info.Cms is null) return list;
        try
        {
            var cms = new SignedCms();
            cms.Decode(info.Cms);
            foreach (var c in cms.Certificates.Cast<X509Certificate2>().Take(256)) list.Add(c);
        }
        catch (System.Security.Cryptography.CryptographicException) { }
        return list;
    }

    /// <summary>Revocation data from an adbe-revocationInfoArchival signed or unsigned attribute.</summary>
    static PdfValidationData? Embedded(PdfSignatureInfo info)
    {
        if (info.Cms is null || info.IsDocumentTimeStamp) return null;
        try
        {
            var cms = new SignedCms();
            cms.Decode(info.Cms);
            if (cms.SignerInfos.Count == 0) return null;
            var data = new PdfValidationData();
            var si = cms.SignerInfos[0];
            foreach (var attr in si.SignedAttributes.Cast<CryptographicAttributeObject>().Concat(si.UnsignedAttributes.Cast<CryptographicAttributeObject>()))
            {
                if (attr.Oid?.Value != RevocationInfoArchival) continue;
                foreach (var v in attr.Values.Cast<AsnEncodedData>().Take(8)) Parse(v.RawData, data);
            }
            return data.IsEmpty ? null : data;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or AsnContentException) { return null; }
    }

    static void Parse(byte[] der, PdfValidationData into)
    {
        try
        {
            var seq = new AsnReader(der, AsnEncodingRules.BER).ReadSequence();
            while (seq.HasData)
            {
                var tag = seq.PeekTag();
                var list = seq.ReadSequence(tag);
                if (tag.TagClass != TagClass.ContextSpecific || tag.TagValue > 1) continue;
                var inner = list.ReadSequence();
                while (inner.HasData && (tag.TagValue == 0 ? into.Crls.Count : into.OcspResponses.Count) < 256)
                {
                    var blob = inner.ReadEncodedValue().ToArray();
                    (tag.TagValue == 0 ? into.Crls : into.OcspResponses).Add(blob);
                }
            }
        }
        catch (AsnContentException) { }
    }
}
