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
}

/// <summary>Counts of validation material in a /VRI entry (§12.8.4.3).</summary>
public sealed record PdfValidationRelatedInfo(string Key, int Certificates, int Crls, int Ocsps, string? UpdateTime);

/// <summary>Read-only view of the document security store (/DSS, §12.8.4.3). Contents are not validated or used for trust decisions.</summary>
public sealed record PdfDocumentSecurityStore(int Certificates, int Crls, int Ocsps, IReadOnlyList<PdfValidationRelatedInfo> Vri);

/// <summary>Detached CMS (adbe.pkcs7.detached) signature creation helpers and verification, using System.Security.Cryptography.Pkcs.</summary>
public static class PdfSignatures
{
    /// <summary>Digest of the byte ranges followed by a CMS/PKCS#7 detached signature over them.</summary>
    public static byte[] ComputeCms(byte[] pdf, long[] byteRange, X509Certificate2 cert, DateTimeOffset signingTime, HashAlgorithmName hash)
    {
        var content = Concat(pdf, byteRange);
        var cms = new SignedCms(new ContentInfo(content), true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert)
        {
            DigestAlgorithm = new Oid(hash.Name switch { "SHA384" => "2.16.840.1.101.3.4.2.2", "SHA512" => "2.16.840.1.101.3.4.2.3", _ => "2.16.840.1.101.3.4.2.1" }),
            IncludeOption = X509IncludeOption.WholeChain,
        };
        signer.SignedAttributes.Add(new Pkcs9SigningTime(signingTime.UtcDateTime));
        cms.ComputeSignature(signer, silent: true);
        return cms.Encode();
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
        var found = new List<(string Name, PdfDictionary V, int VNum)>();
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
                found.Add((full, v, (d.Get("V") as PdfRef)?.Number ?? -1));
        }
        if (f.Resolve(form.Get("Fields")) is PdfArray fields) foreach (var x in fields.Items) Walk(x, "", null);

        var infos = found.Select(s => Check(f, s.Name, s.V)).ToList();
        var dss = ReadDss(f);
        var permsDocMdp = (f.Resolve(f.Catalog.Get("Perms")) as PdfDictionary)?.Get("DocMDP") as PdfRef;
        var ends = infos.Select(i => i.ByteRangeValid ? i.ByteRange[2] + i.ByteRange[3] : long.MaxValue).ToList();
        var docMdpCount = found.Count(s => DocMdpReferences(f, s.V).Count > 0);
        var result = new List<PdfSignatureInfo>();
        for (var i = 0; i < infos.Count; i++)
            result.Add(EnrichDocMdp(f, infos[i], found[i].V, found[i].VNum, permsDocMdp?.Number, ends[i], ends, docMdpCount, dss));
        return result;
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
            info = info with { DigestValid = digestOk };
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
