using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace AdoBye.Tests;

public class JpxTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jpx");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Dir, "*.pdf")
            .Where(p => !Path.GetFileName(p).StartsWith("poc-malformed-", StringComparison.Ordinal))
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) })
            .OrderBy(a => (string)a[0]);

    public static IEnumerable<object[]> MalformedPocFixtures() =>
        Directory.GetFiles(Dir, "poc-malformed-*.pdf")
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) })
            .OrderBy(a => (string)a[0]);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DecodesAgainstOpenJpegReference(string name)
    {
        var meta = File.ReadAllText(Path.Combine(Dir, name + ".meta")).Split(' ');
        string mode = meta[0];
        int w = int.Parse(meta[1]), h = int.Parse(meta[2]), tol = int.Parse(meta[3]);
        var expected = File.ReadAllBytes(Path.Combine(Dir, name + ".bin"));

        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var f = doc.File;
        var xo = (PdfDictionary)f.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)f.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(f, img, doc.Pages[0].Resources, out _, out var warn);
        Assert.NotNull(data);
        Assert.Null(warn);
        Assert.Equal(w, data!.Width);
        Assert.Equal(h, data.Height);

        var nc = mode.Length;
        int max = 0, bad = 0;
        for (var i = 0; i < w * h; i++)
            for (var k = 0; k < nc; k++)
            {
                var got = mode == "L" ? data.Rgba[i * 4] : data.Rgba[i * 4 + k];
                var diff = Math.Abs(got - expected[i * nc + k]);
                if (diff > tol) bad++;
                if (diff > max) max = diff;
            }
        Assert.True(bad == 0, $"{name}: {bad} samples differ by more than {tol} (max diff {max})");
    }

    [Theory]
    [MemberData(nameof(MalformedPocFixtures))]
    public void ReportsMalformedProgressionOrderChanges(string name)
    {
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var f = doc.File;
        var xo = (PdfDictionary)f.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)f.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(f, img, doc.Pages[0].Resources, out _, out var warn);

        Assert.Null(data);
        Assert.NotNull(warn);
        Assert.True(warn!.Contains("POC", StringComparison.OrdinalIgnoreCase));
    }
}
