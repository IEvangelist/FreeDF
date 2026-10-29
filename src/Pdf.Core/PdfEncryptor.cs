using System.Security.Cryptography;
using System.Text;

namespace Pdf.Core;

/// <summary>Standard security handler, writing side: AES-256 (V5, R6) per ISO 32000-2 §7.6.4 with user/owner passwords and permission flags.</summary>
public sealed class PdfEncryption
{
    public string UserPassword { get; init; } = "";
    public string OwnerPassword { get; init; } = "";
    /// <summary>The /P value; bit set = allowed. Default allows everything.</summary>
    public int Permissions { get; init; } = -4;
    public bool EncryptMetadata { get; init; } = true;
}

internal sealed class PdfEncryptor
{
    readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    public bool EncryptMetadata { get; }
    public PdfDictionary Dict { get; }

    public PdfEncryptor(PdfEncryption o)
    {
        EncryptMetadata = o.EncryptMetadata;
        byte[] Pwd(string s) => Encoding.UTF8.GetBytes(s).Take(127).ToArray();
        var up = Pwd(o.UserPassword);
        var op = Pwd(o.OwnerPassword.Length > 0 ? o.OwnerPassword : o.UserPassword);
        var uv = RandomNumberGenerator.GetBytes(8); var uk = RandomNumberGenerator.GetBytes(8);
        var u = Join(PdfDecryptor.Hash2B(up, uv, []), uv, uk);
        var ue = AesNoPad(PdfDecryptor.Hash2B(up, uk, []), _key);
        var ov = RandomNumberGenerator.GetBytes(8); var ok = RandomNumberGenerator.GetBytes(8);
        var o48 = Join(PdfDecryptor.Hash2B(op, ov, u), ov, ok);
        var oe = AesNoPad(PdfDecryptor.Hash2B(op, ok, u), _key);

        var perms = new byte[16];
        BitConverter.GetBytes(o.Permissions).CopyTo(perms, 0);
        perms[4] = perms[5] = perms[6] = perms[7] = 0xFF;
        perms[8] = (byte)(o.EncryptMetadata ? 'T' : 'F');
        "adb"u8.CopyTo(perms.AsSpan(9));
        RandomNumberGenerator.GetBytes(4).CopyTo(perms, 12);
        using var aes = Aes.Create();
        aes.Key = _key;
        var permsEnc = aes.EncryptEcb(perms, PaddingMode.None);

        PdfString S(byte[] b) => new(b);
        var cf = new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["StdCF"] = new PdfDictionary(new Dictionary<string, PdfObject>
            { ["CFM"] = new PdfName("AESV3"), ["AuthEvent"] = new PdfName("DocOpen"), ["Length"] = new PdfInteger(32) }),
        });
        var d = new Dictionary<string, PdfObject>
        {
            ["Filter"] = new PdfName("Standard"), ["V"] = new PdfInteger(5), ["R"] = new PdfInteger(6), ["Length"] = new PdfInteger(256),
            ["P"] = new PdfInteger(o.Permissions), ["O"] = S(o48), ["U"] = S(u), ["OE"] = S(oe), ["UE"] = S(ue), ["Perms"] = S(permsEnc),
            ["CF"] = cf, ["StmF"] = new PdfName("StdCF"), ["StrF"] = new PdfName("StdCF"),
        };
        if (!o.EncryptMetadata) d["EncryptMetadata"] = new PdfBoolean(false);
        Dict = new PdfDictionary(d);
    }

    static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    byte[] AesNoPad(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, new byte[16], PaddingMode.None);
    }

    /// <summary>AES-256-CBC with a random IV prepended and PKCS#7 padding.</summary>
    public byte[] Encrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = RandomNumberGenerator.GetBytes(16);
        return [.. iv, .. aes.EncryptCbc(data, iv, PaddingMode.PKCS7)];
    }
}
