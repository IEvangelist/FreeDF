using System.Security.Cryptography;

namespace PortableDocument.Core;

/// <summary>
/// Crypto primitives used by the standard security handler. Desktop and server runtimes use the platform implementations;
/// the browser (where AES and friends throw PlatformNotSupportedException) uses <see cref="ManagedCrypto"/>.
/// </summary>
public static class PdfCrypto
{
    /// <summary>When true every primitive uses <see cref="ManagedCrypto"/>. Defaults to <see cref="OperatingSystem.IsBrowser"/>; also set automatically if the platform provider is missing.</summary>
    public static bool UseManaged { get; set; } = OperatingSystem.IsBrowser();

    static T Run<T>(Func<T> platform, Func<T> managed)
    {
        if (!UseManaged)
        {
            try { return platform(); }
            catch (PlatformNotSupportedException) { UseManaged = true; }
        }
        return managed();
    }

    /// <summary>MD5 digest.</summary>
    public static byte[] Md5(ReadOnlySpan<byte> data) { var d = data.ToArray(); return Run(() => MD5.HashData(d), () => ManagedCrypto.Md5(d)); }
    /// <summary>SHA-1 digest.</summary>
    public static byte[] Sha1(ReadOnlySpan<byte> data) { var d = data.ToArray(); return Run(() => SHA1.HashData(d), () => ManagedCrypto.Sha1(d)); }
    /// <summary>SHA-256 digest.</summary>
    public static byte[] Sha256(ReadOnlySpan<byte> data) { var d = data.ToArray(); return Run(() => SHA256.HashData(d), () => ManagedCrypto.Sha256(d)); }
    /// <summary>SHA-384 digest.</summary>
    public static byte[] Sha384(ReadOnlySpan<byte> data) { var d = data.ToArray(); return Run(() => SHA384.HashData(d), () => ManagedCrypto.Sha384(d)); }
    /// <summary>SHA-512 digest.</summary>
    public static byte[] Sha512(ReadOnlySpan<byte> data) { var d = data.ToArray(); return Run(() => SHA512.HashData(d), () => ManagedCrypto.Sha512(d)); }

    /// <summary>AES-CBC decryption with a 16/24/32-byte key; PKCS#7 padding is validated and removed when <paramref name="pkcs7"/> is set. Throws <see cref="CryptographicException"/> on bad key/padding.</summary>
    public static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] data, bool pkcs7) => Run(
        () => { using var a = Aes.Create(); a.Key = key; return a.DecryptCbc(data, iv, pkcs7 ? PaddingMode.PKCS7 : PaddingMode.None); },
        () => pkcs7 ? new ManagedCrypto.Aes(key).DecryptCbcPkcs7(data, iv) : new ManagedCrypto.Aes(key).DecryptCbcNoPad(data, iv));

    /// <summary>AES-CBC encryption (PKCS#7 padded when <paramref name="pkcs7"/> is set, otherwise whole blocks only).</summary>
    public static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] data, bool pkcs7) => Run(
        () => { using var a = Aes.Create(); a.Key = key; return a.EncryptCbc(data, iv, pkcs7 ? PaddingMode.PKCS7 : PaddingMode.None); },
        () => pkcs7 ? new ManagedCrypto.Aes(key).EncryptCbcPkcs7(data, iv) : new ManagedCrypto.Aes(key).EncryptCbcNoPad(data, iv));

    /// <summary>AES-ECB encryption of whole blocks (used for the /Perms entry).</summary>
    public static byte[] AesEcbEncrypt(byte[] key, byte[] data) => Run(
        () => { using var a = Aes.Create(); a.Key = key; return a.EncryptEcb(data, PaddingMode.None); },
        () => new ManagedCrypto.Aes(key).EncryptEcb(data));

    /// <summary>AES-ECB decryption of whole blocks.</summary>
    public static byte[] AesEcbDecrypt(byte[] key, byte[] data) => Run(
        () => { using var a = Aes.Create(); a.Key = key; return a.DecryptEcb(data, PaddingMode.None); },
        () => new ManagedCrypto.Aes(key).DecryptEcb(data));

    /// <summary>Cryptographically secure random bytes (the browser maps this to crypto.getRandomValues).</summary>
    public static byte[] RandomBytes(int count) => RandomNumberGenerator.GetBytes(count);
}
