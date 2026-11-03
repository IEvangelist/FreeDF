using System.Security.Cryptography;
using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.FallbackFonts;
using PortableDocument.Render;
using Xunit.Abstractions;
using DocumentRenderer = PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class SampleImageInterpolationGoldenTests(ITestOutputHelper output)
{
    const string Before = "75ABE00AA9325C6949D61FF87F963FC0D2A5FB6144265DCE213BF2C9C94546C9";
    const string After = "F1BD7DFDA42A00D926FAE5384EF6CF3D7F0447BE1DC5EC418823FB3A63E18092";

    RenderedPage Render(bool disableInterpolation)
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "samples", "09-images.pdf")));
        var page = doc.Pages[0];
        var xobjects = Assert.IsType<PdfDictionary>(doc.File.Resolve(page.Resources.Get("XObject")));
        var flagged = xobjects.Entries.Where(e => doc.File.Resolve(e.Value) is PdfStream s
            && doc.File.Resolve(s.Dict.Get("Interpolate")) is PdfBoolean { Value: true }).ToList();
        var entry = Assert.Single(flagged);
        output.WriteLine($"Interpolation XObject /{entry.Key}: {entry.Value}");
        var image = Assert.IsType<PdfStream>(doc.File.Resolve(entry.Value));
        Assert.Equal(16, image.Dict.Get("Width").AsNumber());
        Assert.Equal(16, image.Dict.Get("Height").AsNumber());
        var content = Encoding.Latin1.GetString(page.GetContents());
        const string inline = "/I true";
        Assert.Equal(1, content.Split(inline).Length - 1);
        if (disableInterpolation)
        {
            image.Dict.Entries["Interpolate"] = new PdfBoolean(false);
            page.Dict.Entries["Contents"] = new PdfStream(new PdfDictionary(),
                Encoding.Latin1.GetBytes(content.Replace(inline, "/I false", StringComparison.Ordinal)));
        }
        return DocumentRenderer.RenderPage(page,
            new PdfRenderOptions { FontFallback = new BundledFontFallback(new DirectoryAssetSource()) }, 96);
    }

    [Fact]
    public void DisabledFlagsRetainThePreInterpolationSampleHash()
    {
        Assert.Equal(Before, Convert.ToHexString(SHA256.HashData(Render(true).Rgba)));
    }

    [Fact]
    public void EnabledFlagsChangeOnlyTheirImageFootprints()
    {
        var before = Render(true);
        var after = Render(false);
        Assert.Equal(After, Convert.ToHexString(SHA256.HashData(after.Rgba)));
        var changed = 0;
        var xobjectChanged = 0;
        var inlineChanged = 0;
        for (var y = 0; y < after.Height; y++)
        for (var x = 0; x < after.Width; x++)
        {
            var o = (y * after.Width + x) * 4;
            if (before.Rgba.AsSpan(o, 4).SequenceEqual(after.Rgba.AsSpan(o, 4))) continue;
            changed++;
            // Gen09: centre tile of row four, and the second inline image below the gallery.
            var xobject = x >= 303 && x <= 491 && y >= 760 && y <= 878;
            var inline = x >= 178 && x <= 264 && y >= 938 && y <= 1024;
            Assert.True(xobject || inline, $"Unexpected changed pixel ({x},{y})");
            if (xobject) xobjectChanged++;
            if (inline) inlineChanged++;
        }
        Assert.True(changed > 1000);
        Assert.True(xobjectChanged > 1000);
        Assert.True(inlineChanged > 1000);
        output.WriteLine($"Changed pixels: {changed}; XObject: {xobjectChanged}; inline: {inlineChanged}");
    }
}
