using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using PortableDocument.Core;
using PortableDocument.Document;

namespace PortableDocument.Signing.Http;

/// <summary>
/// RFC 3161 time-stamping client over HTTP. It only runs when a caller constructs it with a TSA URL and passes it to
/// <see cref="PdfEditor.AddDocumentTimeStamp(byte[], ITimestampProvider, HashAlgorithmName?, string, int, string)"/>; nothing in the library contacts a network on its own and the browser editor never uses it.
/// The returned token's message imprint and nonce are checked; the TSA's signature and trust are not (use <see cref="PdfSignatureTrust"/> on the result).
/// </summary>
public sealed class Rfc3161TimestampClient : ITimestampProvider, IDisposable
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

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
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
