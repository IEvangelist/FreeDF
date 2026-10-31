using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PortableDocument.Core;

public enum PdfTrustStatus
{
    /// <summary>Every check the policy asks for passed: the chain reaches a configured trust anchor, the certificates were valid and not revoked at the validation time, and the key/EKU/policy requirements hold.</summary>
    Valid,
    /// <summary>Nothing proved the signer wrong, but something needed for a positive conclusion is missing (no anchor, unknown issuer, missing or stale revocation data, unsupported algorithm).</summary>
    Indeterminate,
    /// <summary>A check failed: expired or not-yet-valid certificate, revoked certificate, bad path constraint, key usage or policy mismatch, or the signature itself is not intact.</summary>
    Invalid,
}

public enum PdfTrustSeverity { Info, Indeterminate, Invalid }

public sealed record PdfTrustFinding(PdfTrustSeverity Severity, string Code, string Message);

/// <summary>Where the time used for validity and revocation checks came from.</summary>
public enum PdfValidationTimeSource
{
    /// <summary>The caller fixed it in <see cref="PdfTrustPolicy.ValidationTime"/>.</summary>
    Policy,
    /// <summary>The generation time of a document timestamp or signature-time-stamp token whose CMS signature is intact and whose TSA chains to a configured trust anchor (proof the signature existed by then; the TSA's own revocation status is not checked).</summary>
    DocumentTimestamp,
    /// <summary>The signer-claimed signingTime attribute. It is not authenticated by a third party and is only used when the policy opts in.</summary>
    ClaimedSigningTime,
    /// <summary>The current time.</summary>
    Now,
}

public enum PdfRevocationRequirement
{
    /// <summary>Every non-anchor certificate needs authenticated revocation evidence covering the validation time, otherwise the result is Indeterminate.</summary>
    Require,
    /// <summary>Missing evidence is reported but does not prevent Valid; evidence that says "revoked" still makes the result Invalid.</summary>
    BestEffort,
    /// <summary>Revocation is not checked.</summary>
    Ignore,
}

/// <summary>Configuration of certificate path validation (RFC 5280) and offline revocation checking. Do not mutate a policy while an evaluation uses it.</summary>
public sealed class PdfTrustPolicy
{
    /// <summary>Roots (or other certificates) the caller trusts as path anchors.</summary>
    public X509Certificate2Collection TrustAnchors { get; init; } = [];
    /// <summary>Also accept chains that end at a root of the operating system store (used when no custom anchor matches). Off by default.</summary>
    public bool UseSystemStore { get; init; }
    /// <summary>Extra intermediates or responder certificates available for path building.</summary>
    public X509Certificate2Collection AdditionalCertificates { get; init; } = [];
    /// <summary>Caller-supplied validation data in addition to the embedded signature attribute and /DSS data.</summary>
    public PdfValidationData? SuppliedData { get; init; }
    /// <summary>Use revocation data embedded in the signature (adbe-revocationInfoArchival) and the /DSS. Default true.</summary>
    public bool UseEmbeddedValidationData { get; init; } = true;
    public PdfRevocationRequirement Revocation { get; init; } = PdfRevocationRequirement.Require;
    /// <summary>Fixed validation time. When null the time is a trusted document timestamp, else (if <see cref="UseClaimedSigningTime"/>) the claimed signing time, else now.</summary>
    public DateTimeOffset? ValidationTime { get; init; }
    /// <summary>Allow the unauthenticated, signer-claimed signingTime to select the validation time. Default false.</summary>
    public bool UseClaimedSigningTime { get; init; }
    /// <summary>Use the time of a trusted document timestamp that covers the signature. Default true.</summary>
    public bool UseDocumentTimestamps { get; init; } = true;
    /// <summary>When non-empty and the signer certificate has an extendedKeyUsage extension, one of these OIDs (or anyExtendedKeyUsage) must be present.</summary>
    public IReadOnlyList<string> RequiredExtendedKeyUsages { get; init; } = [];
    /// <summary>When non-empty and the signer certificate has a certificatePolicies extension, one of these policy OIDs (or anyPolicy) must be present; certificates without it fail.</summary>
    public IReadOnlyList<string> RequiredCertificatePolicies { get; init; } = [];
    /// <summary>Longest accepted path in certificates, including the signer and the anchor.</summary>
    public int MaxChainLength { get; init; } = 10;
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed record PdfTrustResult(
    PdfTrustStatus Status,
    DateTimeOffset ValidationTime,
    PdfValidationTimeSource ValidationTimeSource,
    IReadOnlyList<X509Certificate2> Chain,
    IReadOnlyList<PdfTrustFinding> Findings,
    IReadOnlyList<PdfRevocationStatus> Revocation)
{
    /// <summary>True only when <see cref="Status"/> is Valid, i.e. the policy was fully satisfied.</summary>
    public bool ChainTrusted => Status == PdfTrustStatus.Valid;
}

public sealed record PdfSignatureTrustResult(PdfSignatureInfo Signature, PdfTrustResult Trust);

/// <summary>X.509 path validation and offline revocation checking against a <see cref="PdfTrustPolicy"/>. No network access is ever performed.</summary>
public static class PdfCertificateValidator
{
    const string OcspSigning = "1.3.6.1.5.5.7.3.9";

    public static PdfTrustResult Validate(X509Certificate2 leaf, PdfTrustPolicy policy, DateTimeOffset time, PdfValidationData? data = null, PdfValidationTimeSource source = PdfValidationTimeSource.Policy)
    {
        var sources = new List<(string, PdfValidationData)>();
        if (data is not null) sources.Add(("Supplied", data));
        return Validate(leaf, policy, time, source, sources, true, true);
    }

    internal static PdfTrustResult Validate(X509Certificate2 leaf, PdfTrustPolicy policy, DateTimeOffset time, PdfValidationTimeSource tsrc,
        IReadOnlyList<(string Source, PdfValidationData Data)> sources, bool checkRevocation, bool leafRules)
    {
        var findings = new List<PdfTrustFinding>();
        void Add(PdfTrustSeverity s, string code, string msg) => findings.Add(new PdfTrustFinding(s, code, msg));
        var revocation = new List<PdfRevocationStatus>();
        var chainCerts = new List<X509Certificate2> { leaf };
        PdfTrustResult Done()
        {
            var st = findings.Any(f => f.Severity == PdfTrustSeverity.Invalid) ? PdfTrustStatus.Invalid
                : findings.Any(f => f.Severity == PdfTrustSeverity.Indeterminate) ? PdfTrustStatus.Indeterminate : PdfTrustStatus.Valid;
            return new PdfTrustResult(st, time, tsrc, chainCerts, findings, revocation);
        }

        if (policy.TrustAnchors.Count == 0 && !policy.UseSystemStore)
        {
            Add(PdfTrustSeverity.Indeterminate, "NoTrustAnchors", "No trust anchors are configured, so the signer certificate cannot be trusted.");
            return Done();
        }

        var pool = new X509Certificate2Collection();
        foreach (var ac in policy.AdditionalCertificates) pool.Add(ac);
        foreach (var (_, d) in sources) foreach (var c in d.Certificates) pool.Add(c);

        var elements = BuildChain(leaf, policy, time, pool, findings);
        if (elements is null) return Done();
        chainCerts = elements;
        if (elements.Count > policy.MaxChainLength) Add(PdfTrustSeverity.Invalid, "ChainTooLong", $"The certification path has {elements.Count} certificates; the policy allows {policy.MaxChainLength}.");

        if (leafRules) CheckLeaf(leaf, policy, findings);

        if (checkRevocation && policy.Revocation != PdfRevocationRequirement.Ignore)
            CheckRevocation(elements, policy, time, sources, pool, findings, revocation);

        return Done();
    }

    static List<X509Certificate2>? BuildChain(X509Certificate2 leaf, PdfTrustPolicy policy, DateTimeOffset time, X509Certificate2Collection pool, List<PdfTrustFinding> findings)
    {
        List<X509Certificate2>? best = null;
        List<PdfTrustFinding>? bestFindings = null;
        var modes = new List<bool>();
        if (policy.TrustAnchors.Count > 0) modes.Add(true);
        if (policy.UseSystemStore) modes.Add(false);
        foreach (var custom in modes)
        {
            var local = new List<PdfTrustFinding>();
            using var chain = new X509Chain();
            var p = chain.ChainPolicy;
            p.RevocationMode = X509RevocationMode.NoCheck;
            p.VerificationTime = time.UtcDateTime;
            p.DisableCertificateDownloads = true;
            p.VerificationFlags = X509VerificationFlags.NoFlag;
            if (custom)
            {
                p.TrustMode = X509ChainTrustMode.CustomRootTrust;
                p.CustomTrustStore.AddRange(policy.TrustAnchors.Cast<X509Certificate2>().ToArray());
            }
            p.ExtraStore.AddRange(pool.Cast<X509Certificate2>().ToArray());
            bool ok;
            try { ok = chain.Build(leaf); }
            catch (CryptographicException e) { local.Add(new PdfTrustFinding(PdfTrustSeverity.Invalid, "ChainBuildFailed", "The certification path could not be built: " + e.GetType().Name)); ok = false; }
            var certs = chain.ChainElements.Cast<X509ChainElement>().Select(e => X509CertificateLoader.LoadCertificate(e.Certificate.RawData)).ToList();
            if (certs.Count == 0) certs.Add(leaf);
            if (!ok)
                foreach (var s in Statuses(chain)) local.Add(s);
            if (ok) { findings.AddRange(local); return certs; }
            if (best is null || local.Count(f => f.Severity == PdfTrustSeverity.Invalid) < bestFindings!.Count(f => f.Severity == PdfTrustSeverity.Invalid)) { best = certs; bestFindings = local; }
        }
        findings.AddRange(bestFindings ?? []);
        return best ?? [leaf];
    }

    static IEnumerable<PdfTrustFinding> Statuses(X509Chain chain)
    {
        var flags = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (a, s) => a | s.Status);
        var seen = new HashSet<string>();
        foreach (var s in chain.ChainStatus)
        {
            if (!seen.Add(s.Status.ToString())) continue;
            var (sev, code) = s.Status switch
            {
                X509ChainStatusFlags.UntrustedRoot => (PdfTrustSeverity.Indeterminate, "UntrustedRoot"),
                X509ChainStatusFlags.PartialChain => (PdfTrustSeverity.Indeterminate, "PartialChain"),
                X509ChainStatusFlags.NotTimeValid => (PdfTrustSeverity.Invalid, "NotTimeValid"),
                X509ChainStatusFlags.NotTimeNested => (PdfTrustSeverity.Invalid, "NotTimeNested"),
                X509ChainStatusFlags.NotSignatureValid => (PdfTrustSeverity.Invalid, "CertificateSignatureInvalid"),
                X509ChainStatusFlags.InvalidBasicConstraints => (PdfTrustSeverity.Invalid, "InvalidBasicConstraints"),
                X509ChainStatusFlags.InvalidNameConstraints or X509ChainStatusFlags.HasNotSupportedNameConstraint or X509ChainStatusFlags.HasNotPermittedNameConstraint or X509ChainStatusFlags.HasExcludedNameConstraint
                    => (PdfTrustSeverity.Invalid, "NameConstraintsViolated"),
                X509ChainStatusFlags.InvalidPolicyConstraints or X509ChainStatusFlags.NoIssuanceChainPolicy => (PdfTrustSeverity.Invalid, "PolicyConstraintsViolated"),
                X509ChainStatusFlags.NotValidForUsage => (PdfTrustSeverity.Invalid, "KeyUsageViolated"),
                X509ChainStatusFlags.Cyclic => (PdfTrustSeverity.Invalid, "CyclicChain"),
                X509ChainStatusFlags.RevocationStatusUnknown or X509ChainStatusFlags.OfflineRevocation => (PdfTrustSeverity.Info, "PlatformRevocationIgnored"),
                _ => (PdfTrustSeverity.Invalid, s.Status.ToString()),
            };
            yield return new PdfTrustFinding(sev, code, s.StatusInformation?.Trim() is { Length: > 0 } m ? m : code);
        }
        _ = flags;
    }

    static void CheckLeaf(X509Certificate2 leaf, PdfTrustPolicy policy, List<PdfTrustFinding> findings)
    {
        foreach (var ext in leaf.Extensions)
            if (ext is X509KeyUsageExtension ku && (ku.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
                findings.Add(new PdfTrustFinding(PdfTrustSeverity.Invalid, "KeyUsage", "The signer certificate's keyUsage allows neither digitalSignature nor nonRepudiation."));
        if (policy.RequiredExtendedKeyUsages.Count > 0 && leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault() is { } eku)
        {
            var have = eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value).ToHashSet();
            if (!have.Contains("2.5.29.37.0") && !policy.RequiredExtendedKeyUsages.Any(have.Contains))
                findings.Add(new PdfTrustFinding(PdfTrustSeverity.Invalid, "ExtendedKeyUsage", "The signer certificate's extendedKeyUsage contains none of the required purposes."));
        }
        if (policy.RequiredCertificatePolicies.Count > 0)
        {
            var have = CertificatePolicies(leaf);
            if (have is null || !(have.Contains("2.5.29.32.0") || policy.RequiredCertificatePolicies.Any(have.Contains)))
                findings.Add(new PdfTrustFinding(PdfTrustSeverity.Invalid, "CertificatePolicy", "The signer certificate does not assert a required certificate policy."));
        }
    }

    static HashSet<string>? CertificatePolicies(X509Certificate2 c)
    {
        var ext = c.Extensions["2.5.29.32"];
        if (ext is null) return null;
        try
        {
            var set = new HashSet<string>();
            var seq = new AsnReader(ext.RawData, AsnEncodingRules.DER).ReadSequence();
            while (seq.HasData) set.Add(seq.ReadSequence().ReadObjectIdentifier());
            return set;
        }
        catch (AsnContentException) { return null; }
    }

    static void CheckRevocation(List<X509Certificate2> chain, PdfTrustPolicy policy, DateTimeOffset time, IReadOnlyList<(string Source, PdfValidationData Data)> sources,
        X509Certificate2Collection pool, List<PdfTrustFinding> findings, List<PdfRevocationStatus> results)
    {
        var crls = new List<(string, PdfRevocationParser.Crl)>();
        var ocsps = new List<(string, PdfRevocationParser.Ocsp)>();
        var skipped = new List<string>();
        foreach (var (src, d) in sources)
        {
            foreach (var c in d.Crls) if (PdfRevocationParser.ParseCrl(c) is { } p) crls.Add((src, p)); else skipped.Add($"A CRL from {src} could not be parsed");
            foreach (var o in d.OcspResponses) if (PdfRevocationParser.ParseOcsp(o) is { } p) ocsps.Add((src, p)); else skipped.Add($"An OCSP response from {src} was unsuccessful or could not be parsed");
        }
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i + 1 < chain.Count; i++)
        {
            var cert = chain[i];
            var issuer = chain[i + 1];
            var subject = cert.Subject;
            var notes = new List<string>();
            PdfRevocationStatus? good = null, revoked = null;
            var skew = policy.ClockSkew;

            bool Covers(DateTimeOffset thisUpdate, DateTimeOffset? next)
                => thisUpdate <= now + skew && (thisUpdate >= time - skew || (thisUpdate <= time + skew && (next is null || next >= time - skew)));

            var serial = PdfRevocationParser.SerialKey(cert.SerialNumberBytes.Span);
            foreach (var (src, o) in ocsps)
            {
                foreach (var r in o.Responses)
                {
                    if (!MatchesCert(r, cert, issuer, serial)) continue;
                    if (!OcspSignerOk(o, issuer, pool, notes, src)) continue;
                    if (!Covers(r.ThisUpdate, r.NextUpdate)) { notes.Add($"OCSP response from {src} (thisUpdate {r.ThisUpdate:u}) does not cover the validation time"); continue; }
                    switch (r.Status)
                    {
                        case 0: good ??= new PdfRevocationStatus(subject, PdfRevocationOutcome.Good, "OCSP/" + src, r.ThisUpdate, null, "OCSP reports good"); break;
                        case 1 when r.RevokedAt is { } at && at <= time + skew:
                            revoked ??= new PdfRevocationStatus(subject, PdfRevocationOutcome.Revoked, "OCSP/" + src, r.ThisUpdate, at, $"Revoked at {at:u}"); break;
                        case 1: good ??= new PdfRevocationStatus(subject, PdfRevocationOutcome.Good, "OCSP/" + src, r.ThisUpdate, null, $"Revoked only after the validation time ({r.RevokedAt:u})"); break;
                        default: notes.Add($"OCSP response from {src} answered 'unknown'"); break;
                    }
                }
            }
            foreach (var (src, c) in crls)
            {
                if (!c.IssuerDer.AsSpan().SequenceEqual(cert.IssuerName.RawData)) continue;
                if (c.OutOfScope is { } oos) { notes.Add($"CRL from {src} ignored: {oos}"); continue; }
                if (issuer.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } ku && (ku.KeyUsages & X509KeyUsageFlags.CrlSign) == 0) { notes.Add("The issuer's keyUsage does not allow cRLSign"); continue; }
                if (!PdfRevocationParser.Verify(issuer, c.Tbs, c.SigOid, c.SigParams, c.Signature, out var err)) { notes.Add($"CRL from {src}: signature not verified" + (err.Length > 0 ? " (" + err + ")" : "")); continue; }
                if (!Covers(c.ThisUpdate, c.NextUpdate)) { notes.Add($"CRL from {src} (thisUpdate {c.ThisUpdate:u}) does not cover the validation time"); continue; }
                if (c.Revoked.TryGetValue(serial, out var e) && e.Reason != 8)
                {
                    if (e.Date <= time + skew) revoked ??= new PdfRevocationStatus(subject, PdfRevocationOutcome.Revoked, "CRL/" + src, c.ThisUpdate, e.Date, $"Revoked at {e.Date:u}");
                    else good ??= new PdfRevocationStatus(subject, PdfRevocationOutcome.Good, "CRL/" + src, c.ThisUpdate, null, $"Revoked only after the validation time ({e.Date:u})");
                }
                else good ??= new PdfRevocationStatus(subject, PdfRevocationOutcome.Good, "CRL/" + src, c.ThisUpdate, null, "Not listed in the CRL");
            }

            if (revoked is not null)
            {
                results.Add(revoked);
                findings.Add(new PdfTrustFinding(PdfTrustSeverity.Invalid, "Revoked", $"{subject}: {revoked.Detail}."));
            }
            else if (good is not null) results.Add(good);
            else
            {
                var detail = notes.Count > 0 ? string.Join("; ", notes.Distinct()) : "No OCSP response or CRL for this certificate was available";
                results.Add(new PdfRevocationStatus(subject, PdfRevocationOutcome.Indeterminate, "", null, null, detail));
                findings.Add(new PdfTrustFinding(policy.Revocation == PdfRevocationRequirement.Require ? PdfTrustSeverity.Indeterminate : PdfTrustSeverity.Info,
                    "RevocationUnknown", $"{subject}: revocation status could not be established ({detail})."));
            }
        }
        if (chain.Count > 0) results.Add(new PdfRevocationStatus(chain[^1].Subject, PdfRevocationOutcome.NotChecked, "", null, null, "Trust anchor"));
        if (skipped.Count > 0) findings.Add(new PdfTrustFinding(PdfTrustSeverity.Info, "UnusableRevocationData", string.Join("; ", skipped.Distinct())));
    }

    static bool MatchesCert(PdfRevocationParser.SingleResponse r, X509Certificate2 cert, X509Certificate2 issuer, string serial)
    {
        if (PdfRevocationParser.SerialKey(r.Serial) != serial) return false;
        var h = PdfRevocationParser.HashFromOid(r.HashOid);
        if (h is null) return false;
        var nameHash = HashData(h.Value, issuer.SubjectName.RawData);
        var keyHash = HashData(h.Value, issuer.PublicKey.EncodedKeyValue.RawData);
        return nameHash.AsSpan().SequenceEqual(r.NameHash) && keyHash.AsSpan().SequenceEqual(r.KeyHash);
    }

    static byte[] HashData(HashAlgorithmName h, byte[] d) => h.Name switch { "SHA1" => SHA1.HashData(d), "SHA384" => SHA384.HashData(d), "SHA512" => SHA512.HashData(d), _ => SHA256.HashData(d) };

    static bool OcspSignerOk(PdfRevocationParser.Ocsp o, X509Certificate2 issuer, X509Certificate2Collection pool, List<string> notes, string src)
    {
        if (PdfRevocationParser.Verify(issuer, o.Tbs, o.SigOid, o.SigParams, o.Signature, out var err0)) return true;
        foreach (var cand in o.Certs.Concat(pool.Cast<X509Certificate2>()))
        {
            if (!PdfRevocationParser.IsIssuedBy(cand, issuer)) continue;
            if (cand.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault() is not { } eku || !eku.EnhancedKeyUsages.Cast<Oid>().Any(x => x.Value == OcspSigning)) continue;
            if (o.ProducedAt.UtcDateTime < cand.NotBefore.ToUniversalTime() || o.ProducedAt.UtcDateTime > cand.NotAfter.ToUniversalTime()) continue;
            var idOk = o.ResponderName is not null ? o.ResponderName.AsSpan().SequenceEqual(cand.SubjectName.RawData)
                : o.ResponderKeyHash is not null && SHA1.HashData(cand.PublicKey.EncodedKeyValue.RawData).AsSpan().SequenceEqual(o.ResponderKeyHash);
            if (idOk && PdfRevocationParser.Verify(cand, o.Tbs, o.SigOid, o.SigParams, o.Signature, out _)) return true;
        }
        notes.Add($"OCSP response from {src}: signature not verified by the issuer or an authorized responder" + (err0.Length > 0 ? " (" + err0 + ")" : ""));
        return false;
    }
}
