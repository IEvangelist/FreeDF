using System.Security.Cryptography;
using AdoBye.Render;
using AdoByePdfDocument = AdoBye.Document.PdfDocument;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

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

    public static string[] Text(AdoByePdfDocument document) =>
        document.Pages.Select(page => string.Concat(
            PdfRenderer.ExtractText(page).Select(span => span.Text))).ToArray();

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
            var adoBye = AdoByePdfDocument.Load(bytes);
            using var pdfPig = PdfPigDocument.Open(bytes);
            var adoByeText = Text(adoBye).Select(Normalize).ToArray();
            var pdfPigText = Text(pdfPig).Select(Normalize).ToArray();
            var repeatedAdoBye = Text(AdoByePdfDocument.Load(bytes)).Select(Normalize).ToArray();
            using var repeatedPdfPig = PdfPigDocument.Open(bytes);
            var repeatedPdfPigText = Text(repeatedPdfPig).Select(Normalize).ToArray();
            if (!adoByeText.SequenceEqual(repeatedAdoBye) ||
                !pdfPigText.SequenceEqual(repeatedPdfPigText))
            {
                throw new InvalidOperationException($"FAIL {file}: unstable text across fresh opens.");
            }

            if (adoBye.Pages.Count != pdfPig.NumberOfPages ||
                !adoByeText.SequenceEqual(pdfPigText))
            {
                throw new InvalidOperationException(
                    $"FAIL {file}: unequal page counts or per-page text after removing whitespace. " +
                    $"AdoBye pages/chars={adoBye.Pages.Count}/{adoByeText.Sum(t => t.Length)}, " +
                    $"PdfPig pages/chars={pdfPig.NumberOfPages}/{pdfPigText.Sum(t => t.Length)}.");
            }

            Console.WriteLine(
                $"PASS {file}: bytes={bytes.Length}, SHA256={Convert.ToHexString(SHA256.HashData(bytes))}, " +
                $"pages={adoBye.Pages.Count}, normalized chars={adoByeText.Sum(t => t.Length)}.");
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
    public int AdoByeOpen() => AdoByePdfDocument.Load(_pdf).Pages.Count;

    [Benchmark]
    public int PdfPigOpen()
    {
        using var document = PdfPigDocument.Open(_pdf);
        return document.NumberOfPages;
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
    public string[] AdoByeOpenAndExtractText() =>
        ComparisonCorpus.Text(AdoByePdfDocument.Load(_pdf));

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
    readonly AdoByePdfDocument[] _adoBye = new AdoByePdfDocument[BatchSize];
    readonly PdfPigDocument[] _pdfPig = new PdfPigDocument[BatchSize];

    [GlobalSetup]
    public void LoadBytes() => _pdf = ComparisonCorpus.Read(CorpusFile);

    [IterationSetup]
    public void Setup()
    {
        for (var i = 0; i < BatchSize; i++)
        {
            _adoBye[i] = AdoByePdfDocument.Load(_pdf);
            _pdfPig[i] = PdfPigDocument.Open(_pdf);
        }
    }

    [IterationCleanup]
    public void Cleanup()
    {
        foreach (var document in _pdfPig) document.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = BatchSize)]
    public string[][] AdoByeExtractText() => _adoBye.Select(ComparisonCorpus.Text).ToArray();

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public string[][] PdfPigExtractText() => _pdfPig.Select(ComparisonCorpus.Text).ToArray();
}
