using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;
using Xunit;

namespace AdoBye.Tests;

/// <summary>The browser editor pins CultureInfo.InvariantCulture; PDF output must not depend on the thread culture at all.</summary>
public class CultureInvarianceTests
{
    static string Fx(params string[] p) => Path.Combine([AppContext.BaseDirectory, "fixtures", .. p]);

    static T Under<T>(string culture, Func<T> action)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return action();
        }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous; }
    }

    // Writer timestamps and ids are the only legitimately varying bytes.
    static string Stable(byte[] pdf) => Regex.Replace(Encoding.Latin1.GetString(pdf), @"/(ModDate|CreationDate)\s*\([^)]*\)|/ID\s*\[[^\]]*\]|D:\d{14}[^)>/\s]*", "");

    static string TextEdit()
    {
        var source = TestPdf.Build("q 1.2 0 0.18 0.85 12 20 cm BT /F1 18 Tf 73 Tz 0.4 Tc 1.5 Tw 30 90 Td (Original text) Tj ET Q",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var session = new EditorSession(new FontCatalog());
        session.Handle("POST", "/api/open", new Dictionary<string, string>(), source, "culture.pdf");
        var before = Pdf.ExtractText(PdfDocument.Load(source).Pages[0]).Single();
        using var response = JsonDocument.Parse(session.Handle("GET", "/api/page/0/text", new Dictionary<string, string>(), null).Data);
        var text = response.RootElement.EnumerateArray().Single();
        var item = new Item("original", "text", text.GetProperty("x").GetDouble(), text.GetProperty("y").GetDouble() - .95 * before.Size,
            before.Width, 0, before.Text, "Helvetica", before.Size, false, false, "#000000", "", "", 0, 1, null, 0, 0,
            before.Index, Spacing: text.GetProperty("spacing").GetDouble(), OStyle: "Helvetica|false|false");
        var inserted = item with { Id = "inserted", Orig = null, Text = "Inserted 1.5", X = 40.25, Y = 140.75, Size = 11.5 };
        session.Handle("PUT", "/api/page/0/items", new Dictionary<string, string>(), JsonSerializer.SerializeToUtf8Bytes(new[] { item, inserted }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return Stable(session.Handle("GET", "/api/export/pdf", new Dictionary<string, string>(), null).Data);
    }

    static string FieldEdit()
    {
        var editor = PdfEditor.Open(PdfDocument.Open(Fx("features", "features.pdf")));
        Assert.True(editor.SetFieldValue("firstName", "Grace 1,5 \u0130stanbul"));
        return Stable(editor.Save());
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void EditedPageContentIsIdenticalUnderAnyThreadCulture(string culture) =>
        Assert.Equal(Under("", TextEdit), Under(culture, TextEdit));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void FormAppearancesAreIdenticalUnderAnyThreadCulture(string culture) =>
        Assert.Equal(Under("", FieldEdit), Under(culture, FieldEdit));

    [Theory]
    [InlineData("de-DE", "10-annotations.pdf")]
    [InlineData("tr-TR", "10-annotations.pdf")]
    [InlineData("de-DE", "11-acroform.pdf")]
    [InlineData("de-DE", "06-patterns-shadings.pdf")]
    [InlineData("tr-TR", "20-report.pdf")]
    public void RenderedPagesAreIdenticalUnderAnyThreadCulture(string culture, string sample)
    {
        var path = Path.Combine(SamplesTests.SamplesDir()!, sample);
        string Render()
        {
            var document = PdfDocument.Open(path);
            return string.Join("|", document.Pages.Take(2).Select(page => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pdf.RenderPage(page, 72).ToPng()))));
        }
        Assert.Equal(Under("", Render), Under(culture, Render));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void OutlineAndFieldOrderAndNamesAreCultureIndependent(string culture)
    {
        string Names() => string.Join("|", PdfDocument.Open(Fx("features", "features.pdf")).GetFormFields().Select(f => f.FullName))
            + "#" + string.Join("|", PdfDocument.Open(Fx("features", "features.pdf")).GetOutlines().Select(o => o.Title));
        Assert.Equal(Under("", Names), Under(culture, Names));
    }
}
