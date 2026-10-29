using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

/// <summary>Every committed sample under /samples must open and render page 1 cleanly.</summary>
public class SamplesTests
{
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

    public static IEnumerable<object[]> Samples() =>
        (SamplesDir() is { } dir ? Directory.GetFiles(dir, "*.pdf").Order() : Enumerable.Empty<string>())
        .Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void SamplesFolderIsPresent() => Assert.NotNull(SamplesDir());

    [Theory]
    [MemberData(nameof(Samples))]
    public void RendersFirstPage(string file)
    {
        var path = Path.Combine(SamplesDir()!, file);
        var password = Passwords.FirstOrDefault(kv => file.StartsWith(kv.Key, StringComparison.Ordinal)).Value ?? "";
        var doc = PdfDocument.Open(path, password);
        Assert.True(doc.Pages.Count >= 1);
        var r = DocumentRenderer.RenderPage(doc.Pages[0], 48);
        Assert.True(r.Width > 0 && r.Height > 0);
        Assert.Empty(r.Warnings);
    }
}
