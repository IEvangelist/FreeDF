using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using AdoBye.Content;
using AdoBye.Document;
using AdoBye.Render;

if (args is ["--profile-text", var file, var count])
{
    var bytes = ComparisonCorpus.Read(file);
    var iterations = int.Parse(count, System.Globalization.CultureInfo.InvariantCulture);
    long characters = 0;
    for (var i = 0; i < iterations; i++)
        foreach (var text in ComparisonCorpus.Text(PdfDocument.Load(bytes)))
            characters += text.Length;
    Console.WriteLine($"Profile workload: {iterations} fresh opens/extractions of {file}, {characters} characters.");
    return;
}

if (args.Contains("--preflight"))
{
    Console.WriteLine($"Accepted documents: {string.Join(", ", ComparisonCorpus.ValidatedFiles)}");
    return;
}

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

    sealed class NullBackend : IRenderBackend
    {
        public void Save() { }
        public void Restore() { }
        public void Clip(PathData path, Matrix ctm, bool evenOdd) { }
        public void Fill(PathData path, Matrix ctm, bool evenOdd, RgbColor color, double alpha, string blendMode) { }
        public void Stroke(PathData path, Matrix ctm, StrokeStyle style, RgbColor color, double alpha, string blendMode) { }
        public void DrawImage(ImageData image, Matrix ctm, double alpha, RgbColor maskColor, string blendMode) { }
        public void PaintShader(Func<double, double, RgbColor?> deviceToColor, double alpha, string blendMode) { }
        public void SetSoftMask(Action<IRenderBackend>? draw, bool luminosity, RgbColor backdrop) { }
    }

    // Interpreter-only cost (no rasterization).
    [Benchmark]
    public void TextInterpretOnly()
    {
        var doc = PdfDocument.Load(_text);
        foreach (var p in doc.Pages) new ContentInterpreter(doc.File, new NullBackend()).Render(p, Matrix.Identity);
    }

    [Benchmark] public void Text() => RenderAll(_text);
    [Benchmark] public void Vector() => RenderAll(_vector);
    [Benchmark] public void Image() => RenderAll(_image);
    [Benchmark] public byte[]? JpegBaseline() => JpegDecoder.Decode(_jpeg, out _, out _, out _);
    [Benchmark] public byte[]? JpegProgressive() => JpegDecoder.Decode(_jpegProg, out _, out _, out _);
    [Benchmark] public PdfDocument Parse() => PdfDocument.Load(_text);
}
