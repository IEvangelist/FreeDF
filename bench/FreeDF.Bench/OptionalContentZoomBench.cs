using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

[MemoryDiagnoser]
public class OptionalContentZoomBench
{
    [Params(1, 3)]
    public double Zoom { get; set; }

    OptionalContentState _state = null!;
    PdfDictionary _group = null!;

    [GlobalSetup]
    public void Setup()
    {
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R] " +
                "/D << /BaseState /ON /AS [<< /Event /View /OCGs [5 0 R] /Category [/Zoom] >>] >> >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>"),
            [4] = new PdfStream(new PdfDictionary(), []),
            [5] = Parse("<< /Type /OCG /Name (Zoom) /Usage << /Zoom << /min 1 /max 2 >> >> >>")
        };
        var bytes = PdfWriter.Write(objects, 1, null, options: new PdfWriteOptions { FileId = new byte[16] });
        var document = PdfDocument.Load(bytes);
        _state = new OptionalContentState(document);
        _group = _state.Groups.Single();
        if (document.File.Repaired || document.Pages.Count != 1
            || IsVisible() != (Zoom == 1) || _state.Warnings.Count != 0)
            throw new InvalidOperationException("Optional-content zoom preflight failed.");
        Console.WriteLine($"PASS optional-content zoom: zoom={Zoom}, bytes={bytes.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(bytes))}.");
    }

    [Benchmark]
    public bool IsVisible() => _state.IsVisible(_group, zoom: Zoom);
}
