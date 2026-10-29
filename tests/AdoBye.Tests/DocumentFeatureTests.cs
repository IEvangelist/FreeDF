using AdoBye.Document;
using Xunit;

namespace AdoBye.Tests;

public class DocumentFeatureTests
{
    static PdfDocument Load() => PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "features", "features.pdf"));

    [Fact]
    public void Outlines_resolve_to_pages()
    {
        var o = Load().GetOutlines();
        Assert.Equal(3, o.Count);
        Assert.Equal("Chapter 2", o[1].Title);
        Assert.Equal(1, o[1].PageIndex);
    }

    [Fact]
    public void Link_annotation_has_uri()
    {
        var a = Load().Pages[0].GetAnnotations();
        Assert.Contains(a, x => x.Subtype == "Link" && x.Uri == "https://example.com");
    }

    [Fact]
    public void Form_fields_are_listed()
    {
        var f = Load().GetFormFields();
        Assert.Contains(f, x => x.FullName == "firstName" && x.FieldType == "Tx" && x.Value == "Ada");
        Assert.Contains(f, x => x.FullName == "agree" && x.FieldType == "Btn");
    }

    [Fact]
    public void Page_labels_follow_ranges()
    {
        var d = Load();
        Assert.Equal("i", d.GetPageLabel(0));
        Assert.Equal("A-5", d.GetPageLabel(1));
        Assert.Equal("A-6", d.GetPageLabel(2));
    }

    [Fact]
    public void Xmp_and_attachments()
    {
        var d = Load();
        Assert.Contains("xmpmeta", d.GetXmpMetadata());
        var e = Assert.Single(d.GetEmbeddedFiles());
        Assert.Equal("hello.txt", e.Name);
        Assert.Equal("hello attachment", System.Text.Encoding.ASCII.GetString(e.Data));
    }
}
