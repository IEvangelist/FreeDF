using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

/// <summary>Every committed sample under /samples must open and render cleanly (the encrypted ones with their documented passwords).</summary>
public class SamplesTests
{
    static readonly Dictionary<string, string> Passwords = new()
    {
        ["17-"] = "user123",
        ["18-"] = "owner123",
    };

    internal static string? SamplesDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var s = Path.Combine(d.FullName, "samples");
            if (Directory.Exists(s) && Directory.GetFiles(s, "01-*.pdf").Length > 0) return s;
        }
        return null;
    }

    internal static PdfDocument Open(string dir, string file)
    {
        var password = Passwords.FirstOrDefault(kv => file.StartsWith(kv.Key, StringComparison.Ordinal)).Value ?? "";
        return PdfDocument.Open(Path.Combine(dir, file), password);
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
}
