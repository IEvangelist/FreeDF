using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Pdf.Core;

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
}

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
    public static IReadOnlyList<PdfSignatureInfo> Verify(byte[] pdf)
    {
        var f = PdfFile.Load(pdf);
        return Verify(f);
    }

    public static IReadOnlyList<PdfSignatureInfo> Verify(PdfFile f)
    {
        var result = new List<PdfSignatureInfo>();
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary form) return result;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Walk(PdfObject? node, string prefix, string? inheritedFt)
        {
            if (f.Resolve(node) is not PdfDictionary d || !seen.Add(d)) return;
            var name = (f.Resolve(d.Get("T")) as PdfString)?.Text;
            var full = name is null ? prefix : prefix.Length == 0 ? name : prefix + "." + name;
            var ft = f.Resolve(d.Get("FT")).AsName() ?? inheritedFt;
            if (f.Resolve(d.Get("Kids")) is PdfArray kids) foreach (var k in kids.Items) Walk(k, full, ft);
            if (ft == "Sig" && f.Resolve(d.Get("V")) is PdfDictionary v && v.Get("ByteRange") is not null)
                result.Add(Check(f, full, v));
        }
        if (f.Resolve(form.Get("Fields")) is PdfArray fields) foreach (var x in fields.Items) Walk(x, "", null);
        return result;
    }

    static PdfSignatureInfo Check(PdfFile f, string field, PdfDictionary v)
    {
        string? Text(string k) => (f.Resolve(v.Get(k)) as PdfString)?.Text;
        var br = (f.Resolve(v.Get("ByteRange")) as PdfArray)?.Items.Select(i => (long)(f.Resolve(i).AsNumber() ?? -1)).ToArray() ?? [];
        var info = new PdfSignatureInfo { FieldName = field, SubFilter = f.Resolve(v.Get("SubFilter")).AsName(), Reason = Text("Reason"), Location = Text("Location"), ByteRange = br };
        var data = f.Data;
        if (br.Length != 4 || br.Any(x => x < 0) || br[0] != 0 || br[1] <= 0 || br[2] < br[1] || br[2] + br[3] > data.Length
            || data[br[1]] != (byte)'<' || data[br[2] - 1] != (byte)'>')
            return info with { Error = "Malformed /ByteRange" };
        var end = data.Length;
        while (end > 0 && data[end - 1] is (byte)'\n' or (byte)'\r' or (byte)' ') end--;
        var covers = br[2] + br[3] >= end;
        info = info with { ByteRangeValid = true, CoversWholeDocument = covers };
        if (f.Resolve(v.Get("Contents")) is not PdfString contents) return info with { Error = "Missing /Contents" };
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

    // /Contents is zero-padded after the DER structure; SignedCms wants exactly the structure.
    static byte[] TrimDer(byte[] b)
    {
        if (b.Length < 2 || b[0] != 0x30) return b;
        int len, hdr;
        if ((b[1] & 0x80) == 0) { len = b[1]; hdr = 2; }
        else
        {
            var n = b[1] & 0x7F; len = 0; hdr = 2 + n;
            if (n > 4 || hdr > b.Length) return b;
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
