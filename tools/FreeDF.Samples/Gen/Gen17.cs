using PortableDocument.Core;

namespace FreeDF.Samples.Gen;

static class Gen17
{
    public const string Out = "17-encrypted-user-password.pdf";
    const string Password = "user123";

    public static void Build(string path)
    {
        var d = Secure.Page("Board briefing", "Encryption  /  AES-256", "PASSWORD PROTECTED",
        [
            ("How to open this file", $"This document is encrypted with the AES-256 security handler (V5, R6). The document-open password is  {Password}  (owner password: owner-{Password})."),
            ("What it demonstrates", "A /Encrypt dictionary with /CF /StdCF using /CFM /AESV3, a 32-byte file key wrapped by the user and owner passwords (/UE, /OE), and a /Perms entry. Every string and stream is encrypted except the cross-reference data."),
            ("Why it matters", "Opening this sample verifies that a reader performs the ISO 32000-2 Algorithm 2.A key derivation (the iterated SHA-256/384/512 hash) and AES-CBC decryption with a per-object IV."),
        ]);
        d.Save(path, encryption: Secure.Encryption(Password, "owner-" + Password, PdfPermissions.All, Out));
    }
}
