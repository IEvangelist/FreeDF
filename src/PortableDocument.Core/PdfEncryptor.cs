using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PortableDocument.Core;

public enum PdfEncryptionMethod
{
    /// <summary>AES with a 256-bit key (V5/R6, AESV3).</summary>
    Aes256,
    /// <summary>AES with a 128-bit key (V4/R4, AESV2).</summary>
    Aes128,
    /// <summary>RC4 with a 128-bit key (V2/R3; V4 /V2 crypt filter for public-key).</summary>
    Rc4_128,
    /// <summary>RC4 with a 40-bit key (V1/R2).</summary>
    Rc4_40,
}

/// <summary>A public-key recipient: its certificate and the permissions it is granted.</summary>
public sealed record PdfRecipient(X509Certificate2 Certificate, int Permissions = -4);

/// <summary>
/// Encryption options for writing. With no <see cref="Recipients"/> the standard (password) handler is used; with recipients the
/// public-key handler (Adobe.PubSec, adbe.pkcs7.s5) is used and the passwords are ignored.
/// </summary>
public sealed class PdfEncryption
{
    public string UserPassword { get; init; } = "";
    public string OwnerPassword { get; init; } = "";
    /// <summary>The /P value; bit set = allowed. Default allows everything.</summary>
    public int Permissions { get; init; } = -4;
    public bool EncryptMetadata { get; init; } = true;
    /// <summary>Encrypt only embedded file streams (/Type /EmbeddedFile): /StmF and /StrF are Identity and /EFF names the crypt filter (ISO 32000-2 7.6.6, Table 20).
    /// Requires an AES method and no public-key recipients.</summary>
    public bool EmbeddedFilesOnly { get; init; }
    /// <summary>Cipher and key length (default AES-256).</summary>
    public PdfEncryptionMethod Method { get; init; } = PdfEncryptionMethod.Aes256;
    /// <summary>Certificates that can open the file (public-key handler). Per-recipient permissions override <see cref="Permissions"/>.</summary>
    public IReadOnlyList<PdfRecipient>? Recipients { get; init; }
    /// <summary>
    /// Source of the file key, salts and IVs (default: a cryptographic RNG); it must return exactly the requested number of bytes.
    /// A constant or otherwise non-random source can make password-encrypted output reproducible, which is ONLY appropriate for test fixtures and sample files and must never be used to protect real documents.
    /// Public-key recipient CMS envelopes still use platform cryptographic randomness and are not byte-reproducible.
    /// </summary>
    public Func<int, byte[]>? RandomBytes { get; init; }
}

internal sealed class PdfEncryptor
{
    static readonly byte[] Pad =
    [0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08, 0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A];

    readonly byte[] _key;
    readonly Func<int, byte[]> _rng;
    readonly PdfEncryptionMethod _method;
    public bool EncryptMetadata { get; }
    public bool EmbeddedFilesOnly { get; }
    public PdfDictionary Dict { get; }

    public PdfEncryptor(PdfEncryption o, byte[] id)
    {
        _method = o.Method;
        if (o.EmbeddedFilesOnly && (o.Recipients is { Count: > 0 } || o.Method is PdfEncryptionMethod.Rc4_40 or PdfEncryptionMethod.Rc4_128))
            throw new NotSupportedException("Embedded-files-only encryption (/EFF) requires an AES method and the standard password handler");
        EmbeddedFilesOnly = o.EmbeddedFilesOnly;
        _rng = o.RandomBytes is { } custom
            ? n => custom(n) is { } b && b.Length == n ? b : throw new ArgumentException($"PdfEncryption.RandomBytes must return exactly {n} bytes")
            : RandomNumberGenerator.GetBytes;
        EncryptMetadata = o.EncryptMetadata;
        if (o.Recipients is { Count: > 0 } rc) { (_key, Dict) = PubSec(o, rc); return; }
        if (o.Method == PdfEncryptionMethod.Aes256) { _key = _rng(32); Dict = Aes256Dict(o); return; }
        (_key, Dict) = Legacy(o, id);
    }

    static PdfString S(byte[] b) => new(b);

    PdfDictionary Aes256Dict(PdfEncryption o)
    {
        byte[] Pwd(string s) => Encoding.UTF8.GetBytes(s).Take(127).ToArray();
        var up = Pwd(o.UserPassword);
        var op = Pwd(o.OwnerPassword.Length > 0 ? o.OwnerPassword : o.UserPassword);
        var uv = _rng(8); var uk = _rng(8);
        var u = Join(PdfDecryptor.Hash2B(up, uv, []), uv, uk);
        var ue = AesNoPad(PdfDecryptor.Hash2B(up, uk, []), _key);
        var ov = _rng(8); var ok = _rng(8);
        var o48 = Join(PdfDecryptor.Hash2B(op, ov, u), ov, ok);
        var oe = AesNoPad(PdfDecryptor.Hash2B(op, ok, u), _key);

        var perms = new byte[16];
        BitConverter.GetBytes(o.Permissions).CopyTo(perms, 0);
        perms[4] = perms[5] = perms[6] = perms[7] = 0xFF;
        perms[8] = (byte)(o.EncryptMetadata ? 'T' : 'F');
        "adb"u8.CopyTo(perms.AsSpan(9));
        _rng(4).CopyTo(perms, 12);
        using var aes = Aes.Create();
        aes.Key = _key;
        var permsEnc = aes.EncryptEcb(perms, PaddingMode.None);

        var cf = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["StdCF"] = new PdfDictionary(new Dictionary<string, PdfObject>
            { ["CFM"] = new PdfName("AESV3"), ["AuthEvent"] = new PdfName("DocOpen"), ["Length"] = new PdfInteger(32) }),
        });
        var d = new Dictionary<string, PdfObject>
        {
            ["Filter"] = new PdfName("Standard"), ["V"] = new PdfInteger(5), ["R"] = new PdfInteger(6), ["Length"] = new PdfInteger(256),
            ["P"] = new PdfInteger(o.Permissions), ["O"] = S(o48), ["U"] = S(u), ["OE"] = S(oe), ["UE"] = S(ue), ["Perms"] = S(permsEnc),
            ["CF"] = cf, ["StmF"] = new PdfName(o.EmbeddedFilesOnly ? "Identity" : "StdCF"), ["StrF"] = new PdfName(o.EmbeddedFilesOnly ? "Identity" : "StdCF"),
        };
        if (o.EmbeddedFilesOnly) d["EFF"] = new PdfName("StdCF");
        if (!o.EncryptMetadata) d["EncryptMetadata"] = new PdfBoolean(false);
        return new PdfDictionary(d);
    }

    // Standard handler R2/R3/R4: algorithms 2, 3, 4/5 of ISO 32000-1 §7.6.3.
    static (byte[] Key, PdfDictionary Dict) Legacy(PdfEncryption o, byte[] id)
    {
        var m = o.Method;
        var (v, r, bits) = m switch { PdfEncryptionMethod.Rc4_40 => (1, 2, 40), PdfEncryptionMethod.Rc4_128 => (2, 3, 128), _ => (4, 4, 128) };
        var n = bits / 8;
        var encMeta = o.EncryptMetadata || r < 4;
        byte[] Padded(string s) { var b = Encoding.Latin1.GetBytes(s); return [.. b.Take(32), .. Pad.Take(32 - Math.Min(32, b.Length))]; }
        var userPad = Padded(o.UserPassword);
        var ownerPad = Padded(o.OwnerPassword.Length > 0 ? o.OwnerPassword : o.UserPassword);

        var h = MD5.HashData(ownerPad);
        if (r >= 3) for (var i = 0; i < 50; i++) h = MD5.HashData(h);
        var ownerKey = h.Take(n).ToArray();
        var ov = PdfDecryptor.Rc4(ownerKey, userPad);
        if (r >= 3) for (var i = 1; i <= 19; i++) ov = PdfDecryptor.Rc4(ownerKey.Select(b => (byte)(b ^ i)).ToArray(), ov);

        var ms = new List<byte>(userPad);
        ms.AddRange(ov); ms.AddRange(BitConverter.GetBytes(o.Permissions)); ms.AddRange(id);
        if (r >= 4 && !encMeta) ms.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        var kh = MD5.HashData(ms.ToArray());
        if (r >= 3) for (var i = 0; i < 50; i++) kh = MD5.HashData(kh.AsSpan(0, n));
        var key = kh.Take(n).ToArray();

        byte[] u;
        if (r == 2) u = PdfDecryptor.Rc4(key, Pad);
        else
        {
            u = PdfDecryptor.Rc4(key, MD5.HashData([.. Pad, .. id]));
            for (var i = 1; i <= 19; i++) u = PdfDecryptor.Rc4(key.Select(b => (byte)(b ^ i)).ToArray(), u);
            u = [.. u, .. new byte[16]];
        }

        var d = new Dictionary<string, PdfObject>
        {
            ["Filter"] = new PdfName("Standard"), ["V"] = new PdfInteger(v), ["R"] = new PdfInteger(r), ["Length"] = new PdfInteger(bits),
            ["P"] = new PdfInteger(o.Permissions), ["O"] = S(ov), ["U"] = S(u),
        };
        if (v == 4)
        {
            d["CF"] = new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["StdCF"] = new PdfDictionary(new Dictionary<string, PdfObject>
                { ["CFM"] = new PdfName("AESV2"), ["AuthEvent"] = new PdfName("DocOpen"), ["Length"] = new PdfInteger(16) }),
            });
            d["StmF"] = new PdfName(o.EmbeddedFilesOnly ? "Identity" : "StdCF"); d["StrF"] = new PdfName(o.EmbeddedFilesOnly ? "Identity" : "StdCF");
            if (o.EmbeddedFilesOnly) d["EFF"] = new PdfName("StdCF");
            if (!encMeta) d["EncryptMetadata"] = new PdfBoolean(false);
        }
        return (key, new PdfDictionary(d));
    }

    // Public-key handler (§7.6.5): a random 20-byte seed + 4 permission bytes go into one CMS EnvelopedData per recipient;
    // the file key is SHA-1 (SHA-256 for AESV3) over the seed, all recipient blobs and 0xFFFFFFFF when metadata stays clear.
    (byte[] Key, PdfDictionary Dict) PubSec(PdfEncryption o, IReadOnlyList<PdfRecipient> recipients)
    {
        var m = o.Method;
        if (m == PdfEncryptionMethod.Rc4_40) throw new NotSupportedException("40-bit RC4 is not supported for the public-key handler");
        var seed = _rng(20);
        var blobs = new List<byte[]>();
        foreach (var rc in recipients)
        {
            var plain = new byte[24];
            seed.CopyTo(plain, 0);
            plain[20] = (byte)(rc.Permissions >> 24); plain[21] = (byte)(rc.Permissions >> 16); plain[22] = (byte)(rc.Permissions >> 8); plain[23] = (byte)rc.Permissions;
            var cms = new EnvelopedCms(new ContentInfo(plain), new AlgorithmIdentifier(new Oid("2.16.840.1.101.3.4.1.42"))); // AES-256-CBC
            cms.Encrypt(new CmsRecipient(rc.Certificate));
            blobs.Add(cms.Encode());
        }
        var input = new List<byte>(seed);
        foreach (var b in blobs) input.AddRange(b);
        if (!o.EncryptMetadata) input.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);
        var aes256 = m == PdfEncryptionMethod.Aes256;
        var key = (aes256 ? SHA256.HashData(input.ToArray()) : SHA1.HashData(input.ToArray())).Take(aes256 ? 32 : 16).ToArray();
        var recips = new PdfArray(blobs.Select(b => (PdfObject)S(b)).ToList());
        var cfm = m switch { PdfEncryptionMethod.Aes256 => "AESV3", PdfEncryptionMethod.Aes128 => "AESV2", _ => "V2" };
        var bits = aes256 ? 256 : 128;
        var cfd = new Dictionary<string, PdfObject>
        {
            ["CFM"] = new PdfName(cfm), ["Recipients"] = recips, ["Length"] = new PdfInteger(bits / 8),
            ["AuthEvent"] = new PdfName("DocOpen"),
        };
        if (!o.EncryptMetadata) cfd["EncryptMetadata"] = new PdfBoolean(false);
        var d = new Dictionary<string, PdfObject>
        {
            ["Filter"] = new PdfName("Adobe.PubSec"), ["SubFilter"] = new PdfName("adbe.pkcs7.s5"),
            ["V"] = new PdfInteger(aes256 ? 5 : 4), ["Length"] = new PdfInteger(bits),
            ["CF"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["DefaultCryptFilter"] = new PdfDictionary(cfd) }),
            ["StmF"] = new PdfName("DefaultCryptFilter"), ["StrF"] = new PdfName("DefaultCryptFilter"),
        };
        if (!o.EncryptMetadata) d["EncryptMetadata"] = new PdfBoolean(false);
        return (key, new PdfDictionary(d));
    }

    static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    byte[] AesNoPad(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, new byte[16], PaddingMode.None);
    }

    byte[] ObjKey(int num, int gen)
    {
        var b = new List<byte>(_key) { (byte)num, (byte)(num >> 8), (byte)(num >> 16), (byte)gen, (byte)(gen >> 8) };
        if (_method == PdfEncryptionMethod.Aes128) b.AddRange("sAlT"u8.ToArray());
        return MD5.HashData(b.ToArray()).Take(Math.Min(_key.Length + 5, 16)).ToArray();
    }

    /// <summary>Encrypts a string/stream belonging to indirect object <paramref name="num"/>: AES-CBC with random IV prepended and PKCS#7 padding, or RC4.</summary>
    public byte[] Encrypt(byte[] data, int num, int gen = 0)
    {
        if (_method == PdfEncryptionMethod.Aes256)
        {
            using var a = Aes.Create();
            a.Key = _key;
            var iv0 = _rng(16);
            return [.. iv0, .. a.EncryptCbc(data, iv0, PaddingMode.PKCS7)];
        }
        var key = ObjKey(num, gen);
        if (_method is PdfEncryptionMethod.Rc4_40 or PdfEncryptionMethod.Rc4_128) return PdfDecryptor.Rc4(key, data);
        using var aes = Aes.Create();
        aes.Key = key;
        var iv = _rng(16);
        return [.. iv, .. aes.EncryptCbc(data, iv, PaddingMode.PKCS7)];
    }
}
