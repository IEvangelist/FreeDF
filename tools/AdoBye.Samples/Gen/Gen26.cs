using System.Security.Cryptography;
using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Samples.Gen;

/// <summary>Sample 20 rewritten by this repo's PdfEditor/PdfWriter with compressed object streams and a cross-reference stream (PDF 1.5+ structure).</summary>
static class Gen26
{
    public const string Out = "26-repacked-object-streams.pdf";

    public static void Build(string path)
    {
        var source = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Gen20.Out));
        var editor = PdfEditor.Open(source);
        var options = new PdfWriteOptions { Compress = true, ObjectStreams = true, FileId = MD5.HashData(source) };
        File.WriteAllBytes(path, editor.Save(options: options));
    }
}
