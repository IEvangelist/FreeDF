using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Pdf.Content;
using Pdf.Document;
using Pdf.Render;

BenchmarkSwitcher.FromAssembly(typeof(RenderBench).Assembly).Run(args);

[MemoryDiagnoser]
[ShortRunJob]
public class RenderBench
{
    byte[] _text = [], _vector = [], _image = [], _jpeg = [], _jpegProg = [];

    static byte[] Fx(string n) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", n));

    [GlobalSetup]
    public void Setup()
    {
        _text = Fx("text.pdf"); _vector = Fx("vector.pdf"); _image = Fx("image.pdf");
        _jpeg = Fx("photo.jpg"); _jpegProg = Fx("photo-prog.jpg");
    }

    static void RenderAll(byte[] pdf)
    {
        var doc = PdfDocument.Load(pdf);
        foreach (var p in doc.Pages) PdfRenderer.RenderPage(p, 96);
    }

    [Benchmark] public void Text() => RenderAll(_text);
    [Benchmark] public void Vector() => RenderAll(_vector);
    [Benchmark] public void Image() => RenderAll(_image);
    [Benchmark] public byte[]? JpegBaseline() => JpegDecoder.Decode(_jpeg, out _, out _, out _);
    [Benchmark] public byte[]? JpegProgressive() => JpegDecoder.Decode(_jpegProg, out _, out _, out _);
    [Benchmark] public PdfDocument Parse() => PdfDocument.Load(_text);
}
