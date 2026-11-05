using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Security.Cryptography;
using PortableDocument.Render;
using PortablePdfDocument = global::PortableDocument.Document.PdfDocument;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using PdfSharpReader = PdfSharp.Pdf.IO.PdfReader;
using PdfSharpOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;

public static class ComparisonCorpus
{
    static readonly string[] Files =
    [
        "text.pdf", "vector.pdf", "image.pdf",
        "03-typography-embedded.pdf", "06-patterns-shadings.pdf", "09-images.pdf"
    ];

    static readonly Lazy<string[]> Accepted = new(Preflight);
    public static IEnumerable<string> ValidatedFiles => Accepted.Value;

    public static byte[] Read(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", fileName));

    public static string[] Text(PortablePdfDocument document) =>
        document.Pages.Select(page => string.Concat(
            DocumentRenderer.ExtractText(page).Select(span => span.Text))).ToArray();

    public static string[] Text(PdfPigDocument document) =>
        document.GetPages().Select(page => string.Concat(
            page.Letters.Select(letter => letter.Value))).ToArray();

    static string Normalize(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    static string[] Preflight()
    {
        List<string> accepted = [];
        foreach (var file in Files)
        {
            var bytes = Read(file);
            var portableDocument = PortablePdfDocument.Load(bytes);
            using var pdfPig = PdfPigDocument.Open(bytes);
            var portableDocumentText = Text(portableDocument).Select(Normalize).ToArray();
            var pdfPigText = Text(pdfPig).Select(Normalize).ToArray();
            var repeatedPortableDocument = Text(PortablePdfDocument.Load(bytes)).Select(Normalize).ToArray();
            using var repeatedPdfPig = PdfPigDocument.Open(bytes);
            var repeatedPdfPigText = Text(repeatedPdfPig).Select(Normalize).ToArray();
            using var sharpStream = new MemoryStream(bytes, writable: false);
            using var sharp = PdfSharpReader.Open(sharpStream, PdfSharpOpenMode.Import);
            using var repeatedSharpStream = new MemoryStream(bytes, writable: false);
            using var repeatedSharp = PdfSharpReader.Open(repeatedSharpStream, PdfSharpOpenMode.Import);
            if (!portableDocumentText.SequenceEqual(repeatedPortableDocument) ||
                !pdfPigText.SequenceEqual(repeatedPdfPigText))
            {
                throw new InvalidOperationException($"FAIL {file}: unstable text across fresh opens.");
            }

            if (portableDocument.Pages.Count != pdfPig.NumberOfPages ||
                portableDocument.Pages.Count != sharp.PageCount || sharp.PageCount != repeatedSharp.PageCount ||
                !portableDocumentText.SequenceEqual(pdfPigText))
            {
                throw new InvalidOperationException(
                    $"FAIL {file}: unequal page counts or per-page text after removing whitespace. " +
                    $"PortableDocument pages/chars={portableDocument.Pages.Count}/{portableDocumentText.Sum(t => t.Length)}, " +
                    $"PdfPig pages/chars={pdfPig.NumberOfPages}/{pdfPigText.Sum(t => t.Length)}, " +
                    $"PDFsharp pages={sharp.PageCount}/{repeatedSharp.PageCount}.");
            }

            Console.WriteLine(
                $"PASS {file}: bytes={bytes.Length}, SHA256={Convert.ToHexString(SHA256.HashData(bytes))}, " +
                $"pages={portableDocument.Pages.Count}, normalized chars={portableDocumentText.Sum(t => t.Length)}.");
            accepted.Add(file);
        }

        if (accepted.Count == 0)
            throw new InvalidOperationException("No equivalent corpus documents passed preflight.");
        return accepted.ToArray();
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("Open")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PdfOpenBench
{
    [ParamsSource(nameof(Corpus))]
    public string CorpusFile { get; set; } = null!;
    public IEnumerable<string> Corpus => ComparisonCorpus.ValidatedFiles;
    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup() => _pdf = ComparisonCorpus.Read(CorpusFile);

    [Benchmark(Baseline = true)]
    public int PortableDocumentOpen() => PortablePdfDocument.Load(_pdf).Pages.Count;

    [Benchmark]
    public int PdfPigOpen()
    {
        using var document = PdfPigDocument.Open(_pdf);
        return document.NumberOfPages;
    }

    [Benchmark]
    public int PdfSharpOpen()
    {
        using var stream = new MemoryStream(_pdf, writable: false);
        using var document = PdfSharpReader.Open(stream, PdfSharpOpenMode.Import);
        return document.PageCount;
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("OpenAndExtract")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class TextExtractionBench
{
    [ParamsSource(nameof(Corpus))]
    public string CorpusFile { get; set; } = null!;
    public IEnumerable<string> Corpus => ComparisonCorpus.ValidatedFiles;
    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup() => _pdf = ComparisonCorpus.Read(CorpusFile);

    [Benchmark(Baseline = true)]
    public string[] PortableDocumentOpenAndExtractText() =>
        ComparisonCorpus.Text(PortablePdfDocument.Load(_pdf));

    [Benchmark]
    public string[] PdfPigOpenAndExtractText()
    {
        using var document = PdfPigDocument.Open(_pdf);
        return ComparisonCorpus.Text(document);
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("AlreadyOpen")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class OpenDocumentTextBench
{
    const int BatchSize = 32;
    [ParamsSource(nameof(Corpus))]
    public string CorpusFile { get; set; } = null!;
    public IEnumerable<string> Corpus => ComparisonCorpus.ValidatedFiles;
    byte[] _pdf = [];
    readonly PortablePdfDocument[] _portableDocument = new PortablePdfDocument[BatchSize];
    readonly PdfPigDocument[] _pdfPig = new PdfPigDocument[BatchSize];

    [GlobalSetup]
    public void LoadBytes() => _pdf = ComparisonCorpus.Read(CorpusFile);

    [IterationSetup]
    public void Setup()
    {
        for (var i = 0; i < BatchSize; i++)
        {
            _portableDocument[i] = PortablePdfDocument.Load(_pdf);
            _pdfPig[i] = PdfPigDocument.Open(_pdf);
        }
    }

    [IterationCleanup]
    public void Cleanup()
    {
        foreach (var document in _pdfPig) document.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = BatchSize)]
    public string[][] PortableDocumentExtractText() => _portableDocument.Select(ComparisonCorpus.Text).ToArray();

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public string[][] PdfPigExtractText() => _pdfPig.Select(ComparisonCorpus.Text).ToArray();
}
