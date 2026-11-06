using System.Text;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace FreeDF.Tests;

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
        var p = DocumentRenderer.RenderPage(doc.Pages[0], 72);
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

    [Theory]
    [InlineData("", true, false)]
    [InlineData("/P null", true, false)]
    [InlineData("/P -3392", false, false)]
    [InlineData("/P -3904", true, true)]
    public void Ua1RequiresPresentAccessibilityPermissions(string replacement, bool ua1Error, bool ua2Error)
    {
        var encrypted = PdfEditor.Open(TestPdf.Build("")).Save(options: new PdfWriteOptions
        {
            Encryption = new PdfEncryption { Permissions = -3392 },
        });
        var text = Encoding.Latin1.GetString(encrypted);
        const string original = "/P -3392";
        Assert.Equal(1, text.Split(original).Length - 1);
        var bytes = Encoding.Latin1.GetBytes(text.Replace(original, replacement.PadRight(original.Length)));
        var doc = PdfDocument.Load(bytes);
        Assert.True(doc.File.IsEncrypted);
        Assert.Single(doc.Pages);
        foreach (var (profile, expected) in new[]
        {
            (PdfValidationProfile.PdfUA1, ua1Error),
            (PdfValidationProfile.PdfUA2, ua2Error),
        })
        {
            var report = PdfValidator.Validate(doc, profile);
            Assert.Equal(expected, report.Has("PDFUA-ENCRYPT-ACCESS"));
            Assert.DoesNotContain(report.Findings, f => f.RuleId == "VALIDATION-INTERNAL");
        }
    }

    [Fact]
    public void UnencryptedDocumentsDoNotRequireAccessibilityPermissions()
    {
        var doc = PdfDocument.Load(TestPdf.Build(""));
        Assert.False(doc.File.IsEncrypted);
        Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1).Has("PDFUA-ENCRYPT-ACCESS"));
    }
}
