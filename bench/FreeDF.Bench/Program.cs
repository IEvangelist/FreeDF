using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PortableDocument.Content;
using PortableDocument.Document;
using PortableDocument.Render;

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

if (Environment.GetEnvironmentVariable("FREEDF_SEQ") == "1") AppContext.SetSwitch("PortableDocument.Render.DisableThreads", true);

if (args is ["--codec-bench"]) { RasterBench.Codecs(); return; }

if (args is ["--raster-micro"])
{
    RasterBench.Micro();
    return;
}

if (args is ["--raster-phases", var pdpi, var piter, ..var prest])
{
    RasterBench.Phases(prest.Length > 0 ? prest[0] : null!, double.Parse(pdpi, System.Globalization.CultureInfo.InvariantCulture), int.Parse(piter, System.Globalization.CultureInfo.InvariantCulture));
    return;
}

if (args is ["--raster-bench", var rdpi, var riter, ..var rrest])
{
    RasterBench.Run(double.Parse(rdpi, System.Globalization.CultureInfo.InvariantCulture), int.Parse(riter, System.Globalization.CultureInfo.InvariantCulture),
        rrest.Length > 0 && rrest[0] != "-" ? rrest[0] : null, rrest.Length > 1 ? rrest[1] : null);
    return;
}

if (args.Contains("--preflight"))
{
    Console.WriteLine($"Accepted documents: {string.Join(", ", ComparisonCorpus.ValidatedFiles)}");
    foreach (var members in new[] { 1, 1000 })
        foreach (var encrypted in new[] { false, true })
            new ObjectStreamBench { MemberCount = members, Encrypted = encrypted }.Setup();
    foreach (var rows in new[] { 64, 4096 })
        foreach (var updates in new[] { 1, 16 })
            new XrefHybridBench { Rows = rows, Updates = updates }.Setup();
    foreach (var objects in new[] { 64, 4096 })
        foreach (var dictionaryValues in new[] { false, true })
            new IndirectObjectBench { Objects = objects, DictionaryValues = dictionaryValues }.Setup();
    foreach (var streams in new[] { 64, 4096 })
        foreach (var indirectLength in new[] { false, true })
            new OrdinaryStreamBench { Streams = streams, IndirectLength = indirectLength }.Setup();
    foreach (var rows in new[] { 64, 4096 })
        foreach (var fixedEntry in new[] { false, true })
            new XrefClassicBench { Rows = rows, FixedEntry = fixedEntry }.Setup();
    foreach (var rows in new[] { 64, 4096 })
        foreach (var filter in new[] { "FlateDecode", "LZWDecode" })
            new XrefFilterBench { Rows = rows, Filter = filter }.Setup();
    foreach (var filters in new[] { 1, 2 })
        foreach (var indirect in new[] { false, true })
            foreach (var codec in new[] { "FlateDecode", "LZWDecode" })
                new ObjectStreamMetadataBench
                {
                    FilterCount = filters, IndirectParameters = indirect, CompressionFilter = codec
                }.Setup();
    foreach (var indirect in new[] { false, true })
        foreach (var codec in new[] { "FlateDecode", "LZWDecode" })
            new SingleFilterArrayMetadataBench
            {
                IndirectParameters = indirect, CompressionFilter = codec
            }.Setup();
    foreach (var size in new[] { 256, 1048576 })
        foreach (var random in new[] { false, true })
            foreach (var strict in new[] { false, true })
                new FlateBench { DecodedBytes = size, RandomInput = random, Strict = strict }.Setup();
    foreach (var size in new[] { 0, 256, 1048576 })
        new FlateEncodeBench { DecodedBytes = size }.Setup();
    foreach (var pattern in new[] { "Literal", "Repeated" })
        foreach (var strict in new[] { false, true })
            new LzwBench { Pattern = pattern, Strict = strict }.Setup();
    foreach (var zoom in new[] { 1, 3 })
        new OptionalContentZoomBench { Zoom = zoom }.Setup();
    foreach (var displayEvent in new[] { "View", "Print", "Export" })
        new OptionalContentEventBench { Event = displayEvent }.Setup();
    foreach (var displayEvent in new[] { "Print", "Export" })
        foreach (var manual in new[] { false, true })
            foreach (var recommendation in new[] { false, true })
                new OptionalContentTemporaryEventBench
                {
                    Event = displayEvent, Manual = manual, Recommendation = recommendation
                }.Setup();
    foreach (var policy in new[] { "AnyOn", "AllOn", "AnyOff", "AllOff" })
        new OptionalContentMembershipBench { Policy = policy }.Setup();
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
        foreach (var p in doc.Pages) DocumentRenderer.RenderPage(p, 96);
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
