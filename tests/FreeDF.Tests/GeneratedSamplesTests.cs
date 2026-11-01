using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using FreeDF.Samples;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

/// <summary>Regenerates every sample with the C# generators (once per run) into a temp folder.</summary>
public sealed class GeneratedSamples : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "samples-" + Guid.NewGuid().ToString("N"));
    public IReadOnlyList<string> Files { get; }

    public GeneratedSamples()
    {
        Directory.CreateDirectory(Dir);
        Files = SampleGenerator.Generate(Dir);
    }

    public string Full => Path.Combine(Dir, "27-full-collection.pdf");

    public IEnumerable<string> Sources => Files.Where(f => string.CompareOrdinal(f[..2], "27") < 0);

    /// <summary>Samples numbered above the collection (28+): committed and published, but not merged into 27.</summary>
    public IEnumerable<string> Standalone => Files.Where(f => string.CompareOrdinal(f[..2], "27") > 0);

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
}

/// <summary>The committed samples must be exactly what the generators produce, and the full collection must faithfully contain all the others.</summary>
public class GeneratedSamplesTests(GeneratedSamples gen) : IClassFixture<GeneratedSamples>
{
    const string FullFile = "27-full-collection.pdf";

    PdfDocument Source(string file) => SamplesTests.Open(gen.Dir, file);

    [Fact]
    public void GeneratorsProduceTheCommittedSamplesByteForByte()
    {
        var committed = SamplesTests.SamplesDir()!;
        Assert.Equal(26, gen.Sources.Count());
        Assert.Equal(["28-benchmarks-brand-spec.pdf"], gen.Standalone);
        foreach (var f in gen.Sources.Concat(gen.Standalone))
            Assert.True(File.ReadAllBytes(Path.Combine(committed, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(gen.Dir, f))), $"{f} differs from its generator output (regenerate the samples)");
    }

    [Fact]
    public void FullCollectionHasThePagesOfEverySampleExactlyOnce()
    {
        var sources = gen.Sources.ToList();
        var counts = sources.Select(f => Source(f).Pages.Count).ToList();
        var full = Source(FullFile);
        Assert.False(full.File.IsEncrypted);
        Assert.Equal(counts.Sum(), full.Pages.Count);

        var outline = full.GetOutlines();
        Assert.Equal(sources.Count, outline.Count);
        var start = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            Assert.StartsWith(sources[i][..2], outline[i].Title);
            Assert.Equal(start, outline[i].PageIndex);
            Assert.StartsWith(sources[i][..2], full.GetPageLabel(start));
            start += counts[i];
        }
    }

    [Fact]
    public void EncryptedSamplesAreMergedInTheClearWithTheirContent()
    {
        var full = Source(FullFile);
        var starts = StartPages();
        foreach (var (prefix, text) in new[] { ("17-", "Board briefing"), ("18-", "Restricted release") })
        {
            var spans = DocumentRenderer.ExtractText(full.Pages[starts[prefix]]);
            Assert.Contains(text, string.Concat(spans.Select(s => s.Text)));
        }
    }

    [Fact]
    public void SiblingLinksAndNamedDestinationsResolveInsideTheMergedFile()
    {
        var full = Source(FullFile);
        var starts = StartPages();
        var sources = gen.Sources.ToList();

        // 01's GoToR tiles become GoTo links to the first page of the sample they named.
        var links = full.Pages[starts["01-"]].GetAnnotations().Where(a => a.Subtype == "Link").ToList();
        Assert.True(links.Count >= 20);
        Assert.All(links, a => Assert.Contains(a.DestPage!.Value, starts.Values));
        Assert.Equal(links.Count, links.Select(a => a.DestPage).Distinct().Count());

        // Every link of every other sample keeps pointing at the same page of its own sample (named destinations in 12 included).
        foreach (var f in sources.Where(f => !f.StartsWith("01-", StringComparison.Ordinal)))
        {
            var src = Source(f);
            for (var i = 0; i < src.Pages.Count; i++)
            {
                var expected = src.Pages[i].GetAnnotations().Where(a => a.Subtype == "Link").Select(a => a.DestPage + starts[f[..3]]).ToList();
                var actual = full.Pages[starts[f[..3]] + i].GetAnnotations().Where(a => a.Subtype == "Link").Select(a => a.DestPage).ToList();
                Assert.Equal(expected, actual);
            }
        }
        var nav = full.Pages[starts["12-"]].GetAnnotations().Where(a => a.Subtype == "Link").ToList();
        Assert.NotEmpty(nav);
        Assert.All(nav, a => Assert.InRange(a.DestPage!.Value, starts["12-"], starts["12-"] + 6));
    }

    [Fact]
    public void FormLayersAttachmentsAndStructureSurviveTheMerge()
    {
        var full = Source(FullFile);
        var starts = StartPages();

        var form = Source("11-acroform.pdf");
        Assert.Equal(form.GetFormFields().Select(f => (f.FullName, f.FieldType, f.Value, f.Flags)), full.GetFormFields().Select(f => (f.FullName, f.FieldType, f.Value, f.Flags)));
        Assert.Equal(form.Pages[0].GetAnnotations().Count(a => a.Subtype == "Widget"), full.Pages[starts["11-"]].GetAnnotations().Count(a => a.Subtype == "Widget"));

        Assert.Equal(LayerNames(Source("15-optional-content.pdf")), LayerNames(full));
        Assert.True(LayerNames(full).Count >= 2);

        var files = Source("14-embedded-files.pdf").GetEmbeddedFiles();
        Assert.NotEmpty(files);
        Assert.Equal(files.Select(f => (f.Name, f.MimeType, Convert.ToHexString(f.Data))), full.GetEmbeddedFiles().Select(f => (f.Name, f.MimeType, Convert.ToHexString(f.Data))));

        var tagged = Source("19-tagged-accessible.pdf");
        Assert.Equal(Flatten(tagged.Structure!.Children, starts["19-"]), Flatten(full.Structure!.Children, 0));

        // Bookmarks nested under their sample keep their titles and move with their pages.
        var outline = full.GetOutlines();
        foreach (var prefix in new[] { "12-", "20-", "26-" })
        {
            var expected = Source(gen.Sources.Single(f => f.StartsWith(prefix, StringComparison.Ordinal))).GetOutlines();
            var actual = outline.Single(o => o.Title.StartsWith(prefix[..2], StringComparison.Ordinal)).Children;
            Assert.NotEmpty(expected);
            Assert.Equal(expected.Select(o => (o.Title, o.PageIndex + starts[prefix])), actual.Select(o => (o.Title, o.PageIndex)));
        }
    }

    [Fact]
    public void FullCollectionRendersEveryPage()
    {
        var full = Source(FullFile);
        var warnings = new List<string>();
        for (var i = 0; i < full.Pages.Count; i++)
        {
            var r = DocumentRenderer.RenderPage(full.Pages[i], 24);
            Assert.True(r.Width > 0 && r.Height > 0);
            warnings.AddRange(r.Warnings.Select(w => $"page {i + 1}: {w}"));
        }
        Assert.Empty(warnings);
    }

    // First merged page of each sample, keyed by "NN-" (and, for convenience, by the 3-character prefix).
    Dictionary<string, int> StartPages()
    {
        var starts = new Dictionary<string, int>();
        var next = 0;
        foreach (var f in gen.Sources)
        {
            starts[f[..3]] = next;
            next += Source(f).Pages.Count;
        }
        return starts;
    }

    static List<string> LayerNames(PdfDocument doc)
    {
        var f = doc.File;
        var ocgs = f.Resolve(f.Resolve(f.Catalog.Get("OCProperties")) is PdfDictionary p ? p.Get("OCGs") : null) as PdfArray;
        return ocgs is null ? [] : ocgs.Items.Select(o => f.Resolve(o) is PdfDictionary d && f.Resolve(d.Get("Name")) is PdfString s ? s.Text : "").ToList();
    }

    static List<string> Flatten(IEnumerable<StructElement> elements, int pageOffset) =>
        elements.SelectMany(e => new[] { $"{e.Depth}|{e.StandardType}|{e.Alt}|{e.Lang}|{e.PageIndex + pageOffset}|{e.MarkedContent.Count()}" }.Concat(Flatten(e.ChildElements, pageOffset))).ToList();
}
