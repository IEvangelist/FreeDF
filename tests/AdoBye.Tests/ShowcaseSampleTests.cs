using System.Text;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using AdoBye.Samples;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

/// <summary>Sample 28 (benchmarks, brand directions, spec parity): deterministic, tagged, self-contained, and consistent with the docs it quotes.</summary>
public class ShowcaseSampleTests(GeneratedSamples gen) : IClassFixture<GeneratedSamples>
{
    const string File28 = "28-benchmarks-brand-spec.pdf";

    byte[] Bytes => File.ReadAllBytes(Path.Combine(gen.Dir, File28));

    PdfDocument Doc() => PdfDocument.Load(Bytes);

    static string PageText(PdfDocument doc, int page) => string.Join("\n", DocumentRenderer.ExtractText(doc.Pages[page]).Select(s => s.Text));

    [Fact]
    public void RegeneratesByteForByte()
    {
        var other = Path.Combine(Path.GetTempPath(), "samples-28-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal([File28], SampleGenerator.Generate(other, "28-"));
            Assert.True(File.ReadAllBytes(Path.Combine(other, File28)).AsSpan().SequenceEqual(Bytes));
        }
        finally { if (Directory.Exists(other)) Directory.Delete(other, true); }
    }

    [Fact]
    public void HasEighteenA4PagesWithTitleLanguageAndOutline()
    {
        var doc = Doc();
        Assert.Equal(18, doc.Pages.Count);
        Assert.All(doc.Pages, p => { Assert.InRange(p.MediaBox.Width, 595.2, 595.4); Assert.InRange(p.MediaBox.Height, 841.8, 842.0); });
        Assert.Contains("Measured, named, mapped", doc.Info("Title"));
        var catalog = (PdfDictionary)doc.File.Resolve(doc.File.Trailer.Get("Root"))!;
        Assert.Equal("en-US", ((PdfString)catalog.Get("Lang")!).Text);
        Assert.True(((PdfDictionary)doc.File.Resolve(catalog.Get("ViewerPreferences"))!).Get("DisplayDocTitle") is PdfBoolean { Value: true });
        var outline = doc.GetOutlines();
        Assert.Equal(["Cover", "Read this first", "Part 1: Benchmarks", "Part 2: Brand ideas", "Part 3: Spec parity", "Method and provenance"], outline.Select(o => o.Title));
        Assert.Equal([0, 1, 2, 9, 14, 17], outline.Select(o => o.PageIndex));
        Assert.Equal([7, 5, 3], outline.Skip(2).Take(3).Select(o => o.Children.Count));
    }

    [Fact]
    public void EmbedsEverySubsetAndReferencesNothingExternal()
    {
        var doc = Doc();
        var fonts = 0;
        foreach (var page in doc.Pages)
        {
            if (doc.File.Resolve(page.Resources.Get("Font")) is not PdfDictionary dict) continue;
            foreach (var (_, value) in dict.Entries)
            {
                var font = (PdfDictionary)doc.File.Resolve(value)!;
                Assert.Equal("Type0", font.Get("Subtype").AsName());
                Assert.NotNull(font.Get("ToUnicode"));
                var cid = (PdfDictionary)doc.File.Resolve(((PdfArray)doc.File.Resolve(font.Get("DescendantFonts"))!)[0])!;
                var desc = (PdfDictionary)doc.File.Resolve(cid.Get("FontDescriptor"))!;
                Assert.True(desc.Get("FontFile2") is not null || desc.Get("FontFile3") is not null, "font is not embedded");
                Assert.Matches("^[A-Z]{6}\\+", font.Get("BaseFont").AsName()!);
                fonts++;
            }
        }
        Assert.True(fonts >= 7);

        var text = Encoding.Latin1.GetString(Bytes);
        foreach (var token in new[] { "/URI", "/GoToR", "/GoToE", "/Launch", "/SubmitForm", "/ImportData", "/JavaScript", "/EmbeddedFile", "/Subtype /Image", "/Encrypt" })
            Assert.DoesNotContain(token, text);
    }

    [Fact]
    public void IsTaggedWithAltTextOnEveryFigureAndPassesTheUA1Heuristics()
    {
        var doc = Doc();
        var tree = doc.Structure;
        Assert.NotNull(tree);
        var all = tree!.Children.SelectMany(c => c.Descendants().Prepend(c)).ToList();
        var figures = all.Where(e => e.StandardType == "Figure").ToList();
        Assert.True(figures.Count >= 30);
        Assert.All(figures, f => Assert.False(string.IsNullOrWhiteSpace(f.Alt), "figure without alternative text"));
        Assert.Equal(18, all.Count(e => e.StandardType == "H1"));
        Assert.Contains(all, e => e.StandardType == "Table");
        Assert.Contains(all, e => e.StandardType == "TH");
        Assert.Contains(all, e => e.StandardType == "L");

        var report = PdfValidator.ValidatePdfUA(doc, 1);
        Assert.True(report.IsCompliant, string.Join("\n", report.Errors.Select(e => e.ToString())));
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void RendersEveryPageWithoutWarnings()
    {
        var doc = Doc();
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var r = DocumentRenderer.RenderPage(doc.Pages[i], 48);
            Assert.True(r.Width > 0 && r.Height > 0);
            Assert.Empty(r.Warnings);
        }
    }

    [Fact]
    public void QuotesTheReportsAndTheMatrixExactly()
    {
        var doc = Doc();
        Assert.Contains("15 / 0", PageText(doc, 1));
        Assert.Contains("4.55\u00D7", PageText(doc, 1));
        Assert.Contains("524 \u2192 374 ms", PageText(doc, 5));

        // Status pills on the two parity tables must equal the status words in docs/spec-matrix.md.
        var matrix = Path.Combine(Path.GetDirectoryName(SamplesTests.SamplesDir()!)!, "docs", "spec-matrix.md");
        var section = "";
        int done = 0, partial = 0, editorPartial = 0;
        foreach (var line in File.ReadLines(matrix))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) { section = line; continue; }
            if (!line.StartsWith("| ", StringComparison.Ordinal)) continue;
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (section.StartsWith("## Parser", StringComparison.Ordinal) || section.StartsWith("## Renderer", StringComparison.Ordinal))
            {
                if (cells[3] == "Done") done++;
                else if (cells[3] == "Partial") partial++;
            }
            else if (section == "## Editor" && cells.Length > 2 && cells[2].StartsWith("Partial", StringComparison.Ordinal)) editorPartial++;
        }
        var pills = Enumerable.Range(15, 2).SelectMany(p => DocumentRenderer.ExtractText(doc.Pages[p]).Select(s => s.Text)).ToList();
        Assert.Equal(done, pills.Count(t => t == "Done"));
        Assert.Equal(partial + editorPartial, pills.Count(t => t == "Partial"));
        Assert.Equal(15, partial + editorPartial);
    }
}
