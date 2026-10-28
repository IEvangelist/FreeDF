using Pdf.Core;
using Pdf.Document;
using Pdf.Render;
using Xunit;

namespace Pdf.Tests;

public class EncryptionTests
{
    static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "encrypted");

    public static IEnumerable<object[]> Empty() =>
        Directory.GetFiles(Dir, "*-empty.pdf").Concat(Directory.GetFiles(Dir, "*objstm.pdf")).Select(f => new object[] { Path.GetFileName(f) });

    public static IEnumerable<object[]> WithPw() =>
        Directory.GetFiles(Dir, "*-pw.pdf").Select(f => new object[] { Path.GetFileName(f) });

    [Theory, MemberData(nameof(Empty))]
    public void OpensWithEmptyUserPassword(string file)
    {
        var doc = PdfDocument.Open(Path.Combine(Dir, file));
        Assert.True(doc.File.IsEncrypted);
        Assert.Equal("Secret Title", doc.Info("Title"));
        var p = PdfRenderer.RenderPage(doc.Pages[0], 72);
        Assert.Equal((255, 0, 0), (p.Rgba[(150 * 200 + 50) * 4], p.Rgba[(150 * 200 + 50) * 4 + 1], p.Rgba[(150 * 200 + 50) * 4 + 2]));
    }

    [Theory, MemberData(nameof(WithPw))]
    public void RequiresPassword(string file)
    {
        var path = Path.Combine(Dir, file);
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(path));
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(path, "wrong"));
        Assert.Equal("Secret Title", PdfDocument.Open(path, "secret").Info("Title"));
        Assert.Equal("Secret Title", PdfDocument.Open(path, "owner").Info("Title"));
    }
}
