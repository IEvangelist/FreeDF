using PortableDocument.Core;

namespace FreeDF.Samples.Gen;

static class Gen18
{
    public const string Out = "18-encrypted-owner-restricted.pdf";
    const string Owner = "owner123";

    public static void Build(string path)
    {
        var d = Secure.Page("Restricted release", "Encryption  /  Permissions", "OPENS FREELY · RESTRICTED",
        [
            ("How to open this file", $"No password is needed to read it (empty user password). The owner password is  {Owner} ; it lifts every restriction."),
            ("Restrictions applied", "Printing is limited to low resolution; copying text, editing, annotating, form filling and page assembly are all disallowed. These are the /P permission bits, enforced cooperatively by the viewer."),
            ("Why it matters", "AES-256 (V5, R6) with an empty user password and a non-empty owner password is the common 'view-only' protection. A reader must derive the file key with the empty password and expose the permission flags to the application."),
        ]);
        d.Save(path, encryption: Secure.Encryption("", Owner, PdfPermissions.Print | PdfPermissions.ExtractAccessibility, Out));
    }
}
