using System.Security.Cryptography;

namespace Pdf.Core;

public sealed class PdfPasswordException(string msg) : PdfParseException(msg);

/// <summary>Standard security handler (ISO 32000-2 §7.6): RC4 40/128, AESV2, AESV3 (R2-R6).</summary>
public sealed class PdfDecryptor
{
    static readonly byte[] Pad =
    [0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08, 0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A];

    enum Method { None, Rc4, AesV2, AesV3 }

    readonly byte[] _key;
    readonly Method _stm, _str;
    readonly int _v;
    public bool EncryptMetadata { get; }
    public int Permissions { get; }
    public int Revision { get; }

    PdfDecryptor(byte[] key, Method stm, Method str, int v, bool encMeta, int p, int r)
    { _key = key; _stm = stm; _str = str; _v = v; EncryptMetadata = encMeta; Permissions = p; Revision = r; }

    public static PdfDecryptor Create(PdfDictionary enc, byte[] id0, Func<PdfObject?, PdfObject?> resolve, string password)
    {
        var filter = resolve(enc.Get("Filter")).AsName();
        if (filter != "Standard") throw new PdfParseException($"Unsupported security handler /{filter} (public-key handlers need a recipient certificate)");
        int Int(string k, int d = 0) => (int)(resolve(enc.Get(k)).AsNumber() ?? d);
        byte[] Bytes(string k) => (resolve(enc.Get(k)) as PdfString)?.Bytes ?? [];
        var v = Int("V"); var r = Int("R"); var p = Int("P");
        var length = Int("Length", 40);
        var encMeta = resolve(enc.Get("EncryptMetadata")) is not PdfBoolean { Value: false };
        Method stm = Method.Rc4, str = Method.Rc4;
        if (v >= 4)
        {
            var cf = resolve(enc.Get("CF")) as PdfDictionary;
            Method Of(string key)
            {
                var name = resolve(enc.Get(key)).AsName() ?? "Identity";
                if (name == "Identity") return Method.None;
                var d = resolve(cf?.Get(name)) as PdfDictionary;
                if (d is null) return Method.None;
                var l = (int)(resolve(d.Get("Length")).AsNumber() ?? 0);
                if (l > 0 && v == 4 && length == 40) length = l <= 40 ? l * 8 : l; // CF /Length is in bytes in practice
                return resolve(d.Get("CFM")).AsName() switch { "V2" => Method.Rc4, "AESV2" => Method.AesV2, "AESV3" => Method.AesV3, _ => Method.None };
            }
            stm = Of("StmF"); str = Of("StrF");
            if (v == 4) length = 128;
        }
        if (v == 1) length = 40;
        var o = Bytes("O"); var u = Bytes("U");
        byte[]? key;
        if (r >= 5)
        {
            key = TryKeyV5(r, o, u, Bytes("OE"), Bytes("UE"), password);
        }
        else
        {
            var n = r == 2 ? 5 : Math.Clamp(length / 8, 5, 16);
            key = TryUser(password, o, u, p, id0, r, n, encMeta)
                  ?? TryUser(UserFromOwner(password, o, r, n), o, u, p, id0, r, n, encMeta);
        }
        if (key is null) throw new PdfPasswordException("Incorrect or missing password");
        return new PdfDecryptor(key, stm, str, v, encMeta, p, r);
    }

    static byte[] PadPwd(string pwd) { var b = System.Text.Encoding.Latin1.GetBytes(pwd); return [.. b.Take(32), .. Pad.Take(32 - Math.Min(32, b.Length))]; }
    static byte[] PadPwd(byte[] b) => [.. b.Take(32), .. Pad.Take(32 - Math.Min(32, b.Length))];

    static byte[]? TryUser(string pwd, byte[] o, byte[] u, int p, byte[] id0, int r, int n, bool encMeta)
        => TryUser(PadPwd(pwd), o, u, p, id0, r, n, encMeta);

    static byte[]? TryUser(byte[] padded, byte[] o, byte[] u, int p, byte[] id0, int r, int n, bool encMeta)
    {
        using var md5 = MD5.Create();
        var ms = new List<byte>(padded); ms.AddRange(o.Take(32));
        ms.AddRange(BitConverter.GetBytes(p)); ms.AddRange(id0);
        if (r >= 4 && !encMeta) ms.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        var h = md5.ComputeHash(ms.ToArray());
        if (r >= 3) for (var i = 0; i < 50; i++) h = md5.ComputeHash(h, 0, n);
        var key = h.Take(n).ToArray();
        if (r == 2) return Rc4(key, Pad).AsSpan().SequenceEqual(u.AsSpan(0, Math.Min(32, u.Length))) ? key : null;
        var chk = md5.ComputeHash([.. Pad, .. id0]);
        chk = Rc4(key, chk);
        for (var i = 1; i <= 19; i++) chk = Rc4(key.Select(b => (byte)(b ^ i)).ToArray(), chk);
        return u.Length >= 16 && chk.AsSpan(0, 16).SequenceEqual(u.AsSpan(0, 16)) ? key : null;
    }

    static byte[] UserFromOwner(string ownerPwd, byte[] o, int r, int n)
    {
        using var md5 = MD5.Create();
        var h = md5.ComputeHash(PadPwd(ownerPwd));
        if (r >= 3) for (var i = 0; i < 50; i++) h = md5.ComputeHash(h);
        var key = h.Take(n).ToArray();
        var data = o.Take(32).ToArray();
        if (r == 2) return Rc4(key, data);
        for (var i = 19; i >= 0; i--) data = Rc4(key.Select(b => (byte)(b ^ i)).ToArray(), data);
        return data;
    }

    static byte[]? TryKeyV5(int r, byte[] o, byte[] u, byte[] oe, byte[] ue, string password)
    {
        if (u.Length < 48 || o.Length < 48) return null;
        var pwd = System.Text.Encoding.UTF8.GetBytes(password).Take(127).ToArray();
        byte[] H(byte[] salt, byte[] udata) => r == 5 ? SHA256.HashData([.. pwd, .. salt, .. udata]) : Hash2B(pwd, salt, udata);
        var u48 = u.Take(48).ToArray();
        if (H(u[32..40], []).AsSpan().SequenceEqual(u.AsSpan(0, 32)))
            return AesCbcNoPad(H(u[40..48], []), new byte[16], ue.Take(32).ToArray());
        if (H(o[32..40], u48).AsSpan().SequenceEqual(o.AsSpan(0, 32)))
            return AesCbcNoPad(H(o[40..48], u48), new byte[16], oe.Take(32).ToArray());
        return null;
    }

    // ISO 32000-2 Algorithm 2.B
    static byte[] Hash2B(byte[] pwd, byte[] salt, byte[] udata)
    {
        var k = SHA256.HashData([.. pwd, .. salt, .. udata]);
        for (var round = 0; ; round++)
        {
            var k1one = new List<byte>(); k1one.AddRange(pwd); k1one.AddRange(k); k1one.AddRange(udata);
            var k1 = new byte[k1one.Count * 64];
            for (var i = 0; i < 64; i++) k1one.CopyTo(k1, i * k1one.Count);
            using var aes = Aes.Create(); aes.Key = k[..16]; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None;
            var e = aes.EncryptCbc(k1, k[16..32], PaddingMode.None);
            var mod = 0; for (var i = 0; i < 16; i++) mod += e[i];
            k = (mod % 3) switch { 0 => SHA256.HashData(e), 1 => SHA384.HashData(e), _ => SHA512.HashData(e) };
            if (round >= 63 && e[^1] <= round - 32) break;
        }
        return k[..32];
    }

    static byte[] AesCbcNoPad(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create(); aes.Key = key;
        return aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    public static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = new byte[256]; for (var i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; (s[i], s[j]) = (s[j], s[i]); }
        var o = new byte[data.Length]; int a = 0, b = 0;
        for (var k = 0; k < data.Length; k++)
        {
            a = (a + 1) & 255; b = (b + s[a]) & 255; (s[a], s[b]) = (s[b], s[a]);
            o[k] = (byte)(data[k] ^ s[(s[a] + s[b]) & 255]);
        }
        return o;
    }

    byte[] ObjKey(int num, int gen, Method m)
    {
        if (_v >= 5) return _key;
        var b = new List<byte>(_key) { (byte)num, (byte)(num >> 8), (byte)(num >> 16), (byte)gen, (byte)(gen >> 8) };
        if (m == Method.AesV2) b.AddRange("sAlT"u8.ToArray());
        return MD5.HashData(b.ToArray()).Take(Math.Min(_key.Length + 5, 16)).ToArray();
    }

    byte[] Run(byte[] data, int num, int gen, Method m)
    {
        if (m == Method.None) return data;
        var key = ObjKey(num, gen, m);
        if (m == Method.Rc4) return Rc4(key, data);
        if (data.Length < 32) return data.Length >= 16 ? [] : data;
        using var aes = Aes.Create(); aes.Key = key;
        var iv = data[..16]; var body = data[16..(16 + (data.Length - 16) / 16 * 16)];
        try { return aes.DecryptCbc(body, iv, PaddingMode.PKCS7); }
        catch (CryptographicException) { return aes.DecryptCbc(body, iv, PaddingMode.None); }
    }

    public PdfObject Decrypt(PdfObject o, int num, int gen) => o switch
    {
        PdfString s => s with { Bytes = Run(s.Bytes, num, gen, _str) },
        PdfArray a => new PdfArray(a.Items.Select(i => Decrypt(i, num, gen)).ToList()),
        PdfStream st => DecryptStream(st, num, gen),
        PdfDictionary d => DictCopy(d, num, gen),
        _ => o,
    };

    PdfDictionary DictCopy(PdfDictionary d, int num, int gen)
        => new(d.Entries.ToDictionary(kv => kv.Key, kv => Decrypt(kv.Value, num, gen)));

    PdfStream DecryptStream(PdfStream st, int num, int gen)
    {
        var type = st.Dict.Get("Type").AsName();
        if (type == "XRef") return st;
        var dict = DictCopy(st.Dict, num, gen);
        if (type == "Metadata" && !EncryptMetadata) return new PdfStream(dict, st.RawData);
        // An explicit /Crypt filter named Identity opts the stream out of decryption.
        var f = st.Dict.Get("Filter");
        var first = f is PdfArray { Items.Count: > 0 } fa ? fa.Items[0].AsName() : f.AsName();
        if (first == "Crypt") return new PdfStream(dict, st.RawData);
        return new PdfStream(dict, Run(st.RawData, num, gen, _stm));
    }
}
