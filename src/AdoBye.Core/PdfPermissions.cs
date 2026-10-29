namespace AdoBye.Core;

/// <summary>User access permission flags from the encryption dictionary's /P entry (ISO 32000-2 Table 22).</summary>
[Flags]
public enum PdfPermissions
{
    None = 0,
    Print = 1 << 2,
    Modify = 1 << 3,
    Copy = 1 << 4,
    Annotate = 1 << 5,
    FillForms = 1 << 8,
    ExtractAccessibility = 1 << 9,
    Assemble = 1 << 10,
    PrintHighQuality = 1 << 11,
    All = Print | Modify | Copy | Annotate | FillForms | ExtractAccessibility | Assemble | PrintHighQuality,
}

public static class PdfPermissionHelper
{
    /// <summary>Extracts the defined permission bits from a /P value.</summary>
    public static PdfPermissions FromP(int p) => (PdfPermissions)p & PdfPermissions.All;

    /// <summary>Builds a /P value: reserved bits (7-8 clear per spec, 1-2 clear, 13-32 set) plus the granted flags.</summary>
    public static int ToP(PdfPermissions allowed) => unchecked((int)0xFFFFF0C0) | (int)(allowed & PdfPermissions.All);
}

/// <summary>Thrown when an operation is refused because the document's permissions (and the opening credentials) do not allow it.</summary>
public sealed class PdfPermissionException(PdfPermissions required) : InvalidOperationException($"The document permissions do not allow: {required}")
{
    public PdfPermissions Required { get; } = required;
}
