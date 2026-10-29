using PortableDocument.Core;
using PortableDocument.Document;
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

    static byte[] FileSpecificationPdf()
    {
        const string pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(attachment) 5 0 R] >> >> /AF [5 0 R] /AcroForm << /AF [5 0 R] /Fields [7 0 R] >> /Collection << /View /D /Schema << /Title << /Subtype /S /N (Title) >> >> /Sort << /S /Title /A true >> >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /AF [5 0 R] /Annots [4 0 R] >>\nendobj\n" +
            "4 0 obj\n<< /Type /Annot /Subtype /FileAttachment /Rect [10 10 20 20] /AF [5 0 R] >>\nendobj\n" +
            "5 0 obj\n<< /Type /Filespec /F (fallback.txt) /UF (unicode.txt) /DOS (DOS.TXT) /Mac (Mac.txt) /Unix (unix.txt) /Desc (attachment description) /ID [(first-id) (second-id)] /V true /AFRelationship /Data /CI << /Title (A report) >> /EF << /F 6 0 R /UF 6 0 R >> >>\nendobj\n" +
            "6 0 obj\n<< /Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size 5 /CreationDate (D:20260101000000Z) /ModDate (D:20260102000000Z) /CheckSum <01020304> >> /Length 5 >>\nstream\nhello\nendstream\nendobj\n" +
            "7 0 obj\n<< /T (attachedField) /AF [5 0 R] /Kids [7 0 R] >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 8 >>\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(pdf);
    }

    [Fact]
    public void File_specifications_expose_metadata_and_associated_files_without_losing_legacy_api()
    {
        var doc = PdfDocument.Load(FileSpecificationPdf());
        var spec = Assert.Single(doc.GetFileSpecifications());

        Assert.Equal("attachment", spec.Name);
        Assert.Equal("fallback.txt", spec.FileName);
        Assert.Equal("unicode.txt", spec.UnicodeFileName);
        Assert.Equal("DOS.TXT", spec.DosFileName);
        Assert.Equal("Mac.txt", spec.MacFileName);
        Assert.Equal("unix.txt", spec.UnixFileName);
        Assert.Equal("attachment description", spec.Description);
        Assert.Equal(new[] { "first-id", "second-id" }, spec.Id);
        Assert.True(spec.IsVolatile);
        Assert.Equal("Data", spec.AFRelationship);
        Assert.Equal("A report", doc.File.Resolve(spec.CollectionItem?.Get("Title")) is PdfString title ? title.Text : null);

        var embedded = spec.EmbeddedFiles["UF"];
        Assert.Equal("text/plain", embedded.MimeType);
        Assert.Equal(5, embedded.Size);
        Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(embedded.Data));
        Assert.Equal("D:20260101000000Z", embedded.CreationDate);
        Assert.Equal("D:20260102000000Z", embedded.ModificationDate);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, embedded.Checksum);
        Assert.Same(spec.EmbeddedFiles["F"].Stream, embedded.Stream);
        Assert.NotNull(embedded.Parameters);
        Assert.NotNull(embedded.Stream);

        var associated = doc.GetAssociatedFiles();
        Assert.Equal(5, associated.Count);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.Catalog);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.AcroForm);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.FormField && file.FieldName == "attachedField");
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.Page && file.PageIndex == 0);
        Assert.Contains(associated, file => file.Scope == PdfAssociatedFileScope.Annotation && file.PageIndex == 0 && file.AnnotationIndex == 0);

        var collection = Assert.IsType<PdfDocumentCollection>(doc.GetCollection());
        Assert.Equal("D", collection.View);
        Assert.NotNull(collection.Schema?.Get("Title"));
        Assert.NotNull(collection.Sort?.Get("S"));
        Assert.Same(collection.Dictionary, doc.File.Resolve(doc.File.Catalog.Get("Collection")));

        var legacy = Assert.Single(doc.GetEmbeddedFiles());
        Assert.Equal("attachment", legacy.Name);
        Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(legacy.Data));
    }
}
