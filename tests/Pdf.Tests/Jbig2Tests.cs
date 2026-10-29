using Pdf.Content;
using Pdf.Core;
using Pdf.Document;
using Xunit;

namespace Pdf.Tests;

public class Jbig2Tests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "jbig2");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "jbig2"), "*.pdf")
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) }).OrderBy(a => (string)a[0]);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void DecodesAgainstReferencePixels(string name)
    {
        var expected = File.ReadAllText(Path.Combine(Dir, name + ".txt"));
        var doc = PdfDocument.Load(File.ReadAllBytes(Path.Combine(Dir, name + ".pdf")));
        var f = doc.File;
        var xo = (PdfDictionary)f.Resolve(doc.Pages[0].Resources.Get("XObject"))!;
        var img = (PdfStream)f.Resolve(xo.Get("Im0"))!;
        var data = ImageDecoder.Decode(f, img, doc.Pages[0].Resources, out _, out var warn);
        Assert.NotNull(data);
        Assert.Null(warn);
        Assert.Equal(expected.Length, data!.Width * data.Height);
        var bad = 0;
        for (var i = 0; i < expected.Length; i++)
            if ((data.Rgba[i * 4] > 127) != (expected[i] == '1')) bad++;
        Assert.Equal(0, bad);
    }
}
