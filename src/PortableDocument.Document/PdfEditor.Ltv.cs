using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>
/// RFC 3161 time-stamping client over HTTP. It only runs when a caller constructs it with a TSA URL and passes it to
/// <see cref="PdfEditor.AddDocumentTimeStamp(byte[], ITimestampProvider, HashAlgorithmName?, string, int, string)"/>; nothing in the library contacts a network on its own and the browser editor never uses it.
/// The returned token's message imprint and nonce are checked; the TSA's signature and trust are not (use <see cref="PdfSignatureTrust"/> on the result).
/// </summary>
public sealed class Rfc3161TimestampClient : ITimestampProvider
{
    readonly Uri _url;
    readonly HttpClient _http;
    readonly bool _ownsClient;
    readonly string? _policyOid;

    public Rfc3161TimestampClient(Uri tsaUrl, HttpClient? httpClient = null, string? policyOid = null)
    {
        if (tsaUrl.Scheme is not ("http" or "https")) throw new ArgumentException("The TSA URL must be http or https.", nameof(tsaUrl));
        _url = tsaUrl;
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 1 << 20 };
        _policyOid = policyOid;
    }

    public byte[] GetTimestampToken(HashAlgorithmName hash, byte[] digest)
    {
        var oid = hash.Name switch { "SHA256" => "2.16.840.1.101.3.4.2.1", "SHA384" => "2.16.840.1.101.3.4.2.2", "SHA512" => "2.16.840.1.101.3.4.2.3", "SHA1" => "1.3.14.3.2.26", _ => throw new ArgumentException("Unsupported hash algorithm", nameof(hash)) };
        var nonce = new byte[8];
        RandomNumberGenerator.Fill(nonce);
        var nonceValue = new BigInteger(nonce, isUnsigned: true);
        var req = BuildRequest(oid, digest, nonceValue, _policyOid);
        using var content = new ByteArrayContent(req);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using var response = _http.PostAsync(_url, content).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return ParseResponse(body, oid, digest, nonceValue);
    }

    internal static byte[] BuildRequest(string hashOid, byte[] digest, BigInteger nonce, string? policyOid)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteInteger(1);
            using (w.PushSequence())
            {
                using (w.PushSequence()) { w.WriteObjectIdentifier(hashOid); w.WriteNull(); }
                w.WriteOctetString(digest);
            }
            if (policyOid is not null) w.WriteObjectIdentifier(policyOid);
            w.WriteInteger(nonce);
            w.WriteBoolean(true);
        }
        return w.Encode();
    }

    internal static byte[] ParseResponse(byte[] body, string hashOid, byte[] digest, BigInteger nonce)
    {
        try
        {
            var resp = new AsnReader(body, AsnEncodingRules.BER).ReadSequence();
            var status = resp.ReadSequence();
            var code = (int)status.ReadInteger();
            if (code is not (0 or 1)) throw new InvalidOperationException($"The time-stamping authority refused the request (PKIStatus {code}).");
            var token = resp.ReadEncodedValue().ToArray();
            var cms = new SignedCms();
            cms.Decode(token);
            if (cms.ContentInfo.ContentType.Value != "1.2.840.113549.1.9.16.1.4") throw new InvalidOperationException("The response token has no TSTInfo.");
            var tst = new AsnReader(cms.ContentInfo.Content, AsnEncodingRules.BER).ReadSequence();
            tst.ReadInteger(); tst.ReadObjectIdentifier();
            var imprint = tst.ReadSequence();
            var alg = imprint.ReadSequence().ReadObjectIdentifier();
            var hashed = imprint.ReadOctetString();
            if (alg != hashOid || !hashed.AsSpan().SequenceEqual(digest)) throw new InvalidOperationException("The token's message imprint does not match the request.");
            tst.ReadInteger(); tst.ReadGeneralizedTime();
            if (tst.HasData && tst.PeekTag().TagValue == 16) tst.ReadEncodedValue();
            if (tst.HasData && tst.PeekTag().TagValue == 1) tst.ReadBoolean();
            if (!tst.HasData || tst.PeekTag().TagValue != 2 || tst.ReadInteger() != nonce) throw new InvalidOperationException("The token does not echo the request nonce.");
            return token;
        }
        catch (Exception e) when (e is AsnContentException or CryptographicException)
        {
            throw new InvalidOperationException("The time-stamp response is malformed: " + e.Message, e);
        }
    }
}

public sealed partial class PdfEditor
{
    /// <summary>Appends a document timestamp whose token comes from an <see cref="ITimestampProvider"/>; see <see cref="AddDocumentTimeStamp(byte[], TimeStampProvider, HashAlgorithmName?, string, int, string)"/>.</summary>
    public static byte[] AddDocumentTimeStamp(byte[] pdf, ITimestampProvider provider, HashAlgorithmName? hash = null, string fieldName = "DocTimeStamp1", int contentsSize = 16384, string password = "")
        => AddDocumentTimeStamp(pdf, provider.GetTimestampToken, hash, fieldName, contentsSize, password);

    /// <summary>
    /// Adds the validation data in <paramref name="data"/> to the document security store (/DSS, §12.8.4.3) in an incremental update, with a /VRI entry
    /// (key: uppercase hex SHA-1 of the signature's /Contents) for each signature or document timestamp (or only <paramref name="fieldName"/>). Existing
    /// signatures stay intact. Together with a signature whose CMS has a timestamp (or a following document timestamp) this yields PAdES-B-LT; add
    /// a document timestamp afterwards for B-LTA. The data is stored as given; it is not validated here (see <see cref="PdfSignatureTrust"/>).
    /// </summary>
    public static byte[] AddValidationInformation(byte[] pdf, PdfValidationData data, string? fieldName = null, string password = "")
    {
        var f = PdfFile.Load(pdf, password);
        if (f.Repaired) throw new InvalidOperationException("Adding validation data needs an intact file; this one required repair");
        var infos = PdfSignatures.Verify(f).Where(s => s.ByteRangeValid && (fieldName is null || s.FieldName == fieldName)).ToList();
        if (infos.Count == 0) throw new InvalidOperationException(fieldName is null ? "The document has no signatures" : $"No signature field named {fieldName}");
        if (data.IsEmpty) throw new ArgumentException("No validation data supplied", nameof(data));

        var size = (int)(f.Resolve(f.Trailer.Get("Size")).AsNumber() ?? 1);
        var next = Math.Max(f.Xref.Keys.Max() + 1, size);
        var rootNum = (f.Trailer.Get("Root") as PdfRef ?? throw new PdfParseException("no catalog reference")).Number;
        var objs = new Dictionary<int, PdfObject>();
        var catalog = (PdfDictionary)f.GetObject(rootNum)!;

        PdfRef NewStream(byte[] bytes)
        {
            var n = next++;
            objs[n] = new PdfStream(new PdfDictionary(), bytes);
            return new PdfRef(n, 0);
        }
        List<PdfObject> Items(PdfDictionary? d, string key) => d is not null && f.Resolve(d.Get(key)) is PdfArray a ? [.. a.Items] : [];
        HashSet<string> Existing(List<PdfObject> items) => items.Select(i => f.Resolve(i) is PdfStream s ? Convert.ToHexString(SHA256.HashData(SafeDecode(f, s))) : "").ToHashSet();
        PdfObject Merge(List<PdfObject> have, IEnumerable<byte[]> add)
        {
            var seen = Existing(have);
            var added = new List<PdfRef>();
            foreach (var b in add)
                if (seen.Add(Convert.ToHexString(SHA256.HashData(b)))) added.Add(NewStream(b));
            return new PdfArray([.. have, .. added]);
        }

        var dssRaw = catalog.Get("DSS");
        var dssNum = dssRaw is PdfRef dr ? dr.Number : 0;
        var dss = f.Resolve(dssRaw) as PdfDictionary ?? new PdfDictionary();
        var dssEntries = new Dictionary<string, PdfObject>(dss.Entries);

        var certBlobs = data.Certificates.Select(c => c.RawData).ToList();
        if (certBlobs.Count > 0) dssEntries["Certs"] = Merge(Items(dss, "Certs"), certBlobs);
        if (data.Crls.Count > 0) dssEntries["CRLs"] = Merge(Items(dss, "CRLs"), data.Crls);
        if (data.OcspResponses.Count > 0) dssEntries["OCSPs"] = Merge(Items(dss, "OCSPs"), data.OcspResponses);

        // Each stream is stored once; VRI entries point at the same objects as the global arrays.
        PdfRef? Find(string key, byte[] blob)
        {
            if (dssEntries[key] is not PdfArray a) return null;
            var h = SHA256.HashData(blob);
            foreach (var item in a.Items)
                if (item is PdfRef r && (objs.TryGetValue(r.Number, out var o) ? o : f.GetObject(r.Number)) is PdfStream s && SHA256.HashData(SafeDecode(f, s, objs.ContainsKey(r.Number))).AsSpan().SequenceEqual(h)) return r;
            return null;
        }

        var vriEntries = f.Resolve(dss.Get("VRI")) is PdfDictionary ev ? new Dictionary<string, PdfObject>(ev.Entries) : new Dictionary<string, PdfObject>();
        var tu = new PdfString(Encoding.ASCII.GetBytes(PdfDate(DateTimeOffset.UtcNow)));
        foreach (var s in infos)
        {
            var contents = Convert.FromHexString(Encoding.ASCII.GetString(f.Data, (int)s.ByteRange[1] + 1, (int)(s.ByteRange[2] - s.ByteRange[1] - 2)).Where(c => !char.IsWhiteSpace(c)).ToArray());
            var key = Convert.ToHexString(SHA1.HashData(contents));
            var oldVri = vriEntries.TryGetValue(key, out var ov) ? f.Resolve(ov) as PdfDictionary : null;
            var entry = new Dictionary<string, PdfObject>(oldVri?.Entries ?? new Dictionary<string, PdfObject>()) { ["TU"] = tu };
            void Fill(string vriKey, string dssKey, IEnumerable<byte[]> blobs)
            {
                var refs = blobs.Select(b => Find(dssKey, b)).OfType<PdfRef>().ToList();
                if (refs.Count == 0) return;
                var merged = Items(oldVri, vriKey);
                foreach (var r in refs) if (!merged.Any(m => m is PdfRef mr && mr.Number == r.Number)) merged.Add(r);
                entry[vriKey] = new PdfArray(merged);
            }
            Fill("Cert", "Certs", certBlobs);
            Fill("CRL", "CRLs", data.Crls);
            Fill("OCSP", "OCSPs", data.OcspResponses);
            vriEntries[key] = new PdfDictionary(entry);
        }
        dssEntries["VRI"] = new PdfDictionary(vriEntries);

        var newDss = new PdfDictionary(dssEntries);
        if (dssNum != 0) objs[dssNum] = newDss;
        else
        {
            dssNum = next++;
            objs[dssNum] = newDss;
            catalog = With(catalog, "DSS", new PdfRef(dssNum, 0));
        }
        objs[rootNum] = catalog;
        return AppendUpdate(f, objs, next, rootNum, (f.Trailer.Get("Info") as PdfRef)?.Number, new HashSet<int>());
    }

    static byte[] SafeDecode(PdfFile f, PdfStream s, bool isNew = false)
    {
        if (isNew) return s.RawData;
        try { return f.DecodeStream(s); } catch (Exception) { return []; }
    }
}
