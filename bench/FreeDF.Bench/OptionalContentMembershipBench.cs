using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

[MemoryDiagnoser]
public class OptionalContentMembershipBench
{
    [Params("AnyOn", "AllOn", "AnyOff", "AllOff")]
    public string Policy { get; set; } = "AnyOn";

    PdfDocument _document = null!;

    [GlobalSetup]
    public void Setup()
    {
        static PdfObject Parse(string text) => new PdfLexer(Encoding.ASCII.GetBytes(text)).ReadObject()!;
        var members = string.Join(" ", Enumerable.Range(6, 16).Select(n => $"{n} 0 R"));
        var off = string.Join(" ", Enumerable.Range(14, 8).Select(n => $"{n} 0 R"));
        var objects = new Dictionary<int, PdfObject>
        {
            [1] = Parse($"<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [{members}] " +
                $"/D << /BaseState /ON /OFF [{off}] >> >> >>"),
            [2] = Parse("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            [3] = Parse("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R " +
                "/Resources << /Properties << /Layer 5 0 R >> >> >>"),
            [4] = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(
                string.Concat(Enumerable.Repeat("/OC /Layer BDC 0 0 1 1 re f EMC\n", 128)))),
            [5] = Parse($"<< /Type /OCMD /OCGs [{members}] /P /{Policy} >>")
        };
        foreach (var number in Enumerable.Range(6, 16))
            objects[number] = Parse($"<< /Type /OCG /Name (Group {number}) >>");
        var bytes = PdfWriter.Write(objects, 1, null, options: new PdfWriteOptions { FileId = new byte[16] });
        _document = PdfDocument.Load(bytes);
        var expected = Policy is "AnyOn" or "AnyOff" ? 128 : 0;
        if (_document.File.Repaired || _document.Pages.Count != 1 || Interpret() != expected)
            throw new InvalidOperationException("Optional-content membership preflight failed.");
        Console.WriteLine($"PASS optional-content membership: policy={Policy}, bytes={bytes.Length}, " +
            $"SHA256={Convert.ToHexString(SHA256.HashData(bytes))}, " +
            $"engine={typeof(ContentInterpreter).Module.ModuleVersionId}.");
    }

    [Benchmark]
    public int Interpret()
    {
        var backend = new CountingBackend();
        var interpreter = new ContentInterpreter(_document.File, backend);
        interpreter.Render(_document.Pages[0], Matrix.Identity);
        if (interpreter.Warnings.Count != 0)
            throw new InvalidOperationException("Unexpected optional-content membership warning.");
        return backend.Fills;
    }

    sealed class CountingBackend : IRenderBackend
    {
        public int Fills { get; private set; }
        public void Save() { }
        public void Restore() { }
        public void Clip(PathData path, Matrix ctm, bool evenOdd) { }
        public void Fill(PathData path, Matrix ctm, bool evenOdd, RgbColor color, double alpha, string blendMode) => Fills++;
        public void Stroke(PathData path, Matrix ctm, StrokeStyle style, RgbColor color, double alpha, string blendMode) { }
        public void DrawImage(ImageData image, Matrix ctm, double alpha, RgbColor maskColor, string blendMode) { }
        public void PaintShader(Func<double, double, RgbColor?> deviceToColor, double alpha, string blendMode) { }
        public void SetSoftMask(Action<IRenderBackend>? draw, bool luminosity, RgbColor backdrop) { }
    }
}
