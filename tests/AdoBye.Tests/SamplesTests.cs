using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Text.RegularExpressions;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

/// <summary>Every committed sample under /samples must open and render cleanly, and the full collection must contain all the others.</summary>
public partial class SamplesTests
{
    const string FullFile = "27-full-collection.pdf";

    static readonly Dictionary<string, string> Passwords = new()
    {
        ["17-"] = "user123",
        ["18-"] = "owner123",
    };

    static string? SamplesDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var s = Path.Combine(d.FullName, "samples");
            if (Directory.Exists(s) && Directory.GetFiles(s, "01-*.pdf").Length > 0) return s;
        }
        return null;
    }

    static PdfDocument Open(string dir, string file)
    {
        var password = Passwords.FirstOrDefault(kv => file.StartsWith(kv.Key, StringComparison.Ordinal)).Value ?? "";
        return PdfDocument.Open(Path.Combine(dir, file), password);
    }

    static IEnumerable<string> Others(string dir) =>
        Directory.GetFiles(dir, "*.pdf").Select(f => Path.GetFileName(f)).Where(n => n != FullFile && NumberedName().IsMatch(n)).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"^\d\d-")]
    private static partial Regex NumberedName();

    public static IEnumerable<object[]> Samples() =>
        (SamplesDir() is { } dir ? Directory.GetFiles(dir, "*.pdf").Order() : Enumerable.Empty<string>())
        .Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void SamplesFolderIsPresent() => Assert.NotNull(SamplesDir());

    [Theory]
    [MemberData(nameof(Samples))]
    public void RendersFirstPage(string file)
    {
        var doc = Open(SamplesDir()!, file);
        Assert.True(doc.Pages.Count >= 1);
        var r = DocumentRenderer.RenderPage(doc.Pages[0], 48);
        Assert.True(r.Width > 0 && r.Height > 0);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void EncryptedSamplesNeedTheDocumentedPasswords()
    {
        var dir = SamplesDir()!;
        var user = Directory.GetFiles(dir, "17-*.pdf").Single();
        Assert.ThrowsAny<Exception>(() => PdfDocument.Open(user, ""));
        Assert.True(PdfDocument.Open(user, "user123").Pages.Count > 0);
        Assert.True(PdfDocument.Open(Directory.GetFiles(dir, "18-*.pdf").Single(), "").Pages.Count > 0);
    }

    [Fact]
    public void FullCollectionIncludesEverySampleExactlyOnce()
    {
        var dir = SamplesDir()!;
        var sources = Others(dir).ToList();
        Assert.Equal(26, sources.Count);
        var counts = sources.Select(f => Open(dir, f).Pages.Count).ToList();

        var full = PdfDocument.Open(Path.Combine(dir, FullFile));
        Assert.False(full.File.IsEncrypted);
        Assert.Equal(counts.Sum(), full.Pages.Count);

        // One bookmark per sample, in order, each landing on that sample's first page; page labels carry the sample number.
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
    public void FullCollectionRendersEveryPage()
    {
        var full = PdfDocument.Open(Path.Combine(SamplesDir()!, FullFile));
        var warnings = new List<string>();
        for (var i = 0; i < full.Pages.Count; i++)
        {
            var r = DocumentRenderer.RenderPage(full.Pages[i], 24);
            Assert.True(r.Width > 0 && r.Height > 0);
            warnings.AddRange(r.Warnings.Select(w => $"page {i + 1}: {w}"));
        }
        Assert.Empty(warnings);
    }

    [Fact]
    public void FullCollectionKeepsFormAttachmentsAndStructure()
    {
        var full = PdfDocument.Open(Path.Combine(SamplesDir()!, FullFile));
        Assert.NotEmpty(full.GetFormFields());
        Assert.NotEmpty(full.GetEmbeddedFiles());
        Assert.True(full.IsTagged());
    }
}
