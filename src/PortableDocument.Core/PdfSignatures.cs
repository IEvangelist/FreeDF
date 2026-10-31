using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PortableDocument.Core;

/// <summary>Result of checking one signature field (ISO 32000-2 §12.8).</summary>
public sealed record PdfSignatureInfo
{
    public string FieldName { get; init; } = "";
    public string? SubFilter { get; init; }
    /// <summary>Common name (or full subject) of the signing certificate.</summary>
    public string? SignerName { get; init; }
    public X509Certificate2? SignerCertificate { get; init; }
    public DateTimeOffset? SigningTime { get; init; }
    public string? Reason { get; init; }
    public string? Location { get; init; }
    public IReadOnlyList<long> ByteRange { get; init; } = [];
    /// <summary>The /ByteRange is well formed and brackets exactly the /Contents string.</summary>
    public bool ByteRangeValid { get; init; }
    /// <summary>The CMS messageDigest equals the digest of the bytes selected by /ByteRange.</summary>
    public bool DigestValid { get; init; }
    /// <summary>The CMS signature verifies against the signer certificate (implies <see cref="DigestValid"/>).</summary>
    public bool SignatureValid { get; init; }
    /// <summary>The signed byte ranges extend to the end of the file.</summary>
    public bool CoversWholeDocument { get; init; }
    /// <summary>Bytes (an incremental update) follow the signed range: the file changed after this signature was applied.</summary>
    public bool ModifiedAfterSigning => !CoversWholeDocument;
    /// <summary>The signer certificate chains to a trusted root in the machine store (self-signed test certificates report false).</summary>
    public bool ChainTrusted { get; init; }
    public string? Error { get; init; }
    /// <summary>Integrity of the signed bytes and CMS signature only; says nothing about trust.</summary>
    public bool IsIntact => ByteRangeValid && SignatureValid;
    /// <summary>The signature value is a document timestamp (/Type /DocTimeStamp, SubFilter ETSI.RFC3161, §12.8.5) rather than a signature.</summary>
    public bool IsDocumentTimeStamp { get; init; }
    /// <summary>Generation time of the RFC 3161 TSTInfo for a document timestamp. It is the TSA's claim; the TSA certificate chain is not validated for trust here.</summary>
    public DateTimeOffset? TimeStampTime { get; init; }
    /// <summary>The signature carries a DocMDP transform and is referenced from the catalog /Perms /DocMDP: a certification signature (§12.8.2.2).</summary>
    public bool IsCertification => DocMdpStatus != PdfDocMdpStatus.NotCertification;
    /// <summary>The /P value of the DocMDP transform parameters, when the signature declares one (even if it is not effective).</summary>
    public PdfDocMdpPermission? DocMdpPermission { get; init; }
    public PdfDocMdpStatus DocMdpStatus { get; init; }
    /// <summary>Structural problems with the DocMDP declaration and, for modified files, the changes after the certified revision that /P does not permit.</summary>
    public IReadOnlyList<string> DocMdpFindings { get; init; } = [];
    /// <summary>The document security store (§12.8.4.3) has a /VRI entry keyed by this signature's /Contents.</summary>
    public bool HasValidationInformation { get; init; }
    /// <summary>The DER CMS SignedData of /Contents (zero padding removed); null when /Contents could not be read.</summary>
    public byte[]? Cms { get; init; }
    /// <summary>
    /// For a CMS signature that carries an ESS signing-certificate-v2 attribute (RFC 5035, required by PAdES/CAdES baseline): whether its first ESSCertIDv2 hash
    /// matches the signer certificate. Null when the attribute is absent.
    /// </summary>
    public bool? SigningCertificateV2Valid { get; init; }
    /// <summary>The field-lock action (/All, /Include or /Exclude) declared by a FieldMDP signature reference or the field's /Lock dictionary (§12.8.2.4); null when the signature locks nothing.</summary>
    public string? FieldLockAction { get; init; }
    /// <summary>Form fields locked by this signature whose value (/V) differs between the signed revision and the current file.</summary>
    public IReadOnlyList<string> FieldMdpFindings { get; init; } = [];
}

/// <summary>Counts of validation material in a /VRI entry (§12.8.4.3).</summary>
public sealed record PdfValidationRelatedInfo(string Key, int Certificates, int Crls, int Ocsps, string? UpdateTime);

/// <summary>Read-only view of the document security store (/DSS, §12.8.4.3). Contents are not validated or used for trust decisions.</summary>
public sealed record PdfDocumentSecurityStore(int Certificates, int Crls, int Ocsps, IReadOnlyList<PdfValidationRelatedInfo> Vri);

/// <summary>Options for <see cref="PdfSignatures.ComputeCms(byte[], long[], X509Certificate2, DateTimeOffset, HashAlgorithmName, PdfCmsOptions)"/>.</summary>
public sealed class PdfCmsOptions
{
    /// <summary>Sign RSA keys with RSASSA-PSS (RFC 4056, salt length = digest length) instead of RSASSA-PKCS1-v1_5. ECDSA keys ignore this.</summary>
    public bool RsaPss { get; init; }
    /// <summary>Add the CMS signingTime attribute (PAdES forbids it; the signature dictionary /M is used instead).</summary>
    public bool IncludeSigningTime { get; init; } = true;
    /// <summary>Add the ESS signing-certificate-v2 attribute (RFC 5035) binding the signer certificate by digest.</summary>
    public bool SigningCertificateV2 { get; init; }
    /// <summary>Embeds CRLs and OCSP responses as a signed adbe-revocationInfoArchival attribute.</summary>
    public PdfValidationData? RevocationInfo { get; init; }
    /// <summary>Certificates (intermediates, usually) to place in the CMS in addition to those found by building the signer's chain in the machine store.</summary>
    public IReadOnlyList<X509Certificate2> ExtraCertificates { get; init; } = [];
    /// <summary>Adds an RFC 3161 token over the signature value as a signature-time-stamp unsigned attribute (CAdES-T). The token must fit in the signature's /Contents.</summary>
    public Func<HashAlgorithmName, byte[], byte[]>? SignatureTimestamp { get; init; }
}

/// <summary>Detached CMS (adbe.pkcs7.detached) signature creation helpers and verification, using System.Security.Cryptography.Pkcs.</summary>
public static class PdfSignatures
{
    /// <summary>Digest of the byte ranges followed by a CMS/PKCS#7 detached signature over them.</summary>
    public static byte[] ComputeCms(byte[] pdf, long[] byteRange, X509Certificate2 cert, DateTimeOffset signingTime, HashAlgorithmName hash)
        => ComputeCms(pdf, byteRange, cert, signingTime, hash, new PdfCmsOptions());

    /// <summary>Detached CMS with optional RSASSA-PSS, ESS signing-certificate-v2 (PAdES/CAdES) and adbe-revocationInfoArchival attributes.</summary>
    public static byte[] ComputeCms(byte[] pdf, long[] byteRange, X509Certificate2 cert, DateTimeOffset signingTime, HashAlgorithmName hash, PdfCmsOptions o)
    {
        if (hash.Name is not ("SHA256" or "SHA384" or "SHA512")) throw new ArgumentException("Unsupported digest algorithm; use SHA-256, SHA-384 or SHA-512.", nameof(hash));
        var content = Concat(pdf, byteRange);
        var cms = new SignedCms(new ContentInfo(content), true);
        var digestOid = hash.Name switch { "SHA384" => "2.16.840.1.101.3.4.2.2", "SHA512" => "2.16.840.1.101.3.4.2.3", _ => "2.16.840.1.101.3.4.2.1" };
        var signer = o.RsaPss
            ? new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert, null, RSASignaturePadding.Pss)
            : new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert);
        signer.DigestAlgorithm = new Oid(digestOid);
        signer.IncludeOption = X509IncludeOption.WholeChain;
        foreach (var extra in o.ExtraCertificates) signer.Certificates.Add(extra);
        if (o.IncludeSigningTime) signer.SignedAttributes.Add(new Pkcs9SigningTime(signingTime.UtcDateTime));
        if (o.SigningCertificateV2) signer.SignedAttributes.Add(new AsnEncodedData(new Oid(SigningCertificateV2Oid), SigningCertificateV2(cert, digestOid, o.ExtraCertificates)));
        if (o.RevocationInfo is { IsEmpty: false } ri && (ri.Crls.Count > 0 || ri.OcspResponses.Count > 0))
            signer.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.840.113583.1.1.8"), RevocationInfoArchival(ri)));
        cms.ComputeSignature(signer, silent: true);
        if (o.SignatureTimestamp is { } stamp)
        {
            var si = cms.SignerInfos[0];
            var token = stamp(HashAlgorithmName.SHA256, SHA256.HashData(si.GetSignature()));
            si.AddUnsignedAttribute(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.14"), token));
        }
        return cms.Encode();
    }

    static byte[] SigningCertificateV2(X509Certificate2 cert, string hashOid, IEnumerable<X509Certificate2> chain)
    {
        var w = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (w.PushSequence())
        using (w.PushSequence())
        {
            using (w.PushSequence())
            {
                if (hashOid != "2.16.840.1.101.3.4.2.1") { using (w.PushSequence()) w.WriteObjectIdentifier(hashOid); }
                w.WriteOctetString(HashOf(hashOid, cert.RawData)!);
                using (w.PushSequence())
                {
                    using (w.PushSequence())
                    using (w.PushSequence(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 4)))
                        w.WriteEncodedValue(cert.IssuerName.RawData);
                    w.WriteIntegerUnsigned(cert.SerialNumberBytes.Span);
                }
            }
            // CA certificates the signer supplies are bound by digest too, so a verifier can treat them as covered by the signature's timestamp.
            foreach (var c in chain)
            {
                if (c.RawData.AsSpan().SequenceEqual(cert.RawData)) continue;
                using (w.PushSequence())
                {
                    if (hashOid != "2.16.840.1.101.3.4.2.1") { using (w.PushSequence()) w.WriteObjectIdentifier(hashOid); }
                    w.WriteOctetString(HashOf(hashOid, c.RawData)!);
                }
            }
        }
        return w.Encode();
    }

    static byte[] RevocationInfoArchival(PdfValidationData d)
    {
        var w = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            void Part(int tag, List<byte[]> items)
            {
                if (items.Count == 0) return;
                using (w.PushSequence(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, tag)))
                using (w.PushSequence())
                    foreach (var i in items) w.WriteEncodedValue(i);
            }
            Part(0, d.Crls);
            Part(1, d.OcspResponses);
        }
        return w.Encode();
    }

    static byte[] Concat(byte[] pdf, long[] r)
    {
        var o = new byte[r[1] + r[3]];
        Buffer.BlockCopy(pdf, (int)r[0], o, 0, (int)r[1]);
        Buffer.BlockCopy(pdf, (int)r[2], o, (int)r[1], (int)r[3]);
        return o;
    }

    /// <summary>Verifies every signature field in <paramref name="pdf"/>.</summary>
    public static IReadOnlyList<PdfSignatureInfo> Verify(byte[] pdf, string password = "")
    {
        var f = PdfFile.Load(pdf, password);
        return Verify(f);
    }

    public static IReadOnlyList<PdfSignatureInfo> Verify(PdfFile f)
    {
        var found = new List<(string Name, PdfDictionary V, int VNum, PdfDictionary Field)>();
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary form) return [];
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Walk(PdfObject? node, string prefix, string? inheritedFt)
        {
            if (f.Resolve(node) is not PdfDictionary d || !seen.Add(d)) return;
            var name = (f.Resolve(d.Get("T")) as PdfString)?.Text;
            var full = name is null ? prefix : prefix.Length == 0 ? name : prefix + "." + name;
            var ft = f.Resolve(d.Get("FT")).AsName() ?? inheritedFt;
            if (f.Resolve(d.Get("Kids")) is PdfArray kids) foreach (var k in kids.Items) Walk(k, full, ft);
            if (ft == "Sig" && f.Resolve(d.Get("V")) is PdfDictionary v && v.Get("ByteRange") is not null)
                found.Add((full, v, (d.Get("V") as PdfRef)?.Number ?? -1, d));
        }
        if (f.Resolve(form.Get("Fields")) is PdfArray fields) foreach (var x in fields.Items) Walk(x, "", null);

        var infos = found.Select(s => Check(f, s.Name, s.V)).ToList();
        var dss = ReadDss(f);
        var permsDocMdp = (f.Resolve(f.Catalog.Get("Perms")) as PdfDictionary)?.Get("DocMDP") as PdfRef;
        var ends = infos.Select(i => i.ByteRangeValid ? i.ByteRange[2] + i.ByteRange[3] : long.MaxValue).ToList();
        var docMdpCount = found.Count(s => DocMdpReferences(f, s.V).Count > 0);
        var result = new List<PdfSignatureInfo>();
        for (var i = 0; i < infos.Count; i++)
            result.Add(EnrichFieldMdp(f, EnrichDocMdp(f, infos[i], found[i].V, found[i].VNum, permsDocMdp?.Number, ends[i], ends, docMdpCount, dss), found[i].Field, found[i].V, ends[i]));
        return result;
    }

    static PdfSignatureInfo EnrichFieldMdp(PdfFile f, PdfSignatureInfo info, PdfDictionary field, PdfDictionary v, long end)
    {
        string? action = null;
        PdfArray? names = null;
        var refs = f.Resolve(v.Get("Reference")) is PdfArray ra ? ra.Items.Select(r => f.Resolve(r)).OfType<PdfDictionary>().Where(d => f.Resolve(d.Get("TransformMethod")).AsName() == "FieldMDP").ToList() : [];
        PdfDictionary? src = refs.Count > 0 ? f.Resolve(refs[0].Get("TransformParams")) as PdfDictionary : f.Resolve(field.Get("Lock")) as PdfDictionary;
        if (src is null) return info;
        action = f.Resolve(src.Get("Action")).AsName();
        names = f.Resolve(src.Get("Fields")) as PdfArray;
        if (action is not ("All" or "Include" or "Exclude")) return info with { FieldLockAction = action, FieldMdpFindings = [$"Unknown field lock action /{action}"] };
        var listed = names?.Items.Select(n => (f.Resolve(n) as PdfString)?.Text).OfType<string>().ToList() ?? [];
        bool Locked(string name)
        {
            var hit = listed.Any(l => name == l || name.StartsWith(l + ".", StringComparison.Ordinal));
            return action == "All" || (action == "Include" ? hit : !hit);
        }
        var findings = new List<string>();
        if (!info.ByteRangeValid || info.CoversWholeDocument) return info with { FieldLockAction = action };
        PdfFile old;
        try { old = f.LoadRevision(end); }
        catch (Exception e) when (e is PdfParseException or ArgumentException or InvalidOperationException) { return info with { FieldLockAction = action, FieldMdpFindings = ["The signed revision could not be re-read, so locked fields were not compared"] }; }
        var before = FieldValues(old);
        var after = FieldValues(f);
        foreach (var (name, value) in before)
        {
            if (!Locked(name)) continue;
            if (!after.TryGetValue(name, out var now)) findings.Add($"Locked field {name} was removed");
            else if (!PdfObjectEquality.DeepEquals(value, now)) findings.Add($"Locked field {name} changed its value");
        }
        foreach (var name in after.Keys)
            if (!before.ContainsKey(name) && Locked(name)) findings.Add($"A field named {name}, covered by the lock, was added");
        return info with { FieldLockAction = action, FieldMdpFindings = findings };
    }

    static Dictionary<string, PdfObject?> FieldValues(PdfFile f)
    {
        var map = new Dictionary<string, PdfObject?>(StringComparer.Ordinal);
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary form) return map;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Walk(PdfObject? node, string prefix, int depth)
        {
            if (depth > 64 || map.Count > 100_000 || f.Resolve(node) is not PdfDictionary d || !seen.Add(d)) return;
            var name = (f.Resolve(d.Get("T")) as PdfString)?.Text;
            var full = name is null ? prefix : prefix.Length == 0 ? name : prefix + "." + name;
            if (name is not null) map[full] = f.Resolve(d.Get("V"));
            if (f.Resolve(d.Get("Kids")) is PdfArray kids) foreach (var k in kids.Items) Walk(k, full, depth + 1);
        }
        if (f.Resolve(form.Get("Fields")) is PdfArray fields) foreach (var x in fields.Items) Walk(x, "", 0);
        return map;
    }

    static List<PdfDictionary> DocMdpReferences(PdfFile f, PdfDictionary v)
        => f.Resolve(v.Get("Reference")) is PdfArray refs
            ? refs.Items.Select(r => f.Resolve(r)).OfType<PdfDictionary>().Where(d => f.Resolve(d.Get("TransformMethod")).AsName() == "DocMDP").ToList()
            : [];

    static PdfSignatureInfo EnrichDocMdp(PdfFile f, PdfSignatureInfo info, PdfDictionary v, int vNum, int? permsNum, long end, List<long> ends, int docMdpCount, PdfDocumentSecurityStore? dss)
    {
        if (dss is not null && f.Resolve(v.Get("Contents")) is PdfString contents)
        {
            var keys = new[] { Convert.ToHexString(SHA1.HashData(contents.Bytes)), Convert.ToHexString(SHA1.HashData(TrimDer(contents.Bytes))) };
            info = info with { HasValidationInformation = dss.Vri.Any(x => keys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)) };
        }
        var refs = DocMdpReferences(f, v);
        if (refs.Count == 0) return info;
        var findings = new List<string>();
        if (refs.Count > 1) findings.Add("The signature has more than one DocMDP signature reference dictionary");
        PdfDocMdpPermission? perm = null;
        var tp = f.Resolve(refs[0].Get("TransformParams")) as PdfDictionary;
        var p = tp is null ? 2 : (int)(f.Resolve(tp.Get("P")).AsNumber() ?? 2);
        if (p is >= 1 and <= 3) perm = (PdfDocMdpPermission)p; else findings.Add($"Invalid DocMDP /P value {p}");
        info = info with { DocMdpPermission = perm };
        if (vNum < 0 || permsNum != vNum)
        {
            findings.Add("The signature is not referenced from the catalog /Perms /DocMDP entry, so it is not an effective certification");
            return info with { DocMdpFindings = findings };
        }
        if (docMdpCount > 1) findings.Add("The document has more than one signature with a DocMDP transform");
        if (ends.Any(e => e < end)) findings.Add("The DocMDP signature is not the first signature in the document");
        if (perm is null) return info with { DocMdpStatus = PdfDocMdpStatus.Undetermined, DocMdpFindings = findings };
        if (!info.ByteRangeValid) return info with { DocMdpStatus = PdfDocMdpStatus.Undetermined, DocMdpFindings = findings };
        if (info.CoversWholeDocument) return info with { DocMdpStatus = PdfDocMdpStatus.Unmodified, DocMdpFindings = findings };
        var changes = PdfDocMdpChanges.Find(f.Data, end, perm.Value, f, out var undetermined);
        findings.AddRange(changes);
        var status = undetermined ? PdfDocMdpStatus.Undetermined : changes.Count > 0 ? PdfDocMdpStatus.Violated : PdfDocMdpStatus.ChangesPermitted;
        return info with { DocMdpStatus = status, DocMdpFindings = findings };
    }

    /// <summary>Reads the document security store (/DSS) of the catalog, or null when there is none.</summary>
    public static PdfDocumentSecurityStore? ReadDss(PdfFile f)
    {
        if (f.Resolve(f.Catalog.Get("DSS")) is not PdfDictionary dss) return null;
        int Count(PdfDictionary d, string key) => f.Resolve(d.Get(key)) is PdfArray a ? a.Count : 0;
        var vri = new List<PdfValidationRelatedInfo>();
        if (f.Resolve(dss.Get("VRI")) is PdfDictionary vd)
            foreach (var (key, val) in vd.Entries)
                if (f.Resolve(val) is PdfDictionary e)
                    vri.Add(new PdfValidationRelatedInfo(key, Count(e, "Cert"), Count(e, "CRL"), Count(e, "OCSP"), (f.Resolve(e.Get("TU")) as PdfString)?.Text));
        return new PdfDocumentSecurityStore(Count(dss, "Certs"), Count(dss, "CRLs"), Count(dss, "OCSPs"), vri);
    }

    public static PdfDocumentSecurityStore? ReadDss(byte[] pdf) => ReadDss(PdfFile.Load(pdf));

    static PdfSignatureInfo Check(PdfFile f, string field, PdfDictionary v)
    {
        string? Text(string k) => (f.Resolve(v.Get(k)) as PdfString)?.Text;
        var br = (f.Resolve(v.Get("ByteRange")) as PdfArray)?.Items.Select(i => (long)(f.Resolve(i).AsNumber() ?? -1)).ToArray() ?? [];
        var info = new PdfSignatureInfo { FieldName = field, SubFilter = f.Resolve(v.Get("SubFilter")).AsName(), Reason = Text("Reason"), Location = Text("Location"), ByteRange = br, IsDocumentTimeStamp = f.Resolve(v.Get("Type")).AsName() == "DocTimeStamp" };
        var data = f.Data;
        if (br.Length != 4 || br.Any(x => x < 0) || br[0] != 0 || br[1] <= 0 || br[2] < br[1] || br[2] + br[3] > data.Length
            || data[br[1]] != (byte)'<' || data[br[2] - 1] != (byte)'>')
            return info with { Error = "Malformed /ByteRange" };
        var end = data.Length;
        while (end > 0 && data[end - 1] is (byte)'\n' or (byte)'\r' or (byte)' ') end--;
        var covers = br[2] + br[3] >= end;
        info = info with { ByteRangeValid = true, CoversWholeDocument = covers };
        if (f.Resolve(v.Get("Contents")) is not PdfString contents) return info with { Error = "Missing /Contents" };
        info = info with { Cms = TrimDer(contents.Bytes) };
        if (info.SubFilter == "ETSI.RFC3161" || info.IsDocumentTimeStamp) return CheckTimeStamp(info, contents, data, br);
        if (info.SubFilter is not ("adbe.pkcs7.detached" or "ETSI.CAdES.detached"))
            return info with { Error = $"Unsupported /SubFilter {info.SubFilter}" };
        try
        {
            var cms = new SignedCms(new ContentInfo(Concat(data, br)), true);
            cms.Decode(TrimDer(contents.Bytes));
            if (cms.SignerInfos.Count == 0) return info with { Error = "No SignerInfo" };
            var si = cms.SignerInfos[0];
            var cert = si.Certificate;
            DateTimeOffset? when = null;
            foreach (var a in si.SignedAttributes)
                if (a.Oid?.Value == "1.2.840.113549.1.9.5")
                    when = new Pkcs9SigningTime(a.Values[0].RawData).SigningTime;
            info = info with
            {
                SignerCertificate = cert,
                SignerName = cert?.GetNameInfo(X509NameType.SimpleName, false),
                SigningTime = when,
            };
            // The signature covers the signed attributes, which contain the digest; check the digest on its own for a precise report.
            var digestOk = false;
            foreach (var a in si.SignedAttributes)
                if (a.Oid?.Value == "1.2.840.113549.1.9.4")
                {
                    var md = new AsnReaderOctet(a.Values[0].RawData).Value;
                    var h = si.DigestAlgorithm.Value switch
                    {
                        "1.3.14.3.2.26" => SHA1.HashData(Concat(data, br)),
                        "2.16.840.1.101.3.4.2.1" => SHA256.HashData(Concat(data, br)),
                        "2.16.840.1.101.3.4.2.2" => SHA384.HashData(Concat(data, br)),
                        "2.16.840.1.101.3.4.2.3" => SHA512.HashData(Concat(data, br)),
                        _ => null,
                    };
                    digestOk = h is not null && h.AsSpan().SequenceEqual(md);
                }
            info = info with { DigestValid = digestOk, SigningCertificateV2Valid = cert is null ? null : EssCertificateMatches(si, cert) };
            try { si.CheckSignature(true); info = info with { SignatureValid = digestOk }; }
            catch (CryptographicException e) { info = info with { Error = e.Message }; }
            if (cert is not null)
            {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
                chain.ChainPolicy.ExtraStore.AddRange(cms.Certificates);
                info = info with { ChainTrusted = chain.Build(cert) };
            }
            return info;
        }
        catch (CryptographicException e) { return info with { Error = e.Message }; }
    }

    internal const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    static bool? EssCertificateMatches(SignerInfo si, X509Certificate2 cert)
    {
        foreach (var a in si.SignedAttributes)
        {
            if (a.Oid?.Value != SigningCertificateV2Oid || a.Values.Count == 0) continue;
            try
            {
                var certs = new System.Formats.Asn1.AsnReader(a.Values[0].RawData, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence().ReadSequence();
                var id = certs.ReadSequence();
                var oid = "2.16.840.1.101.3.4.2.1";
                if (id.PeekTag().TagValue == 16) oid = id.ReadSequence().ReadObjectIdentifier();
                var hash = id.ReadOctetString();
                var expected = HashOf(oid, cert.RawData);
                return expected is not null && expected.AsSpan().SequenceEqual(hash);
            }
            catch (System.Formats.Asn1.AsnContentException) { return false; }
        }
        return null;
    }

    /// <summary>Reads every certificate, CRL and OCSP response of the /DSS (global arrays and all /VRI entries). Unreadable or oversized entries are skipped.</summary>
    public static PdfValidationData ReadDssData(PdfFile f)
    {
        var data = new PdfValidationData();
        if (f.Resolve(f.Catalog.Get("DSS")) is not PdfDictionary dss) return data;
        var total = 0L;
        IEnumerable<byte[]> Blobs(PdfDictionary d, string key)
        {
            if (f.Resolve(d.Get(key)) is not PdfArray arr) yield break;
            foreach (var item in arr.Items.Take(1024))
            {
                if (f.Resolve(item) is not PdfStream s) continue;
                byte[] b;
                try { b = f.DecodeStream(s); } catch (Exception) { continue; }
                if (b.Length == 0 || b.Length > PdfRevocationParser.MaxBlobBytes || (total += b.Length) > 4 * PdfRevocationParser.MaxBlobBytes) continue;
                yield return b;
            }
        }
        void Read(PdfDictionary d, string certs, string crls, string ocsps)
        {
            var part = new PdfValidationData();
            foreach (var b in Blobs(d, certs)) { try { part.Certificates.Add(X509CertificateLoader.LoadCertificate(b)); } catch (CryptographicException) { } }
            part.Crls.AddRange(Blobs(d, crls));
            part.OcspResponses.AddRange(Blobs(d, ocsps));
            data.Merge(part);
        }
        Read(dss, "Certs", "CRLs", "OCSPs");
        if (f.Resolve(dss.Get("VRI")) is PdfDictionary vri)
            foreach (var e in vri.Entries.Values.Take(1024))
                if (f.Resolve(e) is PdfDictionary vd) Read(vd, "Cert", "CRL", "OCSP");
        return data;
    }

    static byte[]? HashOf(string? oid, byte[] data) => oid switch
    {
        "1.3.14.3.2.26" => SHA1.HashData(data),
        "2.16.840.1.101.3.4.2.1" => SHA256.HashData(data),
        "2.16.840.1.101.3.4.2.2" => SHA384.HashData(data),
        "2.16.840.1.101.3.4.2.3" => SHA512.HashData(data),
        _ => null,
    };

    /// <summary>Document timestamp (§12.8.5): an RFC 3161 TimeStampToken whose TSTInfo messageImprint is the digest of the ByteRange bytes.</summary>
    static PdfSignatureInfo CheckTimeStamp(PdfSignatureInfo info, PdfString contents, byte[] data, long[] br)
    {
        const string TstInfo = "1.2.840.113549.1.9.16.1.4";
        try
        {
            var cms = new SignedCms();
            cms.Decode(TrimDer(contents.Bytes));
            if (cms.ContentInfo.ContentType.Value != TstInfo) return info with { Error = "The timestamp token does not contain a TSTInfo" };
            if (cms.SignerInfos.Count == 0) return info with { Error = "No SignerInfo" };
            var tst = new System.Formats.Asn1.AsnReader(cms.ContentInfo.Content, System.Formats.Asn1.AsnEncodingRules.BER).ReadSequence();
            tst.ReadInteger(); tst.ReadObjectIdentifier();
            var imprint = tst.ReadSequence();
            var alg = imprint.ReadSequence().ReadObjectIdentifier();
            var hashed = imprint.ReadOctetString();
            tst.ReadInteger();
            var genTime = tst.ReadGeneralizedTime();
            var expected = HashOf(alg, Concat(data, br));
            var imprintOk = expected is not null && expected.AsSpan().SequenceEqual(hashed);
            var si = cms.SignerInfos[0];
            info = info with { SignerCertificate = si.Certificate, SignerName = si.Certificate?.GetNameInfo(X509NameType.SimpleName, false), TimeStampTime = genTime, DigestValid = imprintOk };
            try { cms.CheckSignature(true); info = info with { SignatureValid = imprintOk }; }
            catch (CryptographicException e) { info = info with { Error = e.Message }; }
            return si.Certificate is null ? info : info with { ChainTrusted = BuildChain(si.Certificate, cms.Certificates) };
        }
        catch (Exception e) when (e is CryptographicException or System.Formats.Asn1.AsnContentException or InvalidOperationException)
        {
            return info with { Error = "Malformed timestamp token: " + e.Message };
        }
    }

    static bool BuildChain(X509Certificate2 cert, X509Certificate2Collection extra)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
        chain.ChainPolicy.ExtraStore.AddRange(extra);
        return chain.Build(cert);
    }

    // /Contents is zero-padded after the DER structure; SignedCms wants exactly the structure.
    static byte[] TrimDer(byte[] b)
    {
        if (b.Length < 2 || b[0] != 0x30) return b;
        int len, hdr;
        if ((b[1] & 0x80) == 0) { len = b[1]; hdr = 2; }
        else
        {
            var n = b[1] & 0x7F; len = 0; hdr = 2 + n;
            if (n == 0 || n > 4 || hdr > b.Length) return b;
            for (var i = 0; i < n; i++) len = (len << 8) | b[2 + i];
        }
        return hdr + len <= b.Length ? b[..(hdr + len)] : b;
    }

    readonly ref struct AsnReaderOctet
    {
        public byte[] Value { get; }
        public AsnReaderOctet(byte[] der)
        {
            var r = new System.Formats.Asn1.AsnReader(der, System.Formats.Asn1.AsnEncodingRules.DER);
            Value = r.ReadOctetString();
        }
    }
}
