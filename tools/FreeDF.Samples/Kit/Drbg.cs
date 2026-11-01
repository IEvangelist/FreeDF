using System.Security.Cryptography;

namespace FreeDF.Samples.Kit;

/// <summary>Deterministic byte source (HMAC-SHA256 in counter mode) so encrypted samples regenerate byte-for-byte. Never use for real documents.</summary>
public sealed class Drbg(string seed)
{
    readonly byte[] _key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
    long _counter;

    public byte[] Bytes(int n)
    {
        var o = new List<byte>();
        while (o.Count < n) o.AddRange(HMACSHA256.HashData(_key, BitConverter.GetBytes(_counter++)));
        return [.. o.Take(n)];
    }
}
