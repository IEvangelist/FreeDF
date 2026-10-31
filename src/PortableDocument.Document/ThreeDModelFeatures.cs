using PortableDocument.Core;

namespace PortableDocument.Document;

/// <summary>Metadata from the 3D stream payload: exactly one of <see cref="U3d"/> / <see cref="Prc"/> is set unless the format is unrecognised or the stream could not be decoded.</summary>
public sealed record Pdf3DModelData(string? Format, U3dModel? U3d, PrcModel? Prc, bool Decoded);

public static class ThreeDModelFeatures
{
    const int MaxStreamBytes = 256 * 1024 * 1024;

    /// <summary>
    /// Reads U3D (ECMA-363) block structure or PRC (ISO 14739-1) section tables from a 3D stream as bounded metadata; nothing is rendered.
    /// The format is chosen from /Subtype, falling back to the payload signature.
    /// </summary>
    public static Pdf3DModelData ReadModel(this PdfDocument doc, Pdf3DStream stream)
    {
        byte[] bytes;
        try { bytes = doc.File.DecodeStream(stream.Stream); }
        catch (Exception e) when (e is PdfParseException or InvalidDataException or InvalidOperationException)
        {
            return new Pdf3DModelData(stream.Subtype, null, null, false);
        }
        if (bytes.Length > MaxStreamBytes) return new Pdf3DModelData(stream.Subtype, null, null, false);
        var format = stream.Subtype switch
        {
            "U3D" => "U3D",
            "PRC" => "PRC",
            _ => U3dReader.LooksLikeU3d(bytes) ? "U3D" : PrcReader.LooksLikePrc(bytes) ? "PRC" : null,
        };
        return format switch
        {
            "U3D" => new Pdf3DModelData(format, U3dReader.Read(bytes), null, true),
            "PRC" => new Pdf3DModelData(format, null, PrcReader.Read(bytes), true),
            _ => new Pdf3DModelData(format, null, null, true),
        };
    }
}
