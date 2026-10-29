using AdoBye.Render;
using AdoByePdfDocument = AdoBye.Document.PdfDocument;
using BenchmarkDotNet.Attributes;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

[MemoryDiagnoser]
public class PdfOpenBench
{
    static readonly string[] CorpusFiles =
    [
        "text.pdf",
        "vector.pdf",
        "image.pdf",
        "03-typography-embedded.pdf",
        "06-patterns-shadings.pdf",
        "09-images.pdf"
    ];

    [ParamsSource(nameof(Corpus))]
    public string CorpusFile { get; set; } = null!;

    public IEnumerable<string> Corpus => CorpusFiles;

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        _pdf = ReadFixture(CorpusFile);
        var adoByePages = AdoByePdfDocument.Load(_pdf).Pages.Count;
        using var pdfPig = PdfPigDocument.Open(_pdf);

        if (adoByePages != pdfPig.NumberOfPages)
        {
            throw new InvalidOperationException(
                $"Page count mismatch for {CorpusFile}: AdoBye={adoByePages}, PdfPig={pdfPig.NumberOfPages}.");
        }
    }

    [Benchmark(Baseline = true)]
    public int AdoByeOpen() => AdoByePdfDocument.Load(_pdf).Pages.Count;

    [Benchmark]
    public int PdfPigOpen()
    {
        using var document = PdfPigDocument.Open(_pdf);
        return document.NumberOfPages;
    }

    static byte[] ReadFixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", fileName));
}

[MemoryDiagnoser]
public class TextExtractionBench
{
    static readonly string[] CorpusFiles = ["text.pdf", "03-typography-embedded.pdf"];

    [ParamsSource(nameof(Corpus))]
    public string CorpusFile { get; set; } = null!;

    public IEnumerable<string> Corpus => CorpusFiles;

    byte[] _pdf = [];

    [GlobalSetup]
    public void Setup()
    {
        _pdf = ReadFixture(CorpusFile);
        var adoBye = ExtractAdoBye();
        var pdfPig = ExtractPdfPig();

        if (adoBye.PageCount != pdfPig.PageCount)
        {
            throw new InvalidOperationException(
                $"Page count mismatch for {CorpusFile}: AdoBye={adoBye.PageCount}, PdfPig={pdfPig.PageCount}.");
        }

        if (adoBye.CharacterCount == 0 || pdfPig.CharacterCount == 0)
        {
            throw new InvalidOperationException($"Expected text extraction from {CorpusFile}.");
        }

        Console.WriteLine(
            $"Preflight {CorpusFile}: pages={adoBye.PageCount}, AdoBye chars={adoBye.CharacterCount}, PdfPig chars={pdfPig.CharacterCount}.");
    }

    [Benchmark(Baseline = true)]
    public long AdoByeOpenAndExtractText() => ExtractAdoBye().CharacterCount;

    [Benchmark]
    public long PdfPigOpenAndExtractText() => ExtractPdfPig().CharacterCount;

    (int PageCount, long CharacterCount) ExtractAdoBye()
    {
        var document = AdoByePdfDocument.Load(_pdf);
        long characterCount = 0;
        foreach (var page in document.Pages)
        {
            foreach (var span in PdfRenderer.ExtractText(page))
            {
                characterCount += span.Text.Length;
            }
        }

        return (document.Pages.Count, characterCount);
    }

    (int PageCount, long CharacterCount) ExtractPdfPig()
    {
        using var document = PdfPigDocument.Open(_pdf);
        long characterCount = 0;
        foreach (var page in document.GetPages())
        {
            foreach (var letter in page.Letters)
            {
                characterCount += letter.Value.Length;
            }
        }

        return (document.NumberOfPages, characterCount);
    }

    static byte[] ReadFixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", fileName));
}
